using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using ManagedShell.UWPInterop;
using WindowsBar.Interop;
using WindowsBar.Views;

namespace WindowsBar.Services
{
    /// <summary>
    /// Abre los paneles nativos de Windows (Inicio, Búsqueda, etc.). Reciben la barra desde la que
    /// se hizo clic para que el panel aparezca en el monitor de esa barra.
    /// </summary>
    public static class ShellCommands
    {
        private const ushort VK_LWIN = 0x5B;
        private const ushort VK_TAB = 0x09;
        private const ushort VK_SPACE = 0x20;

        public static void StartMenu(TaskbarWindow host)
        {
            var before = FlyoutPositioner.Snapshot();
            bool viaLauncher = host != null && ImmersiveLauncher.ShowStart(host.Screen.HMonitor);
            DiagLog.Write($"[Inicio] barra={Describe(host)} IImmersiveLauncher={(viaLauncher ? "OK" : "falló → tecla Win")}");
            if (!viaLauncher) SendCombo(VK_LWIN);
            FlyoutPositioner.EnsureOnMonitor(host, before, "Inicio");
        }

        private static string Describe(TaskbarWindow host) =>
            host == null ? "(ninguna)" : $"{host.Screen.DeviceName}{(host.Screen.Primary ? " (principal)" : "")} {host.Screen.Bounds}";

        public static void Search(TaskbarWindow host) => HotkeyOnMonitor(host, VK_LWIN, 'S');
        public static void TaskView(TaskbarWindow host) => HotkeyOnMonitor(host, VK_LWIN, VK_TAB);
        public static void Widgets(TaskbarWindow host) => HotkeyOnMonitor(host, VK_LWIN, 'W');
        public static void QuickSettings(TaskbarWindow host) => HotkeyOnMonitor(host, VK_LWIN, 'A');
        public static void NotificationCenter(TaskbarWindow host) => HotkeyOnMonitor(host, VK_LWIN, 'N');
        public static void StartContextMenu(TaskbarWindow host)
        {
            // Lo que se abra desde el menú lo lanza Explorer en el monitor principal: se lleva a esta barra.
            App.Services.LaunchPlacer.ExpectFromShellMenu(host, "Menú Win+X");
            HotkeyOnMonitor(host, VK_LWIN, 'X');
        }
        public static void SwitchInputLanguage(TaskbarWindow host) => HotkeyOnMonitor(host, VK_LWIN, VK_SPACE);
        public static void ShowDesktop() => SendCombo(VK_LWIN, 'D');

        public static void TaskManager() => Open("taskmgr.exe");
        public static void TaskbarSettings() => Open("ms-settings:taskbar");
        public static void SoundSettings() => Open("ms-settings:sound");
        public static void NetworkSettings() => Open("ms-settings:network");
        public static void MicrophonePrivacySettings() => Open("ms-settings:privacy-microphone");
        public static void DateTimeSettings() => Open("ms-settings:dateandtime");
        public static void LanguageSettings() => Open("ms-settings:regionlanguage");
        public static void VolumeMixer() => Open("ms-settings:apps-volume");

        /// <summary>
        /// Abre una app pidiendo a Windows que la muestre en el monitor de la barra pulsada (como la barra nativa).
        /// Las apps que restauran su posición guardada lo ignoran: de esas se encarga <see cref="LaunchPlacer"/>.
        /// </summary>
        public static void Launch(string path, string args, TaskbarWindow host)
        {
            // Las apps empaquetadas ("shell:AppsFolder\AUMID") se abren a través de Explorer.
            bool viaExplorer = path.StartsWith("shell:", StringComparison.OrdinalIgnoreCase);
            try
            {
                GrantForegroundToLaunchedApps();
                var info = new NativeMethods.SHELLEXECUTEINFO
                {
                    cbSize = Marshal.SizeOf<NativeMethods.SHELLEXECUTEINFO>(),
                    fMask = NativeMethods.SEE_MASK_FLAG_NO_UI | (host != null ? NativeMethods.SEE_MASK_HMONITOR : 0),
                    lpFile = viaExplorer ? "explorer.exe" : path,
                    lpParameters = viaExplorer ? path : args,
                    nShow = NativeMethods.SW_SHOWNORMAL,
                    hIconOrMonitor = host?.Screen.HMonitor ?? IntPtr.Zero
                };
                if (!NativeMethods.ShellExecuteEx(ref info))
                    DiagLog.Write($"[Abrir] ERROR al abrir {path}: código {Marshal.GetLastWin32Error()}");
            }
            catch (Exception ex)
            {
                DiagLog.Write($"[Abrir] ERROR al abrir {path}: {ex.Message}");
            }
        }

        public static void Open(string target, string args = "")
        {
            try
            {
                GrantForegroundToLaunchedApps();
                Process.Start(new ProcessStartInfo(target, args) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                DiagLog.Write($"[Abrir] ERROR al abrir {target} {args}: {ex.Message}");
            }
        }

        /// <summary>
        /// Explorer coloca los paneles de los atajos en el monitor de la ventana en primer plano
        /// y de la "bandeja" registrada: activamos la barra de ese monitor antes de enviar el atajo.
        /// </summary>
        private static void HotkeyOnMonitor(TaskbarWindow host, ushort modifier, ushort key)
        {
            string action = $"Win+{KeyName(key)}";
            var before = FlyoutPositioner.Snapshot();

            if (host != null)
            {
                host.SetTrayHost();
                ImmersiveShellHelper.AllowExplorerFocus();
                bool fg = NativeMethods.SetForegroundWindow(host.Handle);
                DiagLog.Write($"[{action}] barra={Describe(host)} primer plano={fg}");
            }

            SendCombo(modifier, key);
            FlyoutPositioner.EnsureOnMonitor(host, before, action);
        }

        private static string KeyName(ushort key) => key switch
        {
            VK_TAB => "Tab",
            VK_SPACE => "Espacio",
            _ => ((char)key).ToString()
        };

        private static void HotkeyOnMonitor(TaskbarWindow host, ushort modifier, char key) => HotkeyOnMonitor(host, modifier, (ushort)key);

        /// <summary>
        /// Permite que la app que vamos a abrir pase a primer plano. Sin esto, Windows la abre detrás
        /// y la marca "pidiendo atención" (botón naranja). Para poder ceder el foco, nuestro proceso
        /// debe haber recibido la última entrada: enviamos una tecla sin uso (0xE8) y luego cedemos.
        /// </summary>
        private static void GrantForegroundToLaunchedApps()
        {
            const ushort VK_UNASSIGNED = 0xE8;
            SendCombo(VK_UNASSIGNED);
            NativeMethods.AllowSetForegroundWindow(NativeMethods.ASFW_ANY);
        }

        private static void SendCombo(params ushort[] keys)
        {
            var inputs = new NativeMethods.INPUT[keys.Length * 2];
            for (int i = 0; i < keys.Length; i++)
            {
                inputs[i] = Key(keys[i], false);
                inputs[inputs.Length - 1 - i] = Key(keys[i], true);
            }

            NativeMethods.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<NativeMethods.INPUT>());
        }

        private static void SendCombo(ushort modifier, char key) => SendCombo(modifier, (ushort)key);

        private static NativeMethods.INPUT Key(ushort vk, bool up) => new()
        {
            type = NativeMethods.INPUT_KEYBOARD,
            u = new NativeMethods.InputUnion
            {
                ki = new NativeMethods.KEYBDINPUT { wVk = vk, dwFlags = up ? NativeMethods.KEYEVENTF_KEYUP : 0 }
            }
        };
    }
}
