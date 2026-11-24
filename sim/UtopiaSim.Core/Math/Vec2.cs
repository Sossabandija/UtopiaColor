namespace UtopiaSim;

/// <summary>Vector 2D con doubles. Propio (no el de Godot) para que el núcleo no dependa del motor.</summary>
public readonly record struct Vec2(double X, double Y)
{
    public static readonly Vec2 Zero = new(0, 0);

    public static Vec2 operator +(Vec2 a, Vec2 b) => new(a.X + b.X, a.Y + b.Y);
    public static Vec2 operator -(Vec2 a, Vec2 b) => new(a.X - b.X, a.Y - b.Y);
    public static Vec2 operator *(Vec2 a, double s) => new(a.X * s, a.Y * s);
    public static Vec2 operator *(double s, Vec2 a) => new(a.X * s, a.Y * s);
    public static Vec2 operator /(Vec2 a, double s) => new(a.X / s, a.Y / s);

    public double Length => Math.Sqrt(X * X + Y * Y);
    public double LengthSquared => X * X + Y * Y;

    public static double Dot(Vec2 a, Vec2 b) => a.X * b.X + a.Y * b.Y;

    /// <summary>Producto cruz 2D (z del producto 3D). Positivo si b está a la izquierda de a.</summary>
    public static double Cross(Vec2 a, Vec2 b) => a.X * b.Y - a.Y * b.X;

    public static double Distance(Vec2 a, Vec2 b) => (a - b).Length;

    public static Vec2 FromAngle(double angle, double length = 1) =>
        new(Math.Cos(angle) * length, Math.Sin(angle) * length);
}
