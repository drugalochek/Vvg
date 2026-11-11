using System.IO;

namespace Island;

/// <summary>Простой файловый лог: %APPDATA%\Moku\moku.log (ротация при 512 КБ).</summary>
internal static class Log
{
    static readonly object Gate = new();
    static readonly string Dir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Moku");
    static string FilePath => Path.Combine(Dir, "moku.log");

    public static void Info(string msg) => Write("INFO", msg);
    public static void Warn(string msg) => Write("WARN", msg);
    public static void Error(string msg, Exception? ex = null) =>
        Write("ERROR", ex == null ? msg : msg + Environment.NewLine + ex);

    static void Write(string level, string msg)
    {
        System.Diagnostics.Debug.WriteLine($"[Moku {level}] {msg}");
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(Dir);
                var fi = new FileInfo(FilePath);
                if (fi.Exists && fi.Length > 512 * 1024)
                    File.Move(FilePath, FilePath + ".old", overwrite: true);
                File.AppendAllText(FilePath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {level} {msg}{Environment.NewLine}");
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Лог недоступен (диск/права) — сообщение уже ушло в Debug-вывод, приложение продолжает работу.
        }
    }
}
