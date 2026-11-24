using System.Globalization;
using System.Text.Json.Nodes;
using static UtopiaSim.Tests.SimulationTests;

namespace UtopiaSim.Tests;

/// <summary>Gen triangular de dispersión: viento, fruto (con gen de dieta) y abrojo.</summary>
public class DispersalTests
{
    // Esquinas del triángulo: 0 fruto (arriba), 1 viento (abajo a la izquierda), 2 abrojo (abajo a la derecha).
    const string Fruit = "[0, 1]";
    const string Wind = "[-0.8660254, -0.5]";
    const string Burr = "[0.8660254, -0.5]";
    const string Center = "[0, 0]";

    static readonly string DispersalGene =
        """{ "name": "dispersion", "shape": "polygon", "sides": 3, "boundary": "clamp", "vertexNames": ["fruto", "viento", "abrojo"] }""";

    /// <summary>Original + gen de dispersión en las plantas (sin mutar), con los modos dados.</summary>
    static SimConfig WithDispersal(string start, string modes, int herbivores = 0, int carnivores = 0, string extra = "")
    {
        string json = $$"""
            { "genes": [ {{DispersalGene}} ],
              "species": [
                { "name": "planta", "initialGenes": { "dispersion": {{start}} },
                  "plant": { "dispersal": { "gene": "dispersion", {{modes}} } } },
                { "name": "herbivoro", "initialCount": {{herbivores}} },
                { "name": "carnivoro", "initialCount": {{carnivores}} } ]
              {{extra}} }
            """;
        var patched = JsonMerge.Apply(SimConfig.LoadJson(ConfigPath("original.json")), SimConfig.ParseObject(json, "parche"));
        return SimConfig.FromJson((JsonObject)patched!, "parche");
    }

    /// <summary>Distancia promedio entre cada planta (ya plantada) y su madre, por cómo llegó.</summary>
    static Dictionary<SeedOrigin, double> MeanDistanceFromMother(Simulation sim, int ticks)
    {
        var mother = new Dictionary<int, (double X, double Y)>();
        sim.EventRaised += e =>
        {
            if (e.Type == SimEventType.Born && e.Subject.IsPlant) mother[e.Subject.Id] = (e.Other!.X, e.Other.Y);
        };
        for (int i = 0; i < ticks; i++) sim.Step();
        return sim.Creatures
            .Where(c => c.IsPlant && c.Landed && mother.ContainsKey(c.Id))
            .GroupBy(c => c.Origin)
            .ToDictionary(g => g.Key, g => g.Average(c => sim.Map.LoopDistance(c.X, c.Y, mother[c.Id].X, mother[c.Id].Y)));
    }

    [Fact]
    public void Wind_CarriesSeedsFarther()
    {
        var wind = new Simulation(WithDispersal(Wind, """ "wind": { "maxRangeFactor": 4 } """), seed: 3);
        // Sin bloque "wind": todas las semillas son normales.
        var normal = new Simulation(WithDispersal(Wind, """ "exponent": 2 """), seed: 3);

        var dw = MeanDistanceFromMother(wind, 1500);
        var dn = MeanDistanceFromMother(normal, 1500);

        Assert.True(dw.ContainsKey(SeedOrigin.Wind));
        Assert.False(dn.ContainsKey(SeedOrigin.Wind));
        double ratio = dw[SeedOrigin.Wind] / dn[SeedOrigin.Parent];
        Assert.InRange(ratio, 2.5, 5); // ~4 veces más lejos (un poco menos: el viento a veces da la vuelta al toro)
    }

    [Fact]
    public void Weights_DecideHowOftenEachModeIsUsed()
    {
        var sim = new Simulation(WithDispersal(Center, """ "wind": { "maxRangeFactor": 2 } """), seed: 5);
        for (int i = 0; i < 1500; i++) sim.Step();
        long wind = sim.Stats.Dispersal[0, (int)DispersalCounter.SeedsWind];
        long normal = sim.Stats.Dispersal[0, (int)DispersalCounter.SeedsParent];
        // En el centro cada esquina pesa 1/3: un tercio de las reproducciones es por viento.
        Assert.InRange((double)wind / (wind + normal), 0.28, 0.39);
        Assert.Equal(sim.Stats.Births[0], wind + normal);
    }

    /// <summary>Plantas en la esquina del fruto y 26 herbívoros con dieta fija (0 folívoro, 1 frugívoro), sin carnívoros.</summary>
    static SimConfig WithFruit(double diet, double fruitCalories = 0.5)
    {
        string x = (2 * diet - 1).ToString(CultureInfo.InvariantCulture);
        string cal = fruitCalories.ToString(CultureInfo.InvariantCulture);
        string json = $$"""
            { "genes": [ {{DispersalGene}}, { "name": "dieta", "shape": "polygon", "sides": 2, "boundary": "clamp" } ],
              "species": [
                { "name": "planta", "initialGenes": { "dispersion": {{Fruit}} },
                  "plant": { "dispersal": { "gene": "dispersion",
                    "fruit": { "maxSeeds": 4, "calories": {{cal}}, "maxColorDistance": 2, "transitDays": 0.05, "maxOnPlant": 3 } } } },
                { "name": "herbivoro", "dietGene": "dieta", "initialGenes": { "dieta": [{{x}}] }, "initialJitter": { "maxStep": 0 } },
                { "name": "carnivoro", "initialCount": 0 } ] }
            """;
        var patched = JsonMerge.Apply(SimConfig.LoadJson(ConfigPath("original.json")), SimConfig.ParseObject(json, "parche"));
        return SimConfig.FromJson((JsonObject)patched!, "parche");
    }

    static long Count(Simulation sim, DispersalCounter counter) => sim.Stats.Dispersal[0, (int)counter];

    [Fact]
    public void Frugivore_EatsFruitWithoutKilling_AndCarriesTheSeeds()
    {
        var sim = new Simulation(WithFruit(diet: 1), seed: 2);
        int fruitMeals = 0;
        sim.EventRaised += e =>
        {
            if (e.Type != SimEventType.AteFruit) return;
            fruitMeals++;
            Assert.False(e.Other!.Dead); // la planta sigue viva
        };
        for (int i = 0; i < sim.Config.World.TicksPerDay; i++) sim.Step();

        Assert.True(fruitMeals > 20, $"solo {fruitMeals} frutos comidos");
        Assert.Equal(fruitMeals, Count(sim, DispersalCounter.FruitsEaten));
        Assert.Equal(0, sim.Stats.Deaths[0, (int)DeathCause.Eaten]); // un frugívoro puro no come plantas enteras
        Assert.Equal(0, Count(sim, DispersalCounter.SeedsDigested));  // y no destruye semillas
        Assert.True(Count(sim, DispersalCounter.SeedsGut) > 0);
        Assert.Contains(sim.Creatures, c => c.IsPlant && c.Origin == SeedOrigin.Gut);
    }

    [Fact]
    public void Folivore_NeverEatsFruit()
    {
        var sim = new Simulation(WithFruit(diet: 0), seed: 2);
        for (int i = 0; i < sim.Config.World.TicksPerDay / 2; i++) sim.Step();
        Assert.Equal(0, Count(sim, DispersalCounter.FruitsEaten));
        Assert.True(sim.Stats.Deaths[0, (int)DeathCause.Eaten] > 0);
    }

    [Fact]
    public void HalfDiet_DigestsAboutHalfTheSeeds()
    {
        // Fruto bien calórico, para que a una dieta 0.5 le convenga más que la planta entera.
        var sim = new Simulation(WithFruit(diet: 0.5, fruitCalories: 1.5), seed: 2);
        for (int i = 0; i < 2 * sim.Config.World.TicksPerDay; i++) sim.Step();
        long digested = Count(sim, DispersalCounter.SeedsDigested);
        long passed = sim.Creatures.Where(c => !c.IsPlant).Sum(c => (long)c.SeedsInGut) + Count(sim, DispersalCounter.SeedsGut);
        Assert.True(digested + passed > 100, $"muy pocas semillas comidas: {digested + passed}");
        Assert.InRange((double)digested / (digested + passed), 0.4, 0.6);
    }

    [Fact]
    public void UneatenFruit_FallsUnderTheMother()
    {
        var sim = new Simulation(WithDispersal(Fruit, """ "fruit": { "maxOnPlant": 2 } """), seed: 4);
        for (int i = 0; i < sim.Config.World.TicksPerDay; i++) sim.Step(); // el tick 0 es medianoche: de noche casi no crecen
        Assert.True(Count(sim, DispersalCounter.FruitsFallen) > 0);
        Assert.Equal(sim.Stats.Births[0], Count(sim, DispersalCounter.SeedsParent));
        Assert.All(sim.Creatures.Where(c => c.IsPlant), c => Assert.InRange(c.Fruits, 0, 2));
    }

    const string BurrMode = """ "burr": { "maxSeeds": 4, "dropEveryDays": 0.03, "maxOnPlant": 2 } """;

    [Fact]
    public void Burrs_TravelWithAnimals_AndSowAwayFromTheMother()
    {
        var sim = new Simulation(WithDispersal(Burr, BurrMode, herbivores: 26), seed: 6);
        int burrSeeds = 0;
        sim.EventRaised += e =>
        {
            if (e.Type != SimEventType.Born || e.Subject.Origin != SeedOrigin.Burr) return;
            burrSeeds++;
            // La semilla nace justo donde está el animal que lleva el abrojo.
            Assert.Contains(sim.Creatures, a => !a.IsPlant && a.X == e.Subject.X && a.Y == e.Subject.Y && a.BurrsCarried > 0);
        };
        var d = MeanDistanceFromMother(sim, sim.Config.World.TicksPerDay);

        Assert.True(Count(sim, DispersalCounter.BurrsAttached) > 10, $"solo {Count(sim, DispersalCounter.BurrsAttached)} abrojos pegados");
        Assert.Equal(burrSeeds, Count(sim, DispersalCounter.SeedsBurr));
        Assert.True(burrSeeds > 10);
        Assert.True(d[SeedOrigin.Burr] > 1.2 * d[SeedOrigin.Parent],
            $"abrojo {d[SeedOrigin.Burr]:0} px contra semilla normal {d[SeedOrigin.Parent]:0} px");
        Assert.All(sim.Creatures.Where(c => c.IsPlant), c => Assert.InRange(c.Burrs, 0, 2));
    }

    [Fact]
    public void Burrs_StickToCarnivoresToo()
    {
        var sim = new Simulation(WithDispersal(Burr, BurrMode, herbivores: 0, carnivores: 20), seed: 6);
        for (int i = 0; i < sim.Config.World.TicksPerDay; i++) sim.Step();
        Assert.True(Count(sim, DispersalCounter.BurrsAttached) > 0);
    }

    [Fact]
    public void Burrs_WithoutAnimals_Fall()
    {
        var sim = new Simulation(WithDispersal(Burr, BurrMode), seed: 6);
        for (int i = 0; i < sim.Config.World.TicksPerDay; i++) sim.Step();
        Assert.Equal(0, Count(sim, DispersalCounter.BurrsAttached));
        Assert.True(Count(sim, DispersalCounter.BurrsFallen) > 0);
    }

    [Fact]
    public void Dispersal_GeneNeedsThreeSides()
    {
        string json = """
            { "genes": [ { "name": "color", "shape": "disk" }, { "name": "linea", "shape": "polygon", "sides": 2 } ],
              "species": [ { "name": "p", "kind": "plant", "plant": { "dispersal": { "gene": "linea" } } } ] }
            """;
        var e = Assert.Throws<ConfigException>(() => SimConfig.Parse(json));
        Assert.Contains("3 lados", e.Message);
    }
}
