using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using ManagedShell.Common.Helpers;
using ManagedShell.WindowsTasks;
using WindowsBar.Interop;
using WindowsBar.Services;
using WindowsBar.Views;

namespace WindowsBar.Controls
{
    /// <summary>
    /// Vistas previas (miniaturas en vivo) de las ventanas de una app, encima de su botón.
    /// Hay una por barra: así nunca hay dos abiertas y se pasa de una app a otra sin parpadeo.
    /// </summary>
    public partial class TaskPreview : UserControl
    {
        private const int OpenDelayMs = 400;
        private const int CloseDelayMs = 300;
        private const double GapAboveButton = 10;
        private const int PeekDelayMs = 400;

        private readonly DispatcherTimer _openTimer;
        private readonly DispatcherTimer _closeTimer;
        private readonly DispatcherTimer _peekTimer;
        private FrameworkElement _peekCard;
        private IntPtr _peekedWindow;
        private IntPtr _popupHandle;
        private FrameworkElement _pendingTarget;
        private TaskGroup _pendingGroup;
        private TaskGroup _group;

        private class PreviewRow
        {
            public ApplicationWindow Window { get; init; }
            public IntPtr Handle { get; init; }
            public string Title { get; init; }
            public ImageSource Icon { get; init; }
            public ImageSource AppIcon { get; init; }
            public bool IsActive { get; init; }
        }

        public TaskPreview()
        {
            InitializeComponent();

            _openTimer = new DispatcherTimer(DispatcherPriority.Normal, Dispatcher) { Interval = TimeSpan.FromMilliseconds(OpenDelayMs) };
            _openTimer.Tick += (_, _) =>
            {
                _openTimer.Stop();
                if (_pendingGroup != null && _pendingTarget?.IsMouseOver == true) Show(_pendingTarget, _pendingGroup);
            };

            _closeTimer = new DispatcherTimer(DispatcherPriority.Normal, Dispatcher) { Interval = TimeSpan.FromMilliseconds(CloseDelayMs) };
            _closeTimer.Tick += (_, _) => Close();

            _peekTimer = new DispatcherTimer(DispatcherPriority.Normal, Dispatcher) { Interval = TimeSpan.FromMilliseconds(PeekDelayMs) };
            _peekTimer.Tick += (_, _) =>
            {
                _peekTimer.Stop();
                if (_peekCard?.IsMouseOver == true && _peekCard.DataContext is PreviewRow row) StartPeek(row);
            };

            PreviewPopup.CustomPopupPlacementCallback = CenterAboveTarget;
        }

        private TaskbarWindow Host => Window.GetWindow(this) as TaskbarWindow;

        #region API para los botones de la barra
        /// <summary>El ratón entró en el botón de una app abierta.</summary>
        public void HoverEnter(FrameworkElement target, TaskGroup group)
        {
            _closeTimer.Stop();

            // Ya hay un panel abierto (de esta u otra app): se cambia al instante.
            if (PreviewPopup.IsOpen)
            {
                Show(target, group);
                return;
            }

            _pendingTarget = target;
            _pendingGroup = group;
            _openTimer.Stop();
            _openTimer.Start();
        }

        /// <summary>El ratón salió del botón: se cierra tras un momento, salvo que entre en el panel.</summary>
        public void HoverLeave()
        {
            _openTimer.Stop();
            if (PreviewPopup.IsOpen)
            {
                _closeTimer.Stop();
                _closeTimer.Start();
            }
        }

        /// <summary>Abre el panel sin esperar (clic en una app con varias ventanas).</summary>
        public void ShowNow(FrameworkElement target, TaskGroup group)
        {
            _openTimer.Stop();
            _closeTimer.Stop();
            Show(target, group);
        }

        public void Close()
        {
            _openTimer.Stop();
            _closeTimer.Stop();
            EndPeek();
            PreviewPopup.IsOpen = false;
        }
        #endregion

        #region Vista "peek" de la ventana
        /// <summary>Muestra la ventana a tamaño real en el escritorio y difumina el resto, sin cambiar el foco.</summary>
        private void StartPeek(PreviewRow row)
        {
            if (_popupHandle == IntPtr.Zero || _peekedWindow == row.Handle) return;
            WindowHelper.PeekWindow(true, row.Handle, _popupHandle);
            _peekedWindow = row.Handle;
        }

        private void EndPeek()
        {
            _peekTimer.Stop();
            _peekCard = null;
            if (_peekedWindow == IntPtr.Zero) return;
            WindowHelper.PeekWindow(false, _peekedWindow, _popupHandle);
            _peekedWindow = IntPtr.Zero;
        }

        private void Card_MouseEnter(object sender, MouseEventArgs e)
        {
            if (sender is not FrameworkElement { DataContext: PreviewRow row } card) return;
            _peekCard = card;

            // Con una vista ya activa se cambia al momento; si no, tras el retardo de Windows.
            if (_peekedWindow != IntPtr.Zero)
            {
                StartPeek(row);
                return;
            }
            _peekTimer.Stop();
            _peekTimer.Start();
        }

        private void Card_MouseLeave(object sender, MouseEventArgs e) => _peekTimer.Stop();
        #endregion

        private void Show(FrameworkElement target, TaskGroup group)
        {
            if (_group != group)
            {
                DetachGroup();
                _group = group;
                _group.Windows.CollectionChanged += Group_WindowsChanged;
            }

            var rows = BuildRows();
            if (rows.Count == 0)
            {
                Close();
                return;
            }

            Cards.ItemsSource = rows;
            if (PreviewPopup.IsOpen && PreviewPopup.PlacementTarget != target)
            {
                // Pasar de una app a otra con el panel abierto: se fuerza a recolocarlo sobre el nuevo botón.
                PreviewPopup.PlacementTarget = target;
                double offset = PreviewPopup.HorizontalOffset;
                PreviewPopup.HorizontalOffset = offset + 1;
                PreviewPopup.HorizontalOffset = offset;
            }
            PreviewPopup.PlacementTarget = target;
            PreviewPopup.IsOpen = true;
        }

        private List<PreviewRow> BuildRows()
        {
            if (_group == null) return new List<PreviewRow>();
            var appIcon = _group.Icon;
            return _group.Windows.Where(TaskButton.IsRealWindow).Select(w => new PreviewRow
            {
                Window = w,
                Handle = w.Handle,
                Title = string.IsNullOrWhiteSpace(w.Title) ? _group.Name : w.Title,
                Icon = w.Icon ?? appIcon,
                AppIcon = appIcon,
                IsActive = w.State == ApplicationWindow.WindowState.Active
            }).ToList();
        }

        /// <summary>Se abrió o cerró una ventana de la app con el panel abierto: se rehacen las tarjetas.</summary>
        private void Group_WindowsChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
            {
                if (!PreviewPopup.IsOpen) return;
                var rows = BuildRows();
                if (rows.Count == 0) Close();
                else Cards.ItemsSource = rows;
            }));
        }

        private void DetachGroup()
        {
            if (_group != null) _group.Windows.CollectionChanged -= Group_WindowsChanged;
            _group = null;
        }

        /// <summary>Centrado sobre el botón y separado de la barra (los tamaños llegan en píxeles físicos).</summary>
        private CustomPopupPlacement[] CenterAboveTarget(Size popupSize, Size targetSize, Point offset)
        {
            double dpi = PreviewPopup.PlacementTarget != null ? VisualTreeHelper.GetDpi(PreviewPopup.PlacementTarget).DpiScaleY : 1;
            var point = new Point((targetSize.Width - popupSize.Width) / 2, -popupSize.Height - GapAboveButton * dpi);
            return new[] { new CustomPopupPlacement(point, PopupPrimaryAxis.Horizontal) };
        }

        #region Eventos del panel
        private void PreviewPopup_Opened(object sender, EventArgs e)
        {
            // Esquinas redondeadas de Windows 11 (el popup no admite transparencia para redondearlas en WPF).
            if (PresentationSource.FromVisual(PopupRoot) is HwndSource source)
            {
                _popupHandle = source.Handle;
                int round = NativeMethods.DWMWCP_ROUND;
                NativeMethods.DwmSetWindowAttribute(source.Handle, NativeMethods.DWMWA_WINDOW_CORNER_PREFERENCE, ref round, sizeof(int));

                // El panel sigue visible mientras se muestra una ventana con "peek".
                WindowHelper.ExcludeWindowFromPeek(source.Handle);
            }

            Host?.PopupOpened();
        }

        private void PreviewPopup_Closed(object sender, EventArgs e)
        {
            EndPeek();
            _popupHandle = IntPtr.Zero;
            Cards.ItemsSource = null; // descarga las miniaturas (DwmUnregisterThumbnail)
            DetachGroup();
            Host?.PopupClosed();
        }

        private void PopupRoot_MouseEnter(object sender, MouseEventArgs e) => _closeTimer.Stop();

        private void PopupRoot_MouseLeave(object sender, MouseEventArgs e)
        {
            EndPeek();
            _closeTimer.Stop();
            _closeTimer.Start();
        }

        private void Card_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is not PreviewRow row) return;
            Close();
            row.Window.BringToFront();
        }

        private void CloseWindow_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is not PreviewRow row) return;
            EndPeek();
            row.Window.Close();

            var rest = (Cards.ItemsSource as IEnumerable<PreviewRow>)?.Where(r => r != row).ToList();
            if (rest == null || rest.Count == 0) Close();
            else Cards.ItemsSource = rest;
        }

        private void TaskPreview_Unloaded(object sender, RoutedEventArgs e) => Close();
        #endregion
    }
}
