namespace Island;

internal static class MusicSourcePolicy
{
    internal static bool MatchesExecutablePath(string? expected, string? actual) =>
        !string.IsNullOrWhiteSpace(expected) && !string.IsNullOrWhiteSpace(actual) &&
        expected.Replace('\\', '/').Equals(actual.Replace('\\', '/'), StringComparison.OrdinalIgnoreCase);

    internal static int SelectRoot(IEnumerable<int> candidates, IEnumerable<int> audioRoots)
    {
        int[] roots = candidates.Where(pid => pid > 0).Distinct().ToArray();
        var audio = audioRoots.Where(pid => pid > 0).ToHashSet();
        int[] sounding = roots.Where(audio.Contains).ToArray();
        if (sounding.Length == 1) return sounding[0];
        return roots.Length == 1 ? roots[0] : 0;
    }

    // Exact identities only. Never select the loudest/system/default output (Discord could win).
    internal static bool Matches(string? source, string processName, string? appId)
    {
        if (string.IsNullOrWhiteSpace(source)) return false;
        if (!string.IsNullOrEmpty(appId) && source.Equals(appId, StringComparison.OrdinalIgnoreCase)) return true;
        // The Windows desktop Yandex Music client publishes this media AUMID,
        // while its executable is YandexMusic.exe. Portable installs may lack a shortcut.
        // Explicit aliases only: never treat Yandex Browser or a generic "music" process as the player.
        if (source.Equals("ru.yandex.desktop.music", StringComparison.OrdinalIgnoreCase) &&
            (processName.Equals("YandexMusic", StringComparison.OrdinalIgnoreCase) ||
             processName.Equals("Яндекс Музыка", StringComparison.OrdinalIgnoreCase))) return true;
        string id = source.Replace('\\','/').Split('/').Last();
        return id.Equals(processName, StringComparison.OrdinalIgnoreCase)
            || id.Equals(processName + ".exe", StringComparison.OrdinalIgnoreCase);
    }
}
