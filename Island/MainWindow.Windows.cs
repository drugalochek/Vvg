using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace Island;

public partial class MainWindow
{
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr hwnd, StringBuilder text, int maxCount);
    [DllImport("user32.dll")] static extern bool PostMessage(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);
    const uint WM_CLOSE = 0x0010;
    const int SW_MINIMIZE = 6;
    sealed record OpenWindow(IntPtr Handle, int ProcessId, string Title, string ProcessName, string? Executable);
    DispatcherTimer? _windowsRefreshTimer;
    bool _homeShowsWindows;

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
        if (save) { _s.HomeStartupView = view; _s.Save(); }
        if (_homeShowsWindows) { BuildOpenWindows(); StartWindowsRefresh(); }
        else StopWindowsRefresh();
    }

    void StartWindowsRefresh()
    {
        if (_windowsRefreshTimer != null) return;
        _windowsRefreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _windowsRefreshTimer.Tick += (_, _) => { if (_homeShowsWindows && HomePage.Visibility == Visibility.Visible) BuildOpenWindows(); };
        _windowsRefreshTimer.Start();
    }

    void StopWindowsRefresh() { _windowsRefreshTimer?.Stop(); _windowsRefreshTimer = null; }

    List<OpenWindow> GetOpenWindows()
    {
        var result = new List<OpenWindow>();
        int ownPid = Environment.ProcessId;
        EnumWindows((hwnd, _) =>
        {
            if (!IsWindowVisible(hwnd)) return true;
            GetWindowThreadProcessId(hwnd, out uint pid);
            if (pid == ownPid || pid == 0) return true;
            var title = new StringBuilder(512);
            int length = GetWindowText(hwnd, title, title.Capacity);
            if (length <= 0 || string.IsNullOrWhiteSpace(title.ToString())) return true;
            string processName = "Application";
            string? executable = null;
            try
            {
                using var p = Process.GetProcessById((int)pid);
                processName = p.ProcessName;
                try { executable = p.MainModule?.FileName; } catch { }
            }
            catch { return true; }
            result.Add(new OpenWindow(hwnd, (int)pid, title.ToString(), processName, executable));
            return true;
        }, IntPtr.Zero);
        return result;
    }

    void BuildOpenWindows()
    {
        if (OpenWindowsList == null) return;
        OpenWindowsList.Children.Clear();
        var windows = GetOpenWindows();
        var groups = windows.GroupBy(w => w.Executable ?? w.ProcessName, StringComparer.OrdinalIgnoreCase).ToList();
        foreach (var group in groups)
        {
            var items = group.ToList();
            var first = items[0];
            var tile = new Button { Style = (Style)FindResource("TileBtn"), Height = 54, Margin = new Thickness(3), HorizontalContentAlignment = HorizontalAlignment.Stretch,
                ToolTip = string.Join(Environment.NewLine, items.Select(w => w.Title)) };
            var row = new Grid { Margin = new Thickness(10, 0, 10, 0) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var glyph = new TextBlock { Text = "\uE8A5", FontFamily = (System.Windows.Media.FontFamily)FindResource("Icons"), FontSize = 20, Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
            var labels = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            labels.Children.Add(new TextBlock { Text = first.ProcessName, Foreground = Brushes.White, FontSize = 12, FontWeight = FontWeights.SemiBold });
            labels.Children.Add(new TextBlock { Text = items.Count == 1 ? first.Title : items.Count + " windows", Foreground = new SolidColorBrush(Color.FromArgb(0x99, 255, 255, 255)), FontSize = 10, TextTrimming = TextTrimming.CharacterEllipsis });
            var count = new Border { Width = 20, Height = 20, CornerRadius = new CornerRadius(10), Background = new SolidColorBrush(Color.FromArgb(0x35, 255, 255, 255)), VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock { Text = items.Count.ToString(), Foreground = Brushes.White, FontSize = 10, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } };
            Grid.SetColumn(labels, 1); Grid.SetColumn(count, 2);
            row.Children.Add(glyph); row.Children.Add(labels); row.Children.Add(count);
            tile.Content = row;
            tile.Click += (_, _) => ActivateWindow(first.Handle);
            var menu = new ContextMenu();
            foreach (var window in items)
            {
                var select = new MenuItem { Header = window.Title };
                select.Click += (_, _) => ActivateWindow(window.Handle);
                menu.Items.Add(select);
            }
            menu.Items.Add(new Separator());
            var openNew = new MenuItem { Header = Tr("Открыть новое окно", "Open new window"), IsEnabled = !string.IsNullOrWhiteSpace(first.Executable) };
            openNew.Click += (_, _) => { try { Process.Start(new ProcessStartInfo(first.Executable!) { UseShellExecute = true }); } catch (Exception ex) { Log.Warn("Open new window: " + ex.Message); } };
            menu.Items.Add(openNew);
            var minimize = new MenuItem { Header = Tr("Свернуть окна приложения", "Minimize app windows") };
            minimize.Click += (_, _) => items.ForEach(w => ShowWindow(w.Handle, SW_MINIMIZE));
            menu.Items.Add(minimize);
            var close = new MenuItem { Header = Tr("Закрыть окна приложения", "Close app windows") };
            close.Click += (_, _) => items.ForEach(w => PostMessage(w.Handle, WM_CLOSE, IntPtr.Zero, IntPtr.Zero));
            menu.Items.Add(close);
            tile.ContextMenu = menu;
            OpenWindowsList.Children.Add(tile);
        }
        if (groups.Count == 0) OpenWindowsList.Children.Add(new TextBlock { Text = Tr("Открытых окон не найдено", "No open windows found"), Foreground = new SolidColorBrush(Color.FromArgb(0x88, 255, 255, 255)), FontSize = 12, Margin = new Thickness(8, 12, 8, 8) });
    }

    void ActivateWindow(IntPtr hwnd)
    {
        ShowWindow(hwnd, SW_RESTORE);
        SetForegroundWindow(hwnd);
    }
}