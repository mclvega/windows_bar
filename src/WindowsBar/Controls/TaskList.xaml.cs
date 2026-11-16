using System;
using System.Collections;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using WindowsBar.Services;
using WindowsBar.Views;

namespace WindowsBar.Controls
{
    public partial class TaskList : UserControl
    {
        private const double DraggedOpacity = 0.6;

        private TaskGroup _pressed;
        private Point _pressPoint;
        private bool _dragging;
        private FrameworkElement _draggedContainer;

        public TaskList()
        {
            InitializeComponent();
        }

        public TaskbarWindow Host { get; set; }

        public IEnumerable ItemsSource
        {
            get => Items.ItemsSource;
            set => Items.ItemsSource = value;
        }

        #region Reordenar arrastrando
        private void Items_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // No se marca como manejado: el clic normal sobre el botón tiene que seguir funcionando.
            _pressed = (e.OriginalSource as FrameworkElement)?.DataContext as TaskGroup;
            _pressPoint = e.GetPosition(Items);
        }

        private void Items_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (_pressed == null || e.LeftButton != MouseButtonState.Pressed) return;

            Point position = e.GetPosition(Items);
            if (!_dragging)
            {
                if (!App.Services.Settings.Data.DragToReorder || Host == null) return;
                if (Math.Abs(position.X - _pressPoint.X) < SystemParameters.MinimumHorizontalDragDistance) return;
                StartDrag();
            }

            Host.TaskGroups.DragTo(_pressed, TargetIndex(position.X));
            MarkDraggedContainer();
            e.Handled = true;
        }

        private void Items_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            // Soltar tras arrastrar no debe abrir ni minimizar la app.
            if (_dragging) e.Handled = true;
            FinishDrag();
        }

        private void Items_LostMouseCapture(object sender, MouseEventArgs e)
        {
            // El evento sube también desde el botón (que pierde la captura cuando la toma la lista): solo cuenta la nuestra.
            if (e.OriginalSource == Items) FinishDrag();
        }

        private void StartDrag()
        {
            _dragging = true;
            Host.TaskGroups.BeginDrag();
            // Al capturar el ratón el botón deja de estar pulsado, así que no se dispara su Click.
            Items.CaptureMouse();
            MarkDraggedContainer();
        }

        private void FinishDrag()
        {
            var group = _pressed;
            _pressed = null;
            if (!_dragging) return;

            _dragging = false;
            if (_draggedContainer != null) _draggedContainer.Opacity = 1;
            _draggedContainer = null;
            if (Items.IsMouseCaptured) Items.ReleaseMouseCapture();
            Host?.TaskGroups.EndDrag(group);
        }

        /// <summary>Atenúa el botón arrastrado (el contenedor puede cambiar al moverse en la lista).</summary>
        private void MarkDraggedContainer()
        {
            var container = Items.ItemContainerGenerator.ContainerFromItem(_pressed) as FrameworkElement;
            if (container == _draggedContainer) return;
            if (_draggedContainer != null) _draggedContainer.Opacity = 1;
            _draggedContainer = container;
            if (container != null) container.Opacity = DraggedOpacity;
        }

        /// <summary>
        /// Posición en la que quedaría el botón arrastrado: cuántos de los demás botones
        /// tienen su centro a la izquierda del ratón.
        /// </summary>
        private int TargetIndex(double mouseX)
        {
            int index = 0;
            for (int i = 0; i < Items.Items.Count; i++)
            {
                if (Items.Items[i] == _pressed) continue;
                if (Items.ItemContainerGenerator.ContainerFromIndex(i) is not FrameworkElement container) continue;
                double centerX = container.TranslatePoint(new Point(container.ActualWidth / 2, 0), Items).X;
                if (centerX < mouseX) index++;
            }
            return index;
        }
        #endregion
    }
}
