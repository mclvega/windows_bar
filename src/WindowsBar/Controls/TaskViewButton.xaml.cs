using System.Windows;
using System.Windows.Controls;
using WindowsBar.Services;

namespace WindowsBar.Controls
{
    public partial class TaskViewButton : UserControl
    {
        public TaskViewButton()
        {
            InitializeComponent();
        }

        private void Button_Click(object sender, RoutedEventArgs e) => ShellCommands.TaskView();
    }
}
