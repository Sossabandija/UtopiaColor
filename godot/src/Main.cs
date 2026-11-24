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
///   --gene &lt;nombre&gt;   --speed &lt;0-5&gt;   --zoom &lt;z&gt; (y sigue al seleccionado)   --chart &lt;n&gt;
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
        "Espacio: pausa · Flechas arriba/abajo: velocidad · R: encuadrar · N: noche · C: otro gráfico · G: otro gen";

    Simulation? sim;
    readonly PopulationHistory history = new();
    readonly MetricsHistory metricsHistory = new();
    readonly List<(string Label, Action Refresh)> chartDefs = [];
    WorldView world = null!;
    TimeSeriesChart chart = null!;
    OptionButton chartPicker = null!;
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
    int argChart = -1;
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
        if (argChart >= 0 && argChart < chartPicker.ItemCount)
        {
            chartPicker.Select(argChart);
            RefreshChart();
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
            case Key.C when chartPicker.ItemCount > 0:
                chartPicker.Select((chartPicker.Selected + 1) % chartPicker.ItemCount);
                RefreshChart();
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
        metricsHistory.Reset(sim);
        pendingTicks = 0;
        world.SetSimulation(sim);
        BuildChartList(sim);
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
        metricsHistory.Observe(sim);
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

        var chartHeader = new HBoxContainer();
        column.AddChild(chartHeader);
        chartPicker = new OptionButton
        {
            FocusMode = Control.FocusModeEnum.None,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            ClipText = true,
            TooltipText = "Qué gráfico mostrar (tecla C para pasar al siguiente)",
        };
        chartPicker.ItemSelected += _ => RefreshChart();
        chartHeader.AddChild(chartPicker);
        var logToggle = new CheckBox { Text = "escala log", ButtonPressed = true, FocusMode = Control.FocusModeEnum.None };
        logToggle.Toggled += on =>
        {
            chart.LogScale = on;
            RefreshChart();
        };
        chartHeader.AddChild(logToggle);
        chart = new TimeSeriesChart { CustomMinimumSize = new Vector2(0, 170) };
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
        RefreshChart();
        geneView.Selected = world.Selected;
        geneView.QueueRedraw();
        followButton.SetPressedNoSignal(world.Follow);
        if (inspectorPanel.Visible) inspector.SetCreature(sim, world.Selected);
    }

    // ------------------------------------------------------------------ gráficos

    static readonly Color BirthColor = new(0.92f, 0.92f, 0.92f);
    static readonly Dictionary<DeathCause, (string Name, Color Color)> DeathStyle = new()
    {
        [DeathCause.Starved] = ("de hambre", new Color(0.85f, 0.6f, 0.3f)),
        [DeathCause.Dehydrated] = ("de sed", new Color(0.35f, 0.6f, 1f)),
        [DeathCause.Eaten] = ("comidos", new Color(1f, 0.3f, 0.3f)),
        [DeathCause.Crowded] = ("sin espacio", new Color(0.55f, 0.55f, 0.6f)),
        [DeathCause.Seedling] = ("como brote", new Color(0.55f, 0.85f, 0.35f)),
    };

    /// <summary>Arma la lista de gráficos disponibles para esta simulación (depende de sus especies y genes).</summary>
    void BuildChartList(Simulation s)
    {
        int previous = chartPicker.Selected;
        chartDefs.Clear();

        chartDefs.Add(("Población", () => chart.SetData(history.Days, s.Species.Select(sp =>
            new ChartSeries(PopulationLegend(s, sp), Palette.ForSpecies(sp.Index), history.Counts[sp.Index])).ToList())));

        foreach (var sp in s.Species.Where(sp => s.PreySpeciesOf(sp.Index).Count > 0))
        {
            string n = sp.Name;
            chartDefs.Add(($"Comida a la vista: {n}", () => chart.SetData(metricsHistory.Days,
            [
                Curve("ve", Palette.TextDim, metricsHistory.Get($"{n}.food.visible")),
                Curve("tamaño justo", new Color(1f, 0.85f, 0.3f), metricsHistory.Get($"{n}.food.rightStage")),
                Curve("comestibles", new Color(0.35f, 1f, 0.45f), metricsHistory.Get($"{n}.food.edible")),
            ])));
        }

        foreach (var sp in s.Species)
        {
            var target = sp;
            chartDefs.Add(($"Nacimientos y muertes: {sp.Name}", () =>
            {
                var curves = new List<ChartSeries> { Curve("nacimientos", BirthColor, metricsHistory.Get($"{target.Name}.births")) };
                foreach (var (cause, style) in DeathStyle)
                {
                    var values = metricsHistory.Get(MetricsHistory.DeathKey(target, cause));
                    if (values.Any(v => v > 0)) curves.Add(Curve(style.Name, style.Color, values));
                }
                var failed = metricsHistory.Get($"{target.Name}.huntsFailed");
                if (failed.Any(v => v > 0)) curves.Add(Curve("cazas fallidas", new Color(0.75f, 0.55f, 1f), failed));
                chart.SetData(metricsHistory.Days, curves);
            }));
        }

        var stageEaters = s.Species.Where(sp => sp.StageGeneIndex >= 0).ToList();
        if (stageEaters.Count > 0)
        {
            chartDefs.Add(("Etapa preferida por especie", () =>
            {
                var curves = stageEaters
                    .Select(sp => Curve(sp.Name, Palette.ForSpecies(sp.Index), metricsHistory.Get($"{sp.Name}.stage")))
                    .ToList();
                // Referencia: desde qué etapa un herbívoro (del mismo color) alcanza a las plantas que ya soltaron semilla.
                var plant = s.Species.FirstOrDefault(sp => sp.IsPlant && stageEaters.Any(e => s.PreySpeciesOf(e.Index).Contains(sp.Index)));
                if (plant != null)
                {
                    double reach = plant.Plant.SeedAtSize - plant.Plant.SizeLossOnSeed - s.Config.Predation.HalfWidth;
                    curves.Add(Curve("alcanza adultas", new Color(0.6f, 0.6f, 0.6f), metricsHistory.Days.Select(_ => reach).ToList()));
                }
                chart.SetData(metricsHistory.Days, curves, max: 1, logAllowed: false);
            }));
        }

        var withRefuge = s.Species.Where(sp => s.HasRefuge(sp.Index)).ToList();
        if (withRefuge.Count > 0)
        {
            // Con madrigueras se agregan qué parte está dentro de una y qué parte del cupo está reclamada (dentro o en camino).
            // Con cobertura, qué parte está escondida en una planta (no cuenta como "a salvo": ahí se la puede cazar de cerca).
            chartDefs.Add(("Presas en su refugio (%)", () => chart.SetData(metricsHistory.Days,
                withRefuge.SelectMany(sp =>
                {
                    var color = Palette.ForSpecies(sp.Index);
                    var curves = new List<ChartSeries> { Curve(s.HasBurrows(sp.Index) || s.HasCover(sp.Index) ? $"{sp.Name} a salvo" : sp.Name, color, metricsHistory.Get($"{sp.Name}.inRefuge")) };
                    if (s.HasBurrows(sp.Index))
                    {
                        curves.Add(Curve($"{sp.Name} en madriguera", color.Darkened(0.35f), metricsHistory.Get($"{sp.Name}.inBurrow")));
                        curves.Add(Curve("cupo usado", Palette.BurrowRim, metricsHistory.Get($"{sp.Name}.burrowUse")));
                    }
                    if (s.HasCover(sp.Index))
                        curves.Add(Curve($"{sp.Name} en plantas", new Color(0.45f, 0.80f, 0.45f), metricsHistory.Get($"{sp.Name}.inCover")));
                    return curves;
                }).ToList(),
                max: 100, logAllowed: false)));
        }

        var dietEaters = s.Species.Where(sp => sp.DietGeneIndex >= 0).ToList();
        if (dietEaters.Count > 0)
        {
            chartDefs.Add(("Dieta por especie (0 folívoro, 1 frugívoro)", () => chart.SetData(metricsHistory.Days,
                dietEaters.Select(sp => Curve(sp.Name, Palette.ForSpecies(sp.Index), metricsHistory.Get($"{sp.Name}.diet"))).ToList(),
                max: 1, logAllowed: false)));
        }

        // Genes de 3 o más esquinas (por ejemplo la dispersión): el peso de cada esquina en el tiempo.
        foreach (var gene in s.Genes.Where(g => g.Space is PolygonSpace { Sides: >= 3 }))
        {
            int sides = ((PolygonSpace)gene.Space).Sides;
            foreach (var sp in s.Species.Where(sp => sp.Mutation[gene.Index] != null))
            {
                var (g, target) = (gene, sp);
                chartDefs.Add(($"Gen {g.Name}: {sp.Name}", () => chart.SetData(metricsHistory.Days,
                    Enumerable.Range(0, sides).Select(i => Curve(
                        g.Config.VertexNames?[i] ?? $"esquina {i + 1}",
                        i < Palette.Dispersal.Length ? Palette.Dispersal[i] : Palette.ForSpecies(i),
                        metricsHistory.Get(MetricsHistory.CornerKey(target, g, i)))).ToList(),
                    max: 1, logAllowed: false)));
            }
        }

        foreach (var sp in s.Species.Where(sp => sp.IsPlant && sp.Plant.Dispersal != null))
        {
            var target = sp;
            chartDefs.Add(($"Semillas por modo: {sp.Name}", () =>
            {
                var curves = new List<ChartSeries>();
                void Add(string name, Color color, DispersalCounter k)
                {
                    var values = metricsHistory.Get(MetricsHistory.DispersalKey(target, k));
                    if (values.Any(v => v > 0)) curves.Add(Curve(name, color, values));
                }
                Add("cayeron de la madre", Palette.TextDim, DispersalCounter.SeedsParent);
                Add("viento", Palette.Dispersal[1], DispersalCounter.SeedsWind);
                Add("salieron de un estómago", Palette.Dispersal[0], DispersalCounter.SeedsGut);
                Add("cayeron de un abrojo", Palette.Dispersal[2], DispersalCounter.SeedsBurr);
                Add("destruidas en estómagos", Palette.Danger, DispersalCounter.SeedsDigested);
                chart.SetData(metricsHistory.Days, curves);
            }));
        }

        if (Palette.ColorGene(s) is { } colorGene)
        {
            var mutating = s.Species.Where(sp => sp.Mutation[colorGene.Index] != null).ToList();
            chartDefs.Add(("Tono de color por especie", () => chart.SetData(metricsHistory.Days,
                mutating.Select(sp => Curve(sp.Name, Palette.ForSpecies(sp.Index), metricsHistory.Get($"{sp.Name}.hue"), wraps: true)).ToList(),
                max: 1, logAllowed: false)));
            chartDefs.Add(("Distancia de color a la comida", () => chart.SetData(metricsHistory.Days,
                s.Species.Where(sp => s.PreySpeciesOf(sp.Index).Count > 0)
                    .Select(sp => Curve(sp.Name, Palette.ForSpecies(sp.Index), metricsHistory.Get($"{sp.Name}.food.colorDistance"))).ToList(),
                logAllowed: false)));

            // Genética diploide: qué tan lejos del color de la especie están, en promedio, lo expresado y los alelos ocultos.
            var diploid = s.Species.Where(sp => sp.Config.Diploid != null).ToList();
            if (diploid.Count > 0)
            {
                chartDefs.Add(("Variación de color: expresada y oculta", () => chart.SetData(metricsHistory.Days,
                    diploid.SelectMany(sp => new[]
                    {
                        Curve($"{sp.Name} expresada", Palette.ForSpecies(sp.Index), metricsHistory.Get($"{sp.Name}.colorSpread")),
                        Curve($"{sp.Name} oculta", Palette.ForSpecies(sp.Index).Darkened(0.45f), metricsHistory.Get($"{sp.Name}.colorHidden")),
                    }).ToList(),
                    logAllowed: false)));
            }
        }

        chartPicker.Clear();
        foreach (var (label, _) in chartDefs) chartPicker.AddItem(label);
        chartPicker.Select(previous >= 0 && previous < chartDefs.Count ? previous : 0);
        RefreshChart();
    }

    void RefreshChart()
    {
        int i = chartPicker.Selected;
        if (i >= 0 && i < chartDefs.Count) chartDefs[i].Refresh();
    }

    string PopulationLegend(Simulation s, SpeciesDef sp) => history.ExtinctDay[sp.Index] is double d
        ? $"{sp.Name}: extinta (día {d + 1:0.0})"
        : $"{sp.Name}: {s.Population(sp.Index)}";

    /// <summary>Una curva cuya leyenda muestra el último valor.</summary>
    static ChartSeries Curve(string name, Color color, IReadOnlyList<double> values, bool wraps = false)
    {
        double last = values.Count > 0 ? values[^1] : double.NaN;
        string legend = double.IsFinite(last) ? $"{name}: {last:0.##}" : name;
        return new ChartSeries(legend, color, values, wraps);
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
        var diploid = sim.Species.Where(sp => sp.Config.Diploid != null).Select(sp => sp.Name).ToList();
        geneInfo.Text = $"{g.Name}: {ShapeName(g)}, {rule}. " +
                        (mutating.Count > 0 ? $"Muta en: {string.Join(", ", mutating)}." : "No muta en ninguna especie.") +
                        (diploid.Count > 0 ? $" Puntos tenues: alelos ocultos ({string.Join(", ", diploid)})." : "");
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
                case "--chart": argChart = int.Parse(value, CultureInfo.InvariantCulture); i++; break;
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
