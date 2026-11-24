using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace UtopiaSim;

public sealed class ConfigException(string message, Exception? inner = null) : Exception(message, inner);

public enum GeneShape { Disk, Polygon }

public enum SpeciesKind { Plant, Animal }

/// <summary>De dónde sacan los genes las crías de animales.</summary>
public enum Inheritance
{
    /// <summary>Copia mutada de un solo padre (el que inició el apareamiento). Así funciona el original.</summary>
    OneParent,

    /// <summary>Promedio de ambos padres, luego mutado.</summary>
    Blend,
}

/// <summary>
/// Toda la configuración de una simulación. Los valores por defecto de los campos numéricos
/// son los del original de Processing. Unidades: distancias en "px" del original
/// (1 casilla = <see cref="WorldConfig.TileSize"/> px); los nombres que terminan en Tiles están en casillas.
/// </summary>
public sealed class SimConfig
{
    public ulong Seed { get; set; } = 1;
    public WorldConfig World { get; set; } = new();
    public MovementConfig Movement { get; set; } = new();
    public NeedsConfig Needs { get; set; } = new();
    public PredationConfig Predation { get; set; } = new();
    public List<GeneConfig> Genes { get; set; } = [];
    public List<SpeciesConfig> Species { get; set; } = [];

    static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        WriteIndented = true,
        // Un nombre mal escrito en el JSON es un error, no algo que se ignora en silencio.
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    static readonly JsonDocumentOptions DocumentOptions = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>
    /// Lee una configuración. Si tiene <c>"extends": "otra.json"</c> (ruta relativa a este archivo),
    /// parte de esa y le aplica los cambios con <see cref="JsonMerge"/>.
    /// </summary>
    public static SimConfig Load(string path) => FromJson(LoadJson(path), path);

    /// <summary>El JSON de un archivo con sus "extends" ya resueltos (sin comentarios).</summary>
    public static JsonObject LoadJson(string path) => LoadJson(path, depth: 0);

    static JsonObject LoadJson(string path, int depth)
    {
        if (depth > 10) throw new ConfigException($"'{path}': demasiados \"extends\" encadenados (¿un archivo se extiende a sí mismo?).");
        string text;
        try
        {
            text = File.ReadAllText(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            throw new ConfigException($"No pude leer la configuración '{path}': {e.Message}", e);
        }
        var obj = ParseObject(text, path);
        if (!obj.TryGetPropertyValue("extends", out var ext)) return obj;

        if (ext is not JsonValue v || !v.TryGetValue<string>(out var basePath))
            throw new ConfigException($"'{path}': \"extends\" debe ser la ruta de otro archivo .json.");
        obj.Remove("extends");
        string full = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(path)) ?? ".", basePath);
        return (JsonObject)JsonMerge.Apply(LoadJson(full, depth + 1), obj)!;
    }

    /// <summary>Lee un objeto JSON permitiendo comentarios y comas finales.</summary>
    public static JsonObject ParseObject(string text, string source)
    {
        JsonNode? node;
        try
        {
            node = JsonNode.Parse(text, documentOptions: DocumentOptions);
        }
        catch (JsonException e)
        {
            throw new ConfigException($"Error en '{source}': {e.Message}", e);
        }
        return node as JsonObject ?? throw new ConfigException($"'{source}' debe ser un objeto JSON {{ ... }}.");
    }

    public static SimConfig Parse(string json, string source = "(texto)") => FromJson(ParseObject(json, source), source);

    public static SimConfig FromJson(JsonObject json, string source)
    {
        if (json.ContainsKey("extends"))
            throw new ConfigException($"'{source}': \"extends\" solo funciona al leer un archivo (SimConfig.Load).");
        SimConfig? cfg;
        try
        {
            cfg = json.Deserialize<SimConfig>(JsonOptions);
        }
        catch (JsonException e)
        {
            throw new ConfigException($"Error en '{source}': {e.Message}", e);
        }
        if (cfg == null) throw new ConfigException($"'{source}' está vacío.");
        cfg.Validate(source);
        return cfg;
    }

    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);

    /// <summary>Revisa que la configuración tenga sentido. Junta todos los problemas en un solo mensaje.</summary>
    public void Validate(string source = "configuración")
    {
        var errors = new List<string>();
        void Check(bool ok, string message)
        {
            if (!ok) errors.Add(message);
        }

        Check(World.Size >= 2, "world.size debe ser al menos 2.");
        Check(World.TileSize > 0, "world.tileSize debe ser positivo.");
        Check(World.TicksPerDay > 0, "world.ticksPerDay debe ser positivo.");
        Check(World.NoiseOctaves >= 1, "world.noiseOctaves debe ser al menos 1.");
        Check(Movement.TickBucketCount >= 1, "movement.tickBucketCount debe ser al menos 1.");
        Check(Needs.Caps.Length == Need.Count, $"needs.caps debe tener {Need.Count} valores ({string.Join(", ", Need.Names)}).");

        var geneNames = new HashSet<string>();
        foreach (var g in Genes)
        {
            Check(!string.IsNullOrWhiteSpace(g.Name), "Hay un gen sin nombre.");
            Check(geneNames.Add(g.Name), $"Gen repetido: '{g.Name}'.");
            if (g.Shape == GeneShape.Polygon)
                Check(g.Sides >= 2, $"Gen '{g.Name}': un polígono necesita sides ≥ 2 (2 = línea).");
            if (g.VertexNames != null)
                Check(g.Shape == GeneShape.Polygon && g.VertexNames.Count == g.Sides,
                    $"Gen '{g.Name}': vertexNames debe tener un nombre por esquina ({g.Sides}) y solo sirve para polygon.");
        }
        bool IsLineGene(string name) => Genes.Any(g => g.Name == name && g.Shape == GeneShape.Polygon && g.Sides == 2);
        bool IsTriangleGene(string name) => Genes.Any(g => g.Name == name && g.Shape == GeneShape.Polygon && g.Sides == 3);

        if (!string.IsNullOrEmpty(Predation.MatchGene))
            Check(geneNames.Contains(Predation.MatchGene), $"predation.matchGene: no existe el gen '{Predation.MatchGene}'.");

        Check(Species.Count > 0, "Debe haber al menos una especie.");
        var speciesNames = new HashSet<string>();
        foreach (var s in Species)
        {
            string who = $"Especie '{s.Name}'";
            Check(!string.IsNullOrWhiteSpace(s.Name), "Hay una especie sin nombre.");
            Check(speciesNames.Add(s.Name), $"Especie repetida: '{s.Name}'.");
            Check(s.InitialCount >= 0, $"{who}: initialCount no puede ser negativo.");
            Check(s.ArrivalDay >= 0, $"{who}: arrivalDay no puede ser negativo.");
            Check(s.CaloriesRate >= 0, $"{who}: caloriesRate no puede ser negativo.");
            if (s.Kind == SpeciesKind.Plant)
            {
                Check(s.Eats.Count == 0, $"{who}: una planta no puede comer (eats debe estar vacío).");
                var p = s.Plant ?? new PlantConfig();
                Check(p.HeightTolerance > 0, $"{who}: plant.heightTolerance debe ser positivo.");
                Check(p.SizeLossOnSeed > 0, $"{who}: plant.sizeLossOnSeed debe ser positivo.");
                if (p.Seedling is { } sd)
                {
                    Check(sd.UntilSize > 0 && sd.UntilSize <= p.SeedAtSize, $"{who}: plant.seedling.untilSize debe estar entre 0 y plant.seedAtSize.");
                    Check(sd.RiskPerDay >= 0 && sd.ShadeRiskPerDay >= 0 && sd.TerrainRiskPerDay >= 0,
                        $"{who}: los riesgos de plant.seedling no pueden ser negativos.");
                }
                if (p.Dispersal is { } dp)
                {
                    Check(IsTriangleGene(dp.Gene), $"{who}: plant.dispersal.gene '{dp.Gene}' debe ser un gen polygon de 3 lados (fruto, viento, abrojo).");
                    Check(dp.Exponent > 0, $"{who}: plant.dispersal.exponent debe ser positivo.");
                    if (dp.Wind != null) Check(dp.Wind.MaxRangeFactor >= 1, $"{who}: plant.dispersal.wind.maxRangeFactor debe ser al menos 1.");
                    if (dp.Fruit is { } f)
                        Check(f.MaxSeeds >= 1 && f.Calories >= 0 && f.MaxColorDistance >= 0 && f.TransitDays >= 0 && f.MaxOnPlant >= 1,
                            $"{who}: plant.dispersal.fruit: maxSeeds y maxOnPlant ≥ 1, el resto ≥ 0.");
                    if (dp.Burr is { } b)
                        Check(b.MaxSeeds >= 1 && b.DropEveryDays > 0 && b.MaxOnPlant >= 1,
                            $"{who}: plant.dispersal.burr: maxSeeds y maxOnPlant ≥ 1, dropEveryDays > 0.");
                }
                Check(s.DigestDays == 0 && s.MateRangeTiles == null && s.DietGene == null && s.Territory == null && s.Burrows == null && s.Cover == null,
                    $"{who}: digestDays, mateRangeTiles, dietGene, territory, burrows y cover son solo para animales.");
            }
            else
            {
                Check(s.NeedRates is { Length: Need.Count }, $"{who}: needRates debe tener {Need.Count} valores ({string.Join(", ", Need.Names)}).");
                Check(s.Speed >= 0, $"{who}: speed no puede ser negativo.");
                Check(s.DigestDays >= 0, $"{who}: digestDays no puede ser negativo.");
                Check(s.MateRangeTiles is null or > 0, $"{who}: mateRangeTiles debe ser positivo.");
                Check(s.Plant == null, $"{who}: plant es solo para plantas.");
                if (s.Territory is { } t)
                    Check(t.RadiusTiles > 0 && t.Interference >= 0 && t.FailCooldownDays >= 0 && t.Tolerance >= 0 && t.MaxRivalsToBreed is null or >= 0,
                        $"{who}: territory.radiusTiles debe ser positivo; interference, tolerance, maxRivalsToBreed y failCooldownDays, ≥ 0.");
                if (s.Burrows is { } b)
                {
                    Check(b.Count >= 0 && b.Capacity >= 1 && (b.Count >= 1 || b.Dig != null),
                        $"{who}: burrows.capacity debe ser al menos 1, y burrows.count también (salvo que la especie cave: dig).");
                    if (b.Dig is { } dg)
                        Check(dg.MaxCount >= 1 && dg.DigDays > 0 && dg.AbandonDays is null or > 0 && dg.FleeDigDays is null or > 0 && dg.FleeSafeTiles >= 0,
                            $"{who}: burrows.dig.maxCount ≥ 1; digDays, abandonDays y fleeDigDays positivos; fleeSafeTiles ≥ 0.");
                    Check(b.PlantSlots >= 0, $"{who}: burrows.plantSlots no puede ser negativo.");
                    if (b.Collapse is { } col)
                        Check(col.HalfLifeDays is null or > 0 && col.RuinDays >= 0 && col.TilledSlots >= 0 && col.MaxTilledSlots >= 0,
                            $"{who}: burrows.collapse: halfLifeDays positivo; ruinDays, tilledSlots y maxTilledSlots ≥ 0.");
                    if (b.MaxColorDistance is double mcd)
                    {
                        Check(mcd >= 0, $"{who}: burrows.maxColorDistance no puede ser negativo.");
                        Check(geneNames.Contains(b.ColorGene), $"{who}: burrows.colorGene: no existe el gen '{b.ColorGene}'.");
                    }
                    Check(b.SearchTiles > 0 && b.EnterTiles > 0 && b.MinSpacingTiles >= 0,
                        $"{who}: burrows.searchTiles y enterTiles deben ser positivos; minSpacingTiles, ≥ 0.");
                    Check(b.MinHeight >= 0 && b.MaxHeight <= 1 && b.MinHeight < b.MaxHeight,
                        $"{who}: burrows necesita 0 ≤ minHeight < maxHeight ≤ 1.");
                }
                if (s.Cover is { } cv)
                    Check(cv.Capacity >= 1 && cv.SearchTiles > 0 && cv.EnterTiles > 0 && cv.VisibleTiles >= 0 && cv.MinPlantSize >= 0,
                        $"{who}: cover.capacity ≥ 1; searchTiles y enterTiles positivos; visibleTiles y minPlantSize ≥ 0.");
            }
            foreach (var food in s.Eats)
                Check(Species.Any(o => o.Name == food), $"{who}: come '{food}', pero esa especie no existe.");
            foreach (var (gene, value) in s.InitialGenes)
            {
                Check(geneNames.Contains(gene), $"{who}: initialGenes menciona el gen '{gene}', que no existe.");
                Check(value is { Length: 1 or 2 }, $"{who}: initialGenes.{gene} debe ser [x] o [x, y].");
            }
            foreach (var (gene, m) in s.Mutation)
            {
                Check(geneNames.Contains(gene), $"{who}: mutation menciona el gen '{gene}', que no existe.");
                Check(m.MaxStep >= 0, $"{who}: mutation.{gene}.maxStep no puede ser negativo.");
            }
            if (!string.IsNullOrEmpty(s.PreferredStageGene))
                Check(IsLineGene(s.PreferredStageGene), $"{who}: preferredStageGene '{s.PreferredStageGene}' debe ser un gen polygon de 2 lados (una línea).");
            if (!string.IsNullOrEmpty(s.DietGene))
            {
                Check(IsLineGene(s.DietGene), $"{who}: dietGene '{s.DietGene}' debe ser un gen polygon de 2 lados (una línea).");
                Check(s.DietGene != s.PreferredStageGene, $"{who}: dietGene y preferredStageGene deben ser genes distintos.");
            }
            Check(s.DietExponent > 0, $"{who}: dietExponent debe ser positivo.");
            if (s.Diploid is { } dp2)
                Check(dp2.DominanceStep >= 0 && dp2.PollenTiles > 0 && dp2.MinDonorSize >= 0 && dp2.MinDonorSize <= 1,
                    $"{who}: diploid.dominanceStep ≥ 0, pollenTiles > 0 y minDonorSize entre 0 y 1.");
            if (s.Refuge is { } r)
                Check(r.MinHeight >= 0 && r.MaxHeight <= 1 && r.MinHeight < r.MaxHeight,
                    $"{who}: refuge necesita 0 ≤ minHeight < maxHeight ≤ 1.");
        }

        if (errors.Count > 0)
            throw new ConfigException($"Problemas en {source}:\n  - " + string.Join("\n  - ", errors));
    }
}

public sealed class WorldConfig
{
    /// <summary>Casillas por lado. El mundo es un toro: los bordes se conectan.</summary>
    public int Size { get; set; } = 36;
    public double TileSize { get; set; } = 140;
    public int TicksPerDay { get; set; } = 4500;
    /// <summary>Altura máxima del terreno, en casillas.</summary>
    public double ElevationFactor { get; set; } = 8;
    public double WaterLevelTiles { get; set; } = 2.44;
    /// <summary>Profundidad mínima para que una casilla cuente como "agua para beber".</summary>
    public double DrinkDepthPx { get; set; } = 20;
    public double WaveSizePx { get; set; } = 10;
    public double WavePeriodTicks { get; set; } = 100;
    /// <summary>Frecuencia del ruido del terreno, en ciclos por casilla.</summary>
    public double NoiseFrequency { get; set; } = 0.15;
    public int NoiseOctaves { get; set; } = 4;
    public double NoiseFalloff { get; set; } = 0.5;
    /// <summary>Si al aterrizar una semilla hay más plantas que esto en su casilla (contándola), muere.</summary>
    public int MaxPlantsPerTile { get; set; } = 2;
}

public sealed class MovementConfig
{
    public double Friction { get; set; } = 0.85;
    public double Accel { get; set; } = 2;
    public double WanderAccel { get; set; } = 1;
    /// <summary>Radianes por tick al girar.</summary>
    public double TurnSpeed { get; set; } = 0.1;
    public double JumpVelocity { get; set; } = 18;
    public double Gravity { get; set; } = 1;
    public double JumpChance { get; set; } = 0.02;
    /// <summary>Un animal lleno corre hasta este porcentaje más lento.</summary>
    public double HungerSpeedPenalty { get; set; } = 0.25;
    public double VisionTiles { get; set; } = 8.6;
    public double CollisionDistancePx { get; set; } = 33;
    /// <summary>Los animales re-buscan objetivo cada tantos ticks (repartidos para no hacerlo todos juntos).</summary>
    public int TickBucketCount { get; set; } = 20;
    /// <summary>Al perseguir, apuntan a donde estará el objetivo en tantos ticks.</summary>
    public double LookAheadTicks { get; set; } = 7;
    public double BirthBurstSpeedMin { get; set; } = 2;
    public double BirthBurstSpeedMax { get; set; } = 11;
    public double BirthBurstUp { get; set; } = 18;
}

public sealed class NeedsConfig
{
    /// <summary>Valor mínimo de cada necesidad. Orden: hambre, sed, pareja, sueño, huir, cuidar crías.</summary>
    public double[] Caps { get; set; } = [0, 0, 0.6, 0.6, 0, 0];
    /// <summary>Multiplicador de needRates de cada especie.</summary>
    public double RateScale { get; set; } = 0.00003;
    /// <summary>Si el animal no se mueve, el hambre baja a esta fracción de la velocidad normal.</summary>
    public double IdleHungerFactor { get; set; } = 0.3333;
    /// <summary>Las ganas de pareja suben a (min + (1-min)·comida) de la velocidad normal: con hambre, menos ganas.</summary>
    public double MateDrainMinFactor { get; set; } = 0.2;
    /// <summary>Cuánta sed se recupera por tick dentro del agua.</summary>
    public double WaterGain { get; set; } = 0.40;
    /// <summary>Cuánto se "llenan" las ganas de pareja de quien inicia un apareamiento.</summary>
    public double MateCooldown { get; set; } = 0.25;
    /// <summary>A esta distancia de un depredador (casillas), huir es 100% urgente.</summary>
    public double FleeNearTiles { get; set; } = 1.0;
    /// <summary>A FleeNearTiles + esto, huir deja de ser urgente.</summary>
    public double FleeRangeTiles { get; set; } = 3.5;
}

/// <summary>
/// Quién puede comer a quién (además de la red trófica <c>eats</c>).
/// Una presa es comestible si su tamaño (planta) o peso (animal) cae en la ventana
/// <c>centro ± halfWidth · (1 - distancia·distanceFactor)^exponent</c>,
/// donde distancia es la distancia entre los genes <c>matchGene</c> de cazador y presa.
/// </summary>
public sealed class PredationConfig
{
    /// <summary>Gen cuya similitud hace la presa más fácil de comer. Vacío = todos se parecen.</summary>
    public string? MatchGene { get; set; } = "color";
    /// <summary>Centro de la ventana para especies sin preferredStageGene.</summary>
    public double Center { get; set; } = 0.5;
    public double HalfWidth { get; set; } = 0.21;
    public double DistanceFactor { get; set; } = 0.5;
    public double Exponent { get; set; } = 4;
}

public sealed class GeneConfig
{
    public string Name { get; set; } = "";
    public GeneShape Shape { get; set; } = GeneShape.Disk;
    /// <summary>Solo para polygon: 2 = línea, 3 = triángulo, 4 = cuadrado...</summary>
    public int Sides { get; set; }
    public BoundaryRule Boundary { get; set; } = BoundaryRule.Reject;
    /// <summary>Solo para polygon: nombre de cada esquina, para el visor y los CSV (por ejemplo ["fruto", "viento", "abrojo"]).</summary>
    public List<string>? VertexNames { get; set; }
}

public sealed class MutationConfig
{
    /// <summary>Salto máximo. Para comparar con el original: maxStep = MUTATION^exponente.</summary>
    public double MaxStep { get; set; }
    public double Exponent { get; set; } = 1;
}

public sealed class SpeciesConfig
{
    public string Name { get; set; } = "";
    public SpeciesKind Kind { get; set; } = SpeciesKind.Animal;
    public int InitialCount { get; set; }
    /// <summary>
    /// Día en que llega la población inicial (initialCount individuos, repartidos al azar). 0 = desde el comienzo,
    /// como el original. Sirve para que un depredador llegue cuando ya hay presas.
    /// </summary>
    public double ArrivalDay { get; set; }
    /// <summary>Nombres de las especies que puede comer.</summary>
    public List<string> Eats { get; set; } = [];
    public double Speed { get; set; }
    /// <summary>Calorías que da al ser comida, multiplicadas por su tamaño (planta) o peso (animal).</summary>
    public double CaloriesRate { get; set; } = 1;
    /// <summary>Solo animales: velocidad a la que baja cada necesidad (hambre, sed, pareja, sueño, huir, cuidar crías).</summary>
    public double[]? NeedRates { get; set; }
    public Inheritance Inheritance { get; set; } = Inheritance.OneParent;
    /// <summary>Valor inicial de cada gen, en coordenadas de su forma. Los que falten parten en el centro.</summary>
    public Dictionary<string, double[]> InitialGenes { get; set; } = [];
    /// <summary>Variación al azar de los genes iniciales.</summary>
    public MutationConfig InitialJitter { get; set; } = new() { MaxStep = 0.1, Exponent = 1 };
    /// <summary>Mutación por gen al nacer. Un gen que no aparece aquí no muta en esta especie.</summary>
    public Dictionary<string, MutationConfig> Mutation { get; set; } = [];
    /// <summary>
    /// Gen (una línea) que decide qué etapa de la presa prefiere: 0 = temprano, 1 = tardío.
    /// Si falta, se usa predation.center para todos.
    /// </summary>
    public string? PreferredStageGene { get; set; }
    /// <summary>
    /// Gen (una línea) de dieta: 0 = folívoro (celulosa: come la planta entera y destruye las semillas),
    /// 1 = frugívoro (fructosa: come frutos y las semillas pasan intactas). Calorías de la planta entera × (1 − dieta)^dietExponent,
    /// de un fruto × dieta^dietExponent. Si falta, come plantas enteras como en el original y nunca frutos.
    /// </summary>
    public string? DietGene { get; set; }
    /// <summary>Con dietExponent > 1 un generalista (dieta 0.5) saca menos de ambas cosas: conviene especializarse.</summary>
    public double DietExponent { get; set; } = 1;
    /// <summary>
    /// Saciedad: después de comer, no vuelve a buscar comida durante digestDays × calorías de esa comida
    /// (una comida de 0.5 calorías con digestDays 1 = medio día sin cazar). 0 = apagada, como el original.
    /// </summary>
    public double DigestDays { get; set; }
    /// <summary>Hasta qué distancia (casillas) encuentra pareja. Null = movement.visionTiles, como el original.</summary>
    public double? MateRangeTiles { get; set; }
    /// <summary>Refugio: zona del terreno donde nadie puede comerse a esta especie. Null = sin refugio, como el original.</summary>
    public RefugeConfig? Refuge { get; set; }
    /// <summary>Madrigueras con cupo donde se esconde esta especie. Null = sin madrigueras, como el original.</summary>
    public BurrowConfig? Burrows { get; set; }
    /// <summary>Cobertura: se esconde en plantas grandes, donde los cazadores lo ven solo de cerca. Null = sin cobertura, como el original.</summary>
    public CoverConfig? Cover { get; set; }
    /// <summary>
    /// Genética diploide: dos alelos por gen, se expresa el más dominante. Reemplaza a <see cref="Inheritance"/>.
    /// En plantas además las vuelve sexuales (polen de una vecina). Null = un solo valor por gen, como el original.
    /// </summary>
    public DiploidConfig? Diploid { get; set; }
    /// <summary>Territorialidad: cazar con rivales de su especie cerca sale peor. Null = apagada, como el original.</summary>
    public TerritoryConfig? Territory { get; set; }
    public PlantConfig? Plant { get; set; }
}

public sealed class PlantConfig
{
    /// <summary>Altura del terreno (0 = lo más bajo, 1 = lo más alto) donde mejor crece.</summary>
    public double IdealHeight { get; set; } = 0.55;
    public double HeightTolerance { get; set; } = 0.3;
    public double HeightExponent { get; set; } = 1.6;
    public double MinHeightFactor { get; set; } = 0.05;
    public double BaseGrowth { get; set; } = 0.01;
    public double CrowdingFactor { get; set; } = 0.48;
    public double GrowthRate { get; set; } = 3.0;
    public double GrowthNoiseMin { get; set; } = 0.001;
    public double GrowthNoiseMax { get; set; } = 0.002;
    /// <summary>Al llegar a este tamaño suelta una semilla...</summary>
    public double SeedAtSize { get; set; } = 1.0;
    /// <summary>...y pierde esto de tamaño.</summary>
    public double SizeLossOnSeed { get; set; } = 0.5;
    public double SeedBounceChance { get; set; } = 0.5;
    /// <summary>Etapa de brote frágil. Null = apagada (como el original: el brote solo muere si cae en una casilla llena).</summary>
    public SeedlingConfig? Seedling { get; set; }
    /// <summary>Modos de dispersión de semillas según un gen triangular. Null = apagada (semilla normal, como el original).</summary>
    public DispersalConfig? Dispersal { get; set; }
}

/// <summary>
/// Dispersión de semillas. Un gen triangular (polygon de 3 lados) reparte la reproducción entre tres modos,
/// uno por esquina: 0 = fruto (endozoocoria), 1 = viento (anemocoria), 2 = abrojo (epizoocoria).
/// Cada vez que la planta se reproduce sortea el modo con los pesos de su gen como probabilidades.
/// La intensidad de cada modo crece con peso^exponent: con exponent > 1 las mezclas rinden poco y conviene especializarse.
/// Un modo sin su bloque (wind, fruit o burr en null) produce una semilla normal.
/// </summary>
public sealed class DispersalConfig
{
    public string Gene { get; set; } = "";
    public double Exponent { get; set; } = 2;
    public WindConfig? Wind { get; set; }
    public FruitConfig? Fruit { get; set; }
    public BurrConfig? Burr { get; set; }
}

/// <summary>Anemocoria: la semilla sale disparada más lejos (velocidad × hasta maxRangeFactor).</summary>
public sealed class WindConfig
{
    /// <summary>Alcance de una planta 100% viento, en veces el alcance normal (0,5 a 3 casillas).</summary>
    public double MaxRangeFactor { get; set; } = 4;
}

/// <summary>
/// Endozoocoria: la planta adulta cuelga un fruto con varias semillas. Un animal con gen de dieta (dietGene)
/// puede comerse el fruto sin matar a la planta; cada semilla sobrevive a su estómago con probabilidad = su dieta
/// (0 folívoro, 1 frugívoro) y sale transitDays después donde esté el animal. Un fruto que nadie come termina
/// cayendo bajo la madre como una semilla normal.
/// </summary>
public sealed class FruitConfig
{
    /// <summary>Semillas en el fruto de una planta 100% fruto (con menos peso, menos semillas; mínimo 1).</summary>
    public int MaxSeeds { get; set; } = 6;
    /// <summary>Calorías que da un fruto a un frugívoro puro.</summary>
    public double Calories { get; set; } = 0.5;
    /// <summary>Solo se come el fruto de una planta con color (matchGene) a lo más a esta distancia.</summary>
    public double MaxColorDistance { get; set; } = 0.6;
    /// <summary>Días que tardan las semillas en atravesar el estómago.</summary>
    public double TransitDays { get; set; } = 0.1;
    /// <summary>Frutos que puede colgar a la vez; con uno más, el más viejo cae.</summary>
    public int MaxOnPlant { get; set; } = 3;
}

/// <summary>
/// Epizoocoria: la planta adulta tiene un abrojo con varias semillas que se pega al primer animal que la toca
/// (de cualquier especie); el animal va soltando una semilla cada dropEveryDays. Un abrojo que nadie se lleva
/// termina cayendo bajo la madre como una semilla normal.
/// </summary>
public sealed class BurrConfig
{
    /// <summary>Semillas en el abrojo de una planta 100% abrojo (con menos peso, menos semillas; mínimo 1).</summary>
    public int MaxSeeds { get; set; } = 4;
    public double DropEveryDays { get; set; } = 0.05;
    /// <summary>Abrojos que puede tener a la vez; con uno más, el más viejo cae.</summary>
    public int MaxOnPlant { get; set; } = 2;
}

/// <summary>
/// Refugio de una presa: mientras esté donde el suelo tiene una altura entre minHeight y maxHeight, nadie se la puede comer.
/// Alturas de 0 (lo más bajo del mapa) a 1 (lo más alto). El agua cubre hasta world.waterLevelTiles / world.elevationFactor
/// (≈ 0,3 con la configuración original): { "maxHeight": 0.3 } = el agua; { "minHeight": 0.7 } = los cerros.
/// </summary>
public sealed class RefugeConfig
{
    public double MinHeight { get; set; }
    public double MaxHeight { get; set; } = 1;
    /// <summary>Al huir, corre hacia el punto de refugio más cercano (si no, se aleja del depredador como el original).</summary>
    public bool FleeToRefuge { get; set; } = true;
}

/// <summary>
/// Madrigueras: sitios fijos en tierra seca, repartidos al azar con la semilla de la corrida, donde caben hasta
/// <see cref="Capacity"/> individuos de esta especie. Dentro (a menos de enterTiles del centro) nadie se los puede comer.
/// Al sentirse amenazado corre a la madriguera más cercana con cupo a menos de searchTiles; si no hay, huye como siempre.
/// Sale cuando otra necesidad (hambre, sed...) pesa más que el miedo. El cupo cuenta también a los que van en camino.
/// A diferencia de una zona de refugio, protege a un número fijo de individuos: con muchos, casi todos quedan afuera;
/// con pocos, caben todos.
/// </summary>
public sealed class BurrowConfig
{
    public int Count { get; set; } = 30;
    public int Capacity { get; set; } = 2;
    public double SearchTiles { get; set; } = 8;
    /// <summary>Distancia mínima entre dos madrigueras, para repartirlas por el mapa.</summary>
    public double MinSpacingTiles { get; set; } = 3;
    public double EnterTiles { get; set; } = 0.3;
    /// <summary>Alturas del suelo (0-1, como en refuge) donde se puede cavar; siempre fuera del agua.</summary>
    public double MinHeight { get; set; }
    public double MaxHeight { get; set; } = 1;
    /// <summary>La especie cava sus propias madrigueras. Null = solo las <see cref="Count"/> iniciales, fijas.</summary>
    public DigConfig? Dig { get; set; }
    /// <summary>
    /// Cuántas plantas menos caben en la casilla de una madriguera (el tope es world.maxPlantsPerTile).
    /// Las plantas que ya están no mueren: solo baja el cupo para las semillas que caen ahí. 0 = no ocupa lugar.
    /// </summary>
    public int PlantSlots { get; set; }
    /// <summary>
    /// Madriguera de un solo color: el primero que la reclama le fija su color (gen colorGene) y solo entran los que estén
    /// a esta distancia de color o menos; cuando queda vacía, pierde el color. Null = entra cualquiera.
    /// </summary>
    public double? MaxColorDistance { get; set; }
    public string ColorGene { get; set; } = "color";
    /// <summary>Derrumbes por el tiempo, ruinas y tierra revuelta. Null = solo se derrumban por abandono (dig.abandonDays).</summary>
    public CollapseConfig? Collapse { get; set; }
}

/// <summary>
/// Derrumbe de madrigueras. Con halfLifeDays, cada madriguera (también las iniciales) dura un tiempo al azar con esa vida
/// media: la mitad se cae antes de halfLifeDays, la mitad de las que quedan antes del doble, etc. Al caerse, los que estaban
/// dentro o iban hacia ella salen. Cualquier derrumbe (por tiempo o por abandono) deja:
///   - una ruina que impide cavar otra a menos de minSpacingTiles durante ruinDays, como si siguiera ahí;
///   - tierra revuelta: en su casilla caben tilledSlots plantas más, acumulable hasta maxTilledSlots (para siempre).
/// Cada parte está apagada con su valor por defecto.
/// </summary>
public sealed class CollapseConfig
{
    /// <summary>Vida media de una madriguera, en días. Null = no se caen por el tiempo.</summary>
    public double? HalfLifeDays { get; set; }
    public double RuinDays { get; set; }
    public int TilledSlots { get; set; }
    public int MaxTilledSlots { get; set; } = 3;
}

/// <summary>
/// Cavar madrigueras. Un animal que duerme en tierra seca, sin otra madriguera de su especie a menos de minSpacingTiles,
/// cava una donde está: la termina tras digDays durmiendo ahí (si se mueve más de media casilla, empieza de nuevo).
/// Con fleeDigDays, también cava al huir: si está amenazado, no consiguió madriguera con cupo y el depredador está a más
/// de fleeSafeTiles, se detiene a cavar ahí mismo; al terminar (fleeDigDays después) se mete en ella. Si el depredador se
/// acerca a fleeSafeTiles o menos, deja de cavar y arranca (pierde lo avanzado).
/// La especie no puede tener más de maxCount en el mapa (contando las iniciales). Una madriguera que nadie reclama durante
/// abandonDays se derrumba (también las iniciales). Así el número de refugios sigue a dónde viven los animales,
/// pero con tope.
/// </summary>
public sealed class DigConfig
{
    public int MaxCount { get; set; } = 30;
    public double DigDays { get; set; } = 0.1;
    /// <summary>Null = nunca se derrumban.</summary>
    public double? AbandonDays { get; set; } = 3;
    /// <summary>Días que tarda en cavar una madriguera de emergencia al huir. Null = no cava al huir.</summary>
    public double? FleeDigDays { get; set; }
    /// <summary>Solo cava al huir mientras el depredador esté a más de esto (casillas).</summary>
    public double FleeSafeTiles { get; set; } = 2;
}

/// <summary>
/// Cobertura: las plantas grandes (tamaño ≥ minPlantSize) esconden a esta especie, hasta <see cref="Capacity"/> por planta.
/// Al sentirse amenazado, si no consiguió madriguera y el depredador está a más de visibleTiles, corre a la planta grande con
/// cupo más cercana a menos de searchTiles. Si el depredador ya está a visibleTiles o menos, esconderse no sirve: sale arrancando.
/// (Sin esta regla, en las pruebas el 60-75% de los herbívoros cazados estaba quieto en una planta con el carnívoro al lado.)
/// Escondido (a menos de enterTiles de la planta), se queda quieto y un cazador solo lo ve a visibleTiles o menos
/// (sin cobertura lo ve a movement.visionTiles). No es invulnerable: si el cazador llega cerca, lo caza igual.
/// Si se comen la planta, queda al descubierto. Sale cuando otra necesidad pesa más que el miedo.
/// </summary>
public sealed class CoverConfig
{
    public double MinPlantSize { get; set; } = 0.75;
    public int Capacity { get; set; } = 1;
    public double SearchTiles { get; set; } = 1.5;
    public double EnterTiles { get; set; } = 0.2;
    public double VisibleTiles { get; set; } = 2;
}

/// <summary>
/// Genética diploide (mendeliana). Cada gen tiene dos alelos, uno de cada padre, y cada alelo tiene una dominancia de 0 a 1
/// que también se hereda. Se expresa el alelo más dominante (con la misma dominancia, el punto medio); el otro queda oculto:
/// no lo ve la selección, así que puede derivar lejos y reaparecer cuando se cruzan dos portadores.
/// Al nacer, cada padre pasa uno de sus dos alelos al azar; el valor muta como siempre (mutation) y la dominancia cambia
/// hasta ± dominanceStep. En la población inicial las dominancias son al azar.
/// Plantas: el padre es la planta de su especie más cercana a la madre (a menos de pollenTiles y de tamaño ≥ minDonorSize),
/// elegida al formarse cada semilla. Sin vecina, se autopoliniza (selfing) o, con selfing = false, la semilla no se forma.
/// </summary>
public sealed class DiploidConfig
{
    public double DominanceStep { get; set; } = 0.05;
    /// <summary>Solo plantas.</summary>
    public double PollenTiles { get; set; } = 3;
    /// <summary>Solo plantas.</summary>
    public double MinDonorSize { get; set; } = 0.5;
    /// <summary>Solo plantas.</summary>
    public bool Selfing { get; set; } = true;
}

/// <summary>
/// Territorialidad. Rivales = otros de su especie a menos de radiusTiles. Dos efectos, cada uno opcional:
/// - Cría (maxRivalsToBreed): con más rivales que esto cerca, no busca pareja. Con pocos individuos no cambia nada;
///   con muchos juntos dejan de nacer crías. Frena la explosión sin matar de hambre.
/// - Caza (interference): al alcanzar a su presa, la caza resulta con probabilidad 1 / (1 + interference × max(0, rivales − tolerance));
///   si falla, la presa escapa y no vuelve a cazar por failCooldownDays. Ojo: en las pruebas mató de hambre a los carnívoros,
///   porque se juntan donde están las presas.
/// </summary>
public sealed class TerritoryConfig
{
    public double RadiusTiles { get; set; } = 4;
    /// <summary>Null = la cría no depende de los rivales.</summary>
    public int? MaxRivalsToBreed { get; set; }
    /// <summary>0 = cazar no depende de los rivales.</summary>
    public double Interference { get; set; }
    /// <summary>Rivales que no molestan (pareja, crías).</summary>
    public int Tolerance { get; set; }
    public double FailCooldownDays { get; set; } = 0.05;
}

/// <summary>
/// Brote: desde que aterriza hasta llegar a <see cref="UntilSize"/>, la planta puede morir en cualquier tick.
/// El riesgo es una tasa por día: 1 = en promedio muere en un día; 0.1 = el 10% muere por día.
/// Riesgo total = base + sombra × (otras plantas en su casilla) + terreno × (1 − qué tan bien crece a esa altura).
/// Una planta establecida (más grande que UntilSize) ya no muere así: solo muere comida.
/// </summary>
public sealed class SeedlingConfig
{
    public double UntilSize { get; set; } = 0.3;
    public double RiskPerDay { get; set; }
    public double ShadeRiskPerDay { get; set; }
    public double TerrainRiskPerDay { get; set; }
}
