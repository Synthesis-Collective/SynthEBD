using System.Collections.Generic;

namespace SynthEBD;

/// <summary>
/// Neutral morph-application payload that the viewer rendering tier accepts in
/// place of SynthEBD's host-coupled <see cref="BodySlideSetting"/>. Hosts package
/// up the per-shape slider weights from their own world before handing off, so
/// the viewer never sees a SynthEBD or NPC2-specific BodySlide model.
/// </summary>
public sealed class MorphSet
{
    public string Name { get; init; } = "";

    /// <summary>Per-shape slider value sets — keys are NIF shape names, values
    /// are the slider name + Big/Small weights to feed
    /// <see cref="BodySlideDeformer.ApplyDeformation"/>.</summary>
    public Dictionary<string, IReadOnlyList<MorphSlider>> ShapeSliders { get; init; } = new();
}

public readonly record struct MorphSlider(string Name, float Big, float Small);
