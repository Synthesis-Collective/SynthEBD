using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;

namespace SynthEBD;

/// <summary>An item that carries a relative probability weight for weighted random selection.</summary>
public interface IProbabilityWeighted
{
    /// <summary>Relative weight of this item in a weighted random draw.</summary>
    double ProbabilityWeighting { get; set; }
}

/// <summary>
/// Weighted random selection helpers plus computation of multiplicative probability-modifier factors
/// from attribute-conditioned <see cref="AttributeWeightModifier"/> rules.
/// </summary>
public class ProbabilityWeighting
{
    /// <summary>
    /// Selects one item from <paramref name="inputs"/> with probability proportional to each item's
    /// <see cref="IProbabilityWeighted.ProbabilityWeighting"/>. Returns null when the sequence is empty.
    /// </summary>
    public static IProbabilityWeighted SelectByProbability(IEnumerable<IProbabilityWeighted> inputs)
    {
        if (!inputs.Any()) { return null; }

        double totalWeight = inputs.Sum(x => x.ProbabilityWeighting);
        var randomCap = new Random().NextDouble() * totalWeight;

        double currentWeight = 0;
        foreach (var input in inputs)
        {
            currentWeight += input.ProbabilityWeighting;
            if (currentWeight >= randomCap)
            {
                return input;
            }
        }

        // function should always return by this point. Leaving the original (used for ints) below as a fallback

        var inputList = inputs.ToList();

        HashSet<int> weightedSet = new HashSet<int>();
        for (int i = 0; i < inputList.Count; i++)
        {
            for (int j = 0; j < inputList[i].ProbabilityWeighting; j++)
            {
                weightedSet.Add(i);
            }
        }

        return inputList[new Random().Next(weightedSet.Count)];
    }
    
    /// <summary>
    /// Generic weighted random selection: picks one element of <paramref name="inputs"/> with probability
    /// proportional to <paramref name="weightSelector"/>. Returns <c>default(T)</c> for a null/empty sequence,
    /// and falls back to the last element if rounding leaves none selected.
    /// </summary>
    public static T SelectByProbability<T>(IEnumerable<T> inputs, Func<T, double> weightSelector)
    {
        if (inputs == null || !inputs.Any())
        {
            return default(T);
        }

        double totalWeight = inputs.Sum(weightSelector);
        Random random = new Random();
        double randomThreshold = random.NextDouble() * totalWeight;

        double cumulativeWeight = 0;
        foreach (var input in inputs)
        {
            cumulativeWeight += weightSelector(input);
            if (cumulativeWeight >= randomThreshold)
            {
                return input;
            }
        }

        // Fallback if due to rounding no element was returned
        return inputs.Last();
    }

    /// <summary>
    /// Computes the multiplicative probability-modifier factor for a carrier: the product of the
    /// <see cref="AttributeWeightModifier.Factor"/> of every modifier whose attribute condition is
    /// satisfied (per <paramref name="npcMatches"/>). Returns 1.0 when the list is null/empty or
    /// nothing matches, so callers can multiply the base <see cref="IProbabilityWeighted.ProbabilityWeighting"/>
    /// unconditionally. Blank conditions (no sub-attributes) are skipped. This predicate-based
    /// overload exists so the multiplicative math is unit-testable without a live Mutagen environment.
    /// </summary>
    public static double GetProbabilityModifierFactor(IEnumerable<AttributeWeightModifier>? modifiers, Func<NPCAttribute, bool> npcMatches)
    {
        if (modifiers == null) { return 1.0; }
        double factor = 1.0;
        foreach (var modifier in modifiers)
        {
            if (modifier?.Attribute == null || modifier.Attribute.SubAttributes.Count == 0) { continue; } // blank condition = no-op
            if (npcMatches(modifier.Attribute)) { factor *= modifier.Factor; }
        }
        return factor;
    }

    /// <summary>
    /// Production overload of <see cref="GetProbabilityModifierFactor(IEnumerable{AttributeWeightModifier}, Func{NPCAttribute, bool})"/>.
    /// Each modifier's single attribute is tested against the NPC via <see cref="AttributeMatcher"/>
    /// (treated as a pure restriction, exactly like a Disallowed-attribute match test). When verbose
    /// detailed-attribute logging is enabled and a <paramref name="logger"/> is supplied, each applied
    /// factor is written to the NPC report.
    /// </summary>
    public static double GetProbabilityModifierFactor(IEnumerable<AttributeWeightModifier>? modifiers, INpcGetter npc, FormKey? raceOverride, HashSet<AttributeGroup> attributeGroups, AttributeMatcher attributeMatcher, bool detailedLogging, Logger logger = null, NPCInfo npcInfoForLog = null, string itemName = null)
    {
        if (modifiers == null) { return 1.0; }
        double factor = 1.0;
        foreach (var modifier in modifiers)
        {
            if (modifier?.Attribute == null || modifier.Attribute.SubAttributes.Count == 0) { continue; } // blank condition = no-op
            attributeMatcher.MatchNPCtoAttributeList(new HashSet<NPCAttribute> { modifier.Attribute }, npc, raceOverride, attributeGroups, detailedLogging, out bool hasAttributeRestrictions, out bool matchesAttributeRestrictions, out int _, out string matchLog, out string _, out string _, null);
            if (hasAttributeRestrictions && matchesAttributeRestrictions)
            {
                factor *= modifier.Factor;
                if (logger != null && detailedLogging)
                {
                    logger.LogReport((itemName ?? "Item") + ": probability weight x" + modifier.Factor + " (NPC matched probability modifier" + (string.IsNullOrWhiteSpace(matchLog) ? "" : ": " + matchLog.Trim()) + ")", false, npcInfoForLog);
                }
            }
        }
        return factor;
    }
}