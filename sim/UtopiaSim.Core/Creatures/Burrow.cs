namespace UtopiaSim;

/// <summary>
/// Una madriguera: un sitio fijo del mapa donde caben hasta <see cref="Capacity"/> individuos de su especie.
/// El cupo lo ocupan los que están dentro y los que van corriendo hacia ella (ver <see cref="BurrowConfig"/>).
/// </summary>
public sealed class Burrow
{
    internal Burrow(int species, double x, double y, int capacity)
    {
        Species = species;
        X = x;
        Y = y;
        Capacity = capacity;
    }

    public int Species { get; }
    public double X { get; }
    public double Y { get; }
    public int Capacity { get; }
    /// <summary>Cuántos la reclamaron: los que están dentro y los que van en camino.</summary>
    public int Claims { get; internal set; }
    public bool HasRoom => Claims < Capacity;
    /// <summary>True si la cavó un animal durante la corrida (false = de las iniciales).</summary>
    public bool Dug { get; internal init; }
    /// <summary>Último tick en que alguien la tenía reclamada (para derrumbar las abandonadas).</summary>
    public long LastUsedTick { get; internal set; }
    /// <summary>Con burrows.maxColorDistance: el color (gen colorGene) del primero que la reclamó. Null = vacía o sin color.</summary>
    public Vec2? Color { get; internal set; }
    /// <summary>Con collapse.halfLifeDays: tick en que se derrumba por el tiempo (long.MaxValue = nunca).</summary>
    public long CollapseTick { get; internal set; } = long.MaxValue;
}

/// <summary>Lo que queda de una madriguera derrumbada: mientras dure (hasta UntilTick) no se puede cavar otra cerca.</summary>
public readonly record struct Ruin(int Species, double X, double Y, long UntilTick);
