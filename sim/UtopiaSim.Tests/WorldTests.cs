namespace UtopiaSim.Tests;

public class WorldTests
{
    [Fact]
    public void Rng_IsDeterministicAndInRange()
    {
        var a = new Rng(10);
        var b = new Rng(10);
        for (int i = 0; i < 1000; i++)
        {
            double x = a.NextDouble();
            Assert.Equal(x, b.NextDouble());
            Assert.InRange(x, 0, 0.9999999999);
        }
    }

    [Fact]
    public void Noise_RepeatsEveryPeriod()
    {
        var noise = new TileableNoise(new Rng(3));
        var rng = new Rng(4);
        for (int i = 0; i < 500; i++)
        {
            double x = rng.Range(0, 5), y = rng.Range(0, 5);
            Assert.Equal(noise.Noise(x, y, 5), noise.Noise(x + 5, y, 5), 10);
            Assert.Equal(noise.Noise(x, y, 5), noise.Noise(x, y - 5, 5), 10);
        }
    }

    [Fact]
    public void Ground_IsContinuousAcrossTheWrap()
    {
        var map = new WorldMap(new WorldConfig(), new Rng(1));
        for (int i = 0; i < map.Size; i++)
        {
            double y = (i + 0.37) * map.TileSize;
            Assert.Equal(map.GroundLevel(0, y), map.GroundLevel(map.Width - 1e-7, y), 3);
        }
    }

    [Fact]
    public void ClosestWater_IsDrinkable()
    {
        var map = new WorldMap(new WorldConfig(), new Rng(1));
        Assert.True(map.DrinkableTileCount > 0);
        for (int x = 0; x < map.Size; x++)
        {
            for (int y = 0; y < map.Size; y++)
            {
                var p = map.ClosestWaterPoint(x * map.TileSize, y * map.TileSize);
                Assert.True(map.IsDrinkable((int)Math.Round(p.X / map.TileSize), (int)Math.Round(p.Y / map.TileSize)));
            }
        }
    }

    [Fact]
    public void Delta_TakesTheShortWayAroundTheTorus()
    {
        var map = new WorldMap(new WorldConfig(), new Rng(1));
        Assert.Equal(10, map.Delta(10), 9);
        Assert.Equal(-10, map.Delta(map.Width - 10), 9);
        Assert.Equal(20, map.LoopDistance(5, 0, map.Width - 15, 0), 9);
    }
}
