using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Diagnostics;

namespace SynthEBD;

/// <summary>Persistence wrapper that groups a category's worth of <see cref="BodyShapeDescriptor"/>
/// values under one <see cref="Category"/> + <see cref="CategoryDescription"/> pair. Replaces the
/// pre-2026 flat <c>HashSet&lt;BodyShapeDescriptor&gt;</c> shape, where every value entry redundantly
/// carried <c>CategoryDescription</c>.
/// <para>The in-memory <see cref="BodyShapeDescriptor"/> entries still carry their full
/// <see cref="BodyShapeDescriptor.LabelSignature"/> Category+Value, so existing lookup helpers
/// (<see cref="BodyShapeDescriptor.LabelSignature.MapsTo"/>, etc.) keep working unchanged — the
/// flatten extension in <see cref="BodyShapeDescriptorShellExtensions"/> hands a normalized
/// enumerable to consumers that don't care about the grouping.</para>
/// <para>Backwards compatibility: the <see cref="BodyShapeDescriptorShellListConverter"/> attached
/// to the field where shells are serialized detects the old flat shape on read and migrates it
/// into shells. Old saved files load without manual migration.</para></summary>
[DebuggerDisplay("{Category} ({Descriptors.Count} values)")]
public class BodyShapeDescriptorShell : IHasLabel
{
    public string Category { get; set; } = "";
    public string CategoryDescription { get; set; } = "";
    public List<BodyShapeDescriptor> Descriptors { get; set; } = new();

    /// <summary>When true this category is a <b>rules-only</b> ("pseudo-descriptor") category: it stays
    /// fully available to the Body Type Profile / Label by Measurements editor — its rules appear in the
    /// Rules tree and its values can be picked in <c>DescriptorRef</c> conditions — but it is hidden from
    /// the distribution-facing pickers (<see cref="VM_BodyShapeDescriptorSelectionMenu"/>), so asset-pack
    /// subgroups and BodyGen templates never see it.
    /// <para>The point is intermediate categories that exist only to feed other rules. A classifier rule
    /// can gate on <c>[ShoulderWidth:Wide]</c> without <c>ShoulderWidth</c> cluttering the descriptor list
    /// a config author picks from. Before this flag the only way to get that was to omit the category from
    /// <c>TemplateDescriptors</c> entirely, which still evaluates correctly but <i>orphans</i> the rules —
    /// <c>RebuildRuleTree</c> only builds nodes for catalog-backed pairs, so they become invisible and
    /// unreachable in the editor.</para>
    /// <para>Defaults to false, so existing settings deserialize with every category distribution-facing
    /// exactly as before.</para></summary>
    public bool IsRulesOnly { get; set; } = false;

    /// <summary>IHasLabel implementation so the existing duplicate-detection plumbing
    /// (<see cref="OnLoadValidator.CheckGroupDuplicates"/>) can dedupe shells by Category.</summary>
    [JsonIgnore]
    public string Label
    {
        get => Category ?? "";
        set => Category = value ?? "";
    }
}

public static class BodyShapeDescriptorShellExtensions
{
    /// <summary>Iterates every <see cref="BodyShapeDescriptor"/> across every shell as a single
    /// flat sequence. Stamps each yielded descriptor's <c>ID.Category</c> from the shell so
    /// consumers that read it (lookup helpers, validators, the patcher) see a consistent view
    /// even if a descriptor's stored Category drifted from the shell's.
    /// <para>The shell is the source of truth for Category. If you need to write Category, mutate
    /// the shell — don't write to a yielded descriptor's <c>ID.Category</c>, as the next flatten
    /// call will overwrite it.</para></summary>
    public static IEnumerable<BodyShapeDescriptor> Flatten(this IEnumerable<BodyShapeDescriptorShell> shells)
    {
        if (shells == null) yield break;
        foreach (var shell in shells)
        {
            if (shell?.Descriptors == null) continue;
            foreach (var d in shell.Descriptors)
            {
                if (d == null) continue;
                if (d.ID == null) d.ID = new BodyShapeDescriptor.LabelSignature();
                d.ID.Category = shell.Category ?? "";
                yield return d;
            }
        }
    }

    /// <summary>Wraps a flat sequence of descriptors into shells grouped by <c>ID.Category</c>.
    /// Used by the legacy-format JSON converter and by migration code that needs to bridge the
    /// pre-2026 flat collection shape into the new grouped one. The first encountered descriptor
    /// per category seeds <see cref="BodyShapeDescriptorShell.CategoryDescription"/> from its
    /// pre-migration <c>CategoryDescription</c> field (preserved on the legacy converter path).</summary>
    public static List<BodyShapeDescriptorShell> ToShells(
        this IEnumerable<BodyShapeDescriptor> descriptors,
        IReadOnlyDictionary<string, string>? legacyCategoryDescriptions = null)
    {
        var result = new List<BodyShapeDescriptorShell>();
        if (descriptors == null) return result;
        var byCat = new Dictionary<string, BodyShapeDescriptorShell>(StringComparer.Ordinal);
        foreach (var d in descriptors)
        {
            if (d?.ID == null) continue;
            var cat = d.ID.Category ?? "";
            if (!byCat.TryGetValue(cat, out var shell))
            {
                shell = new BodyShapeDescriptorShell { Category = cat };
                if (legacyCategoryDescriptions != null
                    && legacyCategoryDescriptions.TryGetValue(cat, out var desc)
                    && !string.IsNullOrEmpty(desc))
                {
                    shell.CategoryDescription = desc;
                }
                byCat[cat] = shell;
                result.Add(shell);
            }
            shell.Descriptors.Add(d);
        }
        return result;
    }
}

/// <summary>Newtonsoft converter that lets <c>List&lt;BodyShapeDescriptorShell&gt;</c> fields
/// transparently load both the new grouped JSON shape (<c>[{Category, CategoryDescription,
/// Descriptors: [...]}, ...]</c>) and the legacy flat shape (<c>[{ID: {...}, CategoryDescription,
/// ValueDescription, AssociatedRules}, ...]</c>). Always writes the new shape.
/// <para>Detection is by property name: the legacy entries have a top-level "ID" property; the
/// new entries have "Descriptors". Anything else falls through to the default deserializer, which
/// produces an empty shell list (the safe default — same behavior as the pre-refactor empty
/// HashSet).</para></summary>
public class BodyShapeDescriptorShellListConverter : JsonConverter
{
    /// <summary>Handles only <c>List&lt;BodyShapeDescriptorShell&gt;</c>.</summary>
    public override bool CanConvert(System.Type objectType)
        => objectType == typeof(List<BodyShapeDescriptorShell>);

    /// <summary>Reads either the new grouped shell shape or the legacy flat descriptor shape (detected by an "ID" vs "Descriptors" property), migrating legacy data into shells.</summary>
    public override object? ReadJson(JsonReader reader, System.Type objectType, object? existingValue, JsonSerializer serializer)
    {
        if (reader.TokenType == JsonToken.Null) return new List<BodyShapeDescriptorShell>();

        var arr = JArray.Load(reader);
        if (arr.Count == 0) return new List<BodyShapeDescriptorShell>();

        // Probe the first element. New format → "Descriptors" property. Legacy flat → "ID".
        var first = arr[0] as JObject;
        bool legacy = first != null && first.Property("ID") != null && first.Property("Descriptors") == null;
        if (legacy)
        {
            // Deserialize each entry as a BodyShapeDescriptor, capturing the pre-migration
            // CategoryDescription so we can promote it onto the shell. The descriptor model's
            // CategoryDescription property is gone post-refactor, so we read it manually here
            // before the standard deserializer runs.
            var legacyCatDescriptions = new Dictionary<string, string>(StringComparer.Ordinal);
            var descriptors = new List<BodyShapeDescriptor>();
            foreach (var token in arr)
            {
                if (token is not JObject obj) continue;
                var cd = obj.Property("CategoryDescription")?.Value?.ToString();
                var catFromId = obj["ID"]?["Category"]?.ToString() ?? "";
                if (!string.IsNullOrEmpty(cd)
                    && !legacyCatDescriptions.ContainsKey(catFromId))
                {
                    legacyCatDescriptions[catFromId] = cd;
                }
                // Strip the legacy CategoryDescription before deserializing so Newtonsoft
                // doesn't complain about an unknown property after we remove it from the model.
                obj.Remove("CategoryDescription");
                var d = obj.ToObject<BodyShapeDescriptor>(serializer);
                if (d != null) descriptors.Add(d);
            }
            return descriptors.ToShells(legacyCatDescriptions);
        }

        // New format — defer to the default deserializer.
        var result = new List<BodyShapeDescriptorShell>();
        serializer.Populate(arr.CreateReader(), result);
        return result;
    }

    /// <summary>Writes the current grouped shell shape via the default serializer.</summary>
    public override void WriteJson(JsonWriter writer, object? value, JsonSerializer serializer)
    {
        serializer.Serialize(writer, value);
    }
}
