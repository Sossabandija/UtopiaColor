using System.Text.Json.Nodes;
using static UtopiaSim.Tests.SimulationTests;

namespace UtopiaSim.Tests;

/// <summary>Refugio de las presas y territorialidad de los depredadores.</summary>
public class PredatorPreyTests
{
    static SimConfig OriginalWith(string patch) =>
        SimConfig.FromJson((JsonObject)JsonMerge.Apply(SimConfig.LoadJson(ConfigPath("original.json")),
            SimConfig.ParseObject(patch, "parche"))!, "parche");

    const string WaterRefuge = """{ "name": "herbivoro", "refuge": { "maxHeight": 0.3 } }""";

    /// <summary>El vértice más bajo y el más alto del mapa, en px.</summary>
    static ((double X, double Y) Low, (double X, double Y) High) Extremes(Simulation sim)
    {
        var map = sim.Map;
        (int X, int Y) low = (0, 0), high = (0, 0);
        for (int x = 0; x < map.Size; x++)
        {
            for (int y = 0; y < map.Size; y++)
            {
                if (map.Elevation(x, y) < map.Elevation(low.X, low.Y)) low = (x, y);
                if (map.Elevation(x, y) > map.Elevation(high.X, high.Y)) high = (x, y);
            }
        }
        return ((low.X * map.TileSize, low.Y * map.TileSize), (high.X * map.TileSize, high.Y * map.TileSize));
    }

    [Fact]
    public void Refuge_PreyCannotBeEatenThere()
    {
        var sim = new Simulation(OriginalWith($$"""{ "species": [ {{WaterRefuge}} ] }"""), seed: 1);
        var herb = sim.Creatures.First(c => c.Def.Name == "herbivoro");
        var carn = sim.Creatures.First(c => c.Def.Name == "carnivoro");
        herb.Genes[0] = carn.Genes[0]; // mismo color
        herb.Needs[Need.Food] = 0.5;  // peso justo en el centro de la ventana del carnívoro
        var (low, high) = Extremes(sim);

        (herb.X, herb.Y) = low;
        Assert.True(sim.InRefuge(herb));
        Assert.False(sim.CanEat(carn, herb));

        (herb.X, herb.Y) = high;
        Assert.False(sim.InRefuge(herb));
        Assert.True(sim.CanEat(carn, herb));
    }

    [Fact]
    public void Refuge_FleeingPreyRunsToIt()
    {
        var sim = new Simulation(OriginalWith($$"""
            { "species": [ { "name": "planta", "initialCount": 0 }, {{WaterRefuge}}, { "name": "herbivoro", "initialCount": 1 },
                           { "name": "carnivoro", "initialCount": 1 } ] }
            """), seed: 1);
        var herb = sim.Creatures.Single(c => c.Def.Name == "herbivoro");
        var carn = sim.Creatures.Single(c => c.Def.Name == "carnivoro");
        var (_, high) = Extremes(sim);
        (herb.X, herb.Y) = high;
        (carn.X, carn.Y) = (high.X + sim.Map.TileSize, high.Y); // a una casilla

        for (int i = 0; i < 40 && herb.TopNeed != Need.Safety; i++)
        {
            Array.Fill(herb.Needs, 1.0);
            Array.Fill(carn.Needs, 1.0);
            sim.Step();
        }
        Assert.Equal(Need.Safety, herb.TopNeed);
        Assert.Equal(TargetKind.Refuge, herb.TargetKind);
        Assert.True(sim.Map.NormalizedHeight(herb.TargetPoint.X, herb.TargetPoint.Y) <= 0.3);
    }

    [Fact]
    public void Territory_CrowdedCarnivoresDoNotBreed()
    {
        var sim = new Simulation(OriginalWith("""
            { "species": [ { "name": "herbivoro", "initialCount": 120 },
                           { "name": "carnivoro", "initialCount": 40, "territory": { "radiusTiles": 6, "maxRivalsToBreed": 2 } } ] }
            """), seed: 3);
        // Todos los carnívoros parten juntos.
        foreach (var c in sim.Creatures.Where(c => c.Def.Name == "carnivoro")) (c.X, c.Y) = (1000, 1000);
        int matings = 0, crowdedSeen = 0;
        sim.EventRaised += e =>
        {
            if (e.Type != SimEventType.Mated || e.Subject.Def.Name != "carnivoro") return;
            matings++;
            Assert.False(e.Subject.TooCrowdedToBreed || e.Other!.TooCrowdedToBreed, "criaron con demasiados rivales cerca");
        };
        for (int i = 0; i < sim.Config.World.TicksPerDay; i++)
        {
            sim.Step();
            crowdedSeen += sim.Creatures.Count(c => c.TooCrowdedToBreed);
        }
        Assert.True(crowdedSeen > 0);
    }

    [Fact]
    public void Territory_LoneHunterNeverFails()
    {
        var sim = new Simulation(OriginalWith("""
            { "species": [ { "name": "carnivoro", "initialCount": 1, "territory": { "radiusTiles": 40, "interference": 5 } } ] }
            """), seed: 2);
        for (int i = 0; i < sim.Config.World.TicksPerDay; i++) sim.Step();
        Assert.Equal(0, sim.Stats.HuntsFailed[2]);
    }

    [Fact]
    public void Territory_CrowdedHuntersFail_AndWaitBeforeHuntingAgain()
    {
        var sim = new Simulation(OriginalWith("""
            { "species": [ { "name": "herbivoro", "initialCount": 80 },
                           { "name": "carnivoro", "initialCount": 40, "territory": { "radiusTiles": 6, "interference": 1, "failCooldownDays": 0.1 } } ] }
            """), seed: 2);
        int meals = 0;
        sim.EventRaised += e =>
        {
            if (e.Type != SimEventType.Ate || e.Subject.Def.Name != "carnivoro") return;
            meals++;
            Assert.True(sim.Tick >= e.Subject.HuntCooldownUntil, "comió mientras esperaba tras una caza fallida");
        };
        for (int i = 0; i < sim.Config.World.TicksPerDay; i++) sim.Step();

        Assert.True(sim.Stats.HuntsFailed[2] > 5, $"solo {sim.Stats.HuntsFailed[2]} cazas fallidas");
        Assert.True(meals > 5, $"solo {meals} cazas exitosas");
    }
}
