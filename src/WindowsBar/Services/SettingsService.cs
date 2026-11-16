using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace WindowsBar.Services
{
    public class PinnedApp
    {
        /// <summary>Lo que se ejecuta: ruta a .lnk, a .exe o "shell:AppsFolder\AUMID".</summary>
        public string LaunchPath { get; set; }

        /// <summary>Ejecutable al que apunta (para emparejar con ventanas abiertas).</summary>
        public string TargetPath { get; set; }

        /// <summary>AppUserModelID, si se conoce (para emparejar con ventanas abiertas).</summary>
        public string Aumid { get; set; }

        public string Name { get; set; }
    }

    public class AppSettingsData
    {
        // ---- General ----
        public bool StartWithWindows { get; set; } = true;

        /// <summary>true: una barra en cada monitor. false: solo en el principal.</summary>
        public bool ShowOnAllMonitors { get; set; } = true;

        /// <summary>true: cada barra muestra todas las ventanas. false: solo las de su monitor.</summary>
        public bool ShowTasksOnAllTaskbars { get; set; } = true;

        public bool AutoHide { get; set; }

        /// <summary>true: apps centradas (Windows 11). false: alineadas a la izquierda.</summary>
        public bool CenterTasks { get; set; } = true;

        /// <summary>true: los botones de la barra se reordenan arrastrándolos.</summary>
        public bool DragToReorder { get; set; }

        // ---- Elementos de la barra ----
        public bool ShowWidgets { get; set; } = true;
        public bool ShowSearch { get; set; } = true;
        public bool ShowTaskView { get; set; } = true;
        public bool ShowInputLanguage { get; set; } = true;
        public bool ShowClockSeconds { get; set; }
        public bool ShowDesktopButton { get; set; } = true;

        /// <summary>Si ya se importaron las apps ancladas de la barra de Windows.</summary>
        public bool PinsImported { get; set; }

        public List<PinnedApp> PinnedApps { get; set; } = new();

        /// <summary>Identificador de icono de bandeja → visible en la barra (true) u oculto en el desbordamiento (false).</summary>
        public Dictionary<string, bool> TrayIconVisibility { get; set; } = new();

        /// <summary>Estado (ABState) de la barra nativa antes de ocultarla; null si no está oculta por nosotros.</summary>
        public int? NativeTaskbarStateBeforeHide { get; set; }
    }

    public class SettingsService
    {
        private static readonly string Folder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WindowsBar");

        private static readonly string FilePath = Path.Combine(Folder, "settings.json");

        private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

        public AppSettingsData Data { get; private set; }

        public event EventHandler Changed;

        public static SettingsService Load()
        {
            var service = new SettingsService();
            try
            {
                if (File.Exists(FilePath))
                {
                    service.Data = JsonSerializer.Deserialize<AppSettingsData>(File.ReadAllText(FilePath), JsonOptions);
                }
            }
            catch
            {
                // Archivo corrupto: se empieza con valores por defecto.
            }

            service.Data ??= new AppSettingsData();
            return service;
        }

        public void Save()
        {
            Directory.CreateDirectory(Folder);
            string tmp = FilePath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(Data, JsonOptions));
            File.Move(tmp, FilePath, true);
        }

        /// <summary>Guarda y avisa a la interfaz de que algo cambió.</summary>
        public void SaveAndNotify()
        {
            Save();
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }
}
