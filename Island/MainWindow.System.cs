using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;
using System.Windows.Media;
using Brush = System.Windows.Media.Brush;

namespace Island;

public partial class MainWindow
{
    readonly SystemMonitor _systemMonitor = new();
    readonly DispatcherTimer _systemTimer = new() { Interval = TimeSpan.FromSeconds(2) };
    SystemDashboard? _systemDashboard;
    bool _systemInitialized, _systemReading;
    int _systemGeneration;
    bool _systemResetPending = true;

    void EnsureSystemDashboard(bool rebuild = false)
    {
        if (!_systemInitialized)
        {
            _systemInitialized = true;
            _systemTimer.Tick += async (_,_) => await ReadSystemDashboard();
        }
        if (_systemDashboard != null && !rebuild) return;
        _systemDashboard = new SystemDashboard(Ru, _s.CornerRadius, OnSystemAction,
            TryFindResource("Card") as Brush, TryFindResource("CardRim") as Brush);
        SystemPage.Content = _systemDashboard;
    }
    void UpdateSystemDashboardState()
    {
        if (SystemPage == null) return;
        bool show = SystemPanelPolicy.Poll(_state == IslandState.Expanded, SystemPage.Visibility == Visibility.Visible);
        if (!show) { StopSystemDashboard(); return; }
        EnsureSystemDashboard();
        if (!_systemTimer.IsEnabled)
        {
            // A new view must not reuse a CPU delta covering time spent collapsed.
            _systemResetPending = true;
            _systemTimer.Start(); _ = ReadSystemDashboard();
        }
    }
    async Task ReadSystemDashboard()
    {
        if (_systemReading || !_systemTimer.IsEnabled) return;
        int generation = _systemGeneration; _systemReading = true;
        try
        {
            bool reset = _systemResetPending; _systemResetPending = false;
            var data = await Task.Run(() => { if (reset) _systemMonitor.Reset(); return _systemMonitor.Read(); });
            if (generation == _systemGeneration && _systemTimer.IsEnabled) _systemDashboard?.Update(data);
        }
        catch (Exception ex) { Log.Error("System dashboard sample failed",ex); }
        finally { _systemReading = false; }
    }
    void StopSystemDashboard()
    {
        _systemTimer.Stop(); ++_systemGeneration; _systemDashboard?.CancelConfirmation();
    }
    void ResizeForSystemPage(bool system)
    {
        if (Full == null) return;
        double height = system ? 464 : 316;
        if (Math.Abs(Full.Height-height) < 0.1) return;
        Full.Height = height;
        if (_state == IslandState.Expanded) Animate(FullW,height+32,30,spring:false,speed:_s.ExpandSpeed/100.0);
    }
    void OnSystemAction(string action)
    {
        try
        {
            var uri = SystemPanelPolicy.SettingsUri(action);
            if (uri != null) { Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true }); return; }
            switch(action)
            {
                case "microphone":
                    SystemMonitor.ToggleMicrophoneMute(); _ = ReadSystemDashboard(); break;
                case "taskmanager":
                    Launch("taskmgr.exe"); break;
                case "desktop":
                    var shellType = Type.GetTypeFromProgID("Shell.Application") ?? throw new InvalidOperationException("Windows Shell unavailable");
                    dynamic shell = Activator.CreateInstance(shellType)!;
                    try { shell.ToggleDesktop(); } finally { Marshal.FinalReleaseComObject(shell); }
                    Collapse(); break;
                case "lock":
                    if (!SystemLockWorkStation()) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
                    Collapse(); break;
                case "sleep":
                    if (!SystemSetSuspendState(false,false,false)) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
                    Collapse(); break;
                case "restart": case "shutdown":
                    // Never /f: Windows must let unsaved applications block shutdown.
                    Process.Start(new ProcessStartInfo(System.IO.Path.Combine(Environment.SystemDirectory,"shutdown.exe"),action == "restart" ? "/r /t 0" : "/s /t 0") { UseShellExecute = false, CreateNoWindow = true });
                    Collapse(); break;
            }
        }
        catch(Exception ex)
        {
            Log.Error("System action failed: "+action,ex);
            ShowToast("Moku",Tr("Действие недоступно. Проверь параметры Windows.","Action unavailable. Check Windows settings."),ColorError);
        }
    }
    [DllImport("user32.dll", EntryPoint="LockWorkStation", SetLastError=true)] [return:MarshalAs(UnmanagedType.Bool)]
    static extern bool SystemLockWorkStation();
    [DllImport("powrprof.dll", EntryPoint="SetSuspendState", SetLastError=true)] [return:MarshalAs(UnmanagedType.U1)]
    static extern bool SystemSetSuspendState([MarshalAs(UnmanagedType.U1)] bool hibernate,[MarshalAs(UnmanagedType.U1)] bool force,[MarshalAs(UnmanagedType.U1)] bool disableWake);
}
