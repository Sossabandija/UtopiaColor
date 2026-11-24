using Godot;

namespace UtopiaViewer;

/// <summary>Una curva del gráfico. <c>Wraps</c>: el valor da la vuelta (como el tono, de 1 vuelve a 0).</summary>
sealed record ChartSeries(string Legend, Color Color, IReadOnlyList<double> Values, bool Wraps = false);

/// <summary>
/// Gráfico de varias curvas en el tiempo (eje X en días), en escala log o lineal.
/// Los valores NaN cortan la curva (por ejemplo, cuando una especie no existe).
/// </summary>
public partial class TimeSeriesChart : Control
{
    const int LegendFontSize = 13;
    const int AxisFontSize = 11;

    IReadOnlyList<double> days = [];
    IReadOnlyList<ChartSeries> series = [];
    double? fixedMax;
    bool allowLog = true;

    public bool LogScale { get; set; } = true;

    /// <param name="max">Máximo fijo del eje Y (por ejemplo 1 para el tono); null = automático.</param>
    /// <param name="logAllowed">False para gráficos donde la escala log no tiene sentido.</param>
    internal void SetData(IReadOnlyList<double> xDays, IReadOnlyList<ChartSeries> curves, double? max = null, bool logAllowed = true)
    {
        days = xDays;
        series = curves;
        fixedMax = max;
        allowLog = logAllowed;
        QueueRedraw();
    }

    public override void _Draw()
    {
        var size = Size;
        var font = Palette.Font;
        DrawRect(new Rect2(Vector2.Zero, size), Palette.PlotBg);

        // Leyenda en dos columnas.
        float colWidth = size.X / 2;
        for (int i = 0; i < series.Count; i++)
        {
            var pos = new Vector2(8 + (i % 2) * colWidth, 16 + (i / 2) * 17);
            DrawString(font, pos, series[i].Legend, HorizontalAlignment.Left, colWidth - 12, LegendFontSize, series[i].Color);
        }
        float legendHeight = 17 * ((series.Count + 1) / 2) + 6;
        if (days.Count < 2)
        {
            DrawString(font, new Vector2(8, legendHeight + 24), "Juntando datos...", HorizontalAlignment.Left, -1, AxisFontSize, Palette.TextDim);
            return;
        }

        var plot = new Rect2(40, legendHeight + 6, size.X - 48, size.Y - legendHeight - 28);
        bool log = LogScale && allowLog;
        double max = fixedMax ?? Math.Max(1e-9, series.SelectMany(s => s.Values).Where(double.IsFinite).DefaultIfEmpty(1).Max());
        float bottom = plot.End.Y;
        float Y(double v) => log
            ? bottom - (float)(Math.Log10(Math.Max(0, v) + 1) / Math.Log10(max + 1)) * plot.Size.Y
            : bottom - (float)(v / max) * plot.Size.Y;

        foreach (double v in GridValues(max, log))
        {
            float y = Y(v);
            DrawLine(new Vector2(plot.Position.X, y), new Vector2(plot.End.X, y), Palette.Grid, 1);
            DrawString(font, new Vector2(0, y + 4), FormatAxis(v), HorizontalAlignment.Right, plot.Position.X - 4, AxisFontSize, Palette.TextDim);
        }

        double d0 = days[0], d1 = Math.Max(days[^1], d0 + 1e-6);
        DrawString(font, new Vector2(plot.Position.X, size.Y - 6), $"día {d0 + 1:0}", HorizontalAlignment.Left, -1, AxisFontSize, Palette.TextDim);
        DrawString(font, new Vector2(plot.Position.X, size.Y - 6), $"día {d1 + 1:0.0}", HorizontalAlignment.Right, plot.Size.X, AxisFontSize, Palette.TextDim);

        int n = days.Count;
        int points = Math.Clamp((int)plot.Size.X, 2, n);
        var segment = new List<Vector2>(points);
        foreach (var s in series)
        {
            segment.Clear();
            double previous = double.NaN;
            for (int i = 0; i < points; i++)
            {
                int idx = (int)((long)i * (n - 1) / (points - 1));
                double v = idx < s.Values.Count ? s.Values[idx] : double.NaN;
                bool jump = s.Wraps && double.IsFinite(previous) && double.IsFinite(v) && Math.Abs(v - previous) > max / 2;
                if (!double.IsFinite(v) || jump)
                {
                    Flush(segment, s.Color);
                    if (!double.IsFinite(v))
                    {
                        previous = double.NaN;
                        continue;
                    }
                }
                float x = plot.Position.X + (float)((days[idx] - d0) / (d1 - d0)) * plot.Size.X;
                segment.Add(new Vector2(x, Y(v)));
                previous = v;
            }
            Flush(segment, s.Color);
        }
    }

    void Flush(List<Vector2> segment, Color color)
    {
        if (segment.Count >= 2) DrawPolyline(segment.ToArray(), color, 2f, true);
        else if (segment.Count == 1) DrawCircle(segment[0], 1.5f, color);
        segment.Clear();
    }

    static IEnumerable<double> GridValues(double max, bool log)
    {
        if (log)
        {
            yield return 0;
            for (double p = 1; p <= max * 1.01; p *= 10) yield return p;
            yield break;
        }
        for (int k = 0; k <= 4; k++) yield return max * k / 4;
    }

    static string FormatAxis(double v) => v switch
    {
        0 => "0",
        >= 100 => v.ToString("0"),
        >= 10 => v.ToString("0.#"),
        _ => v.ToString("0.##"),
    };
}
