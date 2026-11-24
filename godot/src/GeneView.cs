using Godot;
using UtopiaSim;

namespace UtopiaViewer;

/// <summary>
/// La forma de un gen con un símbolo por ser (como la rueda de color del original).
/// Disco: rueda de color. Polígono: la forma con sus esquinas. Línea: un histograma por especie.
/// Solo se muestran las especies en las que el gen muta (si no muta en ninguna, todas).
/// </summary>
public partial class GeneView : Control
{
    const int MaxSymbolsPerSpecies = 2500;
    const int LineBins = 30;
    static ImageTexture? wheel;

    Simulation? sim;
    GeneDef? gene;

    public Creature? Selected { get; set; }

    internal void SetSimulation(Simulation s)
    {
        sim = s;
        gene = s.Genes.Count > 0 ? s.Genes[0] : null;
        wheel ??= BuildWheel(256);
        QueueRedraw();
    }

    public void SetGene(int index)
    {
        if (sim == null || index < 0 || index >= sim.Genes.Count) return;
        gene = sim.Genes[index];
        QueueRedraw();
    }

    public override void _Draw()
    {
        var size = Size;
        DrawRect(new Rect2(Vector2.Zero, size), Palette.PlotBg);
        if (sim == null || gene == null)
        {
            DrawString(Palette.Font, new Vector2(10, 24), "Esta configuración no tiene genes.", HorizontalAlignment.Left, -1, 14, Palette.TextDim);
            return;
        }

        var shown = SpeciesShown();
        float legendHeight = 24;
        float side = Math.Min(size.X, size.Y - legendHeight);
        var center = new Vector2(size.X / 2, side / 2);
        float radius = side / 2 - 18;

        switch (gene.Space)
        {
            case DiskSpace:
                DrawDisk(center, radius, shown);
                break;
            case PolygonSpace { Sides: 2 } line:
                DrawLineGene(line, new Rect2(10, 8, size.X - 20, size.Y - legendHeight - 16), shown);
                break;
            case PolygonSpace polygon:
                DrawPolygonGene(polygon, center, radius, shown);
                break;
        }
        DrawLegend(new Vector2(10, size.Y - 8), shown);
    }

    /// <summary>Índices de las especies a mostrar, de la más numerosa a la menos (las raras quedan encima).</summary>
    List<int> SpeciesShown()
    {
        var mutating = sim!.Species.Where(sp => sp.Mutation[gene!.Index] != null).Select(sp => sp.Index).ToList();
        if (mutating.Count == 0) mutating = sim.Species.Select(sp => sp.Index).ToList();
        return mutating.OrderByDescending(sim.Population).ToList();
    }

    Vector2 ToScreen(Vec2 p, Vector2 center, float radius) => center + new Vector2((float)p.X, (float)p.Y) * radius;

    void DrawDisk(Vector2 center, float radius, List<int> shown)
    {
        DrawTextureRect(wheel!, new Rect2(center - new Vector2(radius, radius), new Vector2(radius * 2, radius * 2)), false, new Color(0.85f, 0.85f, 0.85f));
        DrawArc(center, radius, 0, Mathf.Tau, 96, new Color(1, 1, 1, 0.5f), 1.5f, true);
        // Sobre la rueda clara, los símbolos van oscuros.
        DrawSymbols(center, radius, shown, s => Palette.ForSpecies(s).Darkened(0.55f));
    }

    void DrawPolygonGene(PolygonSpace polygon, Vector2 center, float radius, List<int> shown)
    {
        var verts = polygon.Vertices.Select(v => ToScreen(v, center, radius)).ToArray();
        DrawColoredPolygon(verts, new Color(0.16f, 0.17f, 0.21f));
        DrawPolyline([.. verts, verts[0]], new Color(1, 1, 1, 0.6f), 1.5f, true);
        for (int i = 0; i < verts.Length; i++)
        {
            var label = center + (verts[i] - center) * 1.0f + (verts[i] - center).Normalized() * 12;
            DrawString(Palette.Font, label + new Vector2(-6, 5), (i + 1).ToString(), HorizontalAlignment.Left, -1, 13, Palette.TextDim);
        }
        DrawSymbols(center, radius, shown, Palette.ForSpecies);
    }

    void DrawSymbols(Vector2 center, float radius, List<int> shown, Func<int, Color> colorOf)
    {
        int g = gene!.Index;
        foreach (int s in shown)
        {
            int stride = Math.Max(1, sim!.Population(s) / MaxSymbolsPerSpecies);
            int k = 0;
            var color = colorOf(s);
            foreach (var c in sim.Creatures)
            {
                if (c.Species != s || k++ % stride != 0) continue;
                DrawSymbol(s, ToScreen(c.Genes[g], center, radius), color);
            }
        }
        if (Selected is { } sel)
        {
            var p = ToScreen(sel.Genes[g], center, radius);
            DrawArc(p, 8, 0, Mathf.Tau, 20, Colors.White, 2.5f, true);
            DrawArc(p, 10.5f, 0, Mathf.Tau, 20, Colors.Black, 1.5f, true);
        }
    }

    /// <summary>Símbolos del original: planta X, herbívoro cuadrado, carnívoro círculo.</summary>
    void DrawSymbol(int species, Vector2 p, Color color, float r = 3.5f)
    {
        switch (species % 4)
        {
            case 0:
                DrawLine(p + new Vector2(-r, -r), p + new Vector2(r, r), color, 1.2f);
                DrawLine(p + new Vector2(-r, r), p + new Vector2(r, -r), color, 1.2f);
                break;
            case 1:
                DrawRect(new Rect2(p - new Vector2(r, r), new Vector2(2 * r, 2 * r)), color, false, 1.2f);
                break;
            case 2:
                DrawArc(p, r, 0, Mathf.Tau, 10, color, 1.2f);
                break;
            default:
                DrawPolyline([p + new Vector2(0, -r), p + new Vector2(r, r), p + new Vector2(-r, r), p + new Vector2(0, -r)], color, 1.2f);
                break;
        }
    }

    void DrawLineGene(PolygonSpace line, Rect2 area, List<int> shown)
    {
        var font = Palette.Font;
        int g = gene!.Index;
        float axisY = area.End.Y - 14;
        float rowHeight = Math.Min(90, (axisY - area.Position.Y) / Math.Max(1, shown.Count));
        float binWidth = area.Size.X / LineBins;

        for (int r = 0; r < shown.Count; r++)
        {
            int s = shown[r];
            var bins = new int[LineBins];
            double sum = 0;
            int n = 0;
            foreach (var c in sim!.Creatures)
            {
                if (c.Species != s) continue;
                double v = line.LinePosition(c.Genes[g]);
                bins[Math.Min(LineBins - 1, (int)(v * LineBins))]++;
                sum += v;
                n++;
            }
            float top = area.Position.Y + r * rowHeight;
            float barMax = rowHeight - 24;
            int peak = Math.Max(1, bins.Max());
            var color = Palette.ForSpecies(s);
            for (int b = 0; b < LineBins; b++)
            {
                float h = barMax * bins[b] / peak;
                DrawRect(new Rect2(area.Position.X + b * binWidth, top + rowHeight - 4 - h, binWidth - 1, h), color);
            }
            string mean = n > 0 ? $"media {sum / n:0.00}" : "sin individuos";
            DrawString(font, new Vector2(area.Position.X, top + 14), $"{sim.Species[s].Name}: {mean}", HorizontalAlignment.Left, -1, 13, color);
            if (n > 0)
            {
                float mx = area.Position.X + (float)(sum / n) * area.Size.X;
                DrawLine(new Vector2(mx, top + 18), new Vector2(mx, top + rowHeight - 4), Colors.White, 1.5f);
            }
            if (Selected is { } sel && sel.Species == s)
            {
                float sx = area.Position.X + (float)line.LinePosition(sel.Genes[g]) * area.Size.X;
                DrawColoredPolygon([new(sx, top + rowHeight - 2), new(sx - 6, top + rowHeight + 8), new(sx + 6, top + rowHeight + 8)], Colors.White);
            }
        }

        DrawLine(new Vector2(area.Position.X, axisY), new Vector2(area.End.X, axisY), Palette.TextDim, 1);
        bool isStage = sim!.Species.Any(sp => sp.StageGeneIndex == g);
        DrawString(font, new Vector2(area.Position.X, axisY + 14), isStage ? "0 = temprano" : "0", HorizontalAlignment.Left, -1, 12, Palette.TextDim);
        DrawString(font, new Vector2(area.Position.X, axisY + 14), isStage ? "1 = tardío" : "1", HorizontalAlignment.Right, area.Size.X, 12, Palette.TextDim);
    }

    void DrawLegend(Vector2 pos, List<int> shown)
    {
        float x = pos.X;
        foreach (int s in shown.OrderBy(i => i))
        {
            DrawSymbol(s, new Vector2(x + 5, pos.Y - 5), Palette.ForSpecies(s), 4.5f);
            string text = sim!.Species[s].Name;
            DrawString(Palette.Font, new Vector2(x + 15, pos.Y), text, HorizontalAlignment.Left, -1, 13, Palette.ForSpecies(s));
            x += 30 + Palette.Font.GetStringSize(text, HorizontalAlignment.Left, -1, 13).X;
        }
    }

    static ImageTexture BuildWheel(int n)
    {
        var img = Image.CreateEmpty(n, n, false, Image.Format.Rgba8);
        for (int px = 0; px < n; px++)
        {
            for (int py = 0; py < n; py++)
            {
                double x = (px + 0.5) / n * 2 - 1, y = (py + 0.5) / n * 2 - 1;
                img.SetPixel(px, py, x * x + y * y > 1 ? new Color(0, 0, 0, 0) : Palette.FromDisk(new Vec2(x, y)));
            }
        }
        return ImageTexture.CreateFromImage(img);
    }
}
