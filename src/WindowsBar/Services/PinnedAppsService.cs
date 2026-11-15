using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using ManagedShell.ShellFolders;
using ManagedShell.WindowsTasks;
using Microsoft.Win32;
using WindowsBar.Interop;

namespace WindowsBar.Services
{
    /// <summary>
    /// Apps ancladas. La primera vez se importan las de la barra de Windows
    /// (accesos directos en "User Pinned\TaskBar", en el orden guardado por Explorer).
    /// </summary>
    public class PinnedAppsService
    {
        private static readonly string WindowsPinnedFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            @"Microsoft\Internet Explorer\Quick Launch\User Pinned\TaskBar");

        private readonly SettingsService _settings;

        public event EventHandler Changed;

        public PinnedAppsService(SettingsService settings)
        {
            _settings = settings;

            if (!_settings.Data.PinsImported)
            {
                _settings.Data.PinnedApps = ImportFromWindows();
                _settings.Data.PinsImported = true;
                _settings.Save();
            }

            if (RepairPackagedPins()) _settings.Save();
        }

        /// <summary>
        /// Anclajes antiguos a un .exe dentro de WindowsApps (apps de la Store, p. ej. WhatsApp.Root.exe)
        /// se convierten a la app del paquete: se abren por su AUMID y muestran su nombre e icono reales.
        /// </summary>
        private bool RepairPackagedPins()
        {
            bool changed = false;
            foreach (var pin in _settings.Data.PinnedApps)
            {
                if (AppIdentity.IsPackaged(pin.Aumid)) continue;

                string path = pin.TargetPath ?? pin.LaunchPath;
                string family = PackageFamilyFromPath(path);
                if (family == null) continue;

                string aumid = FindAumidForFamily(family);
                if (aumid == null) continue;

                pin.Aumid = aumid;
                pin.LaunchPath = AppIdentity.AppsFolderPath(aumid);
                pin.Name = AppIdentity.GetDisplayName(aumid) ?? pin.Name;
                changed = true;
            }
            return changed;
        }

        /// <summary>"...\WindowsApps\5319275A.WhatsAppDesktop_2.2644.101.0_x64__cv1g1gvanyjgm\x.exe" → "5319275A.WhatsAppDesktop_cv1g1gvanyjgm".</summary>
        private static string PackageFamilyFromPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            const string marker = @"\WindowsApps\";
            int i = path.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (i < 0) return null;

            string folder = path.Substring(i + marker.Length).Split('\\')[0];
            string[] parts = folder.Split('_');   // nombre_versión_arquitectura_recurso_idEditor
            return parts.Length >= 5 ? parts[0] + "_" + parts[^1] : null;
        }

        /// <summary>Busca en "shell:AppsFolder" el AUMID de la app principal de una familia de paquete.</summary>
        private static string FindAumidForFamily(string family)
        {
            try
            {
                dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application"));
                dynamic folder = shell.NameSpace("shell:AppsFolder");
                foreach (dynamic item in folder.Items())
                {
                    string id = item.Path;
                    if (id != null && id.StartsWith(family + "!", StringComparison.OrdinalIgnoreCase)) return id;
                }
            }
            catch { }
            return null;
        }

        public IReadOnlyList<PinnedApp> Apps => _settings.Data.PinnedApps;

        public PinnedApp FindMatch(ApplicationWindow window)
        {
            return Apps.FirstOrDefault(p => Matches(p, window));
        }

        public static bool Matches(PinnedApp pin, ApplicationWindow window)
        {
            string windowAumid = AppIdentity.GetAumid(window);
            string windowPath = SafeGet(() => window.WinFileName);

            bool pinHasAumid = !string.IsNullOrEmpty(pin.Aumid);
            bool windowHasAumid = !string.IsNullOrEmpty(windowAumid);

            // Si ambos declaran AUMID, manda el AUMID (distingue p. ej. PWAs de Chrome de Chrome).
            if (pinHasAumid && windowHasAumid)
                return string.Equals(pin.Aumid, windowAumid, StringComparison.OrdinalIgnoreCase);

            return !string.IsNullOrEmpty(pin.TargetPath) && !string.IsNullOrEmpty(windowPath)
                   && string.Equals(Path.GetFullPath(pin.TargetPath), Path.GetFullPath(windowPath), StringComparison.OrdinalIgnoreCase);
        }

        public void Pin(ApplicationWindow window)
        {
            if (FindMatch(window) != null) return;

            string aumid = AppIdentity.GetAumid(window);
            string path = SafeGet(() => window.WinFileName);

            // Las apps empaquetadas (Store/UWP) se lanzan por su AUMID, no por el .exe de WindowsApps.
            bool launchByAumid = (window.IsUWP || AppIdentity.IsPackaged(aumid)) && !string.IsNullOrEmpty(aumid);

            var pin = new PinnedApp
            {
                Aumid = aumid,
                TargetPath = window.IsUWP ? null : path,
                LaunchPath = launchByAumid ? AppIdentity.AppsFolderPath(aumid) : path,
                Name = GetAppName(window)
            };

            _settings.Data.PinnedApps.Add(pin);
            Commit();
        }

        public void Unpin(PinnedApp pin)
        {
            if (_settings.Data.PinnedApps.Remove(pin)) Commit();
        }

        public void Move(PinnedApp pin, int newIndex)
        {
            var list = _settings.Data.PinnedApps;
            int old = list.IndexOf(pin);
            if (old < 0) return;
            newIndex = Math.Clamp(newIndex, 0, list.Count - 1);
            if (old == newIndex) return;
            list.RemoveAt(old);
            list.Insert(newIndex, pin);
            Commit();
        }

        /// <summary>Sustituye las ancladas por las de la barra de Windows.</summary>
        public void ReimportFromWindows()
        {
            _settings.Data.PinnedApps = ImportFromWindows();
            Commit();
        }

        public static string GetAppName(ApplicationWindow window)
        {
            // Apps empaquetadas: el nombre que muestra Windows ("WhatsApp"), no el del .exe ("WhatsApp.Root").
            string aumid = AppIdentity.GetAumid(window);
            if (AppIdentity.IsPackaged(aumid))
            {
                string displayName = AppIdentity.GetDisplayName(aumid);
                if (!string.IsNullOrWhiteSpace(displayName)) return displayName;
            }

            string description = SafeGet(() => window.WinFileDescription);
            if (!string.IsNullOrWhiteSpace(description)) return description;

            string path = SafeGet(() => window.WinFileName);
            return string.IsNullOrEmpty(path) ? window.Title : Path.GetFileNameWithoutExtension(path);
        }

        private void Commit()
        {
            _settings.Save();
            Changed?.Invoke(this, EventArgs.Empty);
        }

        private static List<PinnedApp> ImportFromWindows()
        {
            var result = new List<PinnedApp>();
            if (!Directory.Exists(WindowsPinnedFolder)) return result;

            foreach (string lnk in Directory.GetFiles(WindowsPinnedFolder, "*.lnk"))
            {
                string target = null;
                try { target = ShellLinkHelper.GetLinkTarget(IntPtr.Zero, lnk); } catch { }

                result.Add(new PinnedApp
                {
                    LaunchPath = lnk,
                    TargetPath = string.IsNullOrEmpty(target) ? null : target,
                    Aumid = ShellItemHelper.GetAppUserModelId(lnk),
                    Name = Path.GetFileNameWithoutExtension(lnk)
                });
            }

            // Explorer guarda el orden en un blob binario (Taskband\Favorites) que contiene el nombre
            // de cada .lnk en UTF-16: ordenamos por la posición en que aparece cada nombre.
            byte[] favorites = null;
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Taskband");
                favorites = key?.GetValue("Favorites") as byte[];
            }
            catch { }

            if (favorites != null)
            {
                // El texto puede empezar en offset par o impar: probamos ambas alineaciones.
                string even = Encoding.Unicode.GetString(favorites);
                string odd = favorites.Length > 1 ? Encoding.Unicode.GetString(favorites, 1, favorites.Length - 1) : "";
                int Position(PinnedApp p)
                {
                    string name = Path.GetFileName(p.LaunchPath);
                    int a = even.IndexOf(name, StringComparison.OrdinalIgnoreCase);
                    int b = odd.IndexOf(name, StringComparison.OrdinalIgnoreCase);
                    int pa = a < 0 ? int.MaxValue : a * 2;
                    int pb = b < 0 ? int.MaxValue : b * 2 + 1;
                    return Math.Min(pa, pb);
                }

                result = result.OrderBy(Position).ThenBy(p => p.Name).ToList();
            }
            else
            {
                result = result.OrderBy(p => p.Name).ToList();
            }

            return result;
        }

        private static string SafeGet(Func<string> getter)
        {
            try { return getter(); }
            catch (Exception ex) { Debug.WriteLine(ex.Message); return null; }
        }
    }
}
