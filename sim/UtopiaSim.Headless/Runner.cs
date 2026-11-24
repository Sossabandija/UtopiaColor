using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace UtopiaSim.Headless;

/// <summary>Resultado de una corrida, una fila de summary.csv.</summary>
sealed record RunResult(
    ulong Seed,
    double Days,
    bool StoppedEarly,
    string[] SpeciesNames,
    int[] Final,
    int[] Min,
    int[] Max,
    double?[] ExtinctDay,
    long[] Births,
    long[,] Deaths,
    double TicksPerSecond)
{
    /// <summary>Días hasta la primera extinción (o todos los días simulados si nadie se extinguió).</summary>
    public double CoexistenceDays => ExtinctDay.Where(d => d.HasValue).Select(d => d!.Value).DefaultIfEmpty(Days).Min();

    public bool AllSurvived => ExtinctDay.All(d => d == null);

    public string OneLine()
    {
        var sb = new StringBuilder($"semilla {Seed,4}: {Days,6:0.0} días | ");
        for (int s = 0; s < SpeciesNames.Length; s++)
        {
            sb.Append($"{SpeciesNames[s]} {Final[s],5}");
            if (ExtinctDay[s] is double d) sb.Append($" (extinta día {d:0.0})");
            sb.Append(s < SpeciesNames.Length - 1 ? ", " : "");
        }
        sb.Append($" | {TicksPerSecond / 1000:0.0}k ticks/s");
        return sb.ToString();
    }
}

static class Runner
{
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static RunResult Run(SimConfig config, ulong seed, string dir, CliOptions o, Action<string>? progress)
    {
        Directory.CreateDirectory(dir);
        var sim = new Simulation(config, seed);
        int speciesCount = sim.Species.Count;
        int ticksPerDay = config.World.TicksPerDay;
        long totalTicks = Math.Max(1, (long)Math.Round(o.Days * ticksPerDay));
        string[] names = sim.Species.Select(s => s.Name).ToArray();

        var min = new int[speciesCount];
        var max = new int[speciesCount];
        var extinct = new double?[speciesCount];
        for (int s = 0; s < speciesCount; s++)
        {
            min[s] = max[s] = sim.Population(s);
            if (sim.Population(s) == 0) extinct[s] = 0;
        }
        bool hasAnimals = sim.Species.Any(s => !s.IsPlant);

        progress?.Invoke($"Terreno: {100.0 * sim.Map.DrinkableTileCount / (sim.Map.Size * sim.Map.Size):0}% de las casillas tiene agua para beber.");

        using var pop = new StreamWriter(Path.Combine(dir, "populations.csv"));
        pop.WriteLine("tick,day,daylight," + string.Join(',', names));
        WritePopulations(pop, sim);

        using var genes = o.WriteGenes ? new StreamWriter(Path.Combine(dir, "genes.csv")) : null;
        if (genes != null)
        {
            genes.WriteLine("day,species,id,generation,gene,x,y");
            WriteGenes(genes, sim);
        }

        var sw = Stopwatch.StartNew();
        bool stoppedEarly = false;
        long progressEvery = ticksPerDay * 5L;
        while (sim.Tick < totalTicks)
        {
            sim.Step();

            for (int s = 0; s < speciesCount; s++)
            {
                int p = sim.Population(s);
                if (p < min[s]) min[s] = p;
                if (p > max[s]) max[s] = p;
                if (p == 0 && extinct[s] == null) extinct[s] = sim.Day;
            }

            if (sim.Tick % o.SampleEvery == 0) WritePopulations(pop, sim);
            if (genes != null && sim.Tick % ticksPerDay == 0) WriteGenes(genes, sim);
            if (progress != null && sim.Tick % progressEvery == 0)
            {
                string pops = string.Join(" | ", names.Select((n, s) => $"{n} {sim.Population(s)}"));
                progress($"día {sim.Day,6:0.0} | {pops} | {sim.Tick / sw.Elapsed.TotalSeconds / 1000:0.0}k ticks/s");
            }

            if (o.StopWhenExtinct && (AllExtinct(sim, animalsOnly: true) && hasAnimals || AllExtinct(sim, animalsOnly: false)))
            {
                stoppedEarly = true;
                break;
            }
        }
        if (sim.Tick % o.SampleEvery != 0) WritePopulations(pop, sim);

        var final = Enumerable.Range(0, speciesCount).Select(sim.Population).ToArray();
        return new RunResult(seed, sim.Day, stoppedEarly, names, final, min, max, extinct,
            (long[])sim.Stats.Births.Clone(), (long[,])sim.Stats.Deaths.Clone(),
            sim.Tick / Math.Max(1e-9, sw.Elapsed.TotalSeconds));
    }

    static bool AllExtinct(Simulation sim, bool animalsOnly)
    {
        for (int s = 0; s < sim.Species.Count; s++)
        {
            if (animalsOnly && sim.Species[s].IsPlant) continue;
            if (sim.Population(s) > 0) return false;
        }
        return true;
    }

    static void WritePopulations(StreamWriter w, Simulation sim)
    {
        w.Write(sim.Tick.ToString(Inv));
        w.Write(',');
        w.Write(sim.Day.ToString("0.####", Inv));
        w.Write(',');
        w.Write(sim.Daylight.ToString("0.###", Inv));
        for (int s = 0; s < sim.Species.Count; s++)
        {
            w.Write(',');
            w.Write(sim.Population(s).ToString(Inv));
        }
        w.WriteLine();
    }

    static void WriteGenes(StreamWriter w, Simulation sim)
    {
        string day = sim.Day.ToString("0.##", Inv);
        foreach (var c in sim.Creatures)
        {
            foreach (var g in sim.Genes)
            {
                var v = c.Genes[g.Index];
                w.Write(day);
                w.Write(',');
                w.Write(c.Def.Name);
                w.Write(',');
                w.Write(c.Id.ToString(Inv));
                w.Write(',');
                w.Write(c.Generation.ToString(Inv));
                w.Write(',');
                w.Write(g.Name);
                w.Write(',');
                w.Write(v.X.ToString("0.#####", Inv));
                w.Write(',');
                w.Write(v.Y.ToString("0.#####", Inv));
                w.WriteLine();
            }
        }
    }

    public static void WriteSummary(string path, IReadOnlyList<RunResult> results)
    {
        if (results.Count == 0) return;
        var names = results[0].SpeciesNames;
        var causes = Enum.GetValues<DeathCause>();
        using var w = new StreamWriter(path);

        var header = new List<string> { "seed", "days", "stopped_early", "coexistence_days" };
        foreach (var n in names)
        {
            header.AddRange([$"{n}_final", $"{n}_min", $"{n}_max", $"{n}_extinct_day", $"{n}_births"]);
            header.AddRange(causes.Select(c => $"{n}_deaths_{c.ToString().ToLowerInvariant()}"));
        }
        header.Add("ticks_per_sec");
        w.WriteLine(string.Join(',', header));

        foreach (var r in results)
        {
            var row = new List<string>
            {
                r.Seed.ToString(Inv),
                r.Days.ToString("0.###", Inv),
                r.StoppedEarly ? "1" : "0",
                r.CoexistenceDays.ToString("0.###", Inv),
            };
            for (int s = 0; s < names.Length; s++)
            {
                row.Add(r.Final[s].ToString(Inv));
                row.Add(r.Min[s].ToString(Inv));
                row.Add(r.Max[s].ToString(Inv));
                row.Add(r.ExtinctDay[s]?.ToString("0.###", Inv) ?? "");
                row.Add(r.Births[s].ToString(Inv));
                foreach (var c in causes) row.Add(r.Deaths[s, (int)c].ToString(Inv));
            }
            row.Add(r.TicksPerSecond.ToString("0", Inv));
            w.WriteLine(string.Join(',', row));
        }
    }
}
