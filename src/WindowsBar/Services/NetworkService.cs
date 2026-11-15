using System;
using System.Linq;
using System.Net.NetworkInformation;
using System.Windows.Threading;

namespace WindowsBar.Services
{
    public enum NetworkKind { None, Ethernet, Wifi, Other }

    /// <summary>Tipo de conexión activa (cable, Wi-Fi o ninguna).</summary>
    public class NetworkService : ObservableObject, IDisposable
    {
        private readonly Dispatcher _dispatcher;
        private readonly DispatcherTimer _timer;

        private NetworkKind _kind;
        private string _name = "";
        private bool _isAvailable;

        public NetworkKind Kind { get => _kind; private set { if (Set(ref _kind, value)) OnPropertyChanged(nameof(Glyph)); } }
        public string Name { get => _name; private set => Set(ref _name, value); }
        public bool IsAvailable { get => _isAvailable; private set { if (Set(ref _isAvailable, value)) OnPropertyChanged(nameof(Glyph)); } }

        public string Glyph => Kind switch
        {
            NetworkKind.Wifi => "",
            NetworkKind.Ethernet => "",
            NetworkKind.Other => "",
            _ => ""
        };

        public string ToolTip => Kind == NetworkKind.None
            ? "Sin conexión a Internet"
            : $"{Name}\n{(IsAvailable ? "Conectado" : "Sin acceso a Internet")}";

        public NetworkService(Dispatcher dispatcher)
        {
            _dispatcher = dispatcher;
            NetworkChange.NetworkAddressChanged += OnNetworkChanged;
            NetworkChange.NetworkAvailabilityChanged += OnNetworkChanged;

            _timer = new DispatcherTimer(TimeSpan.FromSeconds(15), DispatcherPriority.Background, (_, _) => Refresh(), dispatcher);
            Refresh();
        }

        private void OnNetworkChanged(object sender, EventArgs e) => _dispatcher.BeginInvoke(Refresh);

        public void Refresh()
        {
            try
            {
                var active = NetworkInterface.GetAllNetworkInterfaces()
                    .Where(n => n.OperationalStatus == OperationalStatus.Up
                                && n.NetworkInterfaceType != NetworkInterfaceType.Loopback
                                && n.NetworkInterfaceType != NetworkInterfaceType.Tunnel
                                && !IsVirtual(n)
                                && n.GetIPProperties().GatewayAddresses.Any(g => !g.Address.Equals(System.Net.IPAddress.Any)))
                    .OrderBy(n => n.NetworkInterfaceType == NetworkInterfaceType.Ethernet ? 0 : 1)
                    .FirstOrDefault();

                if (active == null)
                {
                    Kind = NetworkKind.None;
                    Name = "";
                }
                else
                {
                    Kind = active.NetworkInterfaceType switch
                    {
                        NetworkInterfaceType.Wireless80211 => NetworkKind.Wifi,
                        NetworkInterfaceType.Ethernet or NetworkInterfaceType.GigabitEthernet or NetworkInterfaceType.FastEthernetT => NetworkKind.Ethernet,
                        _ => NetworkKind.Other
                    };
                    Name = active.Name;
                }

                IsAvailable = NetworkInterface.GetIsNetworkAvailable() && active != null;
            }
            catch
            {
                Kind = NetworkKind.None;
            }

            OnPropertyChanged(nameof(ToolTip));
        }

        private static bool IsVirtual(NetworkInterface n)
        {
            string d = (n.Description ?? "").ToLowerInvariant();
            return d.Contains("virtual") || d.Contains("hyper-v") || d.Contains("vmware") || d.Contains("wireguard")
                   || d.Contains("vpn") || d.Contains("tap-") || d.Contains("loopback");
        }

        public void Dispose()
        {
            _timer.Stop();
            NetworkChange.NetworkAddressChanged -= OnNetworkChanged;
            NetworkChange.NetworkAvailabilityChanged -= OnNetworkChanged;
        }
    }
}
