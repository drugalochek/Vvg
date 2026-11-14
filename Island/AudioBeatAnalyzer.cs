using System.Diagnostics;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;
using NAudio.Wave;
using Timer = System.Threading.Timer;

namespace Island;

/// <summary>
/// Measures low, mid and high energy from the current player process only.
/// Application loopback requires Windows build 20348+. Never falls back to the system mix.
/// • Захват работает только пока играет музыка (SetActive) — в простое не тратит CPU.
/// • При смене устройства вывода (наушники, Bluetooth, HDMI) захват автоматически
///   пересоздаётся на новом устройстве — волна больше не «умирает» после переключения.
/// </summary>
internal sealed class AudioBeatAnalyzer : IDisposable, IMMNotificationClient
{
    /// <summary>Одна сессия захвата со своими фильтрами: старый поток никогда не трогает состояние нового.</summary>
    sealed class CaptureSession
    {
        public required ProcessLoopbackCapture Capture;
        public int ProcessId;
        public required Biquad LowPass, MidHighPass, MidLowPass, HighPass;
        public int Channels, Bits, SampleRate;
        public bool IsFloat;
        public double BassFloor;
    }

    readonly object _gate = new();
    readonly MMDeviceEnumerator? _enumerator;
    CaptureSession? _session;
    Timer? _restartTimer;
    bool _wantActive, _disposed;
    string? _sourceAppId;
    readonly Timer _sourceTimer;
    float _bassLevel, _midLevel, _highLevel, _activityLevel;
    int _beatPending;
    long _lastBeatTicks;

    public float BassLevel => Volatile.Read(ref _bassLevel);
    public float MidLevel => Volatile.Read(ref _midLevel);
    public float HighLevel => Volatile.Read(ref _highLevel);
    public float ActivityLevel => Volatile.Read(ref _activityLevel);

    public AudioBeatAnalyzer()
    {
        _sourceTimer = new Timer(_ => CheckSource(), null, 2000, 2000);
        try
        {
            _enumerator = new MMDeviceEnumerator();
            _enumerator.RegisterEndpointNotificationCallback(this);
        }
        catch (Exception ex)
        {
            _enumerator = null;
            Log.Warn("Audio device notifications unavailable: " + ex.Message);
        }
    }

    public bool ConsumeBeat() => Interlocked.Exchange(ref _beatPending, 0) != 0;

    /// <summary>Включить/выключить анализ звука (вызывается из UI при play/pause).</summary>
    public void SetActive(bool active, string? sourceAppId)
    {
        lock (_gate)
        {
            if (_disposed || (_wantActive == active && _sourceAppId == sourceAppId)) return;
            _wantActive = active;
            _sourceAppId = sourceAppId;
        }
        if (active) ScheduleRestart(0);
        else
        {
            CancelRestart();
            StopCapture();
        }
    }

    void ScheduleRestart(int delayMs)
    {
        lock (_gate)
        {
            if (_disposed) return;
            _restartTimer?.Dispose();
            _restartTimer = new Timer(_ =>
            {
                StopCapture();
                StartCapture();
            }, null, delayMs, Timeout.Infinite);
        }
    }

    void CancelRestart()
    {
        lock (_gate)
        {
            _restartTimer?.Dispose();
            _restartTimer = null;
        }
    }

    void StartCapture()
    {
        lock (_gate)
        {
            if (_disposed || !_wantActive || _session != null) return;
            try
            {
                if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 20348)) return;
                int pid = MusicProcessResolver.Resolve(_sourceAppId);
                if (pid == 0) return; // Unknown music source: silence, never the Windows mix.
                var capture = new ProcessLoopbackCapture(pid);
                var format = capture.WaveFormat;
                int rate = format.SampleRate;
                var session = new CaptureSession
                {
                    Capture = capture,
                    ProcessId = pid,
                    Channels = Math.Max(1, format.Channels),
                    Bits = format.BitsPerSample,
                    SampleRate = rate,
                    IsFloat = format.Encoding == WaveFormatEncoding.IeeeFloat && format.BitsPerSample == 32,
                    LowPass = Biquad.LowPass(rate, 160),
                    MidHighPass = Biquad.HighPass(rate, 160),
                    MidLowPass = Biquad.LowPass(rate, 2200),
                    HighPass = Biquad.HighPass(rate, 2200)
                };
                capture.DataAvailable += (_, e) => OnDataAvailable(session, e);
                capture.RecordingStopped += (_, e) => OnRecordingStopped(session, e);
                _session = session;
                capture.StartRecording();
                Log.Info($"Audio multi-band analysis started ({rate} Hz, {session.Channels} ch, {session.Bits}-bit)");
            }
            catch (Exception ex)
            {
                _session = null;
                Log.Warn("WASAPI loopback unavailable; retrying later: " + ex.Message);
                // Например, нет ни одного устройства вывода. Повторим, когда оно появится, или через 5 с.
                _restartTimer?.Dispose();
                _restartTimer = new Timer(_ => StartCapture(), null, 5000, Timeout.Infinite);
            }
        }
    }

    int _checkingSource;
    void CheckSource()
    {
        if (Interlocked.Exchange(ref _checkingSource, 1) != 0) return;
        try
        {
            lock (_gate)
            {
                if (_disposed || !_wantActive || !OperatingSystem.IsWindowsVersionAtLeast(10, 0, 20348)) return;
                int pid = MusicProcessResolver.Resolve(_sourceAppId);
                if (pid == (_session?.ProcessId ?? 0)) return;
                ScheduleRestart(0);
            }
        }
        catch (Exception ex) { Log.Warn("Music source refresh: " + ex.Message); }
        finally { Volatile.Write(ref _checkingSource, 0); }
    }

    void StopCapture()
    {
        CaptureSession? session;
        lock (_gate)
        {
            session = _session;
            _session = null;
        }
        Interlocked.Exchange(ref _beatPending, 0);
        Volatile.Write(ref _bassLevel, 0);
        Volatile.Write(ref _midLevel, 0);
        Volatile.Write(ref _highLevel, 0);
        Volatile.Write(ref _activityLevel, 0);
        if (session == null) return;
        try
        {
            session.Capture.StopRecording();
            session.Capture.Dispose();
        }
        catch (Exception ex) { Log.Warn("Audio capture stop: " + ex.Message); }
    }

    void OnRecordingStopped(CaptureSession session, StoppedEventArgs e)
    {
        bool current;
        lock (_gate) current = ReferenceEquals(_session, session) && !_disposed && _wantActive;
        if (!current) return;                       // это мы сами остановили старую сессию
        if (e.Exception != null) Log.Warn("WASAPI loopback stopped: " + e.Exception.Message);
        ScheduleRestart(1000);                      // устройство пропало/сменилось — переподключаемся
    }

    void OnDataAvailable(CaptureSession s, WaveInEventArgs e)
    {
        if (_disposed || e.BytesRecorded <= 0 || !ReferenceEquals(Volatile.Read(ref _session), s)) return;
        try
        {
            int bytesPerSample = Math.Max(1, s.Bits / 8);
            int frameBytes = bytesPerSample * s.Channels;
            int frames = e.BytesRecorded / frameBytes;
            if (frames == 0) return;

            double lowSquares = 0, midSquares = 0, highSquares = 0;
            for (int frame = 0; frame < frames; frame++)
            {
                double mono = 0;
                int offset = frame * frameBytes;
                for (int channel = 0; channel < s.Channels; channel++)
                    mono += ReadSample(e.Buffer, offset + channel * bytesPerSample, s.IsFloat, s.Bits);
                mono /= s.Channels;

                double low = s.LowPass.Process(mono);
                double midSample = s.MidLowPass.Process(s.MidHighPass.Process(mono));
                double highSample = s.HighPass.Process(mono);
                lowSquares += low * low;
                midSquares += midSample * midSample;
                highSquares += highSample * highSample;
            }

            double lowRms = Math.Sqrt(lowSquares / frames);
            double midRms = Math.Sqrt(midSquares / frames);
            double highRms = Math.Sqrt(highSquares / frames);
            double seconds = (double)frames / s.SampleRate;

            // Compress each band independently so quieter instruments remain visible.
            float bass = (float)Math.Clamp(lowRms * 25.0, 0, 1);
            float mid = (float)Math.Clamp(midRms * 24.0, 0, 1);
            float high = (float)Math.Clamp(highRms * 22.0, 0, 1);
            float activity = Math.Clamp(bass * 0.40f + mid * 0.36f + high * 0.24f, 0, 1);
            Volatile.Write(ref _bassLevel, bass);
            Volatile.Write(ref _midLevel, mid);
            Volatile.Write(ref _highLevel, high);
            Volatile.Write(ref _activityLevel, activity);

            // Kick detection remains low-band and transient-based; the visible wave uses all bands.
            double floorFollow = 1.0 - Math.Exp(-seconds / 1.35);
            s.BassFloor += (lowRms - s.BassFloor) * floorFollow;
            double threshold = Math.Max(0.005, s.BassFloor * 1.42);
            long now = Stopwatch.GetTimestamp();
            long previous = Interlocked.Read(ref _lastBeatTicks);
            bool refractoryPassed = now - previous > Stopwatch.Frequency * 0.17;
            if (lowRms > threshold && lowRms > 0.007 && refractoryPassed)
            {
                Interlocked.Exchange(ref _lastBeatTicks, now);
                Interlocked.Exchange(ref _beatPending, 1);
            }
        }
        catch (Exception ex) { Log.Warn("Audio beat analysis failed: " + ex.Message); }
    }

    static double ReadSample(byte[] buffer, int offset, bool isFloat, int bits)
    {
        if (isFloat) return BitConverter.ToSingle(buffer, offset);
        return bits switch
        {
            8 => (buffer[offset] - 128) / 128.0,
            16 => BitConverter.ToInt16(buffer, offset) / 32768.0,
            24 => ReadInt24(buffer, offset) / 8388608.0,
            32 => BitConverter.ToInt32(buffer, offset) / 2147483648.0,
            _ => 0
        };
    }

    static int ReadInt24(byte[] data, int offset)
    {
        int value = data[offset] | (data[offset + 1] << 8) | (data[offset + 2] << 16);
        return (value & 0x800000) != 0 ? value | unchecked((int)0xFF000000) : value;
    }

    // ---- IMMNotificationClient: Windows сообщает о смене устройств вывода ----
    void IMMNotificationClient.OnDefaultDeviceChanged(DataFlow flow, Role role, string defaultDeviceId)
    {
        if (flow != DataFlow.Render || role != Role.Multimedia) return;
        bool active;
        lock (_gate) active = _wantActive && !_disposed;
        if (active)
        {
            Log.Info("Default audio output changed — restarting loopback capture");
            ScheduleRestart(400);                   // небольшая пауза: драйвер нового устройства успевает проснуться
        }
    }

    void IMMNotificationClient.OnDeviceStateChanged(string deviceId, DeviceState newState) { }
    void IMMNotificationClient.OnDeviceAdded(string pwstrDeviceId) { }
    void IMMNotificationClient.OnDeviceRemoved(string deviceId) { }
    void IMMNotificationClient.OnPropertyValueChanged(string pwstrDeviceId, PropertyKey key) { }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _sourceTimer.Dispose();
            _wantActive = false;
            _restartTimer?.Dispose();
            _restartTimer = null;
        }
        try { _enumerator?.UnregisterEndpointNotificationCallback(this); } catch { }
        StopCapture();
        try { _enumerator?.Dispose(); } catch { }
    }

    sealed class Biquad
    {
        readonly double _b0, _b1, _b2, _a1, _a2;
        double _x1, _x2, _y1, _y2;

        Biquad(double b0, double b1, double b2, double a0, double a1, double a2)
        {
            _b0 = b0 / a0; _b1 = b1 / a0; _b2 = b2 / a0;
            _a1 = a1 / a0; _a2 = a2 / a0;
        }

        public static Biquad LowPass(int rate, double cutoff)
        {
            double w = 2 * Math.PI * cutoff / rate;
            double cos = Math.Cos(w), alpha = Math.Sin(w) / (2 * 0.70710678);
            return new Biquad((1 - cos) / 2, 1 - cos, (1 - cos) / 2,
                1 + alpha, -2 * cos, 1 - alpha);
        }

        public static Biquad HighPass(int rate, double cutoff)
        {
            double w = 2 * Math.PI * cutoff / rate;
            double cos = Math.Cos(w), alpha = Math.Sin(w) / (2 * 0.70710678);
            return new Biquad((1 + cos) / 2, -(1 + cos), (1 + cos) / 2,
                1 + alpha, -2 * cos, 1 - alpha);
        }

        public double Process(double input)
        {
            double output = _b0 * input + _b1 * _x1 + _b2 * _x2 - _a1 * _y1 - _a2 * _y2;
            _x2 = _x1; _x1 = input; _y2 = _y1; _y1 = output;
            return output;
        }
    }
}
