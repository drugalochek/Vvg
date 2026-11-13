using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Button = System.Windows.Controls.Button;
using ToolTip = System.Windows.Controls.ToolTip;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using Image = System.Windows.Controls.Image;
using Orientation = System.Windows.Controls.Orientation;

namespace Island;

public partial class MainWindow
{
    delegate bool WindowEnumCallback(IntPtr hwnd, IntPtr param);
    [DllImport("user32.dll")] static extern bool EnumWindows(WindowEnumCallback callback, IntPtr param);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] static extern bool IsWindow(IntPtr hwnd);
    [DllImport("user32.dll")] static extern bool IsIconic(IntPtr hwnd);
    [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr hwnd, int command);
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
    [DllImport("user32.dll")] static extern IntPtr GetWindow(IntPtr hwnd, uint command);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr hwnd, StringBuilder text, int maxCount);
    [DllImport("user32.dll")] static extern bool PostMessage(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] static extern bool PrintWindow(IntPtr hwnd, IntPtr dc, uint flags);
    [DllImport("user32.dll")] static extern bool GetWindowDisplayAffinity(IntPtr hwnd, out uint affinity);
    [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out int value, int size);
    const uint WM_CLOSE = 0x0010;
    const int SW_MINIMIZE = 6, SW_RESTORE = 9;
    sealed record OpenWindow(IntPtr Handle, int ProcessId, string Title, string ProcessName, string? Executable);
    readonly Dictionary<IntPtr, (int Pid, long Started)> _mokuMinimized = new();
    readonly System.Threading.SemaphoreSlim _previewSlots = new(2);
    DispatcherTimer? _windowsRefreshTimer;
    bool _homeShowsWindows;
    string _windowListSignature = "";

    void HomeClipboardMode_Click(object sender, RoutedEventArgs e) => SetHomeView("Clipboard");
    void HomeWindowsMode_Click(object sender, RoutedEventArgs e) => SetHomeView("Windows");

    void SetHomeView(string view, bool save = true)
    {
        _homeShowsWindows = view == "Windows";
        if (HomeWindowsScroll != null) HomeWindowsScroll.Visibility = _homeShowsWindows ? Visibility.Visible : Visibility.Collapsed;
        if (RecentList != null) RecentList.Visibility = _homeShowsWindows ? Visibility.Collapsed : Visibility.Visible;
        if (RecentlyCopiedText != null) RecentlyCopiedText.Visibility = _homeShowsWindows ? Visibility.Collapsed : Visibility.Visible;
        if (HomeClipboardModeBtn != null) HomeClipboardModeBtn.Opacity = _homeShowsWindows ? 0.55 : 1;
        if (HomeWindowsModeBtn != null) HomeWindowsModeBtn.Opacity = _homeShowsWindows ? 1 : 0.55;
        if (save) { _s.LastHomeView = view; _s.Save(); }
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
                using var p = Process.GetProcessById((int)pid);
                string? executable = null;
                try { executable = p.MainModule?.FileName; } catch { }
                result.Add(new OpenWindow(hwnd, (int)pid, title.ToString(), p.ProcessName, executable));
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
        // Keep the existing controls alive while hovering and when nothing changed.
        string signature = string.Join("|", windows.Select(w => $"{w.Handle}:{w.Title}")) + _s.Language;
        if (signature == _windowListSignature || OpenWindowsList.Children.OfType<FrameworkElement>().Any(x => x.IsMouseOver || x.ContextMenu?.IsOpen == true)) return;
        _windowListSignature = signature;
        OpenWindowsList.Children.Clear();
        foreach (var group in windows.GroupBy(w => w.Executable ?? w.ProcessName, StringComparer.OrdinalIgnoreCase))
        {
            var items = group.ToList(); var first = items[0];
            var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
            ImageSource? icon = null;
            if (first.Executable != null) icon = AppIcon(new AppShortcut { Path = first.Executable }, false);
            stack.Children.Add(IconElement(icon, false, 25));
            var dots = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 5, 0, 0) };
            for (int i = 0; i < Math.Min(items.Count, 8); i++) dots.Children.Add(new System.Windows.Shapes.Ellipse
            { Width = 3, Height = 3, Fill = Brushes.White, Opacity = 0.7, Margin = new Thickness(1.5, 0, 1.5, 0) });
            stack.Children.Add(dots);
            var tile = new Button { Style = (Style)FindResource("TileBtn"), Width = 56, Height = 54, Margin = new Thickness(3), Content = stack };
            tile.Click += (_, _) => ActivateWindow(first.Handle);
            tile.ToolTip = CreateWindowTooltip(items);
            ToolTipService.SetInitialShowDelay(tile, 400); ToolTipService.SetShowDuration(tile, 15000);
            var menu = new ContextMenu();
            foreach (var w in items) AddMenu(menu, w.Title, () => ActivateWindow(w.Handle));
            menu.Items.Add(new Separator());
            AddMenu(menu, Tr("Новое окно", "New window"), () => OpenNewAppWindow(first.Executable!), first.Executable != null);
            AddMenu(menu, Tr("Свернуть окна приложения", "Minimize app windows"), () => { MinimizeWindows(items); BuildOpenWindows(); });
            AddMenu(menu, Tr("Развернуть свёрнутые Moku", "Restore windows minimized by Moku"), () => RestoreWindowsMinimizedByMoku(items.Select(w => w.Handle)));
            AddMenu(menu, Tr("Закрыть окна приложения", "Close app windows"), () => items.ForEach(w => PostMessage(w.Handle, WM_CLOSE, IntPtr.Zero, IntPtr.Zero)));
            tile.ContextMenu = menu;
            OpenWindowsList.Children.Add(tile);
        }
        HomeWindowsStatus.Text = windows.Count == 0 ? Tr("Открытых окон не найдено", "No open windows found")
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
            var host = new Border { Width = 240, Height = 110, CornerRadius = new CornerRadius(8), Background = new SolidColorBrush(Color.FromRgb(30, 30, 34)), Margin = new Thickness(0, 0, 0, 5) };
            host.Child = PreviewUnavailable(); panel.Children.Add(host); previews.Add((w, host));
        }
        var tip = new ToolTip { Content = panel, Background = new SolidColorBrush(Color.FromRgb(19, 19, 23)), Foreground = Brushes.White, BorderBrush = new SolidColorBrush(Color.FromRgb(57, 57, 64)), Padding = new Thickness(10) };
        tip.Opened += async (_, _) =>
        {
            foreach (var (w, host) in previews)
            {
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
        Collapse();
        if (IsIconic(hwnd)) ShowWindow(hwnd, SW_RESTORE); // Do not unmaximize already visible windows.
        SetForegroundWindow(hwnd);
    }
    static long ProcessStarted(int pid) { try { using var p = Process.GetProcessById(pid); return p.StartTime.ToUniversalTime().Ticks; } catch { return 0; } }
    void MinimizeWindows(IEnumerable<OpenWindow> windows)
    {
        foreach (var w in windows)
        {
            if (!IsWindow(w.Handle) || IsIconic(w.Handle)) continue;
            ShowWindow(w.Handle, SW_MINIMIZE);
            if (IsIconic(w.Handle)) _mokuMinimized[w.Handle] = (w.ProcessId, ProcessStarted(w.ProcessId));
        }
    }
    void MinimizeAll_Click(object sender, RoutedEventArgs e) { MinimizeWindows(GetOpenWindows()); }
    void RestoreAll_Click(object sender, RoutedEventArgs e) => RestoreWindowsMinimizedByMoku();
    void RestoreWindowsMinimizedByMoku(IEnumerable<IntPtr>? only = null)
    {
        var filter = only == null ? null : new HashSet<IntPtr>(only);
        foreach (var (hwnd, identity) in _mokuMinimized.ToArray())
        {
            if (filter != null && !filter.Contains(hwnd)) continue;
            GetWindowThreadProcessId(hwnd, out uint pid);
            if (IsWindow(hwnd) && pid == identity.Pid && ProcessStarted((int)pid) == identity.Started && IsIconic(hwnd)) ShowWindow(hwnd, SW_RESTORE);
            _mokuMinimized.Remove(hwnd);
        }
    }
}
