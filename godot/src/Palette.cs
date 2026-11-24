using Godot;
using UtopiaSim;

namespace UtopiaViewer;

/// <summary>Colores compartidos por todas las vistas.</summary>
static class Palette
{
    /// <summary>Color de cada especie en gráficos y bordes (los del original: verde, azul, rojo).</summary>
    static readonly Color[] SpeciesColors =
    [
        new(0.15f, 0.85f, 0.25f),
        new(0.30f, 0.55f, 1.00f),
        new(1.00f, 0.25f, 0.20f),
        new(1.00f, 0.80f, 0.10f),
        new(0.80f, 0.35f, 1.00f),
        new(0.10f, 0.90f, 0.90f),
    ];

    /// <summary>Colores de las necesidades, como en la ficha del original.</summary>
    public static readonly Color[] Needs =
    [
        new(0.59f, 0.39f, 0.20f),
        new(0.15f, 0.35f, 1.00f),
        new(1.00f, 0.00f, 1.00f),
        new(0.50f, 1.00f, 0.00f),
        new(1.00f, 0.00f, 0.00f),
        new(1.00f, 0.74f, 0.47f),
    ];

    public static readonly Color PanelBg = new(0.09f, 0.10f, 0.12f, 0.96f);
    public static readonly Color PlotBg = new(0.05f, 0.06f, 0.08f);
    public static readonly Color Grid = new(1f, 1f, 1f, 0.08f);
    public static readonly Color TextDim = new(0.70f, 0.72f, 0.76f);
    public static readonly Color Danger = new(1.00f, 0.40f, 0.35f);
    /// <summary>Modos de dispersión: fruto, viento, abrojo (mismo orden que las esquinas del gen).</summary>
    public static readonly Color[] Dispersal = [new(1.00f, 0.42f, 0.25f), new(0.55f, 0.85f, 1.00f), new(0.88f, 0.78f, 0.40f)];
    /// <summary>Madrigueras: el hoyo vacío y el borde de tierra; se llenan con el color de su especie según el cupo usado.</summary>
    public static readonly Color BurrowHole = new(0.10f, 0.07f, 0.05f);
    public static readonly Color BurrowRim = new(0.92f, 0.80f, 0.55f);

    public static Color ForSpecies(int index) => SpeciesColors[index % SpeciesColors.Length];

    /// <summary>Color de un punto de un gen de disco (la rueda de color).</summary>
    public static Color FromDisk(Vec2 p)
    {
        var (r, g, b) = ColorUtil.DiskToRgb(p);
        return new Color((float)r, (float)g, (float)b);
    }

    /// <summary>El primer gen con forma de disco (normalmente "color"), o null.</summary>
    public static GeneDef? ColorGene(Simulation sim) =>
        sim.Genes.FirstOrDefault(g => g.Name == "color" && g.Space is DiskSpace)
        ?? sim.Genes.FirstOrDefault(g => g.Space is DiskSpace);

    /// <summary>Color con el que se dibuja un ser: su gen de color, o el de su especie si no hay.</summary>
    public static Color ForCreature(Creature c, GeneDef? colorGene) =>
        colorGene == null ? ForSpecies(c.Species) : FromDisk(c.Genes[colorGene.Index]);

    public static Font Font => ThemeDB.FallbackFont;

    public static string Capitalize(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];
}
