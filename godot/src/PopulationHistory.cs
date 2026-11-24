using UtopiaSim;

namespace UtopiaViewer;

/// <summary>Poblaciones a lo largo del tiempo, para el gráfico.</summary>
sealed class PopulationHistory
{
    /// <summary>Cada cuántos ticks se anota (como ARCHIVE_EVERY del original).</summary>
    public const int SampleEvery = 30;

    public List<double> Days { get; } = [];
    public List<int[]> Counts { get; } = [];
    public double?[] ExtinctDay { get; private set; } = [];
    public int Max { get; private set; } = 1;

    public void Reset(Simulation sim)
    {
        Days.Clear();
        Counts.Clear();
        ExtinctDay = new double?[sim.Species.Count];
        Max = 1;
        Record(sim);
    }

    /// <summary>Llamar después de cada tick.</summary>
    public void Observe(Simulation sim)
    {
        for (int s = 0; s < ExtinctDay.Length; s++)
            if (ExtinctDay[s] == null && sim.Population(s) == 0) ExtinctDay[s] = sim.Day;
        if (sim.Tick % SampleEvery == 0) Record(sim);
    }

    void Record(Simulation sim)
    {
        var counts = new int[sim.Species.Count];
        for (int s = 0; s < counts.Length; s++)
        {
            counts[s] = sim.Population(s);
            if (counts[s] > Max) Max = counts[s];
        }
        Days.Add(sim.Day);
        Counts.Add(counts);
    }
}
