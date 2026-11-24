using Godot;
using UtopiaSim;

namespace UtopiaViewer;

/// <summary>Ficha del ser seleccionado: quién es, qué está haciendo, sus genes y sus necesidades.</summary>
public partial class InspectorView : Control
{
    const int TextSize = 14;
    const float LineHeight = 18;
    const float BarHeight = 16;
    const float BarStep = 21;

    Simulation? sim;
    Creature? creature;

    internal void SetCreature(Simulation? s, Creature? c)
    {
        sim = s;
        creature = c;
        if (s != null && c != null)
        {
            bool seedsWorld = s.Species.Any(sp => sp.IsPlant && sp.Plant.Dispersal != null);
            int lines = c.IsPlant
                ? 3 + ShownGenes(s, c).Count() + (c.Def.Plant.Dispersal != null ? 1 : 0)
                : 6 + ShownGenes(s, c).Count() + (c.Def.StageGeneIndex >= 0 ? 1 : 0) + (seedsWorld || c.Def.DietGeneIndex >= 0 ? 1 : 0);
            float height = 34 + lines * LineHeight + (c.IsPlant ? 0 : 6 + Need.Count * BarStep);
            CustomMinimumSize = new Vector2(400, height);
        }
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (sim == null || creature == null) return;
        var c = creature;
        var font = Palette.Font;
        float width = Size.X;
        float y = 22;

        void Line(string text, Color? color = null)
        {
            DrawString(font, new Vector2(0, y), text, HorizontalAlignment.Left, width, TextSize, color ?? Palette.TextDim);
            y += LineHeight;
        }

        // Encabezado: color del ser + nombre.
        DrawRect(new Rect2(0, 5, 22, 22), Palette.ForCreature(c, Palette.ColorGene(sim)));
        DrawRect(new Rect2(0, 5, 22, 22), Palette.ForSpecies(c.Species), false, 2);
        string title = c.IsPlant ? $"Planta #{c.Id}" : c.Name;
        DrawString(font, new Vector2(30, y), title, HorizontalAlignment.Left, -1, 19, Colors.White);
        if (!c.IsPlant)
        {
            float tw = font.GetStringSize(title, HorizontalAlignment.Left, -1, 19).X;
            DrawString(font, new Vector2(38 + tw, y), $"{c.Def.Name} #{c.Id}", HorizontalAlignment.Left, -1, TextSize, Palette.ForSpecies(c.Species));
        }
        y += 26;

        double ticksPerDay = sim.Config.World.TicksPerDay;
        if (c.Dead) Line($"Murió: {DeathText(c.CauseOfDeath)}", Palette.Danger);
        else Line($"Edad {(sim.Tick - c.BirthTick) / ticksPerDay:0.00} días · generación {c.Generation} · hijos {c.Children}");
        Line(c.ParentA == null
            ? "Población inicial"
            : c.ParentB == null ? $"Madre: #{c.ParentA}" : $"Padres: #{c.ParentA} y #{c.ParentB}");

        if (c.IsPlant)
        {
            if (!c.Landed) Line("Semilla en vuelo");
            else if (!c.Dead && Simulation.IsSeedling(c))
                Line($"Brote, tamaño {c.PlantSize:0.00}: riesgo de morir {sim.SeedlingRiskPerDay(c):0.0}/día hasta {c.Def.Plant.Seedling!.UntilSize:0.0#}");
            else Line($"Tamaño {c.PlantSize:0.00} (suelta semilla al llegar a 1)");
            if (c.Def.Plant.Dispersal != null)
            {
                string origin = c.ParentA == null ? "" : $"Llegó {OriginText(c.Origin)} · ";
                Line($"{origin}frutos {c.Fruits} · abrojos {c.Burrs}");
            }
        }
        else
        {
            Line($"Peso {c.Needs[Need.Food]:0.00} · comidas {c.Meals} · tragos {c.Drinks}");
            string status = sim.IsDigesting(c) ? $" · saciado {(c.DigestUntil - sim.Tick) / ticksPerDay * 24:0.0} h más"
                : sim.IsHuntCooldown(c) ? $" · cazó sin éxito, espera {(c.HuntCooldownUntil - sim.Tick) / ticksPerDay * 24:0.0} h"
                : "";
            if (sim.InRefuge(c)) status += " · en su refugio";
            if (c.TooCrowdedToBreed) status += " · demasiados rivales para criar";
            if (!c.Dead) Line($"Ahora: {Doing(c)}{status}", Colors.White);
            if (c.Def.StageGeneIndex >= 0) Line($"Prefiere presas en etapa {sim.PreferredStage(c):0.00} (0 = temprano, 1 = tardío)");
            bool seedsWorld = sim.Species.Any(sp => sp.IsPlant && sp.Plant.Dispersal != null);
            string carried = $"{c.SeedsInGut} semillas en el estómago · {c.BurrsCarried} abrojos";
            if (c.Def.DietGeneIndex >= 0) Line($"Dieta {sim.Diet(c):0.00} · {carried}");
            else if (seedsWorld) Line($"Lleva {carried}");
        }

        foreach (var g in ShownGenes(sim, c))
        {
            var v = c.Genes[g.Index];
            string value = g.Space switch
            {
                DiskSpace => $"tono {DiskSpace.Hue(v):0.00} · saturación {DiskSpace.Saturation(v):0.00}",
                PolygonSpace { Sides: 2 } line => $"{line.LinePosition(v):0.00}",
                PolygonSpace polygon => string.Join(" / ", polygon.VertexWeights(v).Select((w, i) =>
                    g.Config.VertexNames is { } names ? $"{names[i]} {w:0.00}" : w.ToString("0.00"))),
                _ => "",
            };
            bool mutates = c.Def.Mutation[g.Index] != null;
            Line($"Gen {g.Name}: {value}{(mutates ? "" : " (no muta)")}");
        }

        if (c.IsPlant) return;

        y += 6;
        Line("Urgencia (barra larga = más urgente):");
        var caps = sim.Config.Needs.Caps;
        float barX = 112, barWidth = width - barX - 4;
        for (int i = 0; i < Need.Count; i++)
        {
            float top = y - 13;
            DrawString(font, new Vector2(0, top + 13), Palette.Capitalize(Need.Names[i]), HorizontalAlignment.Right, barX - 8, TextSize, Palette.TextDim);
            float available = (float)(1 - caps[i]) * barWidth;
            DrawRect(new Rect2(barX, top, available, BarHeight), new Color(0, 0, 0, 0.6f));
            float urgency = (float)Math.Clamp(1 - c.Needs[i], 0, 1) * barWidth;
            DrawRect(new Rect2(barX, top, Math.Min(urgency, available), BarHeight), Palette.Needs[i]);
            if (i == c.TopNeed && !c.Dead) DrawRect(new Rect2(barX - 2, top - 2, available + 4, BarHeight + 4), Colors.White, false, 2);
            y += BarStep;
        }
    }

    static string DeathText(DeathCause? cause) => cause switch
    {
        DeathCause.Eaten => "comido",
        DeathCause.Starved => "de hambre",
        DeathCause.Dehydrated => "de sed",
        DeathCause.Crowded => "sin espacio (su casilla estaba llena)",
        DeathCause.Seedling => "como brote, antes de establecerse",
        _ => "?",
    };

    /// <summary>Genes que importan para este ser: los que muta su especie o los que usa (color, etapa, dieta, dispersión).</summary>
    static IEnumerable<GeneDef> ShownGenes(Simulation sim, Creature c) => sim.Genes.Where(g =>
        c.Def.Mutation[g.Index] != null || g == sim.MatchGene || g.Index == c.Def.StageGeneIndex ||
        g.Index == c.Def.DietGeneIndex || g.Index == c.Def.DispersalGeneIndex);

    static string OriginText(SeedOrigin origin) => origin switch
    {
        SeedOrigin.Wind => "con el viento",
        SeedOrigin.Gut => "en el estómago de un animal",
        SeedOrigin.Burr => "pegada a un animal (abrojo)",
        _ => "cayendo de su madre",
    };

    static string Doing(Creature c)
    {
        string target = c.TargetKind switch
        {
            TargetKind.Creature => $"{c.TargetCreature!.Def.Name} #{c.TargetCreature.Id}",
            TargetKind.Water => "agua",
            _ => "",
        };
        bool hasTarget = target.Length > 0;
        return c.TopNeed switch
        {
            Need.Food => hasTarget ? $"va a comerse a {target}" : "busca comida (deambula)",
            Need.Water => "va a beber",
            Need.Mate => hasTarget ? $"busca pareja: {target}" : "busca pareja (deambula)",
            Need.Rest => "duerme",
            Need.Safety => c.TargetKind == TargetKind.Refuge ? "huye a su refugio" : hasTarget ? $"huye de {target}" : "huye",
            Need.Care => hasTarget ? $"cuida a su cría {target}" : "cuida a su cría",
            _ => "recién nacido",
        };
    }
}
