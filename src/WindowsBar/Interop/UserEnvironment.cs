using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace WindowsBar.Interop
{
    /// <summary>
    /// Deja el entorno del proceso igual que el de una sesión normal del usuario (el que usa Explorer).
    /// Las apps que abre la barra heredan este entorno: si WindowsBar se lanzó desde VS Code u otra
    /// herramienta, variables como ELECTRON_RUN_AS_NODE harían que VS Code, Discord, Postman... no abrieran.
    /// </summary>
    internal static class UserEnvironment
    {
        public static void ResetToUserDefaults()
        {
            Dictionary<string, string> clean = ReadDefaultUserEnvironment();
            if (clean == null || clean.Count == 0) return;

            // Quitar lo que no forma parte del entorno normal del usuario...
            foreach (DictionaryEntry entry in Environment.GetEnvironmentVariables())
            {
                string name = (string)entry.Key;
                if (!clean.ContainsKey(name)) Environment.SetEnvironmentVariable(name, null);
            }

            // ...y fijar los valores normales (PATH actualizado incluido).
            foreach (var pair in clean)
            {
                Environment.SetEnvironmentVariable(pair.Key, pair.Value);
            }
        }

        private static Dictionary<string, string> ReadDefaultUserEnvironment()
        {
            if (!OpenProcessToken(GetCurrentProcess(), TOKEN_QUERY | TOKEN_DUPLICATE, out IntPtr token)) return null;
            try
            {
                if (!CreateEnvironmentBlock(out IntPtr block, token, false)) return null;
                try
                {
                    // Bloque: "NOMBRE=valor\0NOMBRE=valor\0...\0\0" en UTF-16.
                    var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    IntPtr cursor = block;
                    while (true)
                    {
                        string entry = Marshal.PtrToStringUni(cursor);
                        if (string.IsNullOrEmpty(entry)) break;

                        int eq = entry.IndexOf('=', 1); // las entradas "=C:=C:\..." empiezan por '='
                        if (eq > 0) result[entry.Substring(0, eq)] = entry.Substring(eq + 1);

                        cursor += (entry.Length + 1) * 2;
                    }
                    return result;
                }
                finally
                {
                    DestroyEnvironmentBlock(block);
                }
            }
            finally
            {
                CloseHandle(token);
            }
        }

        private const uint TOKEN_QUERY = 0x0008;
        private const uint TOKEN_DUPLICATE = 0x0002;

        [DllImport("kernel32.dll")]
        private static extern IntPtr GetCurrentProcess();

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);

        [DllImport("userenv.dll", SetLastError = true)]
        private static extern bool CreateEnvironmentBlock(out IntPtr environment, IntPtr token, bool inherit);

        [DllImport("userenv.dll", SetLastError = true)]
        private static extern bool DestroyEnvironmentBlock(IntPtr environment);

        [DllImport("kernel32.dll")]
        private static extern bool CloseHandle(IntPtr handle);
    }
}
