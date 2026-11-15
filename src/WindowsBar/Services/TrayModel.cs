using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows.Threading;
using ManagedShell.WindowsTray;
using Microsoft.Win32;
using WindowsBar.Interop;

namespace WindowsBar.Services
{
    /// <summary>
    /// Reparte los iconos de bandeja entre la barra (Visible) y el panel de desbordamiento (Overflow).
    /// Por defecto respeta lo que el usuario tenía activado en Windows 11
    /// (HKCU\Control Panel\NotifyIconSettings\*\IsPromoted); las decisiones propias se guardan en settings.
    /// </summary>
    public class TrayModel : IDisposable
    {
        // Iconos del sistema que dibujamos nosotros (volumen, red, batería, micrófono, Reunirse ahora).
        private static readonly HashSet<string> SystemIconGuids = new(StringComparer.OrdinalIgnoreCase)
        {
            NotificationArea.VOLUME_GUID,
            NotificationArea.NETWORK_GUID,
            NotificationArea.POWER_GUID,
            NotificationArea.MICROPHONE_GUID,
            NotificationArea.MEETNOW_GUID
        };

        private readonly NotificationArea _notificationArea;
        private readonly SettingsService _settings;
        private readonly Dispatcher _dispatcher;
        private readonly HashSet<NotifyIcon> _watched = new();
        private List<(string Path, uint? Uid, Guid? IconGuid)> _promoted = new();
        private bool _refreshPending;

        public ObservableCollection<NotifyIcon> Visible { get; } = new();
        public ObservableCollection<NotifyIcon> Overflow { get; } = new();

        public TrayModel(NotificationArea notificationArea, SettingsService settings, Dispatcher dispatcher)
        {
            _notificationArea = notificationArea;
            _settings = settings;
            _dispatcher = dispatcher;

            LoadPromotedFromWindows();

            if (_notificationArea?.TrayIcons != null)
            {
                _notificationArea.TrayIcons.CollectionChanged += OnTrayIconsChanged;
            }

            Refresh();
        }

        public bool IsVisibleInBar(NotifyIcon icon)
        {
            string id = SafeIdentifier(icon);
            if (id != null && _settings.Data.TrayIconVisibility.TryGetValue(id, out bool visible))
                return visible;

            return IsPromotedInWindows(icon);
        }

        public void SetVisibleInBar(NotifyIcon icon, bool visible)
        {
            string id = SafeIdentifier(icon);
            if (id == null) return;
            _settings.Data.TrayIconVisibility[id] = visible;
            _settings.Save();
            Refresh();
        }

        /// <summary>Todos los iconos gestionables (barra + desbordamiento), para la configuración.</summary>
        public IReadOnlyList<NotifyIcon> All => Visible.Concat(Overflow).ToList();

        public event EventHandler Changed;

        /// <summary>Olvida las decisiones propias y vuelve a lo configurado en Windows.</summary>
        public void ResetToWindows()
        {
            _settings.Data.TrayIconVisibility.Clear();
            _settings.Save();
            LoadPromotedFromWindows();
            Refresh();
        }

        private void OnTrayIconsChanged(object sender, NotifyCollectionChangedEventArgs e) => ScheduleRefresh();

        private void OnIconPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            // La ruta/GUID pueden llegar después del primer aviso.
            if (e.PropertyName is nameof(NotifyIcon.Path) or nameof(NotifyIcon.GUID) or nameof(NotifyIcon.IsHidden))
                ScheduleRefresh();
        }

        private void ScheduleRefresh()
        {
            if (_refreshPending) return;
            _refreshPending = true;
            _dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
            {
                _refreshPending = false;
                Refresh();
            }));
        }

        private void Refresh()
        {
            var all = _notificationArea?.TrayIcons?.ToList() ?? new List<NotifyIcon>();

            foreach (var icon in all.Where(i => _watched.Add(i)))
                icon.PropertyChanged += OnIconPropertyChanged;
            foreach (var gone in _watched.Where(i => !all.Contains(i)).ToList())
            {
                gone.PropertyChanged -= OnIconPropertyChanged;
                _watched.Remove(gone);
            }

            var shown = all.Where(i => !i.IsHidden && !SystemIconGuids.Contains(i.GUID.ToString())).ToList();

            Sync(Visible, shown.Where(IsVisibleInBar).ToList());
            Sync(Overflow, shown.Where(i => !IsVisibleInBar(i)).ToList());
            Changed?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>Actualiza la colección con movimientos mínimos para no recrear controles.</summary>
        private static void Sync(ObservableCollection<NotifyIcon> target, IList<NotifyIcon> desired)
        {
            for (int i = target.Count - 1; i >= 0; i--)
                if (!desired.Contains(target[i])) target.RemoveAt(i);

            for (int i = 0; i < desired.Count; i++)
            {
                int current = target.IndexOf(desired[i]);
                if (current < 0) target.Insert(i, desired[i]);
                else if (current != i) target.Move(current, i);
            }
        }

        private bool IsPromotedInWindows(NotifyIcon icon)
        {
            string path;
            try { path = icon.Path; } catch { return false; }
            if (string.IsNullOrEmpty(path)) return false;

            return _promoted.Any(p =>
            {
                // Windows identifica el icono por GUID si lo tiene; si no, por ejecutable + UID.
                if (p.IconGuid != null) return icon.GUID == p.IconGuid.Value;
                return icon.GUID == Guid.Empty
                       && string.Equals(p.Path, path, StringComparison.OrdinalIgnoreCase)
                       && (p.Uid == null || p.Uid == icon.UID);
            });
        }

        private void LoadPromotedFromWindows()
        {
            var list = new List<(string, uint?, Guid?)>();
            try
            {
                using var root = Registry.CurrentUser.OpenSubKey(@"Control Panel\NotifyIconSettings");
                if (root != null)
                {
                    foreach (string name in root.GetSubKeyNames())
                    {
                        using var key = root.OpenSubKey(name);
                        if (key == null || Convert.ToInt32(key.GetValue("IsPromoted") ?? 0) != 1) continue;

                        string exe = ShellItemHelper.ExpandKnownFolderPath(key.GetValue("ExecutablePath") as string);
                        if (string.IsNullOrEmpty(exe)) continue;

                        object uidValue = key.GetValue("UID");
                        uint? uid = uidValue == null ? null : Convert.ToUInt32(uidValue);
                        Guid? iconGuid = Guid.TryParse(key.GetValue("IconGuid") as string, out Guid g) ? g : null;
                        list.Add((Path.GetFullPath(exe), uid, iconGuid));
                    }
                }
            }
            catch { }

            _promoted = list;
        }

        private static string SafeIdentifier(NotifyIcon icon)
        {
            try { return icon.Identifier; } catch { return null; }
        }

        public void Dispose()
        {
            if (_notificationArea?.TrayIcons != null)
                _notificationArea.TrayIcons.CollectionChanged -= OnTrayIconsChanged;
            foreach (var icon in _watched) icon.PropertyChanged -= OnIconPropertyChanged;
            _watched.Clear();
        }
    }
}
