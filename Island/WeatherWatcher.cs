using System.IO;
using System.Globalization;
using System.Net.Http;
using System.Text.Json;

namespace Island;

public enum WeatherKind { Clear, PartlyCloudy, Cloudy, Fog, Rain, Snow, Thunder }

public record WeatherInfo(WeatherKind Kind, bool IsDay, int TempC);

/// <summary>Weather from Open-Meteo. Last successful result is cached locally.</summary>
public sealed class WeatherWatcher : IDisposable
{
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };
    static readonly string CachePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Moku", "weather-cache.json");

    static readonly TimeSpan CacheMaxAge = TimeSpan.FromHours(6);   // старее — не показываем как «текущую» погоду
    static readonly TimeSpan StaleAfter = TimeSpan.FromHours(2);    // давно нет свежих данных — убираем значение

    double _lat, _lon;
    bool _located;
    int _failures;
    DateTime _lastSuccessUtc = DateTime.MinValue;
    bool _hasValue;
    CancellationTokenSource? _stop;

    public event Action<WeatherInfo?>? Changed;

    public async Task StartAsync()
    {
        _stop = new CancellationTokenSource();
        var token = _stop.Token;

        // Show the last known weather immediately, even before the network responds.
        var cached = LoadCache();
        if (cached != null) { _hasValue = true; Changed?.Invoke(cached); }

        while (!token.IsCancellationRequested)
        {
            await RefreshAsync(token);
            // При проблемах сети повторяем быстро, после успеха — раз в 15 минут.
            // Экспоненциальная пауза при подряд идущих ошибках: 45 с, 90 с, ... максимум 10 минут.
            var delay = _failures == 0
                ? TimeSpan.FromMinutes(15)
                : TimeSpan.FromSeconds(Math.Min(600, 45 * Math.Pow(2, Math.Min(_failures - 1, 5))));
            try { await Task.Delay(delay, token); }
            catch (OperationCanceledException) { break; }
        }
    }

    async Task LocateAsync(CancellationToken token)
    {
        if (_located) return;

        var json = await Http.GetStringAsync(
            "https://ipwho.is/?fields=success,latitude,longitude", token);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (!root.TryGetProperty("success", out var ok) || !ok.GetBoolean())
            throw new InvalidOperationException("Не удалось определить местоположение.");

        _lat = root.GetProperty("latitude").GetDouble();
        _lon = root.GetProperty("longitude").GetDouble();
        if (double.IsNaN(_lat) || double.IsNaN(_lon) || _lat is < -90 or > 90 || _lon is < -180 or > 180)
            throw new InvalidOperationException("Сервис геолокации вернул некорректные координаты.");
        _located = true;
    }

    async Task RefreshAsync(CancellationToken token)
    {
        try
        {
            await LocateAsync(token);
            var url = "https://api.open-meteo.com/v1/forecast?latitude=" +
                _lat.ToString(CultureInfo.InvariantCulture) + "&longitude=" +
                _lon.ToString(CultureInfo.InvariantCulture) +
                "&current=temperature_2m,weather_code,is_day";
            var json = await Http.GetStringAsync(url, token);
            using var doc = JsonDocument.Parse(json);
            var current = doc.RootElement.GetProperty("current");
            var code = current.GetProperty("weather_code").GetInt32();
            var isDay = current.GetProperty("is_day").GetInt32() == 1;
            var temp = (int)Math.Round(current.GetProperty("temperature_2m").GetDouble());
            var info = new WeatherInfo(MapCode(code), isDay, temp);

            SaveCache(info);
            _failures = 0;
            _lastSuccessUtc = DateTime.UtcNow;
            _hasValue = true;
            Changed?.Invoke(info);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException
                                       or KeyNotFoundException or InvalidOperationException or FormatException)
        {
            _failures++;
            Log.Warn($"Погода: ошибка обновления (попытка {_failures}): {ex.Message}");
            // Последнее удачное значение остаётся на экране, пока оно не устарело; повтор — по расписанию.
            bool stale = _lastSuccessUtc != DateTime.MinValue && DateTime.UtcNow - _lastSuccessUtc > StaleAfter;
            if (stale) { _hasValue = false; Changed?.Invoke(null); }
            else if (!_hasValue) Changed?.Invoke(LoadCache());   // null, если кэша нет — интерфейс покажет «—°»
        }
    }

    static WeatherInfo? LoadCache()
    {
        try
        {
            if (!File.Exists(CachePath)) return null;
            if (DateTime.UtcNow - File.GetLastWriteTimeUtc(CachePath) > CacheMaxAge) return null;
            var cache = JsonSerializer.Deserialize<WeatherInfo>(File.ReadAllText(CachePath));
            return cache;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            Log.Warn("Кэш погоды не прочитан: " + ex.Message);
            return null;
        }
    }

    static void SaveCache(WeatherInfo info)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(CachePath)!);
            var tempPath = CachePath + ".tmp";
            File.WriteAllText(tempPath, JsonSerializer.Serialize(info));
            File.Move(tempPath, CachePath, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn("Кэш погоды не сохранён: " + ex.Message);
        }
    }

    internal static WeatherKind MapCode(int c) => c switch
    {
        0 => WeatherKind.Clear,
        1 or 2 => WeatherKind.PartlyCloudy,
        3 => WeatherKind.Cloudy,
        45 or 48 => WeatherKind.Fog,
        >= 51 and <= 67 => WeatherKind.Rain,
        >= 71 and <= 77 => WeatherKind.Snow,
        >= 80 and <= 82 => WeatherKind.Rain,
        85 or 86 => WeatherKind.Snow,
        >= 95 => WeatherKind.Thunder,
        _ => WeatherKind.Cloudy
    };

    public void Dispose()
    {
        _stop?.Cancel();
        _stop?.Dispose();
        _stop = null;
    }
}
