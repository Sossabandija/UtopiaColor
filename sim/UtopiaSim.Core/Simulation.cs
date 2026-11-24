namespace UtopiaSim;

/// <summary>
/// La simulación completa. No sabe nada de gráficos: avanza con <see cref="Step"/> y expone su estado
/// para que un visor (Godot) o la consola lo lean.
///
/// Orden de un tick (igual que el original):
///   1. Todos los animales deciden (necesidades → buscar objetivo → "apretar teclas" → acelerar).
///   2. Todos se mueven; las plantas crecen y sueltan semillas; se come, se bebe y se aparea.
///   3. Se retiran los muertos.
/// </summary>
public sealed class Simulation
{
    // Umbrales de navegación del original (en fracciones de vuelta completa).
    const double TurnThreshold = 0.03;
    const double AimWindowNear = 0.3;
    const double AimWindowFar = 0.05;
    const double NearDistanceTiles = 3;
    const double StrafeChance = 0.5;
    const double StrafeMin = 0.05;
    const double StrafeMax = 0.45;
    const double FleeTurnThreshold = 0.1;
    const double FleeAimWindow = 0.25;
    const double MaxVzToJump = 1;
    const double SpawnHeightAboveGround = 0.1;

    readonly List<Creature> creatures = [];
    readonly Rng rng;
    readonly SpatialGrid grid;
    readonly int[,] plantsPerTile;
    readonly bool[,] canEatSpecies; // [comedor, comida]
    readonly int[][] foodSpecies;
    readonly int[][] predatorSpecies;
    readonly int[][] ownSpecies;
    readonly int[] alive;
    readonly List<int> cellBuffer = [];
    readonly double visionPx;
    int nextId;
    double daylight;

    public Simulation(SimConfig config, ulong? seed = null)
    {
        config.Validate();
        Config = config;
        Seed = seed ?? config.Seed;
        rng = new Rng(Seed);

        var genes = new GeneDef[config.Genes.Count];
        for (int i = 0; i < genes.Length; i++) genes[i] = new GeneDef(i, config.Genes[i]);
        Genes = genes;

        var species = new SpeciesDef[config.Species.Count];
        for (int i = 0; i < species.Length; i++) species[i] = new SpeciesDef(i, config.Species[i], genes);
        Species = species;

        MatchGene = string.IsNullOrEmpty(config.Predation.MatchGene)
            ? null
            : genes.First(g => g.Name == config.Predation.MatchGene);

        int s = species.Length;
        canEatSpecies = new bool[s, s];
        for (int a = 0; a < s; a++)
            foreach (var food in species[a].Config.Eats)
                canEatSpecies[a, Array.FindIndex(species, sp => sp.Name == food)] = true;
        foodSpecies = new int[s][];
        predatorSpecies = new int[s][];
        ownSpecies = new int[s][];
        for (int a = 0; a < s; a++)
        {
            foodSpecies[a] = Enumerable.Range(0, s).Where(b => canEatSpecies[a, b]).ToArray();
            predatorSpecies[a] = Enumerable.Range(0, s).Where(b => canEatSpecies[b, a]).ToArray();
            ownSpecies[a] = [a];
        }

        Map = new WorldMap(config.World, rng);
        grid = new SpatialGrid(Map.Width, Math.Max(1, config.World.Size / 2), s);
        plantsPerTile = new int[Map.Size, Map.Size];
        alive = new int[s];
        Stats = new SimStats(s);
        visionPx = config.Movement.VisionTiles * Map.TileSize;
        daylight = ComputeDaylight(0);

        SpawnInitialPopulation();
    }

    public SimConfig Config { get; }
    public ulong Seed { get; }
    public WorldMap Map { get; }
    public IReadOnlyList<GeneDef> Genes { get; }
    public IReadOnlyList<SpeciesDef> Species { get; }
    /// <summary>Gen cuya similitud decide qué tan fácil es comerse a alguien (el color, en el original).</summary>
    public GeneDef? MatchGene { get; }
    public SimStats Stats { get; }
    public long Tick { get; private set; }
    public double Day => (double)Tick / Config.World.TicksPerDay;
    /// <summary>0 = medianoche, 1 = mediodía.</summary>
    public double Daylight => daylight;
    /// <summary>Todos los seres vivos (después de cada <see cref="Step"/> no quedan muertos aquí).</summary>
    public IReadOnlyList<Creature> Creatures => creatures;

    /// <summary>Se dispara en cada nacimiento, muerte, comida, trago y apareamiento. Null = sin costo.</summary>
    public event Action<SimEvent>? EventRaised;

    public int Population(int species) => alive[species];

    public GeneDef? FindGene(string name) => Genes.FirstOrDefault(g => g.Name == name);

    public SpeciesDef? FindSpecies(string name) => Species.FirstOrDefault(s => s.Name == name);

    /// <summary>Avanza un tick.</summary>
    public void Step()
    {
        Tick++;
        daylight = ComputeDaylight(Tick);
        grid.Rebuild(creatures);

        int count = creatures.Count;
        for (int i = 0; i < count; i++)
        {
            var c = creatures[i];
            if (!c.Dead && !c.IsPlant) DoActions(c);
        }

        // Las crías que nacen en este bucle se agregan al final y también se mueven este tick (como el original).
        for (int i = 0; i < creatures.Count; i++)
        {
            var c = creatures[i];
            if (!c.Dead) DoPhysics(c);
        }

        RemoveDead();
    }

    /// <summary>
    /// ¿Puede <paramref name="eater"/> comerse a <paramref name="food"/> ahora mismo?
    /// Depende de la red trófica, de cuánto se parecen sus genes matchGene y de la etapa de la presa.
    /// </summary>
    public bool CanEat(Creature eater, Creature food)
    {
        if (!canEatSpecies[eater.Species, food.Species]) return false;
        var p = Config.Predation;
        double center = PreferredStage(eater);
        double stage = food.IsPlant ? food.PlantSize : food.Needs[Need.Food];
        if (stage < center - p.HalfWidth || stage > center + p.HalfWidth) return false; // fuera incluso de la ventana más ancha
        double dist = MatchGene == null
            ? 0
            : MatchGene.Space.Distance(eater.Genes[MatchGene.Index], food.Genes[MatchGene.Index]);
        double window = p.HalfWidth * Math.Pow(Math.Max(0, 1 - dist * p.DistanceFactor), p.Exponent);
        return stage >= center - window && stage <= center + window;
    }

    /// <summary>Etapa de presa que prefiere un comedor: su gen de etapa, o predation.center si no tiene.</summary>
    public double PreferredStage(Creature c)
    {
        int g = c.Def.StageGeneIndex;
        if (g < 0) return Config.Predation.Center;
        return ((PolygonSpace)Genes[g].Space).LinePosition(c.Genes[g]);
    }

    double ComputeDaylight(long tick) =>
        0.5 - 0.5 * Math.Cos((double)tick / Config.World.TicksPerDay * MathUtil.Tau);

    // ------------------------------------------------------------------ población

    void SpawnInitialPopulation()
    {
        foreach (var sp in Species)
        {
            var jitter = sp.Config.InitialJitter;
            for (int i = 0; i < sp.Config.InitialCount; i++)
            {
                double x = rng.Range(0, Map.Width), y = rng.Range(0, Map.Width), angle = rng.Angle();
                var genes = new Vec2[Genes.Count];
                for (int g = 0; g < Genes.Count; g++)
                    genes[g] = Genes[g].Space.Mutate(sp.InitialGenes[g], rng, jitter.MaxStep, jitter.Exponent);

                var c = CreateCreature(sp, x, y, angle, genes, generation: 0, parentA: null, parentB: null);
                c.Z = Map.GroundLevel(x, y);
                if (sp.IsPlant)
                {
                    c.PlantSize = rng.NextDouble();
                    c.Landed = true;
                }
                else
                {
                    c.Needs[Need.Food] = rng.Range(0.3333, 0.6666);
                    c.Needs[Need.Water] = rng.Range(0.3333, 0.6666);
                }
                Add(c);
            }
        }
    }

    Creature CreateCreature(SpeciesDef sp, double x, double y, double angle, Vec2[] genes,
        int generation, Creature? parentA, Creature? parentB)
    {
        var c = new Creature(nextId++, sp, genes)
        {
            X = x,
            Y = y,
            Angle = angle,
            Generation = generation,
            BirthTick = Tick,
            ParentA = parentA?.Id,
            ParentB = parentB?.Id,
        };
        if (!sp.IsPlant)
        {
            c.Name = NameGenerator.Next(rng);
            c.TickBucket = rng.Below(Config.Movement.TickBucketCount);
            for (int i = 0; i < Need.Count; i++) c.Needs[i] = rng.NextDouble();
            c.Needs[Need.Mate] = 1;
            c.Needs[Need.Rest] = 1;
            c.Needs[Need.Care] = 1;
        }
        return c;
    }

    void Add(Creature c)
    {
        creatures.Add(c);
        alive[c.Species]++;
        if (c.IsPlant)
        {
            (c.TileX, c.TileY) = Map.TileOf(c.X, c.Y);
            plantsPerTile[c.TileX, c.TileY]++;
        }
    }

    Vec2[] InheritGenes(SpeciesDef sp, Creature parent, Creature? other)
    {
        var result = new Vec2[Genes.Count];
        for (int g = 0; g < Genes.Count; g++)
        {
            var space = Genes[g].Space;
            Vec2 start = other == null ? parent.Genes[g] : space.Blend(parent.Genes[g], other.Genes[g]);
            var m = sp.Mutation[g];
            result[g] = m == null ? start : space.Mutate(start, rng, m.MaxStep, m.Exponent);
        }
        return result;
    }

    void SpawnSeed(Creature parent, SpeciesDef sp)
    {
        var genes = InheritGenes(sp, parent, null);
        var seed = CreateCreature(sp, parent.X, parent.Y, parent.Angle, genes, parent.Generation + 1, parent, null);
        seed.Z = Map.GroundLevel(seed.X, seed.Y) + SpawnHeightAboveGround;
        Burst(seed, rng.Angle());
        Add(seed);
        Stats.Births[sp.Index]++;
        Raise(SimEventType.Born, seed, parent);
    }

    Creature SpawnBaby(Creature a, Creature b, SpeciesDef sp, double food, double water)
    {
        var genes = InheritGenes(sp, a, sp.Config.Inheritance == Inheritance.Blend ? b : null);
        var baby = CreateCreature(sp, a.X, a.Y, a.Angle, genes, a.Generation + 1, a, b);
        baby.Z = Map.GroundLevel(baby.X, baby.Y) + SpawnHeightAboveGround;
        Burst(baby, rng.Angle());
        baby.Needs[Need.Food] = food;
        baby.Needs[Need.Water] = water;
        Add(baby);
        Stats.Births[sp.Index]++;
        Raise(SimEventType.Born, baby, a);
        return baby;
    }

    /// <summary>Salto al nacer (o rebote de una semilla) en la dirección dada.</summary>
    void Burst(Creature c, double angle)
    {
        var m = Config.Movement;
        double speed = rng.Range(m.BirthBurstSpeedMin, m.BirthBurstSpeedMax);
        c.Vx = Math.Cos(angle) * speed;
        c.Vy = Math.Sin(angle) * speed;
        c.Vz = m.BirthBurstUp;
    }

    void Kill(Creature c, DeathCause cause, Creature? killer = null)
    {
        if (c.Dead) return;
        c.Dead = true;
        c.CauseOfDeath = cause;
        alive[c.Species]--;
        if (c.IsPlant) plantsPerTile[c.TileX, c.TileY]--;
        Stats.Deaths[c.Species, (int)cause]++;
        Raise(SimEventType.Died, c, killer, cause);
    }

    void RemoveDead()
    {
        int w = 0;
        for (int i = 0; i < creatures.Count; i++)
        {
            var c = creatures[i];
            if (!c.Dead) creatures[w++] = c;
        }
        creatures.RemoveRange(w, creatures.Count - w);
    }

    void Raise(SimEventType type, Creature subject, Creature? other = null, DeathCause cause = default) =>
        EventRaised?.Invoke(new SimEvent(type, subject, other, cause));

    // ------------------------------------------------------------------ decisiones (animales)

    bool IsBucketTick(Creature c) => Tick % Config.Movement.TickBucketCount == c.TickBucket;

    void DoActions(Creature c)
    {
        UpdateNeeds(c);
        if (c.Dead) return;

        int next = ArgMin(c.Needs);
        if (IsBucketTick(c) || next != c.TopNeed) Search(c, next);
        Pathfind(c, next);
        c.TopNeed = next;
        ApplyIntent(c);
    }

    void UpdateNeeds(Creature c)
    {
        var nc = Config.Needs;
        double[] needs = c.Needs;
        double[] rates = c.Def.Config.NeedRates!;
        for (int i = 0; i < Need.Count; i++)
        {
            double rate = rates[i] * nc.RateScale;
            if (rate == 0) continue;
            if (i == Need.Mate) rate *= nc.MateDrainMinFactor + (1 - nc.MateDrainMinFactor) * needs[Need.Food];
            // Se usa la intención del tick anterior, como el original.
            if (i == Need.Food && !IsExerting(c.Intent)) rate *= nc.IdleHungerFactor;
            needs[i] = MathUtil.Clamp(needs[i] - rate, nc.Caps[i], 1);
            if (needs[i] <= 0 && (i == Need.Food || i == Need.Water))
            {
                Kill(c, i == Need.Food ? DeathCause.Starved : DeathCause.Dehydrated);
                return;
            }
        }

        double restCap = nc.Caps[Need.Rest];
        needs[Need.Rest] = restCap + (1 - restCap) * daylight;

        if (IsBucketTick(c))
        {
            c.Predator = FindNearest(c, predatorSpecies[c.Species], requireEdible: false);
            if (c.Predator == null)
            {
                needs[Need.Safety] = 1;
            }
            else
            {
                double d = Map.LoopDistance(c.X, c.Y, c.Predator.X, c.Predator.Y) / Map.TileSize;
                needs[Need.Safety] = MathUtil.Clamp((d - nc.FleeNearTiles) / nc.FleeRangeTiles, 0, 1);
            }
        }
    }

    static bool IsExerting(Intent it) =>
        (it & (Intent.Forward | Intent.Back | Intent.StrafeNeg | Intent.StrafePos | Intent.Jump)) != 0;

    /// <summary>Índice del menor valor; en empate gana el primero (como el argsort estable del original).</summary>
    static int ArgMin(double[] values)
    {
        int best = 0;
        for (int i = 1; i < values.Length; i++)
            if (values[i] < values[best]) best = i;
        return best;
    }

    void Search(Creature c, int need)
    {
        switch (need)
        {
            case Need.Food:
                SetTarget(c, FindNearest(c, foodSpecies[c.Species], requireEdible: true));
                break;
            case Need.Water:
                c.TargetKind = TargetKind.Water;
                c.TargetCreature = null;
                c.TargetPoint = Map.ClosestWaterPoint(c.X, c.Y);
                break;
            case Need.Mate:
                SetTarget(c, FindNearest(c, ownSpecies[c.Species], requireEdible: false));
                break;
            case Need.Safety:
                SetTarget(c, c.Predator);
                break;
            case Need.Care:
                SetTarget(c, c.RecentChild);
                break;
            // Need.Rest: se mantiene el objetivo anterior (como el original); dormido no se mueve.
        }
    }

    static void SetTarget(Creature c, Creature? target)
    {
        c.TargetCreature = target;
        c.TargetKind = target == null ? TargetKind.None : TargetKind.Creature;
    }

    /// <summary>
    /// El ser vivo más cercano (dentro del rango de visión) de las especies dadas.
    /// Revisa anillos de celdas de adentro hacia afuera y para cuando ningún anillo restante
    /// puede tener a alguien más cerca: el resultado es el mismo que revisar a todos.
    /// </summary>
    Creature? FindNearest(Creature self, int[] speciesList, bool requireEdible)
    {
        if (speciesList.Length == 0) return null;
        double best = visionPx;
        Creature? result = null;
        int rings = grid.RingsFor(visionPx);
        if (rings < 0)
        {
            cellBuffer.Clear();
            for (int i = 0; i < grid.CellCount; i++) cellBuffer.Add(i);
            ScanCells(self, speciesList, requireEdible, ref best, ref result);
            return result;
        }
        for (int r = 0; r <= rings; r++)
        {
            if ((r - 1) * grid.CellSize >= best) break;
            grid.CollectRing(self.X, self.Y, r, cellBuffer);
            ScanCells(self, speciesList, requireEdible, ref best, ref result);
        }
        return result;
    }

    void ScanCells(Creature self, int[] speciesList, bool requireEdible, ref double best, ref Creature? result)
    {
        foreach (int s in speciesList)
        {
            foreach (int cell in cellBuffer)
            {
                foreach (var other in grid.Cell(s, cell))
                {
                    if (other == self || other.Dead) continue;
                    double d = Map.LoopDistance(self.X, self.Y, other.X, other.Y);
                    if (d >= best) continue;
                    if (requireEdible && !CanEat(self, other)) continue;
                    best = d;
                    result = other;
                }
            }
        }
    }

    void GetTargetPosition(Creature c, double lookAhead, out double x, out double y)
    {
        if (c.TargetKind == TargetKind.Creature)
        {
            var t = c.TargetCreature!;
            x = t.X + lookAhead * t.Vx;
            y = t.Y + lookAhead * t.Vy;
        }
        else
        {
            x = c.TargetPoint.X;
            y = c.TargetPoint.Y;
        }
    }

    void Pathfind(Creature c, int need)
    {
        var m = Config.Movement;
        if (c.TargetKind == TargetKind.None)
        {
            // Con hambre, sed o ganas de pareja pero sin objetivo a la vista: deambular.
            c.Intent = need <= Need.Mate ? Intent.Wander : Intent.None;
            return;
        }

        Intent it = Intent.None;
        if (need <= Need.Mate || need == Need.Care)
        {
            GetTargetPosition(c, m.LookAheadTicks, out double tx, out double ty);
            double sx = c.X + m.LookAheadTicks * c.Vx, sy = c.Y + m.LookAheadTicks * c.Vy;
            double dx = Map.Delta(tx - sx), dy = Map.Delta(ty - sy);
            double dist = Math.Sqrt(dx * dx + dy * dy);
            double angle = MathUtil.WrapAngle(Math.Atan2(dy, dx) - c.Angle) / MathUtil.Tau;
            if (angle <= -TurnThreshold) it |= Intent.TurnNeg;
            else if (angle >= TurnThreshold) it |= Intent.TurnPos;

            double window = dist <= NearDistanceTiles * Map.TileSize ? AimWindowNear : AimWindowFar;
            if (Math.Abs(angle) < window)
            {
                it |= Intent.Forward;
                if (rng.NextDouble() < m.JumpChance && dist >= Map.TileSize) it |= Intent.Jump;
                if (rng.NextDouble() < StrafeChance)
                {
                    // Ojo: en el original este strafe empuja hacia el lado CONTRARIO al objetivo.
                    // Se conserva igual para que el comportamiento sea comparable.
                    if (angle >= StrafeMin && angle < StrafeMax) it |= Intent.StrafeNeg;
                    else if (angle <= -StrafeMin && angle > -StrafeMax) it |= Intent.StrafePos;
                }
            }
        }
        else if (need == Need.Safety)
        {
            GetTargetPosition(c, 0, out double tx, out double ty);
            double dx = Map.Delta(c.X - tx), dy = Map.Delta(c.Y - ty);
            double angle = MathUtil.WrapAngle(Math.Atan2(dy, dx) - c.Angle) / MathUtil.Tau;
            if (angle <= -FleeTurnThreshold) it |= Intent.TurnNeg;
            else if (angle >= FleeTurnThreshold) it |= Intent.TurnPos;
            if (Math.Abs(angle) < FleeAimWindow) it |= Intent.Forward;
            it |= Intent.Jump; // huyendo, siempre salta
        }
        c.Intent = it;
    }

    void ApplyIntent(Creature c)
    {
        var m = Config.Movement;
        var it = c.Intent;
        double speed = c.Def.Config.Speed;
        double hungerMult = 1 - m.HungerSpeedPenalty * Math.Min(c.Needs[Need.Food], 1);
        double a = m.Accel * speed * hungerMult;
        double cos = Math.Cos(c.Angle), sin = Math.Sin(c.Angle);

        if ((it & Intent.Back) != 0) { c.Vx -= cos * a; c.Vy -= sin * a; }
        if ((it & Intent.Forward) != 0) { c.Vx += cos * a; c.Vy += sin * a; }
        if ((it & Intent.StrafePos) != 0) { c.Vx -= sin * a; c.Vy += cos * a; }
        if ((it & Intent.StrafeNeg) != 0) { c.Vx += sin * a; c.Vy -= cos * a; }

        if ((it & Intent.Wander) != 0)
        {
            if (IsBucketTick(c)) c.WanderAction = rng.Below(4); // 0 quieto, 1 avanzar, 2 y 3 girar
            double wa = m.WanderAccel * speed * hungerMult;
            switch (c.WanderAction)
            {
                case 1: c.Vx += cos * wa; c.Vy += sin * wa; break;
                case 2: c.Angle -= m.TurnSpeed; break;
                case 3: c.Angle += m.TurnSpeed; break;
            }
        }

        if ((it & Intent.Jump) != 0 && OnGround(c) && c.Vz <= MaxVzToJump) c.Vz = m.JumpVelocity;
        if ((it & Intent.TurnNeg) != 0) c.Angle -= m.TurnSpeed;
        if ((it & Intent.TurnPos) != 0) c.Angle += m.TurnSpeed;
        c.Angle = MathUtil.WrapAngle(c.Angle);
    }

    // ------------------------------------------------------------------ física y acciones

    bool OnGround(Creature c) => c.Z <= Map.GroundLevel(c.X, c.Y);

    void DoPhysics(Creature c)
    {
        var m = Config.Movement;
        if (c.IsPlant)
        {
            GrowPlant(c);
            // Una planta ya plantada no se mueve: su física no cambiaría nada.
            if (c.Landed) return;
        }

        bool startedOnGround = OnGround(c);
        c.X = Map.Wrap(c.X + c.Vx);
        c.Y = Map.Wrap(c.Y + c.Vy);
        c.Z += c.Vz;
        if (c.IsPlant)
        {
            UpdatePlantTile(c); // las semillas en vuelo no tienen roce
        }
        else
        {
            c.Vx *= m.Friction;
            c.Vy *= m.Friction;
        }

        double ground = Map.GroundLevel(c.X, c.Y);
        if (c.Vz <= 0 && startedOnGround) c.Z = ground; // pegado al suelo al bajar una pendiente
        if (c.Z <= ground)
        {
            c.Z = ground;
            c.Vz = 0;
            if (c.IsPlant && !c.Landed)
            {
                if (rng.NextDouble() < c.Def.Plant.SeedBounceChance)
                {
                    Burst(c, Math.Atan2(c.Vy, c.Vx)); // rebota y sigue en la misma dirección
                }
                else
                {
                    c.Vx = 0;
                    c.Vy = 0;
                    c.Landed = true;
                    if (plantsPerTile[c.TileX, c.TileY] > Config.World.MaxPlantsPerTile)
                    {
                        Kill(c, DeathCause.Crowded);
                        return;
                    }
                }
            }
        }
        else
        {
            c.Vz -= m.Gravity;
        }

        if (c.IsPlant) return;
        TryEat(c);
        TryDrink(c);
        TryMate(c);
    }

    void GrowPlant(Creature c)
    {
        // Una planta plantada está exactamente a nivel del suelo (Z = suelo), así que Z sirve de altura.
        if (!c.Landed && !OnGround(c)) return;
        var p = c.Def.Plant;
        double height = c.Z / Map.TileSize / Config.World.ElevationFactor;
        double offBy = Math.Abs(height - p.IdealHeight);
        double heightFactor = p.MinHeightFactor + (1 - p.MinHeightFactor) *
            Math.Pow(MathUtil.Clamp(1 - offBy / p.HeightTolerance, 0, 1), p.HeightExponent);
        double growth = p.BaseGrowth + heightFactor * p.CrowdingFactor * daylight;
        c.PlantSize += growth * rng.Range(p.GrowthNoiseMin, p.GrowthNoiseMax) * p.GrowthRate;
        if (c.PlantSize >= p.SeedAtSize)
        {
            c.PlantSize -= p.SizeLossOnSeed;
            c.LastSeedTick = Tick;
            c.Children++;
            SpawnSeed(c, c.Def);
        }
    }

    void UpdatePlantTile(Creature c)
    {
        var (tx, ty) = Map.TileOf(c.X, c.Y);
        if (tx == c.TileX && ty == c.TileY) return;
        plantsPerTile[c.TileX, c.TileY]--;
        plantsPerTile[tx, ty]++;
        c.TileX = tx;
        c.TileY = ty;
    }

    double Distance3(Creature a, Creature b)
    {
        double dx = Map.Delta(a.X - b.X), dy = Map.Delta(a.Y - b.Y), dz = a.Z - b.Z;
        return Math.Sqrt(dx * dx + dy * dy + dz * dz);
    }

    void TryEat(Creature c)
    {
        if (c.TopNeed != Need.Food || c.TargetKind != TargetKind.Creature) return;
        var prey = c.TargetCreature!;
        if (prey.Dead || !CanEat(c, prey)) return;
        if (Distance3(c, prey) >= Config.Movement.CollisionDistancePx) return;

        double value = prey.IsPlant ? prey.PlantSize : prey.Needs[Need.Food];
        double calories = prey.Def.Config.CaloriesRate * value;
        Kill(prey, DeathCause.Eaten, c);
        c.Needs[Need.Food] = Math.Min(1, c.Needs[Need.Food] + calories);
        c.LastMealTick = Tick;
        c.Meals++;
        Raise(SimEventType.Ate, c, prey);
    }

    void TryDrink(Creature c)
    {
        if (c.TopNeed != Need.Water || c.Z >= Map.WaterLevel(c.X, c.Y, Tick)) return;
        c.Needs[Need.Water] = Math.Min(1, c.Needs[Need.Water] + Config.Needs.WaterGain);
        c.Drinks++;
        Raise(SimEventType.Drank, c);
    }

    void TryMate(Creature c)
    {
        if (c.TopNeed != Need.Mate || c.TargetKind != TargetKind.Creature) return;
        var partner = c.TargetCreature!;
        if (partner.Dead || partner == c || partner.Species != c.Species) return;
        if (Distance3(c, partner) >= Config.Movement.CollisionDistancePx) return;

        // Tener una cría cansa: cada padre pierde 1/3 de su comida, y la cría recibe 1/3 de la suma.
        double childFood = (c.Needs[Need.Food] + partner.Needs[Need.Food]) / 3.0;
        c.Needs[Need.Food] -= c.Needs[Need.Food] / 3.0;
        partner.Needs[Need.Food] -= partner.Needs[Need.Food] / 3.0;
        c.Needs[Need.Care] = 0;
        partner.Needs[Need.Care] = 0;
        double childWater = (c.Needs[Need.Water] + partner.Needs[Need.Water]) / 2.0;

        var baby = SpawnBaby(c, partner, c.Def, childFood, childWater);
        c.Needs[Need.Mate] = Math.Min(1, c.Needs[Need.Mate] + Config.Needs.MateCooldown);
        c.Children++;
        partner.Children++;
        c.RecentChild = baby;
        partner.RecentChild = baby;
        Raise(SimEventType.Mated, c, partner);
    }
}
