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
        // Procesos que dibujan los paneles del shell de Windows 11.
        private static readonly HashSet<string> ShellProcesses = new(StringComparer.OrdinalIgnoreCase)
        {
            "ShellExperienceHost", "StartMenuExperienceHost", "SearchHost", "SearchApp",
            "Widgets", "WidgetBoard", "ShellHost", "explorer", "msedgewebview2"
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

                    // Misma posición relativa respecto al borde inferior-derecho/izquierdo del monitor de destino.
                    int width = rect.Right - rect.Left;
                    int height = rect.Bottom - rect.Top;
                    int offsetX = rect.Left - source.Left;
                    int offsetFromBottom = source.Bottom - rect.Bottom;
                    int x = target.Left + Math.Clamp(offsetX, 0, Math.Max(0, target.Width - width));
                    int y = target.Bottom - offsetFromBottom - height;
                    y = Math.Clamp(y, target.Top, Math.Max(target.Top, target.Bottom - height));

                    bool moved = NativeMethods.SetWindowPos(hwnd, IntPtr.Zero, x, y, 0, 0,
                        NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE);
                    NativeMethods.GetWindowRect(hwnd, out var after);
                    DiagLog.Write($"[{action}]   movida a ({x},{y}) resultado={moved} rect final=({after.Left},{after.Top})");
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
                if (!NativeMethods.IsWindowVisible(hwnd)) return true;
                NativeMethods.DwmGetWindowAttribute(hwnd, NativeMethods.DWMWA_CLOAKED, out int cloaked, sizeof(int));
                if (cloaked != 0) return true;
                NativeMethods.GetWindowRect(hwnd, out var r);
                if (r.Right - r.Left < 50 || r.Bottom - r.Top < 50) return true;
                list.Add(hwnd);
                return true;
            }, IntPtr.Zero);
            return list;
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
