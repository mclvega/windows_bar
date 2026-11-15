using System;
using System.Windows;
using System.Windows.Controls;

namespace WindowsBar.Controls
{
    /// <summary>
    /// Panel de 3 hijos [izquierda, centro, derecha]: el centro se coloca centrado respecto
    /// al ancho total (como en Windows 11) y se desplaza solo si chocaría con los laterales.
    /// </summary>
    public class CenteringPanel : Panel
    {
        protected override Size MeasureOverride(Size available)
        {
            double height = 0;
            double sides = 0;

            for (int i = 0; i < InternalChildren.Count; i++)
            {
                if (i == 1) continue;
                var child = InternalChildren[i];
                child.Measure(available);
                sides += child.DesiredSize.Width;
                height = Math.Max(height, child.DesiredSize.Height);
            }

            if (InternalChildren.Count > 1)
            {
                var center = InternalChildren[1];
                center.Measure(new Size(Math.Max(0, available.Width - sides), available.Height));
                height = Math.Max(height, center.DesiredSize.Height);
            }

            double width = double.IsInfinity(available.Width) ? sides : available.Width;
            return new Size(width, double.IsInfinity(available.Height) ? height : available.Height);
        }

        protected override Size ArrangeOverride(Size final)
        {
            double leftWidth = 0, rightWidth = 0;

            if (InternalChildren.Count > 0)
            {
                var left = InternalChildren[0];
                leftWidth = left.DesiredSize.Width;
                left.Arrange(new Rect(0, 0, leftWidth, final.Height));
            }

            if (InternalChildren.Count > 2)
            {
                var right = InternalChildren[2];
                rightWidth = right.DesiredSize.Width;
                right.Arrange(new Rect(Math.Max(0, final.Width - rightWidth), 0, rightWidth, final.Height));
            }

            if (InternalChildren.Count > 1)
            {
                var center = InternalChildren[1];
                double available = Math.Max(0, final.Width - leftWidth - rightWidth);
                double width = Math.Min(center.DesiredSize.Width, available);
                double x = (final.Width - width) / 2;
                x = Math.Max(leftWidth, Math.Min(x, final.Width - rightWidth - width));
                center.Arrange(new Rect(x, 0, width, final.Height));
            }

            return final;
        }
    }
}
