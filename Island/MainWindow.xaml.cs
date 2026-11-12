using HorizontalAlignment = System.Windows.HorizontalAlignment;
using System.IO;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using Point = System.Windows.Point;
using Color = System.Windows.Media.Color;
using Application = System.Windows.Application;
using Button = System.Windows.Controls.Button;
using Brushes = System.Windows.Media.Brushes;
using Clipboard = System.Windows.Clipboard;
using ColorConverter = System.Windows.Media.ColorConverter;
using Cursors = System.Windows.Input.Cursors;
using FontFamily = System.Windows.Media.FontFamily;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using RadioButton = System.Windows.Controls.RadioButton;
using Size = System.Windows.Size;
using TextDataFormat = System.Windows.TextDataFormat;

namespace Island;

public class ClipItem
{
    public string Text { get; }
    public string Time { get; }
    public string Preview { get; }

    public ClipItem(string text)
    {
        Text = text;
        Time = DateTime.Now.ToString("HH:mm");
        var head = text.Length > 90 ? text[..90] : text;
        Preview = head.Replace('\r', ' ').Replace('\n', ' ').Trim() + (text.Length > 90 ? "…" : "");
    }
}

public partial class MainWindow : Window
{
    enum IslandState { Collapsed, Toast, Expanded }
    enum EyeMood { Neutral, Happy, Excited, Calm, Sleepy, Focused, Surprised }
    EyeMood _eyeMood = EyeMood.Neutral;
    DateTime _moodUntil = DateTime.MinValue;

    // Эмоции меняют только форму и движение двух исходных белых овалов. Никаких зрачков/новых деталей.
    void SetEyeMood(EyeMood mood, double seconds = 2.0)
    {
        if (_welcomeRunning) return;
        _eyeMood = mood;
        _moodUntil = seconds > 0 ? DateTime.Now.AddSeconds(seconds) : DateTime.MaxValue;
        EnsureFx();
    }

    // ---- Win32 ----
    const int WM_CLIPBOARDUPDATE = 0x031D, WM_HOTKEY = 0x0312, HotkeyId = 1;
    const uint MOD_ALT = 0x0001, VK_SPACE = 0x20;
    const int GWL_EXSTYLE = -20, WS_EX_TOOLWINDOW = 0x80;

    [DllImport("user32.dll", SetLastError = true)] static extern bool AddClipboardFormatListener(IntPtr h);
    [DllImport("user32.dll")] static extern bool RemoveClipboardFormatListener(IntPtr h);
    [DllImport("user32.dll", SetLastError = true)] static extern bool RegisterHotKey(IntPtr h, int id, uint mods, uint vk);
    [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr h, int id);
    [DllImport("user32.dll")] static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
    [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr h, int index);
    [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr h, int index, int value);
    [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr h);
    [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr h, IntPtr dc);
    [DllImport("user32.dll")] static extern int GetSystemMetrics(int index);
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
    const uint SWP_NOZORDER = 0x0004, SWP_NOACTIVATE = 0x0010;
    [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleBitmap(IntPtr dc, int w, int h);
    [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
    [DllImport("gdi32.dll")] static extern bool BitBlt(IntPtr dst, int x, int y, int w, int h, IntPtr src, int sx, int sy, uint rop);
    [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr obj);
    [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr dc);

    const byte VK_VOLUME_MUTE = 0xAD, VK_VOLUME_DOWN = 0xAE, VK_VOLUME_UP = 0xAF;
    const byte VK_MEDIA_NEXT = 0xB0, VK_MEDIA_PREV = 0xB1, VK_MEDIA_PLAY = 0xB3;

    // ---- Ограничения памяти ----
    const int MaxItems = 50;
    const int MaxItemChars = 200_000;
    const int MaxTotalChars = 1_500_000;

    // ---- Размеры острова ----
    const double CollapsedH = 36, ToastW = 340, ToastH = 56, FullW = 652, FullH = 348;
    const double IdleW = 204, PlayW = 312, GlowPad = 12;

    static readonly string[] AccentColors =
        { "#FF7C5CFF", "#FF30D158", "#FF0A84FF", "#FFFF453A", "#FFFF9F0A", "#FFFF375F" };
    const string ColorOk = "#FF30D158", ColorError = "#FFFF453A";

    static readonly SolidColorBrush Neutral = Freeze(new SolidColorBrush(Color.FromArgb(0x26, 255, 255, 255)));

    readonly Settings _s = Settings.Load();
    IslandState _state = IslandState.Collapsed;
    HwndSource? _source;
    bool _ready;
    bool _allowClose;
    string? _expectedClipboardText;
    System.Windows.Forms.NotifyIcon? _trayIcon;
    System.Drawing.Icon? _ownedTrayIcon;
    System.Windows.Forms.ContextMenuStrip? _trayMenu;
    Action? _refreshTrayLanguage;
    int _animVer, _fullFadeVer, _totalChars;
    NowPlaying? _now;
    string? _lastTrack;
    SolidColorBrush _accent = Brushes.White;

    readonly MediaWatcher _media = new();
    readonly DispatcherTimer _clock = new();
    readonly DispatcherTimer _toastTimer = new() { Interval = TimeSpan.FromSeconds(2.5) };
    readonly DispatcherTimer _clipboardTimer = new() { Interval = TimeSpan.FromMilliseconds(110) };

    public ObservableCollection<ClipItem> History { get; } = new();
    public ObservableCollection<ClipItem> Recent { get; } = new();
    public ICollectionView HistoryView { get; }

    static SolidColorBrush Freeze(SolidColorBrush b) { b.Freeze(); return b; }

    public MainWindow()
    {
        string startupStage = "InitializeComponent";
        try
        {
        InitializeComponent();
        Log.Info("MainWindow: XAML loaded");
        startupStage = "window setup";
        Pill.VerticalAlignment = VerticalAlignment.Top;
        Pill.RenderTransformOrigin = new Point(0.5, 0.5);
        Pill.RenderTransform = _jellyTransform;

        startupStage = "history and data context";
        HistoryView = CollectionViewSource.GetDefaultView(History);
        HistoryView.Filter = o =>
            string.IsNullOrWhiteSpace(SearchBox?.Text) ||
            ((ClipItem)o).Text.Contains(SearchBox.Text, StringComparison.OrdinalIgnoreCase);
        DataContext = this;

        startupStage = "building tiles";
        BuildTiles();
        startupStage = "building accent swatches";
        BuildAccentSwatches();
        // Language selector is now the native WPF English/Russian segmented control.
        // ApplyLanguage() synchronizes its selected state from saved settings.
        startupStage = "applying language";
        ApplyLanguage();

        // применить сохранённые настройки
        startupStage = "applying saved settings";
        (_s.Align switch { "Left" => AlignLeft, "Right" => AlignRight, _ => AlignCenter }).IsChecked = true;
        OffsetText.Text = _s.OffsetY + " px";
        ScaleText.Text = _s.Scale + "%";
        UpdateAutoButton();
        ApplyScale();
        ApplyAccent();

        startupStage = "initializing timers";
        _clock.Tick += (_, _) => { UpdateMini(); ScheduleClock(); };
        UpdateMini();
        ScheduleClock();

        _toastTimer.Tick += (_, _) =>
        {
            _toastTimer.Stop();
            if (_state == IslandState.Toast) Collapse();
        };
        _clipboardTimer.Tick += async (_, _) =>
        {
            _clipboardTimer.Stop();
            await ReadClipboardAsync();
        };

        startupStage = "starting media watcher";
        _media.Changed += OnMediaChanged;
        _ = _media.StartAsync();

        startupStage = "initializing visual effects and weather";
        InitLiquid();

        startupStage = "registering window events";
        SourceInitialized += OnSourceInitialized;
        Loaded += (_, _) =>
        {
            InitializeTray();
            StartWelcomeIntro();
        };
        Closing += OnWindowClosing;
        Closed += OnClosed;
        // Start as a small round island; the welcome animation unfolds it after Loaded.
        _boxW = 48;
        _boxH = 48;
        Pill.Width = 48;
        Pill.Height = 48;
        Pill.CornerRadius = new CornerRadius(24);
        Grid.SetColumn(Eyes, 0);
        Grid.SetColumnSpan(Eyes, 3);
        Eyes.HorizontalAlignment = HorizontalAlignment.Center;
        Eyes.Margin = new Thickness(0);
        TimeText.Visibility = Visibility.Collapsed;
        WeatherHost.Visibility = Visibility.Collapsed;
        TempText.Visibility = Visibility.Collapsed;
        ApplyPosition();
        _ready = true;
        Log.Info("MainWindow: initialization complete");
        }
        catch (Exception ex)
        {
            Log.Error($"MainWindow constructor failed at stage: {startupStage}", ex);
            throw;
        }
    }

    // ================= Системное =================
    void OnSourceInitialized(object? sender, EventArgs e)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        SetWindowLong(hwnd, GWL_EXSTYLE, GetWindowLong(hwnd, GWL_EXSTYLE) | WS_EX_TOOLWINDOW);
        _source = HwndSource.FromHwnd(hwnd);
        _source.AddHook(WndProc);
        if (!AddClipboardFormatListener(hwnd))
            Log.Error("AddClipboardFormatListener не удался, код " + Marshal.GetLastWin32Error());
        if (!RegisterHotKey(hwnd, HotkeyId, MOD_ALT, VK_SPACE))
            Log.Warn("Alt+Space занят другой программой, горячая клавиша недоступна (код " + Marshal.GetLastWin32Error() + ")");
        ApplyPosition();
        InitializeTray();
        _tbCreated = RegisterWindowMessage("TaskbarCreated");
        StartGameWatch();
    }

    // ================= Трей: пересоздание после перезапуска проводника =================
    uint _tbCreated;
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern uint RegisterWindowMessage(string s);
    void RecreateTray()
    {
        try
        {
            if (_trayIcon != null) { _trayIcon.Visible = false; _trayIcon.Dispose(); _trayIcon = null; }
            _trayMenu?.Dispose(); _trayMenu = null;
            _ownedTrayIcon?.Dispose(); _ownedTrayIcon = null;
            InitializeTray();
        }
        catch (Exception ex) { Log.Error("RecreateTray", ex); }
    }

    // ================= В играх: полупрозрачный и не ловит клики =================
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out GameRect r);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr h, System.Text.StringBuilder sb, int max);
    [StructLayout(LayoutKind.Sequential)] struct GameRect { public int L, T, R, B; }
    System.Windows.Threading.DispatcherTimer? _gameTimer;
    bool _gameMode; int _topTick;
    void StartGameWatch()
    {
        _gameTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _gameTimer.Tick += (_, _) => CheckGameMode();
        _gameTimer.Start();
    }
    void CheckGameMode()
    {
        try
        {
            var me = new WindowInteropHelper(this).Handle;
            var fg = GetForegroundWindow();
            bool full = false;
            if (fg != IntPtr.Zero && fg != me && GetWindowRect(fg, out var r))
            {
                var cls = new System.Text.StringBuilder(64); GetClassName(fg, cls, 64);
                string cn = cls.ToString();
                bool shell = cn is "WorkerW" or "Progman" or "Shell_TrayWnd";
                bool noCaption = (GetWindowLong(fg, -16) & 0x00C00000) == 0;
                full = !shell && noCaption && r.L <= 0 && r.T <= 0 && r.R >= GetSystemMetrics(0) && r.B >= GetSystemMetrics(1);
            }
            if (full != _gameMode)
            {
                _gameMode = full;
                int ex = GetWindowLong(me, GWL_EXSTYLE);
                SetWindowLong(me, GWL_EXSTYLE, full ? ex | 0x20 | 0x08000000 : ex & ~0x20 & ~0x08000000);
                Opacity = full ? 0.35 : 1.0;
                if (full && _state == IslandState.Expanded) Collapse();
            }
            if (IsVisible && ++_topTick % 5 == 0)
                SetWindowPos(me, (IntPtr)(-1), 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010);
        }
        catch (Exception ex) { Log.Error("CheckGameMode", ex); }
    }

    void OnClosed(object? sender, EventArgs e)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        RemoveClipboardFormatListener(hwnd);
        UnregisterHotKey(hwnd, HotkeyId);
        _source?.RemoveHook(WndProc);
        _weather.Dispose();
        _media.Dispose();
        if (_fxAttached) CompositionTarget.Rendering -= FxRendering;
        _gameTimer?.Stop();
        _clipboardTimer.Stop();
        _toastTimer.Stop();
        _clock.Stop();
        _media.Changed -= OnMediaChanged;
        if (_trayIcon != null) { _trayIcon.Visible = false; _trayIcon.Dispose(); _trayIcon = null; }
        _ownedTrayIcon?.Dispose();
        _ownedTrayIcon = null;
        _trayMenu?.Dispose();
    }

    IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_CLIPBOARDUPDATE) OnClipboardChanged();
        else if (_tbCreated != 0 && (uint)msg == _tbCreated) RecreateTray();
        else if (msg == WM_HOTKEY && wParam.ToInt32() == HotkeyId)
        {
            if (!IsVisible) ShowFromTray();
            else if (_state == IslandState.Expanded) Collapse();
            else Expand();
            handled = true;
        }
        return IntPtr.Zero;
    }

    // ================= Настройки: положение, размер, цвет =================
    // Окно НЕ меняет размер на каждом кадре анимации: оно один раз растёт до размера «с запасом»,
    // а Pill анимируется внутри него. После окончания анимации окно сжимается до целевого размера.
    double _boxW = IdleW, _boxH = CollapsedH;

    void ApplyPosition() => SetWindowBox(_boxW, _boxH);

    void SetWindowBox(double pillW, double pillH)
    {
        double k = _s.Scale / 100.0;
        // запас на «желейное» растяжение и пружинный перелёт + поля Pill (12 по бокам, 14 снизу)
        double w = Math.Ceiling(pillW * k * 1.12) + 24, h = Math.Ceiling(pillH * k * 1.1) + 18;
        double sw = SystemParameters.PrimaryScreenWidth;
        double left = _s.Align switch
        {
            "Left" => 16 - GlowPad,
            "Right" => sw - w - 16 + GlowPad,
            _ => (sw - w) / 2
        };
        Pill.HorizontalAlignment = _s.Align switch
        {
            "Left" => HorizontalAlignment.Left,
            "Right" => HorizontalAlignment.Right,
            _ => HorizontalAlignment.Center
        };

        var hwnd = new WindowInteropHelper(this).Handle;
        var src = hwnd == IntPtr.Zero ? null : PresentationSource.FromVisual(this);
        if (src?.CompositionTarget == null)
        {
            Width = w; Height = h; Left = left; Top = _s.OffsetY;   // окно ещё не создано
            return;
        }
        var m = src.CompositionTarget.TransformToDevice;
        // Размер и позиция одним вызовом — без промежуточного кадра со сдвинутым островом.
        SetWindowPos(hwnd, IntPtr.Zero,
            (int)Math.Round(left * m.M11), (int)Math.Round(_s.OffsetY * m.M22),
            (int)Math.Round(w * m.M11), (int)Math.Round(h * m.M22),
            SWP_NOZORDER | SWP_NOACTIVATE);
    }

    void ApplyScale()
    {
        double k = _s.Scale / 100.0;
        Pill.LayoutTransform = new ScaleTransform(k, k);
        if (_ready) ApplyPosition();
    }

    void ApplyAccent()
    {
        var c = (Color)ColorConverter.ConvertFromString(_s.Accent);
        _accent = Freeze(new SolidColorBrush(c));
        NoteIcon.Foreground = _accent;
        _accentColor = c;
        Wave.Stroke = _accent;
        Wave.Effect = new DropShadowEffect { Color = c, BlurRadius = 10, ShadowDepth = 0, Opacity = 0.9 };
        UpdatePlayButton();
        foreach (Border b in AccentPanel.Children)
            b.BorderBrush = (string)b.Tag == _s.Accent ? Brushes.White : Brushes.Transparent;
    }

    void UpdatePlayButton()
    {
        bool p = _now?.Playing == true;
        PlayBtn.Background = p ? _accent : Neutral;
        // мягкое свечение акцентом, пока играет музыка
        PlayBtn.Effect = p
            ? new DropShadowEffect { Color = ((SolidColorBrush)_accent).Color, BlurRadius = 18, ShadowDepth = 0, Opacity = 0.7 }
            : null;
    }

    void BuildAccentSwatches()
    {
        foreach (var hex in AccentColors)
        {
            var dot = new Border
            {
                Width = 24, Height = 24, CornerRadius = new CornerRadius(12),
                Margin = new Thickness(0, 0, 8, 0), Cursor = Cursors.Hand,
                Background = Freeze(new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex))),
                BorderThickness = new Thickness(2), BorderBrush = Brushes.Transparent, Tag = hex
            };
            var h = hex;
            dot.MouseLeftButtonUp += (_, _) => { _s.Accent = h; _s.Save(); ApplyAccent(); };
            AccentPanel.Children.Add(dot);
        }
    }

    void Align_Checked(object sender, RoutedEventArgs e)
    {
        if (!_ready) return;
        _s.Align = (string)((RadioButton)sender).Tag;
        ApplyPosition();
        _s.Save();
    }

    void SetOffset(int v)
    {
        _s.OffsetY = Math.Clamp(v, 0, 200);
        OffsetText.Text = _s.OffsetY + " px";
        ApplyPosition();
        _s.Save();
    }

    void SetScale(int v)
    {
        _s.Scale = Math.Clamp(v, 70, 150);
        ScaleText.Text = _s.Scale + "%";
        ApplyScale();
        _s.Save();
    }

    void Offset_Minus(object s, RoutedEventArgs e) => SetOffset(_s.OffsetY - 4);
    void Offset_Plus(object s, RoutedEventArgs e) => SetOffset(_s.OffsetY + 4);
    void Scale_Minus(object s, RoutedEventArgs e) => SetScale(_s.Scale - 10);
    void Scale_Plus(object s, RoutedEventArgs e) => SetScale(_s.Scale + 10);

    void OpenSettings_Click(object sender, RoutedEventArgs e)
    {
        Expand();
        TabSet.IsChecked = true;
    }

    // ================= Системный трей =================
    void InitializeTray()
    {
        if (_trayIcon != null) return;
        try
        {
            // Extract the icon embedded in Moku.exe. This also works for single-file installs,
            // where loose content files may not be present beside the executable.
            var exePath = Environment.ProcessPath;
            System.Drawing.Icon? icon = null;
            if (!string.IsNullOrWhiteSpace(exePath) && File.Exists(exePath))
                icon = System.Drawing.Icon.ExtractAssociatedIcon(exePath);
            if (icon == null)
            {
                var iconPath = Path.Combine(AppContext.BaseDirectory, "Moku.ico");
                if (File.Exists(iconPath)) icon = new System.Drawing.Icon(iconPath);
            }
            icon ??= (System.Drawing.Icon)System.Drawing.SystemIcons.Application.Clone();
            _ownedTrayIcon = icon;
            _trayMenu = new System.Windows.Forms.ContextMenuStrip();
            var openItem = new System.Windows.Forms.ToolStripMenuItem();
            openItem.Click += (_, _) => ShowFromTray();
            var hideItem = new System.Windows.Forms.ToolStripMenuItem();
            hideItem.Click += (_, _) => Hide();
            var languageMenu = new System.Windows.Forms.ToolStripMenuItem();
            var englishItem = new System.Windows.Forms.ToolStripMenuItem("English") { Checked = _s.Language == "en", CheckOnClick = false };
            var russianItem = new System.Windows.Forms.ToolStripMenuItem("Русский") { Checked = _s.Language == "ru", CheckOnClick = false };
            englishItem.Click += (_, _) => SetLanguage("en");
            russianItem.Click += (_, _) => SetLanguage("ru");
            languageMenu.DropDownItems.Add(englishItem);
            languageMenu.DropDownItems.Add(russianItem);
            var exitItem = new System.Windows.Forms.ToolStripMenuItem();
            exitItem.Click += (_, _) => ExitApplication();
            _trayMenu.Items.AddRange(new System.Windows.Forms.ToolStripItem[] { openItem, hideItem, languageMenu, new System.Windows.Forms.ToolStripSeparator(), exitItem });
            void RefreshTrayLanguage()
            {
                openItem.Text = _s.Language == "ru" ? "Открыть Moku" : "Open Moku";
                hideItem.Text = _s.Language == "ru" ? "Скрыть" : "Hide";
                languageMenu.Text = _s.Language == "ru" ? "Язык" : "Language";
                exitItem.Text = _s.Language == "ru" ? "Выход" : "Exit";
                englishItem.Checked = _s.Language == "en";
                russianItem.Checked = _s.Language == "ru";
            }
            _refreshTrayLanguage = RefreshTrayLanguage;
            RefreshTrayLanguage();
            _trayIcon = new System.Windows.Forms.NotifyIcon
            {
                Icon = icon,
                Text = "Moku",
                ContextMenuStrip = _trayMenu,
                Visible = true
            };
            _trayIcon.DoubleClick += (_, _) => ShowFromTray();
        }
        catch (Exception ex)
        {
            Log.Error("Could not create the system tray icon", ex);
        }
    }

    void OnWindowClosing(object? sender, CancelEventArgs e)
    {
        // Closing the floating window should not terminate the background app.
        if (_allowClose) return;
        e.Cancel = true;
        Hide();
    }

    void ExitApplication()
    {
        _allowClose = true;
        Application.Current.Shutdown();
    }

    internal void ShowFromTray()
    {
        Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        ApplyPosition();
        Topmost = false; Topmost = true;
        Activate();
        Expand();
    }

    // ================= Автозапуск =================
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    static bool IsAutostart()
    {
        try { using var k = Registry.CurrentUser.OpenSubKey(RunKey); return k?.GetValue("Moku") != null; }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            Log.Warn("Автозапуск: реестр недоступен: " + ex.Message);
            return false;
        }
    }

    void UpdateAutoButton()
    {
        // Language events can arrive before the XAML tree has finished loading.
        if (AutoBtn == null) return;

        AutoBtn.Content = _s.Language == "ru"
            ? (IsAutostart() ? "Запуск при старте: Вкл" : "Запуск при старте: Выкл")
            : (IsAutostart() ? "Launch at startup: On" : "Launch at startup: Off");
    }

    void Language_Checked(object sender, RoutedEventArgs e)
    {
        // XAML raises Checked while InitializeComponent is still constructing the window.
        // Ignore those initialization events; the saved language is applied after loading.
        if (!_ready) return;

        if (sender == LanguageEnglish) SetLanguage("en");
        else if (sender == LanguageRussian) SetLanguage("ru");
    }

    void SetLanguage(string language)
    {
        if (language is not ("en" or "ru")) return;
        _s.Language = language;
        _s.Save();
        ApplyLanguage();
        _refreshTrayLanguage?.Invoke();
    }

    void ApplyLanguage()
    {
        bool ru = _s.Language == "ru";
        if (LanguageEnglish != null) LanguageEnglish.IsChecked = _s.Language == "en";
        if (LanguageRussian != null) LanguageRussian.IsChecked = _s.Language == "ru";
        if (LanguageLabel != null) LanguageLabel.Text = ru ? "Язык интерфейса" : "Language";
        if (TabHome != null) TabHome.Content = ru ? "Главная" : "Home";
        if (TabClip != null) TabClip.Content = ru ? "Буфер" : "Clipboard";
        if (TabSet != null) TabSet.Content = ru ? "Настройки" : "Settings";
        if (MenuSettings != null) MenuSettings.Header = ru ? "Настройки" : "Settings";
        if (MenuExit != null) MenuExit.Header = ru ? "Выход" : "Exit";
        if (NowTitle != null) NowTitle.Text = ru ? "Ничего не воспроизводится" : "Nothing playing";
        if (RecentlyCopiedText != null) RecentlyCopiedText.Text = ru ? "НЕДАВНО СКОПИРОВАНО" : "RECENTLY COPIED";
        if (ClearHistoryButton != null) ClearHistoryButton.ToolTip = ru ? "Очистить историю" : "Clear history";
        if (PositionLabel != null) PositionLabel.Text = ru ? "Положение" : "Position";
        if (TopOffsetLabel != null) TopOffsetLabel.Text = ru ? "Отступ сверху" : "Top offset";
        if (IslandSizeLabel != null) IslandSizeLabel.Text = ru ? "Размер острова" : "Island size";
        if (AccentLabel != null) AccentLabel.Text = ru ? "Цвет музыки и событий" : "Music and event color";
        if (AccentDescription != null) AccentDescription.Text = ru ? "Остров остаётся чёрным — цвет появляется только при воспроизведении музыки или событии." : "The island stays black — color appears only when music is playing or an event occurs.";
        if (SupportBtn != null) SupportBtn.Content = ru ? "Поддержать автора на Boosty" : "Support the author on Boosty";
        if (AlignLeft != null) AlignLeft.Content = ru ? "Слева" : "Left";
        if (AlignCenter != null) AlignCenter.Content = ru ? "По центру" : "Center";
        if (AlignRight != null) AlignRight.Content = ru ? "Справа" : "Right";
        UpdateAutoButton();
        if (TileGrid != null)
        {
            string[] labels = ru ? new[] { "Область", "Проводник", "Блокнот", "Калькулятор", "Терминал", "Диспетчер задач" }
                                 : new[] { "Region", "File Explorer", "Notepad", "Calculator", "Terminal", "Task Manager" };
            for (int i = 0; i < Math.Min(TileGrid.Children.Count, labels.Length); i++)
                if (TileGrid.Children[i] is Button b && b.Content is StackPanel sp && sp.Children.Count > 1 && sp.Children[1] is TextBlock tb) tb.Text = labels[i];
        }
    }

    void SupportAuthor_Click(object sender, RoutedEventArgs e)
    {
        const string url = "https://boosty.to/dollars_ssa/bundle/6ee74471-f9b8-489e-835d-1f9e5d435ee1?share=bundle_link";
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (Exception ex)
        {
            Log.Error("Не удалось открыть страницу поддержки", ex);
            ShowToast("Boosty", _s.Language == "ru" ? "Не удалось открыть страницу поддержки" : "Could not open the support page", ColorError);
        }
    }

    void AutoBtn_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
            if (IsAutostart()) k?.DeleteValue("Moku", throwOnMissingValue: false);
            else k?.SetValue("Moku", $"\"{Environment.ProcessPath}\"");
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            Log.Error("Не удалось изменить автозапуск", ex);
            ShowToast("Launch at startup", "Could not change the setting", ColorError);
        }
        UpdateAutoButton();
    }

    // ================= Буфер обмена =================
    void OnClipboardChanged()
    {
        // Clipboard может быть временно занят другой программой. Читаем с небольшой задержкой.
        _clipboardTimer.Stop();
        _clipboardTimer.Start();
    }

    bool _clipReading, _clipRereadRequested;

    /// <summary>Выполняет делегат в отдельном STA-потоке: чтение буфера (в т.ч. «отложенного» от Excel и т.п.)
    /// не блокирует UI-поток и не вызывает микрофризов.</summary>
    static Task<T> RunSta<T>(Func<T> f)
    {
        var tcs = new TaskCompletionSource<T>();
        var t = new Thread(() =>
        {
            try { tcs.SetResult(f()); }
            catch (Exception ex) { tcs.SetException(ex); }
        }) { IsBackground = true, Name = "Moku.Clipboard" };
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        return tcs.Task;
    }

    async Task<string?> ReadClipboardTextCoreAsync()
    {
        for (int attempt = 0; attempt < 6; attempt++)
        {
            try
            {
                return await RunSta<string?>(() =>
                    Clipboard.ContainsText(TextDataFormat.UnicodeText)
                        ? Clipboard.GetText(TextDataFormat.UnicodeText)
                        : null);   // изображения/файлы и пустой буфер — не ошибка, просто нет текста
            }
            catch (Exception ex) when (ex is ExternalException or InvalidOperationException)
            {
                if (attempt == 5) { Log.Warn("Буфер обмена занят, чтение пропущено: " + ex.Message); return null; }
                await Task.Delay(70 + attempt * 35);
            }
        }
        return null;
    }

    async Task ReadClipboardAsync()
    {
        // Одновременно выполняется только одно чтение; новые уведомления во время чтения не теряются.
        if (_clipReading) { _clipRereadRequested = true; return; }
        _clipReading = true;
        try
        {
            do
            {
                _clipRereadRequested = false;
                var text = await ReadClipboardTextCoreAsync();
                if (text != null) AddToHistory(text);
            } while (_clipRereadRequested);
        }
        catch (Exception ex) { Log.Error("Ошибка чтения буфера обмена", ex); }
        finally { _clipReading = false; }
    }

    void AddToHistory(string text)
    {
        if (_expectedClipboardText != null)
        {
            bool isOurs = string.Equals(text, _expectedClipboardText, StringComparison.Ordinal);
            _expectedClipboardText = null;
            if (isOurs) return;
        }
        if (string.IsNullOrWhiteSpace(text)) return;
        if (text.Length > MaxItemChars) { Log.Info($"Текст в буфере слишком большой ({text.Length} симв.), в историю не добавлен"); return; }
        if (History.Count > 0 && History[0].Text == text) return;

        for (int i = History.Count - 1; i >= 0; i--)
            if (History[i].Text == text) { _totalChars -= History[i].Text.Length; History.RemoveAt(i); }

        History.Insert(0, new ClipItem(text));
        _totalChars += text.Length;
        while (History.Count > 1 && (History.Count > MaxItems || _totalChars > MaxTotalChars))
        {
            _totalChars -= History[^1].Text.Length;
            History.RemoveAt(History.Count - 1);
        }
        SyncRecent();
        ShowToast("Copied", text.Replace('\r', ' ').Replace('\n', ' '), ColorOk);
    }

    async Task<bool> SetClipboardTextAsync(string text)
    {
        for (int attempt = 0; attempt < 6; attempt++)
        {
            try
            {
                Clipboard.SetText(text, TextDataFormat.UnicodeText);
                _expectedClipboardText = text;
                return true;
            }
            catch (System.Runtime.InteropServices.ExternalException)
            {
                if (attempt == 5) return false;
                await Task.Delay(70 + attempt * 35);
            }
            catch (InvalidOperationException)
            {
                if (attempt == 5) return false;
                await Task.Delay(70 + attempt * 35);
            }
        }
        return false;
    }

    void SyncRecent()
    {
        Recent.Clear();
        foreach (var it in History.Take(3)) Recent.Add(it);
    }

    async void HistoryItem_Click(object sender, MouseButtonEventArgs e)
    {
        try
        {
        if (sender is FrameworkElement { DataContext: ClipItem item })
        {
            if (await SetClipboardTextAsync(item.Text))
            {
                Collapse();
                ShowToast("Copied", item.Preview, ColorOk);
            }
            else ShowToast("Clipboard busy", "Could not write text. Please try again.", ColorError);
        }
        }
        catch (Exception ex) { Log.Error("HistoryItem_Click", ex); }
    }

    void ClearHistory_Click(object sender, RoutedEventArgs e)
    {
        History.Clear(); Recent.Clear(); _totalChars = 0;
    }

    void SearchBox_TextChanged(object sender, TextChangedEventArgs e) => HistoryView?.Refresh();

    void ScheduleClock()
    {
        var n = DateTime.Now;
        _clock.Stop();
        _clock.Interval = TimeSpan.FromMilliseconds(60_000 - (n.Second * 1000 + n.Millisecond) + 50);
        _clock.Start();
    }

    // ================= Состояния и анимации =================
    void Animate(double w, double h, double radius, bool spring)
    {
        int ver = ++_animVer;
        SetWindowBox(Math.Max(_boxW, w), Math.Max(_boxH, h));   // одно изменение размера окна на старте анимации
        _boxW = w; _boxH = h;
        // Soft layout motion; the pill itself gets a separate multi-stage jelly settle.
        var dur = TimeSpan.FromMilliseconds(spring ? 340 : 285);
        IEasingFunction ease = new CubicEase { EasingMode = EasingMode.EaseInOut };

        var aw = new DoubleAnimation(w, dur) { EasingFunction = ease };
        var ah = new DoubleAnimation(h, dur)
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };

        aw.Completed += (_, _) =>
        {
            if (ver != _animVer) return;
            Pill.BeginAnimation(WidthProperty, null);
            Pill.Width = w;
            Pill.BeginAnimation(HeightProperty, null);
            Pill.Height = h;
            SetWindowBox(w, h);                                  // одно изменение размера окна по окончании
        };

        // Visible jelly motion: stretch on the travel axis, squash on the other,
        // then make two progressively smaller rebounds before settling.
        _jellyTransform.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        _jellyTransform.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        var sx = new DoubleAnimationUsingKeyFrames();
        var sy = new DoubleAnimationUsingKeyFrames();
        var jellyEase = new CubicEase { EasingMode = EasingMode.EaseOut };
        if (spring)
        {
            sx.KeyFrames.Add(new EasingDoubleKeyFrame(1.15, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(90)), jellyEase));
            sx.KeyFrames.Add(new EasingDoubleKeyFrame(0.91, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(175)), jellyEase));
            sx.KeyFrames.Add(new EasingDoubleKeyFrame(1.045, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(270)), jellyEase));
            sx.KeyFrames.Add(new EasingDoubleKeyFrame(1.0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(390)), jellyEase));
            sy.KeyFrames.Add(new EasingDoubleKeyFrame(0.84, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(90)), jellyEase));
            sy.KeyFrames.Add(new EasingDoubleKeyFrame(1.10, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(175)), jellyEase));
            sy.KeyFrames.Add(new EasingDoubleKeyFrame(0.97, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(270)), jellyEase));
            sy.KeyFrames.Add(new EasingDoubleKeyFrame(1.0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(390)), jellyEase));
        }
        else
        {
            sx.KeyFrames.Add(new EasingDoubleKeyFrame(0.86, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(75)), jellyEase));
            sx.KeyFrames.Add(new EasingDoubleKeyFrame(1.075, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(155)), jellyEase));
            sx.KeyFrames.Add(new EasingDoubleKeyFrame(1.0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(315)), jellyEase));
            sy.KeyFrames.Add(new EasingDoubleKeyFrame(1.14, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(75)), jellyEase));
            sy.KeyFrames.Add(new EasingDoubleKeyFrame(0.95, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(155)), jellyEase));
            sy.KeyFrames.Add(new EasingDoubleKeyFrame(1.0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(315)), jellyEase));
        }
        sx.Completed += (_, _) => _jellyTransform.ScaleX = 1;
        sy.Completed += (_, _) => _jellyTransform.ScaleY = 1;

        Pill.CornerRadius = new CornerRadius(radius);
        RebuildBody();
        Pill.BeginAnimation(WidthProperty, aw);
        Pill.BeginAnimation(HeightProperty, ah);
        _jellyTransform.BeginAnimation(ScaleTransform.ScaleXProperty, sx);
        _jellyTransform.BeginAnimation(ScaleTransform.ScaleYProperty, sy);
    }

    void FadeInFull()
    {
        Full.Opacity = 0;
        var fade = new DoubleAnimation(1, TimeSpan.FromMilliseconds(180))
        {
            BeginTime = TimeSpan.FromMilliseconds(80)
        };
        fade.Completed += (_, _) => { Full.BeginAnimation(OpacityProperty, null); Full.Opacity = 1; };
        Full.BeginAnimation(OpacityProperty, fade);

        // содержимое мягко всплывает снизу
        FullShift.Y = 14;
        var rise = new DoubleAnimation(0, TimeSpan.FromMilliseconds(360))
        {
            BeginTime = TimeSpan.FromMilliseconds(40),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        rise.Completed += (_, _) => { FullShift.BeginAnimation(TranslateTransform.YProperty, null); FullShift.Y = 0; };
        FullShift.BeginAnimation(TranslateTransform.YProperty, rise);
    }

    void FadePage(UIElement el)
    {
        el.Opacity = 0;
        var a = new DoubleAnimation(1, TimeSpan.FromMilliseconds(220))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        a.Completed += (_, _) => { el.BeginAnimation(OpacityProperty, null); el.Opacity = 1; };
        el.BeginAnimation(OpacityProperty, a);
    }

    async void StartWelcomeIntro()
    {
        if (!IsVisible || _state != IslandState.Collapsed) return;
        _welcomeRunning = true;

        // The intro is a separate visual state: a true 48x48 black circle,
        // with only the two eyes visible. Keep every other panel out of the tree's
        // visible layer until the circle has finished unfolding.
        _boxW = 48;
        _boxH = 48;
        Pill.Width = 48;
        Pill.Height = 48;
        Pill.CornerRadius = new CornerRadius(24);
        Grid.SetColumn(Eyes, 0);
        Grid.SetColumnSpan(Eyes, 3);
        Eyes.HorizontalAlignment = HorizontalAlignment.Center;
        Eyes.Margin = new Thickness(0);
        Mini.Visibility = Visibility.Visible;
        TimePanel.Visibility = Visibility.Visible;
        TitlePanel.Visibility = Visibility.Collapsed;
        TimeText.Visibility = Visibility.Collapsed;
        WeatherHost.Visibility = Visibility.Collapsed;
        TempText.Visibility = Visibility.Collapsed;
        TimeText.Opacity = 0;
        WeatherHost.Opacity = 0;
        TempText.Opacity = 0;
        TitlePanel.Opacity = 0;
        ToastPanel.Visibility = Visibility.Collapsed;
        Full.Visibility = Visibility.Collapsed;
        ApplyPosition();
        RebuildBody();

        // 1. Pause on the round face so the greeting is actually visible.
        await Task.Delay(360);

        // 2. Complete the wink before starting the gaze movement.
        var wink = new DoubleAnimationUsingKeyFrames();
        var ease = new CubicEase { EasingMode = EasingMode.EaseInOut };
        wink.KeyFrames.Add(new EasingDoubleKeyFrame(0.08, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(75)), ease));
        wink.KeyFrames.Add(new EasingDoubleKeyFrame(1.0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(235)), ease));
        wink.Completed += (_, _) =>
        {
            EyeLScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            EyeLScale.ScaleY = 1;
        };
        EyeLScale.BeginAnimation(ScaleTransform.ScaleYProperty, wink);
        await Task.Delay(255);

        // 3. Both eyes briefly look toward the eye that just winked.
        var gaze = new DoubleAnimation(-2.6, TimeSpan.FromMilliseconds(145))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        EyeLShift.BeginAnimation(TranslateTransform.XProperty, gaze);
        EyeRShift.BeginAnimation(TranslateTransform.XProperty, gaze);
        await Task.Delay(250);

        EyeLShift.BeginAnimation(TranslateTransform.XProperty, null);
        EyeRShift.BeginAnimation(TranslateTransform.XProperty, null);
        EyeLShift.X = EyeRShift.X = 0;
        Grid.SetColumnSpan(Eyes, 1);
        Eyes.HorizontalAlignment = HorizontalAlignment.Left;
        Eyes.Margin = new Thickness(14, 0, 0, 0);
        UpdateMini();

        // 4. Only now unfold the island.
        Animate(IdleW, CollapsedH, 24, spring: true);
        await Task.Delay(390);
        Pill.CornerRadius = new CornerRadius(18);
        RebuildBody();
        _welcomeRunning = false;
        if (_now == null)
        {
            NowTitle.Text = _s.Language == "ru" ? "Ничего не воспроизводится" : "Nothing playing";
            NowArtist.Text = "";
            PlayBtn.Content = "\uE768";
        }
        else
        {
            NowTitle.Text = _now.Title;
            NowArtist.Text = _now.Artist;
            PlayBtn.Content = _now.Playing ? "\uE769" : "\uE768";
        }
        UpdatePlayButton();
        UpdateMini();
        FadeWelcomeInfo();
        if (_now?.Playing == true) Animate(PlayW, CollapsedH, 18, spring: false);
        EnsureFx();
    }

    void FadeWelcomeInfo()
    {
        foreach (UIElement element in new UIElement[] { TimeText, WeatherHost, TempText, TitlePanel })
            FadeInWelcomeElement(element);
    }

    void FadeInWelcomeElement(UIElement element)
    {
        if (element.Visibility != Visibility.Visible || element.Opacity >= 0.99) return;
        var fade = new DoubleAnimation(1, TimeSpan.FromMilliseconds(520))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        fade.Completed += (_, _) => { element.BeginAnimation(OpacityProperty, null); element.Opacity = 1; };
        element.BeginAnimation(OpacityProperty, fade);
    }

    void Collapse()
    {
        if (_state == IslandState.Collapsed && Full.Visibility != Visibility.Visible)
        {
            UpdateMini();
            EnsureFx();
            return;
        }

        _state = IslandState.Collapsed;
        Mini.Visibility = Visibility.Visible;
        ToastPanel.Visibility = Visibility.Collapsed;
        TimePanel.Visibility = Visibility.Visible;
        SearchBox.Text = "";
        UpdateMini();
        RevealEyes();

        // Fade the expanded content out instead of cutting it off on the first frame.
        int fadeVer = ++_fullFadeVer;
        var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(125))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        fade.Completed += (_, _) =>
        {
            if (fadeVer != _fullFadeVer || _state != IslandState.Collapsed) return;
            Full.BeginAnimation(OpacityProperty, null);
            Full.Opacity = 1;
            Full.Visibility = Visibility.Collapsed;
        };
        Full.BeginAnimation(OpacityProperty, fade);

        Animate(_now?.Playing == true ? PlayW : IdleW, CollapsedH, 18, spring: false);
        EnsureFx();
    }

    void RevealEyes()
    {
        // Start from a neutral gaze so returning from the expanded panel never
        // makes the eyes snap toward the current mouse position.
        _lLx = _lLy = _lRx = _lRy = 0;
        EyeLShift.X = EyeLShift.Y = EyeRShift.X = EyeRShift.Y = 0;
        EyeLScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        EyeRScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);

        foreach (var scale in new[] { EyeLScale, EyeRScale })
        {
            var reveal = new DoubleAnimationUsingKeyFrames();
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            reveal.KeyFrames.Add(new EasingDoubleKeyFrame(0.12, KeyTime.FromTimeSpan(TimeSpan.Zero), ease));
            reveal.KeyFrames.Add(new EasingDoubleKeyFrame(1.12, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(150)), ease));
            reveal.KeyFrames.Add(new EasingDoubleKeyFrame(0.96, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(245)), ease));
            reveal.KeyFrames.Add(new EasingDoubleKeyFrame(1.0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(360)), ease));
            reveal.Completed += (_, _) =>
            {
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
                scale.ScaleY = 1;
            };
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, reveal);
        }
    }

    void Expand()
    {
        ++_fullFadeVer; // cancel any pending collapse fade before showing the panel again
        _toastTimer.Stop();
        _state = IslandState.Expanded;
        Mini.Visibility = Visibility.Collapsed;
        Full.Visibility = Visibility.Visible;
        FadeInFull();
        Animate(FullW, FullH, 30, spring: true);
        Activate();
    }

    /// <summary>Тост. Цветная точка — единственное, что красит остров (кроме музыки).</summary>
    void ShowToast(string title, string sub, string? colorHex = null)
    {
        if (_welcomeRunning || _state == IslandState.Expanded) return;
        _state = IslandState.Toast;
        ToastTitle.Text = title;
        ToastSub.Text = sub;
        ToastDot.Fill = colorHex == null
            ? _accent
            : Freeze(new SolidColorBrush((Color)ColorConverter.ConvertFromString(colorHex)));
        ToastDot.Effect = new DropShadowEffect
        {
            Color = ((SolidColorBrush)ToastDot.Fill).Color, BlurRadius = 10, ShadowDepth = 0, Opacity = 0.9
        };
        TimePanel.Visibility = Visibility.Collapsed;
        ToastPanel.Visibility = Visibility.Visible;
        Animate(ToastW, ToastH, 26, spring: true);
        _toastTimer.Stop();
        _toastTimer.Start();
    }

    void UpdateMini()
    {
        // Clock/media/weather updates can arrive while the welcome animation is
        // running. They must never leak into the initial eyes-only circle.
        if (_welcomeRunning)
        {
            TitlePanel.Visibility = Visibility.Collapsed;
            TimeText.Visibility = Visibility.Collapsed;
            WeatherHost.Visibility = Visibility.Collapsed;
            TempText.Visibility = Visibility.Collapsed;
            return;
        }

        bool playing = _now?.Playing == true;
        var now = DateTime.Now;
        TitlePanel.Visibility = playing ? Visibility.Visible : Visibility.Collapsed;
        TitleText.Text = playing ? _now!.Title : "";

        // In music mode the title owns the whole compact row. Hide the clock and
        // weather completely instead of letting their text overlap the track name.
        TimeText.Visibility = playing ? Visibility.Collapsed : Visibility.Visible;
        WeatherHost.Visibility = !playing && WeatherHost.Child != null ? Visibility.Visible : Visibility.Collapsed;
        TempText.Visibility = !playing && !string.IsNullOrEmpty(TempText.Text) ? Visibility.Visible : Visibility.Collapsed;

        TimeText.Text = now.ToString("HH:mm");
        TimeText.HorizontalAlignment = HorizontalAlignment.Center;
        TimeText.Margin = new Thickness(0);
        if (FullClock != null) FullClock.Text = now.ToString("HH:mm");
    }

    // ================= Жидкость: волна внизу, глазки, погода =================
    [StructLayout(LayoutKind.Sequential)] struct POINT { public int X, Y; }
    [DllImport("user32.dll")] static extern bool GetCursorPos(out POINT p);

    bool _fxAttached;
    bool _welcomeRunning = true;
    TimeSpan _lastRenderTime;
    readonly Random _rng = new();
    readonly WeatherWatcher _weather = new();
    readonly ScaleTransform _jellyTransform = new(1, 1);
    readonly GradientStop _tintMid = new(Color.FromRgb(0, 0, 0), 0.7);
    readonly GradientStop _tintEnd = new(Color.FromRgb(0, 0, 0), 1.0);
    LinearGradientBrush? _bodyBrush;
    Color _accentColor = Colors.White;
    double _wavePhase, _waveAmp, _open = 1, _blinkT = -1;
    double _lLx, _lLy, _lRx, _lRy, _eyeFollow = 0.2;
    double _glX, _glY;                       // «случайный взгляд», когда мышь стоит
    DateTime _nextGlance = DateTime.Now;
    bool _lastWave;
    DateTime _nextBlink = DateTime.Now.AddSeconds(3), _lastMove = DateTime.Now, _lastEyeUpdate = DateTime.Now, _lastBlinkUpdate = DateTime.Now;
    int _lastCx = int.MinValue, _lastCy;

    void InitLiquid()
    {
        _bodyBrush = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
        _bodyBrush.GradientStops.Add(new GradientStop(Color.FromRgb(0, 0, 0), 0.0));
        _bodyBrush.GradientStops.Add(_tintMid);
        _bodyBrush.GradientStops.Add(_tintEnd);
        Body.Fill = _bodyBrush;
        Pill.SizeChanged += (_, _) => RebuildBody();
        _weather.Changed += w => Dispatcher.BeginInvoke(new Action(() => ApplyWeather(w)));
        _ = _weather.StartAsync();
        EnsureFx();
    }

    void EnsureFx()
    {
        if (_fxAttached) return;
        _lastRenderTime = TimeSpan.Zero;
        CompositionTarget.Rendering += FxRendering;
        _fxAttached = true;
    }

    void FxRendering(object? sender, EventArgs e)
    {
        if (e is not RenderingEventArgs args) return;
        double dt = _lastRenderTime == TimeSpan.Zero ? 1.0 / 60.0 :
            Math.Clamp((args.RenderingTime - _lastRenderTime).TotalSeconds, 0.001, 0.05);
        _lastRenderTime = args.RenderingTime;
        bool playing = !_welcomeRunning && _now?.Playing == true;
        double target = playing ? 5.0 : 0.0;
        double follow = 1 - Math.Exp(-11.0 * dt);
        _waveAmp += (target - _waveAmp) * follow;
        if (Math.Abs(target - _waveAmp) < 0.03) _waveAmp = target;
        _wavePhase += 3.25 * dt;

        bool wave = _waveAmp > 0.01;
        bool eyes = !_welcomeRunning && IsVisible && _state == IslandState.Collapsed && TimePanel.Visibility == Visibility.Visible;
        if (eyes) UpdateEyes(playing);
        if (wave || _lastWave) RebuildBody();
        _lastWave = wave;
        if (!wave && !eyes)
        {
            CompositionTarget.Rendering -= FxRendering;
            _fxAttached = false;
            _lastRenderTime = TimeSpan.Zero;
        }
    }

    static double Smooth(double t) { t = Math.Clamp(t, 0, 1); return t * t * (3 - 2 * t); }

    double WaveY(double x, double w, double amp, double r)
    {
        double edge = Math.Min(x, w - x);
        double env = Smooth((edge - r) / 14.0);   // у скруглённых углов волна затухает
        double v = 0.62 * Math.Sin(x * 0.37 - _wavePhase) + 0.38 * Math.Sin(x * 0.21 + _wavePhase * 0.7);
        return amp * env * (0.5 + 0.5 * v);
    }

    List<Point> WavePoints(double xa, double xb, double yb, double amp, double w, double r)
    {
        var pts = new List<Point>();
        for (double x = xa; x >= xb; x -= 3.0) pts.Add(new Point(x, yb + WaveY(x, w, amp, r)));
        pts.Add(new Point(xb, yb));
        return pts;
    }

    void RebuildBody()
    {
        double w = Pill.ActualWidth, h = Pill.ActualHeight;
        if (w < 8 || h < 8 || _bodyBrush == null) return;
        double amp = _waveAmp;
        double x0 = 0.5, x1 = w - 0.5, y0 = 0.5, yb = h - amp - 0.5;
        double r = Math.Min(Pill.CornerRadius.TopLeft, Math.Min((x1 - x0) / 2, (yb - y0) / 2));
        if (r < 1) r = 1;
        bool wave = amp > 0.01;

        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(new Point(x0 + r, y0), true, true);
            c.LineTo(new Point(x1 - r, y0), true, false);
            c.ArcTo(new Point(x1, y0 + r), new Size(r, r), 0, false, SweepDirection.Clockwise, true, false);
            c.LineTo(new Point(x1, yb - r), true, false);
            c.ArcTo(new Point(x1 - r, yb), new Size(r, r), 0, false, SweepDirection.Clockwise, true, false);
            if (wave) c.PolyLineTo(WavePoints(x1 - r, x0 + r, yb, amp, w, r), true, true);
            else c.LineTo(new Point(x0 + r, yb), true, false);
            c.ArcTo(new Point(x0, yb - r), new Size(r, r), 0, false, SweepDirection.Clockwise, true, false);
            c.LineTo(new Point(x0, y0 + r), true, false);
            c.ArcTo(new Point(x0 + r, y0), new Size(r, r), 0, false, SweepDirection.Clockwise, true, false);
        }
        g.Freeze();
        Body.Data = g;

        if (wave)
        {
            var wg = new StreamGeometry();
            using (var c2 = wg.Open())
            {
                c2.BeginFigure(new Point(x1 - r, yb), false, false);
                c2.PolyLineTo(WavePoints(x1 - r, x0 + r, yb, amp, w, r), true, true);
            }
            wg.Freeze();
            Wave.Data = wg;
        }
        Wave.Visibility = wave ? Visibility.Visible : Visibility.Collapsed;
        Wave.Opacity = Math.Min(1, amp / 1.8);

        // нижняя кромка слегка подкрашивается акцентом
        double k = 0.34 * Math.Min(1, amp / 3.6);
        _tintEnd.Color = Color.FromRgb((byte)(_accentColor.R * k), (byte)(_accentColor.G * k), (byte)(_accentColor.B * k));
        _tintMid.Offset = Math.Max(0.3, 1 - 26.0 / h);
    }

    void UpdateEyes(bool playing)
    {
        if (!GetCursorPos(out var p)) return;
        var now = DateTime.Now;
        if (p.X != _lastCx || p.Y != _lastCy) { _lastCx = p.X; _lastCy = p.Y; _lastMove = now; }
        bool idleSleep = false;   // не засыпает: при АФК водит глазами и моргает
        if (_moodUntil != DateTime.MaxValue && now >= _moodUntil) _eyeMood = EyeMood.Neutral;
        bool sleepy = idleSleep || _eyeMood == EyeMood.Sleepy; // курсор давно стоит — глазки засыпают
        bool excited = _eyeMood == EyeMood.Excited;
        bool surprised = _eyeMood == EyeMood.Surprised;
        bool focused = _eyeMood == EyeMood.Focused;
        double eyeDt = Math.Clamp((now - _lastEyeUpdate).TotalSeconds, 0.001, 0.05);
        _eyeFollow = 1 - Math.Exp(-12.0 * eyeDt);
        double openness = sleepy ? 0.30 : surprised ? 1.24 : focused ? 0.88 : 1.0;
        _open += (openness - _open) * (1 - Math.Exp(-5.5 * eyeDt));
        if (Math.Abs(openness - _open) < 0.002) _open = openness;
        _lastEyeUpdate = now;

        // Keep following the pointer even when it is stationary. Random idle glances
        // used to override the pointer target after three seconds, making tracking appear broken.
        Look(EyeL, p, sleepy, false, playing, ref _lLx, ref _lLy);
        Look(EyeR, p, sleepy, false, playing, ref _lRx, ref _lRy);
        if (excited) { _lLy += Math.Sin(now.TimeOfDay.TotalSeconds * 11) * 0.65; _lRy += Math.Sin(now.TimeOfDay.TotalSeconds * 11 + 0.45) * 0.65; }
        if (_eyeMood == EyeMood.Happy) { _lLy += 0.35; _lRy += 0.35; }
        EyeLShift.X = _lLx; EyeLShift.Y = _lLy;
        EyeRShift.X = _lRx; EyeRShift.Y = _lRy;

        double blink = 1;
        if (_blinkT >= 0)
        {
            _blinkT += Math.Clamp((now - _lastBlinkUpdate).TotalSeconds, 0.001, 0.05);
            double ph = _blinkT / 0.17;
            if (ph >= 1)
            {
                _blinkT = -1;
                _nextBlink = now.AddSeconds(_rng.NextDouble() < 0.22 ? 0.25 : 2.5 + _rng.NextDouble() * 4.5);
            }
            else blink = 1 - 0.93 * Math.Sin(Math.PI * ph);
        }
        else if (now >= _nextBlink) _blinkT = 0;

        _lastBlinkUpdate = now;
        double sy = Math.Max(0.06, Math.Min(1.28, _open * blink));
        EyeLScale.ScaleY = sy;
        EyeRScale.ScaleY = sy;
    }

    /// <summary>Овал целиком смещается в сторону курсора (до ±3 px), плавно догоняя цель.</summary>
    void Look(FrameworkElement eye, POINT cur, bool sleepy, bool idle, bool playing, ref double lx, ref double ly)
    {
        double tx = 0, ty = sleepy ? 2.2 : 0;
        if (!sleepy)
        {
            if (idle)
            {
                tx = _glX; ty = _glY;
            }
            else
            {
                try
                {
                    var c = eye.PointToScreen(new Point(eye.ActualWidth / 2, eye.ActualHeight / 2));
                    double dx = cur.X - c.X, dy = cur.Y - c.Y, d = Math.Sqrt(dx * dx + dy * dy);
                    // Ignore a distant cursor. Otherwise the first frame after
                    // startup can pull both eyes hard to one side of the pill.
                    if (d > 1)
                    {
                        double k = Smooth(Math.Min(1.0, d / 90.0));
                        tx = dx / d * 2.35 * k;
                        ty = dy / d * 2.15 * k;
                    }
                }
                catch (InvalidOperationException) { }   // окно скрыто/ещё не отрисовано — глаз остаётся на месте
            }
            // Tiny continuous sway keeps the eyes alive without sudden direction changes.
            double dance = Math.Sin(_wavePhase * (playing ? 1.35 : 0.72));
            tx += dance * (playing ? 0.24 : 0.12);
            ty += Math.Sin(_wavePhase * (playing ? 1.8 : 0.95) + 0.6) * (playing ? 0.42 : 0.22);
        }
        lx += (tx - lx) * _eyeFollow;
        ly += (ty - ly) * _eyeFollow;
        if (Math.Abs(tx - lx) < 0.01) lx = tx;   // дошли до цели — перестаём менять значения и вызывать перерисовку
        if (Math.Abs(ty - ly) < 0.01) ly = ty;
    }

    void ApplyWeather(WeatherInfo? w)
    {
        if (w == null)
        {
            // Данных нет (нет сети/кэша) — честно показываем «—°», без выдуманной иконки.
            WeatherHost.Child = null;
            TempText.Text = "—°";
            bool showFallback = !_welcomeRunning && _now?.Playing != true;
            WeatherHost.Visibility = Visibility.Collapsed;
            TempText.Visibility = showFallback ? Visibility.Visible : Visibility.Collapsed;
            if (showFallback) FadeInWelcomeElement(TempText);
            return;
        }
        WeatherHost.Child = WeatherIcons.Build(w.Kind, w.IsDay);
        TempText.Text = (w.TempC > 0 ? "+" : "") + w.TempC + "°";
        bool showWeather = !_welcomeRunning && _now?.Playing != true;
        WeatherHost.Visibility = showWeather ? Visibility.Visible : Visibility.Collapsed;
        TempText.Visibility = showWeather ? Visibility.Visible : Visibility.Collapsed;
        if (showWeather) { FadeInWelcomeElement(WeatherHost); FadeInWelcomeElement(TempText); }
    }

    // ================= Обработчики окна =================
    void Pill_Click(object sender, MouseButtonEventArgs e)
    {
        if (_state != IslandState.Expanded) Expand();
    }

    void Window_Deactivated(object? sender, EventArgs e)
    {
        if (_state == IslandState.Expanded) Collapse();
    }

    void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) Collapse();
    }

    void Exit_Click(object sender, RoutedEventArgs e) => ExitApplication();

    void Tab_Checked(object sender, RoutedEventArgs e)
    {
        if (HomePage == null || ClipPage == null || SettingsPage == null) return;
        var tag = (sender as RadioButton)?.Tag as string;
        HomePage.Visibility = tag == "Home" ? Visibility.Visible : Visibility.Collapsed;
        ClipPage.Visibility = tag == "Clip" ? Visibility.Visible : Visibility.Collapsed;
        SettingsPage.Visibility = tag == "Set" ? Visibility.Visible : Visibility.Collapsed;
        FadePage(tag == "Home" ? HomePage : tag == "Clip" ? ClipPage : SettingsPage);
        if (tag == "Clip") SearchBox.Focus();
        if (tag == "Set") UpdateAutoButton();
    }

    // ================= Плитки =================
    void BuildTiles()
    {
        var tiles = new (string Glyph, string Label, Action Act)[]
        {
            ("\uE722", "Region",      SnipRegion),
            ("\uE8B7", "File Explorer",    () => Launch("explorer.exe")),
            ("\uE70F", "Notepad",      () => Launch("notepad.exe")),
            ("\uE8EF", "Calculator",  () => Launch("calc.exe")),
            ("\uE756", "Terminal",     () => Launch("powershell.exe")),
            ("\uE9D9", "Task Manager",    () => Launch("taskmgr.exe")),
        };

        var style = (Style)FindResource("TileBtn");
        var icons = (FontFamily)FindResource("Icons");
        foreach (var t in tiles)
        {
            var sp = new StackPanel { HorizontalAlignment = global::System.Windows.HorizontalAlignment.Center };
            sp.Children.Add(new TextBlock
            {
                Text = t.Glyph, FontFamily = icons, FontSize = 19, HorizontalAlignment = global::System.Windows.HorizontalAlignment.Center
            });
            sp.Children.Add(new TextBlock
            {
                Text = t.Label, FontSize = 11, Margin = new Thickness(0, 5, 0, 0),
                HorizontalAlignment = global::System.Windows.HorizontalAlignment.Center
            });
            var btn = new Button { Style = style, Content = sp };
            var act = t.Act;
            btn.Click += (_, _) => act();
            TileGrid.Children.Add(btn);
        }
    }

    void Launch(string exe)
    {
        Collapse();
        try { Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true }); }
        catch (Exception ex) { ShowToast("Could not launch", ex.Message, ColorError); }
    }

    // ================= Скриншоты =================
    (BitmapSource Img, double Sx, double Sy)? CaptureScreen()
    {
        int pw = GetSystemMetrics(0), ph = GetSystemMetrics(1);
        IntPtr sdc = GetDC(IntPtr.Zero);
        IntPtr mdc = CreateCompatibleDC(sdc);
        IntPtr bmp = CreateCompatibleBitmap(sdc, pw, ph);
        try
        {
            var old = SelectObject(mdc, bmp);
            BitBlt(mdc, 0, 0, pw, ph, sdc, 0, 0, 0x00CC0020 | 0x40000000); // SRCCOPY | CAPTUREBLT
            SelectObject(mdc, old);
            var src = Imaging.CreateBitmapSourceFromHBitmap(
                bmp, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            src.Freeze();
            return (src, pw / SystemParameters.PrimaryScreenWidth, ph / SystemParameters.PrimaryScreenHeight);
        }
        catch (Exception ex) { Log.Error("Снимок экрана не удался", ex); return null; }
        finally
        {
            DeleteObject(bmp); DeleteDC(mdc); ReleaseDC(IntPtr.Zero, sdc);
        }
    }

    async void SnipRegion()
    {
        Collapse();
        Hide();                       // чтобы остров не попал в кадр
        await Task.Delay(220);
        (BitmapSource Img, double Sx, double Sy)? shot = null;
        try { shot = CaptureScreen(); }
        catch (Exception ex) { Log.Error("CaptureScreen", ex); }
        if (shot == null)
        {
            Show();
            ShowToast("Error", "Could not capture screenshot", ColorError);
            return;
        }
        var win = new SnipWindow(shot.Value.Img, shot.Value.Sx, shot.Value.Sy,
            msg => ShowToast("Screenshot", msg, ColorOk));
        win.Closed += (_, _) => Show();
        win.Show();
    }

    // ================= Медиа =================
    static void PressKey(byte vk)
    {
        keybd_event(vk, 0, 0, UIntPtr.Zero);
        keybd_event(vk, 0, 2, UIntPtr.Zero);
    }

    void MediaPrev_Click(object s, RoutedEventArgs e) => PressKey(VK_MEDIA_PREV);
    void MediaPlay_Click(object s, RoutedEventArgs e) => PressKey(VK_MEDIA_PLAY);
    void MediaNext_Click(object s, RoutedEventArgs e) => PressKey(VK_MEDIA_NEXT);
    void VolDown_Click(object s, RoutedEventArgs e) => PressKey(VK_VOLUME_DOWN);
    void VolUp_Click(object s, RoutedEventArgs e) => PressKey(VK_VOLUME_UP);
    void VolMute_Click(object s, RoutedEventArgs e) => PressKey(VK_VOLUME_MUTE);

    void OnMediaChanged(NowPlaying? np)
    {
        _now = np;
        // Cache media state silently until the welcome sequence has finished.
        if (_welcomeRunning) return;
        if (np == null)
        {
            NowTitle.Text = "Nothing playing";
            NowArtist.Text = "";
            PlayBtn.Content = "\uE768";
        }
        else
        {
            NowTitle.Text = np.Title;
            NowArtist.Text = np.Artist;
            PlayBtn.Content = np.Playing ? "\uE769" : "\uE768";
            if (!np.Playing) SetEyeMood(EyeMood.Calm, 1.8);
            else SetEyeMood(EyeMood.Happy, 0.9);
            var key = np.Artist + "|" + np.Title;
            if (np.Playing && key != _lastTrack)
            {
                _lastTrack = key;
                SetEyeMood(EyeMood.Excited, 2.4);
                ShowToast("Now playing",
                    string.IsNullOrEmpty(np.Artist) ? np.Title : $"{np.Artist} — {np.Title}");
            }
        }
        UpdatePlayButton();
        UpdateMini();
        FadeInWelcomeElement(TitlePanel);
        FadeInWelcomeElement(TimeText);
        FadeInWelcomeElement(WeatherHost);
        FadeInWelcomeElement(TempText);
        EnsureFx();
        if (_state == IslandState.Collapsed) Collapse();
    }
}
