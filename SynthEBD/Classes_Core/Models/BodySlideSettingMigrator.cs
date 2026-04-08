using System;
using System.Collections.Generic;
using System.Linq;

namespace SynthEBD;

/// <summary>
/// Migrates legacy <see cref="BodySlideSetting"/> data (flat <c>BodyShapeDescriptors</c> hashset)
/// into the per-weight model (<see cref="BodySlideSetting.BodyShapeDescriptorsByWeight"/>).
///
/// Stage 1 only implements the "decline library defaults" path: each preset's legacy descriptors
/// are placed into the single default weight slot closest to the mean of <see cref="NPCWeightRange"/>
/// (ties round down). The accept-library path is implemented in stage 5 alongside the annotation
/// library loader.
/// </summary>
public class BodySlideSettingMigrator
{
    private readonly Logger _logger;

    public BodySlideSettingMigrator(Logger logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Inspects <paramref name="settings"/> and, if <see cref="Settings_OBody.SchemaVersion"/> is 0,
    /// drains any legacy descriptors into the per-weight model and bumps SchemaVersion to 1.
    /// Safe to call repeatedly.
    /// </summary>
    public void MigrateIfNeeded(Settings_OBody settings)
    {
        if (settings == null) return;
        if (settings.SchemaVersion >= 1) return;

        _logger.LogStartupEventStart("Migrating BodySlide settings to schema v1");

        var defaultSlots = (settings.DefaultWeightSlots != null && settings.DefaultWeightSlots.Count > 0)
            ? settings.DefaultWeightSlots.OrderBy(x => x).ToList()
            : new List<int> { 0, 25, 50, 75, 100 };

        int migratedPresets = 0;
        foreach (var preset in EnumerateAllPresets(settings))
        {
            if (MigrateLegacyDescriptors(preset, defaultSlots))
            {
                migratedPresets++;
            }
        }

        settings.SchemaVersion = 1;
        _logger.LogMessage("Migrated " + migratedPresets + " BodySlide preset(s) from flat descriptors to per-weight slots.");
        _logger.LogStartupEventEnd("Migrating BodySlide settings to schema v1");
    }

    private static IEnumerable<BodySlideSetting> EnumerateAllPresets(Settings_OBody settings)
    {
        if (settings.BodySlidesMale != null)
        {
            foreach (var p in settings.BodySlidesMale) yield return p;
        }
        if (settings.BodySlidesFemale != null)
        {
            foreach (var p in settings.BodySlidesFemale) yield return p;
        }
    }

    /// <summary>
    /// If <paramref name="preset"/> has legacy descriptors, place them into the default weight slot
    /// closest to the mean of its WeightRange (ties round down) and clear the legacy buffer.
    /// </summary>
    private bool MigrateLegacyDescriptors(BodySlideSetting preset, List<int> defaultSlots)
    {
        if (preset == null) return false;
        if (preset.LegacyBodyShapeDescriptors == null || preset.LegacyBodyShapeDescriptors.Count == 0)
        {
            // Ensure the new dictionary is in the canonical default-slots layout even when no legacy data is present.
            EnsureDefaultSlots(preset, defaultSlots);
            preset.LegacyBodyShapeDescriptors = null;
            return false;
        }

        EnsureDefaultSlots(preset, defaultSlots);

        int meanWeight = ComputeMeanWeight(preset.WeightRange);
        int targetSlot = ChooseClosestSlot(defaultSlots, meanWeight);

        if (!preset.BodyShapeDescriptorsByWeight.TryGetValue(targetSlot, out var slotSet))
        {
            slotSet = new HashSet<AnnotatedDescriptorSignature>();
            preset.BodyShapeDescriptorsByWeight[targetSlot] = slotSet;
        }

        foreach (var legacy in preset.LegacyBodyShapeDescriptors)
        {
            // Force-source as Manual: the migrator decline path treats every legacy entry as user-authored
            // since the rule engine output was indistinguishable from manual entries on disk.
            slotSet.Add(new AnnotatedDescriptorSignature(legacy.ToLabelSignature(), BodyShapeAnnotationSource.Manual));
        }

        preset.LegacyBodyShapeDescriptors = null;
        return true;
    }

    private static void EnsureDefaultSlots(BodySlideSetting preset, List<int> defaultSlots)
    {
        preset.BodyShapeDescriptorsByWeight ??= new Dictionary<int, HashSet<AnnotatedDescriptorSignature>>();
        foreach (var slot in defaultSlots)
        {
            if (preset.RemovedDefaultWeightSlots != null && preset.RemovedDefaultWeightSlots.Contains(slot))
            {
                continue;
            }
            if (!preset.BodyShapeDescriptorsByWeight.ContainsKey(slot))
            {
                preset.BodyShapeDescriptorsByWeight[slot] = new HashSet<AnnotatedDescriptorSignature>();
            }
        }
    }

    private static int ComputeMeanWeight(NPCWeightRange range)
    {
        if (range == null) return 50;
        return (range.Lower + range.Upper) / 2;
    }

    /// <summary>
    /// Returns the slot whose value is closest to <paramref name="weight"/>; ties round down.
    /// </summary>
    public static int ChooseClosestSlot(IReadOnlyList<int> slots, int weight)
    {
        if (slots == null || slots.Count == 0)
        {
            return weight;
        }

        int best = slots[0];
        int bestDistance = Math.Abs(slots[0] - weight);
        for (int i = 1; i < slots.Count; i++)
        {
            int candidate = slots[i];
            int distance = Math.Abs(candidate - weight);
            if (distance < bestDistance || (distance == bestDistance && candidate < best))
            {
                best = candidate;
                bestDistance = distance;
            }
        }
        return best;
    }
}
