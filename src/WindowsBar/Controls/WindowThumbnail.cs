using System;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using WindowsBar.Interop;

namespace WindowsBar.Controls
{
    /// <summary>
    /// Miniatura en vivo de una ventana (la dibuja DWM encima de este elemento).
    /// Solo funciona dentro de una ventana sin transparencia por píxel (AllowsTransparency = false).
    /// </summary>
    public class WindowThumbnail : FrameworkElement
    {
        private const double MaxThumbWidth = 200;
        private const double MaxThumbHeight = 112;

        public static readonly DependencyProperty SourceHandleProperty = DependencyProperty.Register(
            nameof(SourceHandle), typeof(IntPtr), typeof(WindowThumbnail),
            new PropertyMetadata(IntPtr.Zero, (d, _) => ((WindowThumbnail)d).Register()));

        /// <summary>Ventana de la que se muestra la miniatura.</summary>
        public IntPtr SourceHandle
        {
            get => (IntPtr)GetValue(SourceHandleProperty);
            set => SetValue(SourceHandleProperty, value);
        }

        private IntPtr _thumbnail;
        private HwndSource _host;
        private Size _sourceSize;
        private NativeMethods.RECT _lastRect;

        public WindowThumbnail()
        {
            Loaded += (_, _) => Register();
            Unloaded += (_, _) => Unregister();
            LayoutUpdated += (_, _) => UpdateDestination();
        }

        private void Register()
        {
            Unregister();
            if (!IsLoaded || SourceHandle == IntPtr.Zero) return;

            // Una ventana minimizada no tiene contenido que mostrar: queda el icono de la app de fondo.
            if (NativeMethods.IsIconic(SourceHandle)) return;

            _host = PresentationSource.FromVisual(this) as HwndSource;
            if (_host == null) return;

            if (NativeMethods.DwmRegisterThumbnail(_host.Handle, SourceHandle, out _thumbnail) != 0)
            {
                _thumbnail = IntPtr.Zero;
                return;
            }

            if (NativeMethods.DwmQueryThumbnailSourceSize(_thumbnail, out var size) == 0 && size.cx > 0 && size.cy > 0)
                _sourceSize = new Size(size.cx, size.cy);

            InvalidateMeasure();
        }

        private void Unregister()
        {
            if (_thumbnail != IntPtr.Zero) NativeMethods.DwmUnregisterThumbnail(_thumbnail);
            _thumbnail = IntPtr.Zero;
            _host = null;
            _sourceSize = Size.Empty;
            _lastRect = default;
        }

        /// <summary>El tamaño se ajusta a la proporción de la ventana, sin pasar de 200×112.</summary>
        protected override Size MeasureOverride(Size availableSize)
        {
            if (_thumbnail == IntPtr.Zero || _sourceSize.IsEmpty) return new Size(0, 0);
            double scale = Math.Min(1, Math.Min(MaxThumbWidth / _sourceSize.Width, MaxThumbHeight / _sourceSize.Height));
            return new Size(_sourceSize.Width * scale, _sourceSize.Height * scale);
        }

        /// <summary>Coloca la miniatura sobre este elemento (DWM trabaja en píxeles físicos de la ventana anfitriona).</summary>
        private void UpdateDestination()
        {
            if (_thumbnail == IntPtr.Zero || _host?.RootVisual == null || _host.CompositionTarget == null) return;
            if (!_host.RootVisual.IsAncestorOf(this)) return;

            GeneralTransform toRoot = TransformToAncestor(_host.RootVisual);
            Matrix toDevice = _host.CompositionTarget.TransformToDevice;
            Point topLeft = toDevice.Transform(toRoot.Transform(new Point(0, 0)));
            Point bottomRight = toDevice.Transform(toRoot.Transform(new Point(ActualWidth, ActualHeight)));

            var rect = new NativeMethods.RECT
            {
                Left = (int)Math.Round(topLeft.X),
                Top = (int)Math.Round(topLeft.Y),
                Right = (int)Math.Round(bottomRight.X),
                Bottom = (int)Math.Round(bottomRight.Y)
            };
            if (rect.Left == _lastRect.Left && rect.Top == _lastRect.Top
                && rect.Right == _lastRect.Right && rect.Bottom == _lastRect.Bottom) return;
            _lastRect = rect;

            var props = new NativeMethods.DWM_THUMBNAIL_PROPERTIES
            {
                dwFlags = NativeMethods.DWM_TNP_RECTDESTINATION | NativeMethods.DWM_TNP_VISIBLE
                          | NativeMethods.DWM_TNP_OPACITY | NativeMethods.DWM_TNP_SOURCECLIENTAREAONLY,
                rcDestination = rect,
                opacity = 255,
                fVisible = true,
                fSourceClientAreaOnly = false
            };
            NativeMethods.DwmUpdateThumbnailProperties(_thumbnail, ref props);
        }
    }
}
