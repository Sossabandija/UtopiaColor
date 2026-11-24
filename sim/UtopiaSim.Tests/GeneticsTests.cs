using System.Text.Json.Nodes;
using static UtopiaSim.Tests.SimulationTests;

namespace UtopiaSim.Tests;

/// <summary>Genética diploide: dos alelos por gen, dominancia y polen.</summary>
public class GeneticsTests
{
    static SimConfig OriginalWith(string patch) =>
        SimConfig.FromJson((JsonObject)JsonMerge.Apply(SimConfig.LoadJson(ConfigPath("original.json")),
            SimConfig.ParseObject(patch, "parche"))!, "parche");

    [Fact]
    public void Express_DominantAlleleWins_TieGivesTheMidpoint()
    {
        var line = new PolygonSpace(2, BoundaryRule.Clamp);
        var low = new Allele(new Vec2(2 * 0.2 - 1, 0), 0.9);
        var high = new Allele(new Vec2(2 * 0.8 - 1, 0), 0.3);

        Assert.Equal(low.Value, Allele.Express(line, low, high));
        Assert.Equal(low.Value, Allele.Express(line, high, low));
        Assert.Equal(1, Allele.Hidden(low, high));
        Assert.Equal(0, Allele.Hidden(high, low));

        var tie = high with { Dominance = 0.9 };
        Assert.Equal(0.5, line.LinePosition(Allele.Express(line, low, tie)), 6);
        Assert.Equal(-1, Allele.Hidden(low, tie));
    }

    [Fact]
    public void Diploid_StartsWithTwoAlleles_AndTheSamePositions()
    {
        var haploid = new Simulation(Original(), seed: 4);
        var diploid = new Simulation(OriginalWith("""{ "species": [ { "name": "herbivoro", "diploid": {} } ] }"""), seed: 4);

        Assert.Equal(haploid.Creatures.Select(c => (c.X, c.Y)), diploid.Creatures.Select(c => (c.X, c.Y)));
        var color = diploid.Genes[0].Space;
        foreach (var (h, d) in haploid.Creatures.Zip(diploid.Creatures))
        {
            if (d.Def.Name != "herbivoro")
            {
                Assert.Null(d.Alleles);
                Assert.Equal(h.Genes[0], d.Genes[0]);
                continue;
            }
            Assert.Equal(2, d.Alleles!.Length);
            Assert.Equal(h.Genes[0], d.Alleles[0].Value); // el primer alelo es el sorteo de siempre
            Assert.Equal(Allele.Express(color, d.Alleles[0], d.Alleles[1]), d.Genes[0]);
        }
    }

    [Fact]
    public void Diploid_Baby_GetsOneAlleleFromEachParent()
    {
        // Sin mutación ni cambio de dominancia, cada alelo de la cría es exactamente uno de los de su padre o madre.
        var sim = new Simulation(OriginalWith("""
            { "species": [ { "name": "herbivoro", "initialCount": 60, "diploid": { "dominanceStep": 0 }, "mutation": { "color": null } } ] }
            """), seed: 2);
        int babies = 0;
        sim.EventRaised += e =>
        {
            if (e.Type != SimEventType.Born || e.Subject.Def.Name != "herbivoro") return;
            var baby = e.Subject;
            var mother = sim.Creatures.Single(c => c.Id == baby.ParentA);
            var father = sim.Creatures.Single(c => c.Id == baby.ParentB);
            Assert.Contains(baby.Alleles![0], mother.Alleles!);
            Assert.Contains(baby.Alleles[1], father.Alleles!);
            Assert.Equal(Allele.Express(sim.Genes[0].Space, baby.Alleles[0], baby.Alleles[1]), baby.Genes[0]);
            babies++;
        };
        for (int i = 0; i < 2 * sim.Config.World.TicksPerDay && babies < 10; i++) sim.Step();
        Assert.True(babies >= 10, $"solo nacieron {babies} crías");
    }

    [Fact]
    public void DiploidPlants_TakePollenFromANeighbour()
    {
        var sim = new Simulation(OriginalWith("""
            { "species": [ { "name": "planta", "diploid": { "pollenTiles": 3 } },
                           { "name": "herbivoro", "initialCount": 0 }, { "name": "carnivoro", "initialCount": 0 } ] }
            """), seed: 1);
        int seeds = 0, outcrossed = 0;
        sim.EventRaised += e =>
        {
            if (e.Type != SimEventType.Born) return;
            seeds++;
            var seed = e.Subject;
            Assert.NotNull(seed.ParentB);
            if (seed.ParentB != seed.ParentA) outcrossed++;
            Assert.Equal(2, seed.Alleles!.Length);
        };
        for (int i = 0; i < sim.Config.World.TicksPerDay; i++) sim.Step();

        Assert.True(seeds > 50, $"solo {seeds} semillas");
        Assert.Equal(seeds - outcrossed, sim.Stats.Selfed[0]); // las que no tuvieron vecina se autopolinizaron
        Assert.True(outcrossed > seeds / 2, $"solo {outcrossed} de {seeds} semillas con polen de otra planta");
    }

    [Fact]
    public void DiploidPlants_AloneSelfOrMakeNoSeeds()
    {
        static Simulation Lonely(bool selfing) => new(OriginalWith($$"""
            { "species": [ { "name": "planta", "initialCount": 1, "diploid": { "selfing": {{(selfing ? "true" : "false")}} } },
                           { "name": "herbivoro", "initialCount": 0 }, { "name": "carnivoro", "initialCount": 0 } ] }
            """), seed: 3);

        var selfing = Lonely(true);
        var noSelfing = Lonely(false);
        Creature? first = null;
        selfing.EventRaised += e => { if (e.Type == SimEventType.Born) first ??= e.Subject; };
        for (int i = 0; i < selfing.Config.World.TicksPerDay; i++)
        {
            selfing.Step();
            noSelfing.Step();
        }
        // La primera semilla no tiene vecina: es hija de su madre dos veces. (Después sus hijas crecen y dan polen.)
        Assert.NotNull(first);
        Assert.Equal(first!.ParentA, first.ParentB);
        Assert.True(selfing.Stats.Selfed[0] > 0);
        Assert.Equal(0, noSelfing.Stats.Births[0]);
        Assert.True(noSelfing.Stats.Unpollinated[0] > 0);
    }
}
