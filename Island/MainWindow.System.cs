using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;
using Windows.Devices.Radios;
using Brush = System.Windows.Media.Brush;

namespace Island;

public partial class MainWindow
{
    readonly SystemMonitor _systemMonitor = new();
    readonly HappVpnConnector _happVpn = new();
    readonly DispatcherTimer _systemTimer = new() { Interval = TimeSpan.FromSeconds(2) };
    SystemDashboard? _systemDashboard;
    SystemSnapshot? _systemLast;
    bool _systemInitialized, _systemReading, _happLegacySent;
    readonly HashSet<string> _systemBusy = new();
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
            TryFindResource("Card") as Brush, TryFindResource("CardRim") as Brush, _s.VpnClient);
        if (_systemLast != null) _systemDashboard.Update(_systemLast);
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
            string client = _s.VpnClient;
            bool mokuOff = _s.InternetDisabledAdapters.Count > 0;
            var data = await Task.Run(() => { if (reset) _systemMonitor.Reset(); return _systemMonitor.Read(client, mokuOff); });
            if (generation != _systemGeneration || !_systemTimer.IsEnabled) return;
            _systemLast = data;
            _systemDashboard?.Update(data);
            HappFallback(data);
        }
        catch (Exception ex) { Log.Error("System dashboard sample failed",ex); }
        finally { _systemReading = false; }
    }
    /// <summary>Older Happ builds only know happ://open: send it once if happ://connect changed nothing.</summary>
    void HappFallback(SystemSnapshot data)
    {
        if (_s.VpnClient != "Happ" || _happLegacySent || _systemDashboard == null) return;
        if (_systemDashboard.IsPending("vpn", out bool target, out var elapsed) && target && data.Vpn != true && elapsed > TimeSpan.FromSeconds(5))
        {
            _happLegacySent = true;
            _happVpn.RequestLegacyConnection();
        }
    }
    void StopSystemDashboard()
    {
        _systemTimer.Stop(); ++_systemGeneration; _systemDashboard?.CancelConfirmation();
    }
    void ResizeForSystemPage(bool system)
    {
        if (Full == null) return;
        // Keep the dashboard within the work area at large island scales.
        // Its ScrollViewer retains all controls, including power confirmation.
        double scale = Math.Max(0.1, _s.Scale / 100.0);
        double room = (SystemParameters.WorkArea.Height - 36 - Math.Max(0,_s.OffsetY)) / (scale * 1.16) - 32;
        double height = system ? Math.Clamp(room,240,372) : 316;
        if (Math.Abs(Full.Height-height) < 0.1) return;
        Full.Height = height;
        if (_state == IslandState.Expanded) Animate(FullW,height+32,30,spring:false,speed:_s.ExpandSpeed/100.0);
    }

    async void OnSystemAction(string action)
    {
        try
        {
            switch (action)
            {
                case "internet":
                    await ToggleInternet(); break;
                case "bluetooth":
                    await ToggleRadio(action, RadioKind.Bluetooth); break;
                case "vpn":
                    await ToggleVpn(); break;
                case "microphone":
                    await Task.Run(SystemMonitor.ToggleMicrophoneMute); _ = ReadSystemDashboard(); break;
                case "sleep":
                    Collapse();
                    if (!SystemSetSuspendState(false,false,false)) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
                    break;
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

    async Task ToggleRadio(string id, RadioKind kind)
    {
        if (!_systemBusy.Add(id)) return;
        try
        {
            bool target = _systemLast?.Bluetooth != true;
            _systemDashboard?.SetPending(id, target);
            var result = await Task.Run(() => _systemMonitor.SetRadio(kind, target));
            _systemDashboard?.SetResult(id, result);
            if (result == ToggleResult.Denied) OpenFallback(id);
            _ = ReadSystemDashboard();
        }
        finally { _systemBusy.Remove(id); }
    }

    async Task ToggleInternet()
    {
        if (!_systemBusy.Add("internet")) return;
        _modalOpen = true;   // the UAC prompt takes focus: keep the island open
        try
        {
            bool target = _systemLast?.Internet != true;
            var remembered = _s.InternetDisabledAdapters.ToList();
            var (result, disabled) = await Task.Run(() => (SystemMonitor.SetInternet(target, remembered, out var off), off));
            if (result == ToggleResult.Done)
            {
                _s.InternetDisabledAdapters = target ? new() : disabled;
                _s.Save();
                _systemDashboard?.SetPending("internet", target);
            }
            else _systemDashboard?.SetResult("internet", result);
            _ = ReadSystemDashboard();
        }
        finally { _systemBusy.Remove("internet"); _modalOpen = false; }
    }

    async Task ToggleVpn()
    {
        if (!_systemBusy.Add("vpn")) return;
        try
        {
            bool connected = _systemLast?.Vpn == true;
            if (_s.VpnClient == "Happ")
            {
                _happLegacySent = false;
                var sent = connected ? _happVpn.RequestDisconnection() : _happVpn.RequestConnection();
                if (sent == HappVpnRequestResult.Sent) _systemDashboard?.SetPending("vpn", !connected);
                else _systemDashboard?.SetVpnRequestResult(sent);
            }
            else
            {
                _systemDashboard?.SetPending("vpn", !connected);
                string? entry = _systemLast?.VpnEntry;
                var result = await Task.Run(() => SystemMonitor.ToggleWindowsVpn(connected, entry));
                _systemDashboard?.SetResult("vpn", result);
                if (result == ToggleResult.NoProfile)
                {
                    OpenFallback("vpn");
                    ShowToast("VPN", Tr("Добавь VPN в параметрах Windows или выбери Happ в настройках Moku.", "Add a VPN in Windows Settings or choose Happ in Moku settings."));
                }
            }
            _ = ReadSystemDashboard();
        }
        finally { _systemBusy.Remove("vpn"); }
    }

    void OpenFallback(string id)
    {
        var uri = SystemPanelPolicy.FallbackUri(id);
        if (uri == null) return;
        try { Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true }); } catch { }
    }

    [DllImport("powrprof.dll", EntryPoint="SetSuspendState", SetLastError=true)] [return:MarshalAs(UnmanagedType.U1)]
    static extern bool SystemSetSuspendState([MarshalAs(UnmanagedType.U1)] bool hibernate,[MarshalAs(UnmanagedType.U1)] bool force,[MarshalAs(UnmanagedType.U1)] bool disableWake);
}
