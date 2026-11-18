using System;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using ManagedShell.WindowsTray;
using WindowsBar.Views;

namespace WindowsBar.Controls
{
    public partial class TrayArea : UserControl
    {
        private readonly DispatcherTimer _balloonTimer = new();
        private NotificationBalloon _balloon;

        public TrayArea()
        {
            InitializeComponent();

            var tray = App.Services.Tray;
            VisibleItems.ItemsSource = tray.Visible;
            OverflowItems.ItemsSource = tray.Overflow;

            tray.Overflow.CollectionChanged += Overflow_CollectionChanged;
            UpdateChevron();

            _balloonTimer.Tick += (_, _) => CloseBalloon(BalloonVisibility.TimedOut);
            Unloaded += (_, _) =>
            {
                tray.Overflow.CollectionChanged -= Overflow_CollectionChanged;
                _balloonTimer.Stop();
            };
        }

        private void Overflow_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e) => UpdateChevron();

        private void UpdateChevron()
        {
            ChevronButton.Visibility = App.Services.Tray.Overflow.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            if (App.Services.Tray.Overflow.Count == 0) OverflowPopup.IsOpen = false;
        }

        private void ChevronButton_Click(object sender, RoutedEventArgs e)
        {
            OverflowPopup.IsOpen = !OverflowPopup.IsOpen;
        }

        private TaskbarWindow Host => Window.GetWindow(this) as TaskbarWindow;

        // El panel es otra ventana: mientras esté abierto, la barra no se oculta automáticamente.
        private void OverflowPopup_Opened(object sender, EventArgs e) => Host?.PopupOpened();

        private void OverflowPopup_Closed(object sender, EventArgs e) => Host?.PopupClosed();

        #region Globos de notificación
        /// <summary>Muestra el globo de una app de bandeja. Devuelve false si no se pudo.</summary>
        public bool ShowBalloon(NotificationBalloon balloon)
        {
            if (balloon == null || (string.IsNullOrWhiteSpace(balloon.Title) && string.IsNullOrWhiteSpace(balloon.Info)))
                return false;

            if (_balloon != null) CloseBalloon(BalloonVisibility.Hidden);

            _balloon = balloon;
            BalloonIcon.Source = balloon.Icon ?? balloon.NotifyIcon?.Icon;
            BalloonTitle.Text = balloon.Title;
            BalloonTitle.Visibility = string.IsNullOrWhiteSpace(balloon.Title) ? Visibility.Collapsed : Visibility.Visible;
            BalloonText.Text = balloon.Info;

            BalloonPopup.IsOpen = true;
            balloon.SetVisibility(BalloonVisibility.Visible);

            int timeout = Math.Clamp(balloon.Timeout, 5000, 10000);
            _balloonTimer.Interval = TimeSpan.FromMilliseconds(timeout);
            _balloonTimer.Start();
            return true;
        }

        private void CloseBalloon(BalloonVisibility reason)
        {
            _balloonTimer.Stop();
            BalloonPopup.IsOpen = false;
            _balloon?.SetVisibility(reason);
            _balloon = null;
        }

        private void Balloon_Click(object sender, MouseButtonEventArgs e)
        {
            var balloon = _balloon;
            CloseBalloon(BalloonVisibility.Hidden);
            balloon?.Click();
        }

        private void BalloonClose_Click(object sender, RoutedEventArgs e)
        {
            e.Handled = true;
            CloseBalloon(BalloonVisibility.Hidden);
        }
        #endregion
    }
}
