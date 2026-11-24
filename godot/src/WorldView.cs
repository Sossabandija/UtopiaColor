using System.Numerics;
using Godot;
using UtopiaSim;
using Vector2 = Godot.Vector2;

namespace UtopiaViewer;

/// <summary>
/// El mundo visto desde arriba: terreno, seres y selección.
/// Arrastrar mueve la cámara, la rueda hace zoom y un clic selecciona al ser más cercano.
/// </summary>
public partial class WorldView : Node2D
{
    const float MinZoom = 0.02f;
    const float MaxZoom = 4f;
    const int TerrainSamplesPerTile = 8;
    const float ClickTolerancePx = 6;
    const float MinPlantScreenPx = 2.2f;
    const float MinSeedScreenPx = 1.2f;
    const float MinAnimalScreenPx = 6.5f;
    const float MinBurrowScreenPx = 7f;
    const float TerrainSaturation = 0.6f;
    const float TerrainBrightness = 0.55f;

    // Colores del terreno del original, de lo más bajo a lo más alto.
    static readonly Color[] GroundColors =
    [
        Rgb(150, 135, 120), Rgb(150, 135, 120), Rgb(237, 219, 199), Rgb(121, 212, 85), Rgb(41, 163, 61),
        Rgb(153, 153, 153), Rgb(124, 138, 156), Rgb(255, 255, 255), Rgb(255, 255, 255),
    ];
    static readonly Color WaterColor = Rgb(40, 80, 220);

    /// <summary>
    /// El mundo es un toro: alrededor del mapa se dibujan 8 copias atenuadas, así al mirar cerca
    /// de un borde se ve lo que hay "al otro lado" y se nota que no es el mapa principal.
    /// </summary>
    static readonly Color CopyModulate = new(0.6f, 0.6f, 0.6f);

    Simulation? sim;
    GeneDef? colorGene;
    Sprite2D terrain = null!;
    readonly List<Sprite2D> terrainCopies = [];
    CanvasModulate night = null!;
    Node2D creatureRoot = null!;
    SelectionOverlay overlay = null!;
    SpeciesLayer[] layers = [];
    SpeciesLayer? markers; // frutos y abrojos sobre las plantas
    SpeciesLayer? burrowLayer; // madrigueras: sobre las plantas (si no, el pasto las tapa) y bajo los animales
    bool dragging;
    float dragDistance;

    /// <summary>Ancho del panel de la derecha, para centrar el mundo en el espacio que queda libre.</summary>
    public float PanelWidth { get; set; }
    public Camera2D Camera { get; private set; } = null!;
    public Simulation? Sim => sim;
    public Creature? Selected { get; private set; }
    public bool Follow { get; set; }
    public bool NightTint { get; set; } = true;

    public event Action<Creature?>? SelectionChanged;

    public override void _Ready()
    {
        Camera = new Camera2D();
        AddChild(Camera);
        Camera.MakeCurrent();

        night = new CanvasModulate();
        AddChild(night);

        foreach (var _ in CopyOffsets(1))
        {
            var copy = new Sprite2D { Centered = false, TextureFilter = TextureFilterEnum.Linear, Modulate = CopyModulate };
            AddChild(copy);
            terrainCopies.Add(copy);
        }
        terrain = new Sprite2D { Centered = false, TextureFilter = TextureFilterEnum.Linear };
        AddChild(terrain);

        creatureRoot = new Node2D();
        AddChild(creatureRoot);

        overlay = new SelectionOverlay { View = this };
        AddChild(overlay);
    }

    public void SetSimulation(Simulation s)
    {
        sim = s;
        colorGene = Palette.ColorGene(s);
        Select(null);
        BuildTerrain();

        foreach (var l in layers) l.Free();
        markers?.Free();
        burrowLayer?.Free();
        // Plantas primero para que queden debajo de los animales; entre medio, sus frutos y abrojos y las madrigueras.
        float width = (float)s.Map.Width;
        layers = new SpeciesLayer[s.Species.Count];
        foreach (var sp in s.Species.Where(sp => sp.IsPlant))
            layers[sp.Index] = new SpeciesLayer(creatureRoot, sp, Palette.ForSpecies(sp.Index), CopyOffsets(width));
        markers = s.Species.Any(sp => sp.IsPlant && sp.Plant.Dispersal != null)
            ? new SpeciesLayer(creatureRoot, -1, Meshes.Circle, withBorder: false, default, CopyOffsets(width))
            : null;
        burrowLayer = s.Burrows.Count > 0
            ? new SpeciesLayer(creatureRoot, -1, Meshes.Circle, withBorder: true, Palette.BurrowRim, CopyOffsets(width))
            : null;
        foreach (var sp in s.Species.Where(sp => !sp.IsPlant))
            layers[sp.Index] = new SpeciesLayer(creatureRoot, sp, Palette.ForSpecies(sp.Index), CopyOffsets(width));

        FitView();
    }

    /// <summary>Encuadra el mundo completo en el espacio a la izquierda del panel.</summary>
    public void FitView()
    {
        if (sim == null) return;
        var vp = GetViewportRect().Size;
        float width = (float)sim.Map.Width;
        float z = 0.95f * Math.Min(vp.X - PanelWidth, vp.Y) / width;
        Camera.Zoom = new Vector2(z, z);
        Camera.Position = new Vector2(width / 2 + PanelWidth / (2 * z), width / 2);
        Follow = false;
    }

    public void Select(Creature? c)
    {
        Selected = c;
        if (c == null) Follow = false;
        SelectionChanged?.Invoke(c);
    }

    /// <summary>Copia el estado de la simulación a lo que se dibuja. Llamar una vez por cuadro.</summary>
    public void Sync()
    {
        if (sim == null) return;

        // Tamaño real en el mundo, pero nunca menos que unos pocos pixeles en pantalla.
        float zoom = Camera.Zoom.X;
        float minPlant = MinPlantScreenPx / zoom, minSeed = MinSeedScreenPx / zoom, minAnimal = MinAnimalScreenPx / zoom;

        foreach (var l in layers) l.Begin(sim.Population(l.Species));
        markers?.Begin(2 * sim.Species.Where(sp => sp.IsPlant).Sum(sp => sim.Population(sp.Index)));
        foreach (var c in sim.Creatures)
        {
            var pos = new Vector2((float)c.X, (float)c.Y);
            var color = Palette.ForCreature(c, colorGene);
            if (c.IsPlant)
            {
                float r = c.Landed
                    ? Math.Max(minPlant, 5f + 25f * (float)Math.Clamp(c.PlantSize, 0, 1))
                    : Math.Max(minSeed, 5f);
                if (!c.Landed) color.A = 0.6f;
                layers[c.Species].Add(pos, 0, r, color);
                // Frutos y abrojos: un punto a cada lado, más grande mientras más tenga.
                if (c.Fruits > 0) markers?.Add(pos + new Vector2(-0.6f, -0.6f) * r, 0, Math.Max(minSeed * 2, 5f + 3f * c.Fruits), Palette.Dispersal[0]);
                if (c.Burrs > 0) markers?.Add(pos + new Vector2(0.6f, -0.6f) * r, 0, Math.Max(minSeed * 2, 5f + 3f * c.Burrs), Palette.Dispersal[2]);
            }
            else
            {
                // Más gordo = más lleno, como en el original. Escondido (madriguera o planta): semitransparente.
                float s = Math.Max(minAnimal, 16f + 14f * (float)Math.Clamp(c.Needs[Need.Food], 0, 1));
                if ((c.Burrow != null || c.CoverPlant != null) && (sim.InBurrow(c) || sim.InCover(c))) color.A = 0.45f;
                layers[c.Species].Add(pos, (float)c.Angle, s, color);
            }
        }
        foreach (var l in layers) l.End();
        markers?.End();

        // Madrigueras: hoyo oscuro que se llena con el color de su especie según el cupo reclamado.
        if (burrowLayer != null)
        {
            burrowLayer.Begin(sim.Burrows.Count);
            float r = Math.Max(MinBurrowScreenPx / zoom, 0.4f * (float)sim.Map.TileSize);
            foreach (var b in sim.Burrows)
            {
                var fill = Palette.BurrowHole.Lerp(Palette.ForSpecies(b.Species), (float)b.Claims / b.Capacity);
                burrowLayer.Add(new Vector2((float)b.X, (float)b.Y), 0, r, fill);
            }
            burrowLayer.End();
        }

        if (Selected is { Dead: true }) Follow = false;
        if (Follow && Selected != null)
            Camera.Position = new Vector2((float)Selected.X + PanelWidth / (2 * Camera.Zoom.X), (float)Selected.Y);

        float f = NightTint ? 0.62f + 0.38f * Mathf.Sqrt((float)sim.Daylight) : 1f;
        night.Color = new Color(f, f, Math.Min(1f, f + 0.08f));
        overlay.QueueRedraw();
    }

    public override void _UnhandledInput(InputEvent e)
    {
        switch (e)
        {
            case InputEventMouseButton { ButtonIndex: MouseButton.WheelUp, Pressed: true } wheelUp:
                ZoomAt(wheelUp.Position, 1.15f);
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.WheelDown, Pressed: true } wheelDown:
                ZoomAt(wheelDown.Position, 1 / 1.15f);
                break;
            case InputEventMouseButton mb when mb.ButtonIndex is MouseButton.Left or MouseButton.Right or MouseButton.Middle:
                if (mb.Pressed)
                {
                    dragging = true;
                    dragDistance = 0;
                }
                else
                {
                    if (dragging && mb.ButtonIndex == MouseButton.Left && dragDistance < ClickTolerancePx)
                        SelectAt(ScreenToWorld(mb.Position));
                    dragging = false;
                }
                break;
            case InputEventMouseMotion mm when dragging:
                if (mm.ButtonMask == 0)
                {
                    dragging = false;
                    return;
                }
                dragDistance += mm.Relative.Length();
                if (dragDistance >= ClickTolerancePx)
                {
                    Follow = false;
                    Camera.Position -= mm.Relative / Camera.Zoom.X;
                }
                break;
            default:
                return;
        }
        GetViewport().SetInputAsHandled();
    }

    Vector2 ScreenToWorld(Vector2 screen) =>
        Camera.Position + (screen - GetViewportRect().Size / 2) / Camera.Zoom.X;

    void ZoomAt(Vector2 screen, float factor)
    {
        var before = ScreenToWorld(screen);
        float z = Math.Clamp(Camera.Zoom.X * factor, MinZoom, MaxZoom);
        Camera.Zoom = new Vector2(z, z);
        if (!Follow) Camera.Position += before - ScreenToWorld(screen);
    }

    /// <summary>Selecciona al animal más cercano al clic; si no hay, a la planta más cercana.</summary>
    void SelectAt(Vector2 world)
    {
        if (sim == null) return;
        float radius = Math.Max(40f, 14f / Camera.Zoom.X);
        Creature? best = Nearest(world, radius, plants: false) ?? Nearest(world, radius, plants: true);
        Select(best);
    }

    /// <summary>Las 8 posiciones de las copias alrededor del mapa, para un mundo de ancho <paramref name="width"/>.</summary>
    static IEnumerable<Vector2> CopyOffsets(float width)
    {
        for (int i = -1; i <= 1; i++)
            for (int j = -1; j <= 1; j++)
                if (i != 0 || j != 0) yield return new Vector2(i * width, j * width);
    }

    /// <summary>Lleva un punto a la copia del mundo más cercana a la cámara (para dibujar la selección).</summary>
    public Vector2 NearCamera(double x, double y)
    {
        if (sim == null) return new Vector2((float)x, (float)y);
        return new Vector2(
            Camera.Position.X + (float)sim.Map.Delta(x - Camera.Position.X),
            Camera.Position.Y + (float)sim.Map.Delta(y - Camera.Position.Y));
    }

    Creature? Nearest(Vector2 world, float radius, bool plants)
    {
        Creature? best = null;
        float bestDist = radius;
        foreach (var c in sim!.Creatures)
        {
            if (c.IsPlant != plants) continue;
            float d = (float)sim.Map.LoopDistance(world.X, world.Y, c.X, c.Y); // el clic puede caer en una copia
            if (d < bestDist)
            {
                bestDist = d;
                best = c;
            }
        }
        return best;
    }

    void BuildTerrain()
    {
        var map = sim!.Map;
        int res = map.Size * TerrainSamplesPerTile;
        double step = map.Width / res;
        var img = Image.CreateEmpty(res, res, false, Image.Format.Rgb8);
        for (int i = 0; i < res; i++)
            for (int j = 0; j < res; j++)
                img.SetPixel(i, j, TerrainColor(map, (i + 0.5) * step, (j + 0.5) * step, step));
        var texture = ImageTexture.CreateFromImage(img);
        var scale = new Vector2((float)step, (float)step);
        terrain.Texture = texture;
        terrain.Scale = scale;
        int k = 0;
        foreach (var offset in CopyOffsets((float)map.Width))
        {
            var copy = terrainCopies[k++];
            copy.Texture = texture;
            copy.Scale = scale;
            copy.Position = offset;
        }
    }

    Color TerrainColor(WorldMap map, double x, double y, double step)
    {
        double ground = map.GroundLevel(x, y);
        double e = Math.Clamp(ground / map.TileSize / sim!.Config.World.ElevationFactor, 0, 1);
        e = Math.Max(0, -0.1 + e * 1.1); // el mismo ajuste del original, para más arena
        double fac = e * (GroundColors.Length - 1 - 0.001);
        int k = (int)fac;
        var c = GroundColors[k].Lerp(GroundColors[k + 1], (float)(fac - k));

        // Sombreado por pendiente (luz desde arriba a la izquierda) para que se lea el relieve.
        double dx = map.GroundLevel(x + step, y) - map.GroundLevel(x - step, y);
        double dy = map.GroundLevel(x, y + step) - map.GroundLevel(x, y - step);
        float shade = (float)Math.Clamp(1 - (dx + dy) / (4 * step) * 0.6, 0.75, 1.25);
        c = new Color(c.R * shade, c.G * shade, c.B * shade);

        double water = map.WaterLevelPx;
        if (ground < water)
        {
            float prog = (float)Math.Clamp(1 + (ground - water) / 100, 0, 1);
            c = WaterColor.Lerp(c.Darkened(0.25f), prog);
        }
        return Mute(c);
    }

    /// <summary>
    /// Apaga y desatura el terreno: es el fondo. Los datos son los colores de los seres
    /// (sus genes), y tienen que resaltar.
    /// </summary>
    static Color Mute(Color c)
    {
        float luma = 0.3f * c.R + 0.59f * c.G + 0.11f * c.B;
        var gray = new Color(luma, luma, luma);
        var m = gray.Lerp(c, TerrainSaturation);
        return new Color(m.R * TerrainBrightness, m.G * TerrainBrightness, m.B * TerrainBrightness);
    }

    static Color Rgb(int r, int g, int b) => new(r / 255f, g / 255f, b / 255f);

    /// <summary>Los dibujos de una especie: un MultiMesh para el relleno y otro para el borde (solo animales).</summary>
    sealed class SpeciesLayer
    {
        const float BorderScale = 1.35f;
        readonly MultiMesh fill;
        readonly MultiMesh? border;
        // Nodos que dibujan los MultiMesh: el principal y sus copias alrededor (comparten los datos).
        readonly List<MultiMeshInstance2D> nodes = [];
        readonly Color borderColor;
        int capacity;
        int count;

        public SpeciesLayer(Node parent, SpeciesDef sp, Color speciesColor, IEnumerable<Vector2> copyOffsets)
            : this(parent, sp.Index, sp.IsPlant ? Meshes.Circle : Meshes.Arrow, withBorder: !sp.IsPlant, speciesColor, copyOffsets)
        {
        }

        /// <summary><paramref name="species"/> = -1 para una capa que no es de una especie (frutos y abrojos).</summary>
        public SpeciesLayer(Node parent, int species, Mesh mesh, bool withBorder, Color borderColor, IEnumerable<Vector2> copyOffsets)
        {
            Species = species;
            this.borderColor = borderColor;
            if (withBorder) border = NewMultiMesh(mesh);
            fill = NewMultiMesh(mesh);

            var offsets = copyOffsets.ToList();
            foreach (var multimesh in new[] { border, fill })
            {
                if (multimesh == null) continue;
                foreach (var offset in offsets)
                    AddNode(parent, new MultiMeshInstance2D { Multimesh = multimesh, Position = offset, Modulate = CopyModulate });
                AddNode(parent, new MultiMeshInstance2D { Multimesh = multimesh });
            }
        }

        void AddNode(Node parent, MultiMeshInstance2D node)
        {
            parent.AddChild(node);
            nodes.Add(node);
        }

        public int Species { get; }

        static MultiMesh NewMultiMesh(Mesh mesh) => new()
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform2D,
            UseColors = true,
            Mesh = mesh,
        };

        public void Begin(int expected)
        {
            if (expected > capacity)
            {
                capacity = Math.Max(64, (int)BitOperations.RoundUpToPowerOf2((uint)expected));
                fill.InstanceCount = capacity;
                if (border != null) border.InstanceCount = capacity;
            }
            count = 0;
        }

        public void Add(Vector2 pos, float rotation, float scale, Color color)
        {
            if (count >= capacity) return;
            fill.SetInstanceTransform2D(count, new Transform2D(rotation, new Vector2(scale, scale), 0, pos));
            fill.SetInstanceColor(count, color);
            if (border != null)
            {
                float b = scale * BorderScale;
                border.SetInstanceTransform2D(count, new Transform2D(rotation, new Vector2(b, b), 0, pos));
                border.SetInstanceColor(count, borderColor);
            }
            count++;
        }

        public void End()
        {
            fill.VisibleInstanceCount = count;
            if (border != null) border.VisibleInstanceCount = count;
        }

        public void Free()
        {
            foreach (var n in nodes) n.QueueFree();
        }
    }

    /// <summary>Formas de radio 1: círculo (plantas) y punta de flecha mirando a +X (animales).</summary>
    static class Meshes
    {
        public static readonly ArrayMesh Circle = Build(CircleFan(16));

        public static readonly ArrayMesh Arrow = Build(
        [
            new(1f, 0f), new(-0.7f, 0.65f), new(-0.35f, 0f),
            new(1f, 0f), new(-0.35f, 0f), new(-0.7f, -0.65f),
        ]);

        static Vector2[] CircleFan(int segments)
        {
            var v = new Vector2[segments * 3];
            for (int i = 0; i < segments; i++)
            {
                float a0 = Mathf.Tau * i / segments, a1 = Mathf.Tau * (i + 1) / segments;
                v[i * 3] = Vector2.Zero;
                v[i * 3 + 1] = new Vector2(Mathf.Cos(a0), Mathf.Sin(a0));
                v[i * 3 + 2] = new Vector2(Mathf.Cos(a1), Mathf.Sin(a1));
            }
            return v;
        }

        static ArrayMesh Build(Vector2[] triangles)
        {
            var arrays = new Godot.Collections.Array();
            arrays.Resize((int)Mesh.ArrayType.Max);
            arrays[(int)Mesh.ArrayType.Vertex] = triangles;
            var mesh = new ArrayMesh();
            mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
            return mesh;
        }
    }
}
