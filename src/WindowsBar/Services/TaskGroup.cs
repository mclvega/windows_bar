using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Media;
using ManagedShell.WindowsTasks;
using static ManagedShell.Interop.NativeMethods;

namespace WindowsBar.Services
{
    /// <summary>Un botón de la barra: una app (anclada y/o con ventanas abiertas).</summary>
    public class TaskGroup : ObservableObject
    {
        private ImageSource _pinnedIcon;
        private PinnedApp _pin;
        private string _fallbackName;

        public TaskGroup(string key) { Key = key; }

        public string Key { get; }

        public ObservableCollection<ApplicationWindow> Windows { get; } = new();

        public PinnedApp Pin
        {
            get => _pin;
            set
            {
                if (Set(ref _pin, value))
                {
                    OnPropertyChanged(nameof(IsPinned));
                    _pinnedIcon = null;
                    RaiseAll();
                }
            }
        }

        public bool IsPinned => Pin != null;
        public bool IsRunning => Windows.Count > 0;
        public bool HasMultipleWindows => Windows.Count > 1;
        public bool IsActive => Windows.Any(w => w.State == ApplicationWindow.WindowState.Active);
        public bool IsFlashing => !IsActive && Windows.Any(w => w.State == ApplicationWindow.WindowState.Flashing);

        public string Name
        {
            get
            {
                if (Pin != null && !string.IsNullOrEmpty(Pin.Name)) return Pin.Name;
                var first = Windows.FirstOrDefault();
                if (first != null) return _fallbackName ??= PinnedAppsService.GetAppName(first);
                return Key;
            }
        }

        /// <summary>AUMID de paquete de la app (Store/WinUI), o null.</summary>
        private string PackagedAumid
        {
            get
            {
                if (_packagedAumidResolved) return _packagedAumid;
                var first = Windows.FirstOrDefault();
                if (first == null) return Pin != null && Interop.AppIdentity.IsPackaged(Pin.Aumid) ? Pin.Aumid : null;
                string aumid = Interop.AppIdentity.GetAumid(first);
                _packagedAumid = Interop.AppIdentity.IsPackaged(aumid) ? aumid : null;
                _packagedAumidResolved = true;
                return _packagedAumid;
            }
        }

        private string _packagedAumid;
        private bool _packagedAumidResolved;
        private ImageSource _packagedIcon;

        public ImageSource Icon
        {
            get
            {
                // Apps empaquetadas: su ventana suele dar un icono genérico; usamos el del paquete.
                string packaged = PackagedAumid;
                if (packaged != null)
                {
                    _packagedIcon ??= Interop.ShellItemHelper.GetIcon(Interop.AppIdentity.AppsFolderPath(packaged), 32);
                    if (_packagedIcon != null) return _packagedIcon;
                }

                var window = Windows.FirstOrDefault(w => w.Icon != null);
                if (window != null) return window.Icon;
                if (Pin == null) return null;
                return _pinnedIcon ??= Interop.ShellItemHelper.GetIcon(Pin.IconPath, 32)
                                       ?? Interop.ShellItemHelper.GetIcon(Pin.LaunchPath, 32)
                                       ?? Interop.ShellItemHelper.GetIcon(Pin.TargetPath, 32);
            }
        }

        public string ToolTip
        {
            get
            {
                if (Windows.Count == 1) return string.IsNullOrWhiteSpace(Windows[0].Title) ? Name : Windows[0].Title;
                if (Windows.Count > 1) return $"{Name} - {Windows.Count} ventanas";
                return Name;
            }
        }

        /// <summary>Las apps abiertas ya muestran sus vistas previas: el tooltip queda solo para las ancladas cerradas.</summary>
        public string ButtonToolTip => IsRunning ? null : ToolTip;

        /// <summary>Ventana con progreso (descargas, copias...), si alguna lo informa.</summary>
        private ApplicationWindow ProgressWindow => Windows.FirstOrDefault(w => w.ProgressState != TBPFLAG.TBPF_NOPROGRESS);

        public bool HasProgress => ProgressWindow != null;
        public bool IsProgressIndeterminate => ProgressWindow?.ProgressState == TBPFLAG.TBPF_INDETERMINATE;
        public double ProgressValue => ProgressWindow?.ProgressValue ?? 0;
        public bool IsProgressError => ProgressWindow?.ProgressState == TBPFLAG.TBPF_ERROR;
        public bool IsProgressPaused => ProgressWindow?.ProgressState == TBPFLAG.TBPF_PAUSED;

        /// <summary>Qué ejecutar para abrir una nueva instancia.</summary>
        public string LaunchPath
        {
            get
            {
                if (Pin != null)
                {
                    // Windows ignora los parámetros al abrir un .lnk: con perfil, se abre su ejecutable.
                    bool shortcutWithProfile = !string.IsNullOrEmpty(Pin.Arguments) && !string.IsNullOrEmpty(Pin.TargetPath)
                        && Pin.LaunchPath?.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) == true;
                    return shortcutWithProfile ? Pin.TargetPath : Pin.LaunchPath;
                }
                var w = Windows.FirstOrDefault();
                if (w == null) return null;
                if (PackagedAumid != null) return Interop.AppIdentity.AppsFolderPath(PackagedAumid);
                if (w.IsUWP && !string.IsNullOrEmpty(w.AppUserModelID)) return Interop.AppIdentity.AppsFolderPath(w.AppUserModelID);
                return UnpinnedRelaunch?.File ?? w.WinFileName;
            }
        }

        /// <summary>Argumentos para <see cref="LaunchPath"/> (p. ej. el perfil de Chrome), o null.</summary>
        public string LaunchArguments
        {
            get
            {
                if (Pin != null) return Pin.Arguments;
                var w = Windows.FirstOrDefault();
                if (w == null || PackagedAumid != null || w.IsUWP) return null;
                return UnpinnedRelaunch?.Arguments;
            }
        }

        /// <summary>Comando de relanzamiento de la primera ventana (sin anclar): abre la misma variante (perfil).</summary>
        private Interop.RelaunchInfo UnpinnedRelaunch
        {
            get
            {
                var w = Windows.FirstOrDefault();
                return w == null ? null : Interop.AppIdentity.GetRelaunchInfo(w);
            }
        }

        public void RaiseAll()
        {
            OnPropertyChanged(nameof(IsRunning));
            OnPropertyChanged(nameof(HasMultipleWindows));
            OnPropertyChanged(nameof(IsActive));
            OnPropertyChanged(nameof(IsFlashing));
            OnPropertyChanged(nameof(Name));
            OnPropertyChanged(nameof(Icon));
            OnPropertyChanged(nameof(ToolTip));
            OnPropertyChanged(nameof(ButtonToolTip));
            OnPropertyChanged(nameof(HasProgress));
            OnPropertyChanged(nameof(IsProgressIndeterminate));
            OnPropertyChanged(nameof(ProgressValue));
            OnPropertyChanged(nameof(IsProgressError));
            OnPropertyChanged(nameof(IsProgressPaused));
        }
    }
}
