using Mutagen.Bethesda.Plugins;

namespace SynthEBD;

public class AdditionalRecordTemplate
{
    public HashSet<FormKey> Races { get; set; } = new();
    public FormKey TemplateNPC { get; set; } = new();
    public HashSet<string> AdditionalRacesPaths { get; set; } = new();
}