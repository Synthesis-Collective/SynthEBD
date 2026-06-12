using Mutagen.Bethesda.Plugins;
using Newtonsoft.Json;

namespace SynthEBD;

/// <summary>
/// The allow/disallow rules attached to a body-shape descriptor (or other rule-bearing item): race and
/// race-grouping filters, attribute filters, unique/non-unique flags, a weight range, and a probability
/// weight. <see cref="NPCisValid"/> evaluates an NPC against these rules.
/// </summary>
public class BodyShapeDescriptorRules
{
    public HashSet<FormKey> AllowedRaces { get; set; } = new();
    public HashSet<FormKey> DisallowedRaces { get; set; } = new();
    public HashSet<string> AllowedRaceGroupings { get; set; } = new();
    public HashSet<string> DisallowedRaceGroupings { get; set; } = new();
    public HashSet<NPCAttribute> AllowedAttributes { get; set; } = new(); // keeping as array to allow deserialization of original zEBD settings files
    public HashSet<NPCAttribute> DisallowedAttributes { get; set; } = new();
    public bool AllowUnique { get; set; } = true;
    public bool AllowNonUnique { get; set; } = true;
    public bool AllowRandom { get; set; } = true;
    public double ProbabilityWeighting { get; set; } = 1;
    public NPCWeightRange WeightRange { get; set; } = new() { Lower = 0, Upper = 100 };

    /// <summary>Evaluates whether an NPC satisfies a descriptor's rules (unique/non-unique, allowed/disallowed races, weight range, allowed/forced and disallowed attributes).</summary>
    /// <param name="descriptor">The descriptor whose <see cref="BodyShapeDescriptorRules"/> are evaluated.</param>
    /// <param name="attributeGroups">Named attribute groups referenced by the rules.</param>
    /// <param name="npcInfo">The NPC under evaluation.</param>
    /// <param name="attMatcher">Matcher used to evaluate attribute rules.</param>
    /// <param name="bDetailedAttributeLogging">When true, produces verbose attribute-match logs.</param>
    /// <param name="reportStr">Receives a human-readable reason for rejection, or a matched-forced-attribute note.</param>
    /// <param name="matchedForceIfCount">The NPC's matched ForceIf attribute weight for these rules (0 on rejection), which downstream code uses to bias selection.</param>
    /// <returns><c>true</c> if the NPC is allowed by the rules.</returns>
    public bool NPCisValid(BodyShapeDescriptor descriptor, HashSet<AttributeGroup> attributeGroups, NPCInfo npcInfo, AttributeMatcher attMatcher, bool bDetailedAttributeLogging, out string reportStr, out int matchedForceIfCount)
    {
        reportStr = "";
        matchedForceIfCount = 0;
        // Allow unique NPCs
        if (!descriptor.AssociatedRules.AllowUnique && npcInfo.NPC.Configuration.Flags.HasFlag(Mutagen.Bethesda.Skyrim.NpcConfiguration.Flag.Unique))
        {
            reportStr = descriptor.ID + " disallow unique NPCs";
            return false;
        }

        // Allow non-unique NPCs
        if (!descriptor.AssociatedRules.AllowNonUnique && !npcInfo.NPC.Configuration.Flags.HasFlag(Mutagen.Bethesda.Skyrim.NpcConfiguration.Flag.Unique))
        {
            reportStr = descriptor.ID + " disallow non-unique NPCs";
            return false;
        }

        // Allowed Races
        if (descriptor.AssociatedRules.AllowedRaces.Any() && !descriptor.AssociatedRules.AllowedRaces.Contains(npcInfo.BodyShapeRace))
        {
            reportStr = descriptor.ID + " have Allowed Races that do not include the current NPC's race";
            return false;
        }

        // Disallowed Races
        if (descriptor.AssociatedRules.DisallowedRaces.Contains(npcInfo.BodyShapeRace))
        {
            reportStr = descriptor.ID + " have Disallowed Races that include the current NPC's race";
            return false;
        }

        // Weight Range
        if (npcInfo.NPC.Weight < descriptor.AssociatedRules.WeightRange.Lower || npcInfo.NPC.Weight > descriptor.AssociatedRules.WeightRange.Upper)
        {
            reportStr = descriptor.ID + " specify that the current NPC's weight falls outside of the allowed weight range";
            return false;
        }

        // Allowed and Forced Attributes
        attMatcher.MatchNPCtoAttributeList(descriptor.AssociatedRules.AllowedAttributes, npcInfo.NPC, npcInfo.BodyShapeRace, attributeGroups, bDetailedAttributeLogging, out bool hasAttributeRestrictions, out bool matchesAttributeRestrictions, out int matchedForceIfWeightedCount, out string _, out string unmatchedLog, out string forceIfLog, null);
        if (hasAttributeRestrictions && !matchesAttributeRestrictions)
        {
            reportStr = descriptor.ID + " have the following allowed attributes of which none are matched to the NPC: " + unmatchedLog;
            return false;
        }
        else
        {
            matchedForceIfCount = matchedForceIfWeightedCount;
        }

        if (matchedForceIfCount > 0)
        {
            reportStr = "Current NPC matches the following forced attributes from Body Shape Descriptor " + descriptor.ID.ToString() + ": " + forceIfLog;
        }

        // Disallowed Attributes
        attMatcher.MatchNPCtoAttributeList(descriptor.AssociatedRules.DisallowedAttributes, npcInfo.NPC, npcInfo.BodyShapeRace, attributeGroups, bDetailedAttributeLogging, out hasAttributeRestrictions, out matchesAttributeRestrictions, out int dummy, out string matchLog, out string _, out string _, null);
        if (hasAttributeRestrictions && matchesAttributeRestrictions)
        {
            reportStr = descriptor.ID + " is invalid because the NPC matches one of its disallowed attributes: " + matchLog;
            return false;
        }

        return true;
    }
}

/// <summary>Implemented by items that own a set of <see cref="BodyShapeDescriptorRules"/>.</summary>
public interface IHasDescriptorRules
{
    /// <summary>The descriptor rule sets attached to this item.</summary>
    public HashSet<BodyShapeDescriptorRules> DescriptorRules { get; set; }
}