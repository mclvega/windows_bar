using System;
using System.Windows;
using System.Windows.Threading;
using ManagedShell.AppBar;
using ManagedShell.Common.Helpers;
using ManagedShell.Interop;
using ManagedShell.WindowsTasks;
using ManagedShell.WindowsTray;
using WindowsBar.Services;

namespace WindowsBar.Views
{
    /// <summary>Barra de tareas de un monitor.</summary>
    public partial class TaskbarWindow : AppBarWindow
    {
        private static readonly int TaskbarCreatedMessage = (int)NativeMethods.RegisterWindowMessage("TaskbarCreated");

        private readonly AppServices _services;

        public TaskGroupCollection TaskGroups { get; }

        public TaskbarWindow(AppServices services, AppBarScreen screen, AppBarMode mode)
            : base(services.ShellManager.AppBarManager, services.ShellManager.ExplorerHelper,
                   services.ShellManager.FullScreenHelper, screen, AppBarEdge.Bottom, mode,
                   (double)Application.Current.FindResource("TaskbarHeight"))
        {
            _services = services;
            InitializeComponent();

            // Ocultar automáticamente necesita ventana transparente (solo se puede fijar antes de mostrarla).
            AllowsTransparency = mode == AppBarMode.AutoHide;
            AutoHideElement = RootBorder;

            TaskGroups = new TaskGroupCollection(services.ShellManager.Tasks, services.Pins, services.Settings,
                ShowsWindowOnThisMonitor, Dispatcher);
            TaskList.Host = this;
            TaskList.ItemsSource = TaskGroups.Items;

            ApplySettings();
            services.Settings.Changed += Settings_Changed;

            if (mode == AppBarMode.AutoHide)
            {
                _edgeWatcher = new DispatcherTimer(TimeSpan.FromMilliseconds(100), DispatcherPriority.Input, EdgeWatcher_Tick, Dispatcher);
            }

            if (Screen.Primary && services.ShellManager.NotificationArea != null)
            {
                services.ShellManager.NotificationArea.NotificationBalloonShown += NotificationArea_BalloonShown;
            }
        }

        public AppServices Services => _services;

        #region Ocultar automáticamente
        private readonly DispatcherTimer _edgeWatcher;
        private bool _isAutoHidden;

        /// <summary>
        /// Respaldo para mostrar la barra: si el ratón está en el borde inferior de este monitor y la barra
        /// está oculta, la subimos encima de todo y la mostramos (aunque otra ventana tape la franja de 2 px).
        /// </summary>
        private void EdgeWatcher_Tick(object sender, EventArgs e)
        {
            if (!_isAutoHidden || !Topmost || Handle == IntPtr.Zero) return;

            var cursor = System.Windows.Forms.Cursor.Position;
            var bounds = Screen.Bounds;
            bool atBottomEdge = cursor.X >= bounds.Left && cursor.X < bounds.Right
                                && cursor.Y >= bounds.Bottom - 2 && cursor.Y < bounds.Bottom;
            if (!atBottomEdge) return;

            WindowHelper.ShowWindowTopMost(Handle);
            PeekDuringAutoHide(1500);
        }

        private int _openPopups;

        /// <summary>
        /// Un panel de la barra (p. ej. las vistas previas) se abrió: mientras haya alguno abierto la barra
        /// no se oculta. ManagedShell solo conoce el menú contextual de la propia barra, no estos paneles.
        /// </summary>
        public void PopupOpened()
        {
            _openPopups++;
            OnPropertyChanged(nameof(AllowAutoHide));
        }

        public void PopupClosed()
        {
            if (_openPopups > 0) _openPopups--;
            OnPropertyChanged(nameof(AllowAutoHide));
        }

        protected override bool ShouldAllowAutoHide() => _openPopups == 0 && base.ShouldAllowAutoHide();

        protected override void OnAutoHideAnimationComplete(bool isHiding)
        {
            base.OnAutoHideAnimationComplete(isHiding);
            _isAutoHidden = isHiding;
        }

        /// <summary>
        /// Una ventana MAXIMIZADA no es pantalla completa: sin espacio reservado (ocultar automáticamente),
        /// VS Code o Chrome maximizados ocupan exactamente la pantalla y ManagedShell los confundía con
        /// un juego o un vídeo, mandando la barra al fondo y dejándola sin poder aparecer.
        /// </summary>
        protected override void OnFullScreenEnter(FullScreenApp app)
        {
            if (Interop.NativeMethods.IsZoomed(app.hWnd))
            {
                DiagLog.Write($"[Auto-ocultar] {Screen.DeviceName}: '{app.title}' está maximizada, no es pantalla completa → la barra sigue disponible");
                return;
            }

            DiagLog.Write($"[Auto-ocultar] {Screen.DeviceName}: pantalla completa '{app.title}' → la barra cede");
            base.OnFullScreenEnter(app);
        }
        #endregion

        /// <summary>Indica si la ventana pertenece al monitor de esta barra.</summary>
        private bool ShowsWindowOnThisMonitor(ApplicationWindow window)
        {
            IntPtr hMonitor = window.HMonitor;
            if (hMonitor == Screen.HMonitor) return true;

            // Ventanas en monitores sin barra (desconectados o sin barra) van a la barra principal.
            return Screen.Primary && !_services.WindowManager.HasTaskbarOn(hMonitor);
        }

        private void Settings_Changed(object sender, EventArgs e) => ApplySettings();

        /// <summary>Aplica los ajustes de "Elementos de la barra" y la alineación.</summary>
        private void ApplySettings()
        {
            var s = _services.Settings.Data;
            WidgetsButton.Visibility = s.ShowWidgets ? Visibility.Visible : Visibility.Collapsed;
            SearchButton.Visibility = s.ShowSearch ? Visibility.Visible : Visibility.Collapsed;
            TaskViewButton.Visibility = s.ShowTaskView ? Visibility.Visible : Visibility.Collapsed;
            ShowDesktopButton.Visibility = s.ShowDesktopButton ? Visibility.Visible : Visibility.Collapsed;
            Layout.CenterMiddle = s.CenterTasks;
        }

        /// <summary>
        /// Informa a las apps de dónde está "la bandeja" (Shell_NotifyIconGetRect) para que
        /// sus menús y ventanas emergentes aparezcan junto a esta barra.
        /// </summary>
        public void SetTrayHost()
        {
            _services.ShellManager.NotificationArea?.SetTrayHostSizeData(new TrayHostSizeData
            {
                edge = (NativeMethods.ABEdge)AppBarEdge,
                rc = WindowRect
            });
        }

        #region AppBarWindow
        protected override void OnSourceInitialized(object sender, EventArgs e)
        {
            base.OnSourceInitialized(sender, e);
            UseLayoutRounding = DpiScale % 1 == 0;
            if (Screen.Primary) SetTrayHost();
        }

        protected override IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            base.WndProc(hwnd, msg, wParam, lParam, ref handled);

            if (msg == (int)NativeMethods.WM.SETTINGCHANGE && wParam == (IntPtr)NativeMethods.SPI.SETWORKAREA)
            {
                _services.WindowManager.NotifyDisplayChange();
            }
            else if (msg == TaskbarCreatedMessage && Screen.Primary)
            {
                // Explorer se reinició: vuelve a mostrar su barra; la ocultamos y reabrimos las nuestras.
                Dispatcher.BeginInvoke(new Action(_services.WindowManager.HandleExplorerRestart));
            }

            return IntPtr.Zero;
        }

        protected override void SetScreenProperties(ScreenSetupReason reason)
        {
            if (reason == ScreenSetupReason.DpiChange)
            {
                UpdatePosition();
                UseLayoutRounding = DpiScale % 1 == 0;
                return;
            }

            _services.WindowManager.NotifyDisplayChange();
        }

        protected override void CustomClosing()
        {
            if (!AllowClose) return;

            TaskGroups.Dispose();
            _edgeWatcher?.Stop();
            _services.Settings.Changed -= Settings_Changed;
            if (_services.ShellManager.NotificationArea != null)
            {
                _services.ShellManager.NotificationArea.NotificationBalloonShown -= NotificationArea_BalloonShown;
            }
        }
        #endregion

        private void NotificationArea_BalloonShown(object sender, NotificationBalloonEventArgs e)
        {
            e.Handled = TrayArea.ShowBalloon(e.Balloon);
        }

        #region Menú contextual de la barra
        private void BarContextMenu_Opened(object sender, RoutedEventArgs e)
        {
            DragReorderMenuItem.IsChecked = _services.Settings.Data.DragToReorder;
        }

        private void TaskManager_Click(object sender, RoutedEventArgs e) => ShellCommands.TaskManager();

        private void WindowsBarSettings_Click(object sender, RoutedEventArgs e) => SettingsWindow.Open(this);

        private void DragReorder_Click(object sender, RoutedEventArgs e)
        {
            _services.Settings.Data.DragToReorder = DragReorderMenuItem.IsChecked;
            _services.Settings.SaveAndNotify();
        }
        #endregion
    }
}
