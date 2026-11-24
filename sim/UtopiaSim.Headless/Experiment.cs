using System.Text.Json;
using System.Text.Json.Nodes;

namespace UtopiaSim.Headless;

/// <summary>Una combinación de mecanismos ya aplicada sobre la configuración base.</summary>
sealed record Variant(string Name, string[] Mechanisms, JsonObject Json, SimConfig Config);

/// <summary>
/// Un experimento: una configuración base y varios "mecanismos", cada uno un parche JSON
/// (ver <see cref="JsonMerge"/>). Se prueban todas las combinaciones: con 3 mecanismos, 8 variantes.
/// <code>
/// {
///   "base": "../original.json",   // relativo a este archivo
///   "days": 100, "runs": 16, "firstSeed": 1,
///   "mechanisms": {
///     "saciedad": { "species": [ { "name": "carnivoro", "digestDays": 1 } ] },
///     ...
///   }
/// }
/// </code>
/// </summary>
sealed class Experiment
{
    public const int MaxMechanisms = 6;
    static readonly string[] Keys = ["base", "days", "runs", "firstSeed", "mechanisms"];

    Experiment(string name, JsonObject baseJson, List<(string Name, JsonObject Patch)> mechanisms)
    {
        Name = name;
        BaseJson = baseJson;
        Mechanisms = mechanisms;
    }

    public string Name { get; }
    public JsonObject BaseJson { get; }
    public IReadOnlyList<(string Name, JsonObject Patch)> Mechanisms { get; }
    public double? Days { get; private init; }
    public int? Runs { get; private init; }
    public ulong? FirstSeed { get; private init; }

    public static Experiment Load(string path)
    {
        string text;
        try
        {
            text = File.ReadAllText(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            throw new ConfigException($"No pude leer el experimento '{path}': {e.Message}", e);
        }
        var root = SimConfig.ParseObject(text, path);
        var errors = root.Select(kv => kv.Key).Where(k => !Keys.Contains(k))
            .Select(k => $"campo desconocido '{k}' (se esperaba: {string.Join(", ", Keys)})").ToList();

        if (root["base"] is not JsonValue b || !b.TryGetValue<string>(out var baseFile))
            throw new ConfigException($"'{path}': falta \"base\": la configuración de partida (ruta relativa a este archivo).");
        string basePath = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(path)) ?? ".", baseFile);
        var baseJson = SimConfig.LoadJson(basePath);

        var mechanisms = new List<(string, JsonObject)>();
        if (root["mechanisms"] is JsonObject mech)
        {
            foreach (var (name, patch) in mech)
            {
                if (string.IsNullOrWhiteSpace(name) || name.Contains('+') || name == "base")
                    errors.Add($"mecanismo '{name}': el nombre no puede estar vacío, contener '+' ni ser 'base'.");
                if (patch is JsonObject p) mechanisms.Add((name, p));
                else errors.Add($"mecanismo '{name}': debe ser un objeto con los cambios a la configuración.");
            }
        }
        else
        {
            errors.Add("falta \"mechanisms\": { \"nombre\": { cambios }, ... }.");
        }
        if (mechanisms.Count > MaxMechanisms)
            errors.Add($"hay {mechanisms.Count} mecanismos: son {1 << mechanisms.Count} combinaciones. El máximo es {MaxMechanisms}.");

        double? days = Number(root, "days", errors);
        double? runs = Number(root, "runs", errors);
        double? seed = Number(root, "firstSeed", errors);
        if (errors.Count > 0)
            throw new ConfigException($"Problemas en el experimento '{path}':\n  - " + string.Join("\n  - ", errors));

        return new Experiment(Path.GetFileNameWithoutExtension(path), baseJson, mechanisms)
        {
            Days = days,
            Runs = runs is double r ? (int)r : null,
            FirstSeed = seed is double s ? (ulong)s : null,
        };
    }

    static double? Number(JsonObject root, string key, List<string> errors)
    {
        if (!root.TryGetPropertyValue(key, out var node) || node == null) return null;
        if (node is JsonValue v && v.TryGetValue<double>(out double d) && d > 0) return d;
        errors.Add($"\"{key}\" debe ser un número positivo.");
        return null;
    }

    /// <summary>
    /// Todas las combinaciones, de menos a más mecanismos: base, cada uno solo, de a dos... todos.
    /// Los parches se aplican en el orden en que aparecen en el archivo.
    /// </summary>
    public List<Variant> Variants()
    {
        int n = Mechanisms.Count;
        var masks = Enumerable.Range(0, 1 << n)
            .OrderBy(m => System.Numerics.BitOperations.PopCount((uint)m))
            .ThenBy(m => ReverseOrderKey(m, n));
        var result = new List<Variant>();
        foreach (int mask in masks)
        {
            var json = (JsonObject)BaseJson.DeepClone();
            var used = new List<string>();
            for (int i = 0; i < n; i++)
            {
                if ((mask & (1 << i)) == 0) continue;
                json = (JsonObject)JsonMerge.Apply(json, Mechanisms[i].Patch)!;
                used.Add(Mechanisms[i].Name);
            }
            string name = used.Count == 0 ? "base" : string.Join('+', used);
            result.Add(new Variant(name, used.ToArray(), json, SimConfig.FromJson(json, $"{Name} → {name}")));
        }
        return result;
    }

    /// <summary>Ordena "brotes" antes que "saciedad" (el orden del archivo), no por el valor de la máscara.</summary>
    static int ReverseOrderKey(int mask, int n)
    {
        int key = 0;
        for (int i = 0; i < n; i++)
            if ((mask & (1 << i)) != 0) key |= 1 << (n - 1 - i);
        return -key;
    }

    static readonly JsonSerializerOptions PrettyOptions = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static string Pretty(JsonObject json) => json.ToJsonString(PrettyOptions);
}
