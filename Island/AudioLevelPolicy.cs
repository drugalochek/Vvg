namespace Island;

/// <summary>Session peaks are level, not a frequency spectrum.</summary>
internal static class AudioLevelPolicy
{
    internal const string Spectrum = "ProcessSpectrum", SessionLevel = "PlayerSessionLevel";
    internal static bool UseSessionMeter(bool supported, bool failed) => !supported || failed;
    // A successful activation is not proof that PCM contains the player's sound.
    // Require sustained player-session audio, not merely elapsed time during a quiet intro.
    internal static bool ShouldFallBack(bool pcmSilent, double secondsWithSessionAudio) =>
        pcmSilent && secondsWithSessionAudio >= 0.75;
    internal static bool AcceptSession(int targetRoot, int sessionRoot, bool systemSounds) =>
        targetRoot > 0 && sessionRoot > 0 && targetRoot == sessionRoot && !systemSounds;
    internal static float PeakActivity(float peak) =>
        float.IsFinite(peak) ? MathF.Sqrt(Math.Clamp(peak, 0f, 1f)) : 0f;
}
