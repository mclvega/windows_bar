using System;
using System.Windows;
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
        private readonly AppServices _services;

        public TaskGroupCollection TaskGroups { get; }

        public TaskbarWindow(AppServices services, AppBarScreen screen)
            : base(services.ShellManager.AppBarManager, services.ShellManager.ExplorerHelper,
                   services.ShellManager.FullScreenHelper, screen, AppBarEdge.Bottom, AppBarMode.Normal,
                   (double)Application.Current.FindResource("TaskbarHeight"))
        {
            _services = services;
            InitializeComponent();

            TaskGroups = new TaskGroupCollection(services.ShellManager.Tasks, services.Pins, services.Settings,
                ShowsWindowOnThisMonitor, Dispatcher);
            TaskList.Host = this;
            TaskList.ItemsSource = TaskGroups.Items;

            if (Screen.Primary && services.ShellManager.NotificationArea != null)
            {
                services.ShellManager.NotificationArea.NotificationBalloonShown += NotificationArea_BalloonShown;
            }
        }

        public AppServices Services => _services;

        /// <summary>Indica si la ventana pertenece al monitor de esta barra.</summary>
        private bool ShowsWindowOnThisMonitor(ApplicationWindow window)
        {
            IntPtr hMonitor = window.HMonitor;
            if (hMonitor == Screen.HMonitor) return true;

            // Ventanas en monitores desconocidos (recién desconectados...) van a la barra principal.
            return Screen.Primary && !_services.WindowManager.IsKnownMonitor(hMonitor);
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
            AllTaskbarsMenuItem.IsChecked = _services.Settings.Data.ShowTasksOnAllTaskbars;
        }

        private void TaskManager_Click(object sender, RoutedEventArgs e) => ShellCommands.TaskManager();

        private void TaskbarSettings_Click(object sender, RoutedEventArgs e) => ShellCommands.TaskbarSettings();

        private void AllTaskbars_Click(object sender, RoutedEventArgs e)
        {
            _services.Settings.Data.ShowTasksOnAllTaskbars = AllTaskbarsMenuItem.IsChecked;
            _services.Settings.SaveAndNotify();
        }

        private void Exit_Click(object sender, RoutedEventArgs e) => ((App)Application.Current).ExitGracefully();
        #endregion
    }
}
