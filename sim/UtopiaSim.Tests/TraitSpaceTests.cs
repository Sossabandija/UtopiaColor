namespace UtopiaSim.Tests;

public class TraitSpaceTests
{
    static IEnumerable<TraitSpace> AllSpaces()
    {
        foreach (var rule in Enum.GetValues<BoundaryRule>())
        {
            yield return new DiskSpace(rule);
            foreach (int sides in new[] { 2, 3, 4, 6, 12 }) yield return new PolygonSpace(sides, rule);
        }
    }

    [Fact]
    public void Mutate_AlwaysStaysInside()
    {
        var rng = new Rng(7);
        foreach (var space in AllSpaces())
        {
            var p = Vec2.Zero;
            for (int i = 0; i < 5000; i++)
            {
                p = space.Mutate(p, rng, maxStep: 0.4, exponent: 1);
                Assert.True(space.Contains(p), $"{space.GetType().Name} {space.Boundary} se salió: {p}");
            }
        }
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(6)]
    public void Clamp_FromCorner_ReturnsToCornerWithProbabilityOneOverN(int sides)
    {
        var space = new PolygonSpace(sides, BoundaryRule.Clamp);
        var rng = new Rng(123);
        var corner = space.Vertices[0];
        int stay = 0, n = 40000;
        for (int i = 0; i < n; i++)
            if ((space.Mutate(corner, rng, 0.05, 1) - corner).Length < 1e-9) stay++;
        Assert.InRange(stay / (double)n, 1.0 / sides - 0.01, 1.0 / sides + 0.01);
    }

    [Fact]
    public void Reject_FromCorner_NeverStaysExactlyInCorner()
    {
        var space = new PolygonSpace(3, BoundaryRule.Reject);
        var rng = new Rng(5);
        var corner = space.Vertices[0];
        for (int i = 0; i < 5000; i++)
            Assert.True((space.Mutate(corner, rng, 0.05, 1) - corner).Length > 1e-12);
    }

    /// <summary>Con deriva neutra larga en un cuadrado: Clamp llena las esquinas, Reject las vacía.</summary>
    [Fact]
    public void LongRunCornerOccupancy_ClampAboveReject()
    {
        double Occupancy(BoundaryRule rule)
        {
            var space = new PolygonSpace(4, rule);
            var rng = new Rng(99);
            int near = 0, total = 0;
            for (int walker = 0; walker < 100; walker++)
            {
                var p = Vec2.Zero;
                for (int t = 0; t < 3000; t++)
                {
                    p = space.Mutate(p, rng, 0.08, 1);
                    if (t <= 500) continue;
                    total++;
                    if (space.Vertices.Any(v => (p - v).Length < 0.15)) near++;
                }
            }
            return near / (double)total;
        }

        double reject = Occupancy(BoundaryRule.Reject);
        double clamp = Occupancy(BoundaryRule.Clamp);
        double uniform = 4 * (Math.PI / 4 * 0.15 * 0.15) / 2.0; // área de las 4 esquinas / área del cuadrado (lado √2)
        Assert.True(reject < uniform, $"reject {reject:0.0000} debería estar bajo lo uniforme {uniform:0.0000}");
        Assert.True(clamp > uniform, $"clamp {clamp:0.0000} debería estar sobre lo uniforme {uniform:0.0000}");
    }

    [Theory]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(8)]
    public void VertexWeights_PartitionOfUnityAndLinearPrecision(int sides)
    {
        var space = new PolygonSpace(sides, BoundaryRule.Clamp);
        var rng = new Rng(11);
        for (int i = 0; i < 2000; i++)
        {
            var p = space.Project(new Vec2(rng.Range(-1, 1), rng.Range(-1, 1)));
            var w = space.VertexWeights(p);
            Assert.Equal(1.0, w.Sum(), 9);
            Assert.All(w, x => Assert.True(x >= -1e-9));
            var rebuilt = Vec2.Zero;
            for (int k = 0; k < sides; k++) rebuilt += space.Vertices[k] * w[k];
            Assert.True((rebuilt - p).Length < 1e-9, $"Σ w·v = {rebuilt}, esperado {p}");
        }
    }

    [Fact]
    public void VertexWeights_CornersAndEdges()
    {
        var tri = new PolygonSpace(3, BoundaryRule.Clamp);
        for (int k = 0; k < 3; k++)
        {
            var w = tri.VertexWeights(tri.Vertices[k]);
            Assert.Equal(1.0, w[k], 12);
        }
        var mid = (tri.Vertices[0] + tri.Vertices[1]) * 0.5;
        var wm = tri.VertexWeights(mid);
        Assert.Equal(0.5, wm[0], 9);
        Assert.Equal(0.5, wm[1], 9);
        Assert.Equal(0.0, wm[2], 9);

        var line = new PolygonSpace(2, BoundaryRule.Clamp);
        Assert.Equal([1.0, 0.0], line.VertexWeights(new Vec2(-1, 0)));
        Assert.Equal([0.0, 1.0], line.VertexWeights(new Vec2(1, 0)));
        Assert.Equal([0.5, 0.5], line.VertexWeights(Vec2.Zero));
        Assert.Equal(0.75, line.LinePosition(new Vec2(0.5, 0)), 12);
    }

    [Fact]
    public void Disk_HueSaturationRoundTrip()
    {
        var p = DiskSpace.FromHueSat(0.7, 0.5);
        Assert.Equal(0.7, DiskSpace.Hue(p), 9);
        Assert.Equal(0.5, DiskSpace.Saturation(p), 9);
        Assert.Equal(-0.15451, p.X, 4);
        Assert.Equal(-0.47553, p.Y, 4);
    }
}
