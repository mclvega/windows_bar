using System;
using System.Runtime.InteropServices;
using ManagedShell.UWPInterop;

namespace WindowsBar.Interop
{
    /// <summary>
    /// Abre el menú Inicio en un monitor concreto mediante las interfaces internas del shell
    /// (IImmersiveLauncher + IImmersiveMonitorManager). Definiciones tomadas de RetroBar/CustomShell.
    /// </summary>
    internal static class ImmersiveLauncher
    {
        private static Guid CLSID_ImmersiveMonitorManager = new("47094e3a-0cf2-430f-806f-cf9e4f0f12dd");
        private static Guid IID_ImmersiveMonitorManager = new("4d4c1e64-e410-4faa-bafa-59ca069bfec2");
        private static Guid CLSID_ImmersiveLauncher = new("6f86e01c-c649-4d61-be23-f1322ddeca9d");
        private static Guid IID_ImmersiveLauncher = new("d8d60399-a0f1-f987-5551-321fd1b49864");

        /// <summary>Muestra Inicio en el monitor indicado. Devuelve false si no fue posible.</summary>
        public static bool ShowStart(IntPtr hMonitor)
        {
            try
            {
                ImmersiveShellHelper.AllowExplorerFocus();

                var shell = ImmersiveShellHelper.GetImmersiveShell();
                if (shell == null) return false;

                if (shell.QueryService(ref CLSID_ImmersiveLauncher, ref IID_ImmersiveLauncher, out object launcherObj) != 0) return false;
                var launcher = (IImmersiveLauncher)launcherObj;

                if (shell.QueryService(ref CLSID_ImmersiveMonitorManager, ref IID_ImmersiveMonitorManager, out object managerObj) != 0) return false;
                var manager = (IImmersiveMonitorManager)managerObj;

                if (manager.GetFromHandle(hMonitor, out IImmersiveMonitor monitor) != 0 || monitor == null) return false;

                // Si ya está abierto en ese monitor, el botón Inicio lo cierra (como en Windows).
                if (launcher.IsVisible(out bool visible) == 0 && visible
                    && launcher.GetMonitor(out IImmersiveMonitor current) == 0 && current != null
                    && current.GetHandle(out IntPtr currentHandle) == 0 && currentHandle == hMonitor)
                {
                    launcher.Dismiss(ILDM_GENERIC_NONANIMATING);
                    return true;
                }

                if (launcher.ConnectToMonitor(monitor) != 0) return false;
                return launcher.ShowStartView(ILSM_STARTBUTTON, ILSF_IGNORE_SET_FOREGROUND_ERROR) == 0;
            }
            catch
            {
                return false;
            }
        }

        private const int ILSM_STARTBUTTON = 0xB;
        private const int ILSF_IGNORE_SET_FOREGROUND_ERROR = 0x4;
        private const int ILDM_GENERIC_NONANIMATING = 0x7;

        [ComImport, Guid("880b26f8-9197-43d0-8045-8702d0d72000"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IImmersiveMonitor
        {
            int GetIdentity(out uint pIdentity);
            int Append(object unknown);
            int GetHandle(out IntPtr phMonitor);
            int IsConnected(out bool pfConnected);
            int IsPrimary(out bool pfPrimary);
            int GetTrustLevel(out uint level);
            int GetDisplayRect(out NativeMethods.RECT prcDisplayRect);
            int GetOrientation(out uint pdwOrientation);
            int GetWorkArea(out NativeMethods.RECT prcWorkArea);
            int IsEqual(IImmersiveMonitor pMonitor, out bool pfEqual);
            int GetTrustLevel2(out uint level);
            int GetEffectiveDpi(out uint dpiX, out uint dpiY);
            int GetFilterFlags(out int flags);
        }

        [ComImport, Guid("4d4c1e64-e410-4faa-bafa-59ca069bfec2"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IImmersiveMonitorManager
        {
            int GetCount(out uint pcMonitors);
            int GetConnectedCount(out uint pcMonitors);
            int GetAt(uint idxMonitor, out IImmersiveMonitor monitor);
            int GetFromHandle(IntPtr hMonitor, out IImmersiveMonitor monitor);
        }

        [ComImport, Guid("d8d60399-a0f1-f987-5551-321fd1b49864"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IImmersiveLauncher
        {
            int ShowStartView(int showMethod, int showFlags);
            int Dismiss(int dismissMethod);
            int Dismiss2(int dismissMethod);
            int DismissSynchronouslyWithoutTransition();
            int IsVisible(out bool visible);
            int OnStartButtonPressed(int showMethod, int dismissMethod);
            int SetForeground();
            int ConnectToMonitor(IImmersiveMonitor monitor);
            int GetMonitor(out IImmersiveMonitor monitor);
        }
    }
}
