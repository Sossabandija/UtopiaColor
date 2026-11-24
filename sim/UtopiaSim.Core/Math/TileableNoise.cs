namespace UtopiaSim;

/// <summary>
/// Ruido de gradiente (tipo Perlin) que se repite cada <c>period</c> unidades.
/// El mundo es un toro (los bordes se conectan), así que el terreno también debe repetirse.
/// </summary>
public sealed class TileableNoise
{
    readonly int[] perm = new int[512];

    public TileableNoise(Rng rng)
    {
        var p = new int[256];
        for (int i = 0; i < 256; i++) p[i] = i;
        for (int i = 255; i > 0; i--)
        {
            int j = rng.Below(i + 1);
            (p[i], p[j]) = (p[j], p[i]);
        }
        for (int i = 0; i < 512; i++) perm[i] = p[i & 255];
    }

    static double Fade(double t) => t * t * t * (t * (t * 6 - 15) + 10);

    static double Grad(int hash, double x, double y) => (hash & 7) switch
    {
        0 => x + y,
        1 => -x + y,
        2 => x - y,
        3 => -x - y,
        4 => x,
        5 => -x,
        6 => y,
        _ => -y,
    };

    int Hash(int x, int y) => perm[perm[x & 255] + (y & 255)];

    /// <summary>Ruido en aprox. [-1, 1] que se repite cada <paramref name="period"/> unidades en x e y.</summary>
    public double Noise(double x, double y, int period)
    {
        int xi = (int)Math.Floor(x), yi = (int)Math.Floor(y);
        double xf = x - xi, yf = y - yi;
        int x0 = MathUtil.Mod(xi, period), x1 = MathUtil.Mod(xi + 1, period);
        int y0 = MathUtil.Mod(yi, period), y1 = MathUtil.Mod(yi + 1, period);
        double u = Fade(xf), v = Fade(yf);
        double n00 = Grad(Hash(x0, y0), xf, yf);
        double n10 = Grad(Hash(x1, y0), xf - 1, yf);
        double n01 = Grad(Hash(x0, y1), xf, yf - 1);
        double n11 = Grad(Hash(x1, y1), xf - 1, yf - 1);
        return MathUtil.Lerp(MathUtil.Lerp(n00, n10, u), MathUtil.Lerp(n01, n11, u), v);
    }

    /// <summary>Suma de octavas (fBm), cada una con el doble de frecuencia y periodo.</summary>
    public double Fbm(double x, double y, int period, int octaves, double falloff)
    {
        double sum = 0, amp = 1, norm = 0, freq = 1;
        int per = period;
        for (int o = 0; o < octaves; o++)
        {
            sum += amp * Noise(x * freq, y * freq, per);
            norm += amp;
            amp *= falloff;
            freq *= 2;
            per *= 2;
        }
        return sum / norm;
    }
}
