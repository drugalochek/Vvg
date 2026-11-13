using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace Island;

public partial class MainWindow
{
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumWindowsProc callback, IntPtr extra);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr hwnd, int command);
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr hwnd);
    delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr extra);
    const int SW_RESTORE = 9;

    DispatcherTimer? _appsScanTimer;

    bool TryActivateExistingApp(string executable, out bool processRunning)
    {
        processRunning = false;
        string fullPath;
        try { fullPath = Path.GetFullPath(executable); } catch { fullPath = executable; }
        var processes = Process.GetProcesses();
        try
        {
            foreach (var process in processes)
            {
                string? processPath = null;
                try { processPath = process.MainModule?.FileName; } catch { }
                if (string.IsNullOrWhiteSpace(processPath)) continue;
                try { if (!string.Equals(Path.GetFullPath(processPath), fullPath, StringComparison.OrdinalIgnoreCase)) continue; }
                catch { continue; }
                processRunning = true;
                IntPtr target = IntPtr.Zero;
                EnumWindows((hwnd, _) =>
                {
                    GetWindowThreadProcessId(hwnd, out uint pid);
                    if (pid == process.Id && IsWindowVisible(hwnd)) { target = hwnd; return false; }
                    return true;
                }, IntPtr.Zero);
                if (target != IntPtr.Zero)
                {
                    ShowWindow(target, SW_RESTORE);
                    SetForegroundWindow(target);
                    return true;
                }
            }
        }
        finally { foreach (var process in processes) process.Dispose(); }
        return false;
    }

    void StartAppsScanWatch()
    {
        if (_appsScanTimer != null) return;
        _appsScanTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
        _appsScanTimer.Tick += (_, _) =>
        {
            if (TabApps?.IsChecked == true) ScanSteam_Click(this, new RoutedEventArgs());
        };
        _appsScanTimer.Start();
    }

    void StopAppsScanWatch()
    {
        _appsScanTimer?.Stop();
        _appsScanTimer = null;
    }
}
