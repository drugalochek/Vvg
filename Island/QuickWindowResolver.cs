using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace Island;

/// <summary>Window lookup for activation, deliberately separate from preview/task-list visibility.</summary>
internal static class QuickWindowResolver
{
    delegate bool Callback(IntPtr h, IntPtr p);
    [DllImport("user32.dll")] static extern bool EnumWindows(Callback cb, IntPtr p);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] static extern bool IsIconic(IntPtr h);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] static extern int GetClassName(IntPtr h, StringBuilder s, int n);
    [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr h, int a, out int v, int n);

    [DllImport("user32.dll")] static extern IntPtr GetWindow(IntPtr h, uint command);
    [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr h, int index);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
    internal static IntPtr Find(string command) => FindMatching((name, cls) => QuickAccessPolicy.MatchesWindow(command, name, cls));

    internal static IntPtr FindMatching(Func<string,string,bool> matches)
    {
        IntPtr found = IntPtr.Zero;
        int bestScore = int.MinValue;
        EnumWindows((h, _) =>
        {
            // A minimized window keeps WS_VISIBLE normally; include iconic explicitly.
            // Do NOT exclude owned windows or require a title/preview bitmap.
            if (!IsWindowVisible(h) && !IsIconic(h)) return true;
            GetWindowThreadProcessId(h, out uint pid);
            if (pid == 0 || pid == Environment.ProcessId) return true;
            var c = new StringBuilder(256); GetClassName(h, c, c.Capacity);
            string cls = c.ToString();
            if (cls is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd") return true;
            // Shell-cloaked = another desktop; APP-cloaked minimized UWP is not excluded.
            if (DwmGetWindowAttribute(h, 14, out int cloak, sizeof(int)) == 0 && (cloak & 2) != 0 && !IsIconic(h)) return true;
            try
            {
                int appPid = cls == "ApplicationFrameWindow" ? WindowActivation.HostedAppProcessId(h, (int)pid) : (int)pid;
                using var p = Process.GetProcessById(appPid);
                if (matches(p.ProcessName, cls))
                {
                    var title=new StringBuilder(512); GetWindowText(h,title,title.Capacity);
                    int score=(IsIconic(h) ? 40 : 0) + (title.Length>0 ? 10 : 0)
                        + (GetWindow(h,4)==IntPtr.Zero ? 5 : 0)
                        - ((GetWindowLong(h,-20)&0x80)!=0 ? 20 : 0);
                    if (score>bestScore) { found=h; bestScore=score; }
                }
            }
            catch { }
            return true;
        }, IntPtr.Zero);
        return found;
    }
}
