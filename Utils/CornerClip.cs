using System.Windows;
using System.Windows.Media;

namespace ArchiveThumbViewer.Utils
{
    public static class CornerClip
    {
        public static readonly DependencyProperty RadiusProperty =
            DependencyProperty.RegisterAttached(
                "Radius",
                typeof(double),
                typeof(CornerClip),
                new PropertyMetadata(0.0, OnRadiusChanged));

        public static void SetRadius(DependencyObject element, double value) =>
            element.SetValue(RadiusProperty, value);

        public static double GetRadius(DependencyObject element) =>
            (double)element.GetValue(RadiusProperty);

        private static void OnRadiusChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is FrameworkElement fe)
            {
                fe.SizeChanged -= Fe_SizeChanged;
                if ((double)e.NewValue > 0)
                {
                    fe.SizeChanged += Fe_SizeChanged;
                    ApplyClip(fe);
                }
                else
                {
                    fe.ClearValue(UIElement.ClipProperty);
                }
            }
        }

        private static void Fe_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (sender is FrameworkElement fe) ApplyClip(fe);
        }

        private static void ApplyClip(FrameworkElement fe)
        {
            double r = GetRadius(fe);
            if (fe.ActualWidth <= 0 || fe.ActualHeight <= 0) return;

            fe.Clip = new RectangleGeometry
            {
                Rect = new Rect(0, 0, fe.ActualWidth, fe.ActualHeight),
                RadiusX = r,
                RadiusY = r
            };
        }
    }
}
