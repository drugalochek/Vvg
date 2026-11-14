using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;
using NAudio.Wave;

namespace Island;

/// <summary>Windows build 20348+ application-loopback. No endpoint/global-mix fallback.</summary>
    [ComVisible(true), ComImport, Guid("72A22D78-CDE4-431D-B8CC-843A71199B6D"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IMokuAudioOperation
    {
        [PreserveSig] int GetActivateResult(out int result, [MarshalAs(UnmanagedType.IUnknown)] out object client);
    }
    [ComVisible(true), Guid("41D949AB-9862-444A-80F6-C261334DA5EB"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IMokuAudioCompletion
    {
        [PreserveSig] int ActivateCompleted(IMokuAudioOperation operation);
    }
    [ComVisible(true), Guid("94EA2B94-E9CC-49E0-C0FF-EE64CA8F5B90"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IMokuAgileObject { }

internal sealed class ProcessLoopbackCapture : IDisposable
{
    [DllImport("Mmdevapi.dll", ExactSpelling=true, CharSet=CharSet.Unicode)]
    static extern int ActivateAudioInterfaceAsync(string path, ref Guid iid, IntPtr activation,
        IMokuAudioCompletion completion, out IMokuAudioOperation operation);

    [ComVisible(true), ClassInterface(ClassInterfaceType.None)]
    sealed class Completion : IMokuAudioCompletion, IMokuAgileObject
    {
        internal readonly TaskCompletionSource<object> Result = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal IntPtr Variant, Parameters;
        internal Completion(int pid)
        {
            Parameters = Marshal.AllocCoTaskMem(12);
            Marshal.WriteInt32(Parameters, 0, 1); // AUDIOCLIENT_ACTIVATION_TYPE_PROCESS_LOOPBACK
            Marshal.WriteInt32(Parameters, 4, pid);
            Marshal.WriteInt32(Parameters, 8, 0); // INCLUDE_TARGET_PROCESS_TREE
            Variant = Marshal.AllocCoTaskMem(IntPtr.Size == 8 ? 24 : 16);
            for(int i=0;i<(IntPtr.Size == 8 ? 24 : 16);i++) Marshal.WriteByte(Variant, i, 0);
            Marshal.WriteInt16(Variant, 0, 65); // VT_BLOB
            Marshal.WriteInt32(Variant, 8, 12);
            Marshal.WriteIntPtr(Variant, IntPtr.Size == 8 ? 16 : 12, Parameters);
        }
        internal void Free()
        {
            if (Variant != IntPtr.Zero) { Marshal.FreeCoTaskMem(Variant); Variant=IntPtr.Zero; }
            if (Parameters != IntPtr.Zero) { Marshal.FreeCoTaskMem(Parameters); Parameters=IntPtr.Zero; }
        }
        public int ActivateCompleted(IMokuAudioOperation operation)
        {
            try
            {
                Marshal.ThrowExceptionForHR(operation.GetActivateResult(out int result, out object client));
                Marshal.ThrowExceptionForHR(result);
                Result.TrySetResult(client);
            }
            catch(Exception e) { Result.TrySetException(e); }
            finally { Free(); }
            return 0;
        }
    }
    internal volatile bool IsRunning;
    readonly int _pid;
    readonly CancellationTokenSource _stop = new();
    Task? _worker;
    public WaveFormat WaveFormat { get; } = new WaveFormat(44100,16,2);
    public event EventHandler<WaveInEventArgs>? DataAvailable;
    public event EventHandler<StoppedEventArgs>? RecordingStopped;
    internal ProcessLoopbackCapture(int pid) => _pid=pid;
    public void StartRecording()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10,0,20348))
            throw new PlatformNotSupportedException("Isolated music analysis requires Windows build 20348 or newer.");
        _worker = Task.Run(Run);
    }
    void Run()
    {
        Exception? error=null;
        AudioClient? audio=null;
        IMokuAudioOperation? operation=null;
        try
        {
            var completion=new Completion(_pid);
            Guid iid=typeof(IAudioClient).GUID;
            int hr=ActivateAudioInterfaceAsync("VAD\\Process_Loopback", ref iid, completion.Variant, completion, out operation);
            if(hr<0) { completion.Free(); Marshal.ThrowExceptionForHR(hr); }
            // Keep callback/variant alive until activation finishes. Stop waits asynchronously,
            // and all COM client cleanup stays on this worker (never in the UI thread).
            var raw=completion.Result.Task.GetAwaiter().GetResult();
            audio=new AudioClient((IAudioClient)raw);
            if(_stop.IsCancellationRequested) return;
            using var ready=new AutoResetEvent(false);
            audio.Initialize(AudioClientShareMode.Shared,
                AudioClientStreamFlags.Loopback | AudioClientStreamFlags.EventCallback | AudioClientStreamFlags.AutoConvertPcm,
                0,0,WaveFormat,Guid.Empty);
            var capture=audio.AudioCaptureClient;
            audio.SetEventHandle(ready.SafeWaitHandle.DangerousGetHandle());
            audio.Start();
            IsRunning=true;
            byte[] buffer=Array.Empty<byte>();
            while(!_stop.IsCancellationRequested)
            {
                if(WaitHandle.WaitAny(new[] { ready, _stop.Token.WaitHandle },250)==1) break;
                while(!_stop.IsCancellationRequested && capture.GetNextPacketSize()>0)
                {
                    var data=capture.GetBuffer(out int frames,out AudioClientBufferFlags flags);
                    try
                    {
                        int bytes=checked(frames*WaveFormat.BlockAlign);
                        if(buffer.Length<bytes) buffer=new byte[bytes];
                        if((flags & AudioClientBufferFlags.Silent)!=0) Array.Clear(buffer,0,bytes);
                        else Marshal.Copy(data,buffer,0,bytes);
                        DataAvailable?.Invoke(this,new WaveInEventArgs(buffer,bytes));
                    }
                    finally { capture.ReleaseBuffer(frames); }
                }
            }
            audio.Stop();
        }
        catch(Exception e) { error=e; }
        finally
        {
            IsRunning=false;
            audio?.Dispose();
            if(operation!=null && Marshal.IsComObject(operation)) Marshal.ReleaseComObject(operation);
            RecordingStopped?.Invoke(this,new StoppedEventArgs(error));
        }
    }
    public void StopRecording() => _stop.Cancel();
    public void Dispose()
    {
        _stop.Cancel();
        // Do not block UI or deadlock its analyzer lock on worker callbacks.
        if(_worker!=null) _=_worker.ContinueWith(_=>_stop.Dispose()); else _stop.Dispose();
    }
}
