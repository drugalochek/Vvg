using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Island;

/// <summary>
/// Moku Link: передача файлов между Moku и приложением Moku на телефоне по локальной сети.
/// Здесь только платформо-независимая логика — разбор запросов, имена файлов, QR-ссылка, токены.
/// </summary>
internal static class TransferPolicy
{
    internal const int Port = 47820;
    internal const int DiscoveryPort = 47821;
    internal const string DiscoverMessage = "MOKU_LINK_DISCOVER_1";
    internal const int ProtocolVersion = 1;
    internal static readonly TimeSpan PairCodeLifetime = TimeSpan.FromMinutes(5);
    internal const long MaxUploadBytes = 64L * 1024 * 1024 * 1024; // 64 ГБ — защита от мусорного Content-Length

    static readonly char[] Invalid = Path.GetInvalidFileNameChars()
        .Concat(new[] { '<', '>', ':', '"', '/', '\\', '|', '?', '*' }).Distinct().ToArray();
    static readonly HashSet<string> Reserved = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
    };

    /// <summary>Безопасное имя файла: без путей, запрещённых символов, зарезервированных имён и точек в конце.</summary>
    internal static string SafeFileName(string? raw)
    {
        string name = (raw ?? "").Replace('\\', '/');
        int slash = name.LastIndexOf('/');
        if (slash >= 0) name = name[(slash + 1)..];
        var sb = new StringBuilder(name.Length);
        foreach (char c in name)
            sb.Append(c < 32 || Array.IndexOf(Invalid, c) >= 0 ? '_' : c);
        name = sb.ToString().Trim().TrimEnd('.', ' ');
        if (name.Length == 0 || name.All(c => c == '.' || c == '_')) name = "file";
        string stem = Path.GetFileNameWithoutExtension(name);
        if (Reserved.Contains(stem)) name = "_" + name;
        if (name.Length > 180)
        {
            string ext = Path.GetExtension(name);
            if (ext.Length > 20) ext = "";
            name = name[..(180 - ext.Length)] + ext;
        }
        return name;
    }

    /// <summary>«photo.jpg» → «photo (2).jpg», если файл уже существует.</summary>
    internal static string UniqueName(string name, Func<string, bool> exists)
    {
        if (!exists(name)) return name;
        string stem = Path.GetFileNameWithoutExtension(name), ext = Path.GetExtension(name);
        for (int i = 2; i < 10000; i++)
        {
            string candidate = $"{stem} ({i}){ext}";
            if (!exists(candidate)) return candidate;
        }
        return $"{stem} ({Guid.NewGuid():N}){ext}";
    }

    internal static string NewSecret(int bytes = 24) =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(bytes)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    internal static string NewPairCode() => RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");

    internal static string Hash(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    internal static bool SameSecret(string? a, string? b)
    {
        if (a == null || b == null) return false;
        var x = Encoding.UTF8.GetBytes(a); var y = Encoding.UTF8.GetBytes(b);
        return x.Length == y.Length && CryptographicOperations.FixedTimeEquals(x, y);
    }

    /// <summary>Ссылка для QR-кода: телефон получает адреса ПК, порт, ID и одноразовый код.</summary>
    internal static string PairUri(IEnumerable<string> hosts, int port, string pcId, string pcName, string code) =>
        "moku://pair?h=" + Uri.EscapeDataString(string.Join(",", hosts)) + "&p=" + port +
        "&id=" + Uri.EscapeDataString(pcId) + "&n=" + Uri.EscapeDataString(pcName) + "&c=" + code + "&v=" + ProtocolVersion;

    internal sealed record PairInfo(string[] Hosts, int Port, string Id, string Name, string Code);

    internal static PairInfo? ParsePairUri(string? uri)
    {
        if (uri == null || !uri.StartsWith("moku://pair?", StringComparison.OrdinalIgnoreCase)) return null;
        var q = ParseQuery(uri[(uri.IndexOf('?') + 1)..]);
        if (!q.TryGetValue("h", out var h) || !q.TryGetValue("p", out var p) || !q.TryGetValue("id", out var id) ||
            !q.TryGetValue("c", out var c) || !int.TryParse(p, out int port) || port is < 1 or > 65535) return null;
        var hosts = h.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (hosts.Length == 0 || id.Length == 0 || c.Length != 6) return null;
        return new PairInfo(hosts, port, id, q.GetValueOrDefault("n") ?? "PC", c);
    }

    internal static Dictionary<string, string> ParseQuery(string query)
    {
        var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var part in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            int eq = part.IndexOf('=');
            string k = eq < 0 ? part : part[..eq], v = eq < 0 ? "" : part[(eq + 1)..];
            try { d[Uri.UnescapeDataString(k.Replace('+', ' '))] = Uri.UnescapeDataString(v.Replace('+', ' ')); } catch { }
        }
        return d;
    }

    internal sealed record Request(string Method, string Path, Dictionary<string, string> Query, Dictionary<string, string> Headers, long ContentLength);

    /// <summary>Разбор заголовка HTTP/1.1 (до пустой строки). null — запрос некорректный.</summary>
    internal static Request? ParseRequestHead(string head)
    {
        var lines = head.Replace("\r\n", "\n").Split('\n');
        if (lines.Length == 0) return null;
        var first = lines[0].Split(' ');
        if (first.Length < 3 || !first[2].StartsWith("HTTP/1.", StringComparison.Ordinal)) return null;
        string method = first[0].ToUpperInvariant(), target = first[1];
        if (method is not ("GET" or "POST" or "DELETE" or "OPTIONS")) return null;
        int qi = target.IndexOf('?');
        string path = qi < 0 ? target : target[..qi];
        var query = qi < 0 ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) : ParseQuery(target[(qi + 1)..]);
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in lines.Skip(1))
        {
            if (line.Length == 0) continue;
            int c = line.IndexOf(':');
            if (c <= 0) return null;
            headers[line[..c].Trim()] = line[(c + 1)..].Trim();
        }
        long len = 0;
        if (headers.TryGetValue("Content-Length", out var cl) && (!long.TryParse(cl, out len) || len < 0 || len > MaxUploadBytes)) return null;
        if (headers.ContainsKey("Transfer-Encoding")) return null; // телефон всегда шлёт Content-Length
        return new Request(method, path, query, headers, len);
    }

    internal static string StatusText(int code) => code switch
    {
        200 => "OK", 204 => "No Content", 400 => "Bad Request", 401 => "Unauthorized", 403 => "Forbidden",
        404 => "Not Found", 409 => "Conflict", 410 => "Gone", 413 => "Payload Too Large", 500 => "Internal Server Error",
        503 => "Service Unavailable", _ => "OK"
    };

    internal static string FormatSize(long bytes, bool ru)
    {
        string[] u = ru ? new[] { "Б", "КБ", "МБ", "ГБ" } : new[] { "B", "KB", "MB", "GB" };
        double v = bytes; int i = 0;
        while (v >= 1024 && i < u.Length - 1) { v /= 1024; i++; }
        return (i == 0 ? v.ToString("0") : v.ToString(v < 10 ? "0.0" : "0", System.Globalization.CultureInfo.InvariantCulture)) + " " + u[i];
    }

    /// <summary>Минимальное JSON-экранирование строк для ответов сервера.</summary>
    internal static string Json(string? s)
    {
        var sb = new StringBuilder("\"");
        foreach (char c in s ?? "")
        {
            switch (c)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (c < 32) sb.Append("\\u").Append(((int)c).ToString("x4")); else sb.Append(c);
                    break;
            }
        }
        return sb.Append('"').ToString();
    }
}
