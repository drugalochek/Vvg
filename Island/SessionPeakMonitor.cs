using System.Diagnostics;
using NAudio.CoreAudioApi;

namespace Island;

/// <summary>Windows 10 per-player level meter. No PCM capture or endpoint meter.</summary>
internal sealed class SessionPeakMonitor : IDisposable
{
    readonly CancellationTokenSource _stop = new();
    readonly List<AudioSessionControl> _sessions = new();
    readonly List<MMDevice> _devices = new();
    readonly int _pid;
    readonly long _started;
    Task? _worker;
    int _disposed;
    internal int ProcessId => _pid;
    internal bool IsCompleted => _worker?.IsCompleted == true;
    internal event Action<float>? LevelAvailable;

    internal SessionPeakMonitor(int pid)
    {
        _pid = pid;
        using var p = Process.GetProcessById(pid);
        _started = p.StartTime.ToUniversalTime().Ticks;
    }
    internal void Start() => _worker = Task.Run(Run);
    bool SameProcess()
    {
        try
        {
            using var p = Process.GetProcessById(_pid);
            return !p.HasExited && p.StartTime.ToUniversalTime().Ticks == _started;
        }
        catch { return false; }
    }
    void ClearBindings()
    {
        foreach (var s in _sessions) try { s.Dispose(); } catch { }
        _sessions.Clear();
        foreach (var d in _devices) try { d.Dispose(); } catch { }
        _devices.Clear();
    }
    void Bind(MMDeviceEnumerator enumerator)
    {
        ClearBindings();
        foreach (var device in enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
        {
            _devices.Add(device);
            try
            {
                var sessions = device.AudioSessionManager.Sessions;
                for (int i = 0; i < sessions.Count; i++)
                {
                    var session = sessions[i];
                    bool keep = false;
                    try
                    {
                        int pid = (int)session.GetProcessID;
                        keep = AudioLevelPolicy.AcceptSession(_pid,
                            pid == _pid ? pid : pid > 0 ? MusicProcessResolver.Root(pid) : 0, session.IsSystemSoundsSession)
                            && session.AudioMeterInformation != null;
                    }
                    catch { }
                    if (keep) _sessions.Add(session); else session.Dispose();
                }
            }
            catch { } // One unavailable output does not hide other devices.
        }
    }
    void Run()
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            long nextBind = 0;
            while (!_stop.IsCancellationRequested && SameProcess())
            {
                long now = Stopwatch.GetTimestamp();
                if (now >= nextBind)
                {
                    try { Bind(enumerator); } catch { ClearBindings(); }
                    nextBind = now + Stopwatch.Frequency; // New sessions / output routing.
                }
                float peak = 0;
                foreach (var session in _sessions)
                    try
                    {
                        float value = session.AudioMeterInformation.MasterPeakValue;
                        if (float.IsFinite(value)) peak = Math.Max(peak, Math.Clamp(value, 0f, 1f));
                    }
                    catch { nextBind = 0; }
                LevelAvailable?.Invoke(peak);
                if (_stop.Token.WaitHandle.WaitOne(33)) break;
            }
        }
        catch (Exception ex) { Log.Warn("Player session meter: " + ex.Message); }
        finally
        {
            ClearBindings();
            LevelAvailable?.Invoke(0);
        }
    }
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _stop.Cancel();
        // Never wait for worker while the analyzer lock is held.
        if (_worker != null) _ = _worker.ContinueWith(_ => _stop.Dispose());
        else _stop.Dispose();
    }
}
