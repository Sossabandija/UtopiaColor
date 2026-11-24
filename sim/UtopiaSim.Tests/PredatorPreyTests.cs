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

    // ------------------------------------------------------------------ madrigueras

    [Fact]
    public void Burrows_OnDryLand_Spaced_AndDoNotChangeTheStart()
    {
        var with = new Simulation(OriginalWith("""{ "species": [ { "name": "herbivoro", "burrows": { "count": 20 } } ] }"""), seed: 2);
        var without = new Simulation(OriginalWith("{}"), seed: 2);

        Assert.Empty(without.Burrows);
        Assert.Equal(20, with.Burrows.Count);
        double spacing = with.Config.Species[1].Burrows!.MinSpacingTiles * with.Map.TileSize;
        foreach (var b in with.Burrows)
        {
            Assert.True(with.Map.GroundLevel(b.X, b.Y) > with.Map.WaterLevelPx, "madriguera bajo el agua");
            Assert.All(with.Burrows.Where(o => o != b), o => Assert.True(with.Map.LoopDistance(b.X, b.Y, o.X, o.Y) >= spacing));
        }
        // Las madrigueras usan su propio generador: el mapa y la población inicial son los mismos.
        Assert.Equal(without.Creatures.Select(c => (c.X, c.Y)), with.Creatures.Select(c => (c.X, c.Y)));
    }

    /// <summary>Dos herbívoros junto a una madriguera de cupo 1 y un carnívoro (sin hambre) a una casilla.</summary>
    static (Simulation Sim, Creature A, Creature B, Creature Carn, Burrow Burrow) OneBurrowScene()
    {
        var sim = new Simulation(OriginalWith("""
            { "species": [ { "name": "planta", "initialCount": 0 },
                           { "name": "herbivoro", "initialCount": 2, "burrows": { "count": 1, "capacity": 1, "searchTiles": 20 } },
                           { "name": "carnivoro", "initialCount": 1 } ] }
            """), seed: 1);
        var burrow = sim.Burrows.Single();
        var herbs = sim.Creatures.Where(c => c.Def.Name == "herbivoro").ToArray();
        var carn = sim.Creatures.Single(c => c.Def.Name == "carnivoro");
        double t = sim.Map.TileSize;
        (herbs[0].X, herbs[0].Y) = (burrow.X + 2 * t, burrow.Y);
        (herbs[1].X, herbs[1].Y) = (burrow.X + 2 * t, burrow.Y + 0.3 * t);
        (carn.X, carn.Y) = (burrow.X + 3 * t, burrow.Y);
        return (sim, herbs[0], herbs[1], carn, burrow);
    }

    /// <summary>Avanza hasta <paramref name="until"/>, sin hambre ni sed ni sueño; el miedo lo calcula la simulación.</summary>
    static void StepCalm(Simulation sim, Func<bool> until, params Creature[] who)
    {
        for (int i = 0; i < 60 && !until(); i++)
        {
            foreach (var c in who)
                for (int n = 0; n < Need.Count; n++)
                    if (n != Need.Safety) c.Needs[n] = 1;
            sim.Step();
        }
    }

    [Fact]
    public void Burrow_FleeingPreyClaimsIt_UpToItsCapacity()
    {
        var (sim, a, b, carn, burrow) = OneBurrowScene();
        StepCalm(sim, () => a.TopNeed == Need.Safety && b.TopNeed == Need.Safety, a, b, carn);

        Assert.Equal(Need.Safety, a.TopNeed);
        Assert.Equal(Need.Safety, b.TopNeed);
        Assert.Equal(1, burrow.Claims);
        var (inside, outside) = a.Burrow == burrow ? (a, b) : (b, a);
        Assert.Same(burrow, inside.Burrow);
        Assert.Equal(TargetKind.Refuge, inside.TargetKind);
        Assert.Equal(new Vec2(burrow.X, burrow.Y), inside.TargetPoint);
        Assert.Null(outside.Burrow);                          // no hay cupo: huye como siempre
        Assert.Equal(TargetKind.Creature, outside.TargetKind);
    }

    [Fact]
    public void Burrow_HidesPrey_AndIsFreedWhenTheDangerPasses()
    {
        var (sim, a, b, carn, burrow) = OneBurrowScene();
        StepCalm(sim, () => burrow.Claims == 1, a, b, carn);
        var hidden = a.Burrow == burrow ? a : b;

        hidden.Genes[0] = carn.Genes[0]; // mismo color
        hidden.Needs[Need.Food] = 0.5;   // peso justo en el centro de la ventana del carnívoro
        (hidden.X, hidden.Y) = (burrow.X, burrow.Y);
        Assert.True(sim.InBurrow(hidden));
        Assert.True(sim.InRefuge(hidden));
        Assert.False(sim.CanEat(carn, hidden));

        // El carnívoro se va lejos: cuando deja de verlo, sale y libera el cupo.
        (carn.X, carn.Y) = (burrow.X + sim.Map.Width / 2, burrow.Y + sim.Map.Width / 2);
        StepCalm(sim, () => hidden.Burrow == null, a, b, carn);
        Assert.Null(hidden.Burrow);
        Assert.Equal(0, burrow.Claims);
        Assert.False(sim.InRefuge(hidden));
    }

    // ------------------------------------------------------------------ cobertura (plantas grandes)

    /// <summary>
    /// Una planta grande, dos herbívoros a una casilla de ella (cupo 1) y un carnívoro sin hambre a dos casillas de la planta
    /// (a una de los herbívoros). Escondidos, el carnívoro los ve a <paramref name="visibleTiles"/> o menos.
    /// </summary>
    static (Simulation Sim, Creature Plant, Creature A, Creature B, Creature Carn) OnePlantScene(double visibleTiles = 0.5)
    {
        var sim = new Simulation(OriginalWith($$"""
            { "species": [ { "name": "planta", "initialCount": 1 },
                           { "name": "herbivoro", "initialCount": 2, "cover": { "visibleTiles": {{visibleTiles.ToString(System.Globalization.CultureInfo.InvariantCulture)}} } },
                           { "name": "carnivoro", "initialCount": 1 } ] }
            """), seed: 1);
        var plant = sim.Creatures.Single(c => c.IsPlant);
        plant.Landed = true;
        plant.PlantSize = 0.9;
        var herbs = sim.Creatures.Where(c => c.Def.Name == "herbivoro").ToArray();
        var carn = sim.Creatures.Single(c => c.Def.Name == "carnivoro");
        double t = sim.Map.TileSize;
        (herbs[0].X, herbs[0].Y) = (plant.X + t, plant.Y);
        (herbs[1].X, herbs[1].Y) = (plant.X + t, plant.Y + 0.2 * t);
        (carn.X, carn.Y) = (plant.X + 2 * t, plant.Y);
        return (sim, plant, herbs[0], herbs[1], carn);
    }

    [Fact]
    public void Cover_FleeingPreyHidesInABigPlant_UpToItsCapacity()
    {
        var (sim, plant, a, b, carn) = OnePlantScene();
        StepCalm(sim, () => a.TopNeed == Need.Safety && b.TopNeed == Need.Safety && a.Predator != null && b.Predator != null,
            a, b, carn);

        Assert.Equal(1, plant.CoverClaims);
        var (hider, other) = a.CoverPlant == plant ? (a, b) : (b, a);
        Assert.Same(plant, hider.CoverPlant);
        Assert.Equal(TargetKind.Refuge, hider.TargetKind);
        Assert.Equal(new Vec2(plant.X, plant.Y), hider.TargetPoint);
        Assert.Null(other.CoverPlant); // sin cupo: huye como siempre
        Assert.Equal(TargetKind.Creature, other.TargetKind);
    }

    [Fact]
    public void Cover_HuntersSeeHiddenPreyOnlyUpClose_AndItIsNotInvulnerable()
    {
        var (sim, plant, a, b, carn) = OnePlantScene();
        StepCalm(sim, () => plant.CoverClaims == 1, a, b, carn);
        var hider = a.CoverPlant == plant ? a : b;
        hider.Genes[0] = carn.Genes[0]; // mismo color
        hider.Needs[Need.Food] = 0.5;  // peso justo en el centro de la ventana del carnívoro
        (hider.X, hider.Y) = (plant.X, plant.Y);
        double t = sim.Map.TileSize;

        Assert.True(sim.InCover(hider));
        Assert.False(sim.InRefuge(hider)); // la cobertura no es un refugio: si lo alcanzan, lo cazan
        Assert.True(sim.CanEat(carn, hider));
        (carn.X, carn.Y) = (plant.X + 3 * t, plant.Y);
        Assert.False(sim.CanSpot(carn, hider));
        (carn.X, carn.Y) = (plant.X + 0.4 * t, plant.Y); // a menos de visibleTiles (0.5)
        Assert.True(sim.CanSpot(carn, hider));

        // Fuera de la planta se lo ve desde lejos, como siempre.
        (hider.X, hider.Y) = (plant.X + 0.5 * t, plant.Y + 0.5 * t);
        (carn.X, carn.Y) = (plant.X + 4 * t, plant.Y);
        Assert.False(sim.InCover(hider));
        Assert.True(sim.CanSpot(carn, hider));
    }

    [Fact]
    public void Cover_IsLostWhenThePlantShrinks()
    {
        var (sim, plant, a, b, carn) = OnePlantScene();
        StepCalm(sim, () => plant.CoverClaims == 1, a, b, carn);
        var hider = a.CoverPlant == plant ? a : b;
        (hider.X, hider.Y) = (plant.X, plant.Y);
        Assert.True(sim.InCover(hider));

        plant.PlantSize = 0.5; // soltó semilla (o la mordieron): ya no esconde a nadie
        Assert.False(sim.InCover(hider));
        StepCalm(sim, () => hider.CoverPlant == null, a, b, carn);
        Assert.Null(hider.CoverPlant);
        Assert.Equal(0, plant.CoverClaims);
    }

    [Fact]
    public void Cover_NotUsedWhenThePredatorIsAlreadyCloseEnoughToSeeIt()
    {
        // Con visibleTiles 2 y el carnívoro a una casilla, esconderse no sirve: huye en vez de quedarse quieto.
        var (sim, plant, a, b, carn) = OnePlantScene(visibleTiles: 2);
        StepCalm(sim, () => a.TopNeed == Need.Safety && b.TopNeed == Need.Safety && a.Predator != null && b.Predator != null,
            a, b, carn);

        Assert.Equal(0, plant.CoverClaims);
        Assert.Null(a.CoverPlant);
        Assert.Null(b.CoverPlant);
        Assert.Equal(TargetKind.Creature, a.TargetKind);
    }
}
