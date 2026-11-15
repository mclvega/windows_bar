using System;
using System.IO;

namespace WindowsBar.Services
{
    /// <summary>Registro de diagnóstico en %LOCALAPPDATA%\WindowsBar\diag.log (se recorta al pasar de 1 MB).</summary>
    public static class DiagLog
    {
        public static readonly string FilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WindowsBar", "diag.log");

        private static readonly object Lock = new();

        public static void Write(string message)
        {
            try
            {
                lock (Lock)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                    var info = new FileInfo(FilePath);
                    if (info.Exists && info.Length > 1_000_000) info.Delete();
                    File.AppendAllText(FilePath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}  {message}{Environment.NewLine}");
                }
            }
            catch
            {
                // El diagnóstico nunca debe romper la barra.
            }
        }
    }
}
