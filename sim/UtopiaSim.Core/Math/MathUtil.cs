namespace UtopiaSim;

public static class MathUtil
{
    public const double Tau = Math.PI * 2;

    public static double Lerp(double a, double b, double t) => a + (b - a) * t;

    public static double Clamp(double v, double min, double max) => v < min ? min : (v > max ? max : v);

    /// <summary>Módulo que siempre devuelve un valor en [0, n).</summary>
    public static int Mod(int a, int n)
    {
        int r = a % n;
        return r < 0 ? r + n : r;
    }

    /// <summary>Lleva un ángulo a (-π, π].</summary>
    public static double WrapAngle(double a)
    {
        while (a <= -Math.PI) a += Tau;
        while (a > Math.PI) a -= Tau;
        return a;
    }

    /// <summary>atan2 llevado a [0, 2π).</summary>
    public static double Atan2Positive(double y, double x)
    {
        double r = Math.Atan2(y, x);
        return r < 0 ? r + Tau : r;
    }
}
