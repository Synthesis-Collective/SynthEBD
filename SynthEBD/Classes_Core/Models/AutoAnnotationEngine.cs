namespace SynthEBD;

/// <summary>
/// Computes a preset's <b>automatic</b> descriptor assignments at one weight slot from the active
/// Auto-Annotation rules of its body type. Pure and stateless: the caller supplies the preset, the
/// profile, and (optionally) the slot's measurement values, so the same code serves the BodySlides
/// preset editor, the pre-patch step, and the Descriptor Filter.
///
/// <para><b>Every rule kind is treated the same.</b> Slider conditions read the preset's sliders,
/// Measurement conditions read the supplied measurement values, and DescriptorRef conditions read
/// the descriptors earlier rules (or the user) produced, all in one dependency-ordered pass
/// (<see cref="BodySlideMeasurementEvaluator.RunClassifierRules"/>).</para>
///
/// <para><b>Evaluability.</b> A category is assigned only when it can be fully evaluated: every
/// eligible rule producing it can read every measurement it names (the measurement is
/// <i>known</i> for this slot -- present in the cache entry, even if it failed to compute), and every
/// category its DescriptorRef conditions point at is itself evaluable or manually set. Otherwise the
/// category gets nothing, not even its default: a default is only correct when every rule that could
/// have claimed the category was actually tested. Slider-only categories are therefore always
/// evaluable, and measurement categories become evaluable once the preset has been scanned.</para>
///
/// <para><b>Manual wins per (weight slot, category).</b> A category the user owns in this slot
/// (<see cref="BodySlideSetting.ManualCategoriesByWeight"/>, or any Manual descriptor in it) is never
/// assigned automatically. Its Manual values seed the pass so DescriptorRef conditions see exactly
/// what the user chose, and its own rules and default are dropped from the pass.</para>
/// </summary>
public static class AutoAnnotationEngine
{
    /// <summary>Output of one slot derivation.</summary>
    public sealed class SlotResult
    {
        /// <summary>The automatic descriptors for the slot (rule matches, then category defaults).</summary>
        public List<BodyShapeDescriptor.LabelSignature> Descriptors { get; } = new();

        /// <summary>Categories that have rules or a default but could not be evaluated for this slot
        /// (a measurement they need is not cached, or they depend on such a category).</summary>
        public HashSet<string> UnevaluableCategories { get; } = new(StringComparer.Ordinal);
    }

    /// <summary>
    /// Derives the automatic descriptors for <paramref name="preset"/> at <paramref name="weight"/>.
    /// </summary>
    /// <param name="profile">The body type's active Auto-Annotation profile.</param>
    /// <param name="preset">The preset (slider values and Manual annotations are read from it).</param>
    /// <param name="gender">The preset's gender, for gender-filtered rules.</param>
    /// <param name="weight">The weight slot.</param>
    /// <param name="measurements">The slot's successfully computed measurement values, or null when
    /// the slot has no cache entry.</param>
    /// <param name="knownMeasurementNames">Every measurement the slot's cache entry carries, including
    /// ones that failed to compute (a failed measurement is evaluated as "condition fails", exactly as
    /// at scan time). Null = the keys of <paramref name="measurements"/>.</param>
    /// <param name="includeDrafts">Whether draft rules take part (editor previews only).</param>
    public static SlotResult DeriveForSlot(
        BodyTypeProfile profile,
        BodySlideSetting preset,
        Gender gender,
        int weight,
        IReadOnlyDictionary<string, float>? measurements,
        IReadOnlyCollection<string>? knownMeasurementNames = null,
        bool includeDrafts = false)
    {
        var result = new SlotResult();
        if (profile == null || preset == null) return result;

        measurements ??= new Dictionary<string, float>(StringComparer.Ordinal);
        var known = new HashSet<string>(knownMeasurementNames ?? measurements.Keys, StringComparer.Ordinal);

        var manualCategories = preset.GetManualCategories(weight);
        var seeds = BodySlideMeasurementEvaluator.CollectExternalDescriptors(preset, weight);

        var eligible = BodySlideMeasurementEvaluator.FilterEligibleRules(profile.Rules, gender, includeDrafts)
            .Where(r => !manualCategories.Contains(r.Descriptor.Category))
            .ToList();
        var defaults = (profile.DefaultDescriptorValuesByCategory ?? new Dictionary<string, string>())
            .Where(kv => !string.IsNullOrEmpty(kv.Key) && !string.IsNullOrEmpty(kv.Value) && !manualCategories.Contains(kv.Key))
            .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal);

        var unevaluable = FindUnevaluableCategories(eligible, known, manualCategories);
        result.UnevaluableCategories.UnionWith(unevaluable);
        if (unevaluable.Count > 0)
        {
            eligible = eligible.Where(r => !unevaluable.Contains(r.Descriptor.Category)).ToList();
            foreach (var category in unevaluable) defaults.Remove(category);
        }

        var seen = new HashSet<(string, string)>();
        var defaultOutput = BodySlideMeasurementEvaluator.RunClassifierRules(
            eligible, measurements, defaults,
            (rule, _) =>
            {
                if (seen.Add((rule.Descriptor.Category, rule.Descriptor.Value)))
                {
                    result.Descriptors.Add(new BodyShapeDescriptor.LabelSignature { Category = rule.Descriptor.Category, Value = rule.Descriptor.Value });
                }
            },
            seeds,
            SliderSample.For(preset, weight));

        foreach (var d in defaultOutput)
        {
            if (seen.Add((d.Category, d.Value))) result.Descriptors.Add(d);
        }
        return result;
    }

    /// <summary>
    /// Categories produced by <paramref name="rules"/> that can't be fully evaluated: some rule
    /// producing them names a measurement outside <paramref name="knownMeasurementNames"/>, or
    /// (transitively) references such a category through a DescriptorRef condition. Manually set
    /// categories are always evaluable as references -- their value is the user's. Only enabled
    /// groups count, matching <see cref="MeasurementMath.RuleMatches"/>.
    /// </summary>
    public static HashSet<string> FindUnevaluableCategories(
        IEnumerable<MeasurementRule> rules,
        IReadOnlySet<string> knownMeasurementNames,
        IReadOnlySet<string>? manualCategories = null)
    {
        var ruleList = rules.Where(r => r?.Descriptor != null && !string.IsNullOrEmpty(r.Descriptor.Category)).ToList();
        var unevaluable = new HashSet<string>(StringComparer.Ordinal);
        var referencesByCategory = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

        foreach (var rule in ruleList)
        {
            string category = rule.Descriptor.Category;
            if (!referencesByCategory.TryGetValue(category, out var refs))
            {
                refs = new HashSet<string>(StringComparer.Ordinal);
                referencesByCategory[category] = refs;
            }
            foreach (var group in rule.GroupsORlogic ?? new())
            {
                if (group == null || group.IsDisabled) continue;
                foreach (var cond in group.ConditionsANDlogic ?? new())
                {
                    if (cond == null) continue;
                    switch (cond.Kind)
                    {
                        case MeasurementConditionKind.Measurement:
                            if (!string.IsNullOrEmpty(cond.MeasurementName) && !knownMeasurementNames.Contains(cond.MeasurementName))
                            {
                                unevaluable.Add(category);
                            }
                            break;
                        case MeasurementConditionKind.DescriptorRef:
                            if (!string.IsNullOrEmpty(cond.RefCategory) && !string.Equals(cond.RefCategory, category, StringComparison.Ordinal))
                            {
                                refs.Add(cond.RefCategory);
                            }
                            break;
                    }
                }
            }
        }

        // Propagate through DescriptorRef dependencies until stable.
        bool changed = true;
        while (changed)
        {
            changed = false;
            foreach (var (category, refs) in referencesByCategory)
            {
                if (unevaluable.Contains(category)) continue;
                if (refs.Any(r => unevaluable.Contains(r) && (manualCategories == null || !manualCategories.Contains(r))))
                {
                    unevaluable.Add(category);
                    changed = true;
                }
            }
        }
        return unevaluable;
    }
}
