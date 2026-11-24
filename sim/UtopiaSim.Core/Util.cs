namespace UtopiaSim;

public static class ColorUtil
{
    /// <summary>Convierte tono/saturación a RGB en [0, 1], igual que dnaToColor del original.</summary>
    public static (double R, double G, double B) HueSatToRgb(double hue, double sat)
    {
        double h = hue * 6.0;
        double rem = h % 1.0;
        double midDown = 1 - sat * rem;
        double midUp = 1 - sat + sat * rem;
        double low = 1 - sat;
        return h switch
        {
            < 1 => (1, midUp, low),
            < 2 => (midDown, 1, low),
            < 3 => (low, 1, midUp),
            < 4 => (low, midDown, 1),
            < 5 => (midUp, low, 1),
            _ => (1, low, midDown),
        };
    }

    /// <summary>Color de un punto de un gen de disco.</summary>
    public static (double R, double G, double B) DiskToRgb(Vec2 p) =>
        HueSatToRgb(DiskSpace.Hue(p), DiskSpace.Saturation(p));
}

internal static class NameGenerator
{
    const string Consonants = "BCDFGHJKLMNPQRSTVWXZ";
    const string Vowels = "AEIOUY";

    /// <summary>Nombre inventado alternando consonantes y vocales (como el original).</summary>
    public static string Next(Rng rng)
    {
        int len = (int)rng.Range(4.8, 8);
        bool inverted = rng.NextDouble() < 0.5;
        Span<char> buf = stackalloc char[len];
        for (int i = 0; i < len; i++)
        {
            bool vowel = (i % 2 == 1) ^ inverted;
            string options = vowel ? Vowels : Consonants;
            char ch = options[rng.Below(options.Length)];
            buf[i] = i == 0 ? ch : char.ToLowerInvariant(ch);
        }
        return new string(buf);
    }
}
