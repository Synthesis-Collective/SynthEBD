using Mutagen.Bethesda.Plugins;
using Newtonsoft.Json;

namespace SynthEBD;

public class BodyShapeDescriptorRules
{
    public string DescriptorSignature { get; set; } = "";
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

    [JsonIgnore]
    public int MatchedForceIfCount { get; set; } = 0;

    public static bool NPCisValid(BodyShapeDescriptor descriptor, NPCInfo npcInfo, out string reportStr)
    {
        reportStr = "";
        // Allow unique NPCs
        if (!descriptor.AssociatedRules.AllowUnique && npcInfo.NPC.Configuration.Flags.HasFlag(Mutagen.Bethesda.Skyrim.NpcConfiguration.Flag.Unique))
        {
            reportStr = descriptor.Signature + " disallow unique NPCs";
            return false;
        }

        // Allow non-unique NPCs
        if (!descriptor.AssociatedRules.AllowNonUnique && !npcInfo.NPC.Configuration.Flags.HasFlag(Mutagen.Bethesda.Skyrim.NpcConfiguration.Flag.Unique))
        {
            reportStr = descriptor.Signature + " disallow non-unique NPCs";
            return false;
        }

        // Allowed Races
        if (descriptor.AssociatedRules.AllowedRaces.Any() && !descriptor.AssociatedRules.AllowedRaces.Contains(npcInfo.BodyShapeRace))
        {
            reportStr = descriptor.Signature + " have Allowed Races that do not include the current NPC's race";
            return false;
        }

        // Disallowed Races
        if (descriptor.AssociatedRules.DisallowedRaces.Contains(npcInfo.BodyShapeRace))
        {
            reportStr = descriptor.Signature + " have Disallowed Races that include the current NPC's race";
            return false;
        }

        // Weight Range
        if (npcInfo.NPC.Weight < descriptor.AssociatedRules.WeightRange.Lower || npcInfo.NPC.Weight > descriptor.AssociatedRules.WeightRange.Upper)
        {
            reportStr = descriptor.Signature + " specify that the current NPC's weight falls outside of the allowed weight range";
            return false;
        }

        // Allowed and Forced Attributes
        descriptor.AssociatedRules.MatchedForceIfCount = 0;
        AttributeMatcher.MatchNPCtoAttributeList(descriptor.AssociatedRules.AllowedAttributes, npcInfo.NPC, out bool hasAttributeRestrictions, out bool matchesAttributeRestrictions, out int matchedForceIfWeightedCount, out string _, out string unmatchedLog, out string forceIfLog);
        if (hasAttributeRestrictions && !matchesAttributeRestrictions)
        {
            reportStr = descriptor.Signature + " have the following allowed attributes of which none are matched to the NPC: " + unmatchedLog;
            return false;
        }
        else
        {
            descriptor.AssociatedRules.MatchedForceIfCount = matchedForceIfWeightedCount;
        }

        if (descriptor.AssociatedRules.MatchedForceIfCount > 0)
        {
            reportStr = "Current NPC matches the following forced attributes from Body Shape Descriptor " + descriptor.Signature.ToString() + ": " + forceIfLog;
        }

        // Disallowed Attributes
        AttributeMatcher.MatchNPCtoAttributeList(descriptor.AssociatedRules.DisallowedAttributes, npcInfo.NPC, out hasAttributeRestrictions, out matchesAttributeRestrictions, out int dummy, out string matchLog, out string _, out string _);
        if (hasAttributeRestrictions && matchesAttributeRestrictions)
        {
            reportStr = descriptor.Signature + " is invalid because the NPC matches one of its disallowed attributes: " + matchLog;
            return false;
        }

        return true;
    }
}

public interface IHasDescriptorRules
{
    public HashSet<BodyShapeDescriptorRules> DescriptorRules { get; set; }
}