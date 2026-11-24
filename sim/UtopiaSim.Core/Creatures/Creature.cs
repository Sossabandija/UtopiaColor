namespace UtopiaSim;

/// <summary>
/// Un ser vivo: planta o animal. Los visores solo deberían leerlo; la simulación es quien lo modifica.
/// </summary>
public sealed class Creature
{
    internal Creature(int id, SpeciesDef species, Vec2[] genes)
    {
        Id = id;
        Def = species;
        Genes = genes;
    }

    public int Id { get; }
    public SpeciesDef Def { get; }
    public int Species => Def.Index;
    public bool IsPlant => Def.IsPlant;

    /// <summary>Valor de cada gen (índice = <see cref="GeneDef.Index"/>). Con genética diploide, el valor que se expresa.</summary>
    public Vec2[] Genes { get; }

    /// <summary>
    /// Con genética diploide: los dos alelos de cada gen, en [2 × gen] (de la madre) y [2 × gen + 1] (del padre).
    /// Null si la especie no es diploide.
    /// </summary>
    public Allele[]? Alleles { get; internal set; }

    // --- Estado físico (unidades del original: px y px/tick) ---
    public double X { get; internal set; }
    public double Y { get; internal set; }
    public double Z { get; internal set; }
    /// <summary>Hacia dónde mira, en radianes.</summary>
    public double Angle { get; internal set; }
    public double Vx { get; internal set; }
    public double Vy { get; internal set; }
    public double Vz { get; internal set; }

    // --- Planta ---
    /// <summary>Tamaño de la planta: parte en 0, suelta semilla al llegar a 1 y baja a 0.5.</summary>
    public double PlantSize { get; internal set; }
    /// <summary>False mientras la semilla vuela.</summary>
    public bool Landed { get; internal set; }
    /// <summary>Cómo llegó esta semilla a donde está (cayó de su madre, viento, estómago o abrojo).</summary>
    public SeedOrigin Origin { get; internal set; }
    /// <summary>Frutos colgando (endozoocoria).</summary>
    public int Fruits { get; internal set; }
    /// <summary>Abrojos esperando a que pase un animal (epizoocoria).</summary>
    public int Burrs { get; internal set; }
    /// <summary>Velocidad de vuelo de la semilla (viento), en veces la normal. También al rebotar.</summary>
    internal double SeedSpeedFactor = 1;

    // --- Animal ---
    /// <summary>Necesidades (ver <see cref="Need"/>). Solo lectura para los visores.</summary>
    public double[] Needs { get; } = new double[Need.Count];
    /// <summary>Necesidad que está atendiendo (-1 = ninguna todavía).</summary>
    public int TopNeed { get; internal set; } = -1;
    public Intent Intent { get; internal set; }
    public TargetKind TargetKind { get; internal set; }
    public Creature? TargetCreature { get; internal set; }
    public Vec2 TargetPoint { get; internal set; }
    public Creature? Predator { get; internal set; }
    public Creature? RecentChild { get; internal set; }
    /// <summary>Semillas que lleva en el estómago (pasarán intactas).</summary>
    public int SeedsInGut => Gut?.Sum(p => p.Seeds) ?? 0;
    /// <summary>Abrojos pegados.</summary>
    public int BurrsCarried => CarriedBurrs?.Count ?? 0;
    internal List<SeedPacket>? Gut;
    internal List<SeedPacket>? CarriedBurrs;
    internal int TickBucket;
    internal int WanderAction = -1;

    // --- Vida y ficha ---
    public bool Dead { get; internal set; }
    public DeathCause? CauseOfDeath { get; internal set; }
    public string Name { get; internal set; } = "";
    public int Generation { get; internal set; }
    public long BirthTick { get; internal set; }
    public int? ParentA { get; internal set; }
    public int? ParentB { get; internal set; }
    public int Children { get; internal set; }
    public int Meals { get; internal set; }
    public int Drinks { get; internal set; }
    public long LastMealTick { get; internal set; } = long.MinValue;
    /// <summary>Con saciedad (digestDays): hasta este tick no busca comida.</summary>
    public long DigestUntil { get; internal set; } = long.MinValue;
    /// <summary>Con territorialidad: después de una caza fallida, hasta este tick no busca comida.</summary>
    public long HuntCooldownUntil { get; internal set; } = long.MinValue;
    /// <summary>Con territorialidad de cría: hay demasiados rivales cerca para buscar pareja (se revisa cada tickBucketCount ticks).</summary>
    public bool TooCrowdedToBreed { get; internal set; }
    /// <summary>Con madrigueras: la que reclamó al huir (está dentro o va hacia ella). Null = ninguna.</summary>
    public Burrow? Burrow { get; internal set; }
    /// <summary>Con cobertura: la planta grande que reclamó para esconderse (está en ella o va hacia ella). Null = ninguna.</summary>
    public Creature? CoverPlant { get; internal set; }
    /// <summary>Planta: cuántos animales la reclamaron para esconderse.</summary>
    public int CoverClaims { get; internal set; }
    public long LastSeedTick { get; internal set; } = long.MinValue;

    // Casilla donde se cuenta (solo plantas, para el límite por casilla).
    internal int TileX, TileY;

    public override string ToString() =>
        $"#{Id} {Def.Name}{(Name.Length > 0 ? " " + Name : "")} ({X:0},{Y:0}){(Dead ? " muerto" : "")}";
}

/// <summary>Cómo llegó una semilla a donde está.</summary>
public enum SeedOrigin
{
    /// <summary>Cayó cerca de su madre (semilla normal, o un fruto/abrojo que nadie se llevó).</summary>
    Parent,
    Wind,
    /// <summary>Pasó por el estómago de un animal que se comió el fruto.</summary>
    Gut,
    /// <summary>La soltó un animal que llevaba pegado un abrojo.</summary>
    Burr,
}

/// <summary>Semillas que un animal lleva en el estómago o en un abrojo pegado.</summary>
internal sealed class SeedPacket(Creature parent, int seeds, long nextTick)
{
    /// <summary>La planta madre (puede haber muerto; sus genes siguen sirviendo).</summary>
    public Creature Parent { get; } = parent;
    public int Seeds { get; set; } = seeds;
    /// <summary>Estómago: cuándo salen todas. Abrojo: cuándo cae la próxima.</summary>
    public long NextTick { get; set; } = nextTick;
}
