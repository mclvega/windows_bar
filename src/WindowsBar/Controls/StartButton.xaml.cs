using System.Windows;
using System.Windows.Controls;
using WindowsBar.Services;
using WindowsBar.Views;

namespace WindowsBar.Controls
{
    public partial class StartButton : UserControl
    {
        public StartButton()
        {
            InitializeComponent();
        }

        private TaskbarWindow Host => Window.GetWindow(this) as TaskbarWindow;

        private void Button_Click(object sender, RoutedEventArgs e) => ShellCommands.StartMenu(Host);

        #region Menú de clic derecho (enlace rápido)
        private static string ItemName(object sender) => ((MenuItem)sender).Header.ToString().Replace("_", "");

        /// <summary>Abre lo indicado en el Tag del elemento en el monitor de esta barra.</summary>
        private void Launch_Click(object sender, RoutedEventArgs e) =>
            ShellCommands.LaunchOnMonitor((string)((MenuItem)sender).Tag, "", Host, ItemName(sender));

        private void LaunchAdmin_Click(object sender, RoutedEventArgs e) =>
            ShellCommands.LaunchOnMonitor((string)((MenuItem)sender).Tag, "", Host, ItemName(sender), asAdmin: true);

        private void Search_Click(object sender, RoutedEventArgs e) => ShellCommands.Search(Host);

        private void Run_Click(object sender, RoutedEventArgs e) =>
            ShellCommands.LaunchOnMonitor("explorer.exe", "shell:::{2559a1f3-21d7-11d4-bdaf-00c04f60b9f0}", Host, "Ejecutar");

        private void SignOut_Click(object sender, RoutedEventArgs e) => ShellCommands.Open("shutdown.exe", "/l");

        private void Sleep_Click(object sender, RoutedEventArgs e) =>
            System.Windows.Forms.Application.SetSuspendState(System.Windows.Forms.PowerState.Suspend, false, false);

        private void Shutdown_Click(object sender, RoutedEventArgs e) => ShellCommands.Open("shutdown.exe", "/s /t 0");

        private void Restart_Click(object sender, RoutedEventArgs e) => ShellCommands.Open("shutdown.exe", "/r /t 0");

        private void Desktop_Click(object sender, RoutedEventArgs e) => ShellCommands.ShowDesktop();
        #endregion
    }
}
