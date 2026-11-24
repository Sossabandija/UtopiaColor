using System.Text.Json;
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

    public static SimConfig Load(string path)
    {
        string json;
        try
        {
            json = File.ReadAllText(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            throw new ConfigException($"No pude leer la configuración '{path}': {e.Message}", e);
        }
        return Parse(json, path);
    }

    public static SimConfig Parse(string json, string source = "(texto)")
    {
        SimConfig? cfg;
        try
        {
            cfg = JsonSerializer.Deserialize<SimConfig>(json, JsonOptions);
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
        }
        bool IsLineGene(string name) => Genes.Any(g => g.Name == name && g.Shape == GeneShape.Polygon && g.Sides == 2);

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
            Check(s.CaloriesRate >= 0, $"{who}: caloriesRate no puede ser negativo.");
            if (s.Kind == SpeciesKind.Plant)
            {
                Check(s.Eats.Count == 0, $"{who}: una planta no puede comer (eats debe estar vacío).");
                var p = s.Plant ?? new PlantConfig();
                Check(p.HeightTolerance > 0, $"{who}: plant.heightTolerance debe ser positivo.");
                Check(p.SizeLossOnSeed > 0, $"{who}: plant.sizeLossOnSeed debe ser positivo.");
            }
            else
            {
                Check(s.NeedRates is { Length: Need.Count }, $"{who}: needRates debe tener {Need.Count} valores ({string.Join(", ", Need.Names)}).");
                Check(s.Speed >= 0, $"{who}: speed no puede ser negativo.");
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
}
