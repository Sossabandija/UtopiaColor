namespace UtopiaSim;

/// <summary>
/// Un alelo de un gen (genética diploide): su valor en la forma del gen y su dominancia (0 a 1).
/// De los dos alelos de un gen se expresa el más dominante.
/// </summary>
public readonly record struct Allele(Vec2 Value, double Dominance)
{
    /// <summary>
    /// El valor que se expresa: el del alelo más dominante, o el punto medio si dominan igual.
    /// </summary>
    public static Vec2 Express(TraitSpace space, Allele a, Allele b) =>
        a.Dominance > b.Dominance ? a.Value
        : b.Dominance > a.Dominance ? b.Value
        : space.Blend(a.Value, b.Value);

    /// <summary>Índice (0 o 1) del alelo oculto, o -1 si dominan igual (ninguno queda oculto).</summary>
    public static int Hidden(Allele a, Allele b) =>
        a.Dominance > b.Dominance ? 1 : b.Dominance > a.Dominance ? 0 : -1;
}
