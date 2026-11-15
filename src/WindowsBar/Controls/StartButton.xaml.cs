using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using WindowsBar.Services;

namespace WindowsBar.Controls
{
    public partial class StartButton : UserControl
    {
        public StartButton()
        {
            InitializeComponent();
        }

        private void Button_Click(object sender, RoutedEventArgs e) => ShellCommands.StartMenu(Window.GetWindow(this) as WindowsBar.Views.TaskbarWindow);

        /// <summary>Clic derecho: menú de enlace rápido (Win+X), como en Windows.</summary>
        private void Button_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;
            ShellCommands.StartContextMenu(Window.GetWindow(this) as WindowsBar.Views.TaskbarWindow);
        }
    }
}
