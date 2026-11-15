using System;
using System.Globalization;
using System.Windows.Threading;
using Microsoft.Win32;

namespace WindowsBar.Services
{
    /// <summary>Hora y fecha con el formato regional del usuario.</summary>
    public class ClockService : ObservableObject, IDisposable
    {
        private readonly DispatcherTimer _timer;
        private string _time = "";
        private string _date = "";
        private string _toolTip = "";

        public string Time { get => _time; private set => Set(ref _time, value); }
        public string Date { get => _date; private set => Set(ref _date, value); }
        public string ToolTip { get => _toolTip; private set => Set(ref _toolTip, value); }

        private readonly SettingsService _settings;

        public ClockService(Dispatcher dispatcher, SettingsService settings)
        {
            _settings = settings;
            _settings.Changed += OnSystemChanged;
            _timer = new DispatcherTimer(DispatcherPriority.Normal, dispatcher);
            _timer.Tick += (_, _) => Tick();
            SystemEvents.TimeChanged += OnSystemChanged;
            SystemEvents.UserPreferenceChanged += OnSystemChanged;
            Tick();
        }

        private void OnSystemChanged(object sender, EventArgs e)
        {
            CultureInfo.CurrentCulture.ClearCachedData();
            Tick();
        }

        private void Tick()
        {
            DateTime now = DateTime.Now;
            var culture = CultureInfo.CurrentCulture;

            bool seconds = _settings.Data.ShowClockSeconds;
            Time = now.ToString(seconds ? culture.DateTimeFormat.LongTimePattern : culture.DateTimeFormat.ShortTimePattern, culture);
            Date = now.ToString(culture.DateTimeFormat.ShortDatePattern, culture);
            string longDate = now.ToString(culture.DateTimeFormat.LongDatePattern, culture);
            ToolTip = longDate.Length > 0 ? char.ToUpper(longDate[0], culture) + longDate.Substring(1) : longDate;

            // Despertar justo al empezar el siguiente segundo o minuto.
            _timer.Stop();
            _timer.Interval = seconds
                ? TimeSpan.FromMilliseconds(1000 - now.Millisecond + 20)
                : TimeSpan.FromMilliseconds(60000 - now.Second * 1000 - now.Millisecond + 50);
            _timer.Start();
        }

        public void Dispose()
        {
            _timer.Stop();
            SystemEvents.TimeChanged -= OnSystemChanged;
            _settings.Changed -= OnSystemChanged;
            SystemEvents.UserPreferenceChanged -= OnSystemChanged;
        }
    }
}
