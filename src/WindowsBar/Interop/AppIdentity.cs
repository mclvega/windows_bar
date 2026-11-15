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

        private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

        [DllImport("kernel32.dll")]
        private static extern IntPtr OpenProcess(uint access, bool inherit, uint processId);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetApplicationUserModelId(IntPtr process, ref uint length, StringBuilder aumid);

        [DllImport("kernel32.dll")]
        private static extern bool CloseHandle(IntPtr handle);
    }
}
