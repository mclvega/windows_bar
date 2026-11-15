using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Threading;
using Microsoft.Win32;

namespace WindowsBar.Services
{
    /// <summary>
    /// Detecta si alguna app está usando el micrófono, igual que el indicador de privacidad de Windows:
    /// en CapabilityAccessManager, una entrada con LastUsedTimeStart &gt; 0 y LastUsedTimeStop == 0 está en uso.
    /// </summary>
    public class MicUsageService : ObservableObject, IDisposable
    {
        private const string ConsentStore = @"Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\microphone";

        private readonly DispatcherTimer _timer;
        private bool _inUse;
        private string _toolTip = "";

        public bool InUse { get => _inUse; private set => Set(ref _inUse, value); }
        public string ToolTip { get => _toolTip; private set => Set(ref _toolTip, value); }

        public MicUsageService(Dispatcher dispatcher)
        {
            _timer = new DispatcherTimer(TimeSpan.FromSeconds(2), DispatcherPriority.Background, (_, _) => Refresh(), dispatcher);
            Refresh();
        }

        private void Refresh()
        {
            var apps = new List<string>();
            try
            {
                using var root = Registry.CurrentUser.OpenSubKey(ConsentStore);
                if (root != null)
                {
                    Scan(root, apps, packaged: true);
                    using var nonPackaged = root.OpenSubKey("NonPackaged");
                    if (nonPackaged != null) Scan(nonPackaged, apps, packaged: false);
                }
            }
            catch { }

            InUse = apps.Count > 0;
            ToolTip = apps.Count switch
            {
                0 => "",
                1 => $"{apps[0]} está usando el micrófono",
                _ => $"{apps.Count} aplicaciones están usando el micrófono:\n" + string.Join("\n", apps)
            };
        }

        private static void Scan(RegistryKey parent, List<string> apps, bool packaged)
        {
            foreach (string name in parent.GetSubKeyNames())
            {
                if (packaged && name == "NonPackaged") continue;

                using var key = parent.OpenSubKey(name);
                if (key == null) continue;

                long start = Convert.ToInt64(key.GetValue("LastUsedTimeStart") ?? 0L);
                long stop = Convert.ToInt64(key.GetValue("LastUsedTimeStop") ?? 0L);
                if (start > 0 && stop == 0)
                {
                    apps.Add(packaged
                        ? name.Split('_')[0].Split('.').Last()
                        : Path.GetFileNameWithoutExtension(name.Replace('#', '\\')));
                }
            }
        }

        public void Dispose() => _timer.Stop();
    }
}
