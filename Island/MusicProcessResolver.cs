using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using NAudio.CoreAudioApi;

namespace Island;

internal static class MusicProcessResolver
{
    [DllImport("kernel32.dll", SetLastError=true)] static extern IntPtr OpenProcess(uint access, bool inherit, int pid);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);
    [DllImport("kernel32.dll", CharSet=CharSet.Unicode)] static extern int GetApplicationUserModelId(IntPtr p, ref uint n, StringBuilder? id);
    internal static string? AppId(int pid)
    {
        var h = OpenProcess(0x1000, false, pid);
        if (h == IntPtr.Zero) return null;
        try
        {
            uint n = 0;
            if (GetApplicationUserModelId(h, ref n, null) != 122 || n == 0 || n > 4096) return null;
            var s = new StringBuilder((int)n);
            return GetApplicationUserModelId(h, ref n, s) == 0 ? s.ToString() : null;
        }
        finally { CloseHandle(h); }
    }
    [StructLayout(LayoutKind.Sequential, CharSet=CharSet.Unicode)]
    struct Entry
    {
        public uint Size, Usage, Pid;
        public UIntPtr Heap;
        public uint Module, Threads, Parent;
        public int Priority;
        public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst=260)] public string Exe;
    }
    [DllImport("kernel32.dll")] static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint pid);
    [DllImport("kernel32.dll", CharSet=CharSet.Unicode)] static extern bool Process32FirstW(IntPtr h, ref Entry e);
    [DllImport("kernel32.dll", CharSet=CharSet.Unicode)] static extern bool Process32NextW(IntPtr h, ref Entry e);
    static int Root(int pid)
    {
        IntPtr h=CreateToolhelp32Snapshot(2,0);
        if(h==new IntPtr(-1)) return pid;
        try
        {
            var entries=new Dictionary<int,Entry>();
            var e=new Entry { Size=(uint)Marshal.SizeOf<Entry>(), Exe="" };
            if(Process32FirstW(h,ref e)) do { entries[(int)e.Pid]=e; } while(Process32NextW(h,ref e));
            for(int i=0;i<32 && entries.TryGetValue(pid,out var current) && entries.TryGetValue((int)current.Parent,out var parent);i++)
            {
                if(parent.Pid==current.Pid || !current.Exe.Equals(parent.Exe,StringComparison.OrdinalIgnoreCase)) break;
                using var childProcess=Process.GetProcessById(pid);
                using var parentProcess=Process.GetProcessById((int)parent.Pid);
                if(parentProcess.StartTime>childProcess.StartTime || parentProcess.SessionId!=childProcess.SessionId) break;
                // Equal filenames alone are not an identity boundary.
                string? parentExe=parentProcess.MainModule?.FileName, childExe=childProcess.MainModule?.FileName;
                if(string.IsNullOrEmpty(parentExe) || string.IsNullOrEmpty(childExe) || !string.Equals(parentExe,childExe,StringComparison.OrdinalIgnoreCase)) break;
                pid=(int)parent.Pid;
            }
        }
        catch { }
        finally { CloseHandle(h); }
        return pid;
    }
    internal static int Resolve(string? source)
    {
        if (string.IsNullOrWhiteSpace(source)) return 0;
        // Prefer a matching process with an audio session. Search all render endpoints,
        // not the default endpoint: a player may be routed to different headphones.
        var audible = new HashSet<int>();
        try
        {
            using var e = new MMDeviceEnumerator();
            var devices = e.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);
            foreach (var d in devices)
                using (d)
                {
                    var sessions = d.AudioSessionManager.Sessions;
                    for (int i=0; i<sessions.Count; i++)
                        using (var session = sessions[i]) audible.Add((int)session.GetProcessID);
                }
        }
        catch { }
        using var me = Process.GetCurrentProcess();
        int fallback = 0;
        foreach (var p in Process.GetProcesses())
            using (p)
                try
                {
                    if (p.SessionId != me.SessionId || !MusicSourcePolicy.Matches(source, p.ProcessName, AppId(p.Id))) continue;
                    if (audible.Contains(p.Id)) return Root(p.Id);
                    if (fallback == 0) fallback = p.Id;
                }
                catch { }
        return fallback == 0 ? 0 : Root(fallback);
    }
}
