namespace Island;

internal static class MusicSourcePolicy
{
    // Exact identities only. Never select the loudest/system/default output (Discord could win).
    internal static bool Matches(string? source, string processName, string? appId)
    {
        if (string.IsNullOrWhiteSpace(source)) return false;
        if (!string.IsNullOrEmpty(appId) && source.Equals(appId, StringComparison.OrdinalIgnoreCase)) return true;
        string id = source.Replace('\\','/').Split('/').Last();
        return id.Equals(processName, StringComparison.OrdinalIgnoreCase)
            || id.Equals(processName + ".exe", StringComparison.OrdinalIgnoreCase);
    }
}
