using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using WindowsBar.Services;

namespace WindowsBar.Controls
{
    public partial class SystemIcons : UserControl
    {
        public SystemIcons()
        {
            InitializeComponent();
            DataContext = App.Services;
        }

        private void Microphone_Click(object sender, RoutedEventArgs e) => ShellCommands.MicrophonePrivacySettings();

        private void Language_Click(object sender, RoutedEventArgs e) => ShellCommands.SwitchInputLanguage();

        private void Language_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;
            ShellCommands.LanguageSettings();
        }

        private void QuickSettings_Click(object sender, RoutedEventArgs e) => ShellCommands.QuickSettings();

        /// <summary>Rueda del ratón sobre los iconos: subir/bajar volumen, como en Windows 11.</summary>
        private void QuickSettings_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            e.Handled = true;
            App.Services.Audio.ChangeVolume(e.Delta > 0 ? 2 : -2);
        }

        /// <summary>Clic central: silenciar / quitar silencio.</summary>
        private void QuickSettings_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Middle) return;
            e.Handled = true;
            App.Services.Audio.ToggleMute();
        }

        private void SoundSettings_Click(object sender, RoutedEventArgs e) => ShellCommands.SoundSettings();

        private void VolumeMixer_Click(object sender, RoutedEventArgs e) => ShellCommands.VolumeMixer();

        private void NetworkSettings_Click(object sender, RoutedEventArgs e) => ShellCommands.NetworkSettings();
    }
}
