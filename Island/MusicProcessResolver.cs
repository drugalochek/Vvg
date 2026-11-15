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
    internal static int Root(int pid)
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
    sealed record Candidate(int Pid, string Name, string? Path, string? PackagedId);
    static readonly object ResolveGate = new();
    static string? _cachedSource;
    static long _cacheUntil;
    static int _cachedPid;
    static long _cachedStart;
    internal static int Resolve(string? source)
    {
        if (string.IsNullOrWhiteSpace(source)) return 0;
        lock (ResolveGate)
        {
            if (source == _cachedSource && Stopwatch.GetTimestamp() < _cacheUntil && _cachedPid > 0)
                try
                {
                    using var cached = Process.GetProcessById(_cachedPid);
                    if (!cached.HasExited && cached.StartTime.ToUniversalTime().Ticks == _cachedStart) return _cachedPid;
                }
                catch { }
            int pid = ResolveCore(source);
            if (_cachedSource != source || _cachedPid != pid)
            {
                string label = source.Replace("\r", "").Replace("\n", "");
                if (label.Length > 180) label = label[..180];
                Log.Info($"Music source '{label}' resolved to player PID {pid}");
            }
            _cachedSource = source;
            _cachedPid = pid;
            _cacheUntil = Stopwatch.GetTimestamp() + Stopwatch.Frequency;
            _cachedStart = 0;
            if (pid > 0)
                try { using var p = Process.GetProcessById(pid); _cachedStart = p.StartTime.ToUniversalTime().Ticks; }
                catch { _cachedPid = 0; }
            return _cachedPid;
        }
    }
    static int ResolveCore(string source)
    {
        var audible = new HashSet<int>();
        try
        {
            using var e = new MMDeviceEnumerator();
            foreach (var d in e.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
                using (d)
                {
                    var sessions = d.AudioSessionManager.Sessions;
                    for (int i = 0; i < sessions.Count; i++)
                        using (var session = sessions[i])
                            if (!session.IsSystemSoundsSession && session.GetProcessID > 0)
                                audible.Add(Root((int)session.GetProcessID));
                }
        }
        catch { }
        using var me = Process.GetCurrentProcess();
        var candidates = new List<Candidate>();
        foreach (var p in Process.GetProcesses())
            using (p)
                try
                {
                    if (p.SessionId != me.SessionId) continue;
                    string? path = null;
                    try { path = p.MainModule?.FileName; } catch { }
                    candidates.Add(new(p.Id, p.ProcessName, path, AppId(p.Id)));
                }
                catch { }
        var matches = candidates.Where(p => MusicSourcePolicy.Matches(source, p.Name, p.PackagedId)).ToList();
        // Unpackaged Electron players (including Yandex Music) may publish an explicit
        // desktop AUMID which GetApplicationUserModelId cannot read from the process.
        // Read the exact window property first, registered shortcut identity second.
        var owners = MusicAppIdentity.WindowOwners(source, candidates.Select(p => p.Pid).ToHashSet());
        matches.AddRange(candidates.Where(p => owners.Contains(p.Pid)));
        if (matches.Count == 0)
        {
            var paths = MusicAppIdentity.ShortcutExecutables(source);
            matches.AddRange(candidates.Where(p => paths.Any(path => MusicSourcePolicy.MatchesExecutablePath(path, p.Path))));
        }
        int[] roots = matches.Select(p => Root(p.Pid)).Distinct().ToArray();
        // The sound can belong to a child/helper rather than the media-control window.
        return MusicSourcePolicy.SelectRoot(roots, audible); // Ambiguous player: never guess the loudest process.
    }
}
