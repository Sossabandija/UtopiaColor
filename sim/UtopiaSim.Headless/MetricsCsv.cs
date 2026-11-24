using System.Globalization;

namespace UtopiaSim.Headless;

/// <summary>Escribe las mediciones de <see cref="SimMetrics"/> en CSV.</summary>
static class MetricsCsv
{
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    static readonly DeathCause[] Causes = Enum.GetValues<DeathCause>();
    static readonly DispersalCounter[] Counters = Enum.GetValues<DispersalCounter>();

    /// <summary>SeedsParent → seeds_parent.</summary>
    static string Snake(string pascal) =>
        string.Concat(pascal.Select((ch, i) => char.IsUpper(ch) ? (i > 0 ? "_" : "") + char.ToLowerInvariant(ch) : ch.ToString()));

    static IEnumerable<SpeciesDef> Dispersing(Simulation sim) => sim.Species.Where(sp => sp.IsPlant && sp.Plant.Dispersal != null);
    static IEnumerable<SpeciesDef> DiploidPlants(Simulation sim) => sim.Species.Where(sp => sp.IsPlant && sp.Config.Diploid != null);

    /// <summary>Nombre de cada esquina de un polígono de 3 o más lados (vertexNames, o v0, v1...).</summary>
    static IEnumerable<string> CornerNames(GeneDef g, PolygonSpace poly) =>
        g.Config.VertexNames ?? Enumerable.Range(0, poly.Sides).Select(i => $"v{i}").ToList();

    static string Lower(DeathCause c) => c.ToString().ToLowerInvariant();

    /// <summary>Genes que vale la pena medir en una especie: los que mutan en ella.</summary>
    static IEnumerable<GeneDef> MeasuredGenes(Simulation sim, SpeciesDef sp) =>
        sim.Genes.Where(g => sp.Mutation[g.Index] != null);

    /// <summary>
    /// Columnas de metrics.csv. Tasas en eventos por día (para comparar aunque cambie el intervalo).
    ///   {especie}_pop, _births_per_day, _deaths_{causa}_per_day
    ///   {especie que come}_food_visible / _food_right_stage / _food_edible / _food_color_dist
    ///   {especie}_{gen}_x / _y / _spread (y _hue si el gen es un disco, _pos de 0 a 1 si es una línea,
    ///   el peso de cada esquina si es un polígono de 3 o más lados)
    ///   {planta con dispersión}_seeds_parent/_wind/_gut/_burr, _seeds_digested, _fruits_eaten/_fallen, _burrs_attached/_fallen (por día)
    ///   {especie con territorialidad}_hunts_failed_per_day; {especie con refugio o madrigueras}_in_refuge (fracción escondida)
    ///   {especie con madrigueras}_in_burrow (fracción dentro de una madriguera), _burrow_use (fracción del cupo reclamada)
    ///   {especie con cobertura}_in_cover (fracción escondida en una planta grande)
    ///   {especie diploide}_{gen}_hidden_spread (distancia del alelo oculto al centro de lo expresado), _allele_gap (entre sus dos alelos)
    ///   {planta diploide}_selfed_per_day (semillas autopolinizadas), _unpollinated_per_day (sin polen, sin semilla)
    /// </summary>
    public static string Header(Simulation sim)
    {
        var cols = new List<string> { "tick", "day" };
        foreach (var sp in sim.Species)
        {
            cols.Add($"{sp.Name}_pop");
            cols.Add($"{sp.Name}_births_per_day");
            cols.AddRange(Causes.Select(c => $"{sp.Name}_deaths_{Lower(c)}_per_day"));
        }
        foreach (var sp in sim.Species.Where(sp => sim.PreySpeciesOf(sp.Index).Count > 0))
            cols.AddRange(new[] { "visible", "right_stage", "edible", "color_dist" }.Select(m => $"{sp.Name}_food_{m}"));
        foreach (var sp in sim.Species)
        {
            foreach (var g in MeasuredGenes(sim, sp))
            {
                cols.AddRange([$"{sp.Name}_{g.Name}_x", $"{sp.Name}_{g.Name}_y", $"{sp.Name}_{g.Name}_spread"]);
                if (g.Space is DiskSpace) cols.Add($"{sp.Name}_{g.Name}_hue");
                if (g.Space is PolygonSpace { Sides: 2 }) cols.Add($"{sp.Name}_{g.Name}_pos");
                if (g.Space is PolygonSpace { Sides: >= 3 } poly) cols.AddRange(CornerNames(g, poly).Select(v => $"{sp.Name}_{g.Name}_{v}"));
                if (sp.Config.Diploid != null) cols.AddRange([$"{sp.Name}_{g.Name}_hidden_spread", $"{sp.Name}_{g.Name}_allele_gap"]);
            }
        }
        foreach (var sp in Dispersing(sim))
            cols.AddRange(Counters.Select(k => $"{sp.Name}_{Snake(k.ToString())}_per_day"));
        foreach (var sp in sim.Species.Where(sp => sp.Config.Territory != null)) cols.Add($"{sp.Name}_hunts_failed_per_day");
        foreach (var sp in DiploidPlants(sim)) cols.AddRange([$"{sp.Name}_selfed_per_day", $"{sp.Name}_unpollinated_per_day"]);
        foreach (var sp in sim.Species.Where(sp => sim.HasRefuge(sp.Index))) cols.Add($"{sp.Name}_in_refuge");
        foreach (var sp in sim.Species.Where(sp => sim.HasBurrows(sp.Index))) cols.AddRange([$"{sp.Name}_in_burrow", $"{sp.Name}_burrow_use"]);
        foreach (var sp in sim.Species.Where(sp => sim.HasCover(sp.Index))) cols.Add($"{sp.Name}_in_cover");
        return string.Join(',', cols);
    }

    public static string Row(Simulation sim, MetricsSample m)
    {
        var cells = new List<string> { m.Tick.ToString(Inv), F(m.Day) };
        double days = Math.Max(m.IntervalDays, 1e-9);
        foreach (var sp in sim.Species)
        {
            int s = sp.Index;
            cells.Add(m.Population[s].ToString(Inv));
            cells.Add(F(m.Births[s] / days));
            cells.AddRange(Causes.Select(c => F(m.Deaths[s, (int)c] / days)));
        }
        foreach (var sp in sim.Species.Where(sp => sim.PreySpeciesOf(sp.Index).Count > 0))
        {
            var f = m.Food[sp.Index]!.Value;
            bool any = f.Eaters > 0;
            cells.Add(any ? F(f.Visible) : "");
            cells.Add(any ? F(f.RightStage) : "");
            cells.Add(any ? F(f.Edible) : "");
            cells.Add(double.IsNaN(f.ColorDistance) ? "" : F(f.ColorDistance));
        }
        foreach (var sp in sim.Species)
        {
            foreach (var g in MeasuredGenes(sim, sp))
            {
                var gs = m.Genes[sp.Index, g.Index];
                bool any = gs.Count > 0;
                cells.Add(any ? F(gs.MeanX) : "");
                cells.Add(any ? F(gs.MeanY) : "");
                cells.Add(any ? F(gs.Spread) : "");
                if (g.Space is DiskSpace) cells.Add(any ? F(DiskSpace.Hue(new Vec2(gs.MeanX, gs.MeanY))) : "");
                if (g.Space is PolygonSpace { Sides: 2 } line) cells.Add(any ? F(line.LinePosition(new Vec2(gs.MeanX, gs.MeanY))) : "");
                if (g.Space is PolygonSpace { Sides: >= 3 } poly)
                {
                    var w = any ? poly.VertexWeights(new Vec2(gs.MeanX, gs.MeanY)) : null;
                    for (int i = 0; i < poly.Sides; i++) cells.Add(w != null ? F(w[i]) : "");
                }
                if (sp.Config.Diploid != null)
                {
                    cells.Add(any && !double.IsNaN(gs.HiddenSpread) ? F(gs.HiddenSpread) : "");
                    cells.Add(any && !double.IsNaN(gs.AlleleGap) ? F(gs.AlleleGap) : "");
                }
            }
        }
        foreach (var sp in Dispersing(sim))
            cells.AddRange(Counters.Select(k => F(m.Dispersal[sp.Index, (int)k] / days)));
        foreach (var sp in sim.Species.Where(sp => sp.Config.Territory != null)) cells.Add(F(m.HuntsFailed[sp.Index] / days));
        foreach (var sp in DiploidPlants(sim))
        {
            cells.Add(F(m.Selfed[sp.Index] / days));
            cells.Add(F(m.Unpollinated[sp.Index] / days));
        }
        foreach (var sp in sim.Species.Where(sp => sim.HasRefuge(sp.Index)))
            cells.Add(double.IsNaN(m.InRefuge[sp.Index]) ? "" : F(m.InRefuge[sp.Index]));
        foreach (var sp in sim.Species.Where(sp => sim.HasBurrows(sp.Index)))
        {
            cells.Add(double.IsNaN(m.InBurrow[sp.Index]) ? "" : F(m.InBurrow[sp.Index]));
            cells.Add(double.IsNaN(m.BurrowUse[sp.Index]) ? "" : F(m.BurrowUse[sp.Index]));
        }
        foreach (var sp in sim.Species.Where(sp => sim.HasCover(sp.Index)))
            cells.Add(double.IsNaN(m.InCover[sp.Index]) ? "" : F(m.InCover[sp.Index]));
        return string.Join(',', cells);
    }

    /// <summary>
    /// Tabla de supervivencia: cuántos murieron, por especie, causa, etapa (juvenil = nunca se reprodujo)
    /// y rango de edad. Solo filas con al menos una muerte.
    /// </summary>
    public static void WriteDeaths(string path, Simulation sim, SimMetrics metrics)
    {
        using var w = new StreamWriter(path);
        w.WriteLine("species,cause,stage,age_from_days,age_to_days,count");
        double tpd = sim.Config.World.TicksPerDay;
        foreach (var sp in sim.Species)
        {
            foreach (var cause in Causes)
            {
                foreach (var stage in Enum.GetValues<LifeStage>())
                {
                    for (int bin = 0; bin < SimMetrics.AgeBins; bin++)
                    {
                        long n = metrics.DeathsByAge[sp.Index, (int)cause, (int)stage, bin];
                        if (n == 0) continue;
                        var (from, to) = SimMetrics.AgeBinRange(bin);
                        w.WriteLine($"{sp.Name},{Lower(cause)},{stage.ToString().ToLowerInvariant()},{F(from / tpd)},{F(to / tpd)},{n}");
                    }
                }
            }
        }
    }

    /// <summary>Tamaño (planta) o peso (animal) de las presas al ser comidas.</summary>
    public static void WritePreyStage(string path, Simulation sim, SimMetrics metrics)
    {
        using var w = new StreamWriter(path);
        w.WriteLine("species,stage_from,stage_to,eaten");
        foreach (var sp in sim.Species)
        {
            for (int b = 0; b < SimMetrics.StageBins; b++)
            {
                long n = metrics.EatenAtStage[sp.Index, b];
                if (n > 0) w.WriteLine($"{sp.Name},{F((double)b / SimMetrics.StageBins)},{F((double)(b + 1) / SimMetrics.StageBins)},{n}");
            }
        }
    }

    static string F(double v) => v.ToString("0.#####", Inv);
}
