using System;
using System.IO;
using Microsoft.Win32;

namespace WindowsBar.Services
{
    /// <summary>Entrada de inicio automático en HKCU\...\Run.</summary>
    public static class StartupService
    {
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string ValueName = "WindowsBar";

        public static string ExePath => Environment.ProcessPath;

        /// <summary>Las ejecuciones desde la carpeta de compilación no se registran en el inicio.</summary>
        public static bool IsDevelopmentBuild =>
            ExePath?.IndexOf(@"\bin\", StringComparison.OrdinalIgnoreCase) >= 0;

        public static string RegisteredPath
        {
            get
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKey);
                return (key?.GetValue(ValueName) as string)?.Trim('"');
            }
        }

        public static bool IsRegistered => !string.IsNullOrEmpty(RegisteredPath);

        /// <summary>Ajusta el registro al valor deseado. Devuelve false si no se pudo (p. ej. build de desarrollo).</summary>
        public static bool Apply(bool enabled)
        {
            try
            {
                using var key = Registry.CurrentUser.CreateSubKey(RunKey);
                if (!enabled)
                {
                    key.DeleteValue(ValueName, false);
                    return true;
                }

                if (IsDevelopmentBuild) return false;
                key.SetValue(ValueName, $"\"{ExePath}\"");
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>Al arrancar: si debe iniciarse con Windows y la entrada falta o está rota, la rehace.</summary>
        public static void EnsureConsistent(bool enabled)
        {
            if (!enabled || IsDevelopmentBuild) return;

            string registered = RegisteredPath;
            if (string.IsNullOrEmpty(registered) || !File.Exists(registered))
                Apply(true);
        }
    }
}
