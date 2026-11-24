namespace UtopiaSim;

/// <summary>
/// Terreno: un mapa de alturas de Size × Size casillas sobre un toro (los bordes se conectan),
/// con un nivel de agua fijo que ondula un poco.
/// </summary>
public sealed class WorldMap
{
    readonly double[,] elev;
    readonly (int X, int Y)[,] closestWater;
    readonly WorldConfig cfg;

    public WorldMap(WorldConfig config, Rng rng)
    {
        cfg = config;
        Size = config.Size;
        TileSize = config.TileSize;
        Width = Size * TileSize;
        WaterLevelPx = config.WaterLevelTiles * TileSize;

        elev = new double[Size, Size];
        var noise = new TileableNoise(rng);
        int period = Math.Max(1, (int)Math.Round(Size * config.NoiseFrequency));
        double scale = (double)period / Size;
        double min = double.MaxValue, max = double.MinValue;
        for (int x = 0; x < Size; x++)
        {
            for (int y = 0; y < Size; y++)
            {
                double v = noise.Fbm(x * scale, y * scale, period, config.NoiseOctaves, config.NoiseFalloff);
                elev[x, y] = v;
                min = Math.Min(min, v);
                max = Math.Max(max, v);
            }
        }
        double range = max - min;
        for (int x = 0; x < Size; x++)
            for (int y = 0; y < Size; y++)
                elev[x, y] = range > 0 ? (elev[x, y] - min) / range * config.ElevationFactor : 0;

        closestWater = new (int, int)[Size, Size];
        for (int x = 0; x < Size; x++)
        {
            for (int y = 0; y < Size; y++)
            {
                closestWater[x, y] = SearchClosestWater(x, y);
                if (IsDrinkable(x, y)) DrinkableTileCount++;
            }
        }
    }

    /// <summary>Casillas por lado.</summary>
    public int Size { get; }
    public double TileSize { get; }
    /// <summary>Ancho (y alto) del mundo en px.</summary>
    public double Width { get; }
    public double WaterLevelPx { get; }
    public int DrinkableTileCount { get; }

    /// <summary>Altura del vértice (x, y) de la grilla, en casillas (0 a ElevationFactor).</summary>
    public double Elevation(int x, int y) => elev[x, y];

    public bool IsDrinkable(int x, int y) => elev[x, y] * TileSize <= WaterLevelPx - cfg.DrinkDepthPx;

    /// <summary>Altura del suelo en px en cualquier punto (interpolación bilineal).</summary>
    public double GroundLevel(double x, double y)
    {
        double xv = Wrap(x) / TileSize, yv = Wrap(y) / TileSize;
        int xi = Math.Min((int)xv, Size - 1), yi = Math.Min((int)yv, Size - 1);
        double xr = xv - xi, yr = yv - yi;
        int x2 = (xi + 1) % Size, y2 = (yi + 1) % Size;
        double e1 = MathUtil.Lerp(elev[xi, yi], elev[xi, y2], yr);
        double e2 = MathUtil.Lerp(elev[x2, yi], elev[x2, y2], yr);
        return TileSize * MathUtil.Lerp(e1, e2, xr);
    }

    /// <summary>Altura de la superficie del agua en px (con olas, igual que el original).</summary>
    public double WaterLevel(double x, double y, long tick) =>
        WaterLevelPx + Math.Sin((x * 3 + y * 2) / Size + tick * MathUtil.Tau / cfg.WavePeriodTicks) * cfg.WaveSizePx;

    /// <summary>Lleva una coordenada a [0, Width).</summary>
    public double Wrap(double v)
    {
        v %= Width;
        if (v < 0) v += Width;
        if (v >= Width) v -= Width;
        return v;
    }

    /// <summary>Diferencia más corta sobre el toro, en (-Width/2, Width/2].</summary>
    public double Delta(double d)
    {
        double half = Width / 2;
        while (d <= -half) d += Width;
        while (d > half) d -= Width;
        return d;
    }

    public double LoopDistance(double x1, double y1, double x2, double y2)
    {
        double dx = Delta(x1 - x2), dy = Delta(y1 - y2);
        return Math.Sqrt(dx * dx + dy * dy);
    }

    public (int X, int Y) TileOf(double x, double y)
    {
        int tx = Math.Min((int)(Wrap(x) / TileSize), Size - 1);
        int ty = Math.Min((int)(Wrap(y) / TileSize), Size - 1);
        return (tx, ty);
    }

    /// <summary>Punto (px) del agua bebible más cercana, precalculado como en el original.</summary>
    public Vec2 ClosestWaterPoint(double x, double y)
    {
        int ix = (int)(Wrap(x) / TileSize + 0.5) % Size;
        int iy = (int)(Wrap(y) / TileSize + 0.5) % Size;
        var (wx, wy) = closestWater[ix, iy];
        return new Vec2(wx * TileSize, wy * TileSize);
    }

    /// <summary>Búsqueda en anillos crecientes, en el mismo orden que el original.</summary>
    (int, int) SearchClosestWater(int x, int y)
    {
        if (IsDrinkable(x, y)) return (x, y);
        for (int dist = 1; dist <= Size / 2; dist++)
        {
            for (int shift = -dist; shift < dist; shift++)
            {
                ReadOnlySpan<(int Dx, int Dy)> deltas = [(dist, shift), (-dist, -shift), (shift, -dist), (-shift, dist)];
                foreach (var (dx, dy) in deltas)
                {
                    int tx = MathUtil.Mod(x + dx, Size), ty = MathUtil.Mod(y + dy, Size);
                    if (IsDrinkable(tx, ty)) return (tx, ty);
                }
            }
        }
        return (x, y);
    }
}
