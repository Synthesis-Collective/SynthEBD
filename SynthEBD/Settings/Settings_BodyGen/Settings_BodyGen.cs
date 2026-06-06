using Mutagen.Bethesda.Plugins;

namespace SynthEBD;

/// <summary>Settings POCO for the BodyGen body-shape system, persisted as JSON. Records which
/// male/female BodyGen config is active and the NPC/slider-group selections used by the previewer.</summary>
public class Settings_BodyGen
{
    /// <summary>Name of the active male BodyGen config; null when none is selected.</summary>
    public string CurrentMaleConfig { get; set; } = null;
    /// <summary>Name of the active female BodyGen config; null when none is selected.</summary>
    public string CurrentFemaleConfig { get; set; } = null;

    /// <summary>NPC used as the male preview subject in the BodyGen previewer.</summary>
    public FormKey PreviewNpcMale { get; set; } = FormKey.Null;
    /// <summary>NPC used as the female preview subject in the BodyGen previewer.</summary>
    public FormKey PreviewNpcFemale { get; set; } = FormKey.Null;
    /// <summary>Slider group last previewed for the male subject.</summary>
    public string PreviewSliderGroupMale { get; set; } = "";
    /// <summary>Slider group last previewed for the female subject.</summary>
    public string PreviewSliderGroupFemale { get; set; } = "";
}