using System;
using System.Collections.Generic;
using System.Linq;

namespace SynthEBD;

/// <summary>
/// Helpers for reading <see cref="BodySlideSetting.BodyShapeDescriptorsByWeight"/> with an NPC weight
/// in hand. Picks the weight slot closest to the NPC's weight; ties round down (the lower slot key
/// wins). If the closest slot is empty, walks outward to the next non-empty slot.
///
/// Used by all patcher consumers that have NPC context (OBody selection, HeadPart descriptor checks,
/// Asset subgroup descriptor checks). UI-only consumers (filters, trainer export, exchange) keep
/// using <see cref="BodySlideSettingExtensions.GetDescriptorUnion"/> since they have no NPC weight.
/// </summary>
public static class PerWeightDescriptorLookup
{
    /// <summary>
    /// Returns the descriptor set from the weight slot closest to <paramref name="npcWeight"/>.
    /// If that slot is empty, the next-closest non-empty slot is used. Returns an empty set if
    /// the preset has no populated slots.
    /// </summary>
    public static HashSet<AnnotatedDescriptorSignature> GetDescriptorsForWeight(BodySlideSetting preset, float npcWeight)
    {
        var result = new HashSet<AnnotatedDescriptorSignature>();
        if (preset?.BodyShapeDescriptorsByWeight == null || preset.BodyShapeDescriptorsByWeight.Count == 0)
        {
            return result;
        }

        // Primary sort: distance to NPC weight. Tie-breaker: lower slot key wins (rounds down).
        var ordered = preset.BodyShapeDescriptorsByWeight
            .OrderBy(kvp => Math.Abs(kvp.Key - npcWeight))
            .ThenBy(kvp => kvp.Key);

        foreach (var kvp in ordered)
        {
            if (kvp.Value != null && kvp.Value.Count > 0)
            {
                foreach (var d in kvp.Value)
                {
                    result.Add(d);
                }
                return result;
            }
        }

        return result;
    }
}
