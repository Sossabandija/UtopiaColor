using Godot;
using UtopiaSim;

namespace UtopiaViewer;

/// <summary>Curva de población de cada especie desde el inicio (escala logarítmica o lineal).</summary>
public partial class PopulationChart : Control
{
    const int FontSize = 13;
    Simulation? sim;
    PopulationHistory? history;

    /// <summary>En escala log se ven a la vez 3.000 plantas y 5 carnívoros.</summary>
    public bool LogScale { get; set; } = true;

    internal void SetData(Simulation s, PopulationHistory h)
    {
        sim = s;
        history = h;
        QueueRedraw();
    }

    public override void _Draw()
    {
        var size = Size;
        var font = Palette.Font;
        DrawRect(new Rect2(Vector2.Zero, size), Palette.PlotBg);
        if (sim == null || history == null || history.Days.Count == 0) return;

        // Leyenda en dos columnas: población actual, o el día en que se extinguió.
        int species = sim.Species.Count;
        float colWidth = size.X / 2;
        for (int s = 0; s < species; s++)
        {
            var pos = new Vector2(8 + (s % 2) * colWidth, 16 + (s / 2) * 17);
            string text = history.ExtinctDay[s] is double d
                ? $"{sim.Species[s].Name}: extinta (día {d + 1:0.0})"
                : $"{sim.Species[s].Name}: {sim.Population(s)}";
            DrawString(font, pos, text, HorizontalAlignment.Left, colWidth - 12, FontSize, Palette.ForSpecies(s));
        }
        float legendHeight = 17 * ((species + 1) / 2) + 6;

        var plot = new Rect2(40, legendHeight + 6, size.X - 48, size.Y - legendHeight - 28);
        double max = Math.Max(1, history.Max);
        float bottom = plot.End.Y;
        float Y(double v) => LogScale
            ? bottom - (float)(Math.Log10(v + 1) / Math.Log10(max + 1)) * plot.Size.Y
            : bottom - (float)(v / max) * plot.Size.Y;

        // Líneas guía con su valor.
        var gridValues = LogScale
            ? Enumerable.Range(0, 7).Select(p => Math.Pow(10, p)).Where(v => v <= max * 1.01).Prepend(0)
            : Enumerable.Range(0, 5).Select(k => Math.Round(max * k / 4));
        foreach (double v in gridValues.Distinct())
        {
            float y = Y(v);
            DrawLine(new Vector2(plot.Position.X, y), new Vector2(plot.End.X, y), Palette.Grid, 1);
            DrawString(font, new Vector2(0, y + 4), v.ToString("0"), HorizontalAlignment.Right, plot.Position.X - 4, 11, Palette.TextDim);
        }

        double d0 = history.Days[0];
        double d1 = Math.Max(history.Days[^1], d0 + 1e-6);
        DrawString(font, new Vector2(plot.Position.X, size.Y - 6), $"día {d0 + 1:0}", HorizontalAlignment.Left, -1, 11, Palette.TextDim);
        DrawString(font, new Vector2(plot.Position.X, size.Y - 6), $"día {d1 + 1:0.0}", HorizontalAlignment.Right, plot.Size.X, 11, Palette.TextDim);

        int n = history.Days.Count;
        if (n < 2) return;
        int points = Math.Clamp((int)plot.Size.X, 2, n);
        var line = new Vector2[points];
        for (int s = 0; s < species; s++)
        {
            for (int i = 0; i < points; i++)
            {
                int idx = (int)((long)i * (n - 1) / (points - 1));
                float x = plot.Position.X + (float)((history.Days[idx] - d0) / (d1 - d0)) * plot.Size.X;
                line[i] = new Vector2(x, Y(history.Counts[idx][s]));
            }
            DrawPolyline(line, Palette.ForSpecies(s), 2f, true);
        }
    }
}
