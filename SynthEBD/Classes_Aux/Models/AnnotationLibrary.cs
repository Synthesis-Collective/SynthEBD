using System.Collections.Generic;

namespace SynthEBD;

/// <summary>
/// A single entry in an annotation library: the canonical preset name and its
/// per-weight descriptor assignments.
/// </summary>
public class AnnotationLibraryEntry
{
    /// <summary>
    /// Matched case-insensitively against <see cref="BodySlideSetting.ReferencedBodySlide"/>.
    /// </summary>
    public string PresetName { get; set; } = "";

    /// <summary>
    /// Keys are integer NPC weight values (0-100). Each value is the list of
    /// descriptors that apply when an NPC's weight is closest to that key.
    /// Keys do not need to match <see cref="Settings_OBody.DefaultWeightSlots"/>; the
    /// annotator will add non-standard slots to the preset as needed.
    /// </summary>
    public Dictionary<int, List<BodyShapeDescriptor.LabelSignature>> DescriptorsByWeight { get; set; } = new();
}

/// <summary>
/// The complete annotation library for one body type.
/// Serialized as <c>InternalData/BodySlideAnnotationLibraries/{BodyType}.json</c>.
/// </summary>
public class AnnotationLibrary
{
    /// <summary>Body type this library covers (e.g. "HIMBO", "CBBE").</summary>
    public string BodyType { get; set; } = "";

    public List<AnnotationLibraryEntry> Entries { get; set; } = new();
}
