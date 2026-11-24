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

    // ------------------------------------------------------------------ llegada tardía

    [Fact]
    public void ArrivalDay_SpeciesAppearsLater_WithoutChangingTheStart()
    {
        var late = new Simulation(OriginalWith("""{ "species": [ { "name": "carnivoro", "arrivalDay": 0.01 } ] }"""), seed: 5);
        var original = new Simulation(Original(), seed: 5);
        int arrival = (int)Math.Round(0.01 * late.Config.World.TicksPerDay);

        Assert.Equal(0, late.Population(2));
        Assert.False(late.HasArrived(2));
        Assert.True(late.HasArrived(1));
        // Sin los carnívoros, el resto parte igual que siempre (los carnívoros se sortean al final).
        Assert.Equal(original.Creatures.Where(c => c.Species != 2).Select(c => (c.X, c.Y)), late.Creatures.Select(c => (c.X, c.Y)));

        for (int i = 0; i < arrival; i++) late.Step();
        Assert.True(late.HasArrived(2));
        Assert.Equal(late.Config.Species[2].InitialCount, late.Population(2));
        Assert.All(late.Creatures.Where(c => c.Species == 2), c => Assert.Equal(arrival, c.BirthTick));
    }

    // ------------------------------------------------------------------ cavar madrigueras

    /// <summary>Herbívoros solos (sin plantas ni carnívoros) que cavan; parten de noche, así que duermen.</summary>
    static Simulation Diggers(int herbivores, string dig) => new(OriginalWith($$"""
        { "species": [ { "name": "planta", "initialCount": 0 },
                       { "name": "herbivoro", "initialCount": {{herbivores}}, "burrows": { "count": 0, "dig": {{dig}} } },
                       { "name": "carnivoro", "initialCount": 0 } ] }
        """), seed: 1);

    /// <summary>Avanza sin hambre, sed ni ganas de pareja: de noche, lo más urgente es dormir.</summary>
    static void StepAsleep(Simulation sim, int ticks)
    {
        for (int i = 0; i < ticks; i++)
        {
            foreach (var c in sim.Creatures)
                foreach (int n in new[] { Need.Food, Need.Water, Need.Mate, Need.Care })
                    c.Needs[n] = 1;
            sim.Step();
        }
    }

    [Fact]
    public void Dig_ASleepingAnimalDigsABurrowWhereItSleeps()
    {
        var sim = Diggers(1, """{ "digDays": 0.02 }""");
        var herb = sim.Creatures.Single();
        var (_, high) = Extremes(sim); // tierra seca
        (herb.X, herb.Y) = high;
        int needed = sim.DigTicksNeeded(sim.Config.Species[1].Burrows!);

        StepAsleep(sim, needed + 2 * sim.Config.Movement.TickBucketCount);

        Assert.Equal(Need.Rest, herb.TopNeed);
        var burrow = Assert.Single(sim.Burrows);
        Assert.True(burrow.Dug);
        Assert.True(sim.Map.LoopDistance(burrow.X, burrow.Y, herb.X, herb.Y) <= 0.5 * sim.Map.TileSize);
        Assert.Equal(1, sim.Stats.BurrowsDug[1]);

        // Durmiendo junto a su madriguera no cava otra (quedaría a menos de minSpacingTiles).
        StepAsleep(sim, 2 * needed);
        Assert.Single(sim.Burrows);
    }

    [Fact]
    public void Dig_NeverMoreThanMaxCount()
    {
        var sim = Diggers(40, """{ "digDays": 0.02, "maxCount": 3 }""");
        StepAsleep(sim, 3 * sim.DigTicksNeeded(sim.Config.Species[1].Burrows!) + 2 * sim.Config.Movement.TickBucketCount);
        Assert.Equal(3, sim.Burrows.Count);
    }

    [Fact]
    public void Dig_AbandonedBurrowsCollapse_UsedOnesDoNot()
    {
        var sim = new Simulation(OriginalWith("""
            { "species": [ { "name": "planta", "initialCount": 0 },
                           { "name": "herbivoro", "initialCount": 0, "burrows": { "count": 2, "dig": { "abandonDays": 0.01 } } },
                           { "name": "carnivoro", "initialCount": 0 } ] }
            """), seed: 1);
        Assert.Equal(2, sim.Burrows.Count);
        sim.Burrows[0].Claims = 1; // alguien la tiene reclamada
        for (int i = 0; i < 0.02 * sim.Config.World.TicksPerDay; i++) sim.Step();

        Assert.Single(sim.Burrows);
        Assert.Equal(1, sim.Burrows[0].Claims);
        Assert.Equal(1, sim.Stats.BurrowsCollapsed[1]);
    }

    // ------------------------------------------------------------------ cavar al huir, color y lugar de plantas

    /// <summary>
    /// Un herbívoro en tierra seca, sin madrigueras, que cava de emergencia; un carnívoro (dormido: parten de noche)
    /// a <paramref name="carnTiles"/> casillas.
    /// </summary>
    static (Simulation Sim, Creature Herb, Creature Carn) FleeDigScene(double carnTiles)
    {
        var sim = new Simulation(OriginalWith("""
            { "species": [ { "name": "planta", "initialCount": 0 },
                           { "name": "herbivoro", "initialCount": 1,
                             "burrows": { "count": 0, "dig": { "fleeDigDays": 0.005, "fleeSafeTiles": 2 } } },
                           { "name": "carnivoro", "initialCount": 1 } ] }
            """), seed: 1);
        var herb = sim.Creatures.Single(c => c.Def.Name == "herbivoro");
        var carn = sim.Creatures.Single(c => c.Def.Name == "carnivoro");
        var (_, high) = Extremes(sim); // tierra seca
        (herb.X, herb.Y) = high;
        (carn.X, carn.Y) = (high.X + carnTiles * sim.Map.TileSize, high.Y);
        return (sim, herb, carn);
    }

    static void StepCalmFor(Simulation sim, int ticks, Func<bool> until, params Creature[] who)
    {
        for (int i = 0; i < ticks && !until(); i++)
        {
            foreach (var c in who)
                for (int n = 0; n < Need.Count; n++)
                    if (n != Need.Safety) c.Needs[n] = 1;
            sim.Step();
        }
    }

    [Fact]
    public void FleeDig_ThreatenedPreyStopsToDig_AndHidesInIt()
    {
        var (sim, herb, carn) = FleeDigScene(carnTiles: 3);
        StepCalmFor(sim, 100, () => herb.FleeDigging, herb, carn);
        Assert.True(herb.FleeDigging);
        Assert.Equal(Need.Safety, herb.TopNeed);
        var (x, y) = (herb.X, herb.Y);

        StepCalmFor(sim, 200, () => sim.Burrows.Count > 0 && sim.InBurrow(herb), herb, carn);

        var burrow = Assert.Single(sim.Burrows);
        Assert.True(burrow.Dug);
        Assert.Same(burrow, herb.Burrow);
        Assert.True(sim.InBurrow(herb));
        Assert.True(sim.Map.LoopDistance(burrow.X, burrow.Y, x, y) <= 0.5 * sim.Map.TileSize, "no se quedó quieto cavando");
        Assert.Equal(1, sim.Stats.BurrowsDugFleeing[1]);
        Assert.Equal(1, sim.Stats.BurrowsDug[1]);
    }

    [Fact]
    public void FleeDig_StopsAndRunsWhenThePredatorComesClose()
    {
        var (sim, herb, carn) = FleeDigScene(carnTiles: 3);
        StepCalmFor(sim, 100, () => herb.FleeDigging && herb.DigTicks > 3, herb, carn);
        Assert.True(herb.FleeDigging);

        (carn.X, carn.Y) = (herb.X + 1.5 * sim.Map.TileSize, herb.Y); // más cerca que fleeSafeTiles
        StepCalmFor(sim, 1, () => false, herb, carn);

        Assert.False(herb.FleeDigging);
        Assert.Equal(0, herb.DigTicks);
        Assert.Empty(sim.Burrows);
        Assert.Equal(TargetKind.Creature, herb.TargetKind); // huye del carnívoro
        Assert.Same(carn, herb.TargetCreature);
    }

    [Fact]
    public void FleeDig_NotStartedWhenThePredatorIsAlreadyClose()
    {
        var (sim, herb, carn) = FleeDigScene(carnTiles: 1.5);
        StepCalmFor(sim, 60, () => false, herb, carn);
        Assert.Equal(Need.Safety, herb.TopNeed);
        Assert.Equal(0, sim.Stats.BurrowsDugFleeing[1]);
    }

    [Fact]
    public void BurrowColor_FirstClaimantSetsIt_OthersMustMatch()
    {
        var sim = new Simulation(OriginalWith("""
            { "species": [ { "name": "planta", "initialCount": 0 },
                           { "name": "herbivoro", "initialCount": 2,
                             "burrows": { "count": 1, "capacity": 2, "searchTiles": 20, "maxColorDistance": 0.3 } },
                           { "name": "carnivoro", "initialCount": 1 } ] }
            """), seed: 1);
        var burrow = sim.Burrows.Single();
        var herbs = sim.Creatures.Where(c => c.Def.Name == "herbivoro").ToArray();
        var carn = sim.Creatures.Single(c => c.Def.Name == "carnivoro");
        double t = sim.Map.TileSize;
        (herbs[0].X, herbs[0].Y) = (burrow.X + 2 * t, burrow.Y);
        (herbs[1].X, herbs[1].Y) = (burrow.X + 2 * t, burrow.Y + 0.3 * t);
        (carn.X, carn.Y) = (burrow.X + 3 * t, burrow.Y);
        herbs[0].Genes[0] = new Vec2(0.5, 0);
        herbs[1].Genes[0] = new Vec2(-0.5, 0); // a 1 de distancia: no la comparten

        StepCalm(sim, () => herbs.All(h => h.TopNeed == Need.Safety) && burrow.Claims > 0, herbs[0], herbs[1], carn);
        Assert.Equal(1, burrow.Claims);
        var (inside, outside) = herbs[0].Burrow == burrow ? (herbs[0], herbs[1]) : (herbs[1], herbs[0]);
        Assert.Equal(inside.Genes[0], burrow.Color);
        Assert.Null(outside.Burrow);
        Assert.False(sim.BurrowAdmits(burrow, outside));

        // Con un color parecido sí la dejan entrar.
        outside.Genes[0] = inside.Genes[0] + new Vec2(0.1, 0.1);
        Assert.True(sim.BurrowAdmits(burrow, outside));
        StepCalm(sim, () => burrow.Claims == 2, herbs[0], herbs[1], carn);
        Assert.Equal(2, burrow.Claims);

        // Pasado el peligro salen los dos y la madriguera pierde el color.
        (carn.X, carn.Y) = (burrow.X + sim.Map.Width / 2, burrow.Y + sim.Map.Width / 2);
        StepCalm(sim, () => burrow.Claims == 0, herbs[0], herbs[1], carn);
        Assert.Equal(0, burrow.Claims);
        Assert.Null(burrow.Color);
    }

    [Fact]
    public void BurrowPlantSlots_TileHoldsFewerPlants_UntilTheBurrowCollapses()
    {
        var sim = new Simulation(OriginalWith("""
            { "species": [ { "name": "planta", "initialCount": 0 },
                           { "name": "herbivoro", "initialCount": 0,
                             "burrows": { "count": 2, "plantSlots": 1, "dig": { "abandonDays": 0.01 } } },
                           { "name": "carnivoro", "initialCount": 0 } ] }
            """), seed: 1);
        int max = sim.Config.World.MaxPlantsPerTile;
        var kept = sim.Burrows[0];
        var lost = sim.Burrows[1];
        var keptTile = sim.Map.TileOf(kept.X, kept.Y);
        var lostTile = sim.Map.TileOf(lost.X, lost.Y);
        Assert.Equal(max - 1, sim.PlantCapacity(keptTile.X, keptTile.Y));
        Assert.Equal(max - 1, sim.PlantCapacity(lostTile.X, lostTile.Y));
        var (_, high) = Extremes(sim);
        var freeTile = sim.Map.TileOf(high.X + sim.Map.Width / 2, high.Y + sim.Map.Width / 2);
        if (freeTile != keptTile && freeTile != lostTile) Assert.Equal(max, sim.PlantCapacity(freeTile.X, freeTile.Y));

        kept.Claims = 1; // alguien la tiene reclamada: no se derrumba
        for (int i = 0; i < 0.02 * sim.Config.World.TicksPerDay; i++) sim.Step();

        Assert.Same(kept, Assert.Single(sim.Burrows));
        Assert.Equal(max - 1, sim.PlantCapacity(keptTile.X, keptTile.Y));
        Assert.Equal(max, sim.PlantCapacity(lostTile.X, lostTile.Y));
    }

    // ------------------------------------------------------------------ derrumbe por el tiempo, ruinas y tierra revuelta

    [Fact]
    public void Collapse_HalfTheBurrowsFallWithinTheirHalfLife()
    {
        var sim = new Simulation(OriginalWith("""
            { "species": [ { "name": "planta", "initialCount": 0 },
                           { "name": "herbivoro", "initialCount": 0,
                             "burrows": { "count": 80, "minSpacingTiles": 1, "collapse": { "halfLifeDays": 0.05 } } },
                           { "name": "carnivoro", "initialCount": 0 } ] }
            """), seed: 1);
        Assert.Equal(80, sim.Burrows.Count);
        for (int i = 0; i < 0.05 * sim.Config.World.TicksPerDay; i++) sim.Step();
        Assert.InRange(sim.Burrows.Count, 25, 55); // ~40
        Assert.Equal(80 - sim.Burrows.Count, sim.Stats.BurrowsAged[1]);
        for (int i = 0; i < 0.5 * sim.Config.World.TicksPerDay; i++) sim.Step();
        Assert.True(sim.Burrows.Count <= 2, $"quedan {sim.Burrows.Count} tras 10 vidas medias");
    }

    [Fact]
    public void Collapse_OccupantsComeOut()
    {
        var sim = new Simulation(OriginalWith("""
            { "species": [ { "name": "planta", "initialCount": 0 },
                           { "name": "herbivoro", "initialCount": 2,
                             "burrows": { "count": 1, "capacity": 2, "searchTiles": 20, "collapse": { "halfLifeDays": 1000 } } },
                           { "name": "carnivoro", "initialCount": 1 } ] }
            """), seed: 1);
        var burrow = sim.Burrows.Single();
        var herbs = sim.Creatures.Where(c => c.Def.Name == "herbivoro").ToArray();
        var carn = sim.Creatures.Single(c => c.Def.Name == "carnivoro");
        double t = sim.Map.TileSize;
        (herbs[0].X, herbs[0].Y) = (burrow.X + 2 * t, burrow.Y);
        (herbs[1].X, herbs[1].Y) = (burrow.X + 2 * t, burrow.Y + 0.3 * t);
        (carn.X, carn.Y) = (burrow.X + 3 * t, burrow.Y);
        StepCalm(sim, () => burrow.Claims == 2, herbs[0], herbs[1], carn);
        Assert.Equal(2, burrow.Claims);

        burrow.CollapseTick = sim.Tick + 1;
        StepCalmFor(sim, 1, () => false, herbs[0], herbs[1], carn);

        Assert.Empty(sim.Burrows);
        Assert.All(herbs, h => Assert.Null(h.Burrow));
        Assert.All(herbs, h => Assert.False(sim.InRefuge(h)));
        Assert.Equal(1, sim.Stats.BurrowsAged[1]);
    }

    [Fact]
    public void Collapse_RuinBlocksDiggingNearby_UntilItFades()
    {
        var sim = new Simulation(OriginalWith("""
            { "species": [ { "name": "planta", "initialCount": 0 },
                           { "name": "herbivoro", "initialCount": 1,
                             "burrows": { "count": 1, "dig": { "digDays": 0.02, "abandonDays": null }, "collapse": { "halfLifeDays": 1000, "ruinDays": 0.2 } } },
                           { "name": "carnivoro", "initialCount": 0 } ] }
            """), seed: 1);
        var burrow = sim.Burrows.Single();
        var herb = sim.Creatures.Single();
        (herb.X, herb.Y) = (burrow.X + 0.5 * sim.Map.TileSize, burrow.Y); // duerme al lado

        burrow.CollapseTick = 1;
        StepAsleep(sim, 1);
        Assert.Empty(sim.Burrows);
        var ruin = Assert.Single(sim.RuinsOf(1));
        Assert.Equal((burrow.X, burrow.Y), (ruin.X, ruin.Y));

        // Mientras dure la ruina no cava ahí (tendría tiempo de sobra: digDays es 0.02).
        StepAsleep(sim, (int)(0.15 * sim.Config.World.TicksPerDay));
        Assert.Empty(sim.Burrows);

        // Cuando la ruina se olvida, cava.
        StepAsleep(sim, (int)(0.1 * sim.Config.World.TicksPerDay));
        Assert.Empty(sim.RuinsOf(1));
        Assert.Single(sim.Burrows);
    }

    [Fact]
    public void Collapse_TilledSoilHoldsMorePlants_UpToTheMax()
    {
        // Duerme en el mismo lugar: cava, la madriguera se derrumba por abandono, vuelve a cavar...
        var sim = Diggers(1, """{ "digDays": 0.01, "abandonDays": 0.002 }""");
        var herb = sim.Creatures.Single();
        var (_, high) = Extremes(sim);
        (herb.X, herb.Y) = high;
        var cfg = sim.Config.Species[1].Burrows!;
        cfg.Collapse = new CollapseConfig { TilledSlots = 1, MaxTilledSlots = 3 };
        int max = sim.Config.World.MaxPlantsPerTile;
        var tile = sim.Map.TileOf(high.X, high.Y);

        StepAsleep(sim, (int)(0.3 * sim.Config.World.TicksPerDay));

        Assert.True(sim.Stats.BurrowsCollapsed[1] >= 4, $"solo {sim.Stats.BurrowsCollapsed[1]} derrumbes");
        Assert.Equal(3, sim.TilledSlots(tile.X, tile.Y));
        Assert.Equal(max + 3, sim.PlantCapacity(tile.X, tile.Y));
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
