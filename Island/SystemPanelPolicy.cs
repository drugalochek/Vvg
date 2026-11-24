using System.Text.RegularExpressions;

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

    /// <summary>The only actions the System tab exposes. Everything else was removed on purpose.</summary>
    internal static readonly string[] Toggles = { "wifi", "bluetooth", "vpn", "microphone" };
    internal static readonly string[] PowerActions = { "sleep", "restart", "shutdown" };

    /// <summary>Settings page opened only when Windows refuses a direct toggle.</summary>
    internal static string? FallbackUri(string action) => action switch
    {
        "wifi" => "ms-settings:network-wifi", "bluetooth" => "ms-settings:bluetooth",
        "vpn" => "ms-settings:network-vpn", "microphone" => "ms-settings:sound",
        _ => null
    };

    /// <summary>
    /// GPU load like Task Manager: per adapter and engine type, utilization of all processes is summed;
    /// the busiest engine is the adapter load. Instance names look like
    /// pid_1234_luid_0x00000000_0x0000D1B4_phys_0_eng_0_engtype_3D.
    /// </summary>
    internal static double? GpuLoad(IEnumerable<(string Instance, double Value)> samples)
    {
        var sums = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        bool any = false;
        foreach (var (instance, value) in samples)
        {
            if (string.IsNullOrEmpty(instance) || double.IsNaN(value) || double.IsInfinity(value) || value < 0) continue;
            any = true;
            var luid = Regex.Match(instance, @"luid_(0x[0-9a-f]+_0x[0-9a-f]+)", RegexOptions.IgnoreCase);
            var type = Regex.Match(instance, @"engtype_([^_]*)$", RegexOptions.IgnoreCase);
            var eng = Regex.Match(instance, @"phys_(\d+)_eng_(\d+)", RegexOptions.IgnoreCase);
            string key = (luid.Success ? luid.Groups[1].Value : "?") + "|" +
                         (type.Success && type.Groups[1].Value.Length > 0 ? type.Groups[1].Value : eng.Success ? eng.Value : instance);
            sums[key] = sums.TryGetValue(key, out var old) ? old + value : value;
        }
        if (!any) return null;
        return Math.Clamp(sums.Count == 0 ? 0 : sums.Values.Max(), 0, 100);
    }

    /// <summary>"NVIDIA GeForce RTX 4060" → "GeForce RTX 4060"; software adapters are not a GPU.</summary>
    internal static string? GpuName(string? driverDescription)
    {
        if (string.IsNullOrWhiteSpace(driverDescription)) return null;
        string n = driverDescription.Trim();
        if (Regex.IsMatch(n, @"Basic Render|Basic Display|Remote Display|Hyper-V|Virtual|Parsec|Indirect Display|Mirage", RegexOptions.IgnoreCase)) return null;
        n = Regex.Replace(n, @"\((R|TM|C)\)|®|™", "", RegexOptions.IgnoreCase);
        n = Regex.Replace(n, @"^(NVIDIA|AMD|ATI)\s+", "", RegexOptions.IgnoreCase);
        n = Regex.Replace(n, @"\s+Laptop GPU$", " Laptop", RegexOptions.IgnoreCase);
        n = Regex.Replace(n, @"\s{2,}", " ").Trim();
        return n.Length == 0 ? null : n.Length > 26 ? n[..26].TrimEnd() + "…" : n;
    }

    /// <summary>Adapter names used by Happ tunnels (Happ TUN, Xray TUN, sing-box, tun2proxy / Wintun).</summary>
    internal static bool LooksLikeTunnel(string? name, string? description)
    {
        string s = (name ?? "") + " " + (description ?? "");
        if (Regex.IsMatch(s, @"Teredo|ISATAP|6to4|Loopback|Hyper-V|VirtualBox|VMware|Bluetooth", RegexOptions.IgnoreCase)) return false;
        return Regex.IsMatch(s, @"happ|wintun|xray|sing-?box|tun2|\btun\d*\b", RegexOptions.IgnoreCase);
    }

    /// <summary>Happ in proxy mode points the Windows system proxy at its local inbound.</summary>
    internal static bool IsLocalProxy(int? enabled, string? server)
    {
        if (enabled != 1 || string.IsNullOrWhiteSpace(server)) return false;
        return Regex.IsMatch(server, @"(^|[=;\s])(127\.0\.0\.1|localhost|\[::1\])(:\d+)?", RegexOptions.IgnoreCase);
    }

    internal static bool HappConnected(bool running, bool tunnelUp, bool localProxy) => running && (tunnelUp || localProxy);

    /// <summary>Phonebook entry names from rasphone.pbk (Windows VPN connections added in Settings).</summary>
    internal static IReadOnlyList<string> PhonebookEntries(string? pbk)
    {
        var list = new List<string>();
        if (string.IsNullOrEmpty(pbk)) return list;
        foreach (Match m in Regex.Matches(pbk, @"^\[(.+?)\]\s*$", RegexOptions.Multiline))
        {
            string name = m.Groups[1].Value.Trim();
            if (name.Length > 0 && !list.Contains(name, StringComparer.OrdinalIgnoreCase)) list.Add(name);
        }
        return list;
    }

    /// <summary>A pending toggle shows "connecting…" until the real state flips or the wait expires.</summary>
    internal static bool PendingExpired(DateTime started, DateTime now, double seconds = 12) => (now - started).TotalSeconds >= seconds;
}
