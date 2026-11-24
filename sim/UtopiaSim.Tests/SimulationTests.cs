using System.Text;

namespace UtopiaSim.Tests;

public class SimulationTests
{
    internal static string ConfigPath(string name) => Path.Combine(AppContext.BaseDirectory, "configs", name);

    internal static SimConfig Original() => SimConfig.Load(ConfigPath("original.json"));

    internal static string Fingerprint(Simulation sim)
    {
        var sb = new StringBuilder();
        sb.Append(sim.Tick).Append('|');
        for (int s = 0; s < sim.Species.Count; s++) sb.Append(sim.Population(s)).Append(',');
        foreach (var c in sim.Creatures.Take(200))
            sb.Append($"{c.Id}:{c.X:R}:{c.Y:R}:{c.Genes[0].X:R};");
        return sb.ToString();
    }

    [Theory]
    [InlineData("original.json")]
    [InlineData("etapa.json")]
    [InlineData("etapa_refugio.json")]
    [InlineData("dispersion.json")]
    [InlineData("ecosistema.json")]
    public void ShippedConfigsLoad(string file)
    {
        var cfg = SimConfig.Load(ConfigPath(file));
        Assert.Equal(3, cfg.Species.Count);
        _ = new Simulation(cfg);
    }

    [Fact]
    public void SameSeed_SameRun()
    {
        var a = new Simulation(Original(), seed: 42);
        var b = new Simulation(Original(), seed: 42);
        for (int i = 0; i < 3000; i++)
        {
            a.Step();
            b.Step();
        }
        Assert.Equal(Fingerprint(a), Fingerprint(b));
    }

    [Fact]
    public void DifferentSeed_DifferentRun()
    {
        var a = new Simulation(Original(), seed: 1);
        var b = new Simulation(Original(), seed: 2);
        for (int i = 0; i < 500; i++)
        {
            a.Step();
            b.Step();
        }
        Assert.NotEqual(Fingerprint(a), Fingerprint(b));
    }

    [Fact]
    public void OneDay_PopulationsStayConsistent()
    {
        var sim = new Simulation(Original(), seed: 3);
        int born = 0, died = 0;
        sim.EventRaised += e =>
        {
            if (e.Type == SimEventType.Born) born++;
            if (e.Type == SimEventType.Died) died++;
        };
        int start = sim.Creatures.Count;
        for (int i = 0; i < sim.Config.World.TicksPerDay; i++) sim.Step();

        Assert.True(sim.Population(0) > 0, "las plantas no deberían extinguirse en un día");
        Assert.True(sim.Population(1) > 0, "los herbívoros no deberían extinguirse en un día");
        Assert.Equal(start + born - died, sim.Creatures.Count);
        for (int s = 0; s < sim.Species.Count; s++)
            Assert.Equal(sim.Creatures.Count(c => c.Species == s), sim.Population(s));
        Assert.All(sim.Creatures, c => Assert.False(c.Dead));
        Assert.All(sim.Creatures, c =>
        {
            Assert.InRange(c.X, 0, sim.Map.Width);
            Assert.InRange(c.Y, 0, sim.Map.Width);
        });
    }

    [Fact]
    public void PlantsPerTile_NeverExceedsLimitAfterLanding()
    {
        var sim = new Simulation(Original(), seed: 4);
        for (int i = 0; i < 9000; i++) sim.Step();
        int max = sim.Config.World.MaxPlantsPerTile;
        var crowded = sim.Creatures
            .Where(c => c.IsPlant && c.Landed)
            .GroupBy(c => sim.Map.TileOf(c.X, c.Y))
            .Where(g => g.Count() > max)
            .Select(g => $"{g.Key}: {g.Count()}");
        Assert.Empty(crowded);
    }

    [Fact]
    public void CanEat_WindowMatchesOriginal()
    {
        var sim = new Simulation(Original(), seed: 1);
        var herb = sim.Creatures.First(c => c.Def.Name == "herbivoro");
        var plant = sim.Creatures.First(c => c.IsPlant);
        plant.Genes[0] = herb.Genes[0]; // mismo color → ventana completa: 0.5 ± 0.21

        plant.PlantSize = 0.30;
        Assert.True(sim.CanEat(herb, plant));
        plant.PlantSize = 0.70;
        Assert.True(sim.CanEat(herb, plant));
        plant.PlantSize = 0.28;
        Assert.False(sim.CanEat(herb, plant));
        plant.PlantSize = 0.72;
        Assert.False(sim.CanEat(herb, plant));

        // Color opuesto en la rueda (distancia 2): ventana de ancho 0 (solo exactamente 0.5, como el original).
        plant.Genes[0] = new Vec2(-herb.Genes[0].X, -herb.Genes[0].Y) / herb.Genes[0].Length;
        herb.Genes[0] = herb.Genes[0] / herb.Genes[0].Length;
        plant.PlantSize = 0.501;
        Assert.False(sim.CanEat(herb, plant));

        // Una planta no puede comerse a un herbívoro.
        Assert.False(sim.CanEat(plant, herb));
    }

    [Fact]
    public void StageGene_MovesTheWindow()
    {
        var sim = new Simulation(SimConfig.Load(ConfigPath("etapa.json")), seed: 1);
        var herb = sim.Creatures.First(c => c.Def.Name == "herbivoro");
        var plant = sim.Creatures.First(c => c.IsPlant);
        var stage = sim.FindGene("etapa")!;
        plant.Genes[0] = herb.Genes[0];

        herb.Genes[stage.Index] = new Vec2(1, 0); // extremo "tardío": etapa 1
        Assert.Equal(1.0, sim.PreferredStage(herb), 12);
        plant.PlantSize = 0.95;
        Assert.True(sim.CanEat(herb, plant));
        plant.PlantSize = 0.5;
        Assert.False(sim.CanEat(herb, plant));

        herb.Genes[stage.Index] = new Vec2(-1, 0); // extremo "temprano": etapa 0
        plant.PlantSize = 0.1;
        Assert.True(sim.CanEat(herb, plant));
        plant.PlantSize = 0.5;
        Assert.False(sim.CanEat(herb, plant));
    }

    [Fact]
    public void Config_ReportsAllProblemsTogether()
    {
        const string json = """
        {
          "genes": [ { "name": "color", "shape": "disk" }, { "name": "tri", "shape": "polygon", "sides": 1 } ],
          "species": [
            { "name": "a", "kind": "plant", "eats": ["b"] },
            { "name": "b", "kind": "animal", "eats": ["zzz"], "needRates": [1, 2] }
          ]
        }
        """;
        var e = Assert.Throws<ConfigException>(() => SimConfig.Parse(json));
        Assert.Contains("sides", e.Message);
        Assert.Contains("una planta no puede comer", e.Message);
        Assert.Contains("'zzz'", e.Message);
        Assert.Contains("needRates", e.Message);
    }

    [Fact]
    public void Config_RejectsMisspelledKeys()
    {
        var e = Assert.Throws<ConfigException>(() => SimConfig.Parse("""{ "sed": 3, "species": [] }"""));
        Assert.Contains("sed", e.Message);
    }
}
