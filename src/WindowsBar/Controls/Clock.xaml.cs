using System.Windows;
using System.Windows.Controls;
using WindowsBar.Services;

namespace WindowsBar.Controls
{
    public partial class Clock : UserControl
    {
        public Clock()
        {
            InitializeComponent();
            DataContext = App.Services.Clock;
        }

        /// <summary>Calendario y notificaciones (Win+N), como en Windows 11.</summary>
        private void Clock_Click(object sender, RoutedEventArgs e) => ShellCommands.NotificationCenter();

        private void DateTimeSettings_Click(object sender, RoutedEventArgs e) => ShellCommands.DateTimeSettings();
    }
}
