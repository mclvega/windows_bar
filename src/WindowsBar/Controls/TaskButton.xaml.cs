using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using ManagedShell.Interop;
using ManagedShell.WindowsTasks;
using WindowsBar.Services;
using WindowsBar.Views;

namespace WindowsBar.Controls
{
    /// <summary>Botón de una app en la barra (DataContext = <see cref="TaskGroup"/>).</summary>
    public partial class TaskButton : UserControl
    {
        private TaskGroup _group;
        private bool _wasActiveOnPress;
        private readonly HashSet<ApplicationWindow> _rectHandlers = new();

        public TaskButton()
        {
            InitializeComponent();
        }

        private TaskbarWindow Host => Window.GetWindow(this) as TaskbarWindow;

        private void TaskButton_Loaded(object sender, RoutedEventArgs e)
        {
            _group = DataContext as TaskGroup;
            if (_group == null) return;
            _group.Windows.CollectionChanged += Windows_CollectionChanged;
            SyncButtonRectHandlers();
        }

        private void TaskButton_Unloaded(object sender, RoutedEventArgs e)
        {
            if (_group != null) _group.Windows.CollectionChanged -= Windows_CollectionChanged;
            foreach (var w in _rectHandlers) w.GetButtonRect -= Window_GetButtonRect;
            _rectHandlers.Clear();
            _group = null;
        }

        private void Windows_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e) => SyncButtonRectHandlers();

        /// <summary>
        /// Windows pregunta dónde está el botón de cada ventana para animar el minimizado hacia él.
        /// </summary>
        private void SyncButtonRectHandlers()
        {
            var current = _group?.Windows.ToList() ?? new List<ApplicationWindow>();
            foreach (var w in current.Where(w => _rectHandlers.Add(w)))
                w.GetButtonRect += Window_GetButtonRect;
            foreach (var gone in _rectHandlers.Where(w => !current.Contains(w)).ToList())
            {
                gone.GetButtonRect -= Window_GetButtonRect;
                _rectHandlers.Remove(gone);
            }
        }

        private void Window_GetButtonRect(ref NativeMethods.ShortRect rect)
        {
            var host = Host;
            if (host == null || !IsVisible) return;

            // Con varias barras mostrando la misma ventana, responde la del monitor de la ventana.
            var window = _group?.Windows.FirstOrDefault();
            if (window != null && window.HMonitor != host.Screen.HMonitor && !host.Screen.Primary) return;

            Point topLeft = PointToScreen(new Point(0, 0));
            Point bottomRight = PointToScreen(new Point(ActualWidth, ActualHeight));
            rect.Left = (short)topLeft.X;
            rect.Top = (short)topLeft.Y;
            rect.Right = (short)bottomRight.X;
            rect.Bottom = (short)bottomRight.Y;
        }

        #region Clics
        private void AppButton_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // Se guarda antes de que el clic cambie el foco.
            _wasActiveOnPress = _group?.IsActive == true;
        }

        private void AppButton_Click(object sender, RoutedEventArgs e)
        {
            if (_group == null) return;

            if (!_group.IsRunning || (Keyboard.Modifiers & ModifierKeys.Shift) != 0)
            {
                Launch();
                return;
            }

            var windows = _group.Windows.ToList();

            if (windows.Count == 1)
            {
                var w = windows[0];
                if (_wasActiveOnPress && w.CanMinimize) w.Minimize();
                else w.BringToFront();
                return;
            }

            // Ctrl+clic: ir rotando entre las ventanas de la app.
            if ((Keyboard.Modifiers & ModifierKeys.Control) != 0)
            {
                int active = windows.FindIndex(w => w.State == ApplicationWindow.WindowState.Active);
                windows[(active + 1) % windows.Count].BringToFront();
                return;
            }

            ShowWindowPicker(windows);
        }

        private void AppButton_MouseUp(object sender, MouseButtonEventArgs e)
        {
            // Clic central: nueva instancia, como en Windows.
            if (e.ChangedButton == MouseButton.Middle)
            {
                e.Handled = true;
                Launch();
            }
        }

        /// <summary>Lista de ventanas de la app cuando tiene varias abiertas.</summary>
        private void ShowWindowPicker(List<ApplicationWindow> windows)
        {
            var picker = new ContextMenu
            {
                PlacementTarget = AppButton,
                Placement = PlacementMode.Top
            };

            foreach (var w in windows)
            {
                var item = new MenuItem
                {
                    Header = string.IsNullOrWhiteSpace(w.Title) ? _group.Name : w.Title,
                    Icon = new Image { Source = w.Icon, Width = 16, Height = 16 },
                    FontWeight = w.State == ApplicationWindow.WindowState.Active ? FontWeights.SemiBold : FontWeights.Normal
                };
                item.Click += (_, _) => w.BringToFront();
                picker.Items.Add(item);
            }

            picker.IsOpen = true;
        }

        private void Launch()
        {
            string path = _group?.LaunchPath;
            if (string.IsNullOrEmpty(path)) return;

            if (path.StartsWith("shell:", StringComparison.OrdinalIgnoreCase))
                ShellCommands.Open("explorer.exe", path);
            else
                ShellCommands.Open(path);
        }
        #endregion

        #region Menú contextual
        private void AppButton_ContextMenuOpening(object sender, ContextMenuEventArgs e)
        {
            if (_group == null)
            {
                e.Handled = true;
                return;
            }

            Menu.Items.Clear();
            Menu.Placement = PlacementMode.Top;

            var open = new MenuItem
            {
                Header = _group.Name,
                Icon = new Image { Source = _group.Icon, Width = 16, Height = 16 }
            };
            open.Click += (_, _) => Launch();
            Menu.Items.Add(open);

            var pins = App.Services.Pins;
            if (_group.IsPinned)
            {
                var unpin = new MenuItem { Header = "Desanclar de la barra de tareas", Icon = Glyph("") };
                unpin.Click += (_, _) => pins.Unpin(_group.Pin);
                Menu.Items.Add(unpin);
            }
            else if (_group.IsRunning)
            {
                var pin = new MenuItem { Header = "Anclar a la barra de tareas", Icon = Glyph("") };
                var window = _group.Windows[0];
                pin.Click += (_, _) => pins.Pin(window);
                Menu.Items.Add(pin);
            }

            if (_group.IsPinned)
            {
                int index = pins.Apps.ToList().IndexOf(_group.Pin);
                if (index > 0)
                {
                    var left = new MenuItem { Header = "Mover a la izquierda", Icon = Glyph("") };
                    left.Click += (_, _) => pins.Move(_group.Pin, index - 1);
                    Menu.Items.Add(left);
                }
                if (index >= 0 && index < pins.Apps.Count - 1)
                {
                    var right = new MenuItem { Header = "Mover a la derecha", Icon = Glyph("") };
                    right.Click += (_, _) => pins.Move(_group.Pin, index + 1);
                    Menu.Items.Add(right);
                }
            }

            if (_group.IsRunning)
            {
                Menu.Items.Add(new Separator());
                var windows = _group.Windows.ToList();
                var close = new MenuItem
                {
                    Header = windows.Count == 1 ? "Cerrar ventana" : "Cerrar todas las ventanas",
                    Icon = Glyph("")
                };
                close.Click += (_, _) => windows.ForEach(w => w.Close());
                Menu.Items.Add(close);
            }
        }

        private static TextBlock Glyph(string glyph) => new()
        {
            Text = glyph,
            FontSize = 14,
            FontFamily = (FontFamily)Application.Current.FindResource("IconFont"),
            Foreground = (Brush)Application.Current.FindResource("Foreground"),
            VerticalAlignment = VerticalAlignment.Center
        };
        #endregion
    }
}
