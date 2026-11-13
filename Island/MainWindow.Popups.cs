using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Application = System.Windows.Application;
using Color = System.Windows.Media.Color;

namespace Island;

public partial class MainWindow
{
    void RefreshPopupTheme()
    {
        if (Application.Current == null) return;
        var resources = Application.Current.Resources;
        resources["MokuPopupRadius"] = new CornerRadius(Math.Clamp(_s.CornerRadius, 0, 40));
        resources["MokuPopupItemRadius"] = new CornerRadius(Math.Clamp(_s.CornerRadius, 0, 10));
        if (_bodyBrush == null) return;
        var brush = _bodyBrush.CloneCurrentValue(); brush.Opacity = 1;
        foreach (var stop in brush.GradientStops)
        {
            // Retain the island's hue without making menu text illegible on a bright custom theme.
            var c = stop.Color;
            double scale = Math.Max(c.R, Math.Max(c.G, c.B)) > 100 ? 0.32 : 1;
            stop.Color = Color.FromRgb((byte)Math.Clamp(c.R * scale + 12, 0, 255), (byte)Math.Clamp(c.G * scale + 12, 0, 255), (byte)Math.Clamp(c.B * scale + 14, 0, 255));
        }
        brush.Freeze(); resources["MokuPopupSurface"] = brush;
    }
    void UpdatePreviewClip(Border host)
    {
        // Border.ClipToBounds alone is rectangular in WPF; explicitly clip the image corners.
        double radius = Math.Clamp(_s.CornerRadius, 0, Math.Min(host.ActualWidth, host.ActualHeight) / 2);
        host.Clip = new RectangleGeometry(new Rect(0, 0, Math.Max(0, host.ActualWidth), Math.Max(0, host.ActualHeight)), radius, radius);
    }
}
