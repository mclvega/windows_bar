using System;
using System.Runtime.InteropServices;

namespace WindowsBar.Interop
{
    /// <summary>
    /// Restauración de la barra de tareas de Explorer independiente de ManagedShell,
    /// para poder usarla incluso si la app no llegó a inicializarse.
    /// </summary>
    internal static class NativeTaskbar
    {
        public static void Restore(int abState)
        {
            IntPtr tray = NativeMethods.FindWindow("Shell_TrayWnd", null);
            if (tray != IntPtr.Zero)
            {
                var abd = new NativeMethods.APPBARDATA
                {
                    cbSize = Marshal.SizeOf<NativeMethods.APPBARDATA>(),
                    hWnd = tray,
                    lParam = (IntPtr)abState
                };
                NativeMethods.SHAppBarMessage(NativeMethods.ABM_SETSTATE, ref abd);
                NativeMethods.ShowWindow(tray, NativeMethods.SW_SHOWNA);
            }

            IntPtr secondary = NativeMethods.FindWindowEx(IntPtr.Zero, IntPtr.Zero, "Shell_SecondaryTrayWnd", null);
            while (secondary != IntPtr.Zero)
            {
                NativeMethods.ShowWindow(secondary, NativeMethods.SW_SHOWNA);
                secondary = NativeMethods.FindWindowEx(IntPtr.Zero, secondary, "Shell_SecondaryTrayWnd", null);
            }
        }
    }
}
