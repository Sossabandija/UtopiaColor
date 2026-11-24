namespace UtopiaSim.Tests;

public class MetricsTests
{
    [Fact]
    public void Measuring_DoesNotChangeTheRun()
    {
        var plain = new Simulation(SimulationTests.Original(), seed: 7);
        var measured = new Simulation(SimulationTests.Original(), seed: 7);
        using var metrics = new SimMetrics(measured);
        var probe = new List<Creature>();
        for (int i = 0; i < 4000; i++)
        {
            plain.Step();
            measured.Step();
            if (i % 97 == 0)
            {
                metrics.Sample();
                measured.FindInRadius(1000, 1000, 2000, 0, probe);
            }
        }
        Assert.Equal(SimulationTests.Fingerprint(plain), SimulationTests.Fingerprint(measured));
    }

    [Fact]
    public void DeathTable_AddsUpToTheSimulationCounters()
    {
        var sim = new Simulation(SimulationTests.Original(), seed: 3);
        using var metrics = new SimMetrics(sim);
        long births = 0;
        for (int i = 0; i < 6000; i++)
        {
            sim.Step();
            if (i % 450 == 0) births += metrics.Sample().Births.Sum();
        }
        births += metrics.Sample().Births.Sum();

        Assert.Equal(sim.Stats.Births.Sum(), births);
        for (int s = 0; s < sim.Species.Count; s++)
        {
            foreach (var cause in Enum.GetValues<DeathCause>())
            {
                long total = 0;
                for (int stage = 0; stage < 2; stage++)
                    for (int bin = 0; bin < SimMetrics.AgeBins; bin++)
                        total += metrics.DeathsByAge[s, (int)cause, stage, bin];
                Assert.Equal(sim.Stats.Deaths[s, (int)cause], total);
            }
        }
        long eaten = Enumerable.Range(0, SimMetrics.StageBins).Sum(b => metrics.EatenAtStage[0, b]);
        Assert.Equal(sim.Stats.Deaths[0, (int)DeathCause.Eaten], eaten);
    }

    [Fact]
    public void FoodStats_AreNested()
    {
        var sim = new Simulation(SimulationTests.Original(), seed: 1);
        using var metrics = new SimMetrics(sim);
        for (int i = 0; i < 2000; i++) sim.Step();
        var sample = metrics.Sample();

        Assert.Null(sample.Food[0]); // las plantas no comen
        var herb = sample.Food[1]!.Value;
        Assert.True(herb.Visible > 0);
        Assert.True(herb.RightStage <= herb.Visible);
        Assert.True(herb.Edible <= herb.RightStage);
        Assert.InRange(herb.ColorDistance, 0, 2);
        Assert.Equal(sim.Population(0), sample.Genes[0, 0].Count);
    }

    [Fact]
    public void FindInRadius_MatchesBruteForce()
    {
        var sim = new Simulation(SimulationTests.Original(), seed: 2);
        for (int i = 0; i < 3000; i++) sim.Step();
        var found = new List<Creature>();
        var rng = new Rng(9);
        for (int q = 0; q < 50; q++)
        {
            double x = rng.Range(0, sim.Map.Width), y = rng.Range(0, sim.Map.Width), r = rng.Range(50, 2500);
            for (int s = 0; s < sim.Species.Count; s++)
            {
                sim.FindInRadius(x, y, r, s, found);
                var expected = sim.Creatures.Where(c => c.Species == s && sim.Map.LoopDistance(x, y, c.X, c.Y) <= r)
                    .Select(c => c.Id).Order();
                Assert.Equal(expected, found.Select(c => c.Id).Order());
            }
        }
    }

    [Fact]
    public void AgeBins_CoverAgesWithoutGaps()
    {
        Assert.Equal(0, SimMetrics.AgeBin(0));
        Assert.Equal(1, SimMetrics.AgeBin(1));
        Assert.Equal(2, SimMetrics.AgeBin(3));
        Assert.Equal(3, SimMetrics.AgeBin(4));
        foreach (long t in new long[] { 0, 1, 2, 5, 100, 4500, 123456 })
        {
            var (from, to) = SimMetrics.AgeBinRange(SimMetrics.AgeBin(t));
            Assert.InRange(t, from, to - 1);
        }
    }
}
