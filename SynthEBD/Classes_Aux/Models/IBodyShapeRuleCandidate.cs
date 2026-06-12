using Mutagen.Bethesda.Plugins;

namespace SynthEBD;

/// <summary>
/// Common distribution-rule surface of body-shape candidates (<see cref="BodyGenConfig.BodyGenTemplate"/>
/// morphs and <see cref="BodySlideSetting"/> presets), letting <see cref="BodyShapeCandidateValidator"/>
/// validate both axes with a single rule battery (R18).
/// </summary>
public interface IBodyShapeRuleCandidate
{
    string Label { get; }
    bool AllowUnique { get; }
    bool AllowNonUnique { get; }
    bool AllowRandom { get; }
    NPCWeightRange WeightRange { get; }
    HashSet<FormKey> AllowedRaces { get; }
    HashSet<FormKey> DisallowedRaces { get; }
    HashSet<NPCAttribute> AllowedAttributes { get; }
    HashSet<NPCAttribute> DisallowedAttributes { get; }

    /// <summary>
    /// The descriptor labels to validate for this candidate. BodySlide presets return only the descriptors
    /// annotated at the NPC's weight slot (so per-weight presets aren't spuriously rejected by rules that
    /// only apply to the other slot); BodyGen morphs return all of their descriptors regardless of weight.
    /// </summary>
    HashSet<BodyShapeDescriptor.LabelSignature> GetDescriptorsForValidation(float npcWeight);
}
