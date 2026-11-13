using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Shapes = System.Windows.Shapes;
using Point = System.Windows.Point;

namespace MokuInstaller;

/// <summary>Настроение глазок установщика.</summary>
enum Mood { Neutral, Reading, Happy, Excited, Curious, Focused, Surprised, Scared, Sad, Bye }

/// <summary>
/// «Живые» глазки установщика и удаления: эмоции на каждое действие, перелёты с прыжком,
/// моргание и подмигивание, всплывающие подсказки-«пузыри», искры, слёзы, кивки,
/// подталкивание кнопки и мягкое появление/исчезновение окна. Всё — на пружинах,
/// поэтому любую анимацию можно перебить новой без рывка.
/// </summary>
public partial class MainWindow
{
    sealed class Eye
    {
        public int Side;                       // −1 левый, +1 правый
        public double Stiff = 1, Delay;        // правый чуть «тяжелее» и моргает на долю секунды позже
        public Spring SX = new(1), SY = new(1), Rot = new(0), Lift = new(0);
        public ScaleTransform Scale = null!;
        public RotateTransform Rotate = null!;
        public TranslateTransform Shift = null!;
    }

    sealed class Particle
    {
        public FrameworkElement El = null!;
        public RotateTransform Turn = new();
        public double X, Y, VX, VY, Age, Life, Spin, Gravity;
    }

    sealed class PopAnim { public ScaleTransform S = new(1, 1); public Spring V = new(1); }

    readonly record struct EyeShape(double SX, double SY, double Tilt, double Lift);

    static EyeShape ShapeOf(Mood m, int side) => m switch
    {
        Mood.Reading => new(1.05, 0.70, 0, 0.6),                       // прищур «читаю»
        Mood.Happy => new(1.30, 0.45, 14, -1.0),                       // «^ ^»
        Mood.Excited => new(1.16, 1.18, 0, -1.2),                      // распахнуты и подпрыгивают
        Mood.Curious => side < 0 ? new(1.0, 1.18, 0, -0.8)             // одна «бровь» выше,
                                 : new(1.0, 0.84, 0, 0.5),             // голова чуть набок
        Mood.Focused => new(0.90, 0.72, -10, 0.2),                     // сосредоточенный «\ /»
        Mood.Surprised => new(1.20, 1.32, 0, -1.6),
        Mood.Scared => new(0.84, 1.28, 0, -0.6),                       // + дрожь
        Mood.Sad => new(1.02, 0.70, 9, 2.0),                           // опущены, смотрят вниз
        Mood.Bye => new(1.12, 0.32, 10, 1.4),                          // устало прикрываются
        _ => new(1.0, 1.0, 0, 0)
    };

    static readonly Geometry StarGeometry = MakeStar();
    static Geometry MakeStar()
    {
        var g = Geometry.Parse("M0,-4.6 Q0.7,-0.7 4.6,0 Q0.7,0.7 0,4.6 Q-0.7,0.7 -4.6,0 Q-0.7,-0.7 0,-4.6 Z");
        g.Freeze();
        return g;
    }

    static readonly Color[] SparkColors =
    {
        Color.FromRgb(255, 255, 255), Color.FromRgb(255, 226, 140), Color.FromRgb(176, 220, 255), Color.FromRgb(255, 178, 214)
    };

    Eye _eyeL = null!, _eyeR = null!;
    Spring _posX, _posY, _hopY, _gazeX, _gazeY, _nod, _bubbleS, _hintY, _headY, _rootS = new(0.9);
    double _t, _shake, _blinkT = -1, _winkT = -1, _nextBlinkAt = 2.6, _nextGlanceAt, _glanceX, _glanceY;
    double _moodUntil, _bubbleUntil, _pageSince, _nextNudgeAt = 6, _hintFade = 1, _headFade = 1, _sparkFade;
    Mood _tempMood;
    bool _hasTemp, _eyesInit, _closing, _nudged, _licenseRead, _folderEmptySaid;
    string _lastHint = "", _lastHead = "";
    Point _spark;
    DateTime _last = DateTime.Now, _lastMove = DateTime.MinValue;
    readonly Random _rng = new();
    readonly List<Particle> _particles = new();
    readonly Dictionary<UIElement, PopAnim> _pops = new();
    readonly TranslateTransform _hintShift = new(), _headShift = new();
    readonly ScaleTransform _rootScale = new(0.9, 0.9);

    IEnumerable<Eye> BothEyes() { yield return _eyeL; yield return _eyeR; }

    // ================================================================ настройка

    void InitMood()
    {
        _eyeL = new Eye { Side = -1, Scale = EyeLScale, Rotate = EyeLRot, Shift = EyeLShift };
        _eyeR = new Eye { Side = 1, Stiff = 0.88, Delay = 0.035, Scale = EyeRScale, Rotate = EyeRRot, Shift = EyeRShift };
        foreach (var e in BothEyes()) { e.SY.Reset(0.08); e.SY.Target = 1; }   // глаза «раскрываются» при старте

        HintText.RenderTransform = _hintShift;
        Headline.RenderTransform = _headShift;
        Root.RenderTransformOrigin = new Point(0.5, 0.5);
        Root.RenderTransform = _rootScale;
        Root.Opacity = 0;
        _rootS.Target = 1;

        // Реакции на галочки и переключатели — у каждой своя эмоция и фраза.
        DesktopCheck.Checked += (_, _) => Toggle(DesktopCheck, true);
        DesktopCheck.Unchecked += (_, _) => Toggle(DesktopCheck, false);
        AutoCheck.Checked += (_, _) => Toggle(AutoCheck, true);
        AutoCheck.Unchecked += (_, _) => Toggle(AutoCheck, false);
        KeepSettingsCheck.Checked += (_, _) => Toggle(KeepSettingsCheck, true);
        KeepSettingsCheck.Unchecked += (_, _) => Toggle(KeepSettingsCheck, false);
        LaunchCheck.Checked += (_, _) => Toggle(LaunchCheck, true);
        LaunchCheck.Unchecked += (_, _) => Toggle(LaunchCheck, false);
        AppLanguageRussian.Checked += (_, _) => { if (IsLoaded) { Wink(); Feel(Mood.Happy, 0.8); } };
        AppLanguageEnglish.Checked += (_, _) => { if (IsLoaded) { Wink(); Feel(Mood.Happy, 0.8); } };

        NextButton.MouseEnter += (_, _) =>
        {
            if (!NextButton.IsEnabled || _installing) return;
            PopEl(NextButton, 1.4);
            Feel(_uninstallMode && _page < 2 ? Mood.Sad : Mood.Happy, 0.8);
        };
        BackButton.Click += (_, _) => Feel(Mood.Curious, 1.2);
        CloseButton.MouseEnter += (_, _) => { if (!_installing && !_done) Feel(Mood.Surprised, 1.0); };
        LicenseBox.AddHandler(ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler(License_Scroll));
    }

    /// <summary>Приветствие после показа окна.</summary>
    void GreetOnLoad()
    {
        if (_uninstallMode)
        {
            After(380, () => { Feel(Mood.Sad, 2.4); Say(T("Уже уходишь?", "Leaving already?"), 2.6); });
        }
        else
        {
            After(380, () => { Feel(Mood.Excited, 1.2); Say(T("Привет! Я Moku", "Hi! I'm Moku"), 2.2); });
            After(3000, () =>
            {
                if (_page == 0 && AgreeCheck.IsChecked != true)
                    Say(T("Прочитай и отметь галочку ↓", "Read it and tick the box ↓"), 3.0);
            });
        }
    }

    // ================================================================ действия → эмоции

    void Feel(Mood m, double seconds = 1.6)
    {
        bool changed = !_hasTemp || _tempMood != m;
        _tempMood = m; _hasTemp = true; _moodUntil = _t + seconds;
        if (changed) foreach (var e in BothEyes()) { e.SY.Velocity += 3.2; e.SX.Velocity -= 1.6; }   // «толчок» смены эмоции
    }

    Mood BaseMood()
    {
        if (_closing) return Mood.Surprised;
        return _page switch
        {
            0 => _uninstallMode ? Mood.Sad : AgreeCheck.IsChecked == true ? Mood.Neutral : Mood.Reading,
            1 => Mood.Neutral,
            2 => Mood.Focused,
            _ => _uninstallMode ? Mood.Bye : Mood.Happy
        };
    }

    Mood CurrentMood()
    {
        if (_hasTemp && _t < _moodUntil) return _tempMood;
        _hasTemp = false;
        return BaseMood();
    }

    void OnPageEntered(int p)
    {
        _pageSince = _t; _nudged = false; _nextNudgeAt = _t + 6;
        var steps = new[] { Step0, Step1, Step2, Step3 };
        if (p > 0) PopEl(steps[p - 1], 3.0, new Point(0, 0.5));   // «✓» у пройденного шага подпрыгивает
        PopEl(steps[p], 2.0, new Point(0, 0.5));
        switch (p)
        {
            case 0:
                Feel(Mood.Curious, 1.0);
                break;
            case 1:
                if (_uninstallMode) { Feel(Mood.Sad, 1.4); Say(T("Что убрать вместе со мной?", "What should go with me?"), 2.6); }
                else { Feel(Mood.Curious, 2.2); Say(T("Куда меня поселим?", "Where shall I live?"), 2.6); }
                break;
            case 2:
                Say(_uninstallMode ? T("Собираю вещи…", "Packing up…") : T("Распаковываюсь…", "Unpacking…"), 2.4);
                break;
            default:
                if (_uninstallMode)
                {
                    Feel(Mood.Sad, 3.0);
                    After(250, Tear); After(1100, Tear);
                    Say(T("Пока! Буду скучать", "Bye! I'll miss you"), 3.6);
                }
                else
                {
                    Feel(Mood.Excited, 3.2);
                    Burst(16); After(420, () => Burst(10)); After(900, () => Burst(8));
                    Say(T("Ура! Всё готово", "Yay! All set"), 3.0);
                }
                break;
        }
    }

    void AgreeReaction(bool on)
    {
        PopEl(AgreeCheck, 2.6, new Point(0, 0.5));
        if (_uninstallMode)
        {
            if (on) { Feel(Mood.Sad, 2.4); Tear(); Say(T("Эх… ладно", "Oh… okay"), 2.0); }
            else { Feel(Mood.Happy, 1.8); Burst(7); Say(T("Ура, остаёмся!", "Yay, staying!"), 2.0); }
        }
        else
        {
            if (on) { Feel(Mood.Happy, 1.8); Burst(8); Say(T("Спасибо! Жми «Далее»", "Thanks! Press Next"), 2.2); }
            else { Feel(Mood.Surprised, 1.0); Shake(2.5); Say(T("Эй, без галочки дальше никак", "Hey, I need that tick"), 2.2); }
        }
    }

    void Toggle(CheckBox box, bool on)
    {
        if (!IsLoaded) return;
        PopEl(box, 2.4, new Point(0, 0.5));
        SetAnchor(box);
        Hop(1500, () => { if (_page == 1 && !_installing) SetAnchor(NextButton); });

        if (box == KeepSettingsCheck)
        {
            if (on) { Feel(Mood.Happy, 1.4); Say(T("Настройки сохраню", "I'll keep your settings"), 1.8); }
            else { Feel(Mood.Scared, 1.6); Shake(2.2); Say(T("Настройки тоже удалю!", "Settings will be deleted too!"), 2.4); }
        }
        else if (box == LaunchCheck)
        {
            if (on) { Feel(Mood.Excited, 1.2); Say(T("Запущусь сразу!", "I'll start right away!"), 1.8); }
            else { Feel(Mood.Sad, 1.0); Say(T("Ладно, подожду", "Okay, I'll wait"), 1.6); }
        }
        else if (_uninstallMode)
        {
            if (on) { Feel(Mood.Sad, 1.0); Say(T("Уберу", "Will remove"), 1.4); }
            else { Feel(Mood.Curious, 1.0); Say(T("Оставлю", "Will keep"), 1.4); }
        }
        else if (box == DesktopCheck)
        {
            if (on) { Feel(Mood.Happy, 1.3); Say(T("Буду на рабочем столе", "See you on the desktop"), 1.8); }
            else { Feel(Mood.Surprised, 0.9); Say(T("Ладно, без ярлыка", "Okay, no shortcut"), 1.6); }
        }
        else if (box == AutoCheck)
        {
            if (on) { Feel(Mood.Happy, 1.3); Say(T("Буду встречать тебя при входе", "I'll greet you at sign-in"), 2.0); }
            else { Feel(Mood.Sad, 1.2); Say(T("Хорошо, позовёшь сам", "Okay, call me when needed"), 1.8); }
        }
    }

    void FolderReaction()
    {
        if (string.IsNullOrWhiteSpace(FolderBox.Text))
        {
            if (!_folderEmptySaid) { Feel(Mood.Sad, 1.4); Say(T("Без папки никак", "I need a folder"), 1.8); }
            _folderEmptySaid = true;
            return;
        }
        _folderEmptySaid = false;
        if (FolderBox.IsKeyboardFocused) Feel(Mood.Reading, 0.9);   // «читает», что ты печатаешь
    }

    void BrowseStarted() { Feel(Mood.Curious, 2.0); _gazeY.Velocity -= 30; }

    void BrowseFinished(bool chosen)
    {
        if (!chosen) { Feel(Mood.Neutral, 0.4); return; }
        PopEl(FolderBox, 1.2);
        Feel(Mood.Happy, 1.6); Burst(6);
        Say(T("Уютное место!", "Cozy spot!"), 1.8);
    }

    void License_Scroll(object sender, ScrollChangedEventArgs e)
    {
        if (_page != 0 || _uninstallMode || !IsLoaded) return;
        if (Math.Abs(e.VerticalChange) > 0.1) Feel(Mood.Reading, 1.4);
        if (!_licenseRead && e.ExtentHeight > e.ViewportHeight + 1 && e.VerticalOffset + e.ViewportHeight >= e.ExtentHeight - 2)
        {
            _licenseRead = true;
            Feel(Mood.Happy, 1.2);
            Say(T("Всё прочитано — отлично!", "All read — nice!"), 1.8);
        }
    }

    /// <summary>Строка статуса установки/удаления: кивок и «подпрыгивание» текста.</summary>
    void Status(string text)
    {
        StatusText.Text = text;
        PopEl(StatusText, 1.6, new Point(0, 0.5));
        Nod();
    }

    void OnError()
    {
        Feel(Mood.Scared, 1.4);
        Shake(4.5);
        Say(T("Ой… что-то пошло не так", "Oops… something went wrong"), 3.0);
        After(1400, () => Feel(Mood.Sad, 2.6));
    }

    /// <summary>Закрыть окно с коротким «сжатием» вместо резкого исчезновения.</summary>
    void BeginClose()
    {
        if (_closing) return;
        _closing = true;
        _rootS.Target = 0.9;
        _bubbleUntil = 0;
        After(230, Close);
    }

    void RefuseClose()
    {
        Shake(4);
        Feel(Mood.Focused, 1.2);
        Say(T("Подожди, ещё чуть-чуть", "Hold on, almost done"), 1.8);
    }

    // ================================================================ эффекты

    void Say(string text, double seconds = 2.2)
    {
        if (_closing) return;
        bool wasShown = _t < _bubbleUntil && _bubbleS.Value > 0.5;
        BubbleText.Text = text;
        _bubbleUntil = _t + seconds;
        if (wasShown) _bubbleS.Velocity += 2.5;      // смена текста — лёгкий «пульс»
        else { _bubbleS.Value = Math.Max(_bubbleS.Value, 0.15); _bubbleS.Velocity += 4; }
    }

    void Shake(double amount) => _shake = Math.Max(_shake, amount);
    void Nod() => _nod.Velocity += 55;
    void Wink() { if (_winkT < 0) _winkT = 0; }

    void PopEl(FrameworkElement el, double kick, Point? origin = null)
    {
        if (!_pops.TryGetValue(el, out var p))
        {
            p = new PopAnim();
            el.RenderTransformOrigin = origin ?? new Point(0.5, 0.5);
            el.RenderTransform = p.S;
            _pops[el] = p;
        }
        p.V.Velocity += kick;
    }

    void After(int ms, Action a)
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(ms) };
        timer.Tick += (_, _) => { timer.Stop(); a(); };
        timer.Start();
    }

    Point EyesCenter() => new(Canvas.GetLeft(Eyes) + 9.5, Canvas.GetTop(Eyes) + 8);

    void Burst(int count)
    {
        if (!_eyesInit) return;
        var c = EyesCenter();
        for (int i = 0; i < count; i++)
        {
            double a = -Math.PI / 2 + (_rng.NextDouble() * 2 - 1) * 1.35;
            double v = 70 + _rng.NextDouble() * 90;
            var p = new Particle
            {
                X = c.X, Y = c.Y, VX = Math.Cos(a) * v, VY = Math.Sin(a) * v,
                Life = 0.8 + _rng.NextDouble() * 0.5, Spin = (_rng.NextDouble() * 2 - 1) * 360, Gravity = 150
            };
            double size = 0.55 + _rng.NextDouble() * 0.6;
            p.El = new Shapes.Path
            {
                Data = StarGeometry,
                Fill = new SolidColorBrush(SparkColors[_rng.Next(SparkColors.Length)]),
                IsHitTestVisible = false,
                RenderTransform = new TransformGroup { Children = { new ScaleTransform(size, size), p.Turn } }
            };
            AddParticle(p);
        }
    }

    void Tear()
    {
        if (!_eyesInit) return;
        var c = EyesCenter();
        var p = new Particle { X = c.X - 7, Y = c.Y + 7, VX = -4, VY = 8, Life = 1.3, Gravity = 60 };
        p.El = new Shapes.Ellipse
        {
            Width = 3.2, Height = 5, Margin = new Thickness(-1.6, -2.5, 0, 0),
            Fill = new SolidColorBrush(Color.FromRgb(158, 210, 255)), IsHitTestVisible = false,
            RenderTransform = p.Turn
        };
        AddParticle(p);
    }

    void AddParticle(Particle p)
    {
        Canvas.SetLeft(p.El, p.X); Canvas.SetTop(p.El, p.Y);
        EyeLayer.Children.Add(p.El);
        _particles.Add(p);
    }

    void StepParticles(double dt)
    {
        for (int i = _particles.Count - 1; i >= 0; i--)
        {
            var p = _particles[i];
            p.Age += dt;
            if (p.Age >= p.Life || _closing) { EyeLayer.Children.Remove(p.El); _particles.RemoveAt(i); continue; }
            p.VY += p.Gravity * dt;
            double drag = Math.Exp(-1.8 * dt);
            p.VX *= drag;
            p.X += p.VX * dt; p.Y += p.VY * dt;
            p.Turn.Angle += p.Spin * dt;
            double k = p.Age / p.Life;
            p.El.Opacity = 1 - k * k;
            Canvas.SetLeft(p.El, p.X); Canvas.SetTop(p.El, p.Y);
        }
    }

    // ================================================================ кадр

    void Frame(object? s, EventArgs e)
    {
        var now = DateTime.Now;
        double dt = Math.Clamp((now - _last).TotalSeconds, 0.001, 0.05); _last = now;
        _t += dt;

        StepWindow(dt);
        StepTexts(dt);
        StepPops(dt);
        StepSpark(dt);
        StepParticles(dt);

        if (_anchor != null && _anchor.IsVisible && _anchor.IsLoaded)
        {
            try
            {
                var p = _anchor.TransformToVisual(EyeLayer).Transform(new Point(0, _anchor.ActualHeight / 2));
                double tx = p.X - 30, ty = p.Y - 8;
                if (!_eyesInit) { _posX.Reset(tx); _posY.Reset(ty); _eyesInit = true; }
                else
                {
                    double d = Math.Sqrt((tx - _posX.Target) * (tx - _posX.Target) + (ty - _posY.Target) * (ty - _posY.Target));
                    if (d > 24) _hopY.Velocity -= Math.Min(240, 70 + d * 0.8);   // перелёт к новому пункту — прыжком по дуге
                    _posX.Target = tx; _posY.Target = ty;
                }
            }
            catch (InvalidOperationException) { }
        }
        if (!_eyesInit) return;

        UpdateEyes(dt, now);
        StepBubble(dt);
        StepNudge(now);
    }

    void UpdateEyes(double dt, DateTime now)
    {
        var mood = CurrentMood();

        _posX.Step(dt, 95, 0.9); _posY.Step(dt, 95, 0.9);
        _hopY.Step(dt, 150, 0.5);
        _nod.Step(dt, 320, 0.42);
        _shake *= Math.Exp(-6 * dt);

        double ex = _posX.Value, ey = _posY.Value;

        // ---- взгляд ----
        double gtx, gty;
        bool mouseActive = (now - _lastMove).TotalSeconds < 2.5;
        if (mood == Mood.Reading && !mouseActive)
        {
            double line = _t * 0.6 % 1.0;                           // «строка»: слева направо и быстрый возврат
            gtx = -2.3 + 4.6 * line;
            gty = 0.5 + 0.5 * Math.Sin(_t * 0.37);
        }
        else if (mood == Mood.Focused && _page == 2 && _sparkFade > 0.2)
        {
            gtx = Math.Clamp((_spark.X - (ex + 9.5)) / 40, -2.6, 2.6);   // следит за бегущей искрой прогресса
            gty = Math.Clamp((_spark.Y - (ey + 8)) / 40, -1.6, 1.6);
        }
        else if (mood is Mood.Sad or Mood.Bye)
        {
            gtx = Math.Sin(_t * 0.5) * 0.6;
            gty = 1.5;
        }
        else if (mouseActive)
        {
            var m = Mouse.GetPosition(EyeLayer);
            gtx = Math.Clamp((m.X - ex) / 60, -2.6, 2.6);
            gty = Math.Clamp((m.Y - ey) / 60, -1.6, 1.6);
        }
        else
        {
            if (_t >= _nextGlanceAt)
            {
                _glanceX = (_rng.NextDouble() * 2 - 1) * 2.4; _glanceY = (_rng.NextDouble() * 2 - 1) * 1.3;
                _nextGlanceAt = _t + 1.3 + _rng.NextDouble() * 2.7;
            }
            gtx = _glanceX; gty = _glanceY;
        }
        if (mood == Mood.Curious) gty -= 0.6;
        _gazeX.Target = gtx; _gazeY.Target = gty;
        _gazeX.Step(dt, 200, 0.62); _gazeY.Step(dt, 200, 0.62);

        // ---- характер движения ----
        double offX = _shake * Math.Sin(_t * 46);
        double offY = 0;
        if (mood == Mood.Excited) offY -= Math.Abs(Math.Sin(_t * 8)) * 2.4;      // подпрыгивает от радости
        if (mood == Mood.Scared) offX += Math.Sin(_t * 63) * 0.5;                 // мелкая дрожь
        if (mood == Mood.Happy) offY += Math.Sin(_t * 4.2) * 0.5;

        Canvas.SetLeft(Eyes, ex + _gazeX.Value + offX);
        Canvas.SetTop(Eyes, ey + _hopY.Value + _gazeY.Value + _nod.Value + offY);

        // ---- моргание ----
        bool noBlink = mood is Mood.Surprised or Mood.Scared;
        if (_blinkT >= 0)
        {
            _blinkT += dt;
            if (_blinkT > 0.24)
            {
                _blinkT = -1;
                double gap = mood is Mood.Sad or Mood.Bye ? 1.6 + _rng.NextDouble() * 2 : 2.4 + _rng.NextDouble() * 3.4;
                _nextBlinkAt = _t + (_rng.NextDouble() < 0.18 ? 0.3 : gap);   // иногда двойное моргание
            }
        }
        else if (!noBlink && _t >= _nextBlinkAt) _blinkT = 0;
        if (_winkT >= 0) { _winkT += dt; if (_winkT > 0.45) _winkT = -1; }

        // ---- форма ----
        bool slow = mood is Mood.Sad or Mood.Bye;
        (double k, double z) = slow ? (70.0, 0.95) : (240.0, 0.42);
        double speed = Math.Sqrt(_posX.Velocity * _posX.Velocity + (_posY.Velocity + _hopY.Velocity) * (_posY.Velocity + _hopY.Velocity));
        double stretch = Math.Min(0.16, speed / 1600);
        double headTilt = mood == Mood.Curious ? 8 : 0;

        foreach (var eye in BothEyes())
        {
            var sh = ShapeOf(mood, eye.Side);
            eye.SX.Target = sh.SX; eye.SY.Target = sh.SY; eye.Lift.Target = sh.Lift;
            eye.Rot.Target = -eye.Side * sh.Tilt + headTilt;
            eye.SX.Step(dt, k * eye.Stiff, z); eye.SY.Step(dt, k * eye.Stiff, z);
            eye.Lift.Step(dt, k * eye.Stiff, Math.Max(z, 0.6));
            eye.Rot.Step(dt, 200 * eye.Stiff, 0.5);

            double lid = Lid(eye);
            eye.Scale.ScaleX = Math.Clamp(eye.SX.Value * (1 - stretch * 0.5) * (1 + (1 - lid) * 0.2), 0.5, 1.7);
            eye.Scale.ScaleY = Math.Clamp(eye.SY.Value * (1 + stretch) * lid, 0.05, 1.5);
            eye.Rotate.Angle = eye.Rot.Value;
            eye.Shift.Y = eye.Lift.Value;
        }
    }

    double Lid(Eye eye)
    {
        double lid = 1;
        double tb = _blinkT - eye.Delay;
        if (_blinkT >= 0 && tb >= 0 && tb < 0.18) lid = 1 - 0.93 * Math.Sin(Math.PI * tb / 0.18);
        if (eye.Side < 0 && _winkT >= 0 && _winkT < 0.42) lid = Math.Min(lid, 1 - 0.95 * Math.Sin(Math.PI * _winkT / 0.42));
        return Math.Max(0.06, lid);
    }

    // ---- пузырь-подсказка рядом с глазками ----
    void StepBubble(double dt)
    {
        _bubbleS.Target = _t < _bubbleUntil && !_closing ? 1 : 0;
        _bubbleS.Step(dt, 340, 0.48);
        double sc = Math.Max(0, _bubbleS.Value);
        if (sc < 0.02 && _bubbleS.Target == 0) { Bubble.Visibility = Visibility.Collapsed; return; }
        Bubble.Visibility = Visibility.Visible;

        Bubble.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var size = Bubble.DesiredSize;
        double ex = Canvas.GetLeft(Eyes), ey = Canvas.GetTop(Eyes);
        double W = Root.Width, H = Root.Height;
        bool left = ex + 24 + size.Width > W - 8;
        double bx = left ? ex - size.Width - 6 : ex + 24;
        double by = ey - size.Height - 2;
        bool below = by < 6;
        if (below) by = ey + 20;
        bx = Math.Clamp(bx, 6, Math.Max(6, W - size.Width - 6));
        by = Math.Clamp(by, 6, Math.Max(6, H - size.Height - 6));

        Bubble.RenderTransformOrigin = new Point(left ? 1 : 0, below ? 0 : 1);   // «растёт» из угла у глазок
        Canvas.SetLeft(Bubble, bx);
        Canvas.SetTop(Bubble, by + Math.Sin(_t * 2.6) * 1.2);                    // лёгкое покачивание
        BubbleScale.ScaleX = BubbleScale.ScaleY = Math.Min(1.25, sc);
        BubbleRot.Angle = Math.Sin(_t * 3.1) * 1.6 * Math.Min(1, sc);
        Bubble.Opacity = Math.Clamp(sc * 1.6, 0, 1);
    }

    // ---- если пользователь задумался, кнопка «Далее» мягко подпрыгивает ----
    void StepNudge(DateTime now)
    {
        if (_closing || _installing || !NextButton.IsEnabled || _anchor != NextButton) return;
        if (_t < _nextNudgeAt || _t - _pageSince < 4 || (now - _lastMove).TotalSeconds < 4) return;
        _nextNudgeAt = _t + 3.4;
        PopEl(NextButton, 2.0);
        _hopY.Velocity -= 110;
        if (!_nudged) { _nudged = true; Say(_page == 3 ? T("Готово — жми!", "Done — click!") : T("Жми сюда!", "Click here!"), 2.0); }
    }

    void StepPops(double dt)
    {
        foreach (var p in _pops.Values)
        {
            p.V.Target = 1;
            p.V.Step(dt, 420, 0.32);
            double v = Math.Clamp(p.V.Value, 0.85, 1.18);
            p.S.ScaleX = p.S.ScaleY = v;
        }
    }

    // ---- подсказка и заголовок «въезжают» при смене текста ----
    void AnimateTexts()
    {
        if (HintText.Text != _lastHint)
        {
            _lastHint = HintText.Text;
            _hintY.Reset(7); _hintY.Target = 0; _hintFade = 0;
        }
        if (Headline.Text != _lastHead)
        {
            _lastHead = Headline.Text;
            _headY.Reset(-6); _headY.Target = 0; _headFade = 0;
        }
    }

    void StepTexts(double dt)
    {
        _hintY.Step(dt, 260, 0.72); _headY.Step(dt, 300, 0.6);
        _hintFade = Math.Min(1, _hintFade + dt * 4.5);
        _headFade = Math.Min(1, _headFade + dt * 5.5);
        _hintShift.Y = _hintY.Value; _headShift.Y = _headY.Value;
        HintText.Opacity = _hintFade * _hintFade * (3 - 2 * _hintFade);
        Headline.Opacity = _headFade * _headFade * (3 - 2 * _headFade);
    }

    // ---- искра на «голове» прогресса, за которой следят глаза ----
    void StepSpark(double dt)
    {
        bool show = _page == 2 && Progress.IsVisible && Progress.Value > 0.5;
        _sparkFade += ((show ? 1 : 0) - _sparkFade) * (1 - Math.Exp(-8 * dt));
        if (_sparkFade < 0.01) { Spark.Visibility = Visibility.Collapsed; return; }
        Spark.Visibility = Visibility.Visible;
        try
        {
            double f = (Progress.Value - Progress.Minimum) / Math.Max(1, Progress.Maximum - Progress.Minimum);
            var p = Progress.TransformToVisual(EyeLayer).Transform(new Point(Progress.ActualWidth * f, Progress.ActualHeight / 2));
            _spark = p;
            Canvas.SetLeft(Spark, p.X - Spark.Width / 2);
            Canvas.SetTop(Spark, p.Y - Spark.Height / 2);
        }
        catch (InvalidOperationException) { }
        Spark.Opacity = _sparkFade * (0.7 + 0.3 * Math.Sin(_t * 9));
    }

    // ---- окно появляется «пружинкой» и так же сжимается при закрытии ----
    void StepWindow(double dt)
    {
        _rootS.Step(dt, 260, _closing ? 0.9 : 0.62);
        double s = _rootS.Value;
        _rootScale.ScaleX = _rootScale.ScaleY = s;
        Root.Opacity = Math.Clamp((s - 0.9) / 0.1, 0, 1);
    }
}
