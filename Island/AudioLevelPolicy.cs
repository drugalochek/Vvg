namespace Island;

/// <summary>Session peaks are level, not a frequency spectrum.</summary>
internal static class AudioLevelPolicy
{
    internal static bool UseSessionMeter(bool supported, bool failed) => !supported || failed;
    internal static bool AcceptSession(int targetRoot, int sessionRoot, bool systemSounds) =>
        targetRoot > 0 && sessionRoot > 0 && targetRoot == sessionRoot && !systemSounds;
    internal static float PeakActivity(float peak) =>
        float.IsFinite(peak) ? MathF.Sqrt(Math.Clamp(peak, 0f, 1f)) : 0f;
}
