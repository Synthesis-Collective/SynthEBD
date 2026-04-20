using Mutagen.Bethesda.Plugins;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace SynthEBD;

public class NifPreviewNpcSettings
{
    // Stored as a list so Newtonsoft can serialize/deserialize without needing a
    // TypeConverter for FormKey dictionary keys (Mutagen registers FormKey only
    // as a value-level JsonConverter, not as a TypeConverter — the old
    // Dictionary<FormKey, PreviewNpcPair> shape threw on load).
    [JsonConverter(typeof(RacePreviewNpcsConverter))]
    public List<RacePreviewEntry> RacePreviewNpcs { get; set; } = new();
    public PreviewNpcPair DefaultNpcs { get; set; } = new();
}

public class RacePreviewEntry
{
    public FormKey Race { get; set; } = FormKey.Null;
    public FormKey MaleNpc { get; set; } = FormKey.Null;
    public FormKey FemaleNpc { get; set; } = FormKey.Null;
}

public class PreviewNpcPair
{
    public FormKey MaleNpc { get; set; } = FormKey.Null;
    public FormKey FemaleNpc { get; set; } = FormKey.Null;
}

/// <summary>
/// Reads either the legacy dict shape { "013746:Skyrim.esm": { MaleNpc, FemaleNpc } }
/// or the new list shape [ { Race, MaleNpc, FemaleNpc } ], writes only the list shape.
/// Lets settings files saved before the list migration round-trip cleanly.
/// </summary>
public class RacePreviewNpcsConverter : JsonConverter<List<RacePreviewEntry>>
{
    public override List<RacePreviewEntry> ReadJson(
        JsonReader reader, Type objectType, List<RacePreviewEntry>? existingValue,
        bool hasExistingValue, JsonSerializer serializer)
    {
        var result = new List<RacePreviewEntry>();
        if (reader.TokenType == JsonToken.Null) return result;

        var token = JToken.Load(reader);

        if (token.Type == JTokenType.Array)
        {
            foreach (var item in (JArray)token)
            {
                var entry = item.ToObject<RacePreviewEntry>(serializer);
                if (entry != null) result.Add(entry);
            }
        }
        else if (token.Type == JTokenType.Object)
        {
            foreach (var prop in (JObject)token)
            {
                if (!FormKey.TryFactory(prop.Key, out var race)) continue;
                var pair = prop.Value?.ToObject<PreviewNpcPair>(serializer);
                if (pair == null) continue;
                result.Add(new RacePreviewEntry
                {
                    Race = race,
                    MaleNpc = pair.MaleNpc,
                    FemaleNpc = pair.FemaleNpc
                });
            }
        }
        return result;
    }

    public override void WriteJson(
        JsonWriter writer, List<RacePreviewEntry>? value, JsonSerializer serializer)
    {
        var list = value ?? new List<RacePreviewEntry>();
        var arr = new JArray();
        foreach (var entry in list)
        {
            arr.Add(JObject.FromObject(entry, serializer));
        }
        arr.WriteTo(writer);
    }
}
