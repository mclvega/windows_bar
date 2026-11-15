using System;
using System.Windows.Threading;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;

namespace WindowsBar.Services
{
    /// <summary>Volumen y silencio del dispositivo de salida predeterminado.</summary>
    public class AudioService : ObservableObject, IDisposable, IMMNotificationClient
    {
        private readonly Dispatcher _dispatcher;
        private readonly MMDeviceEnumerator _enumerator = new();
        private MMDevice _device;

        private int _volume;
        private bool _isMuted;
        private bool _hasDevice;
        private string _deviceName = "";

        public int Volume { get => _volume; private set { if (Set(ref _volume, value)) OnPropertyChanged(nameof(Glyph)); } }
        public bool IsMuted { get => _isMuted; private set { if (Set(ref _isMuted, value)) OnPropertyChanged(nameof(Glyph)); } }
        public bool HasDevice { get => _hasDevice; private set { if (Set(ref _hasDevice, value)) OnPropertyChanged(nameof(Glyph)); } }
        public string DeviceName { get => _deviceName; private set => Set(ref _deviceName, value); }

        /// <summary>Glifo de Segoe Fluent Icons según el nivel.</summary>
        public string Glyph
        {
            get
            {
                if (!HasDevice) return "";          // sin dispositivo
                if (IsMuted || Volume == 0) return ""; // silencio
                if (Volume < 34) return "";
                if (Volume < 67) return "";
                return "";
            }
        }

        public string ToolTip => HasDevice
            ? $"{DeviceName}: {(IsMuted ? "silenciado" : Volume + "%")}"
            : "No hay dispositivo de audio";

        public AudioService(Dispatcher dispatcher)
        {
            _dispatcher = dispatcher;
            try { _enumerator.RegisterEndpointNotificationCallback(this); } catch { }
            AttachDefaultDevice();
        }

        public void ChangeVolume(int deltaPercent)
        {
            if (_device == null) return;
            try
            {
                var ep = _device.AudioEndpointVolume;
                float v = Math.Clamp(ep.MasterVolumeLevelScalar + deltaPercent / 100f, 0f, 1f);
                ep.MasterVolumeLevelScalar = v;
                if (ep.Mute && deltaPercent > 0) ep.Mute = false;
            }
            catch { }
        }

        public void ToggleMute()
        {
            if (_device == null) return;
            try { _device.AudioEndpointVolume.Mute = !_device.AudioEndpointVolume.Mute; } catch { }
        }

        private void AttachDefaultDevice()
        {
            DetachDevice();
            try
            {
                _device = _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                _device.AudioEndpointVolume.OnVolumeNotification += OnVolumeNotification;
                DeviceName = _device.FriendlyName;
                HasDevice = true;
                ReadState();
            }
            catch
            {
                _device = null;
                HasDevice = false;
                OnPropertyChanged(nameof(ToolTip));
            }
        }

        private void DetachDevice()
        {
            if (_device == null) return;
            try { _device.AudioEndpointVolume.OnVolumeNotification -= OnVolumeNotification; } catch { }
            _device.Dispose();
            _device = null;
        }

        private void ReadState()
        {
            if (_device == null) return;
            Volume = (int)Math.Round(_device.AudioEndpointVolume.MasterVolumeLevelScalar * 100);
            IsMuted = _device.AudioEndpointVolume.Mute;
            OnPropertyChanged(nameof(ToolTip));
        }

        private void OnVolumeNotification(AudioVolumeNotificationData data)
        {
            _dispatcher.BeginInvoke(() =>
            {
                Volume = (int)Math.Round(data.MasterVolume * 100);
                IsMuted = data.Muted;
                OnPropertyChanged(nameof(ToolTip));
            });
        }

        // ---- IMMNotificationClient: cambio de dispositivo predeterminado ----
        public void OnDefaultDeviceChanged(DataFlow flow, Role role, string defaultDeviceId)
        {
            if (flow == DataFlow.Render && role == Role.Multimedia)
                _dispatcher.BeginInvoke(AttachDefaultDevice);
        }

        public void OnDeviceStateChanged(string deviceId, DeviceState newState) { }
        public void OnDeviceAdded(string pwstrDeviceId) { }
        public void OnDeviceRemoved(string deviceId) { }
        public void OnPropertyValueChanged(string pwstrDeviceId, PropertyKey key) { }

        public void Dispose()
        {
            try { _enumerator.UnregisterEndpointNotificationCallback(this); } catch { }
            DetachDevice();
            _enumerator.Dispose();
        }
    }
}
