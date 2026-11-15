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

        /// <summary>Instantánea de ventanas visibles, para tomarla ANTES de abrir el panel.</summary>
        public static HashSet<IntPtr> Snapshot() => new(VisibleWindows());

        public static void EnsureOnMonitor(TaskbarWindow host, HashSet<IntPtr> before, string action)
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
                    if (before.Contains(hwnd) || !handled.Add(hwnd)) continue;

                    string process = ProcessName(hwnd);
                    if (!ShellProcesses.Contains(process)) continue;

                    NativeMethods.GetWindowRect(hwnd, out var rect);
                    var source = System.Windows.Forms.Screen.FromHandle(hwnd).Bounds;
                    bool wrongMonitor = !source.Equals(target);

                    DiagLog.Write($"[{action}] ventana nueva {process}/{ClassName(hwnd)} rect=({rect.Left},{rect.Top},{rect.Right},{rect.Bottom}) " +
                                  $"monitor={(wrongMonitor ? "OTRO" : "correcto")}");

                    if (!wrongMonitor) continue;

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
                    if (handled.Count == 0) DiagLog.Write($"[{action}] no apareció ninguna ventana nueva del shell");
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

        private static string ClassName(IntPtr hwnd)
        {
            var sb = new StringBuilder(256);
            NativeMethods.GetClassName(hwnd, sb, sb.Capacity);
            return sb.ToString();
        }
    }
}
