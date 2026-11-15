namespace Island;

/// <summary>Session peaks are level, not a frequency spectrum.</summary>
internal static class AudioLevelPolicy
{
    internal const string Windows11 = "Windows11", Windows10 = "Windows10";
    internal static string DefaultMode(bool supported) => supported ? Windows11 : Windows10;
    internal static string NormalizeMode(string? mode, bool supported) =>
        mode is Windows11 or Windows10 ? mode : DefaultMode(supported);
    internal static bool UseSessionMeter(bool supported, bool failed, string? mode = null) =>
        mode == Windows10 || !supported || failed;
    internal static bool AcceptSession(int targetRoot, int sessionRoot, bool systemSounds) =>
        targetRoot > 0 && sessionRoot > 0 && targetRoot == sessionRoot && !systemSounds;
    internal static float PeakActivity(float peak) =>
        float.IsFinite(peak) ? MathF.Sqrt(Math.Clamp(peak, 0f, 1f)) : 0f;
}
