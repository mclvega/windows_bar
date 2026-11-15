using System;
using System.Windows;
using System.Windows.Threading;
using ManagedShell;
using ManagedShell.Common.Enums;
using ManagedShell.Common.Helpers;
using WindowsBar.Interop;
using WindowsBar.Services;

namespace WindowsBar
{
    public partial class App : Application
    {
        private bool _exited;

        public static AppServices Services { get; private set; }

        private void App_OnStartup(object sender, StartupEventArgs e)
        {
            AppDomain.CurrentDomain.UnhandledException += (_, _) => RestoreNativeTaskbar();

            var settings = SettingsService.Load();
            var shellManager = CreateShellManager();

            // Guardamos el estado original de la barra nativa ANTES de ocultarla.
            // Si quedó un valor de una sesión anterior que terminó mal, se conserva ese
            // (el estado actual sería el "auto-ocultar" que forzamos nosotros).
            if (settings.Data.NativeTaskbarStateBeforeHide == null)
            {
                settings.Data.NativeTaskbarStateBeforeHide = (int)shellManager.ExplorerHelper.GetTaskbarState();
                settings.Save();
            }

            StartupService.EnsureConsistent(settings.Data.StartWithWindows);

            Services = new AppServices(shellManager, settings);
            Services.WindowManager.Start();
        }

        public void ExitGracefully()
        {
            Services?.ShellManager.AppBarManager.SignalGracefulShutdown();
            Shutdown();
        }

        private void App_OnExit(object sender, ExitEventArgs e) => ExitApp();

        private void App_OnSessionEnding(object sender, SessionEndingCancelEventArgs e) => ExitApp();

        private void App_OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            // Un fallo puntual (p. ej. un icono corrupto) no debe tumbar la barra.
            e.Handled = true;
            System.Diagnostics.Debug.WriteLine(e.Exception);
        }

        private static ShellManager CreateShellManager()
        {
            EnvironmentHelper.IsAppRunningAsShell = NativeMethods.GetShellWindow() == IntPtr.Zero;

            ShellConfig config = ShellManager.DefaultShellConfig;
            config.TaskIconSize = IconSize.Large;
            config.PinnedNotifyIcons = Array.Empty<string>();
            return new ShellManager(config);
        }

        private void ExitApp()
        {
            if (_exited) return;
            _exited = true;

            Services?.Dispose();
            RestoreNativeTaskbar();
        }

        private static void RestoreNativeTaskbar()
        {
            try
            {
                var settings = Services?.Settings ?? SettingsService.Load();
                NativeTaskbar.Restore(settings.Data.NativeTaskbarStateBeforeHide ?? 0);
                settings.Data.NativeTaskbarStateBeforeHide = null;
                settings.Save();
            }
            catch
            {
                // Último recurso: el usuario puede ejecutar "WindowsBar.exe --restore".
            }
        }
    }
}
