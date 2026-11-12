using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Island;

public class AppShortcut
{
    public string Path { get; set; } = "";
    public string Name { get; set; } = "";
}

/// <summary>Настройки хранятся в %APPDATA%\Moku\settings.json</summary>
public class Settings
{
    public string Align { get; set; } = "Center";   // Left | Center | Right
    public string VerticalAlign { get; set; } = "Top"; // Top | Center | Bottom
    public int OffsetX { get; set; } = 0;
    public int OffsetY { get; set; } = 8;            // отступ сверху, px
    public int Scale { get; set; } = 100;            // размер острова, %
    public int GlassOpacity { get; set; } = 100;
    public int FullscreenOpacity { get; set; } = 35;
    public int CornerRadius { get; set; } = 18;
    public string Theme { get; set; } = "Midnight";
    public string Accent { get; set; } = "#FF7C5CFF"; // цвет для музыки и событий
    public string Language { get; set; } = "en"; // en | ru
    public List<AppShortcut> Apps { get; set; } = new();

    static string FilePath => System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Moku", "settings.json");

    static readonly Regex HexColor = new("^#([0-9A-Fa-f]{6}|[0-9A-Fa-f]{8})$");

    /// <summary>Приводит значения к допустимым (файл могли править руками или он частично повреждён).</summary>
    public void Normalize()
    {
        if (Align is not ("Left" or "Center" or "Right")) Align = "Center";
        if (VerticalAlign is not ("Top" or "Center" or "Bottom")) VerticalAlign = "Top";
        OffsetX = Math.Clamp(OffsetX, -500, 500);
        OffsetY = Math.Clamp(OffsetY, -500, 500);
        Scale = Math.Clamp(Scale, 70, 150);
        GlassOpacity = Math.Clamp(GlassOpacity, 0, 100);
        FullscreenOpacity = Math.Clamp(FullscreenOpacity, 0, 100);
        CornerRadius = Math.Clamp(CornerRadius, 0, 40);
        if (Theme is not ("Midnight" or "Ocean" or "Amethyst" or "Emerald" or "Rose")) Theme = "Midnight";
        if (Accent == null || !HexColor.IsMatch(Accent)) Accent = "#FF7C5CFF";
        if (Language is not ("en" or "ru")) Language = "en";
        Apps ??= new();
        Apps = Apps.Where(a => a != null && !string.IsNullOrWhiteSpace(a.Path) &&
            !string.IsNullOrWhiteSpace(a.Name)).Take(60).ToList();
    }

    public static Settings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                try
                {
                    var s = JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath)) ?? new Settings();
                    s.Normalize();
                    return s;
                }
                catch (JsonException ex)
                {
                    Log.Error("settings.json повреждён, сохранена копия .corrupt, применены значения по умолчанию", ex);
                    File.Copy(FilePath, FilePath + ".corrupt", overwrite: true);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Error("Не удалось прочитать settings.json", ex);
        }
        return new Settings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(FilePath)!);
            var tmp = FilePath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(this));
            File.Move(tmp, FilePath, overwrite: true);   // атомарная замена: файл не остаётся полузаписанным
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Error("Не удалось сохранить settings.json", ex);
        }
    }
}
