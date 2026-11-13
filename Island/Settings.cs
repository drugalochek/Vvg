using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Island;

public class AppShortcut
{
    public string Path { get; set; } = "";
    public string Name { get; set; } = "";
    public string Folder { get; set; } = "";   // v2.10: папка на вкладке «Приложения» ("" — без папки)
    public string Icon { get; set; } = "";     // v2.10: своя иконка (png/jpg/ico/exe), "" — стандартная
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
    public string ThemeColor { get; set; } = ""; // верхний цвет градиента
    public string ThemeBottomColor { get; set; } = "#FF000000"; // нижний цвет градиента
    public int GradientBalance { get; set; } = 70; // положение перехода: 0..100
    public string Accent { get; set; } = "#FF7C5CFF"; // цвет для музыки и событий
    public string Language { get; set; } = "en"; // en | ru

    // ---- v2.7: расширенные настройки ----
    public int JellyStrength { get; set; } = 100;    // «желейность» острова, 0..200 %
    public int AnimationSpeed { get; set; } = 100;   // скорость пружин, 50..200 %
    public int GazeRange { get; set; } = 100;        // дальность взгляда глаз, 50..150 %
    public bool EyesFollowCursor { get; set; } = true;
    public bool EyeEmotions { get; set; } = true;
    public bool SleepAtNight { get; set; } = true;
    public bool BeatBounce { get; set; } = true;     // глаза танцуют под музыку (в такт и под темп)
    public int DanceStrength { get; set; } = 100;    // v2.8: размах танца, 0..200 %
    public bool MusicWave { get; set; } = true;
    public int WaveHeight { get; set; } = 100;       // 30..150 %
    public bool ToastOnCopy { get; set; } = true;
    public bool ToastOnTrack { get; set; } = true;
    public bool GameMode { get; set; } = true;       // полупрозрачность и «сквозные» клики в полноэкранных играх
    public string Hotkey { get; set; } = "Alt+Space";
    public int ClipboardLimit { get; set; } = 50;    // 5..200 записей
    public List<AppShortcut> Apps { get; set; } = new();
    public List<string> HiddenSteamGames { get; set; } = new();
    public bool TransparentOverNormalWindows { get; set; } = false;
    public string HomeStartupView { get; set; } = "Clipboard";
    public bool RememberLastTab { get; set; } = false;
    public string LastTab { get; set; } = "Home";
    public Dictionary<string, string> AppTileSizes { get; set; } = new();

    // ---- v2.10: кастомизация ----
    public string EyeStyle { get; set; } = "Oval";   // Oval | Round | Dots | Anime | Arcs | Robot | Cat
    public int EyeSize { get; set; } = 100;           // 60..150 %
    public int EyeSpacing { get; set; } = 12;         // 4..30 px
    public string EyeColor { get; set; } = "#FFFFFFFF";
    public int EyeGlow { get; set; } = 0;             // 0..100 %
    public int BlinkRate { get; set; } = 100;         // 0..200 %, 0 — не моргают
    public string AppearStyle { get; set; } = "Spring"; // Spring | Smooth | Drop
    public int ExpandSpeed { get; set; } = 100;       // 50..200 %
    public int ToastSpeed { get; set; } = 100;        // 50..200 %
    public int ToastTime { get; set; } = 25;          // время показа уведомления, десятые доли секунды: 15..80
    public string MiniCenter { get; set; } = "Time";  // Time | Date | TimeDate | None
    public string MiniRight { get; set; } = "Weather"; // Weather | Battery | Cpu | Ram | Time | Date | None
    public bool Clock24h { get; set; } = true;
    public bool ClockSeconds { get; set; } = false;
    public int TimerMinutes { get; set; } = 25;       // 1..180
    public string AppTileSize { get; set; } = "Normal"; // Compact | Normal | Large

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
        if (Theme is not ("Midnight" or "Ocean" or "Amethyst" or "Emerald" or "Rose" or "Custom")) Theme = "Midnight";
        if (ThemeColor == null || (ThemeColor.Length > 0 && !HexColor.IsMatch(ThemeColor))) ThemeColor = "";
        else if (ThemeColor.Length == 9) ThemeColor = "#FF" + ThemeColor[3..];
        if (ThemeBottomColor == null || !HexColor.IsMatch(ThemeBottomColor)) ThemeBottomColor = "#FF000000";
        else if (ThemeBottomColor.Length == 9) ThemeBottomColor = "#FF" + ThemeBottomColor[3..];
        GradientBalance = Math.Clamp(GradientBalance, 10, 90);
        if (Accent == null || !HexColor.IsMatch(Accent)) Accent = "#FF7C5CFF";
        if (Language is not ("en" or "ru")) Language = "en";
        JellyStrength = Math.Clamp(JellyStrength, 0, 200);
        AnimationSpeed = Math.Clamp(AnimationSpeed, 50, 200);
        GazeRange = Math.Clamp(GazeRange, 50, 150);
        WaveHeight = Math.Clamp(WaveHeight, 30, 150);
        ClipboardLimit = Math.Clamp(ClipboardLimit, 5, 200);
        DanceStrength = Math.Clamp(DanceStrength, 0, 200);
        if (string.IsNullOrWhiteSpace(Hotkey) || Hotkey.Length > 48) Hotkey = "Alt+Space";
        if (EyeStyle is not ("Oval" or "Round" or "Dots" or "Anime" or "Arcs" or "Robot" or "Cat")) EyeStyle = "Oval";
        EyeSize = Math.Clamp(EyeSize, 60, 150);
        EyeSpacing = Math.Clamp(EyeSpacing, 4, 30);
        if (EyeColor == null || !HexColor.IsMatch(EyeColor)) EyeColor = "#FFFFFFFF";
        else if (EyeColor.Length == 9) EyeColor = "#FF" + EyeColor[3..];
        EyeGlow = Math.Clamp(EyeGlow, 0, 100);
        BlinkRate = Math.Clamp(BlinkRate, 0, 200);
        if (AppearStyle is not ("Spring" or "Smooth" or "Drop")) AppearStyle = "Spring";
        ExpandSpeed = Math.Clamp(ExpandSpeed, 50, 200);
        ToastSpeed = Math.Clamp(ToastSpeed, 50, 200);
        ToastTime = Math.Clamp(ToastTime, 15, 80);
        if (MiniCenter is not ("Time" or "Date" or "TimeDate" or "None")) MiniCenter = "Time";
        if (MiniRight is not ("Weather" or "Battery" or "Cpu" or "Ram" or "Time" or "Date" or "None")) MiniRight = "Weather";
        TimerMinutes = Math.Clamp(TimerMinutes, 1, 180);
        if (AppTileSize is not ("Compact" or "Normal" or "Large")) AppTileSize = "Normal";
        Apps ??= new();
        HiddenSteamGames ??= new();
        HiddenSteamGames = HiddenSteamGames.Where(x => !string.IsNullOrWhiteSpace(x) && Regex.IsMatch(x, @"^\d+$")).Distinct(StringComparer.OrdinalIgnoreCase).Take(2000).ToList();
        if (HomeStartupView is not ("Clipboard" or "Windows")) HomeStartupView = "Clipboard";
        if (LastTab is not ("Home" or "Clip" or "Apps" or "Set")) LastTab = "Home";
        AppTileSizes ??= new();
        AppTileSizes = AppTileSizes.Where(x => !string.IsNullOrWhiteSpace(x.Key) && (x.Value is "1x1" or "2x1" or "2x2")).Take(60).ToDictionary(x => x.Key, x => x.Value, StringComparer.OrdinalIgnoreCase);
        foreach (var a in Apps.Where(a => a != null))
        {
            a.Folder = (a.Folder ?? "").Trim();
            if (a.Folder.Length > 40) a.Folder = a.Folder[..40];
            a.Icon ??= "";
        }
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
