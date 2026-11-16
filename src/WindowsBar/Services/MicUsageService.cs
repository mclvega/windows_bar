using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Threading;
using Microsoft.Win32;
using WindowsBar.Interop;

namespace WindowsBar.Services
{
    /// <summary>
    /// Detecta si alguna app está usando el micrófono, igual que el indicador de privacidad de Windows:
    /// en CapabilityAccessManager, una entrada con LastUsedTimeStart &gt; 0 y LastUsedTimeStop == 0 está en uso,
    /// siempre que su app siga en marcha. Si la app se cerró mientras usaba el micrófono y luego se actualizó
    /// (Discord instala cada versión en una carpeta nueva), la entrada vieja queda sin LastUsedTimeStop para siempre.
    /// </summary>
    public class MicUsageService : ObservableObject, IDisposable
    {
        private const string ConsentStore = @"Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\microphone";

        private readonly DispatcherTimer _timer;
        private bool _inUse;
        private string _toolTip = "";

        public bool InUse { get => _inUse; private set => Set(ref _inUse, value); }
        public string ToolTip { get => _toolTip; private set => Set(ref _toolTip, value); }

        /// <summary>Entrada del registro marcada "en uso", pendiente de comprobar que su app sigue en marcha.</summary>
        private record Candidate(string KeyName, bool Packaged);

        public MicUsageService(Dispatcher dispatcher)
        {
            _timer = new DispatcherTimer(TimeSpan.FromSeconds(2), DispatcherPriority.Background, (_, _) => Refresh(), dispatcher);
            Refresh();
        }

        private void Refresh()
        {
            var candidates = new List<Candidate>();
            try
            {
                using var root = Registry.CurrentUser.OpenSubKey(ConsentStore);
                if (root != null)
                {
                    Scan(root, candidates, packaged: true);
                    using var nonPackaged = root.OpenSubKey("NonPackaged");
                    if (nonPackaged != null) Scan(nonPackaged, candidates, packaged: false);
                }
            }
            catch { }

            // Las rutas de los procesos solo se leen si hay alguna candidata (lo normal es que no haya).
            var apps = new List<string>();
            if (candidates.Count > 0)
            {
                var running = RunningImagePaths();
                foreach (var c in candidates.Where(c => IsRunning(c, running)))
                {
                    string name = DisplayName(c);
                    if (!apps.Contains(name, StringComparer.OrdinalIgnoreCase)) apps.Add(name);
                }
            }

            InUse = apps.Count > 0;
            ToolTip = apps.Count switch
            {
                0 => "",
                1 => $"{apps[0]} está usando el micrófono",
                _ => $"{apps.Count} aplicaciones están usando el micrófono:\n" + string.Join("\n", apps)
            };
        }

        private static void Scan(RegistryKey parent, List<Candidate> candidates, bool packaged)
        {
            foreach (string name in parent.GetSubKeyNames())
            {
                if (packaged && name == "NonPackaged") continue;

                using var key = parent.OpenSubKey(name);
                if (key == null) continue;

                long start = Convert.ToInt64(key.GetValue("LastUsedTimeStart") ?? 0L);
                long stop = Convert.ToInt64(key.GetValue("LastUsedTimeStop") ?? 0L);
                if (start > 0 && stop == 0) candidates.Add(new Candidate(name, packaged));
            }
        }

        private static string DisplayName(Candidate c) => c.Packaged
            ? c.KeyName.Split('_')[0].Split('.').Last()
            : Path.GetFileNameWithoutExtension(c.KeyName.Replace('#', '\\'));

        /// <summary>
        /// NonPackaged: algún proceso con exactamente esa ruta de ejecutable.
        /// Empaquetada ("Nombre_idEditor"): algún proceso dentro de "\WindowsApps\Nombre_versión_..._idEditor\".
        /// </summary>
        private static bool IsRunning(Candidate c, List<string> runningPaths)
        {
            if (!c.Packaged)
            {
                string exe = c.KeyName.Replace('#', '\\');
                return runningPaths.Any(p => string.Equals(p, exe, StringComparison.OrdinalIgnoreCase));
            }

            int sep = c.KeyName.LastIndexOf('_');
            if (sep <= 0) return false;
            string packageName = c.KeyName.Substring(0, sep);
            string publisherId = c.KeyName.Substring(sep + 1);

            const string marker = @"\WindowsApps\";
            foreach (string path in runningPaths)
            {
                int i = path.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
                if (i < 0) continue;
                string folder = path.Substring(i + marker.Length).Split('\\')[0]; // Nombre_versión_arq_recurso_idEditor
                if (folder.StartsWith(packageName + "_", StringComparison.OrdinalIgnoreCase)
                    && folder.EndsWith("_" + publisherId, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        /// <summary>Ruta del ejecutable de cada proceso en marcha (los que no se pueden leer se ignoran).</summary>
        private static List<string> RunningImagePaths()
        {
            var paths = new List<string>();
            var buffer = new StringBuilder(1024);
            foreach (var process in Process.GetProcesses())
            {
                using (process)
                {
                    IntPtr handle = NativeMethods.OpenProcess(NativeMethods.PROCESS_QUERY_LIMITED_INFORMATION, false, process.Id);
                    if (handle == IntPtr.Zero) continue;
                    try
                    {
                        int size = buffer.Capacity;
                        if (NativeMethods.QueryFullProcessImageName(handle, 0, buffer, ref size))
                            paths.Add(buffer.ToString(0, size));
                    }
                    finally
                    {
                        NativeMethods.CloseHandle(handle);
                    }
                }
            }
            return paths;
        }

        public void Dispose() => _timer.Stop();
    }
}
