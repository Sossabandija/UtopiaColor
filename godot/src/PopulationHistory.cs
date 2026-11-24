using UtopiaSim;

namespace UtopiaViewer;

/// <summary>Poblaciones a lo largo del tiempo, para el gráfico.</summary>
sealed class PopulationHistory
{
    /// <summary>Cada cuántos ticks se anota (como ARCHIVE_EVERY del original).</summary>
    public const int SampleEvery = 30;

    public List<double> Days { get; } = [];
    /// <summary>Una lista por especie, alineada con <see cref="Days"/>.</summary>
    public List<List<double>> Counts { get; } = [];
    public double?[] ExtinctDay { get; private set; } = [];

    public void Reset(Simulation sim)
    {
        Days.Clear();
        Counts.Clear();
        for (int s = 0; s < sim.Species.Count; s++) Counts.Add([]);
        ExtinctDay = new double?[sim.Species.Count];
        Record(sim);
    }

    /// <summary>Llamar después de cada tick.</summary>
    public void Observe(Simulation sim)
    {
        for (int s = 0; s < ExtinctDay.Length; s++)
            if (ExtinctDay[s] == null && sim.Population(s) == 0 && sim.HasArrived(s)) ExtinctDay[s] = sim.Day;
        if (sim.Tick % SampleEvery == 0) Record(sim);
    }

    void Record(Simulation sim)
    {
        Days.Add(sim.Day);
        for (int s = 0; s < Counts.Count; s++) Counts[s].Add(sim.Population(s));
    }
}
