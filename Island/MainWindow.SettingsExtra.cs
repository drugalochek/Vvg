using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using Panel = System.Windows.Controls.Panel;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Collections.Generic;
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
        if (TryRegisterHotkey(hwnd, _s.Hotkey))
        {
            _hotkeyRegistered = _s.Hotkey != "Off";
            _lastRegisteredHotkey = _s.Hotkey;
            return;
        }
        Log.Warn("Горячая клавиша " + _s.Hotkey + " занята другой программой (код " + Marshal.GetLastWin32Error() + ")");
        _s.Hotkey = _lastRegisteredHotkey;
        if (_s.Hotkey != "Off") _hotkeyRegistered = TryRegisterHotkey(hwnd, _s.Hotkey);
        if (reportFailure) ShowToast("Moku", _s.Language == "ru" ? "Сочетание занято. Предыдущее сохранено." : "Shortcut is in use. Previous binding restored.", ColorError);
    }

    bool TryRegisterHotkey(IntPtr hwnd, string value)
    {
        if (value == "Off") return true;
        var parts = value.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0) return false;
        uint mods = 0;
        for (int i = 0; i < parts.Length - 1; i++)
        {
            switch (parts[i].ToLowerInvariant())
            {
                case "ctrl": case "control": mods |= MOD_CONTROL; break;
                case "alt": mods |= MOD_ALT; break;
                case "shift": mods |= MOD_SHIFT; break;
                case "win": case "windows": mods |= MOD_WIN; break;
                default: return false;
            }
        }
        if (!Enum.TryParse<Key>(parts[^1], true, out var key) || key == Key.None || key == Key.System) return false;
        uint vk = (uint)KeyInterop.VirtualKeyFromKey(key);
        return vk != 0 && RegisterHotKey(hwnd, HotkeyId, mods | MOD_NOREPEAT, vk);
    }

    static string HotkeyLabel(string id) => id == "Off" ? "—" : id.Replace("+", " + ");

    void UpdateHotkeyHint()
    {
        if (HotkeyHint != null) HotkeyHint.Text = _s.Hotkey == "Off" ? "" : HotkeyLabel(_s.Hotkey);
    }

    // ------------------------------------------------------------------ UI

    /// <summary>
    /// v2.11: настройки, которые строятся кодом, раскладываются по разделам (глаза, анимации, свёрнутый вид…).
    /// Разделы «Цвета и тема» и «Положение и размер» лежат в разметке, сюда добавляются только их записи для поиска.
    /// </summary>
    void BuildExtraSettings()
    {
        if (CatEyes == null) return;
        bool ru = _s.Language == "ru";
        string L(string r, string e) => ru ? r : e;
        _settingsIndex.RemoveAll(x => !x.Static);
        foreach (var cat in new Panel[] { CatEyes, CatAnim, CatMini, CatMusic, CatApps, CatGeneralExtra }) cat.Children.Clear();

        // ---------------- Глаза
        Panel p = CatEyes; _buildCat = "eyes";
        Section(p, L("ВНЕШНИЙ ВИД", "LOOK"));
        ChoiceRow(p, L("Форма", "Shape"), "eyestyle",
            new[] { ("Oval", L("Овалы", "Ovals")), ("Round", L("Круглые", "Round")), ("Dots", L("Точки", "Dots")),
                    ("Anime", L("Аниме", "Anime")), ("Arcs", "^ ^"), ("Robot", L("Робот", "Robot")), ("Cat", L("Кошачьи", "Cat")) },
            () => _s.EyeStyle, v => { _s.EyeStyle = v; ApplyEyeStyle(); RefreshIslandSize(); });
        SliderRow(p, L("Размер", "Size"), 60, 150, () => _s.EyeSize, v => { _s.EyeSize = v; ApplyEyeStyle(); }, v => v + "%", commit: RefreshIslandSize);
        SliderRow(p, L("Расстояние", "Spacing"), 4, 30, () => _s.EyeSpacing, v => { _s.EyeSpacing = v; ApplyEyeStyle(); }, v => v + " px", commit: RefreshIslandSize);
        EyeColorRow(p, L("Цвет", "Color"), ru);
        SliderRow(p, L("Свечение", "Glow"), 0, 100, () => _s.EyeGlow, v => { _s.EyeGlow = v; ApplyEyeStyle(); }, v => v == 0 ? L("выкл", "off") : v + "%");
        Section(p, L("ПОВЕДЕНИЕ", "BEHAVIOR"));
        ToggleRow(p, L("Следят за курсором", "Follow the cursor"), () => _s.EyesFollowCursor, v => _s.EyesFollowCursor = v);
        SliderRow(p, L("Дальность взгляда", "Gaze range"), 50, 150, () => _s.GazeRange, v => _s.GazeRange = v, v => v + "%");
        SliderRow(p, L("Частота моргания", "Blink rate"), 0, 200, () => _s.BlinkRate, v => _s.BlinkRate = v, v => v == 0 ? L("не моргают", "never") : v + "%");
        ToggleRow(p, L("Эмоции", "Emotions"), () => _s.EyeEmotions, v => { _s.EyeEmotions = v; if (!v) { _eyeMood = EyeMood.Neutral; _moodLeft = 0; } });
        ToggleRow(p, L("Засыпать ночью", "Fall asleep at night"), () => _s.SleepAtNight, v => _s.SleepAtNight = v);
        Section(p, L("ТАНЕЦ", "DANCE"));
        ToggleRow(p, L("Танцевать под музыку", "Dance to the music"), () => _s.BeatBounce, v => _s.BeatBounce = v);
        SliderRow(p, L("Энергия танца", "Dance energy"), 0, 200, () => _s.DanceStrength, v => _s.DanceStrength = v, v => v == 0 ? L("только кивает", "nods only") : v + "%");

        // ---------------- Анимации
        p = CatAnim; _buildCat = "anim";
        Section(p, L("ПОЯВЛЕНИЕ", "APPEARING"));
        ChoiceRow(p, L("Стиль появления", "Appear style"), "appear",
            new[] { ("Spring", L("Пружина", "Spring")), ("Smooth", L("Плавно", "Smooth")), ("Drop", L("Капля", "Drop")) },
            () => _s.AppearStyle, v => { _s.AppearStyle = v; JellyKick(); });
        SliderRow(p, L("Желейность", "Jelly strength"), 0, 200, () => _s.JellyStrength, v => _s.JellyStrength = v, v => v + "%");
        Section(p, L("СКОРОСТЬ", "SPEED"));
        SliderRow(p, L("Общая скорость", "Overall speed"), 50, 200, () => _s.AnimationSpeed, v => _s.AnimationSpeed = v, v => v + "%");
        SliderRow(p, L("Раскрытие острова", "Expanding"), 50, 200, () => _s.ExpandSpeed, v => _s.ExpandSpeed = v, v => v + "%");
        SliderRow(p, L("Уведомления", "Notifications"), 50, 200, () => _s.ToastSpeed, v => _s.ToastSpeed = v, v => v + "%");

        // ---------------- Свёрнутый вид
        p = CatMini; _buildCat = "mini";
        Section(p, L("ЧТО ПОКАЗЫВАТЬ", "WHAT TO SHOW"));
        ChoiceRow(p, L("В центре", "Center"), "minicenter",
            new[] { ("Time", L("Время", "Time")), ("Date", L("Дата", "Date")), ("TimeDate", L("Дата и время", "Date & time")), ("None", L("Ничего", "Nothing")) },
            () => _s.MiniCenter, v => { _s.MiniCenter = v; MiniSettingsChanged(); });
        ChoiceRow(p, L("Справа", "Right"), "miniright",
            new[] { ("Weather", L("Погода", "Weather")), ("Battery", L("Батарея", "Battery")), ("Cpu", L("Процессор", "CPU")), ("Ram", L("Память", "RAM")),
                    ("Time", L("Время", "Time")), ("Date", L("Дата", "Date")), ("None", L("Ничего", "Nothing")) },
            () => _s.MiniRight, v => { _s.MiniRight = v; MiniSettingsChanged(); });
        Section(p, L("ЧАСЫ", "CLOCK"));
        ToggleRow(p, L("24-часовой формат", "24-hour clock"), () => _s.Clock24h, v => { _s.Clock24h = v; MiniSettingsChanged(); });
        ToggleRow(p, L("Показывать секунды", "Show seconds"), () => _s.ClockSeconds, v => { _s.ClockSeconds = v; MiniSettingsChanged(); });
        Section(p, L("ТАЙМЕР", "TIMER"));
        SliderRow(p, L("Таймер по умолчанию", "Default timer"), 1, 180, () => _s.TimerMinutes, v => { _s.TimerMinutes = v; UpdateTimerTiles(); },
            v => v + L(" мин", " min"));
        Hint(p, L("Плитки «Таймер» и «Секундомер» — на главной вкладке. Пока они идут, отсчёт виден справа в свёрнутом острове.",
                  "Timer and Stopwatch tiles are on the Home tab. While running, the countdown shows on the right of the compact island."));

        // ---------------- Музыка и уведомления
        p = CatMusic; _buildCat = "music";
        Section(p, L("МУЗЫКА", "MUSIC"));
        ToggleRow(p, L("Волна под музыку", "Music wave"), () => _s.MusicWave, v => _s.MusicWave = v);
        SliderRow(p, L("Высота волны", "Wave height"), 30, 150, () => _s.WaveHeight, v => _s.WaveHeight = v, v => v + "%");
        Section(p, L("УВЕДОМЛЕНИЯ", "NOTIFICATIONS"));
        ToggleRow(p, L("При копировании", "On copy"), () => _s.ToastOnCopy, v => _s.ToastOnCopy = v);
        ToggleRow(p, L("При смене трека", "On new track"), () => _s.ToastOnTrack, v => _s.ToastOnTrack = v);
        SliderRow(p, L("Время показа", "Show for"), 15, 80, () => _s.ToastTime, v => _s.ToastTime = v,
            v => (v / 10.0).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture).Replace(".", ru ? "," : ".") + L(" с", " s"));
        Hint(p, L("Цвет волны и уведомлений — в разделе «Цвета и тема».", "Wave and notification color is in Colors & theme."));

        // ---------------- Приложения
        p = CatApps; _buildCat = "apps";
        Section(p, L("ПЛИТКИ", "TILES"));
        ChoiceRow(p, L("Размер плиток", "Tile size"), "tilesize",
            new[] { ("Compact", L("Компактные", "Compact")), ("Normal", L("Обычные", "Normal")), ("Large", L("Крупные", "Large")) },
            () => _s.AppTileSize, v => { _s.AppTileSize = v; BuildApps(); });
        Hint(p, L("Правой кнопкой по плитке — своя подпись, иконка и папка. Перетащи плитку на папку, чтобы положить её внутрь.",
                  "Right-click a tile to set its label, icon and folder. Drag a tile onto a folder to put it inside."));

        // ---------------- Общие (язык, автозапуск и «Поддержать» — в разметке)
        p = CatGeneralExtra; _buildCat = "general";
        ChoiceRow(p, L("Горячая клавиша", "Shortcut"), "hotkey",
            new[] { ("CtrlAltSpace", "Ctrl+Alt+Space"), ("CtrlShiftSpace", "Ctrl+Shift+Space"), ("WinAltI", "Win+Alt+I"), ("Off", L("Выкл", "Off")) },
            () => _s.Hotkey, v => { _s.Hotkey = v; RegisterIslandHotkey(true); UpdateHotkeyHint(); });
        // v2.8: плавный ползунок вместо четырёх кнопок; лишние записи убираются, только когда ползунок отпустили
        SliderRow(p, L("История буфера", "Clipboard history"), 5, 200, () => _s.ClipboardLimit, v => _s.ClipboardLimit = v,
            v => ru ? $"{v} {Plural(v, "запись", "записи", "записей")}" : $"{v} {(v == 1 ? "item" : "items")}", commit: TrimHistory);
        Section(p, L("ИГРЫ", "GAMES"));
        ToggleRow(p, L("Прятаться в полноэкранных играх", "Fade out in fullscreen games"), () => _s.GameMode, v => { _s.GameMode = v; CheckGameMode(); });
        _buildCat = "";

        RefreshSettingsNav();
    }

    /// <summary>Сброс одного раздела к значениям по умолчанию.</summary>
    void ResetSettingsSection(string cat)
    {
        var d = new Settings();
        switch (cat)
        {
            case "look":
                _s.Theme = d.Theme; _s.ThemeColor = d.ThemeColor; _s.ThemeBottomColor = d.ThemeBottomColor;
                _s.GradientBalance = d.GradientBalance; _s.Accent = d.Accent;
                if (ThemeCombo != null) ThemeCombo.SelectedValue = _s.Theme;
                SetGlassOpacity(d.GlassOpacity);
                ApplyAppearance(); ApplyAccent();
                break;
            case "place":
                _s.Align = d.Align; _s.VerticalAlign = d.VerticalAlign;
                (_s.Align switch { "Left" => AlignLeft, "Right" => AlignRight, _ => AlignCenter }).IsChecked = true;
                (_s.VerticalAlign switch { "Bottom" => VerticalBottom, "Center" => VerticalCenter, _ => VerticalTop }).IsChecked = true;
                SetOffsetX(d.OffsetX); SetOffset(d.OffsetY); SetScale(d.Scale); SetRadius(d.CornerRadius);
                ApplyPosition();
                break;
            case "eyes":
                _s.EyeStyle = d.EyeStyle; _s.EyeSize = d.EyeSize; _s.EyeSpacing = d.EyeSpacing; _s.EyeColor = d.EyeColor; _s.EyeGlow = d.EyeGlow;
                _s.EyesFollowCursor = d.EyesFollowCursor; _s.GazeRange = d.GazeRange; _s.BlinkRate = d.BlinkRate;
                _s.EyeEmotions = d.EyeEmotions; _s.SleepAtNight = d.SleepAtNight; _s.BeatBounce = d.BeatBounce; _s.DanceStrength = d.DanceStrength;
                ApplyEyeStyle(); RefreshIslandSize();
                break;
            case "anim":
                _s.AppearStyle = d.AppearStyle; _s.JellyStrength = d.JellyStrength; _s.AnimationSpeed = d.AnimationSpeed;
                _s.ExpandSpeed = d.ExpandSpeed; _s.ToastSpeed = d.ToastSpeed;
                break;
            case "mini":
                _s.MiniCenter = d.MiniCenter; _s.MiniRight = d.MiniRight; _s.Clock24h = d.Clock24h; _s.ClockSeconds = d.ClockSeconds;
                _s.TimerMinutes = d.TimerMinutes;
                UpdateTimerTiles(); MiniSettingsChanged();
                break;
            case "music":
                _s.MusicWave = d.MusicWave; _s.WaveHeight = d.WaveHeight; _s.ToastOnCopy = d.ToastOnCopy; _s.ToastOnTrack = d.ToastOnTrack;
                _s.ToastTime = d.ToastTime;
                break;
            case "apps":
                _s.AppTileSize = d.AppTileSize;
                BuildApps();
                break;
            case "general":
                _s.Hotkey = d.Hotkey; _s.ClipboardLimit = d.ClipboardLimit; _s.GameMode = d.GameMode;
                SetFullscreenOpacity(d.FullscreenOpacity);
                TrimHistory(); RegisterIslandHotkey(); UpdateHotkeyHint(); CheckGameMode();
                break;
            default: return;
        }
        _s.Save();
        BuildExtraSettings();
        SetEyeMood(EyeMood.Happy, 1.2);
    }

    /// <summary>Изменился состав свёрнутого вида или формат часов.</summary>
    void MiniSettingsChanged()
    {
        UpdateMini();
        ScheduleClock();
        RefreshIslandSize();
    }

    /// <summary>Лёгкий «толчок» желе — показать характер выбранного стиля появления.</summary>
    void JellyKick()
    {
        if (!_islandMoving)
        {
            double w = Pill.ActualWidth, h = Pill.ActualHeight;
            if (w < 1 || h < 1) return;
            _pw.Reset(w); _ph.Reset(h); _pr.Reset(Pill.CornerRadius.TopLeft);
            (_islandStiffness, _islandZeta) = (BouncyStiffness, BouncyZeta);
        }
        _jy.Velocity -= 1.4 * StyleJelly;
        _jx.Velocity += 0.9 * StyleJelly;
        _islandMoving = true;
        EnsureFx();
    }

    static void Hint(Panel p, string text)
    {
        p.Children.Add(new TextBlock
        {
            Text = text, Foreground = new SolidColorBrush(Color.FromArgb(0x80, 255, 255, 255)), FontSize = 11,
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, -2, 0, 10)
        });
    }

    /// <summary>Цвет глаз: готовые оттенки + свой через палитру.</summary>
    void EyeColorRow(Panel p, string label, bool ru)
    {
        Row(p, label, out var host);
        var wrap = new WrapPanel { VerticalAlignment = VerticalAlignment.Center };
        string[] presets = { "#FFFFFFFF", "#FF7FE3FF", "#FFFFD166", "#FFFF8FAB", "#FF8CFF9E", "#FFC3A6FF", "#FFFF9F43" };
        var swatches = new List<(Border b, string hex)>();
        void Mark()
        {
            foreach (var (b, hex) in swatches)
                b.BorderBrush = string.Equals(hex, _s.EyeColor, StringComparison.OrdinalIgnoreCase)
                    ? System.Windows.Media.Brushes.White : new SolidColorBrush(Color.FromArgb(0x30, 255, 255, 255));
        }
        foreach (var hex in presets)
        {
            var c = ParseColor(hex, Colors.White);
            var b = new Border
            {
                Width = 22, Height = 22, CornerRadius = new CornerRadius(11), Margin = new Thickness(0, 0, 6, 4), Cursor = Cursors.Hand,
                Background = new SolidColorBrush(c), BorderThickness = new Thickness(2)
            };
            string h = hex;
            b.MouseLeftButtonUp += (_, e) => { _s.EyeColor = h; _s.Save(); ApplyEyeStyle(); Mark(); e.Handled = true; };
            swatches.Add((b, hex));
            wrap.Children.Add(b);
        }
        var custom = new Button
        {
            Style = (Style)FindResource("TileBtn"), Height = 24, Width = 74, Margin = new Thickness(2, 0, 0, 4),
            Content = new TextBlock { Text = ru ? "Свой…" : "Custom…", FontSize = 11 }
        };
        custom.Click += (_, _) =>
        {
            var picker = new ColorPickerWindow(ParseColor(_s.EyeColor, Colors.White), ru, ru ? "ЦВЕТ ГЛАЗ" : "EYE COLOR") { Owner = this };
            if (WithModal(() => picker.ShowDialog()) != true) return;
            var c = picker.SelectedColor;
            _s.EyeColor = $"#FF{c.R:X2}{c.G:X2}{c.B:X2}";
            _s.Save(); ApplyEyeStyle(); Mark();
        };
        wrap.Children.Add(custom);
        Mark();
        host.Children.Add(wrap);
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
        bool first = p.Children.Count == 0;
        if (!first) p.Children.Add(new Border
        {
            Height = 1, Margin = new Thickness(0, 12, 0, 0),
            Background = new SolidColorBrush(Color.FromArgb(0x14, 255, 255, 255))
        });
        p.Children.Add(new TextBlock
        {
            Text = title, Foreground = new SolidColorBrush(Color.FromArgb(0x80, 255, 255, 255)),
            FontSize = 11, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, first ? 0 : 10, 0, 8)
        });
    }

    Grid Row(Panel p, string label, out Grid host)
    {
        var g = new Grid { Margin = new Thickness(0, 0, 0, 10), Background = System.Windows.Media.Brushes.Transparent };
        if (_buildCat.Length > 0) _settingsIndex.Add(new SettingEntry(() => label, _buildCat, g, false));
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
