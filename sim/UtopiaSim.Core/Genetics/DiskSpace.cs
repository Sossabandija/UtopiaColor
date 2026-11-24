namespace UtopiaSim;

/// <summary>
/// Disco de radio 1: la rueda de color del original.
/// Ángulo = tono, distancia al centro = saturación. No tiene esquinas.
/// </summary>
public sealed class DiskSpace(BoundaryRule boundary) : TraitSpace(boundary)
{
    public override bool Contains(Vec2 p) => p.LengthSquared <= 1 + Eps;

    public override Vec2 Project(Vec2 p)
    {
        double r = p.Length;
        return r <= 1 ? p : p / r;
    }

    protected override Vec2 ReflectOnce(Vec2 p)
    {
        double r = p.Length;
        return r <= 1 ? p : p * ((2 - r) / r);
    }

    protected override Vec2 RandomDirection(Rng rng) => Vec2.FromAngle(rng.Angle());

    /// <summary>Tono en [0, 1).</summary>
    public static double Hue(Vec2 p) => MathUtil.Atan2Positive(p.Y, p.X) / MathUtil.Tau;

    /// <summary>Saturación en [0, 1].</summary>
    public static double Saturation(Vec2 p) => Math.Min(1, p.Length);

    public static Vec2 FromHueSat(double hue, double sat) => Vec2.FromAngle(hue * MathUtil.Tau, sat);
}
