using System.Windows;
using System.Windows.Media;
using Point = System.Windows.Point;

namespace Island;

/// <summary>
/// Пружинная «желейная» анимация самого острова.
/// Ширина, высота и скругление едут на пружинах; растяжение/сжатие (squash &amp; stretch)
/// вычисляется из скорости, поэтому остров тянется в сторону движения и мягко
/// «дрожит», когда останавливается. Новая цель посреди анимации подхватывается
/// без рывка — скорость сохраняется.
/// </summary>
public partial class MainWindow
{
    Spring _pw = new(48), _ph = new(48), _pr = new(24), _jx = new(1), _jy = new(1);
    double _islandStiffness = 300, _islandZeta = 0.7;
    double _baseRadius = 18;          // скругление для текущего состояния до учёта настройки пользователя
    bool _islandMoving;

    // Параметры «характера» движения
    // v2.10: стиль появления — «Пружина» (как раньше), «Плавно» (без перелёта) и «Капля» (сильный отскок).
    double BouncyStiffness => _s.AppearStyle switch { "Smooth" => 250, "Drop" => 330, _ => 290 };   // раскрытие / тост
    double BouncyZeta => _s.AppearStyle switch { "Smooth" => 1.0, "Drop" => 0.46, _ => 0.66 };
    double SoftStiffness => _s.AppearStyle switch { "Smooth" => 300, "Drop" => 380, _ => 360 };     // сворачивание
    double SoftZeta => _s.AppearStyle switch { "Smooth" => 1.0, "Drop" => 0.62, _ => 0.82 };
    double StyleJelly => _s.AppearStyle switch { "Smooth" => 0.35, "Drop" => 1.5, _ => 1.0 };
    const double JellyStiffness = 540, JellyZeta = 0.30;     // «желе»: 2–3 затухающих колебания
    double JellyMax => Math.Max(0.0005, 0.065 * _s.JellyStrength / 100.0 * StyleJelly);   // максимум растяжения (±6,5 % при 100 %)
    double _animKindSpeed = 1;        // v2.10: отдельная скорость для раскрытия и для уведомлений

    /// <summary>Скругление для состояния с учётом настройки «Скругление» (18 px = как в дизайне).</summary>
    double StateRadius(double baseRadius, double w, double h)
    {
        double r = baseRadius * (_s.CornerRadius / 18.0);
        return Math.Clamp(r, 0, Math.Min(w, h) / 2.0);
    }

    /// <summary>Сразу (без анимации) поставить остров в размер — для стартового кружка.</summary>
    void SnapIsland(double w, double h, double radius)
    {
        _islandMoving = false;
        _baseRadius = radius;
        _pw.Reset(w); _ph.Reset(h); _pr.Reset(Math.Min(radius, Math.Min(w, h) / 2));
        _jx.Reset(1); _jy.Reset(1);
        Pill.Width = w; Pill.Height = h;
        Pill.CornerRadius = new CornerRadius(_pr.Value);
        _jellyTransform.ScaleX = _jellyTransform.ScaleY = 1;
        _boxW = w; _boxH = h;
    }

    void Animate(double w, double h, double radius, bool spring, double speed = 1)
    {
        _baseRadius = radius;
        _animKindSpeed = Math.Clamp(speed, 0.3, 3);
        double targetR = StateRadius(radius, w, h);

        // Уже едем к этой же цели — ничего не перезапускаем (раньше каждое медиа-событие
        // заново запускало «желе», и остров подёргивался).
        if (_islandMoving && Math.Abs(_pw.Target - w) < 0.5 && Math.Abs(_ph.Target - h) < 0.5)
        {
            _pr.Target = targetR;
            return;
        }

        if (!_islandMoving)
        {
            double cw = double.IsNaN(Pill.Width) ? Pill.ActualWidth : Pill.Width;
            double ch = double.IsNaN(Pill.Height) ? Pill.ActualHeight : Pill.Height;
            if (Math.Abs(cw - w) < 0.5 && Math.Abs(ch - h) < 0.5 && Math.Abs(Pill.CornerRadius.TopLeft - targetR) < 0.5)
                return;                                   // уже на месте
            _pw.Reset(cw); _ph.Reset(ch); _pr.Reset(Pill.CornerRadius.TopLeft);
        }

        (_islandStiffness, _islandZeta) = spring ? (BouncyStiffness, BouncyZeta) : (SoftStiffness, SoftZeta);
        _pw.Target = w; _ph.Target = h; _pr.Target = targetR;

        // Предвосхищение (anticipation): перед ростом остров на миг «приседает»,
        // перед сжатием — чуть вытягивается. Делает движение живым и заметным.
        double grow = Math.Clamp((h - _ph.Value) / Math.Max(30, _ph.Value), -1, 1);
        double antic = _s.AppearStyle switch { "Smooth" => 0.3, "Drop" => 1.7, _ => 1.0 };
        _jy.Velocity -= grow * 1.1 * antic;
        _jx.Velocity += grow * 0.7 * antic;

        // Окно растёт один раз до размера «с запасом», остров анимируется внутри.
        SetWindowBox(Math.Max(_boxW, w), Math.Max(_boxH, h));
        _islandMoving = true;
        EnsureFx();
    }

    /// <summary>Пересчитать скругление (сменилась настройка) и плавно доехать до него.</summary>
    void RetargetRadius()
    {
        double w = _islandMoving ? _pw.Target : Pill.ActualWidth;
        double h = _islandMoving ? _ph.Target : Pill.ActualHeight;
        if (w < 1 || h < 1) return;
        double targetR = StateRadius(_baseRadius, w, h);
        if (!_islandMoving)
        {
            if (Math.Abs(Pill.CornerRadius.TopLeft - targetR) < 0.25) return;
            _pw.Reset(w); _ph.Reset(h); _pr.Reset(Pill.CornerRadius.TopLeft);
            (_islandStiffness, _islandZeta) = (SoftStiffness, SoftZeta);
        }
        _pr.Target = targetR;
        _islandMoving = true;
        EnsureFx();
    }

    static double SoftClamp(double v, double max) => max * Math.Tanh(v / max);

    /// <summary>Шаг анимации острова. Возвращает true, пока остров движется.</summary>
    bool StepIsland(double dt)
    {
        if (!_islandMoving) return false;

        double speed = _s.AnimationSpeed / 100.0 * _animKindSpeed;                 // время анимации ∝ 1/speed → жёсткость ∝ speed²
        double k = _islandStiffness * speed * speed;
        _pw.Step(dt, k, _islandZeta);
        _ph.Step(dt, k * 0.9, _islandZeta);   // высота чуть «ленивее» ширины — форма течёт, а не масштабируется
        _pr.Step(dt, 420, 0.9);

        // Squash & stretch из относительной скорости: тянется по оси движения, сжимается по другой.
        double vx = _pw.Velocity / Math.Max(40, _pw.Value);
        double vy = _ph.Velocity / Math.Max(30, _ph.Value);
        _jx.Target = 1 + SoftClamp(vx * 0.040 - vy * 0.028, JellyMax);
        _jy.Target = 1 + SoftClamp(vy * 0.040 - vx * 0.028, JellyMax);
        _jx.Step(dt, JellyStiffness, JellyZeta);
        _jy.Step(dt, JellyStiffness, JellyZeta);

        double w = Math.Max(8, _pw.Value), h = Math.Max(8, _ph.Value);
        Pill.Width = w;
        Pill.Height = h;
        Pill.CornerRadius = new CornerRadius(Math.Clamp(_pr.Value, 0, Math.Min(w, h) / 2));
        _jellyTransform.ScaleX = Math.Clamp(_jx.Value, 1 - JellyMax * 1.4, 1 + JellyMax * 1.4);
        _jellyTransform.ScaleY = Math.Clamp(_jy.Value, 1 - JellyMax * 1.4, 1 + JellyMax * 1.4);

        bool settled = _pw.IsSettled(0.25) && _ph.IsSettled(0.25) && _pr.IsSettled(0.1) &&
                       _jx.IsSettled(0.0008) && _jy.IsSettled(0.0008);
        if (settled)
        {
            _pw.Snap(); _ph.Snap(); _pr.Snap(); _jx.Reset(1); _jy.Reset(1);
            Pill.Width = _pw.Value;
            Pill.Height = _ph.Value;
            Pill.CornerRadius = new CornerRadius(_pr.Value);
            _jellyTransform.ScaleX = _jellyTransform.ScaleY = 1;
            _islandMoving = false;
            SetWindowBox(_pw.Value, _ph.Value);           // одно изменение размера окна по окончании
            RebuildBody();
        }
        return true;
    }

    /// <summary>Точка, от которой тянется «желе»: остров как будто подвешен к краю экрана.</summary>
    void UpdateJellyOrigin()
    {
        double ox = _s.Align switch { "Left" => 0.0, "Right" => 1.0, _ => 0.5 };
        double oy = _s.VerticalAlign switch { "Bottom" => 1.0, "Center" => 0.5, _ => 0.0 };
        Pill.RenderTransformOrigin = new Point(ox, oy);
        Pill.VerticalAlignment = _s.VerticalAlign switch
        {
            "Bottom" => VerticalAlignment.Bottom,
            "Center" => VerticalAlignment.Center,
            _ => VerticalAlignment.Top
        };
    }
}
