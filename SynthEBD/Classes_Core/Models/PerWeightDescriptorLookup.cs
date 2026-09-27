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

    /// <summary>
    /// The slot keyed exactly at <paramref name="weight"/> when the preset has one (an existing-but-empty
    /// slot counts: it means "nothing assigned here"), otherwise the nearest slot by key with ties rounding
    /// down. Unlike <see cref="GetDescriptorsForWeight"/> this never walks outward to a non-empty slot --
    /// callers asking "what is true at this weight" must not borrow another weight's labels. Null when the
    /// preset has no slots.
    /// </summary>
    public static HashSet<AnnotatedDescriptorSignature>? GetSlotAtOrNearest(BodySlideSetting? preset, int weight)
    {
        var slots = preset?.BodyShapeDescriptorsByWeight;
        if (slots == null || slots.Count == 0) return null;
        if (slots.TryGetValue(weight, out var exact) && exact != null) return exact;

        int bestKey = 0;
        int bestDist = int.MaxValue;
        foreach (var key in slots.Keys)
        {
            int dist = Math.Abs(key - weight);
            if (dist < bestDist || (dist == bestDist && key < bestKey))
            {
                bestDist = dist;
                bestKey = key;
            }
        }
        return slots[bestKey];
    }

    /// <summary>True when <paramref name="slot"/> holds a Manual descriptor in <paramref name="category"/>.
    /// A manual label is per (weight slot, category): it overrules every rule-derived suggestion for that
    /// category in that slot only, so a manual label at weight 0 leaves weight 100 to the rules.</summary>
    public static bool HasManualInCategory(IEnumerable<AnnotatedDescriptorSignature>? slot, string category)
        => slot != null && slot.Any(x => x != null && x.Source == BodyShapeAnnotationSource.Manual
                                         && string.Equals(x.Category, category, StringComparison.Ordinal));
}
