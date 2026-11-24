namespace UtopiaSim;

/// <summary>Una especie ya resuelta a partir de la configuración (índices en vez de nombres).</summary>
public sealed class SpeciesDef
{
    internal SpeciesDef(int index, SpeciesConfig config, IReadOnlyList<GeneDef> genes)
    {
        Index = index;
        Name = config.Name;
        Config = config;
        IsPlant = config.Kind == SpeciesKind.Plant;
        Plant = config.Plant ?? new PlantConfig();
        var mutation = new MutationConfig?[genes.Count];
        var initial = new Vec2[genes.Count];
        foreach (var g in genes)
        {
            if (config.Mutation.TryGetValue(g.Name, out var m)) mutation[g.Index] = m;
            var start = config.InitialGenes.TryGetValue(g.Name, out var value)
                ? new Vec2(value[0], value.Length > 1 ? value[1] : 0)
                : Vec2.Zero;
            initial[g.Index] = g.Space.Project(start);
            if (config.PreferredStageGene == g.Name) StageGeneIndex = g.Index;
            if (config.DietGene == g.Name) DietGeneIndex = g.Index;
            if (Plant.Dispersal?.Gene == g.Name) DispersalGeneIndex = g.Index;
        }
        Mutation = mutation;
        InitialGenes = initial;
    }

    public int Index { get; }
    public string Name { get; }
    public SpeciesConfig Config { get; }
    public bool IsPlant { get; }
    public PlantConfig Plant { get; }

    /// <summary>Mutación por índice de gen; null = ese gen no muta en esta especie.</summary>
    public IReadOnlyList<MutationConfig?> Mutation { get; }

    public IReadOnlyList<Vec2> InitialGenes { get; }

    /// <summary>Índice del gen de etapa preferida, o -1 si la especie no tiene.</summary>
    public int StageGeneIndex { get; } = -1;

    /// <summary>Índice del gen de dieta (0 folívoro, 1 frugívoro), o -1 si la especie no tiene.</summary>
    public int DietGeneIndex { get; } = -1;

    /// <summary>Solo plantas: índice del gen triangular de dispersión, o -1 si no tiene.</summary>
    public int DispersalGeneIndex { get; } = -1;
}
