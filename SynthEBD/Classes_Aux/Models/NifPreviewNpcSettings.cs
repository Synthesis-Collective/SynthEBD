using Mutagen.Bethesda.Plugins;

namespace SynthEBD;

public class NifPreviewNpcSettings
{
    public Dictionary<FormKey, PreviewNpcPair> RacePreviewNpcs { get; set; } = new();
    public PreviewNpcPair DefaultNpcs { get; set; } = new();
}

public class PreviewNpcPair
{
    public FormKey MaleNpc { get; set; } = FormKey.Null;
    public FormKey FemaleNpc { get; set; } = FormKey.Null;
}
