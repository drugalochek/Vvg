using System.Diagnostics;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace Island;

/// <summary>
/// Analyses the Windows output mix through WASAPI loopback.
/// A band-pass filter isolates bass energy; an adaptive threshold detects beat transients.
/// </summary>
internal sealed class AudioBeatAnalyzer : IDisposable
{
    readonly WasapiLoopbackCapture _capture;
    readonly Biquad _highPass;
    readonly Biquad _lowPass;
    readonly int _channels;
    readonly int _bits;
    readonly bool _isFloat;
    float _bassLevel;
    int _beatPending;
    long _lastBeatTicks;
    double _bassFloor;
    bool _disposed;

    public float BassLevel => Volatile.Read(ref _bassLevel);

    public AudioBeatAnalyzer()
    {
        try
        {
            _capture = new WasapiLoopbackCapture();
            var format = _capture.WaveFormat;
            _channels = Math.Max(1, format.Channels);
            _bits = format.BitsPerSample;
            _isFloat = format.Encoding == WaveFormatEncoding.IeeeFloat && _bits == 32;
            _highPass = Biquad.HighPass(format.SampleRate, 38);
            _lowPass = Biquad.LowPass(format.SampleRate, 155);
            _capture.DataAvailable += OnDataAvailable;
            _capture.RecordingStopped += OnRecordingStopped;
            _capture.StartRecording();
            Log.Info($"Audio beat analysis started ({format.SampleRate} Hz, {_channels} ch, {_bits}-bit)");
        }
        catch (Exception ex)
        {
            Log.Warn("WASAPI loopback unavailable; music wave will use a quiet fallback: " + ex.Message);
        }
    }

    public bool ConsumeBeat() => Interlocked.Exchange(ref _beatPending, 0) != 0;

    void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        if (_disposed || e.BytesRecorded <= 0) return;
        try
        {
            int bytesPerSample = Math.Max(1, _bits / 8);
            int frameBytes = bytesPerSample * _channels;
            int frames = e.BytesRecorded / frameBytes;
            if (frames == 0) return;

            double bassSquares = 0;
            double allSquares = 0;
            for (int frame = 0; frame < frames; frame++)
            {
                double mono = 0;
                int offset = frame * frameBytes;
                for (int channel = 0; channel < _channels; channel++)
                    mono += ReadSample(e.Buffer, offset + channel * bytesPerSample);
                mono /= _channels;

                allSquares += mono * mono;
                double bass = _lowPass.Process(_highPass.Process(mono));
                bassSquares += bass * bass;
            }

            double bassRms = Math.Sqrt(bassSquares / frames);
            double totalRms = Math.Sqrt(allSquares / frames);
            double seconds = (double)frames / _capture.WaveFormat.SampleRate;

            // The floor follows the room/music bed slowly, while beat transients rise quickly.
            double floorFollow = 1.0 - Math.Exp(-seconds / 1.35);
            _bassFloor += (bassRms - _bassFloor) * floorFollow;
            double threshold = Math.Max(0.0065, _bassFloor * 1.48);
            long now = Stopwatch.GetTimestamp();
            long previous = Interlocked.Read(ref _lastBeatTicks);
            bool refractoryPassed = now - previous > Stopwatch.Frequency * 0.17;
            if (bassRms > threshold && bassRms > 0.009 && refractoryPassed)
            {
                Interlocked.Exchange(ref _lastBeatTicks, now);
                Interlocked.Exchange(ref _beatPending, 1);
            }

            // Normalize the low-frequency envelope for the UI's spring response.
            float level = (float)Math.Clamp(bassRms * 13.5, 0, 1);
            Volatile.Write(ref _bassLevel, level);
        }
        catch (Exception ex)
        {
            Log.Warn("Audio beat analysis failed: " + ex.Message);
        }
    }

    double ReadSample(byte[] buffer, int offset)
    {
        if (_isFloat) return BitConverter.ToSingle(buffer, offset);
        return _bits switch
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

    void OnRecordingStopped(object? sender, StoppedEventArgs e)
    {
        if (e.Exception != null && !_disposed)
            Log.Warn("WASAPI loopback stopped: " + e.Exception.Message);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try
        {
            _capture.DataAvailable -= OnDataAvailable;
            _capture.RecordingStopped -= OnRecordingStopped;
            _capture.StopRecording();
            _capture.Dispose();
        }
        catch (Exception ex) { Log.Warn("Audio analyzer shutdown: " + ex.Message); }
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
            double output = _b0 * input + _b1 * _x1 + _b2 * _x2
                - _a1 * _y1 - _a2 * _y2;
            _x2 = _x1; _x1 = input;
            _y2 = _y1; _y1 = output;
            return output;
        }
    }
}
