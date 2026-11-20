using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Windows.Threading;
using WindowsBar.Interop;
using WindowsBar.Views;

namespace WindowsBar.Services
{
    /// <summary>
    /// Tras abrir un panel de Windows (Inicio, Búsqueda, Configuración rápida...), detecta la ventana
    /// que apareció y, si Windows la abrió en otro monitor, la traslada al monitor de la barra pulsada
    /// manteniendo su posición relativa (p. ej. abajo a la derecha). Todo queda en diag.log.
    /// </summary>
    internal static class FlyoutPositioner
    {
        // Procesos que dibujan los paneles del shell de Windows 11. NO incluye procesos genéricos
        // (msedgewebview2, apps normales...) para no mover nunca ventanas del usuario.
        private static readonly HashSet<string> ShellProcesses = new(StringComparer.OrdinalIgnoreCase)
        {
            "ShellExperienceHost", "StartMenuExperienceHost", "SearchHost", "SearchApp",
            "Widgets", "WidgetBoard", "ShellHost", "explorer"
        };

        // Clases de ventana de los paneles (además del proceso, para no tocar p. ej. ventanas del Explorador).
        private static readonly HashSet<string> FlyoutClasses = new(StringComparer.OrdinalIgnoreCase)
        {
            "Windows.UI.Core.CoreWindow", "XamlExplorerHostIslandWindow", "ControlCenterWindow",
            "Shell_InputSwitchTopLevelWindow", "TopLevelWindowForOverflowXamlIsland", "Xaml_WindowedPopupClass"
        };

        private const int PollMs = 50;
        private const int MaxPolls = 30; // 1,5 s

        /// <summary>Instantánea (ventana → rectángulo) de las ventanas visibles, tomada ANTES de abrir el panel.</summary>
        public static Dictionary<IntPtr, NativeMethods.RECT> Snapshot() =>
            VisibleWindows().ToDictionary(h => h, h => { NativeMethods.GetWindowRect(h, out var r); return r; });

        public static void EnsureOnMonitor(TaskbarWindow host, Dictionary<IntPtr, NativeMethods.RECT> before, string action)
        {
            if (host == null) return;

            var target = host.Screen.Bounds;
            int polls = 0;
            var handled = new HashSet<IntPtr>();

            var timer = new DispatcherTimer(DispatcherPriority.Normal, host.Dispatcher)
            {
                Interval = TimeSpan.FromMilliseconds(PollMs)
            };
            timer.Tick += (_, _) =>
            {
                polls++;
                foreach (IntPtr hwnd in VisibleWindows())
                {
                    NativeMethods.GetWindowRect(hwnd, out var rect);

                    // Candidata: ventana nueva, o ya existente que cambió de posición/tamaño (paneles que se deslizan).
                    bool isNew = !before.TryGetValue(hwnd, out var old);
                    bool changed = !isNew && (old.Left != rect.Left || old.Top != rect.Top || old.Right != rect.Right || old.Bottom != rect.Bottom);
                    if ((!isNew && !changed) || !handled.Add(hwnd)) continue;

                    string process = ProcessName(hwnd);
                    if (process == "WindowsBar") continue; // nuestras propias barras (p. ej. al auto-ocultarse)
                    var source = System.Windows.Forms.Screen.FromHandle(hwnd).Bounds;
                    bool wrongMonitor = !source.Equals(target);

                    DiagLog.Write($"[{action}] {(isNew ? "nueva" : "movida")} {process}/{ClassName(hwnd)} '{Title(hwnd)}' " +
                                  $"rect=({rect.Left},{rect.Top},{rect.Right},{rect.Bottom}) monitor={(wrongMonitor ? "OTRO" : "correcto")}");

                    if (!wrongMonitor || !ShellProcesses.Contains(process)) continue;
                    if (!FlyoutClasses.Contains(ClassName(hwnd)))
                    {
                        DiagLog.Write($"[{action}]   clase no reconocida como panel ({ClassName(hwnd)}): no se mueve");
                        continue;
                    }

                    // Misma posición relativa respecto al borde inferior-derecho/izquierdo del monitor de destino.
                    int width = rect.Right - rect.Left;
                    int height = rect.Bottom - rect.Top;
                    int offsetX = rect.Left - source.Left;
                    int offsetFromBottom = source.Bottom - rect.Bottom;
                    int x = target.Left + Math.Clamp(offsetX, 0, Math.Max(0, target.Width - width));
                    int y = target.Bottom - offsetFromBottom - height;

                    // Algunos paneles (Configuración rápida) son tan altos como el área de trabajo de su monitor,
                    // con el contenido pegado abajo. Si no caben en el destino se anclan abajo y sobresalen por
                    // arriba (zona vacía): pegarlos arriba deja el contenido bajo el monitor, y redimensionarlos
                    // no sirve porque el panel recorta su contenido según la altura con la que se creó.
                    bool fits = height <= target.Height - offsetFromBottom;
                    if (fits) y = Math.Clamp(y, target.Top, Math.Max(target.Top, target.Bottom - height));

                    bool moved = NativeMethods.SetWindowPos(hwnd, IntPtr.Zero, x, y, 0, 0,
                        NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE);
                    NativeMethods.GetWindowRect(hwnd, out var after);
                    DiagLog.Write($"[{action}]   movida a ({x},{y}){(fits ? "" : " anclada abajo (más alta que el monitor)")} " +
                                  $"resultado={moved} rect final=({after.Left},{after.Top},{after.Right},{after.Bottom})");
                }

                if (polls >= MaxPolls)
                {
                    timer.Stop();
                    if (handled.Count == 0) DiagLog.Write($"[{action}] ninguna ventana visible nueva ni movida en 1,5 s");
                }
            };
            timer.Start();
        }

        private static List<IntPtr> VisibleWindows()
        {
            var list = new List<IntPtr>();
            NativeMethods.EnumWindows((hwnd, _) =>
            {
                if (IsCandidate(hwnd)) list.Add(hwnd);
                return true;
            }, IntPtr.Zero);

            // EnumWindows no devuelve las ventanas de las bandas del shell (Configuración rápida...): se buscan por punto.
            foreach (IntPtr hwnd in ProbedWindows())
                if (!list.Contains(hwnd) && IsCandidate(hwnd)) list.Add(hwnd);

            return list;
        }

        private static bool IsCandidate(IntPtr hwnd)
        {
            if (!NativeMethods.IsWindowVisible(hwnd)) return false;
            NativeMethods.DwmGetWindowAttribute(hwnd, NativeMethods.DWMWA_CLOAKED, out int cloaked, sizeof(int));
            if (cloaked != 0) return false;
            NativeMethods.GetWindowRect(hwnd, out var r);
            return r.Right - r.Left >= 50 && r.Bottom - r.Top >= 50;
        }

        // Distancia al borde inferior de los puntos de sondeo: dentro de la zona donde aparecen los paneles.
        private const int ProbeFromBottom = 250;
        private const int ProbeFromSide = 150;

        /// <summary>
        /// Ventanas raíz bajo tres puntos de cada monitor, en la zona de los paneles: derecha (Configuración
        /// rápida, notificaciones), centro (Inicio y Búsqueda centrados) e izquierda (Inicio a la izquierda, Widgets).
        /// </summary>
        private static IEnumerable<IntPtr> ProbedWindows()
        {
            var found = new HashSet<IntPtr>();
            foreach (var screen in System.Windows.Forms.Screen.AllScreens)
            {
                var b = screen.Bounds;
                int y = b.Bottom - ProbeFromBottom;
                foreach (int x in new[] { b.Right - ProbeFromSide, b.Left + b.Width / 2, b.Left + ProbeFromSide })
                {
                    IntPtr hwnd = NativeMethods.GetAncestor(NativeMethods.WindowFromPoint(new NativeMethods.POINT { X = x, Y = y }), NativeMethods.GA_ROOT);
                    if (hwnd != IntPtr.Zero) found.Add(hwnd);
                }
            }
            return found;
        }

        private static string ProcessName(IntPtr hwnd)
        {
            try
            {
                NativeMethods.GetWindowThreadProcessId(hwnd, out uint pid);
                return Process.GetProcessById((int)pid).ProcessName;
            }
            catch
            {
                return "?";
            }
        }

        private static string Title(IntPtr hwnd)
        {
            var sb = new StringBuilder(256);
            NativeMethods.GetWindowText(hwnd, sb, sb.Capacity);
            return sb.ToString();
        }

        private static string ClassName(IntPtr hwnd)
        {
            var sb = new StringBuilder(256);
            NativeMethods.GetClassName(hwnd, sb, sb.Capacity);
            return sb.ToString();
        }
    }
}
