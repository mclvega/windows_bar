using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using ManagedShell.WindowsTasks;

namespace WindowsBar.Interop
{
    /// <summary>
    /// Detecta ventanas de incógnito / InPrivate / navegación privada. En Chromium (Chrome, Brave, Edge) el título
    /// es igual que el de una ventana normal; lo que cambia es el nombre accesible de su BrowserRootView, que
    /// termina en "(incógnito)". En Firefox lo indica el propio título.
    /// </summary>
    internal static class PrivateWindowDetector
    {
        private const string ChromiumClass = "Chrome_WidgetWin_1";
        private const string ChromiumRootView = "BrowserRootView";

        // Al final del nombre, para no confundirlo con páginas cuyo título hable de "privacidad".
        private static readonly Regex ChromiumPrivate = new(@"\((inc[oó]gnit[oa]|inprivate)\)\s*$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static readonly Regex FirefoxPrivate = new(@"(private browsing|navegación privada)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        /// <summary>Las comprobaciones con UI Automation, de una en una (en paralelo fallan con E_FAIL).</summary>
        private static readonly System.Threading.SemaphoreSlim UiaLock = new(1, 1);

        /// <summary>null = comprobación en curso.</summary>
        private static readonly ConcurrentDictionary<IntPtr, bool?> Cache = new();

        /// <summary>Se ha detectado una ventana privada (en el hilo de la interfaz): hay que reordenar.</summary>
        public static event EventHandler Detected;

        /// <summary>true si la ventana es privada. Mientras no se sabe devuelve false y lo comprueba en segundo plano.</summary>
        public static bool IsPrivate(ApplicationWindow window)
        {
            IntPtr hwnd = window.Handle;
            if (Cache.TryGetValue(hwnd, out var known)) return known == true;

            Cache[hwnd] = null;
            Prune();

            string className = GetClassName(hwnd);
            string title = null;
            try { title = window.Title; } catch { }

            if (className != ChromiumClass)
            {
                bool isPrivate = className == "MozillaWindowClass" && title != null && FirefoxPrivate.IsMatch(title);
                Cache[hwnd] = isPrivate;
                return isPrivate;
            }

            // UI Automation llama al otro proceso: en segundo plano para no frenar la barra.
            Task.Run(async () =>
            {
                // Recién abierta, la vista del navegador puede no existir aún, y UI Automation falla a veces
                // (E_FAIL) en la primera llamada: se reintenta unas veces.
                bool? found = null;
                for (int attempt = 0; attempt < 10 && found == null && NativeMethods.IsWindow(hwnd); attempt++)
                {
                    if (attempt > 0) await Task.Delay(500);
                    await UiaLock.WaitAsync();
                    try { found = IsChromiumPrivate(hwnd); }
                    finally { UiaLock.Release(); }
                }
                bool isPrivate = found == true;
                Cache[hwnd] = isPrivate;
                if (isPrivate)
                {
                    Services.DiagLog.Write($"[Incógnito] '{title}' detectada como ventana privada");
                    Application.Current?.Dispatcher.BeginInvoke(new Action(() => Detected?.Invoke(null, EventArgs.Empty)));
                }
            });
            return false;
        }

        /// <summary>true/false según el nombre de su BrowserRootView; null si no la tiene (aún, o no es un navegador) o si falla.</summary>
        private static bool? IsChromiumPrivate(IntPtr hwnd)
        {
            try
            {
                var walker = TreeWalker.RawViewWalker;
                var child = walker.GetFirstChild(AutomationElement.FromHandle(hwnd));
                while (child != null)
                {
                    if (child.Current.ClassName == ChromiumRootView)
                        return ChromiumPrivate.IsMatch(child.Current.Name ?? "");
                    child = walker.GetNextSibling(child);
                }
            }
            catch
            {
                // Ventana cerrada o que no responde: se vuelve a intentar.
            }
            return null;
        }

        private static string GetClassName(IntPtr hwnd)
        {
            var sb = new StringBuilder(256);
            return NativeMethods.GetClassName(hwnd, sb, sb.Capacity) > 0 ? sb.ToString() : "";
        }

        /// <summary>Olvida las ventanas que ya no existen.</summary>
        private static void Prune()
        {
            foreach (var hwnd in Cache.Keys.Where(h => !NativeMethods.IsWindow(h)).ToList())
                Cache.TryRemove(hwnd, out _);
        }
    }
}
