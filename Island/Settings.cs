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
    public int OffsetY { get; set; } = 8;            // отступ сверху, px
    public int Scale { get; set; } = 100;            // размер острова, %
    public int GlassOpacity { get; set; } = 100;
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
        OffsetY = Math.Clamp(OffsetY, 0, 200);
        Scale = Math.Clamp(Scale, 70, 150);
        GlassOpacity = Math.Clamp(GlassOpacity, 55, 100);
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
