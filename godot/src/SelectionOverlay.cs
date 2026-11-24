using Godot;
using UtopiaSim;

namespace UtopiaViewer;

/// <summary>
/// Dibuja sobre el mundo lo del ser seleccionado: un anillo, la línea hacia su objetivo
/// y su rango de visión cuando está buscando algo (como el anillo amarillo del original).
/// </summary>
public partial class SelectionOverlay : Node2D
{
    static readonly Color FoodLine = new(0.3f, 1f, 0.3f);
    static readonly Color WaterLine = new(0.4f, 0.75f, 1f);
    static readonly Color FleeLine = new(1f, 0.25f, 0.2f);
    static readonly Color Vision = new(1f, 1f, 0f, 0.35f);

    public WorldView? View { get; set; }

    public override void _Draw()
    {
        if (View?.Sim is not { } sim || View.Selected is not { } c) return;
        float px = 1f / View.Camera.Zoom.X; // 1 pixel de pantalla en unidades del mundo
        var pos = View.NearCamera(c.X, c.Y); // la copia del mundo que se está mirando

        DrawArc(pos, 36 + 8 * px, 0, Mathf.Tau, 40, c.Dead ? Colors.Gray : Colors.White, 2.5f * px, true);
        if (c.Dead || c.IsPlant) return;

        if (c.TopNeed is Need.Food or Need.Water or Need.Mate)
        {
            float vision = (float)(sim.Config.Movement.VisionTiles * sim.Map.TileSize);
            DrawArc(pos, vision, 0, Mathf.Tau, 128, Vision, 2f * px, true);
        }

        if (c.TargetKind == TargetKind.None || c.TopNeed == Need.Rest) return;
        var target = c.TargetKind == TargetKind.Creature
            ? new Vector2((float)c.TargetCreature!.X, (float)c.TargetCreature.Y)
            : new Vector2((float)c.TargetPoint.X, (float)c.TargetPoint.Y);
        // El camino más corto sobre el toro.
        var delta = new Vector2((float)sim.Map.Delta(target.X - pos.X), (float)sim.Map.Delta(target.Y - pos.Y));
        var color = c.TopNeed switch
        {
            Need.Safety => FleeLine,
            Need.Water => WaterLine,
            _ => FoodLine,
        };
        DrawLine(pos, pos + delta, color, 2.5f * px, true);
        DrawCircle(pos + delta, 7 * px, color);
    }
}
