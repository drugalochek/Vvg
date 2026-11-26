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
    internal static readonly string[] Toggles = { "internet", "bluetooth", "vpn", "microphone" };
    internal static readonly string[] PowerActions = { "sleep", "restart", "shutdown" };

    /// <summary>Settings page opened only when Windows refuses a direct toggle.</summary>
    internal static string? FallbackUri(string action) => action switch
    {
        "internet" => "ms-settings:network-status", "bluetooth" => "ms-settings:bluetooth",
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

    // ---------------- Internet: all physical network adapters (Ethernet + Wi-Fi), not only the Wi-Fi radio
    static readonly string[] PhysicalTypes = { "Ethernet", "Wireless80211", "GigabitEthernet", "FastEthernetT", "FastEthernetFx", "Ethernet3Megabit" };

    /// <summary>Real wired / wireless adapters. Virtual switches, VPN tunnels and debug adapters are never touched.</summary>
    internal static bool IsPhysicalNetwork(string? type, string? name, string? description)
    {
        if (type == null || !PhysicalTypes.Contains(type)) return false;
        string s = (name ?? "") + " " + (description ?? "");
        return !Regex.IsMatch(s, VirtualAdapterPattern, RegexOptions.IgnoreCase);
    }

    /// <summary>Adapters Moku never touches: virtual switches, VPN tunnels, Bluetooth PAN, debug adapters.</summary>
    internal const string VirtualAdapterPattern = @"virtual|vEthernet|Hyper-V|VirtualBox|VMware|Loopback|Bluetooth|WAN Miniport|Npcap|Kernel Debug|TAP-|Wintun|\btun\d*\b|happ|xray|sing-?box|WireGuard|OpenVPN|ZeroTier|Tailscale|Hamachi|Radmin|Wi-Fi Direct|Microsoft Wi-Fi Direct";

    /// <summary>On = some physical adapter is enabled; Off = Moku switched them off; null = no adapter at all.</summary>
    internal static bool? InternetState(bool anyPhysicalEnabled, bool disabledByMoku) =>
        anyPhysicalEnabled ? true : disabledByMoku ? false : null;

    /// <summary>
    /// PowerShell run elevated (Windows asks for administrator approval). Turning off disables exactly the listed
    /// adapters; turning on re-enables the ones Moku switched off, or every disabled physical adapter if that list is lost.
    /// </summary>
    internal static string InternetScript(IReadOnlyList<string> names, bool enable)
    {
        string list = string.Join(",", names.Where(n => !string.IsNullOrWhiteSpace(n)).Select(n => "'" + n.Replace("'", "''") + "'"));
        const string head = "$ErrorActionPreference='Stop'; ";
        if (enable)
            return list.Length > 0
                ? head + "Enable-NetAdapter -Name @(" + list + ") -Confirm:$false"
                : head + "Get-NetAdapter -Physical | Where-Object Status -eq 'Disabled' | Enable-NetAdapter -Confirm:$false";
        if (list.Length == 0) throw new ArgumentException("No adapters to disable", nameof(names));
        return head + "Disable-NetAdapter -Name @(" + list + ") -Confirm:$false";
    }

    // ---------------- Internet switch through Task Scheduler: one UAC prompt at setup, none afterwards

    internal const string InternetTaskFolder = @"\Moku";

    /// <summary>Per-user task names; the version suffix lets a future script change register fresh tasks.</summary>
    internal static string InternetTaskName(bool on, string userName)
    {
        string safe = Regex.Replace(string.IsNullOrWhiteSpace(userName) ? "user" : userName, @"[^\w.\-]", "_");
        return (on ? "InternetOn-v1-" : "InternetOff-v1-") + safe;
    }

    /// <summary>
    /// Fixed elevated scripts. Off disables the enabled physical adapters and records their names in
    /// %ProgramData%\Moku\internet-off.txt; On re-enables exactly those (or every disabled physical adapter
    /// if the record is gone) and removes the record. No user-supplied text is ever embedded.
    /// </summary>
    internal static string InternetTaskScript(bool on)
    {
        const string head = "$ErrorActionPreference='Stop';$d=Join-Path $env:ProgramData 'Moku';$f=Join-Path $d 'internet-off.txt';$skip='" + VirtualAdapterPattern + "';";
        if (!on)
            return head +
                "$a=@(Get-NetAdapter -Physical|Where-Object{$_.Status -ne 'Disabled' -and ($_.Name+' '+$_.InterfaceDescription) -notmatch $skip});" +
                "if($a.Count -eq 0){exit 0};New-Item -ItemType Directory -Force $d|Out-Null;" +
                "$p=@();if(Test-Path $f){$p=@(Get-Content $f)};" +
                "Set-Content -Path $f -Value (@($p+@($a|ForEach-Object Name))|Where-Object{$_}|Select-Object -Unique) -Encoding UTF8;" +
                "$a|Disable-NetAdapter -Confirm:$false";
        return head +
            "$n=@();if(Test-Path $f){$n=@(Get-Content $f|Where-Object{$_})};" +
            "$t=@(Get-NetAdapter -Physical|Where-Object{$_.Status -eq 'Disabled' -and ($n.Count -eq 0 -or $n -contains $_.Name) -and ($_.Name+' '+$_.InterfaceDescription) -notmatch $skip});" +
            "if($t.Count -gt 0){$t|Enable-NetAdapter -Confirm:$false};" +
            "Remove-Item $f -ErrorAction SilentlyContinue";
    }

    internal static string EncodePowerShell(string script) => Convert.ToBase64String(System.Text.Encoding.Unicode.GetBytes(script));

    static string PsQuote(string s) => "'" + s.Replace("'", "''") + "'";

    /// <summary>
    /// Elevated once: registers the On/Off tasks for <paramref name="account"/> with the highest run level and
    /// lets that account start them (read + execute) without elevation.
    /// </summary>
    internal static string InternetTaskInstallScript(string account, string userName)
    {
        string off = InternetTaskName(false, userName), on = InternetTaskName(true, userName);
        const string args = "-NoProfile -NonInteractive -ExecutionPolicy Bypass -WindowStyle Hidden -EncodedCommand ";
        return "$ErrorActionPreference='Stop';" +
            "$u=" + PsQuote(account) + ";" +
            "$ps=Join-Path $env:SystemRoot 'System32\\WindowsPowerShell\\v1.0\\powershell.exe';" +
            "$pr=New-ScheduledTaskPrincipal -UserId $u -LogonType Interactive -RunLevel Highest;" +
            "$st=New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -StartWhenAvailable -MultipleInstances IgnoreNew -ExecutionTimeLimit (New-TimeSpan -Minutes 2);" +
            "foreach($k in @(@(" + PsQuote(off) + "," + PsQuote(args + EncodePowerShell(InternetTaskScript(false))) + "),@(" + PsQuote(on) + "," + PsQuote(args + EncodePowerShell(InternetTaskScript(true))) + "))){" +
            "$ac=New-ScheduledTaskAction -Execute $ps -Argument $k[1];" +
            "Register-ScheduledTask -TaskPath '\\Moku\\' -TaskName $k[0] -Action $ac -Principal $pr -Settings $st -Force|Out-Null};" +
            "try{$sid=(New-Object System.Security.Principal.NTAccount($u)).Translate([System.Security.Principal.SecurityIdentifier]).Value;" +
            "$sv=New-Object -ComObject Schedule.Service;$sv.Connect();$fo=$sv.GetFolder('\\Moku');" +
            "foreach($n in @(" + PsQuote(off) + "," + PsQuote(on) + ")){$tk=$fo.GetTask($n);$sd=$tk.GetSecurityDescriptor(4);" +
            "if($sd -notmatch [regex]::Escape($sid)){$tk.SetSecurityDescriptor($sd+'(A;;FRFX;;;'+$sid+')',0)}}}catch{}";
    }
}
