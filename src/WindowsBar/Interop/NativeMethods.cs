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

        // ---------- Procesos ----------
        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern IntPtr OpenProcess(uint access, bool inheritHandle, int processId);

        public const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern bool QueryFullProcessImageName(IntPtr process, int flags, StringBuilder exeName, ref int size);

        [DllImport("kernel32.dll")]
        public static extern bool CloseHandle(IntPtr handle);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern IntPtr FindWindow(string className, string windowName);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern IntPtr FindWindowEx(IntPtr parent, IntPtr childAfter, string className, string windowName);

        [DllImport("user32.dll")]
        public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        public static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern bool AllowSetForegroundWindow(uint processId);

        public const uint ASFW_ANY = unchecked((uint)-1);

        [DllImport("user32.dll")]
        public static extern bool IsWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern bool IsIconic(IntPtr hWnd);

        /// <summary>true si la ventana está maximizada.</summary>
        [DllImport("user32.dll")]
        public static extern bool IsZoomed(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);

        [DllImport("user32.dll")]
        public static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);

        public const uint SWP_NOSIZE = 0x0001;
        public const uint SWP_NOZORDER = 0x0004;
        public const uint SWP_NOACTIVATE = 0x0010;

        public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll")]
        public static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern int GetClassName(IntPtr hWnd, StringBuilder name, int max);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int max);

        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
        public static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int index);

        public const int GWL_EXSTYLE = -20;
        public const long WS_EX_TOPMOST = 0x00000008;
        public const long WS_EX_TOOLWINDOW = 0x00000080;

        /// <summary>Clase de ventana de los menús emergentes Win32 (TrackPopupMenu).</summary>
        public const string MenuWindowClass = "#32768";

        /// <summary>
        /// Ventana bajo un punto de la pantalla. A diferencia de EnumWindows/FindWindow, también encuentra
        /// las ventanas de las bandas del shell (p. ej. la Configuración rápida de Windows 11).
        /// </summary>
        [DllImport("user32.dll")]
        public static extern IntPtr WindowFromPoint(POINT point);

        [DllImport("user32.dll")]
        public static extern IntPtr GetAncestor(IntPtr hWnd, uint flags);

        public const uint GA_ROOT = 2;

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT { public int X, Y; }

        [DllImport("user32.dll")]
        public static extern bool GetWindowPlacement(IntPtr hWnd, ref WINDOWPLACEMENT placement);

        [DllImport("user32.dll")]
        public static extern bool SetWindowPlacement(IntPtr hWnd, ref WINDOWPLACEMENT placement);

        public const int SW_SHOWMINIMIZED = 2;
        public const int SW_SHOWMAXIMIZED = 3;
        public const int SW_RESTORE = 9;

        [StructLayout(LayoutKind.Sequential)]
        public struct WINDOWPLACEMENT
        {
            public int length;
            public int flags;
            public int showCmd;
            public POINT ptMinPosition;
            public POINT ptMaxPosition;
            public RECT rcNormalPosition;
        }

        // ---------- Abrir apps en un monitor concreto ----------
        [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern bool ShellExecuteEx(ref SHELLEXECUTEINFO info);

        public const uint SEE_MASK_FLAG_NO_UI = 0x00000400;
        public const uint SEE_MASK_HMONITOR = 0x00200000;
        public const int SW_SHOWNORMAL = 1;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct SHELLEXECUTEINFO
        {
            public int cbSize;
            public uint fMask;
            public IntPtr hwnd;
            public string lpVerb;
            public string lpFile;
            public string lpParameters;
            public string lpDirectory;
            public int nShow;
            public IntPtr hInstApp;
            public IntPtr lpIDList;
            public string lpClass;
            public IntPtr hkeyClass;
            public uint dwHotKey;
            /// <summary>hIcon o, con SEE_MASK_HMONITOR, el monitor donde debe abrirse la app.</summary>
            public IntPtr hIconOrMonitor;
            public IntPtr hProcess;
        }

        [DllImport("user32.dll")]
        public static extern IntPtr MonitorFromWindow(IntPtr hWnd, uint flags);

        public const uint MONITOR_DEFAULTTONEAREST = 2;

        [DllImport("dwmapi.dll")]
        public static extern int DwmGetWindowAttribute(IntPtr hWnd, int attr, out int value, int size);

        public const int DWMWA_CLOAKED = 14;

        // ---------- Miniaturas DWM (vistas previas de ventanas) ----------
        [DllImport("dwmapi.dll")]
        public static extern int DwmSetWindowAttribute(IntPtr hWnd, int attr, ref int value, int size);

        public const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
        public const int DWMWCP_ROUND = 2;

        [DllImport("dwmapi.dll")]
        public static extern int DwmRegisterThumbnail(IntPtr destination, IntPtr source, out IntPtr thumbnail);

        [DllImport("dwmapi.dll")]
        public static extern int DwmUnregisterThumbnail(IntPtr thumbnail);

        [DllImport("dwmapi.dll")]
        public static extern int DwmUpdateThumbnailProperties(IntPtr thumbnail, ref DWM_THUMBNAIL_PROPERTIES properties);

        [DllImport("dwmapi.dll")]
        public static extern int DwmQueryThumbnailSourceSize(IntPtr thumbnail, out SIZE size);

        public const uint DWM_TNP_RECTDESTINATION = 0x01;
        public const uint DWM_TNP_OPACITY = 0x04;
        public const uint DWM_TNP_VISIBLE = 0x08;
        public const uint DWM_TNP_SOURCECLIENTAREAONLY = 0x10;

        [StructLayout(LayoutKind.Sequential)]
        public struct DWM_THUMBNAIL_PROPERTIES
        {
            public uint dwFlags;
            public RECT rcDestination;
            public RECT rcSource;
            public byte opacity;
            [MarshalAs(UnmanagedType.Bool)] public bool fVisible;
            [MarshalAs(UnmanagedType.Bool)] public bool fSourceClientAreaOnly;
        }

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

        // Datos con que la ventana pide ser relanzada/anclada (Chrome: perfil, nombre e icono del perfil).
        public static readonly PROPERTYKEY PKEY_AppUserModel_RelaunchCommand = new PROPERTYKEY
        {
            fmtid = new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"),
            pid = 2
        };

        public static readonly PROPERTYKEY PKEY_AppUserModel_RelaunchIconResource = new PROPERTYKEY
        {
            fmtid = new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"),
            pid = 3
        };

        public static readonly PROPERTYKEY PKEY_AppUserModel_RelaunchDisplayNameResource = new PROPERTYKEY
        {
            fmtid = new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"),
            pid = 4
        };

        [DllImport("shell32.dll")]
        public static extern int SHGetPropertyStoreForWindow(IntPtr hwnd, [MarshalAs(UnmanagedType.LPStruct)] Guid riid, out IPropertyStore ppv);

        [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)]
        public static extern int SHLoadIndirectString(string pszSource, StringBuilder pszOutBuf, int cchOutBuf, IntPtr ppvReserved);

        public static readonly Guid IID_IPropertyStore =new Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99");
        public static readonly Guid IID_IShellItemImageFactory = new Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b");

        // ---------- Info de idioma ----------
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        public static extern int GetLocaleInfoEx(string lpLocaleName, uint lcType, StringBuilder lpLCData, int cchData);
    }
}
