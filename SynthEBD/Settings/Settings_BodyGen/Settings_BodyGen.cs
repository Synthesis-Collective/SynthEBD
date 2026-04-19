using Mutagen.Bethesda.Plugins;

namespace SynthEBD;

public class Settings_BodyGen
{
    public string CurrentMaleConfig { get; set; } = null;
    public string CurrentFemaleConfig { get; set; } = null;

    public FormKey PreviewNpcMale { get; set; } = FormKey.Null;
    public FormKey PreviewNpcFemale { get; set; } = FormKey.Null;
    public string PreviewSliderGroupMale { get; set; } = "";
    public string PreviewSliderGroupFemale { get; set; } = "";
}