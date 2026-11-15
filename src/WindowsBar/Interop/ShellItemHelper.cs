using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace WindowsBar.Interop
{
    internal static class ShellItemHelper
    {
        /// <summary>
        /// Icono de cualquier elemento del shell: .exe, .lnk o "shell:AppsFolder\AUMID".
        /// </summary>
        public static ImageSource GetIcon(string parsingName, int size = 32)
        {
            if (string.IsNullOrEmpty(parsingName)) return null;

            try
            {
                NativeMethods.SHCreateItemFromParsingName(parsingName, IntPtr.Zero, NativeMethods.IID_IShellItemImageFactory, out var factory);
                int hr = factory.GetImage(new NativeMethods.SIZE { cx = size, cy = size },
                    NativeMethods.SIIGBF_ICONONLY | NativeMethods.SIIGBF_BIGGERSIZEOK, out IntPtr hBitmap);
                Marshal.ReleaseComObject(factory);
                if (hr != 0 || hBitmap == IntPtr.Zero) return null;

                try
                {
                    var source = Imaging.CreateBitmapSourceFromHBitmap(hBitmap, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                    // Los HBITMAP de IShellItemImageFactory vienen con alfa premultiplicado.
                    var converted = new FormatConvertedBitmap(source, PixelFormats.Pbgra32, null, 0);
                    converted.Freeze();
                    return converted;
                }
                finally
                {
                    NativeMethods.DeleteObject(hBitmap);
                }
            }
            catch
            {
                return null;
            }
        }

        /// <summary>AppUserModelID declarado en un acceso directo (.lnk), o null.</summary>
        public static string GetAppUserModelId(string path)
        {
            try
            {
                if (NativeMethods.SHGetPropertyStoreFromParsingName(path, IntPtr.Zero, 0, NativeMethods.IID_IPropertyStore, out var store) != 0 || store == null)
                    return null;

                try
                {
                    var key = NativeMethods.PKEY_AppUserModel_ID;
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

        /// <summary>Expande rutas del registro del tipo "{GUID-carpeta-conocida}\sub\ruta.exe".</summary>
        public static string ExpandKnownFolderPath(string path)
        {
            if (string.IsNullOrEmpty(path) || path[0] != '{') return path;

            int end = path.IndexOf('}');
            if (end < 0 || !Guid.TryParse(path.Substring(0, end + 1), out Guid folderId)) return path;

            if (NativeMethods.SHGetKnownFolderPath(folderId, 0, IntPtr.Zero, out IntPtr ptr) != 0) return path;
            try
            {
                return Marshal.PtrToStringUni(ptr) + path.Substring(end + 1);
            }
            finally
            {
                Marshal.FreeCoTaskMem(ptr);
            }
        }
    }
}
