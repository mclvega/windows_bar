using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics;
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

            // Solo cuentan ventanas que siguen existiendo y se pueden mostrar: al cerrar una app a veces
            // queda registrada una ventana fantasma y la barra intentaba "traerla al frente" en vez de abrir la app.
            var windows = _group.Windows.Where(IsRealWindow).ToList();
            DiagLog.Write($"[Clic] '{_group.Name}' ventanas={_group.Windows.Count} reales={windows.Count} anclada={_group.IsPinned}");

            if (windows.Count == 0 || (Keyboard.Modifiers & ModifierKeys.Shift) != 0)
            {
                Preview?.Close();
                Launch();
                return;
            }

            if (windows.Count == 1)
            {
                Preview?.Close();
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

            // Varias ventanas: miniaturas para elegir cuál traer al frente.
            Preview?.ShowNow(AppButton, _group);
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

        #region Vistas previas
        private TaskPreview Preview => Host?.TaskPreview;

        private void AppButton_MouseEnter(object sender, MouseEventArgs e)
        {
            if (_group?.IsRunning == true) Preview?.HoverEnter(AppButton, _group);
        }

        private void AppButton_MouseLeave(object sender, MouseEventArgs e) => Preview?.HoverLeave();
        #endregion

        /// <summary>Ventana que sigue existiendo y se puede mostrar (descarta ventanas fantasma ya cerradas).</summary>
        internal static bool IsRealWindow(ApplicationWindow w) =>
            Interop.NativeMethods.IsWindow(w.Handle)
            && (Interop.NativeMethods.IsWindowVisible(w.Handle) || Interop.NativeMethods.IsIconic(w.Handle));

        private void Launch()
        {
            string path = _group?.LaunchPath;
            if (string.IsNullOrEmpty(path))
            {
                DiagLog.Write($"[Abrir] '{_group?.Name}' sin ruta para abrir");
                return;
            }

            DiagLog.Write($"[Abrir] '{_group.Name}' → {path} (monitor {Host?.Screen.DeviceName})");
            // La app se abre en el monitor de esta barra; si aparece en otro, LaunchPlacer la trae.
            App.Services.LaunchPlacer.Expect(_group, Host);
            ShellCommands.Launch(path, Host);
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

            Preview?.Close();
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

            // Cambiar el orden solo está permitido con la opción de arrastrar activada.
            if (_group.IsPinned && App.Services.Settings.Data.DragToReorder)
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

                var processIds = KillableProcessIds();
                var endTask = new MenuItem
                {
                    Header = "Finalizar tarea",
                    Icon = Glyph(""),
                    IsEnabled = processIds.Count > 0
                };
                endTask.Click += (_, _) => EndTask(processIds);
                Menu.Items.Add(endTask);
            }
        }

        // Procesos que nunca se finalizan desde la barra: el shell (todas las carpetas y el escritorio)
        // y el anfitrión de apps UWP (aloja varias apps a la vez).
        private static readonly HashSet<string> ProtectedProcesses = new(StringComparer.OrdinalIgnoreCase)
        {
            "explorer", "ApplicationFrameHost", "WindowsBar"
        };

        /// <summary>Procesos distintos de las ventanas reales del grupo que se pueden finalizar.</summary>
        private List<int> KillableProcessIds()
        {
            var ids = new List<int>();
            foreach (var w in _group.Windows.Where(IsRealWindow))
            {
                uint? pid = null;
                try { pid = w.ProcId; } catch { }
                if (pid is null or 0 || ids.Contains((int)pid.Value)) continue;

                try
                {
                    using var p = Process.GetProcessById((int)pid.Value);
                    if (!ProtectedProcesses.Contains(p.ProcessName)) ids.Add(p.Id);
                }
                catch
                {
                    // El proceso ya terminó.
                }
            }
            return ids;
        }

        /// <summary>Finaliza los procesos a la fuerza, como "Finalizar tarea" de Windows.</summary>
        private void EndTask(List<int> processIds)
        {
            string appName = _group?.Name ?? "la aplicación";
            foreach (int id in processIds)
            {
                try
                {
                    using var p = Process.GetProcessById(id);
                    string name = p.ProcessName;
                    p.Kill();
                    DiagLog.Write($"[Finalizar tarea] '{appName}' → proceso {name} ({id}) finalizado");
                }
                catch (ArgumentException)
                {
                    // Ya no existe: nada que hacer.
                }
                catch (Exception ex)
                {
                    DiagLog.Write($"[Finalizar tarea] '{appName}' → ERROR con el proceso {id}: {ex.Message}");
                    MessageBox.Show($"No se pudo finalizar {appName}.\n\nPuede que se esté ejecutando como administrador.",
                        "Finalizar tarea", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
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
