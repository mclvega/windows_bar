using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Windows.Threading;
using ManagedShell.WindowsTasks;

namespace WindowsBar.Services
{
    /// <summary>
    /// Lista de botones de una barra concreta: primero las apps ancladas (en su orden),
    /// después las apps abiertas no ancladas (en orden de apertura). Las ventanas de una
    /// misma app se agrupan en un solo botón, como en Windows 11.
    /// </summary>
    public class TaskGroupCollection : IDisposable
    {
        private readonly Tasks _tasks;
        private readonly PinnedAppsService _pins;
        private readonly SettingsService _settings;
        private readonly Func<ApplicationWindow, bool> _isOnThisMonitor;
        private readonly Dispatcher _dispatcher;
        private readonly ICollectionView _windows;
        private readonly HashSet<ApplicationWindow> _watched = new();
        private readonly Dictionary<string, TaskGroup> _groups = new();
        private bool _refreshPending;
        private bool _dragging;
        private bool _refreshDeferred;

        public ObservableCollection<TaskGroup> Items { get; } = new();

        public TaskGroupCollection(Tasks tasks, PinnedAppsService pins, SettingsService settings,
            Func<ApplicationWindow, bool> isOnThisMonitor, Dispatcher dispatcher)
        {
            _tasks = tasks;
            _pins = pins;
            _settings = settings;
            _isOnThisMonitor = isOnThisMonitor;
            _dispatcher = dispatcher;

            _windows = tasks.CreateGroupedWindowsCollection();
            _windows.Filter = o => o is ApplicationWindow w && Interop.NativeMethods.IsWindow(w.Handle) && w.ShowInTaskbar && (_settings.Data.ShowTasksOnAllTaskbars || _isOnThisMonitor(w));
            _windows.CollectionChanged += OnWindowsChanged;

            _pins.Changed += OnExternalChange;
            _settings.Changed += OnSettingsChanged;

            Refresh();
        }

        private void OnSettingsChanged(object sender, EventArgs e)
        {
            _windows.Refresh();
            ScheduleRefresh();
        }

        private void OnExternalChange(object sender, EventArgs e) => ScheduleRefresh();

        private void OnWindowsChanged(object sender, NotifyCollectionChangedEventArgs e) => ScheduleRefresh();

        private void OnWindowPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            switch (e.PropertyName)
            {
                case "State":
                case "Title":
                case "Icon":
                case "ProgressState":
                case "ProgressValue":
                    // Cambio visual: basta con refrescar el botón que contiene esa ventana.
                    var group = Items.FirstOrDefault(g => g.Windows.Contains((ApplicationWindow)sender));
                    group?.RaiseAll();
                    break;
                case "HMonitor":
                    if (!_settings.Data.ShowTasksOnAllTaskbars) ScheduleRefresh();
                    break;
            }
        }

        private void ScheduleRefresh()
        {
            if (_refreshPending) return;
            _refreshPending = true;
            _dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
            {
                _refreshPending = false;
                // Durante un arrastre, Refresh devolvería las ancladas a su orden guardado: se espera al soltar.
                if (_dragging) { _refreshDeferred = true; return; }
                Refresh();
            }));
        }

        #region Reordenar arrastrando
        /// <summary>Empieza a mover un botón con el ratón.</summary>
        public void BeginDrag() => _dragging = true;

        /// <summary>
        /// Mueve en vivo el botón arrastrado. Las ancladas solo se mueven entre ancladas
        /// y las abiertas no ancladas entre ellas.
        /// </summary>
        public void DragTo(TaskGroup group, int index)
        {
            int current = Items.IndexOf(group);
            if (!_dragging || current < 0) return;

            int pinnedCount = Items.Count(g => g.IsPinned);
            int min = group.IsPinned ? 0 : pinnedCount;
            int max = group.IsPinned ? pinnedCount - 1 : Items.Count - 1;
            index = Math.Clamp(index, min, max);
            if (index != current) Items.Move(current, index);
        }

        /// <summary>
        /// Termina el arrastre. Las ancladas guardan su nueva posición (y se actualizan las demás barras);
        /// las no ancladas conservan el orden de esta barra durante la sesión.
        /// </summary>
        public void EndDrag(TaskGroup group)
        {
            if (!_dragging) return;
            _dragging = false;

            if (group?.Pin != null)
            {
                int pinIndex = Items.Where(g => g.IsPinned).ToList().IndexOf(group);
                if (pinIndex >= 0) _pins.Move(group.Pin, pinIndex);
            }

            if (_refreshDeferred)
            {
                _refreshDeferred = false;
                ScheduleRefresh();
            }
        }
        #endregion

        private void Refresh()
        {
            var windows = _windows.Cast<ApplicationWindow>().ToList();

            // Suscripciones a cambios de cada ventana.
            foreach (var w in windows.Where(w => _watched.Add(w)))
                w.PropertyChanged += OnWindowPropertyChanged;
            foreach (var gone in _watched.Where(w => !windows.Contains(w)).ToList())
            {
                gone.PropertyChanged -= OnWindowPropertyChanged;
                _watched.Remove(gone);
            }

            // Ventanas agrupadas por clave de app.
            var byKey = new Dictionary<string, List<ApplicationWindow>>();
            var runningOrder = new List<string>();
            foreach (var w in windows)
            {
                var pin = _pins.FindMatch(w);
                if (pin != null) _pins.RememberProfile(pin, w);
                string key = pin != null ? PinKey(pin) : WindowKey(w);
                if (!byKey.TryGetValue(key, out var list))
                {
                    byKey[key] = list = new List<ApplicationWindow>();
                    runningOrder.Add(key);
                }
                list.Add(w);
            }

            // Orden deseado: ancladas, luego abiertas no ancladas (las existentes conservan su posición relativa).
            var desired = new List<TaskGroup>();
            foreach (var pin in _pins.Apps)
            {
                var g = GetGroup(PinKey(pin));
                g.Pin = pin;
                desired.Add(g);
            }

            var previousOrder = Items.Select(i => i.Key).ToList();
            var unpinnedKeys = runningOrder.Where(k => !desired.Any(d => d.Key == k))
                .OrderBy(k => { int i = previousOrder.IndexOf(k); return i < 0 ? int.MaxValue : i; })
                .ToList();
            foreach (string key in unpinnedKeys)
            {
                var g = GetGroup(key);
                g.Pin = null;
                desired.Add(g);
            }

            // Ventanas de cada botón.
            foreach (var g in desired)
            {
                var target = byKey.TryGetValue(g.Key, out var list) ? list : new List<ApplicationWindow>();
                bool changed = false;
                for (int i = g.Windows.Count - 1; i >= 0; i--)
                    if (!target.Contains(g.Windows[i])) { g.Windows.RemoveAt(i); changed = true; }
                foreach (var w in target.Where(w => !g.Windows.Contains(w)))
                {
                    g.Windows.Add(w);
                    changed = true;
                }
                if (changed) g.RaiseAll();
            }

            // Sincronizar la colección visible con movimientos mínimos.
            for (int i = Items.Count - 1; i >= 0; i--)
                if (!desired.Contains(Items[i])) Items.RemoveAt(i);
            for (int i = 0; i < desired.Count; i++)
            {
                int current = Items.IndexOf(desired[i]);
                if (current < 0) Items.Insert(i, desired[i]);
                else if (current != i) Items.Move(current, i);
            }

            foreach (var key in _groups.Keys.Where(k => !desired.Any(d => d.Key == k)).ToList())
                _groups.Remove(key);
        }

        private TaskGroup GetGroup(string key)
        {
            if (!_groups.TryGetValue(key, out var g))
                _groups[key] = g = new TaskGroup(key);
            return g;
        }

        // Los argumentos distinguen anclajes del mismo ejecutable (dos perfiles de Chrome). Un .lnk ya es
        // único: su clave no cambia cuando aprende el perfil (así el botón no se recrea).
        private static string PinKey(PinnedApp pin) =>
            "pin:" + (pin.LaunchPath ?? pin.TargetPath ?? pin.Aumid).ToLowerInvariant()
            + (string.IsNullOrEmpty(pin.Arguments) || IsShortcut(pin.LaunchPath) ? "" : " " + pin.Arguments.ToLowerInvariant());

        private static bool IsShortcut(string path) =>
            path != null && path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase);

        internal static string WindowKey(ApplicationWindow w)
        {
            string aumid = Interop.AppIdentity.GetAumid(w), path = null;
            if (!string.IsNullOrEmpty(aumid)) return "aumid:" + aumid.ToLowerInvariant();
            try { path = w.WinFileName; } catch { }
            return string.IsNullOrEmpty(path) ? "hwnd:" + w.Handle : "path:" + path.ToLowerInvariant();
        }

        public void Dispose()
        {
            _windows.CollectionChanged -= OnWindowsChanged;
            _windows.Filter = null;
            _pins.Changed -= OnExternalChange;
            _settings.Changed -= OnSettingsChanged;
            foreach (var w in _watched) w.PropertyChanged -= OnWindowPropertyChanged;
            _watched.Clear();
        }
    }
}
