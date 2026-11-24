namespace UtopiaSim;

/// <summary>Contadores acumulados desde el inicio de la simulación.</summary>
public sealed class SimStats
{
    internal SimStats(int speciesCount)
    {
        Births = new long[speciesCount];
        Deaths = new long[speciesCount, Enum.GetValues<DeathCause>().Length];
    }

    /// <summary>Nacimientos por especie (semillas y crías; no cuenta la población inicial).</summary>
    public long[] Births { get; }

    /// <summary>Muertes por [especie, causa].</summary>
    public long[,] Deaths { get; }
}

public enum SimEventType { Born, Died, Ate, Drank, Mated }

/// <summary>
/// Algo que pasó en un tick. Pensado para el visor (sonidos, partículas).
/// <c>Other</c>: en Born es el padre/madre, en Died quien lo comió, en Ate la presa, en Mated la pareja.
/// </summary>
public readonly record struct SimEvent(SimEventType Type, Creature Subject, Creature? Other = null, DeathCause Cause = default);
