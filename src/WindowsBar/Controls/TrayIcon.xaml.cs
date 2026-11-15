using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using ManagedShell.Common.Helpers;
using ManagedShell.Interop;
using WindowsBar.Views;
using NotifyIconModel = ManagedShell.WindowsTray.NotifyIcon;

namespace WindowsBar.Controls
{
    /// <summary>
    /// Icono de bandeja de otra app. Reenvía los clics y el ratón a la app propietaria
    /// para que muestre su propio menú/ventana. Mayús+clic derecho abre nuestro menú
    /// para mover el icono entre la barra y el desbordamiento.
    /// </summary>
    public partial class TrayIcon : UserControl
    {
        public static readonly DependencyProperty IconWidthProperty =
            DependencyProperty.Register(nameof(IconWidth), typeof(double), typeof(TrayIcon), new PropertyMetadata(24.0));

        public static readonly DependencyProperty IconHeightProperty =
            DependencyProperty.Register(nameof(IconHeight), typeof(double), typeof(TrayIcon), new PropertyMetadata(40.0));

        public double IconWidth { get => (double)GetValue(IconWidthProperty); set => SetValue(IconWidthProperty, value); }
        public double IconHeight { get => (double)GetValue(IconHeightProperty); set => SetValue(IconHeightProperty, value); }

        public TrayIcon()
        {
            InitializeComponent();
        }

        private NotifyIconModel Model => DataContext as NotifyIconModel;

        private static int DoubleClickTime => System.Windows.Forms.SystemInformation.DoubleClickTime;

        /// <summary>La barra que contiene el icono (también si está dentro del desbordamiento).</summary>
        private TaskbarWindow FindHost()
        {
            if (Window.GetWindow(this) is TaskbarWindow direct) return direct;

            DependencyObject node = this;
            while (node != null && node is not Popup)
                node = LogicalTreeHelper.GetParent(node) ?? System.Windows.Media.VisualTreeHelper.GetParent(node);

            return node is Popup popup && popup.PlacementTarget != null
                ? Window.GetWindow(popup.PlacementTarget) as TaskbarWindow
                : null;
        }

        private Popup FindPopup()
        {
            DependencyObject node = this;
            while (node != null && node is not Popup)
                node = LogicalTreeHelper.GetParent(node) ?? System.Windows.Media.VisualTreeHelper.GetParent(node);
            return node as Popup;
        }

        private void Icon_MouseDown(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;
            FindHost()?.SetTrayHost();

            if (e.ChangedButton != MouseButton.Left && !IsShiftRightClick(e))
                Model?.IconMouseDown(e.ChangedButton, MouseHelper.GetCursorPositionParam(), DoubleClickTime);
        }

        private void Icon_MouseUp(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;
            var model = Model;
            if (model == null) return;

            if (IsShiftRightClick(e))
            {
                ShowOwnMenu(model);
                return;
            }

            if (e.ChangedButton == MouseButton.Left)
                model.IconMouseDown(e.ChangedButton, MouseHelper.GetCursorPositionParam(), DoubleClickTime);

            model.IconMouseUp(e.ChangedButton, MouseHelper.GetCursorPositionParam(), DoubleClickTime);

            // Al abrir la app o su menú, el panel de desbordamiento se cierra como en Windows.
            var popup = FindPopup();
            if (popup != null) popup.IsOpen = false;
        }

        private void Icon_MouseEnter(object sender, MouseEventArgs e)
        {
            e.Handled = true;
            var model = Model;
            if (model == null) return;

            // Posición del icono para Shell_NotifyIconGetRect (formato que espera ManagedShell).
            Point location = IconBorder.PointToScreen(new Point(0, 0));
            double dpi = PresentationSource.FromVisual(this)?.CompositionTarget.TransformToDevice.M11 ?? 1;
            model.Placement = new NativeMethods.Rect
            {
                Top = (int)location.Y,
                Left = (int)location.X,
                Bottom = (int)(IconBorder.ActualHeight * dpi),
                Right = (int)(IconBorder.ActualWidth * dpi)
            };
            model.IconMouseEnter(MouseHelper.GetCursorPositionParam());
        }

        private void Icon_MouseLeave(object sender, MouseEventArgs e)
        {
            e.Handled = true;
            Model?.IconMouseLeave(MouseHelper.GetCursorPositionParam());
        }

        private void Icon_MouseMove(object sender, MouseEventArgs e)
        {
            e.Handled = true;
            Model?.IconMouseMove(MouseHelper.GetCursorPositionParam());
        }

        private static bool IsShiftRightClick(MouseButtonEventArgs e)
            => e.ChangedButton == MouseButton.Right && (Keyboard.Modifiers & ModifierKeys.Shift) != 0;

        private void ShowOwnMenu(NotifyIconModel model)
        {
            var tray = App.Services.Tray;
            bool visible = tray.IsVisibleInBar(model);

            var item = new MenuItem
            {
                Header = visible ? "Mover al desbordamiento (^)" : "Mostrar en la barra de tareas"
            };
            item.Click += (_, _) => tray.SetVisibleInBar(model, !visible);

            var menu = new ContextMenu { PlacementTarget = this, Placement = PlacementMode.Top };
            menu.Items.Add(item);
            menu.IsOpen = true;
        }
    }
}
