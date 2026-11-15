using System;
using System.Linq;
using System.Threading;
using WindowsBar.Interop;
using WindowsBar.Services;

namespace WindowsBar
{
    internal static class Program
    {
        private const string MutexName = "WindowsBar.SingleInstance";
        private static Mutex _mutex;

        [STAThread]
        public static int Main(string[] args)
        {
            // "WindowsBar.exe --restore" devuelve la barra nativa a su estado original
            // (útil si la app se cerró de forma forzada y la barra de Windows quedó oculta).
            if (args.Any(a => a.Equals("--restore", StringComparison.OrdinalIgnoreCase)))
            {
                var settings = SettingsService.Load();
                NativeTaskbar.Restore(settings.Data.NativeTaskbarStateBeforeHide ?? 0);
                settings.Data.NativeTaskbarStateBeforeHide = null;
                settings.Save();
                return 0;
            }

            // "WindowsBar.exe --exit" pide a la instancia en marcha que se cierre limpiamente.
            if (args.Any(a => a.Equals("--exit", StringComparison.OrdinalIgnoreCase)))
            {
                if (EventWaitHandle.TryOpenExisting(App.ExitSignalName, out var signal))
                {
                    signal.Set();
                    signal.Dispose();
                }
                return 0;
            }

            _mutex = new Mutex(true, MutexName, out bool createdNew);
            if (!createdNew)
            {
                return 1;
            }

            // En el inicio de sesión Explorer puede no haber creado aún su barra: la esperamos
            // para no registrar nuestras AppBar antes que él.
            for (int i = 0; i < 120 && NativeMethods.FindWindow("Shell_TrayWnd", null) == IntPtr.Zero; i++)
            {
                Thread.Sleep(500);
            }

            // Las apps que abramos heredan nuestro entorno: lo dejamos como el de una sesión normal
            // (sin ELECTRON_RUN_AS_NODE, VSCODE_*, etc. si nos lanzó otra herramienta).
            UserEnvironment.ResetToUserDefaults();
            DiagLog.Write($"[Arranque] entorno de usuario aplicado; ELECTRON_RUN_AS_NODE presente: " +
                          $"{(Environment.GetEnvironmentVariable("ELECTRON_RUN_AS_NODE") == null ? "no" : "SÍ")}, " +
                          $"VSCODE_PID presente: {(Environment.GetEnvironmentVariable("VSCODE_PID") == null ? "no" : "SÍ")}");

            System.Windows.Forms.Application.SetHighDpiMode(System.Windows.Forms.HighDpiMode.PerMonitorV2);

            var app = new App();
            app.InitializeComponent();
            int code = app.Run();

            _mutex.ReleaseMutex();
            return code;
        }
    }
}
