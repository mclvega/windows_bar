using System;
using System.Collections.Generic;
using System.Linq;
using ManagedShell.AppBar;
using WindowsBar.Views;

namespace WindowsBar.Services
{
    /// <summary>
    /// Crea una barra por monitor (o solo en el principal) y las recrea cuando cambian
    /// las pantallas, los ajustes que lo requieren o Explorer se reinicia.
    /// </summary>
    public class WindowManager : IDisposable
    {
        private readonly AppServices _services;
        private readonly List<TaskbarWindow> _taskbars = new();
        private List<AppBarScreen> _screens = new();
        private bool _isSettingDisplays;
        private int _pendingDisplayEvents;

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
            _services.ShellManager.ExplorerHelper.HideExplorerTaskbar = true;
            OpenTaskbars();
            _services.Settings.Changed += Settings_Changed;
        }

        public bool HasTaskbarOn(IntPtr hMonitor) => _taskbars.Any(t => t.Screen.HMonitor == hMonitor);

        private void Settings_Changed(object sender, EventArgs e)
        {
            var s = _services.Settings.Data;
            if (s.ShowOnAllMonitors != _openedAllMonitors || s.AutoHide != _openedAutoHide)
                ReopenTaskbars();
        }

        public void NotifyDisplayChange()
        {
            _pendingDisplayEvents++;
            if (_isSettingDisplays) return;

            _isSettingDisplays = true;
            while (_pendingDisplayEvents > 0)
            {
                if (HaveDisplaysChanged()) ReopenTaskbars();
                _pendingDisplayEvents--;
            }
            _isSettingDisplays = false;
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

            _screens = AppBarScreen.FromAllScreens();
            var targets = s.ShowOnAllMonitors ? _screens : _screens.Where(x => x.Primary).ToList();
            var mode = s.AutoHide ? AppBarMode.AutoHide : AppBarMode.Normal;

            foreach (var screen in targets)
            {
                var bar = new TaskbarWindow(_services, screen, mode);
                bar.Show();
                _taskbars.Add(bar);
            }
        }

        private void CloseTaskbars()
        {
            foreach (var bar in _taskbars)
            {
                bar.AllowClose = true;
                bar.Close();
            }
            _taskbars.Clear();
        }

        private bool HaveDisplaysChanged()
        {
            ResetWinFormsScreenCache();
            var current = AppBarScreen.FromAllScreens();
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
            _services.Settings.Changed -= Settings_Changed;
            CloseTaskbars();
            _services.ShellManager.ExplorerHelper.HideExplorerTaskbar = false;
        }
    }
}
