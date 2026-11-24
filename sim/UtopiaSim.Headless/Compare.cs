using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace UtopiaSim.Headless;

/// <summary>
/// <c>--compare experimento.json</c>: corre cada combinación de mecanismos con las mismas semillas
/// y muestra en cuántas corridas coexisten todas las especies hasta el final.
/// Cada corrida se detiene en su primera extinción: para esta pregunta no hace falta seguir.
/// </summary>
static class Compare
{
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static int Run(CliOptions opts)
    {
        Experiment exp;
        List<Variant> variants;
        try
        {
            exp = Experiment.Load(opts.ComparePath!);
            variants = exp.Variants();
        }
        catch (ConfigException e)
        {
            Console.Error.WriteLine(e.Message);
            return 1;
        }

        double days = opts.Days ?? exp.Days ?? 100;
        int runs = opts.Runs ?? exp.Runs ?? 16;
        ulong firstSeed = opts.Seed ?? exp.FirstSeed ?? 1;
        var settings = opts.Settings(days, StopRule.FirstExtinction) with { FunctionalExtinction = true };
        string outDir = opts.OutDir ?? Path.Combine("runs", $"comparar-{exp.Name}-{DateTime.Now:yyyyMMdd-HHmmss}");
        Directory.CreateDirectory(outDir);
        foreach (var v in variants)
        {
            Directory.CreateDirectory(Path.Combine(outDir, v.Name));
            File.WriteAllText(Path.Combine(outDir, v.Name, "config.json"), Experiment.Pretty(v.Json));
        }

        Console.WriteLine($"Experimento {opts.ComparePath}: {variants.Count} variantes × {runs} semillas " +
                          $"({firstSeed}..{firstSeed + (ulong)runs - 1}) × {days} días");
        Console.WriteLine("Cada corrida se detiene en su primera extinción (un animal con menos de 2 individuos cuenta como extinto).");
        Console.WriteLine($"Salida: {Path.GetFullPath(outDir)}");
        Console.WriteLine();

        var names = variants[0].Config.Species.Select(s => s.Name).ToArray();
        var table = new Table(names, variants.Max(v => v.Name.Length), days);
        Console.WriteLine(table.Header());

        var results = new RunResult[variants.Count][];
        for (int v = 0; v < variants.Count; v++) results[v] = new RunResult[runs];
        var remaining = Enumerable.Repeat(runs, variants.Count).ToArray();
        var consoleLock = new object();
        var total = Stopwatch.StartNew();

        Parallel.For(0, variants.Count * runs, new ParallelOptions { MaxDegreeOfParallelism = opts.Parallel }, job =>
        {
            int v = job / runs, i = job % runs;
            ulong seed = firstSeed + (ulong)i;
            var variant = variants[v];
            results[v][i] = Runner.Run(variant.Config, seed, Path.Combine(outDir, variant.Name, $"seed-{seed}"), settings, null);
            if (Interlocked.Decrement(ref remaining[v]) == 0)
            {
                Runner.WriteSummary(Path.Combine(outDir, variant.Name, "summary.csv"), results[v]);
                lock (consoleLock) Console.WriteLine(table.Row(variant.Name, results[v]));
            }
        });

        // Las filas salen a medida que terminan; al final, la tabla completa en orden.
        Console.WriteLine();
        Console.WriteLine($"Listo en {total.Elapsed.TotalMinutes:0.0} min. En orden:");
        Console.WriteLine();
        Console.WriteLine(table.Header());
        for (int v = 0; v < variants.Count; v++) Console.WriteLine(table.Row(variants[v].Name, results[v]));

        if (exp.Mechanisms.Count > 1)
        {
            Console.WriteLine();
            Console.WriteLine("Efecto de cada mecanismo (variantes con él contra variantes sin él):");
            int width = exp.Mechanisms.Max(m => m.Name.Length);
            foreach (var (mech, _) in exp.Mechanisms)
            {
                var with = results.Where((_, i) => variants[i].Mechanisms.Contains(mech)).SelectMany(r => r).ToArray();
                var without = results.Where((_, i) => !variants[i].Mechanisms.Contains(mech)).SelectMany(r => r).ToArray();
                double pctWith = Table.CoexistPct(with), pctWithout = Table.CoexistPct(without);
                double daysWith = with.Average(r => r.CoexistenceDays), daysWithout = without.Average(r => r.CoexistenceDays);
                Console.WriteLine($"  {mech.PadRight(width)}  coexisten {pctWith,3:0}% contra {pctWithout,3:0}%" +
                                  $"   ·   días hasta la 1ª extinción (promedio) {daysWith,5:0.0} contra {daysWithout,5:0.0}");
            }
        }

        WriteCsv(Path.Combine(outDir, "comparison.csv"), variants, results, names, days);
        Console.WriteLine();
        Console.WriteLine($"Tabla en {Path.Combine(outDir, "comparison.csv")}. Cada variante tiene su config.json y una carpeta por semilla.");
        return 0;
    }

    internal static double Median(IEnumerable<double> xs)
    {
        var a = xs.Order().ToArray();
        if (a.Length == 0) return double.NaN;
        return a.Length % 2 == 1 ? a[a.Length / 2] : (a[a.Length / 2 - 1] + a[a.Length / 2]) / 2;
    }

    /// <summary>Cuántas veces cada especie fue la primera en extinguirse (en un empate cuentan todas).</summary>
    internal static int[] FirstExtinct(RunResult[] rs, int speciesCount)
    {
        var counts = new int[speciesCount];
        foreach (var r in rs)
        {
            if (r.AllSurvived) continue;
            double first = r.ExtinctDay.Where(d => d != null).Min(d => d!.Value);
            for (int s = 0; s < speciesCount; s++)
                if (r.ExtinctDay[s] == first) counts[s]++;
        }
        return counts;
    }

    static void WriteCsv(string path, List<Variant> variants, RunResult[][] results, string[] names, double days)
    {
        using var w = new StreamWriter(path);
        var header = new List<string> { "variant", "mechanisms", "runs", "days", "coexist_runs", "coexist_pct", "median_coexist_days", "mean_coexist_days" };
        header.AddRange(names.Select(n => $"{n}_first_extinct"));
        header.AddRange(names.Select(n => $"{n}_median_max"));
        w.WriteLine(string.Join(',', header));
        for (int v = 0; v < variants.Count; v++)
        {
            var rs = results[v];
            var row = new List<string>
            {
                variants[v].Name,
                variants[v].Mechanisms.Length.ToString(Inv),
                rs.Length.ToString(Inv),
                days.ToString(Inv),
                rs.Count(r => r.AllSurvived).ToString(Inv),
                Table.CoexistPct(rs).ToString("0.#", Inv),
                Median(rs.Select(r => r.CoexistenceDays)).ToString("0.##", Inv),
                rs.Average(r => r.CoexistenceDays).ToString("0.##", Inv),
            };
            row.AddRange(FirstExtinct(rs, names.Length).Select(c => c.ToString(Inv)));
            row.AddRange(names.Select((_, s) => Median(rs.Select(r => (double)r.Max[s])).ToString("0", Inv)));
            w.WriteLine(string.Join(',', row));
        }
    }

    /// <summary>Formato de la tabla de consola.</summary>
    sealed class Table(string[] species, int nameWidth, double days)
    {
        const int CoexistWidth = 11, DaysWidth = 10;
        readonly int nameWidth = Math.Max(nameWidth, 8);
        int GroupWidth => species.Sum(s => Col(s) + 1);
        static int Col(string name) => Math.Max(name.Length, 6);

        public static double CoexistPct(RunResult[] rs) => 100.0 * rs.Count(r => r.AllSurvived) / rs.Length;

        public string Header()
        {
            string top = new string(' ', nameWidth) + "  " + "coexisten".PadRight(CoexistWidth) + "  " +
                         "días hasta".PadLeft(DaysWidth) + "  " + "1ª en extinguirse".PadRight(GroupWidth) + "  " +
                         "población máx. (mediana)";
            var bottom = new StringBuilder("variante".PadRight(nameWidth));
            bottom.Append("  ").Append(("al día " + days.ToString("0", Inv)).PadRight(CoexistWidth));
            bottom.Append("  ").Append("1ª extinc.".PadLeft(DaysWidth)).Append("  ");
            foreach (var s in species) bottom.Append(s.PadLeft(Col(s))).Append(' ');
            bottom.Append("  ");
            foreach (var s in species) bottom.Append(s.PadLeft(Col(s))).Append(' ');
            return top.TrimEnd() + Environment.NewLine + bottom.ToString().TrimEnd();
        }

        public string Row(string name, RunResult[] rs)
        {
            var sb = new StringBuilder(name.PadRight(nameWidth));
            string coexist = $"{rs.Count(r => r.AllSurvived)}/{rs.Length} {CoexistPct(rs),3:0}%";
            sb.Append("  ").Append(coexist.PadRight(CoexistWidth));
            sb.Append("  ").Append(Median(rs.Select(r => r.CoexistenceDays)).ToString("0.0", Inv).PadLeft(DaysWidth)).Append("  ");
            var first = FirstExtinct(rs, species.Length);
            for (int s = 0; s < species.Length; s++) sb.Append(first[s].ToString(Inv).PadLeft(Col(species[s]))).Append(' ');
            sb.Append("  ");
            for (int s = 0; s < species.Length; s++)
                sb.Append(Median(rs.Select(r => (double)r.Max[s])).ToString("0", Inv).PadLeft(Col(species[s]))).Append(' ');
            return sb.ToString().TrimEnd();
        }
    }
}
