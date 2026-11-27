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
        // Rápido: cuanto antes se detecta la ventana, menos tiempo se ve en el monitor equivocado.
        private const int PollMs = 50;
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
            /// <summary>Solo coloca la primera ventana nueva que aparezca (menús del shell: no se sabe qué app abrirán).</summary>
            public bool SingleShot { get; init; }
            public bool Done { get; set; }
            /// <summary>Ventana en primer plano al abrir el menú: si vuelve a ella (Esc), no se mueve.</summary>
            public IntPtr ForegroundAtStart { get; init; }
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

            // Se abre una app concreta desde la barra: deja de esperar lo que pudiera abrir un menú del shell.
            _pending.RemoveAll(e => e.SingleShot);

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

        /// <summary>
        /// Se abre un menú del shell (Win+X) desde la barra <paramref name="host"/>: lo que se abra desde él lo lanza
        /// Explorer en el monitor principal, así que la primera ventana nueva que aparezca se lleva a esta barra.
        /// </summary>
        public void ExpectFromShellMenu(TaskbarWindow host, string name)
        {
            if (host == null) return;

            _windows ??= _tasks.CreateGroupedWindowsCollection();
            _pending.RemoveAll(e => e.SingleShot);

            _pending.Add(new Expectation
            {
                Name = name,
                Matches = _ => true,
                TargetBounds = host.Screen.Bounds,
                TargetWorkArea = host.Screen.WorkingArea,
                TargetName = host.Screen.DeviceName,
                Seen = new HashSet<IntPtr>(CurrentWindows().Select(w => w.Handle)),
                Expires = DateTime.Now.AddSeconds(ExpectSeconds),
                SingleShot = true,
                ForegroundAtStart = NativeMethods.GetForegroundWindow()
            });
            _poll.Start();
        }

        /// <summary>
        /// true si la ventana es de una app recién abierta desde una barra y aún no se ha llevado a su monitor
        /// (el maximizador espera a que esté allí para maximizarla una sola vez, ya en la pantalla de destino).
        /// </summary>
        public bool IsPlacing(ApplicationWindow w) =>
            _pending.Any(e => !e.Done && DateTime.Now <= e.Expires && !e.Seen.Contains(w.Handle) && SafeMatch(e.Matches)(w));

        private IEnumerable<ApplicationWindow> CurrentWindows() =>
            _windows?.Cast<object>().OfType<ApplicationWindow>().ToList() ?? new List<ApplicationWindow>();

        private static Func<ApplicationWindow, bool> SafeMatch(Func<ApplicationWindow, bool> matches) => w =>
        {
            try { return matches(w); }
            catch { return false; }
        };

        private void Poll()
        {
            var windows = CurrentWindows();

            foreach (var expired in _pending.Where(e => e.SingleShot && !e.Done && DateTime.Now > e.Expires))
            {
                IntPtr fg = NativeMethods.GetForegroundWindow();
                string fgTitle = windows.FirstOrDefault(w => w.Handle == fg)?.Title ?? "(no está en la lista de tareas)";
                DiagLog.Write($"[Abrir] '{expired.Name}' caducó sin ventana que mover (primer plano: 0x{fg.ToInt64():X} '{fgTitle}')");
            }

            _pending.RemoveAll(e => e.Done || DateTime.Now > e.Expires);
            if (_pending.Count == 0)
            {
                _poll.Stop();
                return;
            }

            foreach (var expectation in _pending)
            {
                foreach (var w in windows)
                {
                    if (expectation.Done) break;
                    IntPtr hwnd = w.Handle;
                    if (expectation.Seen.Contains(hwnd)) continue;
                    // Todavía sin mostrar: se vuelve a mirar en la siguiente pasada.
                    if (!NativeMethods.IsWindow(hwnd) || !NativeMethods.IsWindowVisible(hwnd)) continue;
                    if (!SafeMatch(expectation.Matches)(w)) continue;

                    expectation.Seen.Add(hwnd);
                    if (expectation.SingleShot) expectation.Done = true;
                    Place(hwnd, expectation, "[Abrir]");
                }

                if (expectation.SingleShot && !expectation.Done) PlaceActivatedWindow(expectation, windows);
            }
        }

        /// <summary>
        /// Menú del shell: si la opción elegida es una app que ya estaba abierta, Windows solo activa su ventana
        /// (en el monitor donde esté). Esa ventana, ya existente, también se lleva al monitor de la barra.
        /// </summary>
        private void PlaceActivatedWindow(Expectation expectation, IEnumerable<ApplicationWindow> windows)
        {
            IntPtr fg = NativeMethods.GetForegroundWindow();
            if (fg == IntPtr.Zero || fg == expectation.ForegroundAtStart) return;
            // Solo ventanas de la lista de tareas: nunca el propio menú, nuestras barras ni paneles del shell.
            if (!windows.Any(w => w.Handle == fg)) return;
            // Se eligió una opción del menú (ratón en el monitor de la barra), no se hizo clic en otra ventana.
            if (!expectation.TargetBounds.Contains(System.Windows.Forms.Cursor.Position)) return;

            expectation.Done = true;
            Place(fg, expectation, "[Traer]");
        }

        private void Place(IntPtr hwnd, Expectation expectation, string tag)
        {
            MoveToScreen(hwnd, expectation.TargetBounds, expectation.TargetWorkArea, expectation.TargetName, expectation.Name, tag, true);

            // Algunas apps se recolocan al terminar de cargar: se comprueba otra vez al cabo de un momento.
            var recheck = new DispatcherTimer(TimeSpan.FromMilliseconds(RecheckMs), DispatcherPriority.Background,
                (s, _) => { ((DispatcherTimer)s).Stop(); MoveToTarget(hwnd, expectation, log: false); }, _poll.Dispatcher);
            recheck.Start();
        }

        private static void MoveToTarget(IntPtr hwnd, Expectation e, bool log = true) =>
            MoveToScreen(hwnd, e.TargetBounds, e.TargetWorkArea, e.TargetName, e.Name, "[Abrir]", log);

        /// <summary>Lleva una ventana ya abierta a la pantalla de la barra pulsada (si está en otra).</summary>
        public static void MoveToScreen(IntPtr hwnd, TaskbarWindow host, string appName)
        {
            if (host == null) return;
            MoveToScreen(hwnd, host.Screen.Bounds, host.Screen.WorkingArea, host.Screen.DeviceName, appName, "[Traer]", false);
        }

        /// <summary>true si la ventana (o, minimizada, su posición al restaurarla) está en la pantalla de esa barra.</summary>
        public static bool IsOnScreen(IntPtr hwnd, TaskbarWindow host) =>
            host == null || SourceScreen(hwnd, out _)?.Bounds.Equals(host.Screen.Bounds) != false;

        /// <summary>Pantalla de la ventana; si está minimizada, la de su posición normal (donde aparecerá al restaurarla).</summary>
        private static System.Windows.Forms.Screen SourceScreen(IntPtr hwnd, out NativeMethods.WINDOWPLACEMENT placement)
        {
            placement = new NativeMethods.WINDOWPLACEMENT { length = Marshal.SizeOf<NativeMethods.WINDOWPLACEMENT>() };
            if (!NativeMethods.IsWindow(hwnd) || !NativeMethods.GetWindowPlacement(hwnd, ref placement)) return null;

            if (NativeMethods.IsIconic(hwnd))
            {
                var r = placement.rcNormalPosition;
                return System.Windows.Forms.Screen.FromRectangle(Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom));
            }
            return System.Windows.Forms.Screen.FromHandle(hwnd);
        }

        /// <summary>
        /// Misma posición relativa dentro de la zona de trabajo del monitor de destino (reducida si no cabe).
        /// Una ventana maximizada no cambia de monitor con SetWindowPlacement (comprobado): se restaura, se mueve
        /// con SetWindowPos y se vuelve a maximizar allí. Una minimizada se restaura primero (el clic la trae al frente).
        /// </summary>
        private static void MoveToScreen(IntPtr hwnd, Rectangle targetBounds, Rectangle targetWorkArea, string targetName,
            string appName, string tag, bool logIfAlreadyThere)
        {
            var current = SourceScreen(hwnd, out _);
            if (current == null) return;
            if (current.Bounds.Equals(targetBounds))
            {
                if (logIfAlreadyThere) DiagLog.Write($"{tag} '{appName}' ventana ya en {targetName}");
                return;
            }

            bool wasMinimized = NativeMethods.IsIconic(hwnd);
            bool maximized;
            int x, y, width, height;

            // Restaurar una ventana minimizada o maximizada se anima en el monitor de origen: mientras dura
            // el cambio, la ventana se hace invisible y solo aparece ya colocada en la pantalla de destino.
            using (wasMinimized || NativeMethods.IsZoomed(hwnd) ? HideWhileMoving(hwnd) : null)
            {
                if (wasMinimized) NativeMethods.ShowWindow(hwnd, NativeMethods.SW_RESTORE);

                maximized = NativeMethods.IsZoomed(hwnd);
                if (maximized) NativeMethods.ShowWindow(hwnd, NativeMethods.SW_RESTORE);

                // Ya en estado normal: su rectángulo real en pantalla.
                NativeMethods.GetWindowRect(hwnd, out var rect);
                var source = System.Windows.Forms.Screen.FromRectangle(Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom)).WorkingArea;
                width = Math.Min(rect.Right - rect.Left, targetWorkArea.Width);
                height = Math.Min(rect.Bottom - rect.Top, targetWorkArea.Height);
                x = targetWorkArea.Left + Math.Clamp(rect.Left - source.Left, 0, targetWorkArea.Width - width);
                y = targetWorkArea.Top + Math.Clamp(rect.Top - source.Top, 0, targetWorkArea.Height - height);

                NativeMethods.SetWindowPos(hwnd, IntPtr.Zero, x, y, width, height,
                    NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE);
                if (maximized) NativeMethods.ShowWindow(hwnd, NativeMethods.SW_SHOWMAXIMIZED);
            }

            string state = maximized ? "maximizada" : wasMinimized ? "minimizada → restaurada" : "normal";
            string landed = System.Windows.Forms.Screen.FromHandle(hwnd).DeviceName;
            DiagLog.Write($"{tag} '{appName}' ventana en otro monitor ({current.DeviceName}) → movida a {targetName} " +
                          $"({x},{y},{width}x{height}) {state}, quedó en {landed}");
        }

        /// <summary>
        /// Hace la ventana invisible (layered con alfa 0) hasta que se libera el resultado, y entonces le devuelve
        /// su estilo original. No hace nada (null) si ya era layered, porque se podría romper cómo se dibuja, ni si
        /// Windows no deja cambiarla (p. ej. apps como administrador): entonces se mueve visible, como antes.
        /// </summary>
        private static IDisposable HideWhileMoving(IntPtr hwnd)
        {
            IntPtr original = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE);
            if ((original.ToInt64() & NativeMethods.WS_EX_LAYERED) != 0) return null;

            NativeMethods.SetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE, new IntPtr(original.ToInt64() | NativeMethods.WS_EX_LAYERED));
            if (!NativeMethods.SetLayeredWindowAttributes(hwnd, 0, 0, NativeMethods.LWA_ALPHA))
            {
                NativeMethods.SetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE, original);
                return null;
            }
            return new RestoreStyle(hwnd, original);
        }

        private sealed class RestoreStyle : IDisposable
        {
            private readonly IntPtr _hwnd;
            private readonly IntPtr _style;

            public RestoreStyle(IntPtr hwnd, IntPtr style) { _hwnd = hwnd; _style = style; }

            public void Dispose()
            {
                NativeMethods.SetWindowLongPtr(_hwnd, NativeMethods.GWL_EXSTYLE, _style);
                NativeMethods.RedrawWindow(_hwnd, IntPtr.Zero, IntPtr.Zero,
                    NativeMethods.RDW_ERASE | NativeMethods.RDW_INVALIDATE | NativeMethods.RDW_FRAME | NativeMethods.RDW_ALLCHILDREN);
            }
        }

        public void Dispose()
        {
            _poll.Stop();
            _pending.Clear();
        }
    }
}
