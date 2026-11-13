using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Button = System.Windows.Controls.Button;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using Orientation = System.Windows.Controls.Orientation;
using Panel = System.Windows.Controls.Panel;

namespace Island;

public partial class MainWindow
{
    bool _capturingHotkey, _captureHooked;
    Button? _hotkeyCaptureButton;
    TextBlock? _hotkeyCaptureStatus;
    readonly Dictionary<string, DateTime> _pendingAppLaunches = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string, string> _steamInstallPaths = new(StringComparer.OrdinalIgnoreCase);
    DispatcherTimer? _appsScanTimer;
    bool _appLayoutHooked;

    void BuildHotkeyRow(Panel panel)
    {
        Row(panel, Tr("Горячая клавиша", "Keyboard shortcut"), out var host);
        var controls = new StackPanel();
        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        _hotkeyCaptureButton = new Button { Style = (Style)FindResource("TileBtn"), Height = 29, MinWidth = 130, Padding = new Thickness(7, 0, 7, 0), Content = HotkeyLabel(_s.Hotkey) + "  ·  " + Tr("Изменить", "Change"), FontSize = 11 };
        _hotkeyCaptureButton.Click += (_, _) =>
        {
            if (_capturingHotkey) { CancelHotkeyCapture(); return; }
            _capturingHotkey = true;
            UnregisterHotKey(new WindowInteropHelper(this).Handle, HotkeyId); _hotkeyRegistered = false;
            _hotkeyCaptureButton.Content = Tr("Нажми клавишу…", "Press a key…");
            _hotkeyCaptureStatus!.Text = Tr("Сочетание или одна клавиша. Esc — отмена.", "Combination or single key. Esc cancels.");
            _hotkeyCaptureButton.Focus();
        };
        buttons.Children.Add(_hotkeyCaptureButton);
        var disable = new Button { Style = (Style)FindResource("TileBtn"), Height = 29, Content = Tr("Выкл.", "Off"), Padding = new Thickness(7, 0, 7, 0), FontSize = 11 };
        disable.Click += (_, _) => { CancelHotkeyCapture(); _s.Hotkey = "Off"; RegisterIslandHotkey(); _s.Save(); UpdateHotkeyHint(); RefreshCaptureLabel(); };
        buttons.Children.Add(disable); controls.Children.Add(buttons);
        _hotkeyCaptureStatus = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 10, Foreground = new SolidColorBrush(Color.FromRgb(175, 175, 185)), Margin = new Thickness(0, 3, 0, 0) };
        controls.Children.Add(_hotkeyCaptureStatus); host.Children.Add(controls);
        Hint(panel, Tr("По умолчанию Alt + Пробел. Можно назначить одну клавишу, например F8. Обычная буква или пробел перестанут вводиться в других программах.", "Default: Alt + Space. A single key such as F8 is supported. A plain letter or Space will no longer type in other apps."));
        if (!_captureHooked) { PreviewKeyDown += CaptureHotkey_KeyDown; _captureHooked = true; }
    }
    void RefreshCaptureLabel() { if (_hotkeyCaptureButton != null) _hotkeyCaptureButton.Content = HotkeyLabel(_s.Hotkey) + "  ·  " + Tr("Изменить", "Change"); }
    void CancelHotkeyCapture()
    {
        if (!_capturingHotkey) return;
        _capturingHotkey = false;
        RegisterIslandHotkey(); RefreshCaptureLabel();
        if (_hotkeyCaptureStatus != null) _hotkeyCaptureStatus.Text = "";
    }
    void CaptureHotkey_KeyDown(object sender, KeyEventArgs e)
    {
        if (!_capturingHotkey) return;
        e.Handled = true;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key == Key.Escape) { CancelHotkeyCapture(); return; }
        if (key is Key.LeftAlt or Key.RightAlt or Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin or Key.None or Key.System or Key.ImeProcessed or Key.DeadCharProcessed) return;
        var parts = new List<string>(); var mods = Keyboard.Modifiers;
        if ((mods & ModifierKeys.Control) != 0) parts.Add("Ctrl");
        if ((mods & ModifierKeys.Alt) != 0) parts.Add("Alt");
        if ((mods & ModifierKeys.Shift) != 0) parts.Add("Shift");
        if ((mods & ModifierKeys.Windows) != 0) parts.Add("Win");
        parts.Add(key.ToString()); string requested = string.Join("+", parts);
        _capturingHotkey = false; _s.Hotkey = requested;
        RegisterIslandHotkey(true); _s.Save(); UpdateHotkeyHint(); RefreshCaptureLabel();
        if (_hotkeyCaptureStatus != null) _hotkeyCaptureStatus.Text = requested == _s.Hotkey
            ? Tr("Сохранено: ", "Saved: ") + HotkeyLabel(_s.Hotkey)
            : Tr("Сочетание занято. Предыдущее сохранено.", "Shortcut in use. Previous binding kept.");
    }

    static string SteamId(string path) => path.StartsWith("steam://rungameid/", StringComparison.OrdinalIgnoreCase) ? path[18..].TrimEnd('/') : "";
    string ResolveLaunchTarget(string path)
    {
        if (!path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase)) return path;
        object? shell = null, shortcut = null;
        try
        {
            var type = Type.GetTypeFromProgID("WScript.Shell");
            if (type == null) return path;
            shell = Activator.CreateInstance(type);
            shortcut = ((dynamic)shell!).CreateShortcut(path);
            string target = ((dynamic)shortcut).TargetPath;
            return string.IsNullOrWhiteSpace(target) ? path : Environment.ExpandEnvironmentVariables(target);
        }
        catch { return path; }
        finally
        {
            if (shortcut != null && Marshal.IsComObject(shortcut)) Marshal.FinalReleaseComObject(shortcut);
            if (shell != null && Marshal.IsComObject(shell)) Marshal.FinalReleaseComObject(shell);
        }
    }
    bool MatchesAppTarget(string target, string? executable, string processName)
    {
        if (IsSteamLink(target))
        {
            return executable != null && _steamInstallPaths.TryGetValue(SteamId(target), out var folder)
                && executable.StartsWith(folder.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }
        string file = Path.GetFileName(target).ToLowerInvariant();
        string windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        if (windows.Length > 0 && target.StartsWith(windows + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            if (file == "notepad.exe" && processName.Equals("notepad", StringComparison.OrdinalIgnoreCase)) return true;
            if (file == "calc.exe" && (processName.Equals("CalculatorApp", StringComparison.OrdinalIgnoreCase) || processName.Equals("Calculator", StringComparison.OrdinalIgnoreCase) || processName.Equals("calc", StringComparison.OrdinalIgnoreCase))) return true;
        }
        if (executable != null) return string.Equals(target, executable, StringComparison.OrdinalIgnoreCase);
        return string.Equals(Path.GetFileNameWithoutExtension(target), processName, StringComparison.OrdinalIgnoreCase);
    }
    bool TryActivateExistingApp(string target, out bool running)
    {
        var open = GetOpenWindows().FirstOrDefault(w => MatchesAppTarget(target, w.Executable, w.ProcessName));
        if (open != null) { running = true; _pendingAppLaunches.Remove(target); ActivateWindow(open.Handle); return true; }
        running = false;
        // Explorer is also the desktop shell: that background process alone is not a folder window.
        if (string.Equals(target, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe"), StringComparison.OrdinalIgnoreCase)) return false;
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                try
                {
                    string? exe = null; try { exe = process.MainModule?.FileName; } catch { }
                    if (!MatchesAppTarget(target, exe, process.ProcessName)) continue;
                    running = true;
                }
                catch { }
            }
        }
        return false;
    }
    bool TryBeginAppLaunch(string target)
    {
        var now = DateTime.UtcNow;
        if (_pendingAppLaunches.TryGetValue(target, out var started) && FeaturePolicies.LaunchPending(now, started))
        { ReportAppAction(Tr("Приложение уже запускается…", "The app is already starting…")); return false; }
        foreach (var key in _pendingAppLaunches.Where(x => !FeaturePolicies.LaunchPending(now, x.Value)).Select(x => x.Key).ToArray()) _pendingAppLaunches.Remove(key);
        _pendingAppLaunches[target] = now; return true;
    }
    void ReportAppAction(string text)
    {
        if (_state == IslandState.Expanded)
        {
            if (AppsPage.Visibility == Visibility.Visible) AppsHint.Text = text;
            else if (_homeShowsWindows && HomeWindowsStatus != null) HomeWindowsStatus.Text = text;
            else if (RecentlyCopiedText != null) RecentlyCopiedText.Text = text;
        }
        else ShowToast("Moku", text, ColorError);
    }
    void OpenNewAppWindow(string path)
    {
        if (!TryBeginAppLaunch(ResolveLaunchTarget(path))) return;
        try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); Collapse(); }
        catch (Exception ex) { _pendingAppLaunches.Remove(ResolveLaunchTarget(path)); Log.Warn(ex.Message); ReportAppAction(Tr("Не удалось открыть новое окно.", "Could not open a new window.")); }
    }
    void AddAppActions(ContextMenu menu, AppShortcut app)
    {
        AddMenu(menu, Tr("Открыть / переключиться", "Open / switch to app"), () => LaunchApp(app.Path));
        AddMenu(menu, Tr("Новое окно", "New window"), () => OpenNewAppWindow(app.Path));
        var size = new MenuItem { Header = Tr("Размер плитки", "Tile size") };
        foreach (var value in new[] { "1x1", "2x1", "2x2" })
        {
            var item = new MenuItem { Header = value.Replace("x", "×"), IsCheckable = true, IsChecked = _s.AppTileSizes.GetValueOrDefault(app.Path, "1x1") == value };
            item.Click += (_, _) => { _s.AppTileSizes[app.Path] = value; _s.Save(); BuildApps(); };
            size.Items.Add(item);
        }
        menu.Items.Add(size); menu.Items.Add(new Separator());
    }
    void LayoutAppTiles()
    {
        if (AppsGrid == null) return;
        if (!_appLayoutHooked) { AppsGrid.SizeChanged += (_, _) => LayoutAppTiles(); _appLayoutHooked = true; }
        double available = AppsGrid.ActualWidth > 1 ? AppsGrid.ActualWidth : 600;
        int columns = Math.Max(1, (int)(available / (AppTileW + 8)));
        var tiles = AppsGrid.Children.OfType<FrameworkElement>().ToList();
        var layout = FeaturePolicies.PackTiles(tiles.Select(t => t.Tag is string path ? _s.AppTileSizes.GetValueOrDefault(path, "1x1") : "1x1"), columns);
        double height = 0;
        for (int i = 0; i < tiles.Count; i++)
        {
            var (x, y, w, h) = layout[i]; var tile = tiles[i];
            tile.Width = w * (AppTileW + 8) - 8; tile.Height = h * (AppTileH + 8) - 8;
            if (tile is Panel p && p.Children.OfType<Button>().FirstOrDefault() is Button b) { b.Width = tile.Width; b.Height = tile.Height; }
            Canvas.SetLeft(tile, x * (AppTileW + 8)); Canvas.SetTop(tile, y * (AppTileH + 8));
            height = Math.Max(height, (y + h) * (AppTileH + 8));
        }
        AppsGrid.Height = height;
    }
    void StartAppsScanWatch()
    {
        if (_appsScanTimer != null) return;
        _appsScanTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
        _appsScanTimer.Tick += (_, _) => ScanSteam_Click(this, new RoutedEventArgs());
        _appsScanTimer.Start();
    }
    void StopAppsScanWatch() { _appsScanTimer?.Stop(); _appsScanTimer = null; }
    void HideSteamGame(AppShortcut app)
    {
        var id = SteamId(app.Path); if (id.Length == 0) return;
        if (!_s.HiddenSteamGames.Contains(id)) _s.HiddenSteamGames.Add(id);
        _s.HiddenSteamNames[id] = app.Name;
        _s.Apps.RemoveAll(x => string.Equals(x.Path, app.Path, StringComparison.OrdinalIgnoreCase));
        _steamCandidates.RemoveAll(x => string.Equals(x.Path, app.Path, StringComparison.OrdinalIgnoreCase));
        _s.Save(); BuildApps(); BuildExtraSettings();
    }
    void RestoreSteamGame(string id)
    {
        _s.HiddenSteamGames.Remove(id);
        _s.Save(); ScanSteam_Click(this, new RoutedEventArgs()); BuildExtraSettings();
    }
    void BuildHiddenSteamSettings(Panel panel)
    {
        Section(panel, Tr("СКРЫТЫЕ ИГРЫ STEAM", "HIDDEN STEAM GAMES"));
        if (_s.HiddenSteamGames.Count == 0) { Hint(panel, Tr("Скрытых игр нет. ПКМ по игре → «Скрыть игру».", "No hidden games. Right-click a game → Hide game.")); return; }
        foreach (var id in _s.HiddenSteamGames.ToArray())
        {
            Row(panel, _s.HiddenSteamNames.GetValueOrDefault(id, "Steam · " + id), out var host);
            var button = new Button { Style = (Style)FindResource("TileBtn"), Content = Tr("Вернуть", "Restore"), Height = 28, FontSize = 11 };
            button.Click += (_, _) => RestoreSteamGame(id); host.Children.Add(button);
        }
        var all = new Button { Style = (Style)FindResource("TileBtn"), Content = Tr("Вернуть все", "Restore all"), Height = 29, HorizontalAlignment = HorizontalAlignment.Left, Padding = new Thickness(12, 0, 12, 0) };
        all.Click += (_, _) => { _s.HiddenSteamGames.Clear(); _s.Save(); ScanSteam_Click(this, new RoutedEventArgs()); BuildExtraSettings(); };
        panel.Children.Add(all);
    }
}
