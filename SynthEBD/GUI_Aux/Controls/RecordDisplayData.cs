using Mutagen.Bethesda.Plugins;

namespace SynthEBD;

/// <summary>
/// Lightweight display cache entry for one major record: just the fields the named FormKey pickers
/// show. Deliberately does NOT hold the record getter (caching getters for a whole load order is
/// prohibitively heavy — the lesson from NPC Plugin Chooser 2's NpcDisplayData).
/// </summary>
public sealed record RecordDisplayData(FormKey FormKey, string? Name, string? EditorID)
{
    /// <summary>Primary display label: Name, falling back to EditorID, falling back to the FormKey.</summary>
    public string DisplayString =>
        !string.IsNullOrWhiteSpace(Name) ? Name
        : !string.IsNullOrWhiteSpace(EditorID) ? EditorID
        : FormKey.ToString();

    /// <summary>Verbose label for suggestion lists: every known identifier, so users can match on any.</summary>
    public string ListString
    {
        get
        {
            string label = DisplayString;
            if (!string.IsNullOrWhiteSpace(Name) && !string.IsNullOrWhiteSpace(EditorID))
            {
                label += " (" + EditorID + ")";
            }
            return label + "  [" + FormKey + "]";
        }
    }
}
