using System;
using System.Collections.Generic;
using System.Reflection;
using System.Windows;
using System.Windows.Interop;
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
                _hideWatchdog = new DispatcherTimer(TimeSpan.FromMilliseconds(500), DispatcherPriority.Background, HideWatchdog_Tick, Dispatcher);
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

        private DispatcherTimer _trayMenuTimer;
        private uint _trayMenuProcessId;
        private DateTime _trayMenuStart;

        /// <summary>
        /// Se hizo clic en un icono de bandeja: su app abre su propio menú o panel (otra ventana, de otro
        /// proceso), y para ManagedShell eso es "el ratón salió de la barra". Como en Windows, la barra
        /// sigue visible mientras ese menú esté abierto.
        /// </summary>
        public void HoldForTrayMenu(IntPtr iconHwnd)
        {
            if (AppBarMode != AppBarMode.AutoHide) return;

            Interop.NativeMethods.GetWindowThreadProcessId(iconHwnd, out _trayMenuProcessId);
            _trayMenuStart = DateTime.Now;

            if (_trayMenuTimer != null) return; // ya retenida: solo se reinicia el plazo

            PopupOpened();
            DiagLog.Write($"[Bandeja] {Screen.DeviceName}: clic en icono (pid {_trayMenuProcessId}) → la barra se mantiene mientras su menú esté abierto");
            _trayMenuTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(200), DispatcherPriority.Background, TrayMenuTimer_Tick, Dispatcher);
        }

        private void TrayMenuTimer_Tick(object sender, EventArgs e)
        {
            var elapsed = DateTime.Now - _trayMenuStart;
            if (elapsed < TimeSpan.FromMilliseconds(800)) return; // margen para que el menú llegue a aparecer

            if (elapsed < TimeSpan.FromMinutes(2) && IsTrayMenuOpen()) return;

            ReleaseTrayMenuHold();
            DiagLog.Write($"[Bandeja] {Screen.DeviceName}: menú del icono cerrado → la barra puede ocultarse");
        }

        private void ReleaseTrayMenuHold()
        {
            if (_trayMenuTimer == null) return;
            _trayMenuTimer.Stop();
            _trayMenuTimer = null;
            PopupClosed();
        }

        /// <summary>Hay un menú Win32 visible, o la app del icono tiene en primer plano una ventana emergente.</summary>
        private bool IsTrayMenuOpen()
        {
            for (IntPtr menu = Interop.NativeMethods.FindWindowEx(IntPtr.Zero, IntPtr.Zero, Interop.NativeMethods.MenuWindowClass, null);
                 menu != IntPtr.Zero;
                 menu = Interop.NativeMethods.FindWindowEx(IntPtr.Zero, menu, Interop.NativeMethods.MenuWindowClass, null))
            {
                if (Interop.NativeMethods.IsWindowVisible(menu)) return true;
            }

            if (_trayMenuProcessId == 0) return false;

            IntPtr foreground = Interop.NativeMethods.GetForegroundWindow();
            if (foreground == IntPtr.Zero) return false;
            Interop.NativeMethods.GetWindowThreadProcessId(foreground, out uint pid);
            if (pid != _trayMenuProcessId) return false;

            // Solo menús/paneles (ventanas de herramienta o siempre encima), no la ventana normal de la app.
            long exStyle = Interop.NativeMethods.GetWindowLongPtr(foreground, Interop.NativeMethods.GWL_EXSTYLE).ToInt64();
            return (exStyle & (Interop.NativeMethods.WS_EX_TOOLWINDOW | Interop.NativeMethods.WS_EX_TOPMOST)) != 0;
        }

        protected override void OnAutoHideAnimationComplete(bool isHiding)
        {
            base.OnAutoHideAnimationComplete(isHiding);
            _isAutoHidden = isHiding;
            if (isHiding) ResetBlockedState();
        }

        #region Vigilante: la barra no debe quedarse visible sin motivo
        // Estado privado de ManagedShell 0.0.374 (AppBarWindow). Solo cambia con eventos (MouseLeave,
        // ContextMenuClosing, DragLeave...): si uno se pierde, la barra se queda visible para siempre.
        private static readonly FieldInfo MouseWithinField = typeof(AppBarWindow).GetField("_isMouseWithin", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo ContextMenuOpenField = typeof(AppBarWindow).GetField("_isContextMenuOpen", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo DragWithinField = typeof(AppBarWindow).GetField("_isDragWithin", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo PeekTimerField = typeof(AppBarWindow).GetField("_peekAutoHideTimer", BindingFlags.Instance | BindingFlags.NonPublic);
        private static bool _fieldsMissingLogged;

        /// <summary>Tras este tiempo bloqueada sin motivo se muestra el marco.</summary>
        private static readonly TimeSpan BlockedFrameDelay = TimeSpan.FromSeconds(1);
        /// <summary>Tras este tiempo se liberan los bloqueos obsoletos y la barra se oculta.</summary>
        private static readonly TimeSpan BlockedReleaseDelay = TimeSpan.FromSeconds(4);

        private readonly DispatcherTimer _hideWatchdog;
        private DateTime? _blockedSince;
        private bool _blockedFrameShown;

        private void HideWatchdog_Tick(object sender, EventArgs e)
        {
            if (Handle == IntPtr.Zero || _isAutoHidden || AppBarMode != AppBarMode.AutoHide || AllowAutoHide)
            {
                ResetBlockedState();
                return;
            }

            if (MouseWithinField == null || ContextMenuOpenField == null || DragWithinField == null || PeekTimerField == null)
            {
                if (!_fieldsMissingLogged)
                {
                    _fieldsMissingLogged = true;
                    DiagLog.Write("[Auto-ocultar] vigilante desactivado: la versión de ManagedShell no tiene los campos esperados");
                }
                return;
            }

            // Bloqueos justificados: ratón sobre la barra o un panel suyo, botón pulsado o "peek" en curso.
            bool peeking = PeekTimerField.GetValue(this) is DispatcherTimer peek && peek.IsEnabled;
            bool buttonDown = System.Windows.Forms.Control.MouseButtons != System.Windows.Forms.MouseButtons.None;
            var popups = VisibleOwnPopups();
            var cursor = System.Windows.Forms.Cursor.Position;
            bool cursorOver = IsCursorIn(Handle, cursor) || popups.Exists(h => IsCursorIn(h, cursor));
            if (peeking || buttonDown || cursorOver)
            {
                // El marco (si está) se mantiene para poder leer el motivo en su tooltip; solo se reinicia el plazo.
                _blockedSince = null;
                return;
            }

            _blockedSince ??= DateTime.Now;
            var blockedFor = DateTime.Now - _blockedSince.Value;
            string reason = BlockedReason(popups.Count);

            if (blockedFor >= BlockedFrameDelay && !_blockedFrameShown)
            {
                _blockedFrameShown = true;
                RootBorder.BorderBrush = (System.Windows.Media.Brush)FindResource("AutoHideBlocked");
                RootBorder.BorderThickness = new Thickness(2);
                RootBorder.ToolTip = $"No se oculta: {reason}";
                DiagLog.Write($"[Auto-ocultar] {Screen.DeviceName}: la barra no se oculta → {reason}");
            }
            else if (_blockedFrameShown)
            {
                RootBorder.ToolTip = $"No se oculta: {reason}";
            }

            if (blockedFor >= BlockedReleaseDelay) ReleaseStaleBlocks(popups.Count);
        }

        /// <summary>Describe qué impide ocultar la barra.</summary>
        private string BlockedReason(int visiblePopups)
        {
            var reasons = new List<string>();
            if ((bool)MouseWithinField.GetValue(this)) reasons.Add("ratón dentro (estado obsoleto)");
            if ((bool)ContextMenuOpenField.GetValue(this)) reasons.Add("menú contextual abierto");
            if ((bool)DragWithinField.GetValue(this)) reasons.Add("arrastre en curso");
            if (_trayMenuTimer != null) reasons.Add($"menú de bandeja (pid {_trayMenuProcessId})");
            else if (_openPopups > 0) reasons.Add($"{_openPopups} panel(es) abierto(s), {visiblePopups} visible(s)");
            return reasons.Count > 0 ? string.Join(", ", reasons) : "motivo desconocido";
        }

        /// <summary>Libera los bloqueos que ya no corresponden a nada real y deja que la barra se oculte.</summary>
        private void ReleaseStaleBlocks(int visiblePopups)
        {
            var released = new List<string>();

            if ((bool)MouseWithinField.GetValue(this))
            {
                MouseWithinField.SetValue(this, false);
                released.Add("ratón dentro");
            }
            if ((bool)ContextMenuOpenField.GetValue(this) && visiblePopups == 0)
            {
                ContextMenuOpenField.SetValue(this, false);
                released.Add("menú contextual");
            }
            if ((bool)DragWithinField.GetValue(this))
            {
                DragWithinField.SetValue(this, false);
                released.Add("arrastre");
            }
            if (_openPopups > 0 && visiblePopups == 0 && _trayMenuTimer == null)
            {
                released.Add($"{_openPopups} panel(es)");
                _openPopups = 0;
            }

            if (released.Count == 0) return;

            DiagLog.Write($"[Auto-ocultar] {Screen.DeviceName}: liberado {string.Join(", ", released)} → la barra se oculta");
            OnPropertyChanged(nameof(AllowAutoHide));
        }

        private void ResetBlockedState()
        {
            _blockedSince = null;
            if (!_blockedFrameShown) return;

            _blockedFrameShown = false;
            RootBorder.BorderBrush = (System.Windows.Media.Brush)FindResource("BarBorder");
            RootBorder.BorderThickness = new Thickness(0, 1, 0, 0);
            RootBorder.ToolTip = null;
        }

        /// <summary>Ventanas visibles de este proceso que no son barras (vistas previas, menús, panel ^...).</summary>
        private static List<IntPtr> VisibleOwnPopups()
        {
            var result = new List<IntPtr>();
            foreach (PresentationSource source in PresentationSource.CurrentSources)
            {
                if (source is not HwndSource hwndSource || hwndSource.IsDisposed) continue;
                if (hwndSource.RootVisual is Window) continue; // barras y ventana de configuración
                if (Interop.NativeMethods.IsWindowVisible(hwndSource.Handle)) result.Add(hwndSource.Handle);
            }
            return result;
        }

        private static bool IsCursorIn(IntPtr hwnd, System.Drawing.Point cursor)
        {
            if (!Interop.NativeMethods.GetWindowRect(hwnd, out var r)) return false;
            return cursor.X >= r.Left && cursor.X < r.Right && cursor.Y >= r.Top && cursor.Y < r.Bottom;
        }
        #endregion

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

            // Como la barra de Windows: sigue visible mientras se muestra una ventana con "peek" desde las vistas previas.
            WindowHelper.ExcludeWindowFromPeek(Handle);
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
            _hideWatchdog?.Stop();
            _trayMenuTimer?.Stop();
            _trayMenuTimer = null;
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
