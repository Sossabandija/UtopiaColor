using System.Diagnostics;
using System.Globalization;
using Godot;
using UtopiaSim;

namespace UtopiaViewer;

/// <summary>
/// Arma la pantalla, carga la configuración y hace avanzar la simulación a la velocidad elegida.
///
/// Opciones de línea de comandos (después de "--"), útiles para pruebas y capturas:
///   --config &lt;archivo.json&gt;   --seed &lt;n&gt;   --advance-days &lt;d&gt;   --select &lt;especie&gt;
///   --gene &lt;nombre&gt;   --speed &lt;0-5&gt;   --zoom &lt;z&gt; (y sigue al seleccionado)
///   --screenshot &lt;ruta.png&gt; (guarda una captura y cierra)
/// </summary>
public partial class Main : Node
{
    const float PanelWidth = 440;
    /// <summary>1× = 30 ticks por segundo, el ritmo del original (30 fps).</summary>
    const double BaseTicksPerSecond = 30;
    /// <summary>Tiempo máximo por cuadro para simular, para que la ventana no se trabe.</summary>
    const double FrameBudgetSeconds = 0.014;
    static readonly int[] Speeds = [0, 1, 10, 30, 100, -1];
    static readonly string[] SpeedLabels = ["Pausa", "1×", "10×", "30×", "100×", "Máx"];

    const string HelpText =
        "Arrastrar: mover · Rueda: zoom · Clic: ver un ser · F: seguirlo · Esc: soltarlo\n" +
        "Espacio: pausa · Flechas arriba/abajo: velocidad · R: encuadrar · N: noche · G: otro gen";

    Simulation? sim;
    readonly PopulationHistory history = new();
    WorldView world = null!;
    PopulationChart chart = null!;
    GeneView geneView = null!;
    InspectorView inspector = null!;
    PanelContainer inspectorPanel = null!;
    OptionButton configPicker = null!;
    OptionButton genePicker = null!;
    SpinBox seedBox = null!;
    Label statusLabel = null!;
    Label errorLabel = null!;
    Label geneInfo = null!;
    Button followButton = null!;
    readonly List<Button> speedButtons = [];

    int speedIndex = 2;
    int lastRunningSpeed = 2;
    double pendingTicks;
    double uiTimer;
    double tpsTimer;
    long tpsTicks;
    double measuredTps;
    string configDir = "";
    readonly List<string> configFiles = [];

    string? argConfig;
    ulong? argSeed;
    double argAdvanceDays;
    string? argSelect;
    string? argGene;
    string? argScreenshot;
    float argZoom;
    int screenshotCountdown = -1;

    public override void _Ready()
    {
        ParseArgs();
        configDir = FindConfigDir();

        world = new WorldView { PanelWidth = PanelWidth };
        AddChild(world);
        world.SelectionChanged += OnSelectionChanged;

        BuildUi();
        LoadConfigList();
        StartSimulation();

        if (sim != null && argAdvanceDays > 0)
        {
            long target = (long)(argAdvanceDays * sim.Config.World.TicksPerDay);
            while (sim.Tick < target) StepOnce();
        }
        if (sim != null && argSelect != null)
            world.Select(sim.Creatures.FirstOrDefault(c => c.Def.Name == argSelect));
        if (argZoom > 0)
        {
            world.Camera.Zoom = new Vector2(argZoom, argZoom);
            world.Follow = world.Selected != null;
        }
        if (sim != null && argGene != null)
        {
            int g = sim.Genes.ToList().FindIndex(x => x.Name == argGene);
            if (g >= 0)
            {
                genePicker.Select(g);
                OnGeneSelected(g);
            }
        }
        if (argScreenshot != null) screenshotCountdown = 90; // ~1,5 s: alcanza a medir ticks/s
    }

    public override void _Process(double delta)
    {
        if (sim != null)
        {
            RunTicks(delta);
            world.Sync();
        }

        tpsTimer += delta;
        if (tpsTimer >= 1)
        {
            measuredTps = tpsTicks / tpsTimer;
            tpsTicks = 0;
            tpsTimer = 0;
        }
        uiTimer += delta;
        if (uiTimer >= 0.2 || screenshotCountdown == 5)
        {
            uiTimer = 0;
            UpdateUi();
        }
        if (screenshotCountdown > 0 && --screenshotCountdown == 0) SaveScreenshotAndQuit();
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (e is not InputEventKey { Pressed: true, Echo: false } key) return;
        switch (key.Keycode)
        {
            case Key.Space:
                SetSpeed(speedIndex == 0 ? lastRunningSpeed : 0);
                break;
            case Key.Up:
                SetSpeed(Math.Min(speedIndex + 1, Speeds.Length - 1));
                break;
            case Key.Down:
                SetSpeed(Math.Max(speedIndex - 1, 0));
                break;
            case Key.F:
                world.Follow = !world.Follow && world.Selected is { Dead: false };
                break;
            case Key.Escape:
                world.Select(null);
                break;
            case Key.R:
                world.FitView();
                break;
            case Key.N:
                world.NightTint = !world.NightTint;
                break;
            case Key.G when genePicker.ItemCount > 0:
                int next = (genePicker.Selected + 1) % genePicker.ItemCount;
                genePicker.Select(next);
                OnGeneSelected(next);
                break;
            default:
                return;
        }
        GetViewport().SetInputAsHandled();
    }

    // ------------------------------------------------------------------ simulación

    void StartSimulation()
    {
        ShowError(null);
        if (configFiles.Count == 0)
        {
            ShowError($"No encontré archivos .json en {configDir}");
            return;
        }
        string path = configFiles[Math.Max(0, configPicker.Selected)];
        try
        {
            sim = new Simulation(SimConfig.Load(path), (ulong)seedBox.Value);
        }
        catch (ConfigException e)
        {
            ShowError(e.Message);
            return;
        }

        history.Reset(sim);
        pendingTicks = 0;
        world.SetSimulation(sim);
        chart.SetData(sim, history);
        geneView.SetSimulation(sim);
        genePicker.Clear();
        foreach (var g in sim.Genes) genePicker.AddItem($"{g.Name} ({ShapeName(g)})");
        if (sim.Genes.Count > 0)
        {
            genePicker.Select(0);
            OnGeneSelected(0);
        }
        UpdateUi();
    }

    void StepOnce()
    {
        sim!.Step();
        history.Observe(sim);
        tpsTicks++;
    }

    void RunTicks(double delta)
    {
        int speed = Speeds[speedIndex];
        if (speed == 0) return;
        var clock = Stopwatch.StartNew();
        if (speed < 0)
        {
            while (clock.Elapsed.TotalSeconds < FrameBudgetSeconds) StepOnce();
            return;
        }
        pendingTicks += speed * BaseTicksPerSecond * delta;
        while (pendingTicks >= 1)
        {
            StepOnce();
            pendingTicks -= 1;
            if (clock.Elapsed.TotalSeconds > FrameBudgetSeconds)
            {
                pendingTicks = 0; // no alcanza: se va más lento en vez de trabarse
                break;
            }
        }
    }

    void SetSpeed(int index)
    {
        speedIndex = index;
        if (index != 0) lastRunningSpeed = index;
        pendingTicks = 0;
        for (int i = 0; i < speedButtons.Count; i++) speedButtons[i].SetPressedNoSignal(i == index);
    }

    // ------------------------------------------------------------------ interfaz

    void BuildUi()
    {
        var layer = new CanvasLayer();
        AddChild(layer);

        // Panel derecho.
        var panel = new PanelContainer();
        panel.AddThemeStyleboxOverride("panel", PanelStyle());
        layer.AddChild(panel);
        panel.SetAnchorsPreset(Control.LayoutPreset.RightWide);
        panel.OffsetLeft = -PanelWidth;
        panel.OffsetRight = 0;
        panel.OffsetTop = 0;
        panel.OffsetBottom = 0;

        var scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        panel.AddChild(scroll);
        var column = new VBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
        };
        column.AddThemeConstantOverride("separation", 6);
        scroll.AddChild(column);

        column.AddChild(MakeLabel("UtopiaColor", 22, Colors.White));

        var configRow = new HBoxContainer();
        column.AddChild(configRow);
        configPicker = new OptionButton
        {
            FocusMode = Control.FocusModeEnum.None,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            TooltipText = "Archivos de configuración en sim/configs",
        };
        configPicker.ItemSelected += _ => StartSimulation();
        configRow.AddChild(configPicker);
        configRow.AddChild(MakeLabel("semilla", 14, Palette.TextDim));
        seedBox = new SpinBox { MinValue = 0, MaxValue = 999_999_999, Step = 1, Rounded = true, Value = 1, CustomMinimumSize = new Vector2(100, 0) };
        configRow.AddChild(seedBox);
        var restart = new Button { Text = "Reiniciar", FocusMode = Control.FocusModeEnum.None, TooltipText = "Vuelve a leer el JSON y parte de cero" };
        restart.Pressed += StartSimulation;
        configRow.AddChild(restart);

        errorLabel = MakeLabel("", 13, Palette.Danger);
        errorLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        column.AddChild(errorLabel);

        var speedRow = new HBoxContainer();
        column.AddChild(speedRow);
        var group = new ButtonGroup();
        for (int i = 0; i < Speeds.Length; i++)
        {
            int index = i;
            var b = new Button
            {
                Text = SpeedLabels[i],
                ToggleMode = true,
                ButtonGroup = group,
                FocusMode = Control.FocusModeEnum.None,
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            };
            b.Pressed += () => SetSpeed(index);
            speedRow.AddChild(b);
            speedButtons.Add(b);
        }
        statusLabel = MakeLabel("", 13, Palette.TextDim);
        column.AddChild(statusLabel);

        var popHeader = new HBoxContainer();
        column.AddChild(popHeader);
        var popTitle = MakeLabel("Población", 16, Colors.White);
        popTitle.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        popHeader.AddChild(popTitle);
        var logToggle = new CheckBox { Text = "escala log", ButtonPressed = true, FocusMode = Control.FocusModeEnum.None };
        logToggle.Toggled += on =>
        {
            chart.LogScale = on;
            chart.QueueRedraw();
        };
        popHeader.AddChild(logToggle);
        chart = new PopulationChart { CustomMinimumSize = new Vector2(0, 170) };
        column.AddChild(chart);

        var geneHeader = new HBoxContainer();
        column.AddChild(geneHeader);
        var geneTitle = MakeLabel("Genes", 16, Colors.White);
        geneTitle.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        geneHeader.AddChild(geneTitle);
        genePicker = new OptionButton { FocusMode = Control.FocusModeEnum.None };
        genePicker.ItemSelected += i => OnGeneSelected((int)i);
        geneHeader.AddChild(genePicker);
        geneInfo = MakeLabel("", 12, Palette.TextDim);
        geneInfo.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        geneInfo.CustomMinimumSize = new Vector2(0, 32);
        column.AddChild(geneInfo);
        // La vista de genes ocupa todo el alto que sobre en el panel.
        geneView = new GeneView { CustomMinimumSize = new Vector2(0, 250), SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        column.AddChild(geneView);

        // Ayuda, arriba a la izquierda.
        var help = MakeLabel(HelpText, 13, new Color(1, 1, 1, 0.85f));
        help.Position = new Vector2(12, 8);
        help.MouseFilter = Control.MouseFilterEnum.Ignore;
        help.AddThemeConstantOverride("outline_size", 4);
        help.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 0.8f));
        layer.AddChild(help);

        // Ficha del ser seleccionado, abajo a la izquierda.
        inspectorPanel = new PanelContainer { Visible = false };
        inspectorPanel.AddThemeStyleboxOverride("panel", PanelStyle());
        layer.AddChild(inspectorPanel);
        inspectorPanel.SetAnchorsPreset(Control.LayoutPreset.BottomLeft);
        inspectorPanel.GrowVertical = Control.GrowDirection.Begin;
        inspectorPanel.OffsetLeft = 12;
        inspectorPanel.OffsetRight = 12;
        inspectorPanel.OffsetTop = -12;
        inspectorPanel.OffsetBottom = -12;
        var inspectorColumn = new VBoxContainer();
        inspectorPanel.AddChild(inspectorColumn);
        inspector = new InspectorView();
        inspectorColumn.AddChild(inspector);
        var buttons = new HBoxContainer();
        inspectorColumn.AddChild(buttons);
        followButton = new Button { Text = "Seguir (F)", ToggleMode = true, FocusMode = Control.FocusModeEnum.None };
        followButton.Toggled += on => world.Follow = on && world.Selected is { Dead: false };
        buttons.AddChild(followButton);
        var close = new Button { Text = "Cerrar (Esc)", FocusMode = Control.FocusModeEnum.None };
        close.Pressed += () => world.Select(null);
        buttons.AddChild(close);

        SetSpeed(speedIndex);
    }

    void UpdateUi()
    {
        if (sim == null) return;
        double day = sim.Day;
        double hours = day % 1 * 24;
        statusLabel.Text =
            $"Día {(int)day + 1}, {(int)hours:00}:{(int)(hours % 1 * 60):00}  ·  {SpeedLabels[speedIndex]}  ·  " +
            $"{measuredTps / 1000:0.0}k ticks/s  ·  {Engine.GetFramesPerSecond():0} fps  ·  {sim.Creatures.Count} seres";
        chart.QueueRedraw();
        geneView.Selected = world.Selected;
        geneView.QueueRedraw();
        followButton.SetPressedNoSignal(world.Follow);
        if (inspectorPanel.Visible) inspector.SetCreature(sim, world.Selected);
    }

    void OnSelectionChanged(Creature? c)
    {
        inspectorPanel.Visible = c != null;
        inspector.SetCreature(sim, c);
        geneView.Selected = c;
        geneView.QueueRedraw();
    }

    void OnGeneSelected(int index)
    {
        if (sim == null || index < 0 || index >= sim.Genes.Count) return;
        geneView.SetGene(index);
        var g = sim.Genes[index];
        var mutating = sim.Species.Where(sp => sp.Mutation[g.Index] != null).Select(sp => sp.Name).ToList();
        string rule = g.Config.Boundary switch
        {
            BoundaryRule.Reject => "borde que rechaza (empuja hacia adentro)",
            BoundaryRule.Reflect => "borde que rebota (neutral)",
            _ => "borde pegajoso (atrapa en las esquinas)",
        };
        geneInfo.Text = $"{g.Name}: {ShapeName(g)}, {rule}. " +
                        (mutating.Count > 0 ? $"Muta en: {string.Join(", ", mutating)}." : "No muta en ninguna especie.");
    }

    void ShowError(string? message)
    {
        errorLabel.Text = message ?? "";
        errorLabel.Visible = !string.IsNullOrEmpty(message);
    }

    static string ShapeName(GeneDef g) => g.Space switch
    {
        DiskSpace => "rueda",
        PolygonSpace { Sides: 2 } => "línea",
        PolygonSpace { Sides: 3 } => "triángulo",
        PolygonSpace { Sides: 4 } => "cuadrado",
        PolygonSpace p => $"{p.Sides} lados",
        _ => "?",
    };

    static StyleBoxFlat PanelStyle() => new()
    {
        BgColor = Palette.PanelBg,
        ContentMarginLeft = 12,
        ContentMarginRight = 12,
        ContentMarginTop = 10,
        ContentMarginBottom = 10,
    };

    static Label MakeLabel(string text, int size, Color color)
    {
        var label = new Label { Text = text };
        label.AddThemeFontSizeOverride("font_size", size);
        label.AddThemeColorOverride("font_color", color);
        return label;
    }

    // ------------------------------------------------------------------ archivos y línea de comandos

    /// <summary>Busca sim/configs junto al proyecto (o una carpeta configs junto al ejecutable exportado).</summary>
    static string FindConfigDir()
    {
        string res = ProjectSettings.GlobalizePath("res://");
        string[] candidates =
        [
            Path.Combine(res, "..", "sim", "configs"),
            Path.Combine(Path.GetDirectoryName(OS.GetExecutablePath()) ?? ".", "configs"),
        ];
        foreach (var c in candidates)
            if (Directory.Exists(c)) return Path.GetFullPath(c);
        return Path.GetFullPath(candidates[0]);
    }

    void LoadConfigList()
    {
        configFiles.Clear();
        configPicker.Clear();
        if (Directory.Exists(configDir)) configFiles.AddRange(Directory.GetFiles(configDir, "*.json").OrderBy(f => f));

        string? wanted = null;
        if (argConfig != null)
        {
            wanted = File.Exists(argConfig) ? Path.GetFullPath(argConfig) : Path.GetFullPath(Path.Combine(configDir, argConfig));
            if (!configFiles.Contains(wanted) && File.Exists(wanted)) configFiles.Add(wanted);
        }
        foreach (var f in configFiles) configPicker.AddItem(Path.GetFileNameWithoutExtension(f));

        int index = wanted != null ? configFiles.IndexOf(wanted) : -1;
        if (index < 0) index = configFiles.FindIndex(f => Path.GetFileNameWithoutExtension(f) == "original");
        if (configFiles.Count > 0) configPicker.Select(Math.Max(0, index));
        seedBox.Value = argSeed ?? 1;
    }

    void ParseArgs()
    {
        var args = OS.GetCmdlineUserArgs();
        for (int i = 0; i + 1 < args.Length; i++)
        {
            string value = args[i + 1];
            switch (args[i])
            {
                case "--config": argConfig = value; i++; break;
                case "--seed": argSeed = ulong.Parse(value, CultureInfo.InvariantCulture); i++; break;
                case "--advance-days": argAdvanceDays = double.Parse(value, CultureInfo.InvariantCulture); i++; break;
                case "--select": argSelect = value; i++; break;
                case "--gene": argGene = value; i++; break;
                case "--speed": speedIndex = lastRunningSpeed = Math.Clamp(int.Parse(value, CultureInfo.InvariantCulture), 0, Speeds.Length - 1); i++; break;
                case "--screenshot": argScreenshot = value; i++; break;
                case "--zoom": argZoom = float.Parse(value, CultureInfo.InvariantCulture); i++; break;
            }
        }
    }

    void SaveScreenshotAndQuit()
    {
        var image = GetViewport().GetTexture().GetImage();
        var error = image.SavePng(argScreenshot!);
        GD.Print(error == Error.Ok ? $"Captura guardada en {argScreenshot}" : $"No pude guardar la captura: {error}");
        GetTree().Quit();
    }
}
