using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using System.Windows.Media;
using System.Windows.Threading;
using Button = System.Windows.Controls.Button;
using Image = System.Windows.Controls.Image;
using ToolTip = System.Windows.Controls.ToolTip;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using Orientation = System.Windows.Controls.Orientation;

namespace Island;

public partial class MainWindow
{
    delegate bool WindowEnumCallback(IntPtr hwnd, IntPtr param);
    [DllImport("user32.dll")] static extern bool EnumWindows(WindowEnumCallback callback, IntPtr param);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] static extern bool IsWindow(IntPtr hwnd);
    [DllImport("user32.dll")] static extern bool IsIconic(IntPtr hwnd);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
    [DllImport("user32.dll")] static extern IntPtr GetWindow(IntPtr hwnd, uint command);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr hwnd, StringBuilder text, int maxCount);
    [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out int value, int size);
    [DllImport("user32.dll")] static extern bool PrintWindow(IntPtr hwnd, IntPtr dc, uint flags);
    [DllImport("user32.dll")] static extern bool GetWindowDisplayAffinity(IntPtr hwnd, out uint affinity);
    readonly System.Threading.SemaphoreSlim _previewSlots = new(2);
    sealed record OpenWindow(IntPtr Handle, int ProcessId, string Title, string ProcessName, string? Executable, string WindowClass);
    DispatcherTimer? _windowsRefreshTimer;
    bool _homeShowsWindows;
    string _windowListSignature = "";

    void SetHomeView(string view, bool save = true)
    {
        _homeShowsWindows = view == "Windows";
        if (HomeWindowsScroll != null) HomeWindowsScroll.Visibility = _homeShowsWindows ? Visibility.Visible : Visibility.Collapsed;
        if (RecentList != null) RecentList.Visibility = _homeShowsWindows ? Visibility.Collapsed : Visibility.Visible;
        if (RecentlyCopiedText != null)
        {
            RecentlyCopiedText.Visibility = Visibility.Visible;
            RecentlyCopiedText.Text = _homeShowsWindows ? Tr("ЗАПУЩЕННЫЕ ПРИЛОЖЕНИЯ", "RUNNING APPS") : Tr("НЕДАВНО СКОПИРОВАНО", "RECENTLY COPIED");
        }
        if (save) { _s.HomeStartupView = _homeShowsWindows ? "Windows" : "Clipboard"; _s.Save(); }
        UpdateHomeRefreshState();
    }
    void UpdateHomeRefreshState()
    {
        if (_homeShowsWindows && _state == IslandState.Expanded && HomePage.Visibility == Visibility.Visible)
        { BuildOpenWindows(); StartWindowsRefresh(); }
        else StopWindowsRefresh();
    }
    void StartWindowsRefresh()
    {
        if (_windowsRefreshTimer != null) return;
        _windowsRefreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _windowsRefreshTimer.Tick += (_, _) => BuildOpenWindows();
        _windowsRefreshTimer.Start();
    }
    void StopWindowsRefresh() { _windowsRefreshTimer?.Stop(); _windowsRefreshTimer = null; }
    List<OpenWindow> GetOpenWindows()
    {
        var result = new List<OpenWindow>();
        EnumWindows((hwnd, _) =>
        {
            if (!IsWindowVisible(hwnd) || GetWindow(hwnd, 4) != IntPtr.Zero || (GetWindowLong(hwnd, GWL_EXSTYLE) & WS_EX_TOOLWINDOW) != 0) return true;
            GetWindowThreadProcessId(hwnd, out uint pid);
            if (pid == Environment.ProcessId || pid == 0) return true;
            var cls = new StringBuilder(128); GetClassName(hwnd, cls, cls.Capacity);
            if (cls.ToString() is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd") return true;
            if (DwmGetWindowAttribute(hwnd, 14, out int cloaked, sizeof(int)) == 0 && cloaked != 0) return true;
            var title = new StringBuilder(1024);
            if (GetWindowText(hwnd, title, title.Capacity) <= 0 || string.IsNullOrWhiteSpace(title.ToString())) return true;
            try
            {
                int appPid = (int)pid;
                // Windows 10 Calculator and other UWP apps may be owned by ApplicationFrameHost.
                if (cls.ToString() == "ApplicationFrameWindow") appPid = WindowActivation.HostedAppProcessId(hwnd, appPid);
                using var p = Process.GetProcessById(appPid);
                string? executable = null; try { executable = p.MainModule?.FileName; } catch { }
                result.Add(new OpenWindow(hwnd, appPid, title.ToString(), p.ProcessName, executable, cls.ToString()));
            }
            catch { }
            return true;
        }, IntPtr.Zero);
        return result;
    }
    void BuildOpenWindows()
    {
        if (OpenWindowsList == null) return;
        var windows = GetOpenWindows();
        string signature = string.Join("|", windows.Select(w => $"{w.Handle}:{w.Title}")) + _s.Language;
        if (signature == _windowListSignature || OpenWindowsList.Children.OfType<FrameworkElement>().Any(x => x.IsMouseOver || x.ContextMenu?.IsOpen == true)) return;
        _windowListSignature = signature; OpenWindowsList.Children.Clear();
        foreach (var group in windows.GroupBy(w => w.Executable ?? w.ProcessName, StringComparer.OrdinalIgnoreCase))
        {
            var items = group.ToList(); var first = items[0];
            var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
            ImageSource? icon = first.Executable == null ? null : AppIcon(new AppShortcut { Path = first.Executable }, false);
            stack.Children.Add(IconElement(icon, false, 25));
            var dots = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 5, 0, 0) };
            for (int i = 0; i < Math.Min(items.Count, 8); i++) dots.Children.Add(new System.Windows.Shapes.Ellipse
            { Width = 3, Height = 3, Fill = Brushes.White, Opacity = 0.7, Margin = new Thickness(1.5, 0, 1.5, 0) });
            stack.Children.Add(dots);
            var tile = new Button { Style = (Style)FindResource("TileBtn"), Width = 56, Height = 54, Margin = new Thickness(3), Content = stack, ToolTip = CreateWindowTooltip(items) };
            ToolTipService.SetInitialShowDelay(tile, 400); ToolTipService.SetShowDuration(tile, 15000);
            tile.Click += (_, _) => ActivateWindow(first.Handle);
            if (items.Count > 1)
            {
                var menu = new ContextMenu();
                foreach (var w in items) AddMenu(menu, w.Title, () => ActivateWindow(w.Handle));
                tile.ContextMenu = menu;
            }
            OpenWindowsList.Children.Add(tile);
        }
        HomeWindowsStatus.Text = windows.Count == 0 ? Tr("Запущенных приложений с окнами нет", "No running apps with windows")
            : windows.Count + " " + Tr(Plural(windows.Count, "окно", "окна", "окон"), windows.Count == 1 ? "window" : "windows");
    }

    ToolTip CreateWindowTooltip(List<OpenWindow> windows)
    {
        var panel = new StackPanel { MaxWidth = 270 };
        var previews = new List<(OpenWindow Window, Border Host)>();
        foreach (var w in windows)
        {
            panel.Children.Add(new TextBlock { Text = w.Title, Foreground = Brushes.White, TextWrapping = TextWrapping.Wrap, FontSize = 11, Margin = new Thickness(0, 5, 0, 4) });
            if (previews.Count >= 4) continue;
            var host = new Border { Width = 240, Height = 110, CornerRadius = new CornerRadius(_s.CornerRadius), Background = new SolidColorBrush(Color.FromRgb(30, 30, 34)), Margin = new Thickness(0, 0, 0, 5) };
            host.SetResourceReference(Border.CornerRadiusProperty, "MokuPopupRadius");
            host.SetResourceReference(Border.BackgroundProperty, "MokuPopupSurface");
            host.SizeChanged += (_, _) => UpdatePreviewClip(host);
            host.Child = PreviewUnavailable(); panel.Children.Add(host); previews.Add((w, host));
        }
        var tip = new ToolTip { Content = panel, Style = (Style)FindResource("MokuWindowTooltip") };
        tip.Opened += async (_, _) =>
        {
            foreach (var (w, host) in previews)
            {
                UpdatePreviewClip(host);
                if (!tip.IsOpen) break;
                if (IsIconic(w.Handle) || !IsWindow(w.Handle) || (GetWindowDisplayAffinity(w.Handle, out uint affinity) && affinity != 0) || !_previewSlots.Wait(0)) continue;
                // A broken application must not freeze Moku or spawn unlimited capture threads.
                var task = System.Threading.Tasks.Task.Run(() =>
                {
                    try { return CaptureWindowPreview(w.Handle); }
                    finally { _previewSlots.Release(); }
                });
                if (await System.Threading.Tasks.Task.WhenAny(task, System.Threading.Tasks.Task.Delay(650)) != task) continue;
                var image = await task;
                if (image != null && tip.IsOpen) host.Child = new Image { Source = image, Stretch = Stretch.Uniform };
            }
        };
        return tip;
    }

    TextBlock PreviewUnavailable() => new() { Text = Tr("Превью недоступно", "Preview unavailable"), Foreground = new SolidColorBrush(Color.FromRgb(170, 170, 180)), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, FontSize = 11 };
    static BitmapSource? CaptureWindowPreview(IntPtr hwnd)
    {
        try
        {
            if (!GetWindowRect(hwnd, out var r)) return null;
            int width = r.R - r.L, height = r.B - r.T;
            if (width < 1 || height < 1 || width > 4096 || height > 2160) return null;
            using var bitmap = new System.Drawing.Bitmap(width, height);
            using (var graphics = System.Drawing.Graphics.FromImage(bitmap))
            {
                var dc = graphics.GetHdc();
                try { if (!PrintWindow(hwnd, dc, 2)) return null; }
                finally { graphics.ReleaseHdc(dc); }
            }
            using var small = new System.Drawing.Bitmap(bitmap, new System.Drawing.Size(240, Math.Max(1, (int)(240.0 * height / width))));
            var hbitmap = small.GetHbitmap();
            try { var image = Imaging.CreateBitmapSourceFromHBitmap(hbitmap, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions()); image.Freeze(); return image; }
            finally { DeleteObject(hbitmap); }
        }
        catch { return null; }
    }


    // Existing normal-window transparency detection is intentionally preserved.
    bool NormalWindowUnderIsland(IntPtr me)
    {
        if (!GetWindowRect(me, out var island)) return false;
        bool overlaps = false;
        EnumWindows((hwnd, _) =>
        {
            if (hwnd == me || !IsWindowVisible(hwnd) || IsIconic(hwnd) || (GetWindowLong(hwnd, GWL_EXSTYLE) & WS_EX_TOOLWINDOW) != 0) return true;
            GetWindowThreadProcessId(hwnd, out uint pid); if (pid == Environment.ProcessId) return true;
            var cls = new StringBuilder(64); GetClassName(hwnd, cls, cls.Capacity);
            if (cls.ToString() is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd") return true;
            if (DwmGetWindowAttribute(hwnd, 14, out int cloaked, sizeof(int)) == 0 && cloaked != 0) return true;
            if (!GetWindowRect(hwnd, out var r)) return true;
            overlaps = FeaturePolicies.Overlaps(r.L, r.T, r.R, r.B, island.L, island.T, island.R, island.B);
            return !overlaps;
        }, IntPtr.Zero);
        return overlaps;
    }
    void ActivateWindow(IntPtr hwnd)
    {
        if (!IsWindow(hwnd)) return;
        Collapse(); WindowActivation.RestoreAndActivate(hwnd);
    }
    bool TryActivateHomeQuickAction(string command, string launchPath)
    {
        var window = GetOpenWindows().FirstOrDefault(w => QuickAccessPolicy.MatchesWindow(command, w.ProcessName, w.WindowClass));
        if (window != null)
        {
            _pendingAppLaunches.Remove(launchPath); ActivateWindow(window.Handle); return true;
        }
        if (!QuickAccessPolicy.IsTerminal(command)) return false;
        using var current = Process.GetCurrentProcess();
        foreach (string name in new[] { "powershell", "pwsh" })
            foreach (var process in Process.GetProcessesByName(name))
                using (process)
                {
                    try
                    {
                        if (process.HasExited || process.SessionId != current.SessionId) continue;
                        var hwnd = WindowActivation.ConsoleWindowForProcess(process.Id);
                        if (hwnd == IntPtr.Zero) continue;
                        _pendingAppLaunches.Remove(launchPath); ActivateWindow(hwnd); return true;
                    }
                    catch (Exception ex) { Log.Warn("Quick-access console lookup: " + ex.Message); }
                }
        return false;
    }

    static long ProcessStarted(int pid) { try { using var p = Process.GetProcessById(pid); return p.StartTime.ToUniversalTime().Ticks; } catch { return 0; } }
}
