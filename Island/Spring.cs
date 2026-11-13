namespace Island;

/// <summary>
/// Пружина (масса–пружина–демпфер). В отличие от анимаций по ключевым кадрам её цель
/// можно менять в любой момент: текущая скорость сохраняется, поэтому движение никогда
/// не «перезапускается» рывком. Это основа всей «желейной» анимации острова и глаз.
/// </summary>
public struct Spring
{
    public double Value;
    public double Velocity;
    public double Target;

    public Spring(double value)
    {
        Value = Target = value;
        Velocity = 0;
    }

    /// <summary>Коэффициент демпфирования по жёсткости и «упругости» zeta (1 = без перелёта, &lt;1 = с отскоком).</summary>
    public static double Damping(double stiffness, double zeta) => 2.0 * zeta * Math.Sqrt(stiffness);

    /// <summary>Шаг симуляции. Полунеявный Эйлер с подшагами ≤ 1/240 с — устойчиво даже для жёстких пружин и рваных кадров.</summary>
    public void Step(double dt, double stiffness, double zeta)
    {
        if (dt <= 0) return;
        double damping = Damping(stiffness, zeta);
        int n = Math.Max(1, (int)Math.Ceiling(dt * 240.0));
        double h = dt / n;
        for (int i = 0; i < n; i++)
        {
            Velocity += ((Target - Value) * stiffness - Velocity * damping) * h;
            Value += Velocity * h;
        }
    }

    public readonly bool IsSettled(double eps) =>
        Math.Abs(Target - Value) < eps && Math.Abs(Velocity) < eps * 12;

    public void Snap()
    {
        Value = Target;
        Velocity = 0;
    }

    public void Reset(double value)
    {
        Value = Target = value;
        Velocity = 0;
    }
}
