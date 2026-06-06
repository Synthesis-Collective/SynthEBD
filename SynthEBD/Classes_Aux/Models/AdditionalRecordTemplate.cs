using Mutagen.Bethesda.Plugins;

namespace SynthEBD;

/// <summary>
/// Maps a set of races to a template NPC whose records seed generated records (plus optional extra
/// race-link paths). Used by asset packs that need race-specific record templates beyond the default.
/// </summary>
public class AdditionalRecordTemplate
{
    public HashSet<FormKey> Races { get; set; } = new();
    public FormKey TemplateNPC { get; set; } = new();
    public HashSet<string> AdditionalRacesPaths { get; set; } = new();
}