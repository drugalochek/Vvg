using System.Windows;
using System.Windows.Media;
using Point = System.Windows.Point;

namespace Island;

/// <summary>
/// «Живые» глаза на пружинах.
///  • Каждый параметр (позиция, ширина, высота, наклон, веко) — отдельная пружина: движения плавные,
///    с лёгким перелётом и «желейным» затуханием, без микродрожи.
///  • Взгляд: плавное слежение за курсором + быстрые заметные «саккады» при резкой смене цели.
///  • Squash &amp; stretch: при быстром движении глаз вытягивается по направлению движения и наклоняется.
///  • Моргание: веко быстро закрывается и открывается с упругим «пружинистым» отскоком,
///    правый глаз — на долю секунды позже, иногда двойное моргание.
///  • Эмоции меняют форму и поведение и включаются с «толчком», чтобы переход был заметен.
///  • Время эмоции тикает только пока глаза видны — тост больше не «съедает» эмоцию.
///  • v2.8: «танец» под музыку по темпу трека; умиление с румянцем, когда глаза «гладят» курсором;
///    любопытство при наведении, скука и зевота без дела, грусть при ошибке. Испуга от резкой мыши больше нет.
/// </summary>
public partial class MainWindow
{
    sealed class EyeRig
    {
        public Spring X, Y, Rot;
        public Spring SX = new(1), SY = new(1), Lid = new(1);
        public double Stiff = 1;          // правый глаз чуть «тяжелее» — лёгкое запаздывание, как у живого
        public int Side;                  // -1 левый, +1 правый
        public int BlinkPhase;            // 0 — открыт, 1 — закрывается, 2 — закрыт, 3 — открывается
        public double PhaseAt, BlinkHold;
        public ScaleTransform Scale = null!;
        public RotateTransform Rotate = null!;
        public TranslateTransform Shift = null!;
        public FrameworkElement Element = null!;
    }

    readonly record struct EyeShape(double SX, double SY, double Tilt, double Lift);

    static EyeShape ShapeOf(EyeMood m) => m switch
    {
        EyeMood.Happy => new(1.24, 0.50, 14, -1.6),      // прищур-улыбка «^ ^»
        EyeMood.Excited => new(1.14, 1.14, 0, -1.2),     // широко открытые, подпрыгивают
        EyeMood.Calm => new(1.08, 0.62, 0, 0.9),         // полуприкрытые, спокойные
        EyeMood.Sleepy => new(1.22, 0.18, 0, 3.0),       // почти закрыты, опущены
        EyeMood.Focused => new(0.90, 0.74, -10, 0.2),    // серьёзный прищур «\ /»
        EyeMood.Surprised => new(1.18, 1.30, 0, -1.8),   // распахнутые
        EyeMood.Blissful => new(1.34, 0.30, 18, -1.4),   // умиление: блаженные дужки «^ ^», сильнее прищур
        EyeMood.Curious => new(1.06, 1.10, 0, -0.6),     // любопытство: чуть шире, голова набок (наклон — в UpdateEyes)
        EyeMood.Bored => new(1.12, 0.52, 0, 1.4),        // скука: веки наполовину, взгляд ленивый
        EyeMood.Sad => new(0.92, 0.70, 9, 2.4),          // грусть: поменьше, опущены
        _ => new(1.0, 1.0, 0, 0)
    };

    const double MaxGazeX = 4.2, MaxGazeY = 3.4, EyeBaseHeight = 20;

    EyeRig _eyeL = null!, _eyeR = null!;
    EyeMood _shownMood = EyeMood.Neutral;
    double _moodLeft;                         // сколько секунд ещё держать эмоцию
    double _eyeTime;                          // часы глаз, секунды
    double _lastMouseMoveAt, _nextGlanceAt = 4, _glanceX, _glanceY;
    double _gazeX, _gazeY, _saccadeUntil = -1, _gazeFreezeUntil;
    double _nextBlinkAt = 2.2, _nextYawnAt;
    // «поглаживание»: курсор быстро ходит туда-сюда прямо по глазам
    double _petLevel, _petLastX = double.NaN, _petLastY;
    int _petDirX, _petDirY;
    double _blush;
    int _blinkRepeat;
    double _lastCursorX = double.NaN, _lastCursorY, _cursorSpeed;
    bool _eyeScript;                          // сценарий (приветствие) управляет взглядом сам
    double _scriptGazeX, _scriptGazeY;
    double _scriptTilt;                       // наклон обоих глаз в сценарии (градусы, «−» — влево)

    void InitEyes()
    {
        _eyeL = new EyeRig { Side = -1, Stiff = 1.0, Scale = EyeLScale, Rotate = EyeLRot, Shift = EyeLShift, Element = EyeL };
        _eyeR = new EyeRig { Side = 1, Stiff = 0.9, Scale = EyeRScale, Rotate = EyeRRot, Shift = EyeRShift, Element = EyeR };
    }

    /// <summary>Показать эмоцию. seconds ≤ 0 — бессрочно. Время идёт только пока глаза на экране.</summary>
    void SetEyeMood(EyeMood mood, double seconds = 2.0)
    {
        if (_welcomeRunning || (!_s.EyeEmotions && mood != EyeMood.Neutral)) return;
        _eyeMood = mood;
        _moodLeft = seconds > 0 ? seconds : double.PositiveInfinity;
        EnsureFx();
    }

    /// <summary>Глаза «раскрываются» после сворачивания острова: из щёлочки с упругим отскоком.</summary>
    void RevealEyes()
    {
        foreach (var rig in new[] { _eyeL, _eyeR })
        {
            rig.X.Reset(0); rig.Y.Reset(0); rig.Rot.Reset(0);
            rig.SX.Reset(1.25);
            rig.SY.Reset(0.12);
            rig.Lid.Reset(1);
            rig.BlinkPhase = 0;
        }
        _gazeX = _gazeY = 0;
        _lastCursorX = double.NaN;            // курсор мог уехать, пока глаз не было видно, — это не «испуг»
        _cursorSpeed = 0;
        _gazeFreezeUntil = _eyeTime + 0.22;   // сначала раскрыться, потом искать курсор — без рывка в сторону мыши
        _nextBlinkAt = Math.Max(_nextBlinkAt, _eyeTime + 1.4);
        EnsureFx();
    }

    void StartBlink(EyeRig rig, double delay, double hold)
    {
        if (rig.BlinkPhase != 0) return;
        rig.BlinkPhase = 1;
        rig.PhaseAt = _eyeTime + delay;
        rig.BlinkHold = hold;
    }

    void BlinkBoth(bool sleepy)
    {
        double hold = sleepy ? 0.22 : 0.035;
        StartBlink(_eyeL, 0, hold);
        StartBlink(_eyeR, 0.014, hold);       // правый — на 14 мс позже: живее, чем идеально синхронно
    }

    void StepLid(EyeRig rig, double dt, bool sleepy)
    {
        double t = _eyeTime;
        switch (rig.BlinkPhase)
        {
            case 1 when t >= rig.PhaseAt:
                rig.Lid.Target = 0;
                rig.Lid.Step(dt, 2600, 0.95);                       // закрывается быстро и решительно
                if (rig.Lid.Value < 0.1 || t - rig.PhaseAt > 0.12) { rig.BlinkPhase = 2; rig.PhaseAt = t; }
                break;
            case 2:
                rig.Lid.Step(dt, 2600, 0.95);
                if (t - rig.PhaseAt >= rig.BlinkHold) { rig.BlinkPhase = 3; rig.Lid.Target = 1; }
                break;
            case 3:
                rig.Lid.Step(dt, sleepy ? 70 : 520, sleepy ? 0.95 : 0.48);   // открывается с упругим «пружинистым» отскоком
                if (rig.Lid.IsSettled(0.004)) { rig.Lid.Snap(); rig.BlinkPhase = 0; }
                break;
            default:
                if (rig.BlinkPhase == 0 && !rig.Lid.IsSettled(0.002)) rig.Lid.Step(dt, 520, 0.48);
                break;
        }
    }

    void MoodPop(EyeMood to)
    {
        foreach (var rig in new[] { _eyeL, _eyeR })
        {
            switch (to)
            {
                case EyeMood.Surprised: rig.SY.Velocity += 9; rig.SX.Velocity += 3; rig.Y.Velocity -= 40; break;
                case EyeMood.Excited: rig.Y.Velocity -= 55; rig.SY.Velocity += 5; break;
                case EyeMood.Happy: rig.SY.Velocity -= 4; rig.Y.Velocity -= 28; break;
                case EyeMood.Focused: rig.SX.Velocity -= 3; break;
                case EyeMood.Neutral: rig.SY.Velocity += 3; break;
                case EyeMood.Blissful: rig.SY.Velocity -= 6; rig.SX.Velocity += 3; rig.Y.Velocity -= 20; break;
                case EyeMood.Curious: rig.SY.Velocity += 4; rig.Y.Velocity -= 18; break;
                case EyeMood.Sad: rig.Y.Velocity += 30; rig.SY.Velocity -= 2; break;
                case EyeMood.Bored: rig.SY.Velocity -= 2; break;
            }
        }
    }

    static readonly double[] GlanceX = { -3.8, -2.4, 0, 2.4, 3.8 };
    static readonly double[] GlanceY = { -2.0, -0.8, 0, 1.2, 2.2 };

    void UpdateEyes(double dt, bool playing, bool beat)
    {
        _eyeTime += dt;
        double t = _eyeTime;

        // ---- эмоция: время тикает только пока глаза видны ----
        if (!double.IsPositiveInfinity(_moodLeft) && _eyeMood != EyeMood.Neutral)
        {
            _moodLeft -= dt;
            if (_moodLeft <= 0) { _eyeMood = EyeMood.Neutral; _moodLeft = 0; }
        }

        // ---- курсор ----
        bool haveCursor = GetCursorPos(out var p);
        if (haveCursor)
        {
            if (!double.IsNaN(_lastCursorX))
            {
                double moved = Math.Sqrt((p.X - _lastCursorX) * (p.X - _lastCursorX) + (p.Y - _lastCursorY) * (p.Y - _lastCursorY));
                _cursorSpeed += (moved / dt - _cursorSpeed) * (1 - Math.Exp(-14 * dt));
                if (moved > 0.5) _lastMouseMoveAt = t;
            }
            _lastCursorX = p.X; _lastCursorY = p.Y;
        }
        double idle = t - _lastMouseMoveAt;

        Point center = default;
        double cdx = 0, cdy = 0, cd = 0;
        bool haveCenter = false;
        if (haveCursor && Eyes.ActualWidth > 0)
        {
            try
            {
                // Центр берём у контейнера глаз, а не у самого глаза: иначе сдвиг глаза менял бы
                // «точку отсчёта» и взгляд дрожал бы в обратной связи.
                center = Eyes.PointToScreen(new Point(Eyes.ActualWidth / 2, Eyes.ActualHeight / 2));
                cdx = p.X - center.X; cdy = p.Y - center.Y;
                cd = Math.Sqrt(cdx * cdx + cdy * cdy);
                haveCenter = true;
            }
            catch (InvalidOperationException) { }
        }

        // ---- «гладят» по глазам → умиление ----
        double px = 1;
        try { px = VisualTreeHelper.GetDpi(this).DpiScaleX; } catch { }
        UpdatePetting(dt, t, haveCenter, cdx, cdy, p.X, p.Y, px);

        // ---- эффективная эмоция ----
        var mood = _eyeMood;
        bool danceOn = playing && _s.BeatBounce && !_eyeScript;
        if (!_eyeScript && _s.EyeEmotions)
        {
            if (mood == EyeMood.Neutral && Pill.IsMouseOver) mood = EyeMood.Curious;               // разглядывает курсор
            if (mood == EyeMood.Neutral && idle > 90 && _s.SleepAtNight && DateTime.Now.Hour < 6) mood = EyeMood.Sleepy; // глубокая ночь
            if (mood == EyeMood.Neutral && idle > 45 && !playing) mood = EyeMood.Bored;            // долго ничего не происходит
            if (mood == EyeMood.Neutral && danceOn && _danceEnergy > 0.55 && _tempoConf > 0.4)
                mood = Bpm < 92 ? EyeMood.Calm : Bpm > 132 ? EyeMood.Excited : EyeMood.Neutral;   // настроение под темп
        }
        else mood = EyeMood.Neutral;

        if (mood != _shownMood)
        {
            MoodPop(mood);
            if (mood == EyeMood.Bored) _nextYawnAt = t + 2.5 + _rng.NextDouble() * 3;
            _shownMood = mood;
        }
        var shape = ShapeOf(mood);
        bool sleepy = mood == EyeMood.Sleepy;

        // ---- куда смотреть ----
        double gx = _gazeX, gy = _gazeY;
        if (t < _gazeFreezeUntil) { gx = 0; gy = 0; }
        else if (_eyeScript) { gx = _scriptGazeX; gy = _scriptGazeY; }
        else if (sleepy) { gx = 0; gy = 0.6; }
        else if (mood == EyeMood.Blissful && haveCenter && cd > 1)
        {
            // жмурится и тянется за «рукой», но совсем чуть-чуть
            double k = Smooth(Math.Min(1.0, cd / 160.0));
            gx = cdx / cd * MaxGazeX * 0.45 * k;
            gy = -0.6;
        }
        else if (mood == EyeMood.Bored)
        {
            if (t >= _nextGlanceAt)
            {
                _glanceX = GlanceX[_rng.Next(GlanceX.Length)] * 0.7 * _s.GazeRange / 100.0;
                _glanceY = 1.2 + _rng.NextDouble();              // скучает — смотрит вниз-в-сторону
                _nextGlanceAt = t + 2.2 + _rng.NextDouble() * 3.0;
            }
            gx = _glanceX; gy = _glanceY;
        }
        else if ((idle > 3.5 || !_s.EyesFollowCursor) && mood is not (EyeMood.Focused or EyeMood.Curious))
        {
            // Мышь давно стоит — осматривается сам: короткие заметные взгляды по сторонам.
            if (t >= _nextGlanceAt)
            {
                _glanceX = GlanceX[_rng.Next(GlanceX.Length)] * _s.GazeRange / 100.0;
                _glanceY = GlanceY[_rng.Next(GlanceY.Length)] * _s.GazeRange / 100.0;
                _nextGlanceAt = t + 0.7 + _rng.NextDouble() * 1.6;
            }
            gx = _glanceX; gy = _glanceY;
        }
        else if (haveCenter && cd > 1)
        {
            double k = Smooth(Math.Min(1.0, cd / 240.0));
            double range = _s.GazeRange / 100.0;
            gx = cdx / cd * MaxGazeX * k * range;
            gy = cdy / cd * MaxGazeY * k * range;
            _nextGlanceAt = t + 0.35;
        }

        double jump = Math.Sqrt((gx - _gazeX) * (gx - _gazeX) + (gy - _gazeY) * (gy - _gazeY));
        if (jump > 2.2)
        {
            _saccadeUntil = t + 0.18;            // резкая смена цели — быстрый «бросок» глаз с перелётом
            if (jump > 4 && _rng.NextDouble() < 0.22) BlinkBoth(false);
        }
        _gazeX = gx; _gazeY = gy;

        // ---- ритм, музыка, характер ----
        double swayX = 0, bobY = 0, headTilt = 0, hopL = 0, hopR = 0, squash = 0;
        double dance = 0;
        if (playing)
        {
            if (danceOn && !sleepy)
            {
                // Танец по темпу: фаза берётся из оценки BPM, поэтому движения попадают в долю, а не просто
                // дёргаются на каждый найденный удар. Стиль зависит от темпа трека.
                dance = _danceEnergy * Math.Clamp(_tempoConf * 1.4, 0.35, 1) * _s.DanceStrength / 100.0;
                double pos = BeatPosition, n = Math.Floor(pos), ph = pos - n;
                double kick = Math.Exp(-ph * 7);           // толчок на ударе, быстро затухает
                double swing = Math.Sin(Math.PI * pos);     // туда-сюда за два удара
                double bpm = Bpm;
                if (bpm < 96)
                {   // медленно: плавно качает «головой» из стороны в сторону
                    swayX += swing * 2.4 * dance;
                    headTilt += swing * 10 * dance;
                    bobY += (1 - Math.Cos(2 * Math.PI * ph)) * 0.45 * dance;
                }
                else if (bpm < 128)
                {   // средне: кивает на каждую долю и пританцовывает шагом в сторону
                    swayX += swing * 1.7 * dance;
                    headTilt += swing * 5 * dance;
                    bobY -= kick * 2.4 * dance;
                    squash = kick * 0.12 * dance;
                }
                else
                {   // быстро: глаза прыгают по очереди, как ноги в танце
                    double hop = kick * 3.0 * dance;
                    if (((long)n & 1) == 0) hopL = hop; else hopR = hop;
                    swayX += Math.Sin(Math.PI * pos / 2) * 1.2 * dance;
                    squash = kick * 0.16 * dance;
                }
            }
            double calmSway = 1 - Math.Min(1, dance);
            swayX += Math.Sin(t * 2.4) * 1.1 * calmSway;
            bobY += Math.Sin(t * 4.8) * 0.5 * calmSway;
            if (beat && _s.BeatBounce)
                foreach (var rig in new[] { _eyeL, _eyeR })
                {
                    double k = 1 - 0.6 * Math.Min(1, dance);   // при танце толчок слабее — его заменяет движение по фазе
                    rig.Y.Velocity -= 60 * k;                  // кивок на каждый удар бочки
                    rig.SY.Velocity -= 3.5 * k;
                    rig.SX.Velocity += 1.8 * k;
                }
        }
        switch (mood)
        {
            case EyeMood.Excited when dance < 0.2: { double s = Math.Sin(t * 6.5); bobY -= s * s * 2.2; break; }
            case EyeMood.Happy: bobY += Math.Sin(t * 5.2) * 0.7; break;
            case EyeMood.Sleepy: bobY += Math.Sin(t * 0.8) * 0.8; break;
            case EyeMood.Calm when dance < 0.2: swayX += Math.Sin(t * 1.1) * 0.8; break;
            case EyeMood.Blissful:
                swayX += Math.Sin(t * 3.1) * 0.9;              // млеет: покачивается
                headTilt += Math.Sin(t * 3.1) * 5;
                bobY += Math.Sin(t * 41) * 0.18;              // «мурчит» — лёгкая дрожь
                break;
            case EyeMood.Curious:
                headTilt += (cdx >= 0 ? 1 : -1) * 9;          // голова набок, в сторону курсора
                break;
            case EyeMood.Sad: bobY += Math.Sin(t * 1.4) * 0.35; break;
        }
        if (mood == EyeMood.Bored && t >= _nextYawnAt && _eyeL.BlinkPhase == 0)
        {
            // зевок: глаза вытягиваются вверх, потом медленно закрываются
            foreach (var rig in new[] { _eyeL, _eyeR }) { rig.SY.Velocity += 9; rig.SX.Velocity -= 3; rig.Y.Velocity -= 25; }
            StartBlink(_eyeL, 0.42, 0.55);
            StartBlink(_eyeR, 0.45, 0.55);
            _nextYawnAt = t + 9 + _rng.NextDouble() * 7;
            _nextBlinkAt = Math.Max(_nextBlinkAt, t + 2);
        }

        // ---- моргание ----
        if (_s.BlinkRate <= 0) _nextBlinkAt = Math.Max(_nextBlinkAt, t + 1);   // v2.10: моргание выключено
        else if (!_eyeScript && mood is not (EyeMood.Surprised or EyeMood.Blissful) && t >= _nextBlinkAt && _eyeL.BlinkPhase == 0 && _eyeR.BlinkPhase == 0)
        {
            BlinkBoth(sleepy);
            if (_blinkRepeat > 0) { _blinkRepeat--; _nextBlinkAt = t + 0.26; }
            else
            {
                if (_rng.NextDouble() < 0.18) _blinkRepeat = 1;   // иногда двойное моргание
                _nextBlinkAt = t + (sleepy ? 3.5 + _rng.NextDouble() * 3 : 2.2 + _rng.NextDouble() * 3.8) * 100.0 / _s.BlinkRate;
            }
        }

        // ---- параметры пружин ----
        bool saccade = t < _saccadeUntil;
        (double posK, double posZ) = sleepy ? (40.0, 1.0) : saccade ? (480.0, 0.5) : dance > 0.1 ? (330.0, 0.6) : (150.0, 0.85);
        (double shapeK, double shapeZ) = sleepy || mood is EyeMood.Calm or EyeMood.Bored ? (60.0, 0.95) : (230.0, 0.42);

        var room = BodyRoom();

        foreach (var rig in new[] { _eyeL, _eyeR })
        {
            double curious = mood == EyeMood.Curious ? ((cdx >= 0) == (rig.Side > 0) ? 1.08 : 0.94) : 1;   // ближний к курсору глаз шире
            rig.X.Target = gx + swayX;
            rig.Y.Target = gy + shape.Lift + bobY - (rig.Side < 0 ? hopL : hopR);
            rig.SX.Target = shape.SX * (1 + squash * 0.6);
            rig.SY.Target = shape.SY * (1 - squash) * curious;
            rig.Rot.Target = -rig.Side * shape.Tilt + headTilt + (_eyeScript ? _scriptTilt : 0);

            rig.X.Step(dt, posK * rig.Stiff, posZ);
            rig.Y.Step(dt, posK * rig.Stiff, posZ);
            rig.SX.Step(dt, shapeK * rig.Stiff, shapeZ);
            rig.SY.Step(dt, shapeK * rig.Stiff, shapeZ);
            rig.Rot.Step(dt, (_eyeScript ? 150 : 200) * rig.Stiff, _eyeScript ? 0.42 : 0.5);   // в приветствии крен мягче и «желейнее»
            StepLid(rig, dt, sleepy);

            // Squash & stretch: быстрое движение вытягивает глаз по направлению и наклоняет его.
            double vx = rig.X.Velocity, vy = rig.Y.Velocity;
            double stX = Math.Min(0.30, Math.Abs(vx) * 0.0055);
            double stY = Math.Min(0.30, Math.Abs(vy) * 0.0055);
            double lid = Math.Max(0.06, rig.Lid.Value);
            double closed = Math.Max(0, 1 - lid);

            double sx = rig.SX.Value * (1 + stX - stY * 0.45) * (1 + closed * 0.25);  // закрываясь, глаз сплющивается вширь
            double sy = rig.SY.Value * (1 + stY - stX * 0.45) * lid;
            sx = Math.Clamp(sx, 0.6, 1.6);
            sy = Math.Clamp(sy, 0.05, 1.38);

            double rot = rig.Rot.Value + Math.Clamp(vx * 0.05, -12, 12);
            (double x, double y) = FitEye(rig, room, ref sx, ref sy, rot);   // глаз никогда не вылезает за край острова

            rig.Shift.X = x;
            rig.Shift.Y = y;
            rig.Scale.ScaleX = sx;
            rig.Scale.ScaleY = sy;
            rig.Rotate.Angle = rot;
        }

        UpdateBlush(dt, mood == EyeMood.Blissful ? 0.9 : 0);
    }

    /// <summary>
    /// Курсор быстро водят туда-сюда прямо по глазам — их «гладят». Считаем развороты движения
    /// над глазами; несколько разворотов подряд — умиление (и продлеваем его, пока гладят).
    /// </summary>
    void UpdatePetting(double dt, double t, bool haveCenter, double cdx, double cdy, double x, double y, double px)
    {
        _petLevel = Math.Max(0, _petLevel - dt * 0.6);
        bool over = haveCenter && Math.Abs(cdx) < (Eyes.ActualWidth / 2 + 20) * px && Math.Abs(cdy) < 24 * px;
        if (!over || _eyeScript || !_s.EyeEmotions || _state != IslandState.Collapsed)
        {
            _petLastX = double.NaN;
            return;
        }
        if (double.IsNaN(_petLastX)) { _petLastX = x; _petLastY = y; return; }
        double dx = x - _petLastX, dy = y - _petLastY;
        double minStep = 1.5 * px;
        bool fast = _cursorSpeed > 220 * px;
        if (Math.Abs(dx) > minStep)
        {
            int d = Math.Sign(dx);
            if (d != _petDirX && _petDirX != 0 && fast) OnPetStroke(d, 0);
            _petDirX = d;
            _petLastX = x;
        }
        if (Math.Abs(dy) > minStep)
        {
            int d = Math.Sign(dy);
            if (d != _petDirY && _petDirY != 0 && fast) OnPetStroke(0, d);
            _petDirY = d;
            _petLastY = y;
        }

        void OnPetStroke(int sx, int sy)
        {
            _petLevel = Math.Min(1.8, _petLevel + 0.34);
            if (_petLevel < 0.9) return;
            if (_eyeMood != EyeMood.Blissful) SetEyeMood(EyeMood.Blissful, 1.8);
            else _moodLeft = Math.Max(_moodLeft, 1.8);     // гладят дальше — блаженство не кончается
            foreach (var rig in new[] { _eyeL, _eyeR }) { rig.X.Velocity += sx * 14; rig.SX.Velocity += 1.2; }
            JellyPoke(0.35 * Math.Abs(sx) + 0.15, -0.25 - 0.2 * Math.Abs(sy));   // остров тоже чуть «тает»
            _nextBlinkAt = Math.Max(_nextBlinkAt, t + 1.5);
        }
    }

    /// <summary>Румянец под глазами: плавно проявляется и следует за глазами.</summary>
    void UpdateBlush(double dt, double target)
    {
        _blush += (target - _blush) * (1 - Math.Exp(-(target > _blush ? 6.0 : 3.5) * dt));
        if (_blush < 0.01)
        {
            if (BlushL.Opacity != 0) { BlushL.Opacity = 0; BlushR.Opacity = 0; }
            _blush = Math.Max(0, _blush);
            return;
        }
        var room = BodyRoom();
        foreach (var (rig, el) in new[] { (_eyeL, BlushL), (_eyeR, BlushR) })
        {
            var home = EyeHome(rig);
            if (double.IsNaN(home.X)) continue;
            double cx = home.X + rig.Shift.X + rig.Side * 4.5;
            double cy = Math.Min(home.Y + rig.Shift.Y + Math.Max(5, rig.Element.ActualHeight * 0.33), room.Bottom - 3.5);   // v2.10: под глазом любого размера
            Point pt;
            try { pt = Pill.TranslatePoint(new Point(cx, cy), BlushLayer); }
            catch (InvalidOperationException) { continue; }
            System.Windows.Controls.Canvas.SetLeft(el, pt.X - el.Width / 2);
            System.Windows.Controls.Canvas.SetTop(el, pt.Y - el.Height / 2);
            el.Opacity = _blush * Math.Clamp(_s.GlassOpacity / 100.0 + 0.4, 0.5, 1);
        }
    }

    // ---------------------------------------------------------------------------------------------
    //  Границы: глаз целиком остаётся внутри видимого тела острова — с учётом скруглённых углов,
    //  поднятой волны музыки, наклона и растяжения глаза. Если места не хватает, глаз чуть
    //  сплющивается, а не вылезает наружу.
    // ---------------------------------------------------------------------------------------------

    readonly record struct Room(double W, double Top, double Bottom, double R);

    const double EyeEdgeGap = 1.5;   // минимальный зазор между глазом и краем острова, px

    /// <summary>Сплошная часть тела острова — те же числа, что и в RebuildBody.</summary>
    Room BodyRoom()
    {
        double w = double.IsNaN(Pill.Width) ? Pill.ActualWidth : Pill.Width;
        double h = double.IsNaN(Pill.Height) ? Pill.ActualHeight : Pill.Height;
        double top = 0.5, bottom = h - Math.Max(0, _waveAmp) - 0.5;   // ниже — только «впадины» волны
        double r = Math.Min(Pill.CornerRadius.TopLeft, Math.Min((w - 1) / 2, (bottom - top) / 2));
        return new Room(w, top, bottom, Math.Max(0, r));
    }

    /// <summary>Сколько места по вертикали есть у вертикали x (учитывая скругление углов).</summary>
    static (double Top, double Bottom) RoomAt(in Room room, double x)
    {
        double left = 0.5, right = room.W - 0.5, r = room.R;
        double dx = x < left + r ? left + r - x : x > right - r ? x - (right - r) : 0;
        if (dx <= 0 || r <= 0) return (room.Top, room.Bottom);
        dx = Math.Min(dx, r);
        double k = r - Math.Sqrt(Math.Max(0, r * r - dx * dx));
        return (room.Top + k, room.Bottom - k);
    }

    /// <summary>Центр глаза в координатах острова без анимационных сдвигов (чистая раскладка).</summary>
    Point EyeHome(EyeRig rig)
    {
        var el = rig.Element;
        var off = VisualTreeHelper.GetOffset(el);   // смещение раскладки, RenderTransform сюда не входит
        var p = new Point(off.X + el.ActualWidth / 2, off.Y + el.ActualHeight / 2);
        try
        {
            if (VisualTreeHelper.GetParent(el) is UIElement parent && parent.IsDescendantOf(Pill))
                return parent.TranslatePoint(p, Pill);
        }
        catch (InvalidOperationException) { }
        return new Point(double.NaN, double.NaN);
    }

    /// <summary>Подогнать сдвиг (и при необходимости высоту) глаза под форму острова.</summary>
    (double X, double Y) FitEye(EyeRig rig, in Room room, ref double sx, ref double sy, double rotDeg)
    {
        double ox = rig.X.Value, oy = rig.Y.Value;
        var home = EyeHome(rig);
        if (double.IsNaN(home.X) || room.W < 8 || room.Bottom - room.Top < 4) return (ox, oy);

        double w0 = rig.Element.ActualWidth > 0 ? rig.Element.ActualWidth : 6;
        double h0 = rig.Element.ActualHeight > 0 ? rig.Element.ActualHeight : EyeBaseHeight;
        double a = rotDeg * Math.PI / 180, c = Math.Abs(Math.Cos(a)), s = Math.Abs(Math.Sin(a));

        double hw = w0 / 2 * sx, hh = h0 / 2 * sy;
        double bw = hw * c + hh * s, bh = hw * s + hh * c;   // габарит повёрнутого глаза

        // По горизонтали — не ближе EyeEdgeGap к боковым краям.
        double minX = 0.5 + EyeEdgeGap + bw, maxX = room.W - 0.5 - EyeEdgeGap - bw;
        double cx = minX <= maxX ? Math.Clamp(home.X + ox, minX, maxX) : room.W / 2;

        // По вертикали — место у обоих краёв глаза (в углах его меньше).
        var (t1, b1) = RoomAt(room, cx - bw);
        var (t2, b2) = RoomAt(room, cx + bw);
        double top = Math.Max(t1, t2) + EyeEdgeGap, bottom = Math.Min(b1, b2) - EyeEdgeGap;
        double avail = Math.Max(1, bottom - top);

        if (2 * bh > avail && c > 0.05)
        {
            // Не помещается даже по центру — сплющиваем, а не выпускаем за край.
            double hhFit = Math.Max(0.5, (avail / 2 - hw * s) / c);
            sy = Math.Max(0.05, Math.Min(sy, hhFit / (h0 / 2)));
            hh = h0 / 2 * sy;
            bh = hw * s + hh * c;
        }

        double minY = top + bh, maxY = bottom - bh;
        double cy = minY <= maxY ? Math.Clamp(home.Y + oy, minY, maxY) : (top + bottom) / 2;
        return (cx - home.X, cy - home.Y);
    }
}
