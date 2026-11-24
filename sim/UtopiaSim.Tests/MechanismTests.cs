using System.Text.Json.Nodes;
using UtopiaSim.Headless;
using static UtopiaSim.Tests.SimulationTests;

namespace UtopiaSim.Tests;

/// <summary>Brotes frágiles, saciedad, pareja a distancia, "extends" y experimentos.</summary>
public class MechanismTests
{
    /// <summary>La configuración original con un parche JSON encima.</summary>
    static SimConfig OriginalWith(string patch) =>
        SimConfig.FromJson((JsonObject)JsonMerge.Apply(SimConfig.LoadJson(ConfigPath("original.json")),
            SimConfig.ParseObject(patch, "parche"))!, "parche");

    [Fact]
    public void JsonMerge_MergesObjectsAndNamedLists()
    {
        var target = SimConfig.ParseObject("""
            { "a": 1, "o": { "x": 1, "y": 2 }, "list": [1, 2],
              "species": [ { "name": "p", "v": 1, "w": 1 }, { "name": "q", "v": 2 } ] }
            """, "base");
        var patch = SimConfig.ParseObject("""
            { "a": null, "o": { "y": 3 }, "list": [9],
              "species": [ { "name": "q", "v": 5 }, { "name": "r", "v": 7 } ] }
            """, "parche");
        var r = (JsonObject)JsonMerge.Apply(target, patch)!;

        Assert.False(r.ContainsKey("a"));                       // null borra
        Assert.Equal(1, r["o"]!["x"]!.GetValue<int>());         // objetos: campo por campo
        Assert.Equal(3, r["o"]!["y"]!.GetValue<int>());
        Assert.Equal("[9]", r["list"]!.ToJsonString());         // listas de números: se reemplazan
        var species = r["species"]!.AsArray();
        Assert.Equal(new[] { "p", "q", "r" }, species.Select(s => s!["name"]!.GetValue<string>()));
        Assert.Equal(1, species[0]!["w"]!.GetValue<int>());     // "p" no se tocó
        Assert.Equal(5, species[1]!["v"]!.GetValue<int>());     // "q" se mezcló por nombre
        Assert.Equal(7, species[2]!["v"]!.GetValue<int>());     // "r" se agregó
        Assert.Equal(2, patch["species"]!.AsArray().Count);     // el parche no cambia
    }

    [Fact]
    public void Extends_ReadsBaseAndAppliesChanges()
    {
        string dir = Path.Combine(Path.GetTempPath(), "utopia-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            File.Copy(ConfigPath("original.json"), Path.Combine(dir, "original.json"));
            File.WriteAllText(Path.Combine(dir, "hijo.json"), """
                // comentarios permitidos
                { "extends": "original.json", "species": [ { "name": "carnivoro", "digestDays": 0.5 } ] }
                """);
            File.WriteAllText(Path.Combine(dir, "nieto.json"), """{ "extends": "hijo.json", "world": { "size": 20 } }""");

            var cfg = SimConfig.Load(Path.Combine(dir, "nieto.json"));
            Assert.Equal(20, cfg.World.Size);
            Assert.Equal(0.5, cfg.Species.Single(s => s.Name == "carnivoro").DigestDays);
            Assert.Equal(0, cfg.Species.Single(s => s.Name == "herbivoro").DigestDays);
            Assert.Equal(77, cfg.Species.Single(s => s.Name == "planta").InitialCount);

            File.WriteAllText(Path.Combine(dir, "ciclo.json"), """{ "extends": "ciclo.json" }""");
            Assert.Throws<ConfigException>(() => SimConfig.Load(Path.Combine(dir, "ciclo.json")));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Seedlings_DieOnlyWhileSmall()
    {
        var cfg = OriginalWith("""
            { "world": { "maxPlantsPerTile": 4 },
              "species": [
                { "name": "planta", "plant": { "seedling": { "untilSize": 0.3, "riskPerDay": 5, "shadeRiskPerDay": 25, "terrainRiskPerDay": 3 } } },
                { "name": "herbivoro", "initialCount": 0 }, { "name": "carnivoro", "initialCount": 0 } ] }
            """);
        var sim = new Simulation(cfg, seed: 2);
        int seedlingDeaths = 0;
        sim.EventRaised += e =>
        {
            if (e is not { Type: SimEventType.Died, Cause: DeathCause.Seedling }) return;
            seedlingDeaths++;
            Assert.True(e.Subject.Landed);
            Assert.True(e.Subject.PlantSize < 0.3, $"murió como brote con tamaño {e.Subject.PlantSize}");
        };
        for (int i = 0; i < 3000; i++) sim.Step();

        Assert.True(seedlingDeaths > 50, $"solo {seedlingDeaths} brotes murieron");
        Assert.Equal(seedlingDeaths, sim.Stats.Deaths[0, (int)DeathCause.Seedling]);
        Assert.All(sim.Creatures.Where(c => c.IsPlant && c.Landed && c.PlantSize >= 0.3),
            c => Assert.Equal(0, sim.SeedlingRiskPerDay(c)));
    }

    [Fact]
    public void Original_HasNoSeedlingDeaths()
    {
        var sim = new Simulation(Original(), seed: 2);
        for (int i = 0; i < 3000; i++) sim.Step();
        Assert.Equal(0, sim.Stats.Deaths[0, (int)DeathCause.Seedling]);
        Assert.True(sim.Stats.Deaths[0, (int)DeathCause.Crowded] > 0);
    }

    [Fact]
    public void Digesting_PredatorWaitsBeforeEatingAgain()
    {
        var sim = new Simulation(OriginalWith("""{ "species": [ { "name": "carnivoro", "digestDays": 0.5 } ] }"""), seed: 1);
        var digestUntil = new Dictionary<int, long>();
        int meals = 0;
        sim.EventRaised += e =>
        {
            if (e.Type != SimEventType.Ate || e.Subject.Def.Name != "carnivoro") return;
            meals++;
            if (digestUntil.TryGetValue(e.Subject.Id, out long until))
                Assert.True(sim.Tick >= until, $"#{e.Subject.Id} comió en el tick {sim.Tick}, digería hasta {until}");
            Assert.True(e.Subject.DigestUntil > sim.Tick);
            digestUntil[e.Subject.Id] = e.Subject.DigestUntil;
        };
        for (int i = 0; i < sim.Config.World.TicksPerDay; i++) sim.Step();
        Assert.True(meals > 5, $"los carnívoros comieron solo {meals} veces");
    }

    [Theory]
    [InlineData(null, false)] // visión normal: 8.6 casillas
    [InlineData(18.0, true)]
    public void MateRange_FindsDistantPartner(double? range, bool expectFound)
    {
        string mate = range is double r ? $", \"mateRangeTiles\": {r.ToString(System.Globalization.CultureInfo.InvariantCulture)}" : "";
        var sim = new Simulation(OriginalWith($$"""
            { "species": [ { "name": "planta", "initialCount": 0 }, { "name": "herbivoro", "initialCount": 0 },
                           { "name": "carnivoro", "initialCount": 2 {{mate}} } ] }
            """), seed: 1);
        var a = sim.Creatures[0];
        var b = sim.Creatures[1];
        double tile = sim.Map.TileSize;
        a.X = 5 * tile;
        a.Y = 5 * tile;
        b.X = 17 * tile; // a 12 casillas
        b.Y = 5 * tile;
        foreach (var c in new[] { a, b })
        {
            Array.Fill(c.Needs, 1.0);
            c.Needs[Need.Mate] = 0.6; // lo más urgente: buscar pareja
        }

        sim.Step();
        Assert.Equal(Need.Mate, a.TopNeed);
        Assert.Equal(expectFound ? b : null, a.TargetCreature);
    }

    [Theory]
    [InlineData("coexistencia.json", "original.json")]
    [InlineData("refugio_adultas.json", "original.json")]
    [InlineData("refugio_etapa.json", "etapa.json")]
    [InlineData("dispersion.json", "etapa_refugio.json")]
    [InlineData("depredadores.json", "dispersion.json")]
    [InlineData("madrigueras.json", "dispersion.json")]
    [InlineData("cobertura.json", "dispersion.json")]
    [InlineData("diploide.json", "agua_madrigueras.json")]
    [InlineData("cavar.json", "agua_madrigueras.json")]
    [InlineData("arranque.json", "madrigueras_cavadas.json")]
    [InlineData("cavar_huyendo.json", "madrigueras_cavadas.json")]
    public void ShippedExperiments_BuildAllCombinations(string file, string baseFile)
    {
        var exp = Experiment.Load(ConfigPath(Path.Combine("experimentos", file)));
        var variants = exp.Variants();
        int n = exp.Mechanisms.Count;

        Assert.Equal(1 << n, variants.Count);
        Assert.Equal("base", variants[0].Name);
        Assert.Equal(string.Join('+', exp.Mechanisms.Select(m => m.Name)), variants[^1].Name);
        Assert.Equal(variants.Count, variants.Select(v => v.Name).Distinct().Count());
        // La variante base es exactamente la configuración de la que parte.
        Assert.Equal(SimConfig.Load(ConfigPath(baseFile)).ToJson(), variants[0].Config.ToJson());
        // Cada mecanismo cambia algo.
        Assert.All(variants.Skip(1), v => Assert.NotEqual(variants[0].Config.ToJson(), v.Config.ToJson()));
    }
}
