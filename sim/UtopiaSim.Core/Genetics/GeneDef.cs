namespace UtopiaSim;

/// <summary>Un gen tal como quedó definido en la configuración, con su forma ya construida.</summary>
public sealed class GeneDef
{
    internal GeneDef(int index, GeneConfig config)
    {
        Index = index;
        Name = config.Name;
        Config = config;
        Space = TraitSpace.Create(config);
    }

    /// <summary>Posición de este gen dentro de <see cref="Creature.Genes"/>.</summary>
    public int Index { get; }

    public string Name { get; }

    public GeneConfig Config { get; }

    public TraitSpace Space { get; }
}
