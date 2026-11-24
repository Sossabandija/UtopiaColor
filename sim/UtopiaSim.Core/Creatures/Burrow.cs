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
}
