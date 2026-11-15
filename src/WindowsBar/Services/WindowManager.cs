using System;
using System.Collections.Generic;
using System.Linq;
using ManagedShell.AppBar;
using WindowsBar.Views;

namespace WindowsBar.Services
{
    /// <summary>
    /// Crea una barra por monitor y las recrea cuando cambia la configuración de pantallas.
    /// </summary>
    public class WindowManager : IDisposable
    {
        private readonly AppServices _services;
        private readonly List<TaskbarWindow> _taskbars = new();
        private List<AppBarScreen> _screens = new();
        private bool _isSettingDisplays;
        private int _pendingDisplayEvents;

        public WindowManager(AppServices services)
        {
            _services = services;
        }

        public IReadOnlyList<TaskbarWindow> Taskbars => _taskbars;

        public void Start()
        {
            _services.ShellManager.ExplorerHelper.HideExplorerTaskbar = true;
            OpenTaskbars();
        }

        public bool IsKnownMonitor(IntPtr hMonitor) => _screens.Any(s => s.HMonitor == hMonitor);

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

        public void ReopenTaskbars()
        {
            CloseTaskbars();
            OpenTaskbars();
        }

        private void OpenTaskbars()
        {
            _screens = AppBarScreen.FromAllScreens();
            foreach (var screen in _screens)
            {
                var bar = new TaskbarWindow(_services, screen);
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
            CloseTaskbars();
            _services.ShellManager.ExplorerHelper.HideExplorerTaskbar = false;
        }
    }
}
