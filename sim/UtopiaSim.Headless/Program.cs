using System.Diagnostics;
using System.Globalization;
using UtopiaSim;
using UtopiaSim.Headless;

CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

CliOptions opts;
try
{
    opts = CliOptions.Parse(args);
}
catch (ArgumentException e)
{
    Console.Error.WriteLine($"Error: {e.Message}\n");
    CliOptions.PrintHelp();
    return 2;
}
if (opts.ShowHelp)
{
    CliOptions.PrintHelp();
    return 0;
}

SimConfig config;
try
{
    config = SimConfig.Load(opts.ConfigPath);
}
catch (ConfigException e)
{
    Console.Error.WriteLine(e.Message);
    return 1;
}

ulong firstSeed = opts.Seed ?? config.Seed;
string outDir = opts.OutDir ?? Path.Combine("runs",
    $"{Path.GetFileNameWithoutExtension(opts.ConfigPath)}-{DateTime.Now:yyyyMMdd-HHmmss}");
Directory.CreateDirectory(outDir);
File.Copy(opts.ConfigPath, Path.Combine(outDir, "config.json"), overwrite: true);

Console.WriteLine($"Config {opts.ConfigPath} | {opts.Days} días | {opts.Runs} corrida(s) desde la semilla {firstSeed}");
Console.WriteLine($"Salida: {Path.GetFullPath(outDir)}");

var results = new RunResult[opts.Runs];
var consoleLock = new object();
var total = Stopwatch.StartNew();
bool single = opts.Runs == 1;

Parallel.For(0, opts.Runs, new ParallelOptions { MaxDegreeOfParallelism = opts.Parallel }, i =>
{
    ulong seed = firstSeed + (ulong)i;
    string dir = single ? outDir : Path.Combine(outDir, $"seed-{seed}");
    Action<string>? progress = single ? msg => Console.WriteLine("  " + msg) : null;
    results[i] = Runner.Run(config, seed, dir, opts, progress);
    lock (consoleLock) Console.WriteLine(results[i].OneLine());
});

Runner.WriteSummary(Path.Combine(outDir, "summary.csv"), results);

Console.WriteLine();
Console.WriteLine($"Listo en {total.Elapsed.TotalSeconds:0.0} s.");
if (!single)
{
    var names = results[0].SpeciesNames;
    int survived = results.Count(r => r.AllSurvived);
    var coexist = results.Select(r => r.CoexistenceDays).Order().ToArray();
    Console.WriteLine($"Todas las especies vivas al final: {survived}/{results.Length} ({100.0 * survived / results.Length:0}%)");
    Console.WriteLine($"Días hasta la primera extinción: mediana {coexist[coexist.Length / 2]:0.0}, " +
                      $"mín {coexist[0]:0.0}, máx {coexist[^1]:0.0}");
    for (int s = 0; s < names.Length; s++)
    {
        int extinct = results.Count(r => r.ExtinctDay[s] != null);
        Console.WriteLine($"  {names[s]}: se extinguió en {extinct}/{results.Length} corridas");
    }
}
return 0;
