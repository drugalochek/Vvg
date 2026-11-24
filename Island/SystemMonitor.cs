using System.Diagnostics;
using System.IO;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using NAudio.CoreAudioApi;
using Windows.Devices.Radios;

namespace Island;

/// <summary>Radio state as Windows reports it. Null = no such adapter.</summary>
internal sealed record SystemSnapshot(double? Cpu, double? Memory, ulong UsedMemory, ulong TotalMemory,
    double? Gpu, string? GpuName, bool? Wifi, bool WifiConnected, bool? Bluetooth, bool? Vpn, string? VpnEntry, bool? MicMuted);

internal enum ToggleResult { Done, Denied, Unavailable, NoProfile, Failed }

/// <summary>
/// Sampled only while the System tab is open (every 2 s). No WMI, no resident polling, no helper processes:
/// CPU/RAM from kernel32, GPU from the same PDH counters Task Manager uses, radios from Windows.Devices.Radios.
/// </summary>
internal sealed class SystemMonitor
{
    (ulong Idle, ulong Kernel, ulong User)? previous;
    readonly GpuMonitor gpu = new();
    string? gpuName; bool gpuNameRead;
    Radio[]? radios; DateTime radiosAt; bool radioAccessRequested;
    readonly object radioLock = new();

    internal void Reset() => previous = null;

    internal SystemSnapshot Read(string vpnClient)
    {
        double? cpu = null, memory = null; ulong used = 0, total = 0;
        if (GetSystemTimes(out var idle, out var kernel, out var user))
        {
            if (previous is { } p) cpu = SystemPanelPolicy.Cpu(p.Idle, p.Kernel, p.User, idle, kernel, user);
            previous = (idle, kernel, user);
        }
        var mem = new MemoryStatus { Length = (uint)Marshal.SizeOf<MemoryStatus>() };
        if (GlobalMemoryStatusEx(ref mem))
        {
            memory = SystemPanelPolicy.Memory(mem.TotalPhysical, mem.AvailablePhysical);
            if (memory != null) { total = mem.TotalPhysical; used = total - mem.AvailablePhysical; }
        }
        if (!gpuNameRead) { gpuNameRead = true; gpuName = ReadGpuName(); }
        double? gpuLoad = gpu.Read();

        bool? wifi = RadioOn(RadioKind.WiFi), bluetooth = RadioOn(RadioKind.Bluetooth);
        NetworkInterface[] interfaces = Array.Empty<NetworkInterface>();
        try { interfaces = NetworkInterface.GetAllNetworkInterfaces(); } catch { }
        bool wifiConnected = interfaces.Any(n => n.NetworkInterfaceType == NetworkInterfaceType.Wireless80211 && n.OperationalStatus == OperationalStatus.Up);
        // No radio API (old driver) but a Wi-Fi adapter exists: report what the adapter shows.
        if (wifi == null && interfaces.Any(n => n.NetworkInterfaceType == NetworkInterfaceType.Wireless80211)) wifi = wifiConnected;

        var (vpn, entry) = VpnState(vpnClient, interfaces);

        bool? mic = null;
        try
        {
            using var devices = new MMDeviceEnumerator();
            using var device = devices.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications);
            mic = device.AudioEndpointVolume.Mute;
        }
        catch { }
        return new(cpu, memory, used, total, gpuLoad, gpuName, wifi, wifiConnected, bluetooth, vpn, entry, mic);
    }

    // ---------------- Wi-Fi / Bluetooth: real radio switches, like the Windows quick settings
    Radio[] Radios(bool refresh = false)
    {
        lock (radioLock)
        {
            try
            {
                if (!radioAccessRequested)
                {
                    radioAccessRequested = true;
                    try { Radio.RequestAccessAsync().AsTask().Wait(3000); } catch { }
                }
                if (refresh || radios == null || (DateTime.UtcNow - radiosAt).TotalSeconds > 20)
                {
                    var task = Radio.GetRadiosAsync().AsTask();
                    radios = task.Wait(3000) ? task.Result.ToArray() : radios ?? Array.Empty<Radio>();
                    radiosAt = DateTime.UtcNow;
                }
            }
            catch (Exception ex) { Log.Error("Radio enumeration failed", ex); radios ??= Array.Empty<Radio>(); }
            return radios;
        }
    }
    bool? RadioOn(RadioKind kind)
    {
        var radio = Radios().FirstOrDefault(r => r.Kind == kind);
        if (radio == null) return null;
        try { return radio.State == RadioState.On; } catch { return null; }
    }
    internal ToggleResult SetRadio(RadioKind kind, bool on)
    {
        var radio = Radios(refresh: true).FirstOrDefault(r => r.Kind == kind);
        if (radio == null) return ToggleResult.Unavailable;
        try
        {
            var task = radio.SetStateAsync(on ? RadioState.On : RadioState.Off).AsTask();
            if (!task.Wait(8000)) return ToggleResult.Failed;
            return task.Result == RadioAccessStatus.Allowed ? ToggleResult.Done : ToggleResult.Denied;
        }
        catch (Exception ex) { Log.Error("Radio switch failed: " + kind, ex); return ToggleResult.Failed; }
    }

    // ---------------- Microphone
    internal static bool ToggleMicrophoneMute()
    {
        using var devices = new MMDeviceEnumerator();
        using var comms = devices.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications);
        bool mute = !comms.AudioEndpointVolume.Mute;
        comms.AudioEndpointVolume.Mute = mute;
        // Discord/games may record from the default (not communications) device: keep both in sync.
        try
        {
            using var main = devices.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Multimedia);
            if (main.ID != comms.ID) main.AudioEndpointVolume.Mute = mute;
        }
        catch { }
        return mute;
    }

    // ---------------- VPN
    static (bool? Connected, string? Entry) VpnState(string client, NetworkInterface[] interfaces)
    {
        try
        {
            if (SystemPanelPolicy.VpnClient(client) == "Happ")
            {
                bool running = Process.GetProcessesByName("Happ").Length > 0;
                bool tunnel = interfaces.Any(n => n.OperationalStatus == OperationalStatus.Up && SystemPanelPolicy.LooksLikeTunnel(n.Name, n.Description));
                bool proxy = false;
                if (running)
                {
                    using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Internet Settings");
                    proxy = SystemPanelPolicy.IsLocalProxy(key?.GetValue("ProxyEnable") as int?, key?.GetValue("ProxyServer") as string);
                }
                return (SystemPanelPolicy.HappConnected(running, tunnel, proxy), null);
            }
            var active = interfaces.FirstOrDefault(n => n.NetworkInterfaceType == NetworkInterfaceType.Ppp && n.OperationalStatus == OperationalStatus.Up);
            return (active != null, active?.Name);
        }
        catch { return (null, null); }
    }

    internal static IReadOnlyList<string> WindowsVpnProfiles()
    {
        var names = new List<string>();
        foreach (var root in new[] { Environment.SpecialFolder.ApplicationData, Environment.SpecialFolder.CommonApplicationData })
        {
            try
            {
                string pbk = Path.Combine(Environment.GetFolderPath(root), @"Microsoft\Network\Connections\Pbk\rasphone.pbk");
                if (File.Exists(pbk))
                    foreach (var n in SystemPanelPolicy.PhonebookEntries(File.ReadAllText(pbk)))
                        if (!names.Contains(n, StringComparer.OrdinalIgnoreCase)) names.Add(n);
            }
            catch { }
        }
        return names;
    }

    /// <summary>rasdial uses the credentials saved in Windows; it never asks for or stores passwords itself.</summary>
    internal static ToggleResult ToggleWindowsVpn(bool connected, string? activeEntry)
    {
        string? entry = connected ? activeEntry : WindowsVpnProfiles().FirstOrDefault();
        if (entry == null) return connected ? ToggleResult.Failed : ToggleResult.NoProfile;
        var psi = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "rasdial.exe"))
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true
        };
        psi.ArgumentList.Add(entry);
        if (connected) psi.ArgumentList.Add("/disconnect");
        using var process = Process.Start(psi);
        if (process == null) return ToggleResult.Failed;
        process.StandardInput.Close();                 // never wait for a console password prompt
        _ = process.StandardOutput.ReadToEndAsync(); _ = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(30000)) { try { process.Kill(); } catch { } return ToggleResult.Failed; }
        return process.ExitCode == 0 ? ToggleResult.Done : ToggleResult.Failed;
    }

    // ---------------- GPU name: the adapter with the most dedicated memory
    static string? ReadGpuName()
    {
        try
        {
            using var cls = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}");
            if (cls == null) return null;
            string? best = null; long bestMemory = -1;
            foreach (var sub in cls.GetSubKeyNames().Where(n => n.Length == 4 && n.All(char.IsDigit)))
            {
                try
                {
                    using var key = cls.OpenSubKey(sub);
                    var name = SystemPanelPolicy.GpuName(key?.GetValue("DriverDesc") as string);
                    if (name == null) continue;
                    long memory = key!.GetValue("HardwareInformation.qwMemorySize") switch
                    {
                        long q => q, int d => (uint)d,
                        byte[] b when b.Length >= 8 => BitConverter.ToInt64(b, 0),
                        byte[] b when b.Length >= 4 => BitConverter.ToUInt32(b, 0),
                        _ => key.GetValue("HardwareInformation.MemorySize") switch
                        {
                            int d => (uint)d, long q => q,
                            byte[] b when b.Length >= 4 => BitConverter.ToUInt32(b, 0), _ => 0
                        }
                    };
                    if (memory > bestMemory) { bestMemory = memory; best = name; }
                }
                catch { }
            }
            return best;
        }
        catch { return null; }
    }

    [StructLayout(LayoutKind.Sequential)]
    struct MemoryStatus
    {
        public uint Length, Load;
        public ulong TotalPhysical, AvailablePhysical, TotalPage, AvailablePage, TotalVirtual, AvailableVirtual, AvailableExtended;
    }
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool GetSystemTimes(out ulong idle, out ulong kernel, out ulong user);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool GlobalMemoryStatusEx(ref MemoryStatus status);
}

/// <summary>"\GPU Engine(*)\Utilization Percentage" — the counter set Task Manager shows as GPU load.</summary>
internal sealed class GpuMonitor
{
    IntPtr query, counter;
    DateTime opened;
    bool primed, unavailable;
    double? last;
    const uint PDH_FMT_DOUBLE = 0x00000200;
    const int PDH_MORE_DATA = unchecked((int)0x800007D2);

    internal double? Read()
    {
        if (unavailable) return null;
        try
        {
            // New processes create new counter instances: re-open the query periodically.
            if (query == IntPtr.Zero || (DateTime.UtcNow - opened).TotalSeconds > 30) { Close(); if (!Open()) return null; }
            if (PdhCollectQueryData(query) != 0) { Close(); return last; }
            if (!primed) { primed = true; return last; }          // rate counters need two samples
            uint size = 0;
            int status = PdhGetFormattedCounterArrayW(counter, PDH_FMT_DOUBLE, ref size, out uint count, IntPtr.Zero);
            if (status == 0 && count == 0) return last = 0;
            if (status != PDH_MORE_DATA || size == 0) return last;
            IntPtr buffer = Marshal.AllocHGlobal((int)size);
            try
            {
                status = PdhGetFormattedCounterArrayW(counter, PDH_FMT_DOUBLE, ref size, out count, buffer);
                if (status != 0) return last;
                var samples = new List<(string, double)>((int)count);
                const int stride = 24;                            // PDH_FMT_COUNTERVALUE_ITEM_W on x64
                for (int i = 0; i < count; i++)
                {
                    IntPtr item = buffer + i * stride;
                    string? name = Marshal.PtrToStringUni(Marshal.ReadIntPtr(item));
                    uint cstatus = (uint)Marshal.ReadInt32(item, 8);
                    double value = BitConverter.Int64BitsToDouble(Marshal.ReadInt64(item, 16));
                    if (name != null && cstatus <= 1) samples.Add((name, value));
                }
                return last = SystemPanelPolicy.GpuLoad(samples) ?? 0;
            }
            finally { Marshal.FreeHGlobal(buffer); }
        }
        catch (Exception ex) { Log.Error("GPU sample failed", ex); Close(); return last; }
    }
    bool Open()
    {
        if (PdhOpenQueryW(null, IntPtr.Zero, out query) != 0) { query = IntPtr.Zero; unavailable = true; return false; }
        if (PdhAddEnglishCounterW(query, @"\GPU Engine(*)\Utilization Percentage", IntPtr.Zero, out counter) != 0)
        {
            // No WDDM 2.x GPU counters on this PC: show "—", never a fake 0 %.
            Close(); unavailable = true; return false;
        }
        opened = DateTime.UtcNow; primed = false; return true;
    }
    void Close()
    {
        if (query != IntPtr.Zero) { try { PdhCloseQuery(query); } catch { } }
        query = counter = IntPtr.Zero; primed = false;
    }
    [DllImport("pdh.dll", CharSet = CharSet.Unicode)] static extern int PdhOpenQueryW(string? source, IntPtr user, out IntPtr query);
    [DllImport("pdh.dll", CharSet = CharSet.Unicode)] static extern int PdhAddEnglishCounterW(IntPtr query, string path, IntPtr user, out IntPtr counter);
    [DllImport("pdh.dll")] static extern int PdhCollectQueryData(IntPtr query);
    [DllImport("pdh.dll", CharSet = CharSet.Unicode)] static extern int PdhGetFormattedCounterArrayW(IntPtr counter, uint format, ref uint size, out uint count, IntPtr buffer);
    [DllImport("pdh.dll")] static extern int PdhCloseQuery(IntPtr query);
}
