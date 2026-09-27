namespace SynthEBD;

/// <summary>
/// Turns Label-then-suggest worklist verdicts (<see cref="PresetAnnotation"/>) into real Manual
/// descriptors on the presets' weight slots.
///
/// <para>Annotations and preset descriptors are deliberately separate stores: annotations are the
/// training data the Suggest Measurements / Suggest Rules passes read, and recording one never touches
/// the preset. Applying is an explicit user action, split into <see cref="Plan"/> (pure, so the UI can
/// show what would change and ask before replacing existing manual values) and <see cref="Execute"/>.</para>
///
/// <para>Semantics, per (preset, weight slot, category) the annotation covers: the slot's descriptors in
/// that category -- from any source -- are replaced by the annotated values as Manual entries. Manual
/// wins per slot and category everywhere else (<see cref="PerWeightDescriptorLookup.HasManualInCategory"/>),
/// so rule-derived suggestions in the category stop landing in that slot.</para>
/// </summary>
public static class PresetAnnotationApplier
{
    /// <summary>One (preset, weight, category) to rewrite.</summary>
    public sealed class Change
    {
        public BodySlideSetting Preset { get; init; } = null!;
        public int Weight { get; init; }
        public string Category { get; init; } = "";
        public List<string> Values { get; init; } = new();

        /// <summary>Manual values currently in the slot for this category that differ from
        /// <see cref="Values"/>. Non-empty = this change overwrites a hand label.</summary>
        public List<string> ReplacedManualValues { get; init; } = new();
    }

    public sealed class ApplyPlan
    {
        public List<Change> Changes { get; } = new();

        /// <summary>Human-readable lines for changes that replace an existing, different manual value.</summary>
        public List<string> Conflicts { get; } = new();

        /// <summary>Annotations whose preset is not in the current preset list ("label (gender)").</summary>
        public List<string> MissingPresets { get; } = new();

        /// <summary>Annotations at a weight the preset has no slot for ("label [weight]").</summary>
        public List<string> MissingSlots { get; } = new();

        /// <summary>Annotated descriptors absent from the descriptor catalog ("Category: Value").</summary>
        public HashSet<string> UnknownDescriptors { get; } = new(StringComparer.Ordinal);
    }

    /// <summary>
    /// Computes what applying <paramref name="annotations"/> would change, without mutating anything.
    /// </summary>
    /// <param name="annotations">The worklist verdicts to apply.</param>
    /// <param name="findPreset">Resolves (preset label, gender) to the preset model, or null.</param>
    /// <param name="knownDescriptors">The descriptor catalog; annotated values outside it are skipped
    /// (and reported). Null skips the check.</param>
    public static ApplyPlan Plan(
        IEnumerable<PresetAnnotation>? annotations,
        Func<string, Gender, BodySlideSetting?> findPreset,
        ICollection<(string Category, string Value)>? knownDescriptors)
    {
        var plan = new ApplyPlan();
        foreach (var annotation in annotations ?? Enumerable.Empty<PresetAnnotation>())
        {
            if (annotation?.Descriptors == null || annotation.Descriptors.Count == 0) continue;

            var preset = findPreset(annotation.PresetLabel ?? "", annotation.PresetGender);
            if (preset == null)
            {
                plan.MissingPresets.Add($"{annotation.PresetLabel} ({annotation.PresetGender})");
                continue;
            }
            if (preset.BodyShapeDescriptorsByWeight == null
                || !preset.BodyShapeDescriptorsByWeight.TryGetValue(annotation.Weight, out var slot)
                || slot == null)
            {
                plan.MissingSlots.Add($"{annotation.PresetLabel} [{annotation.Weight}]");
                continue;
            }

            foreach (var byCategory in annotation.Descriptors
                         .Where(d => d != null && !string.IsNullOrEmpty(d.Category) && !string.IsNullOrEmpty(d.Value))
                         .GroupBy(d => d.Category, StringComparer.Ordinal))
            {
                var values = new List<string>();
                foreach (var value in byCategory.Select(d => d.Value).Distinct(StringComparer.Ordinal))
                {
                    if (knownDescriptors != null && !knownDescriptors.Contains((byCategory.Key, value)))
                    {
                        plan.UnknownDescriptors.Add(byCategory.Key + ": " + value);
                        continue;
                    }
                    values.Add(value);
                }
                if (values.Count == 0) continue;

                var existingManual = slot
                    .Where(x => x != null && x.Source == BodyShapeAnnotationSource.Manual && string.Equals(x.Category, byCategory.Key, StringComparison.Ordinal))
                    .Select(x => x.Value)
                    .Distinct(StringComparer.Ordinal)
                    .ToList();

                // Already exactly this manual label: nothing to do.
                if (existingManual.Count == values.Count && existingManual.All(values.Contains)) continue;

                var change = new Change
                {
                    Preset = preset,
                    Weight = annotation.Weight,
                    Category = byCategory.Key,
                    Values = values,
                    ReplacedManualValues = existingManual,
                };
                plan.Changes.Add(change);
                if (existingManual.Count > 0)
                {
                    plan.Conflicts.Add($"{preset.Label} [{annotation.Weight}] {byCategory.Key}: {string.Join(", ", existingManual)} -> {string.Join(", ", values)}");
                }
            }
        }
        return plan;
    }

    /// <summary>Applies every change in <paramref name="plan"/>: clears the slot's descriptors in the
    /// category (all sources) and adds the annotated values as Manual.</summary>
    public static void Execute(ApplyPlan plan)
    {
        foreach (var change in plan?.Changes ?? new List<Change>())
        {
            if (!change.Preset.BodyShapeDescriptorsByWeight.TryGetValue(change.Weight, out var slot) || slot == null) continue;
            slot.RemoveWhere(x => x != null && string.Equals(x.Category, change.Category, StringComparison.Ordinal));
            foreach (var value in change.Values)
            {
                slot.Add(new AnnotatedDescriptorSignature(
                    new BodyShapeDescriptor.LabelSignature { Category = change.Category, Value = value },
                    BodyShapeAnnotationSource.Manual));
            }
        }
    }

    /// <summary>Summary of what the plan skipped, for the post-apply message; empty when nothing was.</summary>
    public static string FormatSkips(ApplyPlan plan)
    {
        var parts = new List<string>();
        if (plan.MissingPresets.Count > 0)
            parts.Add($"{plan.MissingPresets.Count} annotation(s) skipped because the preset isn't installed: {Preview(plan.MissingPresets)}");
        if (plan.MissingSlots.Count > 0)
            parts.Add($"{plan.MissingSlots.Count} annotation(s) skipped because the preset has no slot at that weight: {Preview(plan.MissingSlots)}");
        if (plan.UnknownDescriptors.Count > 0)
            parts.Add($"{plan.UnknownDescriptors.Count} descriptor(s) skipped because they aren't in your descriptor list: {Preview(plan.UnknownDescriptors)}");
        return parts.Count == 0 ? "" : "\n\n" + string.Join("\n\n", parts);
    }

    private static string Preview(IEnumerable<string> items)
    {
        var list = items.Distinct().ToList();
        var shown = string.Join(", ", list.Take(10));
        return list.Count > 10 ? shown + ", ..." : shown;
    }
}
