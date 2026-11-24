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

    /// <summary>Valor de cada gen (índice = <see cref="GeneDef.Index"/>).</summary>
    public Vec2[] Genes { get; }

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
    public long LastSeedTick { get; internal set; } = long.MinValue;

    // Casilla donde se cuenta (solo plantas, para el límite por casilla).
    internal int TileX, TileY;

    public override string ToString() =>
        $"#{Id} {Def.Name}{(Name.Length > 0 ? " " + Name : "")} ({X:0},{Y:0}){(Dead ? " muerto" : "")}";
}
