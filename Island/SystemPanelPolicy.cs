namespace Island;

/// <summary>Pure calculations: unavailable counters stay unavailable, never a made-up zero.</summary>
internal static class SystemPanelPolicy
{
    internal static string VpnClient(string? value) => string.Equals(value, "Happ", StringComparison.OrdinalIgnoreCase) ? "Happ" : "Windows";
    internal static double? Cpu(ulong oldIdle, ulong oldKernel, ulong oldUser, ulong idle, ulong kernel, ulong user)
    {
        if (idle < oldIdle || kernel < oldKernel || user < oldUser) return null;
        double total = (double)(kernel - oldKernel) + (user - oldUser);
        if (total <= 0) return null;
        return Math.Clamp(100 * (1 - (idle - oldIdle) / total), 0, 100);
    }
    internal static double? Memory(ulong total, ulong available) => total == 0 || available > total ? null : 100.0 * (total - available) / total;
    internal static int? Battery(byte flags, byte percent) => (flags & 128) != 0 || flags == 255 || percent > 100 ? null : percent;
    internal static bool Poll(bool expanded, bool visible) => expanded && visible;
    internal static bool NeedsConfirmation(string action) => action is "sleep" or "restart" or "shutdown";
    internal static string? SettingsUri(string action) => action switch
    {
        "wifi" => "ms-settings:network-wifi", "bluetooth" => "ms-settings:bluetooth",
        "airplane" => "ms-settings:network-airplanemode", "vpn" => "ms-settings:network-vpn",
        "sound" => "ms-settings:sound", "notifications" => "ms-settings:notifications",
        "microphone-settings" => "ms-settings:privacy-microphone", "camera" => "ms-settings:privacy-webcam",
        _ => null
    };
}
