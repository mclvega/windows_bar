using System;
using System.Windows;
using ManagedShell;

namespace WindowsBar.Services
{
    /// <summary>Servicios compartidos por todas las barras (una instancia por proceso).</summary>
    public class AppServices : IDisposable
    {
        public ShellManager ShellManager { get; }
        public SettingsService Settings { get; }
        public PinnedAppsService Pins { get; }
        public TrayModel Tray { get; }
        public AudioService Audio { get; }
        public NetworkService Network { get; }
        public PowerService Power { get; }
        public InputLanguageService InputLanguage { get; }
        public MicUsageService Microphone { get; }
        public ClockService Clock { get; }
        public WindowManager WindowManager { get; }
        public LaunchPlacer LaunchPlacer { get; }

        public AppServices(ShellManager shellManager, SettingsService settings)
        {
            var dispatcher = Application.Current.Dispatcher;

            ShellManager = shellManager;
            Settings = settings;
            Pins = new PinnedAppsService(settings);
            Tray = new TrayModel(shellManager.NotificationArea, settings, dispatcher);
            Audio = new AudioService(dispatcher);
            Network = new NetworkService(dispatcher);
            Power = new PowerService(dispatcher);
            InputLanguage = new InputLanguageService(dispatcher, settings);
            Microphone = new MicUsageService(dispatcher);
            Clock = new ClockService(dispatcher, settings);
            WindowManager = new WindowManager(this);
            LaunchPlacer = new LaunchPlacer(shellManager.Tasks, dispatcher);
        }

        public void Dispose()
        {
            LaunchPlacer.Dispose();
            WindowManager.Dispose();
            Tray.Dispose();
            Audio.Dispose();
            Network.Dispose();
            Power.Dispose();
            InputLanguage.Dispose();
            Microphone.Dispose();
            Clock.Dispose();
            ShellManager.Dispose();
        }
    }
}
