namespace SynthEBD;

/// <summary>
/// Pairs a single NPC attribute condition with a multiplicative probability factor. When the NPC
/// being evaluated matches <see cref="Attribute"/>, the carrying item's selection weight is scaled
/// by <see cref="Factor"/>. Multiple matching modifiers on one item stack multiplicatively.
/// See <see cref="ProbabilityWeighting.GetProbabilityModifierFactor(System.Collections.Generic.IEnumerable{AttributeWeightModifier}, System.Func{NPCAttribute, bool})"/>
/// for the evaluation logic, which is shared by every <see cref="IProbabilityWeighted"/> selection site.
/// </summary>
public class AttributeWeightModifier
{
    public NPCAttribute Attribute { get; set; } = new();
    public double Factor { get; set; } = 1.0; // multiplicative identity; a blank or unmatched modifier is a no-op

    /// <summary>Deep-clones a modifier, cloning its inner attribute, for safe duplication.</summary>
    /// <param name="input">The modifier to clone.</param>
    /// <returns>A new, independent <see cref="AttributeWeightModifier"/>.</returns>
    public static AttributeWeightModifier CloneAsNew(AttributeWeightModifier input) => new()
    {
        Attribute = NPCAttribute.CloneAsNew(input.Attribute),
        Factor = input.Factor
    };
}
