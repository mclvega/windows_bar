using System;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Windows.Threading;
using WindowsBar.Interop;

namespace WindowsBar.Services
{
    /// <summary>Idioma de entrada de la ventana en primer plano (indicador "ESP", "ENG"...).</summary>
    public class InputLanguageService : ObservableObject, IDisposable
    {
        private readonly DispatcherTimer _timer;
        private readonly uint _ownProcessId = (uint)Environment.ProcessId;

        private string _shortName = "";
        private string _fullName = "";
        private bool _isVisible;

        public string ShortName { get => _shortName; private set => Set(ref _shortName, value); }
        public string FullName { get => _fullName; private set => Set(ref _fullName, value); }

        /// <summary>Como en Windows: solo se muestra si hay más de un idioma/teclado instalado.</summary>
        public bool IsVisible { get => _isVisible; private set => Set(ref _isVisible, value); }

        public InputLanguageService(Dispatcher dispatcher)
        {
            _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(300), DispatcherPriority.Background, (_, _) => Refresh(), dispatcher);
            Refresh();
        }

        private void Refresh()
        {
            IntPtr fg = NativeMethods.GetForegroundWindow();
            uint thread = NativeMethods.GetWindowThreadProcessId(fg, out uint pid);

            // Si la ventana activa es nuestra barra, mantenemos el último idioma visto.
            if (pid == _ownProcessId && !string.IsNullOrEmpty(ShortName)) return;

            IntPtr hkl = NativeMethods.GetKeyboardLayout(thread);
            int langId = (int)((long)hkl & 0xFFFF);

            try
            {
                var culture = CultureInfo.GetCultureInfo(langId);
                ShortName = Abbreviate(culture);
                FullName = culture.DisplayName;
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex.Message);
            }

            try { IsVisible = System.Windows.Forms.InputLanguage.InstalledInputLanguages.Count > 1; }
            catch { IsVisible = true; }
        }

        /// <summary>
        /// Abreviatura de 3 letras con el nombre nativo del idioma
        /// ("español" → ESP, "English" → ENG, "français" → FRA).
        /// </summary>
        private static string Abbreviate(CultureInfo culture)
        {
            string native = (culture.IsNeutralCulture ? culture : culture.Parent).NativeName;
            if (string.IsNullOrWhiteSpace(native)) native = culture.NativeName;

            string plain = new string(native.Normalize(NormalizationForm.FormD)
                .Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark && char.IsLetter(c))
                .ToArray());

            if (plain.Length >= 3 && plain.All(c => c < 128))
                return plain.Substring(0, 3).ToUpperInvariant();

            // Alfabetos no latinos: abreviatura ISO de Windows.
            return culture.ThreeLetterWindowsLanguageName;
        }

        public void Dispose() => _timer.Stop();
    }
}
