using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using ManagedShell.WindowsTasks;
using WindowsBar.Interop;
using WindowsBar.Views;

namespace WindowsBar.Services
{
    /// <summary>
    /// Tras abrir una app desde una barra, lleva sus ventanas nuevas al monitor de esa barra. Hace falta para
    /// las apps que restauran su posición guardada (VS Code, Chrome, Discord...) y para las de la Store,
    /// que ignoran el monitor pedido a ShellExecuteEx. Las ventanas que ya estaban abiertas no se tocan.
    /// </summary>
    public class LaunchPlacer : IDisposable
    {
        private const int ExpectSeconds = 15;
        private const int PollMs = 200;
        private const int RecheckMs = 1000;

        private readonly Tasks _tasks;
        private readonly DispatcherTimer _poll;
        private readonly List<Expectation> _pending = new();
        private ICollectionView _windows;

        /// <summary>Una app recién abierta cuyas ventanas nuevas deben ir a un monitor concreto.</summary>
        private class Expectation
        {
            public string Name { get; init; }
            public Func<ApplicationWindow, bool> Matches { get; init; }
            public Rectangle TargetBounds { get; init; }
            public Rectangle TargetWorkArea { get; init; }
            public string TargetName { get; init; }
            public HashSet<IntPtr> Seen { get; init; }
            public DateTime Expires { get; init; }
        }

        public LaunchPlacer(Tasks tasks, Dispatcher dispatcher)
        {
            _tasks = tasks;
            _poll = new DispatcherTimer(TimeSpan.FromMilliseconds(PollMs), DispatcherPriority.Background, (_, _) => Poll(), dispatcher);
            _poll.Stop();
        }

        /// <summary>Se va a abrir la app de <paramref name="group"/> desde la barra <paramref name="host"/>.</summary>
        public void Expect(TaskGroup group, TaskbarWindow host)
        {
            if (group == null || host == null) return;

            // Vista propia y sin filtrar: ve la ventana aunque la barra pulsada solo muestre las de su monitor.
            // Se crea al primer uso, cuando la lista de ventanas de ManagedShell ya está inicializada.
            _windows ??= _tasks.CreateGroupedWindowsCollection();

            var pin = group.Pin;
            string key = group.Key;
            Func<ApplicationWindow, bool> matches = pin != null
                ? w => PinnedAppsService.Matches(pin, w)
                : w => TaskGroupCollection.WindowKey(w) == key;

            _pending.Add(new Expectation
            {
                Name = group.Name,
                Matches = matches,
                TargetBounds = host.Screen.Bounds,
                TargetWorkArea = host.Screen.WorkingArea,
                TargetName = host.Screen.DeviceName,
                Seen = new HashSet<IntPtr>(CurrentWindows().Where(SafeMatch(matches)).Select(w => w.Handle)),
                Expires = DateTime.Now.AddSeconds(ExpectSeconds)
            });
            _poll.Start();
        }

        private IEnumerable<ApplicationWindow> CurrentWindows() =>
            _windows?.Cast<object>().OfType<ApplicationWindow>().ToList() ?? new List<ApplicationWindow>();

        private static Func<ApplicationWindow, bool> SafeMatch(Func<ApplicationWindow, bool> matches) => w =>
        {
            try { return matches(w); }
            catch { return false; }
        };

        private void Poll()
        {
            _pending.RemoveAll(e => DateTime.Now > e.Expires);
            if (_pending.Count == 0)
            {
                _poll.Stop();
                return;
            }

            var windows = CurrentWindows();
            foreach (var expectation in _pending)
            {
                foreach (var w in windows)
                {
                    IntPtr hwnd = w.Handle;
                    if (expectation.Seen.Contains(hwnd)) continue;
                    // Todavía sin mostrar: se vuelve a mirar en la siguiente pasada.
                    if (!NativeMethods.IsWindow(hwnd) || !NativeMethods.IsWindowVisible(hwnd)) continue;
                    if (!SafeMatch(expectation.Matches)(w)) continue;

                    expectation.Seen.Add(hwnd);
                    MoveToTarget(hwnd, expectation);

                    // Algunas apps se recolocan al terminar de cargar: se comprueba otra vez al cabo de un momento.
                    var recheck = new DispatcherTimer(TimeSpan.FromMilliseconds(RecheckMs), DispatcherPriority.Background,
                        (s, _) => { ((DispatcherTimer)s).Stop(); MoveToTarget(hwnd, expectation); }, _poll.Dispatcher);
                    recheck.Start();
                }
            }
        }

        /// <summary>
        /// Misma posición relativa dentro de la zona de trabajo del monitor de destino (reducida si no cabe).
        /// Con Get/SetWindowPlacement se conserva el estado: una ventana maximizada queda maximizada allí.
        /// </summary>
        private static void MoveToTarget(IntPtr hwnd, Expectation e)
        {
            if (!NativeMethods.IsWindow(hwnd)) return;

            var current = System.Windows.Forms.Screen.FromHandle(hwnd);
            if (current.Bounds.Equals(e.TargetBounds)) return;

            var placement = new NativeMethods.WINDOWPLACEMENT { length = Marshal.SizeOf<NativeMethods.WINDOWPLACEMENT>() };
            if (!NativeMethods.GetWindowPlacement(hwnd, ref placement)) return;
            if (placement.showCmd == NativeMethods.SW_SHOWMINIMIZED) return;

            var source = current.WorkingArea;
            var target = e.TargetWorkArea;
            var normal = placement.rcNormalPosition;
            int width = Math.Min(normal.Right - normal.Left, target.Width);
            int height = Math.Min(normal.Bottom - normal.Top, target.Height);
            int x = target.Left + Math.Clamp(normal.Left - source.Left, 0, target.Width - width);
            int y = target.Top + Math.Clamp(normal.Top - source.Top, 0, target.Height - height);

            placement.flags = 0;
            placement.rcNormalPosition = new NativeMethods.RECT { Left = x, Top = y, Right = x + width, Bottom = y + height };
            bool moved = NativeMethods.SetWindowPlacement(hwnd, ref placement);

            DiagLog.Write($"[Abrir] '{e.Name}' ventana en otro monitor ({current.DeviceName}) → movida a {e.TargetName} " +
                          $"({x},{y},{width}x{height}) maximizada={placement.showCmd == 3} resultado={moved}");
        }

        public void Dispose()
        {
            _poll.Stop();
            _pending.Clear();
        }
    }
}
