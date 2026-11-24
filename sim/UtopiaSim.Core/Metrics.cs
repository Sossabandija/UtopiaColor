using System.Numerics;

namespace UtopiaSim;

/// <summary>Etapa de vida al morir. Juvenil = nunca se reprodujo (una planta: nunca soltó una semilla).</summary>
public enum LifeStage { Juvenile, Adult }

/// <summary>
/// Comida al alcance, promediada por individuo de una especie que come.
/// </summary>
/// <param name="Eaters">Población de la especie que come.</param>
/// <param name="Visible">Presas dentro de su rango de visión.</param>
/// <param name="RightStage">De esas, las que tienen el tamaño/peso justo: las podría comer si su color coincidiera perfecto.</param>
/// <param name="Edible">Las que de verdad puede comer (etapa y color), enteras o sus frutos.</param>
/// <param name="ColorDistance">Distancia promedio de color (gen matchGene) a las presas visibles; NaN si no ve ninguna.</param>
public readonly record struct FoodStats(int Eaters, double Visible, double RightStage, double Edible, double ColorDistance);

/// <summary>Centro y dispersión de un gen en una especie.</summary>
/// <param name="Spread">Distancia promedio de cada individuo al centro: crece si la especie se dispersa o se divide.</param>
/// <remarks>
/// Con genética diploide: <c>HiddenSpread</c> = distancia promedio del alelo oculto al centro de lo que se expresa
/// (compárese con <c>Spread</c>, la de lo expresado), y <c>AlleleGap</c> = distancia promedio entre los dos alelos
/// de cada individuo. NaN si la especie no es diploide.
/// </remarks>
public readonly record struct GeneStats(int Count, double MeanX, double MeanY, double Spread,
    double HiddenSpread = double.NaN, double AlleleGap = double.NaN);

/// <summary>Una medición en un momento. Nacimientos y muertes son los del intervalo desde la medición anterior.</summary>
public sealed class MetricsSample
{
    public required long Tick { get; init; }
    public required double Day { get; init; }
    public required double IntervalDays { get; init; }
    public required int[] Population { get; init; }
    /// <summary>Nacimientos en el intervalo, por especie.</summary>
    public required long[] Births { get; init; }
    /// <summary>Muertes en el intervalo, por [especie, causa].</summary>
    public required long[,] Deaths { get; init; }
    /// <summary>Por especie; null si la especie no come a nadie.</summary>
    public required FoodStats?[] Food { get; init; }
    /// <summary>Por [especie, gen].</summary>
    public required GeneStats[,] Genes { get; init; }
    /// <summary>Dispersión de semillas en el intervalo, por [especie, <see cref="DispersalCounter"/>].</summary>
    public required long[,] Dispersal { get; init; }
    /// <summary>Cazas fallidas por territorialidad en el intervalo, por especie del cazador.</summary>
    public required long[] HuntsFailed { get; init; }
    /// <summary>
    /// Fracción de la especie que está escondida ahora, en su zona de refugio o en una madriguera
    /// (NaN si no tiene refugio o no queda nadie).
    /// </summary>
    public required double[] InRefuge { get; init; }
    /// <summary>Fracción de la especie que está dentro de una madriguera ahora (NaN si no tiene madrigueras o no queda nadie).</summary>
    public required double[] InBurrow { get; init; }
    /// <summary>Fracción del cupo total de sus madrigueras que está reclamado (dentro o en camino); NaN sin madrigueras.</summary>
    public required double[] BurrowUse { get; init; }
    /// <summary>Fracción de la especie escondida en una planta grande ahora (NaN si no tiene cobertura o no queda nadie).</summary>
    public required double[] InCover { get; init; }
    /// <summary>Plantas diploides: semillas autopolinizadas en el intervalo, por especie.</summary>
    public required long[] Selfed { get; init; }
    /// <summary>Plantas diploides sin autopolinización: semillas que no se formaron por falta de polen en el intervalo.</summary>
    public required long[] Unpollinated { get; init; }
}

/// <summary>
/// Mediciones para entender la dinámica: comida al alcance, distancia de color, muertes por causa,
/// edad y etapa al morir, y centro/dispersión de los genes.
/// No consume números aleatorios ni modifica nada: medir no cambia la corrida.
/// </summary>
public sealed class SimMetrics : IDisposable
{
    /// <summary>Rango k de edad: [2^(k−1), 2^k) ticks; el rango 0 es edad 0.</summary>
    public const int AgeBins = 26;
    /// <summary>Tamaño (planta) o peso (animal) de las presas al ser comidas, en rangos de 0,05.</summary>
    public const int StageBins = 20;
    /// <summary>Para medir la comida se toma una muestra de hasta tantos individuos por especie.</summary>
    const int MaxEatersSampled = 100;

    readonly Simulation sim;
    readonly long[] birthsInterval;
    readonly long[,] deathsInterval;
    readonly List<Creature> buffer = [];
    long[,] lastDispersal;
    long[] lastHuntsFailed;
    long[] lastSelfed;
    long[] lastUnpollinated;
    long lastSampleTick;

    public SimMetrics(Simulation simulation)
    {
        sim = simulation;
        int s = sim.Species.Count, causes = Enum.GetValues<DeathCause>().Length;
        birthsInterval = new long[s];
        deathsInterval = new long[s, causes];
        DeathsByAge = new long[s, causes, 2, AgeBins];
        EatenAtStage = new long[s, StageBins];
        lastSampleTick = sim.Tick;
        lastDispersal = (long[,])sim.Stats.Dispersal.Clone();
        lastHuntsFailed = (long[])sim.Stats.HuntsFailed.Clone();
        lastSelfed = (long[])sim.Stats.Selfed.Clone();
        lastUnpollinated = (long[])sim.Stats.Unpollinated.Clone();
        sim.EventRaised += OnEvent;
    }

    /// <summary>Muertes acumuladas por [especie, causa, etapa de vida, rango de edad].</summary>
    public long[,,,] DeathsByAge { get; }

    /// <summary>Tamaño/peso de las presas al ser comidas, acumulado por [especie de la presa, rango].</summary>
    public long[,] EatenAtStage { get; }

    public void Dispose() => sim.EventRaised -= OnEvent;

    public static int AgeBin(long ticks) =>
        ticks <= 0 ? 0 : Math.Min(AgeBins - 1, 64 - BitOperations.LeadingZeroCount((ulong)ticks));

    /// <summary>Límites en ticks del rango de edad <paramref name="bin"/>: [desde, hasta).</summary>
    public static (long From, long To) AgeBinRange(int bin) => bin == 0 ? (0, 1) : (1L << (bin - 1), 1L << bin);

    public static LifeStage StageOf(Creature c) => c.Children > 0 ? LifeStage.Adult : LifeStage.Juvenile;

    void OnEvent(SimEvent e)
    {
        var c = e.Subject;
        if (e.Type == SimEventType.Born)
        {
            birthsInterval[c.Species]++;
        }
        else if (e.Type == SimEventType.Died)
        {
            int s = c.Species, cause = (int)e.Cause;
            deathsInterval[s, cause]++;
            DeathsByAge[s, cause, (int)StageOf(c), AgeBin(sim.Tick - c.BirthTick)]++;
            if (e.Cause == DeathCause.Eaten)
            {
                double stage = c.IsPlant ? c.PlantSize : c.Needs[Need.Food];
                EatenAtStage[s, Math.Clamp((int)(stage * StageBins), 0, StageBins - 1)]++;
            }
        }
    }

    /// <summary>Mide el estado actual y reinicia los contadores del intervalo.</summary>
    public MetricsSample Sample()
    {
        int species = sim.Species.Count;
        var food = new FoodStats?[species];
        var population = new int[species];
        for (int s = 0; s < species; s++)
        {
            population[s] = sim.Population(s);
            if (sim.PreySpeciesOf(s).Count > 0) food[s] = MeasureFood(s);
        }

        var dispersalNow = (long[,])sim.Stats.Dispersal.Clone();
        var dispersal = new long[dispersalNow.GetLength(0), dispersalNow.GetLength(1)];
        for (int s = 0; s < dispersal.GetLength(0); s++)
            for (int k = 0; k < dispersal.GetLength(1); k++)
                dispersal[s, k] = dispersalNow[s, k] - lastDispersal[s, k];
        lastDispersal = dispersalNow;

        var huntsNow = (long[])sim.Stats.HuntsFailed.Clone();
        var huntsFailed = huntsNow.Select((n, s) => n - lastHuntsFailed[s]).ToArray();
        lastHuntsFailed = huntsNow;
        var selfed = sim.Stats.Selfed.Select((n, s) => n - lastSelfed[s]).ToArray();
        lastSelfed = (long[])sim.Stats.Selfed.Clone();
        var unpollinated = sim.Stats.Unpollinated.Select((n, s) => n - lastUnpollinated[s]).ToArray();
        lastUnpollinated = (long[])sim.Stats.Unpollinated.Clone();

        var inRefuge = new double[species];
        var inBurrow = new double[species];
        var burrowUse = new double[species];
        var inCover = new double[species];
        for (int s = 0; s < species; s++)
        {
            int claims = 0, capacity = 0;
            foreach (var b in sim.Burrows)
            {
                if (b.Species != s) continue;
                claims += b.Claims;
                capacity += b.Capacity;
            }
            burrowUse[s] = capacity > 0 ? (double)claims / capacity : double.NaN;

            if (!sim.HasRefuge(s) || population[s] == 0)
            {
                inRefuge[s] = inBurrow[s] = inCover[s] = double.NaN;
                continue;
            }
            int hidden = 0, burrowed = 0, covered = 0;
            foreach (var c in sim.Creatures)
            {
                if (c.Species != s) continue;
                if (sim.InRefuge(c)) hidden++;
                if (sim.InBurrow(c)) burrowed++;
                if (sim.InCover(c)) covered++;
            }
            inRefuge[s] = (double)hidden / population[s];
            inBurrow[s] = sim.HasBurrows(s) ? (double)burrowed / population[s] : double.NaN;
            inCover[s] = sim.HasCover(s) ? (double)covered / population[s] : double.NaN;
        }

        var sample = new MetricsSample
        {
            Tick = sim.Tick,
            Day = sim.Day,
            IntervalDays = (double)(sim.Tick - lastSampleTick) / sim.Config.World.TicksPerDay,
            Population = population,
            Births = (long[])birthsInterval.Clone(),
            Deaths = (long[,])deathsInterval.Clone(),
            Food = food,
            Genes = MeasureGenes(),
            Dispersal = dispersal,
            HuntsFailed = huntsFailed,
            InRefuge = inRefuge,
            InBurrow = inBurrow,
            BurrowUse = burrowUse,
            InCover = inCover,
            Selfed = selfed,
            Unpollinated = unpollinated,
        };
        Array.Clear(birthsInterval);
        Array.Clear(deathsInterval);
        lastSampleTick = sim.Tick;
        return sample;
    }

    FoodStats MeasureFood(int species)
    {
        int eaters = sim.Population(species);
        if (eaters == 0) return new FoodStats(0, 0, 0, 0, double.NaN);

        int stride = Math.Max(1, (eaters + MaxEatersSampled - 1) / MaxEatersSampled);
        double halfWidth = sim.Config.Predation.HalfWidth;
        var match = sim.MatchGene;
        long visible = 0, rightStage = 0, edible = 0;
        double colorSum = 0;
        int sampled = 0, k = 0;

        foreach (var c in sim.Creatures)
        {
            if (c.Species != species || k++ % stride != 0) continue;
            sampled++;
            double center = sim.PreferredStage(c);
            foreach (int prey in sim.PreySpeciesOf(species))
            {
                sim.FindInRadius(c.X, c.Y, sim.VisionPx, prey, buffer);
                foreach (var q in buffer)
                {
                    if (q == c || !sim.CanSpot(c, q)) continue; // escondido en una planta y lejos: no lo ve
                    visible++;
                    if (match != null) colorSum += match.Space.Distance(c.Genes[match.Index], q.Genes[match.Index]);
                    double stage = q.IsPlant ? q.PlantSize : q.Needs[Need.Food];
                    if (Math.Abs(stage - center) <= halfWidth) rightStage++;
                    if (sim.BestMeal(c, q, out _) != MealKind.None) edible++;
                }
            }
        }
        return new FoodStats(eaters, (double)visible / sampled, (double)rightStage / sampled, (double)edible / sampled,
            visible > 0 && match != null ? colorSum / visible : double.NaN);
    }

    GeneStats[,] MeasureGenes()
    {
        int species = sim.Species.Count, genes = sim.Genes.Count;
        var count = new int[species];
        var sumX = new double[species, genes];
        var sumY = new double[species, genes];
        foreach (var c in sim.Creatures)
        {
            count[c.Species]++;
            for (int g = 0; g < genes; g++)
            {
                sumX[c.Species, g] += c.Genes[g].X;
                sumY[c.Species, g] += c.Genes[g].Y;
            }
        }

        var spread = new double[species, genes];
        var hiddenSum = new double[species, genes];
        var hiddenCount = new int[species, genes];
        var gapSum = new double[species, genes];
        foreach (var c in sim.Creatures)
        {
            int s = c.Species;
            for (int g = 0; g < genes; g++)
            {
                double mx = sumX[s, g] / count[s], my = sumY[s, g] / count[s];
                double dx = c.Genes[g].X - mx, dy = c.Genes[g].Y - my;
                spread[s, g] += Math.Sqrt(dx * dx + dy * dy);
                if (c.Alleles is not { } al) continue;
                var (a, b) = (al[2 * g], al[2 * g + 1]);
                gapSum[s, g] += Math.Sqrt((a.Value.X - b.Value.X) * (a.Value.X - b.Value.X) + (a.Value.Y - b.Value.Y) * (a.Value.Y - b.Value.Y));
                int h = Allele.Hidden(a, b);
                if (h < 0) continue;
                var v = al[2 * g + h].Value;
                hiddenSum[s, g] += Math.Sqrt((v.X - mx) * (v.X - mx) + (v.Y - my) * (v.Y - my));
                hiddenCount[s, g]++;
            }
        }

        var result = new GeneStats[species, genes];
        for (int s = 0; s < species; s++)
        {
            for (int g = 0; g < genes; g++)
            {
                result[s, g] = count[s] == 0
                    ? new GeneStats(0, double.NaN, double.NaN, double.NaN)
                    : new GeneStats(count[s], sumX[s, g] / count[s], sumY[s, g] / count[s], spread[s, g] / count[s],
                        sim.Species[s].Config.Diploid == null ? double.NaN
                            : hiddenCount[s, g] > 0 ? hiddenSum[s, g] / hiddenCount[s, g] : 0,
                        sim.Species[s].Config.Diploid == null ? double.NaN : gapSum[s, g] / count[s]);
            }
        }
        return result;
    }
}
