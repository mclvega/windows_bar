using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using WindowsBar.Interop;
using WindowsBar.Services;
using NotifyIconModel = ManagedShell.WindowsTray.NotifyIcon;

namespace WindowsBar.Views
{
    /// <summary>Ventana de configuración de WindowsBar (instancia única).</summary>
    public partial class SettingsWindow : Window
    {
        private static SettingsWindow _instance;
        private readonly AppServices _services;
        private bool _loading;

        private SettingsWindow(AppServices services)
        {
            _services = services;
            InitializeComponent();

            SourceInitialized += (_, _) => UseDarkTitleBar();
            Closed += (_, _) =>
            {
                _services.Tray.Changed -= Tray_Changed;
                _services.Pins.Changed -= Pins_Changed;
                _instance = null;
            };

            _services.Tray.Changed += Tray_Changed;
            _services.Pins.Changed += Pins_Changed;

            LoadToggles();
            LoadTray();
            LoadPins();
        }

        /// <summary>Abre (o trae al frente) la configuración, centrada en el monitor de la barra pulsada.</summary>
        public static void Open(TaskbarWindow host)
        {
            if (_instance == null)
            {
                _instance = new SettingsWindow(App.Services);
                PlaceOnMonitor(_instance, host);
                _instance.Show();
            }
            else if (_instance.WindowState == WindowState.Minimized)
            {
                _instance.WindowState = WindowState.Normal;
            }

            _instance.Activate();
        }

        private static void PlaceOnMonitor(Window window, TaskbarWindow host)
        {
            if (host == null)
            {
                window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
                return;
            }

            // WorkingArea está en píxeles físicos; la ventana se posiciona en unidades WPF.
            double scale = host.DpiScale > 0 ? host.DpiScale : 1;
            var area = host.Screen.WorkingArea;
            window.Left = area.Left / scale + (area.Width / scale - window.Width) / 2;
            window.Top = area.Top / scale + (area.Height / scale - window.Height) / 2;
        }

        private void UseDarkTitleBar()
        {
            const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
            int on = 1;
            DwmSetWindowAttribute(new WindowInteropHelper(this).Handle, DWMWA_USE_IMMERSIVE_DARK_MODE, ref on, sizeof(int));
        }

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        #region Navegación
        private void Nav_Checked(object sender, RoutedEventArgs e)
        {
            if (PageGeneral == null) return; // durante InitializeComponent
            PageGeneral.Visibility = NavGeneral.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            PageItems.Visibility = NavItems.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            PageTray.Visibility = NavTray.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            PagePins.Visibility = NavPins.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        }
        #endregion

        #region Interruptores de ajustes
        /// <summary>
        /// Cada interruptor lleva en Tag el nombre de la propiedad booleana de AppSettingsData que controla.
        /// </summary>
        private IEnumerable<CheckBox> SettingToggles =>
            FindChildren<CheckBox>(PageGeneral).Concat(FindChildren<CheckBox>(PageItems)).Where(c => c.Tag is string);

        private void LoadToggles()
        {
            _loading = true;
            foreach (var toggle in SettingToggles)
            {
                var prop = SettingProperty(toggle);
                if (prop == null) continue;
                toggle.IsChecked = (bool)prop.GetValue(_services.Settings.Data);
                toggle.Click -= SettingToggle_Click;
                toggle.Click += SettingToggle_Click;
            }

            if (StartupService.IsDevelopmentBuild)
            {
                StartupHint.Text = "Esta copia se ejecuta desde la carpeta de compilación: el inicio automático se registra al instalar con publish.ps1.";
            }
            _loading = false;
        }

        private void SettingToggle_Click(object sender, RoutedEventArgs e)
        {
            if (_loading || sender is not CheckBox toggle) return;
            var prop = SettingProperty(toggle);
            if (prop == null) return;

            bool value = toggle.IsChecked == true;
            prop.SetValue(_services.Settings.Data, value);

            if (prop.Name == nameof(AppSettingsData.StartWithWindows))
            {
                StartupService.Apply(value);
            }

            _services.Settings.SaveAndNotify();
        }

        private static PropertyInfo SettingProperty(CheckBox toggle)
        {
            var prop = typeof(AppSettingsData).GetProperty((string)toggle.Tag);
            return prop?.PropertyType == typeof(bool) ? prop : null;
        }
        #endregion

        #region Bandeja del sistema
        private class TrayRow
        {
            public NotifyIconModel Model { get; init; }
            public ImageSource Icon { get; init; }
            public string Name { get; init; }
            public string Path { get; init; }
            public bool Visible { get; init; }
        }

        private void Tray_Changed(object sender, EventArgs e) => LoadTray();

        private void LoadTray()
        {
            var rows = _services.Tray.All.Select(icon =>
            {
                string path = "";
                string title = "";
                try { path = icon.Path ?? ""; } catch { }
                try { title = icon.Title ?? ""; } catch { }

                string name = title.Split('\n')[0].Trim();
                if (string.IsNullOrEmpty(name)) name = string.IsNullOrEmpty(path) ? "(sin nombre)" : System.IO.Path.GetFileNameWithoutExtension(path);

                return new TrayRow
                {
                    Model = icon,
                    Icon = icon.Icon,
                    Name = name,
                    Path = path,
                    Visible = _services.Tray.IsVisibleInBar(icon)
                };
            }).OrderBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase).ToList();

            TrayList.ItemsSource = rows;
            TrayEmpty.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private void TrayToggle_Click(object sender, RoutedEventArgs e)
        {
            if (sender is CheckBox { DataContext: TrayRow row } toggle)
            {
                _services.Tray.SetVisibleInBar(row.Model, toggle.IsChecked == true);
            }
        }

        private void TrayReset_Click(object sender, RoutedEventArgs e) => _services.Tray.ResetToWindows();
        #endregion

        #region Apps ancladas
        private class PinRow
        {
            public PinnedApp Pin { get; init; }
            public ImageSource Icon { get; init; }
            public string Name { get; init; }
            public string Path { get; init; }
        }

        private void Pins_Changed(object sender, EventArgs e) => LoadPins();

        private void LoadPins()
        {
            var apps = _services.Pins.Apps.ToList();
            PinList.ItemsSource = apps.Select(pin => new PinRow
            {
                Pin = pin,
                Icon = ShellItemHelper.GetIcon(pin.LaunchPath, 32) ?? ShellItemHelper.GetIcon(pin.TargetPath, 32),
                Name = pin.Name,
                Path = pin.TargetPath ?? pin.LaunchPath
            }).ToList();
            PinsEmpty.Visibility = apps.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private void PinRemove_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is PinRow row) _services.Pins.Unpin(row.Pin);
        }

        private void PinsReimport_Click(object sender, RoutedEventArgs e)
        {
            var answer = MessageBox.Show(this,
                "Se sustituirán tus apps ancladas en WindowsBar por las que tiene ancladas la barra de Windows.\n\n¿Continuar?",
                "Volver a importar", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (answer == MessageBoxResult.Yes) _services.Pins.ReimportFromWindows();
        }
        #endregion

        private static IEnumerable<T> FindChildren<T>(DependencyObject parent) where T : DependencyObject
        {
            foreach (object child in LogicalTreeHelper.GetChildren(parent))
            {
                if (child is not DependencyObject d) continue;
                if (d is T match) yield return match;
                foreach (var nested in FindChildren<T>(d)) yield return nested;
            }
        }
    }
}
