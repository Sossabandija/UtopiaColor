using System.Text.Json.Nodes;

namespace UtopiaSim;

/// <summary>
/// Aplica un "parche" JSON sobre otro, para escribir una configuración como "la original más estos cambios".
/// Reglas:
///   - Objetos: se mezclan campo por campo, recursivamente. Un campo en null se borra.
///   - Listas de objetos con "name" (especies, genes): se mezclan por nombre; un nombre nuevo se agrega al final.
///   - Cualquier otra cosa (números, textos, listas de números): el parche reemplaza.
/// </summary>
public static class JsonMerge
{
    /// <summary>Devuelve el resultado; puede modificar <paramref name="target"/>, nunca <paramref name="patch"/>.</summary>
    public static JsonNode? Apply(JsonNode? target, JsonNode? patch)
    {
        if (patch is JsonObject po && target is JsonObject to)
        {
            foreach (var (key, value) in po)
            {
                if (value == null) to.Remove(key);
                else to[key] = Apply(to[key]?.DeepClone(), value);
            }
            return to;
        }
        if (patch is JsonArray { Count: > 0 } pa && target is JsonArray ta && IsNamedList(pa) && IsNamedList(ta))
        {
            foreach (var item in pa)
            {
                string name = NameOf(item!);
                int i = IndexOfName(ta, name);
                if (i < 0) ta.Add(item!.DeepClone());
                else ta[i] = Apply(ta[i]!.DeepClone(), item);
            }
            return ta;
        }
        return patch?.DeepClone();
    }

    static bool IsNamedList(JsonArray a) => a.All(n => n is JsonObject o && o["name"] is JsonValue v && v.TryGetValue<string>(out _));

    static string NameOf(JsonNode n) => n["name"]!.GetValue<string>();

    static int IndexOfName(JsonArray a, string name)
    {
        for (int i = 0; i < a.Count; i++)
            if (NameOf(a[i]!) == name) return i;
        return -1;
    }
}
