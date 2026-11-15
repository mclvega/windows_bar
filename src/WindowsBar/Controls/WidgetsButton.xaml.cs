using System.Windows;
using System.Windows.Controls;
using WindowsBar.Services;

namespace WindowsBar.Controls
{
    public partial class WidgetsButton : UserControl
    {
        public WidgetsButton()
        {
            InitializeComponent();
        }

        private void Button_Click(object sender, RoutedEventArgs e) => ShellCommands.Widgets(Window.GetWindow(this) as WindowsBar.Views.TaskbarWindow);
    }
}
