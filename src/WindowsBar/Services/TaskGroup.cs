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

        public ImageSource Icon
        {
            get
            {
                var window = Windows.FirstOrDefault(w => w.Icon != null);
                if (window != null) return window.Icon;
                if (Pin == null) return null;
                return _pinnedIcon ??= Interop.ShellItemHelper.GetIcon(Pin.LaunchPath, 32)
                                       ?? Interop.ShellItemHelper.GetIcon(Pin.TargetPath, 32);
            }
        }

        public string ToolTip
        {
            get
            {
                if (Windows.Count == 1) return Windows[0].Title;
                if (Windows.Count > 1) return $"{Name} - {Windows.Count} ventanas";
                return Name;
            }
        }

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
                if (Pin != null) return Pin.LaunchPath;
                var w = Windows.FirstOrDefault();
                if (w == null) return null;
                if (w.IsUWP && !string.IsNullOrEmpty(w.AppUserModelID)) return @"shell:AppsFolder\" + w.AppUserModelID;
                return w.WinFileName;
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
            OnPropertyChanged(nameof(HasProgress));
            OnPropertyChanged(nameof(IsProgressIndeterminate));
            OnPropertyChanged(nameof(ProgressValue));
            OnPropertyChanged(nameof(IsProgressError));
            OnPropertyChanged(nameof(IsProgressPaused));
        }
    }
}
