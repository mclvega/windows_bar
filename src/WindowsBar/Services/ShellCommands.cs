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
            if (host != null && ImmersiveLauncher.ShowStart(host.Screen.HMonitor)) return;
            SendCombo(VK_LWIN);
        }

        public static void Search(TaskbarWindow host) => HotkeyOnMonitor(host, VK_LWIN, 'S');
        public static void TaskView(TaskbarWindow host) => HotkeyOnMonitor(host, VK_LWIN, VK_TAB);
        public static void Widgets(TaskbarWindow host) => HotkeyOnMonitor(host, VK_LWIN, 'W');
        public static void QuickSettings(TaskbarWindow host) => HotkeyOnMonitor(host, VK_LWIN, 'A');
        public static void NotificationCenter(TaskbarWindow host) => HotkeyOnMonitor(host, VK_LWIN, 'N');
        public static void StartContextMenu(TaskbarWindow host) => HotkeyOnMonitor(host, VK_LWIN, 'X');
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

        public static void Open(string target, string args = "")
        {
            try
            {
                Process.Start(new ProcessStartInfo(target, args) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"No se pudo abrir {target}: {ex.Message}");
            }
        }

        /// <summary>
        /// Explorer coloca los paneles de los atajos en el monitor de la ventana en primer plano
        /// y de la "bandeja" registrada: activamos la barra de ese monitor antes de enviar el atajo.
        /// </summary>
        private static void HotkeyOnMonitor(TaskbarWindow host, ushort modifier, ushort key)
        {
            if (host != null)
            {
                host.SetTrayHost();
                ImmersiveShellHelper.AllowExplorerFocus();
                NativeMethods.SetForegroundWindow(host.Handle);
            }

            SendCombo(modifier, key);

            if (host != null) FlyoutPositioner.EnsureOnMonitor(host);
        }

        private static void HotkeyOnMonitor(TaskbarWindow host, ushort modifier, char key) => HotkeyOnMonitor(host, modifier, (ushort)key);

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
