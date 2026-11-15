using System.Windows;
using System.Windows.Controls;
using WindowsBar.Services;

namespace WindowsBar.Controls
{
    public partial class SearchButton : UserControl
    {
        public SearchButton()
        {
            InitializeComponent();
        }

        private void Button_Click(object sender, RoutedEventArgs e) => ShellCommands.Search(Window.GetWindow(this) as WindowsBar.Views.TaskbarWindow);
    }
}
