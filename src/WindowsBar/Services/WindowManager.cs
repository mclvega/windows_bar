using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Threading;
using ManagedShell.AppBar;
using Microsoft.Win32;
using WindowsBar.Interop;
using WindowsBar.Views;

namespace WindowsBar.Services
{
    /// <summary>
    /// Crea una barra por monitor (o solo en el principal) y las recrea cuando cambian
    /// las pantallas, los ajustes que lo requieren o Explorer se reinicia.
    /// </summary>
    public class WindowManager : IDisposable
    {
        /// <summary>Espera tras el último aviso de cambio de pantalla (RDP manda ráfagas) antes de actuar.</summary>
        private static readonly TimeSpan DisplayDebounce = TimeSpan.FromMilliseconds(750);

        /// <summary>Tiempo que se deja a una barra recién abierta para colocarse antes de comprobarla.</summary>
        private static readonly TimeSpan VerifyDelay = TimeSpan.FromSeconds(2);

        /// <summary>Recreaciones seguidas por barras mal colocadas antes de dejar de insistir.</summary>
        private const int MaxRepairAttempts = 3;

        /// <summary>Margen en píxeles al comparar la ventana con el borde del monitor.</summary>
        private const int EdgeTolerance = 2;

        private readonly AppServices _services;
        private readonly List<TaskbarWindow> _taskbars = new();
        private List<AppBarScreen> _screens = new();
        private DispatcherTimer _displayTimer;
        private DispatcherTimer _verifyTimer;
        private int _repairAttempts;

        // Ajustes con los que se abrieron las barras: si cambian hay que reabrirlas.
        private bool _openedAllMonitors;
        private bool _openedAutoHide;

        public WindowManager(AppServices services)
        {
            _services = services;
        }

        public IReadOnlyList<TaskbarWindow> Taskbars => _taskbars;

        public void Start()
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            _displayTimer = new DispatcherTimer(DisplayDebounce, DispatcherPriority.Normal, DisplayTimer_Tick, dispatcher);
            _displayTimer.Stop();
            _verifyTimer = new DispatcherTimer(VerifyDelay, DispatcherPriority.Background, VerifyTimer_Tick, dispatcher);
            _verifyTimer.Stop();

            _services.ShellManager.ExplorerHelper.HideExplorerTaskbar = true;
            OpenTaskbars();
            _services.Settings.Changed += Settings_Changed;
            SystemEvents.SessionSwitch += SystemEvents_SessionSwitch;
        }

        public bool HasTaskbarOn(IntPtr hMonitor) => _taskbars.Any(t => t.Screen.HMonitor == hMonitor);

        private void Settings_Changed(object sender, EventArgs e)
        {
            var s = _services.Settings.Data;
            if (s.ShowOnAllMonitors != _openedAllMonitors || s.AutoHide != _openedAutoHide)
                ReopenTaskbars();
        }

        /// <summary>
        /// Aviso de que las pantallas pueden haber cambiado. No actúa en el momento (puede llegar desde el
        /// WndProc de una barra y en ráfagas): reinicia la espera y, al terminar, reabre o verifica las barras.
        /// </summary>
        public void NotifyDisplayChange()
        {
            if (_displayTimer == null) return;
            _displayTimer.Stop();
            _displayTimer.Start();
        }

        /// <summary>Al reconectar o desbloquear la sesión cambian las pantallas (sobre todo por RDP).</summary>
        private void SystemEvents_SessionSwitch(object sender, SessionSwitchEventArgs e)
        {
            if (e.Reason is not (SessionSwitchReason.RemoteConnect or SessionSwitchReason.ConsoleConnect
                                 or SessionSwitchReason.SessionUnlock))
                return;

            _displayTimer?.Dispatcher.BeginInvoke(new Action(() =>
            {
                DiagLog.Write($"[Sesión] {e.Reason} → se revisan las barras");
                NotifyDisplayChange();
            }));
        }

        private void DisplayTimer_Tick(object sender, EventArgs e)
        {
            _displayTimer.Stop();

            ResetWinFormsScreenCache();
            var current = AppBarScreen.FromAllScreens();
            if (current.Count == 0)
            {
                DiagLog.Write("[Pantallas] ninguna pantalla disponible → se espera al siguiente cambio");
                return;
            }

            if (HaveDisplaysChanged(current))
            {
                DiagLog.Write($"[Pantallas] cambio: {Describe(_screens)} → {Describe(current)}");
                _repairAttempts = 0;
                ReopenTaskbars();
            }
            else
            {
                VerifyTaskbars();
            }
        }

        /// <summary>Explorer se reinició: su barra vuelve a aparecer y hay que ocultarla de nuevo.</summary>
        public void HandleExplorerRestart()
        {
            var explorer = _services.ShellManager.ExplorerHelper;
            explorer.HideExplorerTaskbar = false;
            explorer.HideExplorerTaskbar = true;
            ReopenTaskbars();
        }

        public void ReopenTaskbars()
        {
            CloseTaskbars();
            OpenTaskbars();
        }

        private void OpenTaskbars()
        {
            var s = _services.Settings.Data;
            _openedAllMonitors = s.ShowOnAllMonitors;
            _openedAutoHide = s.AutoHide;

            ResetWinFormsScreenCache();
            _screens = AppBarScreen.FromAllScreens();
            var targets = s.ShowOnAllMonitors ? _screens : _screens.Where(x => x.Primary).ToList();
            var mode = s.AutoHide ? AppBarMode.AutoHide : AppBarMode.Normal;

            foreach (var screen in targets)
            {
                var bar = new TaskbarWindow(_services, screen, mode);
                bar.Show();
                _taskbars.Add(bar);
            }

            DiagLog.Write($"[Pantallas] barras abiertas: {Describe(targets)}");
            ScheduleVerify();
        }

        private void CloseTaskbars()
        {
            _verifyTimer?.Stop();
            foreach (var bar in _taskbars)
            {
                bar.AllowClose = true;
                bar.Close();
            }
            _taskbars.Clear();
        }

        private void ScheduleVerify()
        {
            if (_verifyTimer == null) return;
            _verifyTimer.Stop();
            _verifyTimer.Start();
        }

        private void VerifyTimer_Tick(object sender, EventArgs e)
        {
            _verifyTimer.Stop();
            VerifyTaskbars();
        }

        /// <summary>
        /// Comprueba que cada barra está visible y pegada al borde inferior de su monitor. Si alguna quedó
        /// a medio inicializar (p. ej. oculta y con el tamaño por defecto de WPF), recrea las barras.
        /// </summary>
        private void VerifyTaskbars()
        {
            foreach (var bar in _taskbars)
            {
                if (IsPlacedCorrectly(bar, out string detail)) continue;

                if (_repairAttempts >= MaxRepairAttempts)
                {
                    DiagLog.Write($"[Pantallas] barra {bar.Screen.DeviceName} sigue mal colocada ({detail}) " +
                                  $"tras {MaxRepairAttempts} intentos → se espera al siguiente cambio de pantalla");
                    return;
                }

                _repairAttempts++;
                DiagLog.Write($"[Pantallas] barra {bar.Screen.DeviceName} mal colocada ({detail}) → se recrea " +
                              $"(intento {_repairAttempts}/{MaxRepairAttempts})");
                ReopenTaskbars();
                return;
            }

            _repairAttempts = 0;
        }

        private static bool IsPlacedCorrectly(TaskbarWindow bar, out string detail)
        {
            IntPtr hwnd = bar.Handle;
            if (hwnd == IntPtr.Zero || !NativeMethods.IsWindow(hwnd))
            {
                detail = "sin ventana";
                return false;
            }

            bool visible = NativeMethods.IsWindowVisible(hwnd);
            NativeMethods.GetWindowRect(hwnd, out var r);
            var b = bar.Screen.Bounds;
            detail = $"visible={visible} rect=({r.Left},{r.Top},{r.Right},{r.Bottom}) monitor={b}";

            return visible
                   && Math.Abs(r.Left - b.Left) <= EdgeTolerance
                   && Math.Abs(r.Right - b.Right) <= EdgeTolerance
                   && Math.Abs(r.Bottom - b.Bottom) <= EdgeTolerance
                   && r.Top > b.Top;
        }

        private bool HaveDisplaysChanged(List<AppBarScreen> current)
        {
            if (current.Count != _screens.Count) return true;

            for (int i = 0; i < current.Count; i++)
            {
                var a = _screens[i];
                var b = current[i];
                if (a.Bounds != b.Bounds || a.DeviceName != b.DeviceName || a.Primary != b.Primary || a.HMonitor != b.HMonitor)
                    return true;
            }
            return false;
        }

        private static string Describe(IEnumerable<AppBarScreen> screens)
        {
            var list = screens.Select(s => $"{s.DeviceName}{(s.Primary ? " (principal)" : "")} {s.Bounds}").ToList();
            return list.Count == 0 ? "(ninguna)" : string.Join(", ", list);
        }

        /// <summary>System.Windows.Forms.Screen cachea los monitores; hay que vaciar la caché para ver cambios.</summary>
        private static void ResetWinFormsScreenCache()
        {
            const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic;
            var field = typeof(System.Windows.Forms.Screen).GetField("screens", flags)
                        ?? typeof(System.Windows.Forms.Screen).GetField("s_screens", flags);
            field?.SetValue(null, null);
        }

        public void Dispose()
        {
            SystemEvents.SessionSwitch -= SystemEvents_SessionSwitch;
            _services.Settings.Changed -= Settings_Changed;
            _displayTimer?.Stop();
            CloseTaskbars();
            _services.ShellManager.ExplorerHelper.HideExplorerTaskbar = false;
        }
    }
}
