namespace UtopiaSim;

/// <summary>Contadores acumulados desde el inicio de la simulación.</summary>
public sealed class SimStats
{
    internal SimStats(int speciesCount)
    {
        Births = new long[speciesCount];
        Deaths = new long[speciesCount, Enum.GetValues<DeathCause>().Length];
        Dispersal = new long[speciesCount, Enum.GetValues<DispersalCounter>().Length];
        HuntsFailed = new long[speciesCount];
        Selfed = new long[speciesCount];
        Unpollinated = new long[speciesCount];
        BurrowsDug = new long[speciesCount];
        BurrowsDugFleeing = new long[speciesCount];
        BurrowsCollapsed = new long[speciesCount];
        BurrowsAged = new long[speciesCount];
    }

    /// <summary>Nacimientos por especie (semillas y crías; no cuenta la población inicial).</summary>
    public long[] Births { get; }

    /// <summary>Muertes por [especie, causa].</summary>
    public long[,] Deaths { get; }

    /// <summary>Dispersión de semillas por [especie de planta, contador].</summary>
    public long[,] Dispersal { get; }

    /// <summary>Cazas fallidas por rivales cerca (territorialidad), por especie del cazador.</summary>
    public long[] HuntsFailed { get; }

    /// <summary>Plantas diploides: semillas autopolinizadas (no había vecina que diera polen).</summary>
    public long[] Selfed { get; }

    /// <summary>Plantas diploides sin autopolinización: semillas que no se formaron por falta de polen.</summary>
    public long[] Unpollinated { get; }

    /// <summary>Madrigueras que cavó cada especie (dormidos y al huir).</summary>
    public long[] BurrowsDug { get; }

    /// <summary>De esas, las que cavó de emergencia al huir (dig.fleeDigDays).</summary>
    public long[] BurrowsDugFleeing { get; }

    /// <summary>Madrigueras de cada especie que se derrumbaron (por abandono o por el tiempo).</summary>
    public long[] BurrowsCollapsed { get; }

    /// <summary>De esas, las que se derrumbaron por el tiempo (collapse.halfLifeDays).</summary>
    public long[] BurrowsAged { get; }
}

/// <summary>
/// Contadores de dispersión. Los cuatro primeros cuentan semillas soltadas, en el mismo orden que <see cref="SeedOrigin"/>.
/// </summary>
public enum DispersalCounter
{
    SeedsParent,
    SeedsWind,
    SeedsGut,
    SeedsBurr,
    /// <summary>Semillas destruidas en un estómago.</summary>
    SeedsDigested,
    FruitsEaten,
    /// <summary>Frutos que nadie comió y cayeron bajo la madre.</summary>
    FruitsFallen,
    BurrsAttached,
    /// <summary>Abrojos que nadie se llevó y cayeron bajo la madre.</summary>
    BurrsFallen,
}

public enum SimEventType { Born, Died, Ate, Drank, Mated, AteFruit }

/// <summary>
/// Algo que pasó en un tick. Pensado para el visor (sonidos, partículas).
/// <c>Other</c>: en Born es el padre/madre, en Died quien lo comió, en Ate la presa, en Mated la pareja,
/// en AteFruit la planta (que sigue viva).
/// </summary>
public readonly record struct SimEvent(SimEventType Type, Creature Subject, Creature? Other = null, DeathCause Cause = default);
