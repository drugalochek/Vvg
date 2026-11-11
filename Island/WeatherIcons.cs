using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WPath = System.Windows.Shapes.Path;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;

namespace Island;

/// <summary>Иконки погоды, нарисованные векторами (20×20). Шрифты и картинки не нужны.</summary>
public static class WeatherIcons
{
    enum Key { ClearDay, ClearNight, PartlyDay, PartlyNight, Cloudy, Fog, Rain, Snow, Thunder }

    record Part(string D, string? Fill, string? Stroke, double W);

    static readonly Dictionary<Key, Part[]> Specs = new()
    {
        [Key.ClearDay] = new Part[]
        {
            new("M10,1.8V4.2 M10,15.8V18.2 M1.8,10H4.2 M15.8,10H18.2 M4.2,4.2L5.9,5.9 M14.1,14.1L15.8,15.8 M4.2,15.8L5.9,14.1 M14.1,5.9L15.8,4.2", null, "#FFD60A", 1.6),
            new("M6,10 A4,4 0 1 0 14,10 A4,4 0 1 0 6,10 Z", "#FFD60A", null, 0)
        },
        [Key.ClearNight] = new Part[]
        {
            new("M12.6,2.8 A7.4,7.4 0 1 0 17.2,13.2 A5.9,5.9 0 0 1 12.6,2.8 Z", "#E8EBF4", null, 0)
        },
        [Key.PartlyDay] = new Part[]
        {
            new("M7,1.4V2.8 M1.4,7H2.8 M11.2,7H12.6 M3,3L4,4 M11,3L10,4 M3,11L4,10", null, "#FFD60A", 1.4),
            new("M4,7 A3,3 0 1 0 10,7 A3,3 0 1 0 4,7 Z", "#FFD60A", null, 0),
            new("M7.5,17 A3.3,3.3 0 0 1 7.8,10.5 A4.6,4.6 0 0 1 16.4,11.2 A3,3 0 0 1 15.8,17 Z", "#D5DCE6", null, 0)
        },
        [Key.PartlyNight] = new Part[]
        {
            new("M8.2,2 A4.4,4.4 0 1 0 11.6,8.6 A3.6,3.6 0 0 1 8.2,2 Z", "#E8EBF4", null, 0),
            new("M7.5,17 A3.3,3.3 0 0 1 7.8,10.5 A4.6,4.6 0 0 1 16.4,11.2 A3,3 0 0 1 15.8,17 Z", "#D5DCE6", null, 0)
        },
        [Key.Cloudy] = new Part[]
        {
            new("M3.5,13 A2.8,2.8 0 0 1 3.8,7.6 A4,4 0 0 1 11.2,7 A2.8,2.8 0 0 1 11,13 Z", "#8F9BAB", null, 0),
            new("M7.5,17.5 A3.3,3.3 0 0 1 7.8,11 A4.6,4.6 0 0 1 16.4,11.7 A3,3 0 0 1 15.8,17.5 Z", "#D5DCE6", null, 0)
        },
        [Key.Fog] = new Part[]
        {
            new("M5.5,10 A3.3,3.3 0 0 1 5.8,3.6 A4.6,4.6 0 0 1 14.4,4.2 A3,3 0 0 1 14,10 Z", "#8F9BAB", null, 0),
            new("M2.5,12.5 H17.5 M4.5,15.3 H15.5 M2.5,18 H12.5", null, "#B4BECB", 1.6)
        },
        [Key.Rain] = new Part[]
        {
            new("M5.5,12 A3.3,3.3 0 0 1 5.8,5.5 A4.6,4.6 0 0 1 14.4,6.2 A3,3 0 0 1 14,12 Z", "#C3CCD8", null, 0),
            new("M6.6,14.3 L5.6,17.6 M10.2,14.3 L9.2,17.6 M13.8,14.3 L12.8,17.6", null, "#5AC8FA", 1.7)
        },
        [Key.Snow] = new Part[]
        {
            new("M5.5,12 A3.3,3.3 0 0 1 5.8,5.5 A4.6,4.6 0 0 1 14.4,6.2 A3,3 0 0 1 14,12 Z", "#C3CCD8", null, 0),
            new("M5.3,16 A1.1,1.1 0 1 0 7.5,16 A1.1,1.1 0 1 0 5.3,16 Z", "#FFFFFF", null, 0),
            new("M8.9,17.8 A1.1,1.1 0 1 0 11.1,17.8 A1.1,1.1 0 1 0 8.9,17.8 Z", "#FFFFFF", null, 0),
            new("M12.5,16 A1.1,1.1 0 1 0 14.7,16 A1.1,1.1 0 1 0 12.5,16 Z", "#FFFFFF", null, 0)
        },
        [Key.Thunder] = new Part[]
        {
            new("M5.5,12 A3.3,3.3 0 0 1 5.8,5.5 A4.6,4.6 0 0 1 14.4,6.2 A3,3 0 0 1 14,12 Z", "#8F9BAB", null, 0),
            new("M10.8,10.8 L7.6,15.8 L10.1,15.8 L9,19.4 L13.2,14.2 L10.7,14.2 L11.9,10.8 Z", "#FFD60A", null, 0)
        },
    };

    static readonly Dictionary<string, SolidColorBrush> Cache = new();

    static SolidColorBrush Brush(string hex)
    {
        if (!Cache.TryGetValue(hex, out var b))
        {
            b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
            b.Freeze();
            Cache[hex] = b;
        }
        return b;
    }

    static Key Pick(WeatherKind k, bool day) => k switch
    {
        WeatherKind.Clear => day ? Key.ClearDay : Key.ClearNight,
        WeatherKind.PartlyCloudy => day ? Key.PartlyDay : Key.PartlyNight,
        WeatherKind.Cloudy => Key.Cloudy,
        WeatherKind.Fog => Key.Fog,
        WeatherKind.Rain => Key.Rain,
        WeatherKind.Snow => Key.Snow,
        _ => Key.Thunder
    };

    public static UIElement Build(WeatherKind kind, bool day)
    {
        var cv = new Canvas { Width = 20, Height = 20, IsHitTestVisible = false };
        foreach (var p in Specs[Pick(kind, day)])
        {
            var path = new WPath { Data = Geometry.Parse(p.D), IsHitTestVisible = false };
            if (p.Fill != null) path.Fill = Brush(p.Fill);
            if (p.Stroke != null)
            {
                path.Stroke = Brush(p.Stroke);
                path.StrokeThickness = p.W;
                path.StrokeStartLineCap = PenLineCap.Round;
                path.StrokeEndLineCap = PenLineCap.Round;
                path.StrokeLineJoin = PenLineJoin.Round;
            }
            cv.Children.Add(path);
        }
        return cv;
    }
}
