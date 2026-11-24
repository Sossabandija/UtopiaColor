using UtopiaSim;

namespace UtopiaViewer;

/// <summary>
/// Mediciones de <see cref="SimMetrics"/> a lo largo del tiempo, guardadas como curvas con nombre
/// (por ejemplo "herbivoro.food.edible"). Todas alineadas con <see cref="Days"/>.
/// </summary>
sealed class MetricsHistory
{
    /// <summary>10 mediciones por día: medir la comida cuesta, no conviene hacerlo cada tick.</summary>
    public const int SampleEvery = 450;
    static readonly IReadOnlyList<double> Empty = [];

    readonly Dictionary<string, List<double>> curves = [];
    SimMetrics? metrics;

    public List<double> Days { get; } = [];

    public void Reset(Simulation sim)
    {
        metrics?.Dispose();
        metrics = new SimMetrics(sim);
        Days.Clear();
        curves.Clear();
    }

    /// <summary>Llamar después de cada tick.</summary>
    public void Observe(Simulation sim)
    {
        if (metrics != null && sim.Tick % SampleEvery == 0) Record(sim, metrics.Sample());
    }

    public IReadOnlyList<double> Get(string key) => curves.TryGetValue(key, out var list) ? list : Empty;

    public static string DeathKey(SpeciesDef sp, DeathCause cause) => $"{sp.Name}.deaths.{cause}";

    public static string CornerKey(SpeciesDef sp, GeneDef gene, int corner) => $"{sp.Name}.{gene.Name}.{corner}";

    public static string DispersalKey(SpeciesDef sp, DispersalCounter k) => $"{sp.Name}.disp.{k}";

    void Record(Simulation sim, MetricsSample m)
    {
        Days.Add(m.Day);
        double days = Math.Max(m.IntervalDays, 1e-9);
        var colorGene = Palette.ColorGene(sim);
        foreach (var sp in sim.Species)
        {
            int s = sp.Index;
            Add($"{sp.Name}.births", m.Births[s] / days);
            foreach (var cause in Enum.GetValues<DeathCause>()) Add(DeathKey(sp, cause), m.Deaths[s, (int)cause] / days);

            if (m.Food[s] is { } food)
            {
                bool any = food.Eaters > 0;
                Add($"{sp.Name}.food.visible", any ? food.Visible : double.NaN);
                Add($"{sp.Name}.food.rightStage", any ? food.RightStage : double.NaN);
                Add($"{sp.Name}.food.edible", any ? food.Edible : double.NaN);
                Add($"{sp.Name}.food.colorDistance", food.ColorDistance);
            }

            if (colorGene != null)
            {
                var g = m.Genes[s, colorGene.Index];
                Add($"{sp.Name}.hue", g.Count > 0 ? DiskSpace.Hue(new Vec2(g.MeanX, g.MeanY)) : double.NaN);
            }

            if (sp.StageGeneIndex >= 0 && sim.Genes[sp.StageGeneIndex].Space is PolygonSpace line)
            {
                var g = m.Genes[s, sp.StageGeneIndex];
                Add($"{sp.Name}.stage", g.Count > 0 ? line.LinePosition(new Vec2(g.MeanX, g.MeanY)) : double.NaN);
            }

            if (sp.DietGeneIndex >= 0 && sim.Genes[sp.DietGeneIndex].Space is PolygonSpace dietLine)
            {
                var g = m.Genes[s, sp.DietGeneIndex];
                Add($"{sp.Name}.diet", g.Count > 0 ? dietLine.LinePosition(new Vec2(g.MeanX, g.MeanY)) : double.NaN);
            }

            // Polígonos de 3 o más lados: peso de cada esquina en el centro de la especie.
            foreach (var gene in sim.Genes)
            {
                if (gene.Space is not PolygonSpace { Sides: >= 3 } poly || sp.Mutation[gene.Index] == null) continue;
                var g = m.Genes[s, gene.Index];
                var w = g.Count > 0 ? poly.VertexWeights(new Vec2(g.MeanX, g.MeanY)) : null;
                for (int i = 0; i < poly.Sides; i++) Add(CornerKey(sp, gene, i), w?[i] ?? double.NaN);
            }

            if (sp.IsPlant && sp.Plant.Dispersal != null)
                foreach (var k in Enum.GetValues<DispersalCounter>()) Add(DispersalKey(sp, k), m.Dispersal[s, (int)k] / days);

            if (sim.HasRefuge(s)) Add($"{sp.Name}.inRefuge", m.InRefuge[s] * 100);
            if (sp.Config.Territory is { Interference: > 0 }) Add($"{sp.Name}.huntsFailed", m.HuntsFailed[s] / days);
        }
    }

    void Add(string key, double value)
    {
        if (!curves.TryGetValue(key, out var list))
        {
            list = [];
            for (int i = 0; i < Days.Count - 1; i++) list.Add(double.NaN);
            curves[key] = list;
        }
        list.Add(value);
    }
}
