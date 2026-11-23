using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
using System.Windows.Threading;
using ManagedShell.WindowsTasks;
using WindowsBar.Interop;

namespace WindowsBar.Services
{
    /// <summary>
    /// Maximiza las ventanas nuevas de las apps, se abran desde donde se abran (barra, Inicio, Ctrl+N...).
    /// Las ventanas que ya estaban abiertas al arrancar la barra no se tocan. Quedan fuera los diálogos y
    /// ventanas de tamaño fijo, las minimizadas y las que salen a pantalla completa.
    /// </summary>
    public class NewWindowMaximizer : IDisposable
    {
        private const int PollMs = 200;
        private const int WaitSeconds = 5;
        private const int RecheckMs = 1000;

        private readonly Tasks _tasks;
        private readonly SettingsService _settings;
        private readonly LaunchPlacer _launchPlacer;
        private readonly DispatcherTimer _poll;
        private readonly HashSet<IntPtr> _known = new();
        private readonly List<Pending> _pending = new();
        private readonly uint _ownProcessId = (uint)Environment.ProcessId;
        private ICollectionView _windows;

        /// <summary>Ventana nueva a la espera de poder maximizarse.</summary>
        private class Pending
        {
            public ApplicationWindow Window { get; init; }
            public DateTime Expires { get; set; }
        }

        public NewWindowMaximizer(Tasks tasks, SettingsService settings, LaunchPlacer launchPlacer, Dispatcher dispatcher)
        {
            _tasks = tasks;
            _settings = settings;
            _launchPlacer = launchPlacer;
            _poll = new DispatcherTimer(TimeSpan.FromMilliseconds(PollMs), DispatcherPriority.Background, (_, _) => Poll(), dispatcher);
            _poll.Stop();
        }

        /// <summary>Empieza a vigilar. Las ventanas que ya existen se dan por conocidas y no se maximizan.</summary>
        public void Start()
        {
            if (_windows != null) return;
            _windows = _tasks.CreateGroupedWindowsCollection();
            foreach (var w in CurrentWindows()) _known.Add(w.Handle);
            _windows.CollectionChanged += OnWindowsChanged;
        }

        private IEnumerable<ApplicationWindow> CurrentWindows() =>
            _windows?.Cast<object>().OfType<ApplicationWindow>().ToList() ?? new List<ApplicationWindow>();

        private void OnWindowsChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            // Se olvidan las ventanas cerradas para que la lista no crezca sin fin.
            var current = CurrentWindows().ToList();
            _known.IntersectWith(current.Select(w => w.Handle));

            foreach (var w in current.Where(w => _known.Add(w.Handle)))
                _pending.Add(new Pending { Window = w, Expires = DateTime.Now.AddSeconds(WaitSeconds) });

            if (_pending.Count > 0) _poll.Start();
        }

        private void Poll()
        {
            bool mouseDown = (NativeMethods.GetAsyncKeyState(NativeMethods.VK_LBUTTON) & 0x8000) != 0;

            for (int i = _pending.Count - 1; i >= 0; i--)
            {
                var p = _pending[i];
                IntPtr hwnd = p.Window.Handle;

                if (!NativeMethods.IsWindow(hwnd)) { _pending.RemoveAt(i); continue; }

                // Arrastrando (p. ej. una pestaña sacada de Chrome): se espera a que se suelte.
                if (mouseDown)
                {
                    p.Expires = DateTime.Now.AddSeconds(WaitSeconds);
                    continue;
                }

                // Abierta desde la barra de otro monitor: se espera a que LaunchPlacer la lleve allí,
                // para maximizarla una sola vez y ya en la pantalla de destino.
                if (_launchPlacer.IsPlacing(p.Window))
                {
                    p.Expires = DateTime.Now.AddSeconds(WaitSeconds);
                    continue;
                }

                if (!IsReady(p.Window))
                {
                    if (DateTime.Now > p.Expires) _pending.RemoveAt(i);
                    continue;
                }

                _pending.RemoveAt(i);
                TryMaximize(p.Window, recheck: true);
            }

            if (_pending.Count == 0) _poll.Stop();
        }

        /// <summary>Ya visible y en la barra (las ventanas recién creadas tardan un momento en mostrarse).</summary>
        private static bool IsReady(ApplicationWindow w)
        {
            try { return NativeMethods.IsWindowVisible(w.Handle) && w.ShowInTaskbar; }
            catch { return false; }
        }

        private void TryMaximize(ApplicationWindow w, bool recheck)
        {
            if (!_settings.Data.MaximizeNewWindows) return;

            IntPtr hwnd = w.Handle;
            if (!NativeMethods.IsWindow(hwnd) || !NativeMethods.IsWindowVisible(hwnd)) return;
            if (NativeMethods.IsIconic(hwnd) || NativeMethods.IsZoomed(hwnd)) return;
            if (!CanMaximize(hwnd) || IsFullScreen(hwnd) || IsOwnWindow(hwnd)) return;

            NativeMethods.ShowWindow(hwnd, NativeMethods.SW_SHOWMAXIMIZED);
            if (recheck) DiagLog.Write($"[Maximizar] '{SafeTitle(w)}' ventana nueva maximizada");

            // Algunas apps recolocan su ventana al terminar de cargar: se comprueba una vez más.
            if (recheck)
            {
                var timer = new DispatcherTimer(TimeSpan.FromMilliseconds(RecheckMs), DispatcherPriority.Background,
                    (s, _) =>
                    {
                        ((DispatcherTimer)s).Stop();
                        if (!NativeMethods.IsWindow(hwnd) || NativeMethods.IsZoomed(hwnd)) return;
                        DiagLog.Write($"[Maximizar] '{SafeTitle(w)}' la app la restauró → se vuelve a maximizar");
                        TryMaximize(w, recheck: false);
                    }, _poll.Dispatcher);
                timer.Start();
            }
        }

        /// <summary>Redimensionable y con botón de maximizar: descarta diálogos y ventanas de tamaño fijo.</summary>
        private static bool CanMaximize(IntPtr hwnd)
        {
            long style = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GWL_STYLE).ToInt64();
            return (style & NativeMethods.WS_MAXIMIZEBOX) != 0 && (style & NativeMethods.WS_THICKFRAME) != 0;
        }

        /// <summary>Ocupa el monitor entero (juegos, vídeos a pantalla completa).</summary>
        private static bool IsFullScreen(IntPtr hwnd)
        {
            if (!NativeMethods.GetWindowRect(hwnd, out var r)) return false;
            var bounds = System.Windows.Forms.Screen.FromHandle(hwnd).Bounds;
            return Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom).Contains(bounds);
        }

        private bool IsOwnWindow(IntPtr hwnd)
        {
            NativeMethods.GetWindowThreadProcessId(hwnd, out uint pid);
            return pid == _ownProcessId;
        }

        private static string SafeTitle(ApplicationWindow w)
        {
            try { return w.Title; }
            catch { return w.Handle.ToString(); }
        }

        public void Dispose()
        {
            _poll.Stop();
            _pending.Clear();
            if (_windows != null) _windows.CollectionChanged -= OnWindowsChanged;
        }
    }
}
