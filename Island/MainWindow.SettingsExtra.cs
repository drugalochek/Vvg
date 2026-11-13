using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using Panel = System.Windows.Controls.Panel;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Button = System.Windows.Controls.Button;
using Color = System.Windows.Media.Color;
using Cursors = System.Windows.Input.Cursors;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using RadioButton = System.Windows.Controls.RadioButton;

namespace Island;

/// <summary>
/// Расширенные настройки (v2.7): анимация, глаза, музыка, уведомления, горячая клавиша,
/// буфер обмена и игровой режим. Панель строится кодом и перестраивается при смене языка.
/// </summary>
public partial class MainWindow
{
    const uint MOD_SHIFT = 0x0004, MOD_WIN = 0x0008, VK_I = 0x49;
    bool _hotkeyRegistered;

    /// <summary>Регистрирует горячую клавишу из настроек (старую снимает).</summary>
    void RegisterIslandHotkey(bool reportFailure = false)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;
        if (_hotkeyRegistered) { UnregisterHotKey(hwnd, HotkeyId); _hotkeyRegistered = false; }
        (uint mods, uint vk) = _s.Hotkey switch
        {
            "CtrlShiftSpace" => (MOD_CONTROL | MOD_SHIFT, VK_SPACE),
            "AltSpace" => (MOD_ALT, VK_SPACE),
            "WinAltI" => (MOD_WIN | MOD_ALT, VK_I),
            "Off" => (0u, 0u),
            _ => (MOD_CONTROL | MOD_ALT, VK_SPACE)
        };
        if (vk == 0) return;
        _hotkeyRegistered = RegisterHotKey(hwnd, HotkeyId, mods | MOD_NOREPEAT, vk);
        if (!_hotkeyRegistered)
        {
            Log.Warn($"Горячая клавиша {_s.Hotkey} занята другой программой (код {Marshal.GetLastWin32Error()})");
            if (reportFailure)
                ShowToast("Moku", _s.Language == "ru" ? "Это сочетание занято другой программой" : "This shortcut is used by another app", ColorError);
        }
    }

    static string HotkeyLabel(string id) => id switch
    {
        "CtrlShiftSpace" => "Ctrl + Shift + Space",
        "AltSpace" => "Alt + Space",
        "WinAltI" => "Win + Alt + I",
        "Off" => "—",
        _ => "Ctrl + Alt + Space"
    };

    void UpdateHotkeyHint()
    {
        if (HotkeyHint != null) HotkeyHint.Text = _s.Hotkey == "Off" ? "" : HotkeyLabel(_s.Hotkey);
    }

    // ------------------------------------------------------------------ UI

    void BuildExtraSettings()
    {
        if (ExtraSettingsPanel == null) return;
        bool ru = _s.Language == "ru";
        string L(string r, string e) => ru ? r : e;
        var p = ExtraSettingsPanel;
        p.Children.Clear();

        Section(p, L("АНИМАЦИЯ", "ANIMATION"));
        SliderRow(p, L("Желейность", "Jelly strength"), 0, 200, () => _s.JellyStrength, v => _s.JellyStrength = v, v => v + "%");
        SliderRow(p, L("Скорость анимаций", "Animation speed"), 50, 200, () => _s.AnimationSpeed, v => _s.AnimationSpeed = v, v => v + "%");

        Section(p, L("ГЛАЗА", "EYES"));
        ToggleRow(p, L("Следят за курсором", "Follow the cursor"), () => _s.EyesFollowCursor, v => _s.EyesFollowCursor = v);
        SliderRow(p, L("Дальность взгляда", "Gaze range"), 50, 150, () => _s.GazeRange, v => _s.GazeRange = v, v => v + "%");
        ToggleRow(p, L("Эмоции", "Emotions"), () => _s.EyeEmotions, v => { _s.EyeEmotions = v; if (!v) { _eyeMood = EyeMood.Neutral; _moodLeft = 0; } });
        ToggleRow(p, L("Засыпать ночью", "Fall asleep at night"), () => _s.SleepAtNight, v => _s.SleepAtNight = v);
        ToggleRow(p, L("Танцевать под музыку", "Dance to the music"), () => _s.BeatBounce, v => _s.BeatBounce = v);
        SliderRow(p, L("Энергия танца", "Dance energy"), 0, 200, () => _s.DanceStrength, v => _s.DanceStrength = v, v => v == 0 ? L("только кивает", "nods only") : v + "%");

        Section(p, L("МУЗЫКА", "MUSIC"));
        ToggleRow(p, L("Волна под музыку", "Music wave"), () => _s.MusicWave, v => _s.MusicWave = v);
        SliderRow(p, L("Высота волны", "Wave height"), 30, 150, () => _s.WaveHeight, v => _s.WaveHeight = v, v => v + "%");

        Section(p, L("УВЕДОМЛЕНИЯ", "NOTIFICATIONS"));
        ToggleRow(p, L("При копировании", "On copy"), () => _s.ToastOnCopy, v => _s.ToastOnCopy = v);
        ToggleRow(p, L("При смене трека", "On new track"), () => _s.ToastOnTrack, v => _s.ToastOnTrack = v);

        Section(p, L("ПОВЕДЕНИЕ", "BEHAVIOR"));
        ChoiceRow(p, L("Горячая клавиша", "Shortcut"), "hotkey",
            new[] { ("CtrlAltSpace", "Ctrl+Alt+Space"), ("CtrlShiftSpace", "Ctrl+Shift+Space"), ("WinAltI", "Win+Alt+I"), ("Off", L("Выкл", "Off")) },
            () => _s.Hotkey, v => { _s.Hotkey = v; RegisterIslandHotkey(true); UpdateHotkeyHint(); });
        // v2.8: плавный ползунок вместо четырёх кнопок; лишние записи убираются, только когда ползунок отпустили
        SliderRow(p, L("История буфера", "Clipboard history"), 5, 200, () => _s.ClipboardLimit, v => _s.ClipboardLimit = v,
            v => ru ? $"{v} {Plural(v, "запись", "записи", "записей")}" : $"{v} {(v == 1 ? "item" : "items")}", commit: TrimHistory);
        ToggleRow(p, L("Прятаться в полноэкранных играх", "Fade out in fullscreen games"), () => _s.GameMode, v => { _s.GameMode = v; CheckGameMode(); });

        var reset = new Button
        {
            Style = (Style)FindResource("TileBtn"), Height = 36, Width = 230, Margin = new Thickness(0, 10, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Left,
            Content = L("Сбросить эти настройки", "Reset these settings")
        };
        reset.Click += (_, _) =>
        {
            var d = new Settings();
            _s.JellyStrength = d.JellyStrength; _s.AnimationSpeed = d.AnimationSpeed; _s.GazeRange = d.GazeRange;
            _s.EyesFollowCursor = d.EyesFollowCursor; _s.EyeEmotions = d.EyeEmotions; _s.SleepAtNight = d.SleepAtNight;
            _s.BeatBounce = d.BeatBounce; _s.DanceStrength = d.DanceStrength; _s.MusicWave = d.MusicWave; _s.WaveHeight = d.WaveHeight;
            _s.ToastOnCopy = d.ToastOnCopy; _s.ToastOnTrack = d.ToastOnTrack; _s.GameMode = d.GameMode;
            _s.Hotkey = d.Hotkey; _s.ClipboardLimit = d.ClipboardLimit;
            _s.Save();
            TrimHistory();
            RegisterIslandHotkey(); UpdateHotkeyHint(); CheckGameMode();
            BuildExtraSettings();
            SetEyeMood(EyeMood.Happy, 1.2);
        };
        p.Children.Add(reset);
    }

    void TrimHistory()
    {
        while (History.Count > MaxItems)
        {
            _totalChars -= History[^1].Text.Length;
            History.RemoveAt(History.Count - 1);
        }
        SyncRecent();
    }

    static string Plural(int n, string one, string few, string many)
    {
        int a = Math.Abs(n) % 100, b = a % 10;
        return a is >= 11 and <= 14 ? many : b == 1 ? one : b is >= 2 and <= 4 ? few : many;
    }

    /// <summary>Заголовок группы — тот же вид, что и подписи «НЕДАВНО СКОПИРОВАНО» на главной.</summary>
    static void Section(Panel p, string title)
    {
        p.Children.Add(new Border
        {
            Height = 1, Margin = new Thickness(0, 12, 0, 0),
            Background = new SolidColorBrush(Color.FromArgb(0x14, 255, 255, 255))
        });
        p.Children.Add(new TextBlock
        {
            Text = title, Foreground = new SolidColorBrush(Color.FromArgb(0x80, 255, 255, 255)),
            FontSize = 11, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 10, 0, 8)
        });
    }

    static Grid Row(Panel p, string label, out Grid host)
    {
        var g = new Grid { Margin = new Thickness(0, 0, 0, 10) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(180) });   // как у строк в разметке
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.Children.Add(new TextBlock { Text = label, Foreground = System.Windows.Media.Brushes.White, FontSize = 13, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap });
        host = new Grid();
        Grid.SetColumn(host, 1);
        g.Children.Add(host);
        p.Children.Add(g);
        return g;
    }

    /// <summary>
    /// Плавный ползунок: бегунок идёт ровно за мышью (без шага и «лесенки»), значение применяется сразу,
    /// а сохранение на диск и тяжёлые действия (commit) — один раз, когда ползунок перестали двигать.
    /// </summary>
    void SliderRow(Panel p, string label, int min, int max, Func<int> get, Action<int> set, Func<int, string> fmt, Action? commit = null)
    {
        Row(p, label, out var host);
        var stack = new StackPanel();
        var value = new TextBlock { Text = fmt(get()), Foreground = System.Windows.Media.Brushes.White, FontSize = 12, Margin = new Thickness(0, 0, 0, 3) };
        var slider = new Slider
        {
            Style = (Style)FindResource("MokuSlider"), Minimum = min, Maximum = max, Value = get(),
            IsSnapToTickEnabled = false, IsMoveToPointEnabled = true, SmallChange = 1, LargeChange = Math.Max(1, (max - min) / 20)
        };
        var settle = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(450) };
        settle.Tick += (_, _) => { settle.Stop(); commit?.Invoke(); _s.Save(); };
        slider.ValueChanged += (_, e) =>
        {
            int v = (int)Math.Round(e.NewValue);
            if (v != get()) set(v);
            value.Text = fmt(v);
            settle.Stop(); settle.Start();
        };
        slider.LostMouseCapture += (_, _) => { if (settle.IsEnabled) { settle.Stop(); commit?.Invoke(); _s.Save(); } };
        stack.Children.Add(value);
        stack.Children.Add(slider);
        host.Children.Add(stack);
    }

    /// <summary>Переключатель-«пилюля» с пружинистым бегунком.</summary>
    void ToggleRow(Panel p, string label, Func<bool> get, Action<bool> set)
    {
        Row(p, label, out var host);
        var on = new SolidColorBrush(Color.FromRgb(0x30, 0xD1, 0x58));
        var off = new SolidColorBrush(Color.FromArgb(0x30, 255, 255, 255));
        var knobShift = new TranslateTransform(get() ? 16 : 0, 0);
        var track = new Border
        {
            Width = 40, Height = 24, CornerRadius = new CornerRadius(12), Cursor = Cursors.Hand,
            Background = get() ? on : off, HorizontalAlignment = HorizontalAlignment.Left,
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x38, 255, 255, 255)), BorderThickness = new Thickness(1),
            Child = new System.Windows.Shapes.Ellipse
            {
                Width = 18, Height = 18, Fill = System.Windows.Media.Brushes.White, HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(2, 0, 0, 0), RenderTransform = knobShift
            }
        };
        track.MouseLeftButtonUp += (_, e) =>
        {
            bool v = !get();
            set(v);
            _s.Save();
            track.Background = v ? on : off;
            knobShift.BeginAnimation(TranslateTransform.XProperty,
                new DoubleAnimation(v ? 16 : 0, TimeSpan.FromMilliseconds(320)) { EasingFunction = new ElasticEase { Oscillations = 1, Springiness = 6, EasingMode = EasingMode.EaseOut } });
            e.Handled = true;
        };
        host.Children.Add(track);
    }

    void ChoiceRow(Panel p, string label, string group, (string id, string text)[] items, Func<string> get, Action<string> set)
    {
        Row(p, label, out var host);
        var wrap = new WrapPanel();
        foreach (var (id, text) in items)
        {
            var rb = new RadioButton
            {
                Style = (Style)FindResource("TabBtn"), Content = text, Tag = id,
                GroupName = group + "_" + GetHashCode(), IsChecked = get() == id, Margin = new Thickness(0, 0, 4, 4)
            };
            rb.Checked += (_, _) => { if (get() != id) { set(id); _s.Save(); } };
            wrap.Children.Add(rb);
        }
        host.Children.Add(wrap);
    }
}
