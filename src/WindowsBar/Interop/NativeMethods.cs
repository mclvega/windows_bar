using System;
using System.Runtime.InteropServices;
using System.Text;

namespace WindowsBar.Interop
{
    internal static class NativeMethods
    {
        // ---------- Ventanas ----------
        [DllImport("user32.dll")]
        public static extern IntPtr GetShellWindow();

        [DllImport("user32.dll")]
        public static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern IntPtr FindWindow(string className, string windowName);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern IntPtr FindWindowEx(IntPtr parent, IntPtr childAfter, string className, string windowName);

        [DllImport("user32.dll")]
        public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        public const int SW_SHOWNA = 8;

        // ---------- Teclado ----------
        [DllImport("user32.dll")]
        public static extern IntPtr GetKeyboardLayout(uint threadId);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

        public const uint INPUT_KEYBOARD = 1;
        public const uint KEYEVENTF_KEYUP = 0x0002;

        [StructLayout(LayoutKind.Sequential)]
        public struct INPUT
        {
            public uint type;
            public InputUnion u;
        }

        [StructLayout(LayoutKind.Explicit)]
        public struct InputUnion
        {
            [FieldOffset(0)] public MOUSEINPUT mi;
            [FieldOffset(0)] public KEYBDINPUT ki;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct MOUSEINPUT
        {
            public int dx, dy;
            public uint mouseData, dwFlags, time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct KEYBDINPUT
        {
            public ushort wVk, wScan;
            public uint dwFlags, time;
            public IntPtr dwExtraInfo;
        }

        // ---------- AppBar (estado de la barra nativa) ----------
        [StructLayout(LayoutKind.Sequential)]
        public struct APPBARDATA
        {
            public int cbSize;
            public IntPtr hWnd;
            public uint uCallbackMessage;
            public uint uEdge;
            public RECT rc;
            public IntPtr lParam;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT { public int Left, Top, Right, Bottom; }

        public const uint ABM_SETSTATE = 0x0000000A;

        [DllImport("shell32.dll")]
        public static extern uint SHAppBarMessage(uint dwMessage, ref APPBARDATA pData);

        // ---------- Shell: rutas de carpetas conocidas ----------
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        public static extern int SHGetKnownFolderPath([MarshalAs(UnmanagedType.LPStruct)] Guid rfid, uint flags, IntPtr token, out IntPtr path);

        // ---------- Shell: iconos e información de elementos ----------
        [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
        public static extern void SHCreateItemFromParsingName(string path, IntPtr pbc, [MarshalAs(UnmanagedType.LPStruct)] Guid riid, [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory factory);

        [ComImport, Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        public interface IShellItemImageFactory
        {
            [PreserveSig]
            int GetImage(SIZE size, int flags, out IntPtr phbm);
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct SIZE { public int cx, cy; }

        public const int SIIGBF_ICONONLY = 0x4;
        public const int SIIGBF_BIGGERSIZEOK = 0x1;

        [DllImport("gdi32.dll")]
        public static extern bool DeleteObject(IntPtr hObject);

        // ---------- Propiedades de accesos directos (AppUserModelID) ----------
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        public static extern int SHGetPropertyStoreFromParsingName(string pszPath, IntPtr pbc, int flags, [MarshalAs(UnmanagedType.LPStruct)] Guid riid, out IPropertyStore ppv);

        [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        public interface IPropertyStore
        {
            [PreserveSig] int GetCount(out uint cProps);
            [PreserveSig] int GetAt(uint iProp, out PROPERTYKEY pkey);
            [PreserveSig] int GetValue(ref PROPERTYKEY key, out PROPVARIANT pv);
            [PreserveSig] int SetValue(ref PROPERTYKEY key, ref PROPVARIANT pv);
            [PreserveSig] int Commit();
        }

        [StructLayout(LayoutKind.Sequential, Pack = 4)]
        public struct PROPERTYKEY
        {
            public Guid fmtid;
            public uint pid;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct PROPVARIANT
        {
            public ushort vt;
            public ushort r1, r2, r3;
            public IntPtr p;
            public IntPtr p2;
        }

        [DllImport("ole32.dll")]
        public static extern int PropVariantClear(ref PROPVARIANT pvar);

        public const ushort VT_LPWSTR = 31;

        public static readonly PROPERTYKEY PKEY_AppUserModel_ID = new PROPERTYKEY
        {
            fmtid = new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"),
            pid = 5
        };

        public static readonly Guid IID_IPropertyStore = new Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99");
        public static readonly Guid IID_IShellItemImageFactory = new Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b");

        // ---------- Info de idioma ----------
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        public static extern int GetLocaleInfoEx(string lpLocaleName, uint lcType, StringBuilder lpLCData, int cchData);
    }
}
