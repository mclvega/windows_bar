using System;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Text;
using ManagedShell.WindowsTasks;

namespace WindowsBar.Interop
{
    /// <summary>
    /// Identidad de la app de una ventana. Las apps empaquetadas (Microsoft Store, WinUI: WhatsApp,
    /// Teams...) a menudo no declaran AppUserModelID en la ventana ni tienen título; su identidad real
    /// es la del paquete del proceso ("5319275A.WhatsAppDesktop_cv1g1gvanyjgm!App").
    /// </summary>
    internal static class AppIdentity
    {
        private static readonly ConcurrentDictionary<uint, string> PackageAumidByPid = new();
        private static readonly ConcurrentDictionary<string, string> DisplayNameByAumid = new();

        /// <summary>
        /// AUMID de la app. Si el proceso es empaquetado y la ventana no declara un AUMID de paquete
        /// (WhatsApp declara uno propio), manda el del paquete; si no, el declarado en la ventana.
        /// </summary>
        public static string GetAumid(ApplicationWindow window)
        {
            string declared = null;
            try { declared = window.AppUserModelID; } catch { }

            string package = GetPackageAumid(window);
            if (package != null && !IsPackaged(declared)) return package;

            return string.IsNullOrEmpty(declared) ? package : declared;
        }

        /// <summary>AUMID del paquete del proceso, o null si no es una app empaquetada.</summary>
        public static string GetPackageAumid(ApplicationWindow window)
        {
            uint? pid = null;
            try { pid = window.ProcId; } catch { }
            if (pid is null or 0) return null;

            return PackageAumidByPid.GetOrAdd(pid.Value, id =>
            {
                IntPtr process = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, id);
                if (process == IntPtr.Zero) return null;
                try
                {
                    uint length = 512;
                    var sb = new StringBuilder((int)length);
                    return GetApplicationUserModelId(process, ref length, sb) == 0 ? sb.ToString() : null;
                }
                finally
                {
                    CloseHandle(process);
                }
            });
        }

        /// <summary>Un AUMID de paquete tiene la forma "Familia_editor!IdApp".</summary>
        public static bool IsPackaged(string aumid) => !string.IsNullOrEmpty(aumid) && aumid.Contains('!');

        public static string AppsFolderPath(string aumid) => @"shell:AppsFolder\" + aumid;

        /// <summary>Nombre visible de la app según Windows ("WhatsApp"), o null.</summary>
        public static string GetDisplayName(string aumid)
        {
            if (string.IsNullOrEmpty(aumid)) return null;
            return DisplayNameByAumid.GetOrAdd(aumid, id => ShellItemHelper.GetDisplayName(AppsFolderPath(id)));
        }

        /// <summary>
        /// Cómo pide la ventana ser relanzada (lo que usa la barra de Windows al anclarla). Chrome lo usa
        /// para los perfiles: "chrome.exe --profile-directory=..." con el nombre y el icono del perfil.
        /// Null si la ventana no declara comando de relanzamiento.
        /// </summary>
        public static RelaunchInfo GetRelaunchInfo(ApplicationWindow window)
        {
            IntPtr hwnd;
            try { hwnd = window.Handle; } catch { return null; }
            if (hwnd == IntPtr.Zero) return null;

            try
            {
                if (NativeMethods.SHGetPropertyStoreForWindow(hwnd, NativeMethods.IID_IPropertyStore, out var store) != 0 || store == null)
                    return null;

                try
                {
                    string command = GetString(store, NativeMethods.PKEY_AppUserModel_RelaunchCommand);
                    if (string.IsNullOrWhiteSpace(command)) return null;

                    // Igual que la barra de Windows: sin nombre de relanzamiento, el comando se ignora.
                    string name = ResolveIndirect(GetString(store, NativeMethods.PKEY_AppUserModel_RelaunchDisplayNameResource));
                    if (string.IsNullOrWhiteSpace(name)) return null;

                    var (file, args) = SplitCommandLine(command);
                    if (string.IsNullOrEmpty(file)) return null;

                    return new RelaunchInfo
                    {
                        File = file,
                        Arguments = args,
                        DisplayName = name,
                        IconPath = StripIconIndex(GetString(store, NativeMethods.PKEY_AppUserModel_RelaunchIconResource))
                    };
                }
                finally
                {
                    Marshal.ReleaseComObject(store);
                }
            }
            catch
            {
                return null;
            }
        }

        private static string GetString(NativeMethods.IPropertyStore store, NativeMethods.PROPERTYKEY key)
        {
            if (store.GetValue(ref key, out var pv) != 0) return null;
            try
            {
                return pv.vt == NativeMethods.VT_LPWSTR ? Marshal.PtrToStringUni(pv.p) : null;
            }
            finally
            {
                NativeMethods.PropVariantClear(ref pv);
            }
        }

        /// <summary>"\"C:\x\chrome.exe\" --profile-directory=Default" → ("C:\x\chrome.exe", "--profile-directory=Default").</summary>
        private static (string file, string args) SplitCommandLine(string command)
        {
            command = command.Trim();
            int end;
            string file;
            if (command.StartsWith("\""))
            {
                end = command.IndexOf('"', 1);
                if (end < 0) return (command.Trim('"'), null);
                file = command.Substring(1, end - 1);
                end++;
            }
            else
            {
                end = command.IndexOf(' ');
                if (end < 0) return (command, null);
                file = command.Substring(0, end);
            }

            string args = command.Substring(end).Trim();
            return (file, args.Length == 0 ? null : args);
        }

        /// <summary>Nombres tipo "@recurso" se resuelven con Windows; el resto se devuelve tal cual.</summary>
        private static string ResolveIndirect(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            if (!text.StartsWith("@")) return text;

            var sb = new StringBuilder(512);
            return NativeMethods.SHLoadIndirectString(text, sb, sb.Capacity, IntPtr.Zero) == 0 ? sb.ToString() : null;
        }

        /// <summary>"C:\x\perfil.ico,0" → "C:\x\perfil.ico" (solo si el archivo existe).</summary>
        private static string StripIconIndex(string resource)
        {
            if (string.IsNullOrWhiteSpace(resource)) return null;
            string path = resource.Trim().Trim('"');
            int comma = path.LastIndexOf(',');
            if (comma > 0 && int.TryParse(path.Substring(comma + 1).Trim(), out _)) path = path.Substring(0, comma);
            path = Environment.ExpandEnvironmentVariables(path);
            return System.IO.File.Exists(path) ? path : null;
        }

        private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

        [DllImport("kernel32.dll")]
        private static extern IntPtr OpenProcess(uint access, bool inherit, uint processId);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetApplicationUserModelId(IntPtr process, ref uint length, StringBuilder aumid);

        [DllImport("kernel32.dll")]
        private static extern bool CloseHandle(IntPtr handle);
    }

    /// <summary>Comando de relanzamiento declarado por una ventana (ver <see cref="AppIdentity.GetRelaunchInfo"/>).</summary>
    internal class RelaunchInfo
    {
        public string File { get; set; }
        public string Arguments { get; set; }
        public string DisplayName { get; set; }
        public string IconPath { get; set; }
    }
}
