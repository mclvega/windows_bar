using System.Windows;
using System.Windows.Controls;
using WindowsBar.Services;

namespace WindowsBar.Controls
{
    public partial class ShowDesktopButton : UserControl
    {
        public ShowDesktopButton()
        {
            InitializeComponent();
        }

        private void Button_Click(object sender, RoutedEventArgs e) => ShellCommands.ShowDesktop();
    }
}
