using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;

namespace Island;

internal sealed record SystemSnapshot(double? Cpu, double? Memory, ulong UsedMemory, ulong TotalMemory,
    int? Battery, bool? HasBattery, bool PluggedIn, bool? Wifi, bool? MicMuted, int? Volume, bool? OutputMuted);

/// <summary>No WMI, performance counter subscriptions, resident polling or external processes.</summary>
internal sealed class SystemMonitor
{
    (ulong Idle, ulong Kernel, ulong User)? previous;
    internal void Reset() => previous = null;
    internal SystemSnapshot Read()
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
        int? battery = null; bool? hasBattery = null; bool plugged = false;
        if (GetSystemPowerStatus(out var power))
        {
            battery = SystemPanelPolicy.Battery(power.Flags, power.Percent);
            hasBattery = power.Flags == 255 ? null : (power.Flags & 128) == 0;
            plugged = power.AcLine == 1;
        }
        bool? wifi = null;
        try
        {
            var interfaces = NetworkInterface.GetAllNetworkInterfaces().Where(n => n.NetworkInterfaceType == NetworkInterfaceType.Wireless80211).ToArray();
            if (interfaces.Length > 0) wifi = interfaces.Any(n => n.OperationalStatus == OperationalStatus.Up);
        }
        catch { /* Offline / restricted network service is not a disabled radio. */ }
        bool? mic = null, outputMuted = null; int? volume = null;
        try
        {
            using var devices = new MMDeviceEnumerator();
            using var device = devices.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            volume = (int)Math.Round(device.AudioEndpointVolume.MasterVolumeLevelScalar * 100);
            outputMuted = device.AudioEndpointVolume.Mute;
        }
        catch { }
        try
        {
            using var devices = new MMDeviceEnumerator();
            using var device = devices.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications);
            mic = device.AudioEndpointVolume.Mute;
        }
        catch { }
        return new(cpu, memory, used, total, battery, hasBattery, plugged, wifi, mic, volume, outputMuted);
    }
    internal static bool ToggleMicrophoneMute()
    {
        using var devices = new MMDeviceEnumerator();
        using var device = devices.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications);
        var endpoint = device.AudioEndpointVolume;
        endpoint.Mute = !endpoint.Mute;
        return endpoint.Mute;
    }
    [StructLayout(LayoutKind.Sequential)]
    struct MemoryStatus
    {
        public uint Length, Load;
        public ulong TotalPhysical, AvailablePhysical, TotalPage, AvailablePage, TotalVirtual, AvailableVirtual, AvailableExtended;
    }
    [StructLayout(LayoutKind.Sequential)]
    struct PowerStatus { public byte AcLine, Flags, Percent, Reserved; public uint LifeTime, FullLifeTime; }
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool GetSystemTimes(out ulong idle, out ulong kernel, out ulong user);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool GlobalMemoryStatusEx(ref MemoryStatus status);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool GetSystemPowerStatus(out PowerStatus status);
}
