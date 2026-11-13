using System.Globalization;
using System.Media;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using Brushes = System.Windows.Media.Brushes;
using Button = System.Windows.Controls.Button;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;
using FontFamily = System.Windows.Media.FontFamily;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;

namespace Island;

/// <summary>
/// v2.10: кастомизация — форма, размер, цвет и свечение глаз; что показывать в свёрнутом острове
/// (время, дата, погода, батарея, процессор, память, таймер); формат часов; таймер и секундомер.
/// </summary>
public partial class MainWindow
{
    bool _modalOpen;          // открыт наш диалог — остров не сворачивается при потере фокуса
    double _idleW = 204;      // ширина свёрнутого острова без музыки (считается в RecalcIdleWidth)

    /// <summary>Показать диалог так, чтобы остров не свернулся, пока он открыт.</summary>
    T WithModal<T>(Func<T> show)
    {
        _modalOpen = true;
        try { return show(); }
        finally
        {
            _modalOpen = false;
            if (_state == IslandState.Expanded) Activate();   // чтобы клик мимо острова снова его сворачивал
        }
    }

    static Color ParseColor(string? hex, Color fallback)
    {
        try { return string.IsNullOrWhiteSpace(hex) ? fallback : (Color)ColorConverter.ConvertFromString(hex); }
        catch { return fallback; }
    }

    // ================================================================ глаза

    const string EllipseData = "M0,0.5 A0.5,0.5 0 1 1 1,0.5 A0.5,0.5 0 1 1 0,0.5 Z";

    /// <summary>Базовый размер (при 100 %) и контур глаза в единичном квадрате (Path растягивает его).</summary>
    static (double W, double H, string Data) EyeGeometry(string style) => style switch
    {
        "Round" => (12, 12, EllipseData),
        "Dots" => (7, 7, EllipseData),
        // большой глаз с двумя бликами-«дырочками»
        "Anime" => (10, 19, "F0 " + EllipseData +
                            " M0.14,0.3 A0.19,0.09 0 1 1 0.52,0.3 A0.19,0.09 0 1 1 0.14,0.3 Z" +
                            " M0.56,0.62 A0.1,0.05 0 1 1 0.76,0.62 A0.1,0.05 0 1 1 0.56,0.62 Z"),
        // «^ ^» — дужки
        "Arcs" => (12, 8, "M0,1 C0,-0.33 1,-0.33 1,1 L0.7,1 C0.7,0.3 0.3,0.3 0.3,1 Z"),
        // экранчик робота с пиксельным бликом
        "Robot" => (11, 14, "F0 M0.22,0 L0.78,0 Q1,0 1,0.18 L1,0.82 Q1,1 0.78,1 L0.22,1 Q0,1 0,0.82 L0,0.18 Q0,0 0.22,0 Z" +
                            " M0.2,0.16 L0.44,0.16 L0.44,0.34 L0.2,0.34 Z"),
        // миндалевидный кошачий глаз с вертикальным зрачком
        "Cat" => (10, 20, "F0 M0.5,0 C1.15,0.3 1.15,0.7 0.5,1 C-0.15,0.7 -0.15,0.3 0.5,0 Z" +
                          " M0.5,0.18 C0.61,0.4 0.61,0.6 0.5,0.82 C0.39,0.6 0.39,0.4 0.5,0.18 Z"),
        _ => (6, 20, EllipseData)
    };

    (double W, double H) EyeSize()
    {
        var (bw, bh, _) = EyeGeometry(_s.EyeStyle);
        double k = _s.EyeSize / 100.0;
        return (Math.Round(bw * k, 1), Math.Round(Math.Min(30, bh * k), 1));
    }

    double EyesWidth() => 2 * EyeSize().W + _s.EyeSpacing;

    /// <summary>Насколько глаза шире стандартных (два овала 6 px + 12 px между ними).</summary>
    double EyesExtraWidth() => Math.Max(0, EyesWidth() - 24);

    void ApplyEyeStyle()
    {
        if (EyeL == null || EyeR == null) return;
        var (w, h) = EyeSize();
        Geometry geo;
        try { geo = Geometry.Parse(EyeGeometry(_s.EyeStyle).Data); }
        catch { geo = Geometry.Parse(EllipseData); }
        geo.Freeze();
        var color = ParseColor(_s.EyeColor, Colors.White);
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        foreach (var eye in new[] { EyeL, EyeR })
        {
            eye.Data = geo;
            eye.Width = w;
            eye.Height = h;
            eye.Fill = brush;
        }
        EyeL.Margin = new Thickness(0, 0, _s.EyeSpacing, 0);
        Eyes.Effect = _s.EyeGlow > 0
            ? new DropShadowEffect
            {
                Color = color, ShadowDepth = 0, BlurRadius = 4 + _s.EyeGlow * 0.12,
                Opacity = Math.Clamp(0.3 + _s.EyeGlow / 100.0 * 0.7, 0, 1), RenderingBias = RenderingBias.Performance
            }
            : null;
        if (_ready) EnsureFx();
    }

    // ================================================================ часы и дата

    string FormatTime(DateTime t, bool seconds)
    {
        if (_s.Clock24h) return t.ToString(seconds ? "HH:mm:ss" : "HH:mm", CultureInfo.InvariantCulture);
        return t.ToString(seconds ? "h:mm:ss" : "h:mm", CultureInfo.InvariantCulture) + (t.Hour < 12 ? " AM" : " PM");
    }

    string FormatDate(DateTime t)
    {
        try
        {
            bool ru = _s.Language == "ru";
            var ci = CultureInfo.GetCultureInfo(ru ? "ru-RU" : "en-US");
            string s = t.ToString(ru ? "ddd, d MMM" : "ddd, MMM d", ci).Replace(".", "");
            return s.Length > 0 ? char.ToUpper(s[0], ci) + s[1..] : s;
        }
        catch { return t.ToString("dd.MM", CultureInfo.InvariantCulture); }
    }

    static string FormatSpan(TimeSpan ts)
    {
        if (ts < TimeSpan.Zero) ts = TimeSpan.Zero;
        return ts.TotalHours >= 1
            ? $"{(int)ts.TotalHours}:{ts.Minutes:00}:{ts.Seconds:00}"
            : $"{ts.Minutes:00}:{ts.Seconds:00}";
    }

    string CenterText(DateTime now) => _s.MiniCenter switch
    {
        "Date" => FormatDate(now),
        "TimeDate" => FormatDate(now) + "  " + FormatTime(now, _s.ClockSeconds),
        "None" => "",
        _ => FormatTime(now, _s.ClockSeconds)
    };

    /// <summary>Нужен ли тик часов раз в секунду (секунды, таймер, процессор/память/батарея).</summary>
    bool NeedsSecondTicks() =>
        TimerActive || StopwatchActive ||
        _s.MiniRight is "Cpu" or "Ram" or "Battery" ||
        (_s.ClockSeconds && (_s.MiniCenter is "Time" or "TimeDate" || _s.MiniRight == "Time"));

    // ================================================================ свёрнутый вид

    /// <summary>Что сейчас показывать справа: запущенный таймер/секундомер важнее выбранного виджета.</summary>
    string RightKind() => TimerActive ? "Timer" : StopwatchActive ? "Stopwatch" : _s.MiniRight;

    void UpdateMiniWidgets(DateTime now, bool hasMedia)
    {
        CheckTimerFinished(now);

        string center = CenterText(now);
        TimeText.Text = center;
        if (center.Length == 0) TimeText.Visibility = Visibility.Collapsed;

        string kind = RightKind();
        bool info = !hasMedia && kind is not ("Weather" or "None");
        WeatherDetails.Visibility = !hasMedia && kind == "Weather" ? Visibility.Visible : Visibility.Collapsed;
        InfoPanel.Visibility = info ? Visibility.Visible : Visibility.Collapsed;
        if (info) FillInfo(kind, now);

        UpdateTimerTiles();
        RefreshIslandSize();
    }

    void FillInfo(string kind, DateTime now)
    {
        BatteryGlyph.Visibility = kind == "Battery" ? Visibility.Visible : Visibility.Collapsed;
        InfoIcon.Visibility = kind is "Timer" or "Stopwatch" ? Visibility.Visible : Visibility.Collapsed;
        InfoText.Foreground = (System.Windows.Media.Brush)FindResource("Dim");
        InfoPanel.ToolTip = null;
        switch (kind)
        {
            case "Battery": FillBattery(); break;
            case "Cpu":
                InfoText.Text = $"CPU {CpuPercent()}%";
                InfoPanel.ToolTip = Tr("Загрузка процессора", "Processor load");
                break;
            case "Ram":
                var (load, used, total) = RamInfo();
                InfoText.Text = $"{Tr("ОЗУ", "RAM")} {load}%";
                InfoPanel.ToolTip = total > 0 ? Tr($"Занято {used:0.0} из {total:0.0} ГБ", $"{used:0.0} of {total:0.0} GB used") : null;
                break;
            case "Time": InfoText.Text = FormatTime(now, _s.ClockSeconds); break;
            case "Date": InfoText.Text = FormatDate(now); break;
            case "Timer":
                InfoIcon.Text = "\uE823";
                InfoText.Text = FormatSpan(TimerLeft(now)) + (_timerPaused ? " ❚❚" : "");
                InfoText.Foreground = Brushes.White;
                InfoPanel.ToolTip = Tr("Таймер — управление на главной вкладке", "Timer — controls are on the Home tab");
                break;
            case "Stopwatch":
                InfoIcon.Text = "\uE916";
                InfoText.Text = FormatSpan(StopwatchElapsed(now)) + (_swStart == null ? " ❚❚" : "");
                InfoText.Foreground = Brushes.White;
                InfoPanel.ToolTip = Tr("Секундомер", "Stopwatch");
                break;
        }
    }

    void FillBattery()
    {
        try
        {
            var ps = System.Windows.Forms.SystemInformation.PowerStatus;
            bool noBattery = ps.BatteryChargeStatus.HasFlag(System.Windows.Forms.BatteryChargeStatus.NoSystemBattery)
                             || ps.BatteryLifePercent > 1.0f;
            bool plugged = ps.PowerLineStatus == System.Windows.Forms.PowerLineStatus.Online;
            BatteryBolt.Visibility = plugged ? Visibility.Visible : Visibility.Collapsed;
            if (noBattery)
            {
                BatteryLevel.Width = 0;
                InfoText.Text = Tr("сеть", "AC");
                InfoPanel.ToolTip = Tr("Батарея не найдена — питание от сети", "No battery — running on AC power");
                return;
            }
            double pct = Math.Clamp(ps.BatteryLifePercent, 0, 1);
            BatteryLevel.Width = Math.Max(1.2, 12.6 * pct);
            BatteryLevel.Background = new SolidColorBrush(plugged || pct > 0.2 ? Color.FromRgb(0x30, 0xD1, 0x58)
                                                         : pct > 0.1 ? Color.FromRgb(0xFF, 0xD6, 0x0A) : Color.FromRgb(0xFF, 0x45, 0x3A));
            InfoText.Text = $"{Math.Round(pct * 100)}%";
            InfoPanel.ToolTip = plugged ? Tr("Заряжается", "Charging")
                : ps.BatteryLifeRemaining > 0 ? Tr($"Осталось ≈ {ps.BatteryLifeRemaining / 3600} ч {ps.BatteryLifeRemaining / 60 % 60} мин",
                                                   $"≈ {ps.BatteryLifeRemaining / 3600} h {ps.BatteryLifeRemaining / 60 % 60} min left")
                : null;
        }
        catch (Exception ex)
        {
            Log.Warn("Батарея: " + ex.Message);
            InfoText.Text = "—";
        }
    }

    // ---- процессор и память без PerformanceCounter: дёшево и не требует прав

    [DllImport("kernel32.dll", SetLastError = true)] static extern bool GetSystemTimes(out long idle, out long kernel, out long user);

    [StructLayout(LayoutKind.Sequential)]
    struct MEMORYSTATUSEX
    {
        public uint dwLength, dwMemoryLoad;
        public ulong ullTotalPhys, ullAvailPhys, ullTotalPageFile, ullAvailPageFile, ullTotalVirtual, ullAvailVirtual, ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)] static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX m);

    long _cpuIdle, _cpuTotal;
    int _cpuPct;
    bool _cpuPrimed;

    int CpuPercent()
    {
        try
        {
            if (!GetSystemTimes(out long idle, out long kernel, out long user)) return _cpuPct;
            long total = kernel + user;                 // kernel уже включает время простоя
            long dIdle = idle - _cpuIdle, dTotal = total - _cpuTotal;
            _cpuIdle = idle; _cpuTotal = total;
            if (_cpuPrimed && dTotal > 0)
            {
                int raw = (int)Math.Round(100.0 * (dTotal - dIdle) / dTotal);
                _cpuPct = Math.Clamp((int)Math.Round(_cpuPct * 0.35 + raw * 0.65), 0, 100);   // лёгкое сглаживание, чтобы цифры не скакали
            }
            _cpuPrimed = true;
        }
        catch { }
        return _cpuPct;
    }

    static (int Load, double UsedGb, double TotalGb) RamInfo()
    {
        try
        {
            var m = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
            if (!GlobalMemoryStatusEx(ref m)) return (0, 0, 0);
            double total = m.ullTotalPhys / 1073741824.0, used = (m.ullTotalPhys - m.ullAvailPhys) / 1073741824.0;
            return ((int)m.dwMemoryLoad, used, total);
        }
        catch { return (0, 0, 0); }
    }

    // ---- ширина свёрнутого острова под выбранные виджеты

    double TextW(string text, double size)
    {
        if (string.IsNullOrEmpty(text)) return 0;
        try
        {
            var ft = new FormattedText(text, CultureInfo.CurrentUICulture, System.Windows.FlowDirection.LeftToRight,
                new Typeface(TimeText.FontFamily, FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal),
                size, Brushes.White, VisualTreeHelper.GetDpi(this).PixelsPerDip);
            return ft.WidthIncludingTrailingWhitespace;
        }
        catch { return text.Length * size * 0.58; }
    }

    static string Zeros(string s) => new(s.Select(c => char.IsDigit(c) ? '0' : c).ToArray());

    void RecalcIdleWidth()
    {
        var now = DateTime.Now;
        double left = 14 + EyesWidth();
        double center = TextW(Zeros(CenterText(now)), 14);
        double right = RightKind() switch
        {
            "Weather" => 8 + 20 + 3 + TextW("-00°", 12),
            "Battery" => 8 + 24 + TextW("100%", 12),
            "Cpu" => 8 + TextW("CPU 100%", 12),
            "Ram" => 8 + TextW(Tr("ОЗУ", "RAM") + " 100%", 12),
            "Time" => 8 + TextW(Zeros(FormatTime(now, _s.ClockSeconds)), 12),
            "Date" => 8 + TextW(FormatDate(now), 12),
            "Timer" => 8 + 16 + TextW(Zeros(FormatSpan(TimerLeft(now))) + (_timerPaused ? " ❚❚" : ""), 12),
            "Stopwatch" => 8 + 16 + TextW(Zeros(FormatSpan(StopwatchElapsed(now))) + (_swStart == null ? " ❚❚" : ""), 12),
            _ => 0
        };
        if (right > 0) right += 9;
        double w = 2 * Math.Max(left, Math.Max(right, 24)) + center + (center > 0 ? 32 : 12);
        _idleW = Math.Round(Math.Clamp(w, 96, 420));
    }

    /// <summary>Пересчитать ширину и, если остров свёрнут, плавно подогнать его.</summary>
    void RefreshIslandSize()
    {
        RecalcIdleWidth();
        if (_welcomeRunning || _state != IslandState.Collapsed || !_ready) return;
        double target = _now != null ? PlayW : IdleW;
        if (Math.Abs(_pw.Target - target) < 0.5) return;   // уже там или едем туда — не трогаем пружины
        Animate(target, MiniH, MiniR, spring: false);
    }

    // ================================================================ таймер и секундомер

    DateTime? _timerEnd;
    TimeSpan _timerLeft;
    bool _timerPaused;
    DateTime? _swStart;
    TimeSpan _swAccum;
    TextBlock? _timerLabel, _swLabel;
    Button? _timerTile, _swTile;

    bool TimerActive => _timerEnd != null || _timerPaused;
    bool StopwatchActive => _swStart != null || _swAccum > TimeSpan.Zero;

    TimeSpan TimerLeft(DateTime now)
    {
        var left = _timerEnd is DateTime end ? end - now : _timerPaused ? _timerLeft : TimeSpan.FromMinutes(_s.TimerMinutes);
        return TimeSpan.FromSeconds(Math.Max(0, Math.Ceiling(left.TotalSeconds)));
    }

    TimeSpan StopwatchElapsed(DateTime now) => _swAccum + (_swStart is DateTime s ? now - s : TimeSpan.Zero);

    void AddTimerTiles(Style style, FontFamily icons)
    {
        _timerTile = MakeTile(style, icons, "\uE823", out _timerLabel);
        _timerTile.Click += (_, _) => ToggleTimer();
        _timerTile.MouseWheel += TimerTile_MouseWheel;
        _timerTile.ContextMenu = new ContextMenu();
        _timerTile.ContextMenuOpening += (_, _) => BuildTimerMenu(_timerTile.ContextMenu);
        TileGrid.Children.Add(_timerTile);

        _swTile = MakeTile(style, icons, "\uE916", out _swLabel);
        _swTile.Click += (_, _) => ToggleStopwatch();
        _swTile.ContextMenu = new ContextMenu();
        _swTile.ContextMenuOpening += (_, _) =>
        {
            var m = _swTile.ContextMenu!;
            m.Items.Clear();
            var reset = new MenuItem { Header = Tr("Сбросить", "Reset"), IsEnabled = StopwatchActive };
            reset.Click += (_, _) => { _swStart = null; _swAccum = TimeSpan.Zero; TimersChanged(); };
            m.Items.Add(reset);
        };
        TileGrid.Children.Add(_swTile);

        // 8 плиток в 2 столбца — чуть ниже обычного, чтобы всё поместилось в раскрытый остров.
        foreach (var child in TileGrid.Children.OfType<Button>()) child.Height = 56;
        UpdateTimerTiles();
    }

    static Button MakeTile(Style style, FontFamily icons, string glyph, out TextBlock label)
    {
        var sp = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
        sp.Children.Add(new TextBlock { Text = glyph, FontFamily = icons, FontSize = 19, HorizontalAlignment = HorizontalAlignment.Center });
        label = new TextBlock { FontSize = 11, Margin = new Thickness(0, 5, 0, 0), HorizontalAlignment = HorizontalAlignment.Center };
        sp.Children.Add(label);
        return new Button { Style = style, Content = sp };
    }

    void BuildTimerMenu(ContextMenu? m)
    {
        if (m == null) return;
        m.Items.Clear();
        void Item(string text, Action act, bool enabled = true)
        {
            var mi = new MenuItem { Header = text, IsEnabled = enabled };
            mi.Click += (_, _) => act();
            m.Items.Add(mi);
        }
        Item(Tr("+1 минута", "+1 minute"), () => AddTimerMinutes(1));
        Item(Tr("+5 минут", "+5 minutes"), () => AddTimerMinutes(5));
        Item(Tr("Сбросить", "Reset"), () => { _timerEnd = null; _timerPaused = false; TimersChanged(); }, TimerActive);
        m.Items.Add(new Separator());
        var length = new MenuItem { Header = Tr("Длительность", "Duration") };
        foreach (int min in new[] { 1, 3, 5, 10, 15, 25, 30, 45, 60, 90 })
        {
            var mi = new MenuItem { Header = Tr($"{min} мин", $"{min} min"), IsCheckable = true, IsChecked = _s.TimerMinutes == min };
            int v = min;
            mi.Click += (_, _) => { _s.TimerMinutes = v; _s.Save(); TimersChanged(); };
            length.Items.Add(mi);
        }
        m.Items.Add(length);
    }

    void TimerTile_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        e.Handled = true;
        int step = (Keyboard.Modifiers & ModifierKeys.Shift) != 0 ? 5 : 1;
        int delta = e.Delta > 0 ? step : -step;
        if (TimerActive) { AddTimerMinutes(delta); return; }
        _s.TimerMinutes = Math.Clamp(_s.TimerMinutes + delta, 1, 180);
        _s.Save();
        TimersChanged();
    }

    void AddTimerMinutes(int minutes)
    {
        var now = DateTime.Now;
        if (_timerEnd is DateTime end) _timerEnd = end.AddMinutes(minutes) < now.AddSeconds(1) ? now.AddSeconds(1) : end.AddMinutes(minutes);
        else if (_timerPaused) _timerLeft = TimeSpan.FromTicks(Math.Max(TimeSpan.FromSeconds(1).Ticks, (_timerLeft + TimeSpan.FromMinutes(minutes)).Ticks));
        else _timerEnd = now.AddMinutes(Math.Max(1, minutes));
        TimersChanged();
    }

    void ToggleTimer()
    {
        var now = DateTime.Now;
        if (_timerEnd is DateTime end) { _timerLeft = end - now; _timerEnd = null; _timerPaused = true; }
        else if (_timerPaused) { _timerEnd = now + _timerLeft; _timerPaused = false; }
        else { _timerEnd = now.AddMinutes(_s.TimerMinutes); SetEyeMood(EyeMood.Focused, 1.4); }
        TimersChanged();
    }

    void ToggleStopwatch()
    {
        var now = DateTime.Now;
        if (_swStart is DateTime s) { _swAccum += now - s; _swStart = null; }
        else _swStart = now;
        TimersChanged();
    }

    void TimersChanged()
    {
        UpdateMini();
        ScheduleClock();
    }

    void CheckTimerFinished(DateTime now)
    {
        if (_timerEnd is not DateTime end || now < end) return;
        _timerEnd = null;
        _timerPaused = false;
        try { SystemSounds.Exclamation.Play(); } catch { }
        SetEyeMood(EyeMood.Surprised, 1.8);
        // UpdateMini уже идёт — тост показываем следующим шагом, чтобы не перестраивать вид изнутри обновления.
        Dispatcher.BeginInvoke(new Action(() =>
        {
            ShowToast(Tr("Таймер", "Timer"), Tr($"Время вышло — {_s.TimerMinutes} мин", $"Time's up — {_s.TimerMinutes} min"), ColorOk);
            ScheduleClock();
        }));
    }

    void UpdateTimerTiles()
    {
        if (_timerLabel == null || _swLabel == null) return;
        var now = DateTime.Now;
        _timerLabel.Text = TimerActive
            ? FormatSpan(TimerLeft(now)) + (_timerPaused ? Tr(" · пауза", " · paused") : "")
            : Tr($"Таймер · {_s.TimerMinutes} мин", $"Timer · {_s.TimerMinutes} min");
        _swLabel.Text = StopwatchActive
            ? FormatSpan(StopwatchElapsed(now)) + (_swStart == null ? Tr(" · пауза", " · paused") : "")
            : Tr("Секундомер", "Stopwatch");
        if (_timerTile != null)
            _timerTile.ToolTip = Tr("Клик — старт/пауза, колесо — минуты (Shift — по 5), ПКМ — ещё",
                                    "Click — start/pause, wheel — minutes (Shift — by 5), right-click — more");
        if (_swTile != null) _swTile.ToolTip = Tr("Клик — старт/пауза, ПКМ — сброс", "Click — start/pause, right-click — reset");
    }
}
