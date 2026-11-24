namespace UtopiaSim;

/// <summary>
/// Reparte a los seres en celdas por especie, para que buscar "el más cercano" no tenga que
/// revisar a todos. Se reconstruye al inicio de cada tick; como en la fase de decisiones nadie
/// se mueve, las búsquedas dan exactamente el mismo resultado que revisar a todos.
/// </summary>
internal sealed class SpatialGrid
{
    readonly int n;
    readonly double cellSize;
    readonly List<Creature>[][] cells; // [especie][celda]

    public SpatialGrid(double worldWidth, int cellsPerSide, int speciesCount)
    {
        n = cellsPerSide;
        cellSize = worldWidth / n;
        cells = new List<Creature>[speciesCount][];
        for (int s = 0; s < speciesCount; s++)
        {
            cells[s] = new List<Creature>[n * n];
            for (int i = 0; i < n * n; i++) cells[s][i] = [];
        }
    }

    public void Rebuild(List<Creature> creatures)
    {
        foreach (var perSpecies in cells)
            foreach (var cell in perSpecies)
                cell.Clear();
        foreach (var c in creatures)
            if (!c.Dead) cells[c.Species][CellCoord(c.X) * n + CellCoord(c.Y)].Add(c);
    }

    public List<Creature> Cell(int species, int index) => cells[species][index];

    public double CellSize => cellSize;

    public int CellCount => n * n;

    /// <summary>
    /// Cuántos anillos de celdas alrededor hay que revisar para cubrir <paramref name="radius"/>,
    /// o -1 si el radio cubre el mundo entero (entonces hay que revisar todas las celdas una vez).
    /// </summary>
    public int RingsFor(double radius)
    {
        int k = (int)Math.Ceiling(radius / cellSize);
        return 2 * k + 1 >= n ? -1 : k;
    }

    /// <summary>
    /// Celdas a exactamente <paramref name="ring"/> celdas de distancia (en anillo cuadrado) de la celda de (x, y).
    /// Todo ser en el anillo r está al menos a (r − 1)·CellSize de (x, y).
    /// </summary>
    public void CollectRing(double x, double y, int ring, List<int> output)
    {
        output.Clear();
        int cx = CellCoord(x), cy = CellCoord(y);
        if (ring == 0)
        {
            output.Add(cx * n + cy);
            return;
        }
        for (int d = -ring; d <= ring; d++)
        {
            output.Add(MathUtil.Mod(cx + d, n) * n + MathUtil.Mod(cy - ring, n));
            output.Add(MathUtil.Mod(cx + d, n) * n + MathUtil.Mod(cy + ring, n));
        }
        for (int d = -ring + 1; d <= ring - 1; d++)
        {
            output.Add(MathUtil.Mod(cx - ring, n) * n + MathUtil.Mod(cy + d, n));
            output.Add(MathUtil.Mod(cx + ring, n) * n + MathUtil.Mod(cy + d, n));
        }
    }

    int CellCoord(double v)
    {
        int i = (int)(v / cellSize);
        return i < 0 ? 0 : (i >= n ? n - 1 : i);
    }
}
