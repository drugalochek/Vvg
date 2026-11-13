using System.Runtime.InteropServices;
using System.Text;

namespace Island;

internal static class WindowActivation
{
    delegate bool ChildCallback(IntPtr hwnd, IntPtr param);
    [DllImport("user32.dll")] static extern bool EnumChildWindows(IntPtr parent, ChildCallback callback, IntPtr param);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr hwnd, StringBuilder name, int length);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
    [DllImport("user32.dll")] static extern bool IsWindow(IntPtr hwnd);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] static extern bool IsIconic(IntPtr hwnd);
    [DllImport("user32.dll")] static extern bool ShowWindowAsync(IntPtr hwnd, int command);
    [DllImport("user32.dll")] static extern bool BringWindowToTop(IntPtr hwnd);
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll")] static extern IntPtr GetLastActivePopup(IntPtr hwnd);
    [DllImport("kernel32.dll")] static extern IntPtr GetConsoleWindow();
    [DllImport("kernel32.dll", SetLastError = true)] static extern bool AttachConsole(uint pid);
    [DllImport("kernel32.dll", SetLastError = true)] static extern bool FreeConsole();
    static readonly object ConsoleGate = new();

    internal static bool IsMinimized(IntPtr hwnd) => IsIconic(hwnd);

    // Return true for a valid existing window even if Windows refuses foreground focus.
    // A focus restriction is not a reason to launch another copy of the application.
    internal static bool RestoreAndActivate(IntPtr hwnd)
    {
        if (!IsWindow(hwnd)) return false;
        if (IsIconic(hwnd)) ShowWindowAsync(hwnd, 9); // SW_RESTORE keeps the previous normal/maximized placement.
        var popup = GetLastActivePopup(hwnd);
        if (popup != IntPtr.Zero && popup != hwnd && IsWindowVisible(popup))
        {
            if (IsIconic(popup)) ShowWindowAsync(popup, 9);
            hwnd = popup;
        }
        BringWindowToTop(hwnd); SetForegroundWindow(hwnd);
        return true;
    }

    internal static int HostedAppProcessId(IntPtr frame, int framePid)
    {
        int result = framePid;
        EnumChildWindows(frame, (child, _) =>
        {
            var name = new StringBuilder(128); GetClassName(child, name, name.Capacity);
            if (name.ToString() != "Windows.UI.Core.CoreWindow") return true;
            GetWindowThreadProcessId(child, out uint pid);
            if (pid == 0 || pid == framePid) return true;
            result = (int)pid; return false;
        }, IntPtr.Zero);
        return result;
    }

    internal static IntPtr ConsoleWindowForProcess(int pid)
    {
        // Classic console windows belong to conhost.exe, not to powershell.exe.
        // Never detach an existing console (e.g. a developer running Moku from a terminal).
        lock (ConsoleGate)
        {
            if (GetConsoleWindow() != IntPtr.Zero || !AttachConsole((uint)pid)) return IntPtr.Zero;
            try
            {
                var hwnd = GetConsoleWindow();
                // Windows Terminal pseudo-consoles return a hidden HWND, not their visible UI.
                return hwnd != IntPtr.Zero && IsWindowVisible(hwnd) ? hwnd : IntPtr.Zero;
            }
            finally { FreeConsole(); }
        }
    }
}
