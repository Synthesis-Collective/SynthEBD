using System.Collections.Generic;

namespace CharacterViewer.Rendering;

/// <summary>
/// Neutral morph-application payload that <see cref="BodySlideDeformer"/> consumes
/// in place of a host-coupled BodySlide preset class. Hosts (SynthEBD's
/// <c>BodySlideSetting</c>, NPC2's equivalent) translate from their own
/// preset model into this POCO at the call site, so the deformer never sees a
/// host-specific type.
///
/// Shape filtering (Body / Hands / Feet / etc.) happens in the deformer via the
/// <c>shapeName</c> parameter to <see cref="BodySlideDeformer.ApplyDeformation"/>;
/// the slider values themselves are flat across the whole NPC, matching how
/// BodySlide presets are authored.
/// </summary>
public sealed class MorphSet
{
    /// <summary>Human-readable preset label, used only in diagnostic log lines.</summary>
    public string Label { get; init; } = "";

    /// <summary>Slider data name → Big/Small weights (0–100 scale, matches
    /// BodySlide's authoring convention).</summary>
    public IReadOnlyDictionary<string, MorphSlider> Sliders { get; init; }
        = new Dictionary<string, MorphSlider>();
}

/// <summary>One slider's Big/Small weight pair, on a 0–100 scale. The deformer
/// interpolates between Big (NPC weight 100) and Small (NPC weight 0).</summary>
public readonly record struct MorphSlider(float Big, float Small);
