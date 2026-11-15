using System;
using System.Windows.Forms;
using System.Windows.Threading;
using Microsoft.Win32;

namespace WindowsBar.Services
{
    /// <summary>Batería: porcentaje y si está cargando. Se oculta en equipos sin batería.</summary>
    public class PowerService : ObservableObject, IDisposable
    {
        private readonly Dispatcher _dispatcher;
        private readonly DispatcherTimer _timer;

        private bool _hasBattery;
        private int _percent;
        private bool _isCharging;

        public bool HasBattery { get => _hasBattery; private set => Set(ref _hasBattery, value); }
        public int Percent { get => _percent; private set { if (Set(ref _percent, value)) OnPropertyChanged(nameof(Glyph)); } }
        public bool IsCharging { get => _isCharging; private set { if (Set(ref _isCharging, value)) OnPropertyChanged(nameof(Glyph)); } }

        /// <summary>Battery0..Battery10 (EBA0..EBAA) y BatteryCharging0..10 (EBAB..EBB5).</summary>
        public string Glyph
        {
            get
            {
                int level = Math.Clamp((int)Math.Round(Percent / 10.0), 0, 10);
                return ((char)((IsCharging ? 0xEBAB : 0xEBA0) + level)).ToString();
            }
        }

        public string ToolTip => $"Batería: {Percent}%{(IsCharging ? " (cargando)" : "")}";

        public PowerService(Dispatcher dispatcher)
        {
            _dispatcher = dispatcher;
            SystemEvents.PowerModeChanged += OnPowerModeChanged;
            _timer = new DispatcherTimer(TimeSpan.FromSeconds(30), DispatcherPriority.Background, (_, _) => Refresh(), dispatcher);
            Refresh();
        }

        private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e) => _dispatcher.BeginInvoke(Refresh);

        public void Refresh()
        {
            PowerStatus status = SystemInformation.PowerStatus;
            HasBattery = !status.BatteryChargeStatus.HasFlag(BatteryChargeStatus.NoSystemBattery)
                         && !status.BatteryChargeStatus.HasFlag(BatteryChargeStatus.Unknown);
            Percent = (int)Math.Round(Math.Clamp(status.BatteryLifePercent, 0f, 1f) * 100);
            IsCharging = status.PowerLineStatus == PowerLineStatus.Online;
            OnPropertyChanged(nameof(ToolTip));
        }

        public void Dispose()
        {
            _timer.Stop();
            SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        }
    }
}
