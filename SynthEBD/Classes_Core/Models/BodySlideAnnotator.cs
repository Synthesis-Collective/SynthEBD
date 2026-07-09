using Noggog;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SynthEBD;

/// <summary>
/// Rules-based engine that auto-annotates BodySlide presets with body-shape descriptors by evaluating each
/// preset's slider values against per-slider-group classification rules. Rules are evaluated once per
/// descriptor weight slot (<see cref="BodySlideSetting.BodyShapeDescriptorsByWeight"/>): conditions on the
/// authored endpoint values (<see cref="BodySliderType.Small"/>/<see cref="BodySliderType.Big"/>/
/// <see cref="BodySliderType.Either"/>) are constant across slots, while
/// <see cref="BodySliderType.Interpolated"/> conditions test the linearly weight-blended value at each slot —
/// so a rule with only endpoint conditions annotates all slots or none (legacy whole-preset behavior), and a
/// rule with an Interpolated condition annotates just the slots where it passes. A category's default
/// descriptor fills only the slots no rule in that category matched. Honors manual annotations (never
/// overwriting them) and tracks each preset's annotation state (none / rules-based / manual / mixed).
/// </summary>
public class BodySlideAnnotator
{
    private readonly Logger _logger;
    private readonly PatcherState _patcherState;
    /// <summary>Captures the logger and patcher state used while annotating.</summary>
    public BodySlideAnnotator(Logger logger, PatcherState patcherState)
    {
        _logger = logger;
        _patcherState = patcherState;
    }
    /// <summary>Annotates every preset in <paramref name="bodySlides"/> via <see cref="AnnotateBodySlide(BodySlideSetting, Dictionary{string, SliderClassificationRulesByBodyType}, HashSet{BodyShapeDescriptor.LabelSignature}, bool, string?)"/>.</summary>
    /// <param name="bodySlides">Presets to annotate (mutated in place).</param>
    /// <param name="bodySlideClassificationRules">Classification rules keyed by slider group.</param>
    /// <param name="currentDescriptors">The descriptor universe in use; only categories/values present here are applied.</param>
    /// <param name="overwriteExistingAutoAnnotations">When true, refreshes previously auto-applied descriptors.</param>
    /// <param name="specifiedDescriptorCategory">When set, restricts annotation to a single descriptor category.</param>
    public void AnnotateBodySlides(List<BodySlideSetting> bodySlides, Dictionary<string, SliderClassificationRulesByBodyType> bodySlideClassificationRules, HashSet<BodyShapeDescriptor.LabelSignature> currentDescriptors, bool overwriteExistingAutoAnnotations, string? specifiedDescriptorCategory)
    {
        foreach (var bs in bodySlides)
        {
            AnnotateBodySlide(bs, bodySlideClassificationRules, currentDescriptors, overwriteExistingAutoAnnotations, specifiedDescriptorCategory);
        }
    }

    /// <summary>Instance wrapper around the static core that routes log output to the injected <see cref="Logger"/>.</summary>
    /// <returns>The descriptors that were applied (empty if none/unclassifiable).</returns>
    public List<BodyShapeDescriptor.LabelSignature> AnnotateBodySlide(BodySlideSetting bodySlide, Dictionary<string, SliderClassificationRulesByBodyType> bodySlideClassificationRules, HashSet<BodyShapeDescriptor.LabelSignature> currentDescriptors, bool overwriteExistingAutoAnnotations, string? specifiedDescriptorCategory)
    {
        return AnnotateBodySlide(bodySlide, bodySlideClassificationRules, currentDescriptors, overwriteExistingAutoAnnotations, specifiedDescriptorCategory, _logger.LogMessage);
    }

    /// <summary>
    /// Annotates a single preset: for each descriptor category, evaluates its rules once per weight slot and
    /// adds the matching descriptor to the slots where the rule passed (the category default fills any slots
    /// no rule matched) — skipping categories already manually annotated, and optionally clearing prior
    /// auto-annotations first. Updates the preset's annotation state. Static so tests can drive it without a
    /// <see cref="Logger"/>; <paramref name="logMessage"/> may be null to suppress logging.
    /// </summary>
    /// <returns>The descriptors that were applied (empty if none/unclassifiable).</returns>
    public static List<BodyShapeDescriptor.LabelSignature> AnnotateBodySlide(BodySlideSetting bodySlide, Dictionary<string, SliderClassificationRulesByBodyType> bodySlideClassificationRules, HashSet<BodyShapeDescriptor.LabelSignature> currentDescriptors, bool overwriteExistingAutoAnnotations, string? specifiedDescriptorCategory, Action<string>? logMessage)
    {
        List<BodyShapeDescriptor.LabelSignature> annotatedDescriptors = new();
        if (bodySlide == null)
        {
            return annotatedDescriptors;
        }

        bool hasManual = bodySlide.EnumerateAllDescriptors().Any(x => x.Source == BodyShapeAnnotationSource.Manual);
        bodySlide.AnnotationState = hasManual ? BodyShapeAnnotationState.Manual : BodyShapeAnnotationState.None;

        if (bodySlideClassificationRules == null || bodySlide.SliderGroup == null || bodySlide.SliderValues == null || !bodySlideClassificationRules.ContainsKey(bodySlide.SliderGroup) || bodySlideClassificationRules[bodySlide.SliderGroup] == null)
        {
            return annotatedDescriptors;
        }

        var currentCategories = currentDescriptors.Select(x => x.Category).ToHashSet();

        foreach (var ruleSet in bodySlideClassificationRules[bodySlide.SliderGroup].DescriptorClassifiers)
        {
            if (specifiedDescriptorCategory != null && ruleSet.DescriptorCategory != specifiedDescriptorCategory)
            {
                continue;
            }

            if (!currentCategories.Contains(ruleSet.DescriptorCategory))
            {
                continue;
            }

            if (overwriteExistingAutoAnnotations)
            {
                // remove all auto-annotated descriptors from this category across every slot so they can be refreshed
                bodySlide.RemoveDescriptorsFromAllSlots(x => x.Category == ruleSet.DescriptorCategory && x.Source != BodyShapeAnnotationSource.Manual);
            }

            // skip over the category if it's already manually annotated in any slot
            if (bodySlide.EnumerateAllDescriptors().Any(x => x.Category == ruleSet.DescriptorCategory && x.Source == BodyShapeAnnotationSource.Manual))
            {
                continue;
            }

            annotatedDescriptors.AddRange(ApplyDescriptorCategoryRuleSet(bodySlide, ruleSet, currentDescriptors.Where(x => x.Category == ruleSet.DescriptorCategory).Select(x => x.Value).ToHashSet(), logMessage));
        }

        if (annotatedDescriptors.Any())
        {
            if (bodySlide.AnnotationState == BodyShapeAnnotationState.None)
            {
                bodySlide.AnnotationState = BodyShapeAnnotationState.RulesBased;
            }
            else if (bodySlide.AnnotationState == BodyShapeAnnotationState.Manual)
            {
                bodySlide.AnnotationState = BodyShapeAnnotationState.Mix_Manual_RulesBased;
            }
        }

        return annotatedDescriptors;
    }

    /// <summary>
    /// Pure per-slot counterpart of <see cref="AnnotateBodySlide(BodySlideSetting, Dictionary{string, SliderClassificationRulesByBodyType}, HashSet{BodyShapeDescriptor.LabelSignature}, bool, string?, Action{string}?)"/>:
    /// computes the descriptors the classification rules WOULD assign to <paramref name="bodySlide"/> at
    /// <paramref name="weightSlot"/>, without touching the preset. Applies the same gates as the annotate
    /// pass — keep them in lockstep: rules must exist for the preset's SliderGroup; the category and its
    /// values must exist in <paramref name="currentDescriptors"/> (null skips that filtering); a category
    /// with any Manual annotation anywhere on the preset is skipped (rules never overwrite hand labels);
    /// and the category default fills the slot when no rule matches there.
    ///
    /// Unlike the annotate pass this evaluates at ANY weight 0-100 (Interpolated conditions blend to the
    /// exact weight given), not just the preset's existing descriptor slots. Used by the BodySlide
    /// Classifier's external-descriptor seeding (<c>BodySlideMeasurementEvaluator.CollectExternalDescriptors</c>)
    /// so Label-by-Measurements DescriptorRef conditions read slider labels live from the CURRENT rule
    /// set — drafting or revising a slider rule is visible to measurement rules immediately, with no
    /// "apply annotations" step in between.
    /// </summary>
    public static List<BodyShapeDescriptor.LabelSignature> DeriveDescriptorsForSlot(
        BodySlideSetting bodySlide,
        Dictionary<string, SliderClassificationRulesByBodyType> bodySlideClassificationRules,
        HashSet<BodyShapeDescriptor.LabelSignature>? currentDescriptors,
        int weightSlot)
    {
        var result = new List<BodyShapeDescriptor.LabelSignature>();
        if (bodySlide?.SliderGroup == null || bodySlide.SliderValues == null) return result;
        if (bodySlideClassificationRules == null
            || !bodySlideClassificationRules.TryGetValue(bodySlide.SliderGroup, out var rulesForBodyType)
            || rulesForBodyType?.DescriptorClassifiers == null)
        {
            return result;
        }

        var currentCategories = currentDescriptors?.Select(x => x.Category).ToHashSet();

        foreach (var ruleSet in rulesForBodyType.DescriptorClassifiers)
        {
            if (ruleSet == null || ruleSet.RuleList == null) continue;
            if (currentCategories != null && !currentCategories.Contains(ruleSet.DescriptorCategory)) continue;

            // Manual precedence, same as the annotate pass: a category the user hand-labeled
            // anywhere on this preset is never rules-annotated, so it must not derive here either.
            if (bodySlide.EnumerateAllDescriptors().Any(x => x.Category == ruleSet.DescriptorCategory && x.Source == BodyShapeAnnotationSource.Manual))
            {
                continue;
            }

            var currentValues = currentDescriptors?
                .Where(x => x.Category == ruleSet.DescriptorCategory)
                .Select(x => x.Value)
                .ToHashSet();

            bool anyMatched = false;
            var seenValues = new HashSet<string>(StringComparer.Ordinal);
            foreach (var rule in ruleSet.RuleList)
            {
                if (rule == null) continue;
                if (currentValues != null && !currentValues.Contains(rule.SelectedDescriptorValue)) continue;
                if (!EvaluateDescriptorValueRule(bodySlide, rule, weightSlot)) continue;
                anyMatched = true;
                if (seenValues.Add(rule.SelectedDescriptorValue))
                {
                    result.Add(new BodyShapeDescriptor.LabelSignature { Category = ruleSet.DescriptorCategory, Value = rule.SelectedDescriptorValue });
                }
            }

            if (!anyMatched
                && !ruleSet.DefaultDescriptorValue.IsNullOrWhitespace()
                && (currentValues == null || currentValues.Contains(ruleSet.DefaultDescriptorValue)))
            {
                result.Add(new BodyShapeDescriptor.LabelSignature { Category = ruleSet.DescriptorCategory, Value = ruleSet.DefaultDescriptorValue });
            }
        }
        return result;
    }

    /// <summary>
    /// Applies one category's rule set to a preset, evaluating each rule once per weight slot: the rule's
    /// descriptor is added to every slot where its predicate passes (endpoint-only rules pass all slots or
    /// none, so legacy rules keep whole-preset behavior). The category's default descriptor then fills only
    /// the slots no rule matched. Returns the applied descriptors (one entry per rule/default that landed
    /// in at least one slot).
    /// </summary>
    private static List<BodyShapeDescriptor.LabelSignature> ApplyDescriptorCategoryRuleSet(BodySlideSetting bodySlide, DescriptorClassificationRuleSet ruleSet, HashSet<string> currentValues, Action<string>? logMessage)
    {
        List<BodyShapeDescriptor.LabelSignature> annotatedDescriptors = new();

        var weightSlots = bodySlide.BodyShapeDescriptorsByWeight?.Keys.OrderBy(x => x).ToList();
        if (weightSlots == null || !weightSlots.Any())
        {
            return annotatedDescriptors;
        }

        var matchedSlots = new HashSet<int>(); // slots where at least one rule in this category matched

        foreach (var rule in ruleSet.RuleList)
        {
            if (!currentValues.Contains(rule.SelectedDescriptorValue))
            {
                continue;
            }

            var passingSlots = weightSlots.Where(weight => EvaluateDescriptorValueRule(bodySlide, rule, weight)).ToList();
            if (!passingSlots.Any())
            {
                continue;
            }

            var descriptorSignature = new BodyShapeDescriptor.LabelSignature() { Category = ruleSet.DescriptorCategory, Value = rule.SelectedDescriptorValue };
            foreach (var weight in passingSlots)
            {
                bodySlide.BodyShapeDescriptorsByWeight[weight].Add(new AnnotatedDescriptorSignature(descriptorSignature, BodyShapeAnnotationSource.RulesBased));
                matchedSlots.Add(weight);
            }
            logMessage?.Invoke("BodySlide Preset " + bodySlide.Label + " annotated as " + descriptorSignature.ToString() + FormatWeightSlotSuffix(passingSlots, weightSlots.Count));
            annotatedDescriptors.Add(descriptorSignature);
        }

        if (!ruleSet.DefaultDescriptorValue.IsNullOrWhitespace() && currentValues.Contains(ruleSet.DefaultDescriptorValue))
        {
            var defaultSlots = weightSlots.Where(weight => !matchedSlots.Contains(weight)).ToList();
            if (defaultSlots.Any())
            {
                var descriptorSignature = new BodyShapeDescriptor.LabelSignature() { Category = ruleSet.DescriptorCategory, Value = ruleSet.DefaultDescriptorValue };
                foreach (var weight in defaultSlots)
                {
                    bodySlide.BodyShapeDescriptorsByWeight[weight].Add(new AnnotatedDescriptorSignature(descriptorSignature, BodyShapeAnnotationSource.RulesBased));
                }
                logMessage?.Invoke("BodySlide Preset " + bodySlide.Label + " annotated as (default) " + descriptorSignature.ToString() + FormatWeightSlotSuffix(defaultSlots, weightSlots.Count));
                annotatedDescriptors.Add(descriptorSignature);
            }
        }
        return annotatedDescriptors;
    }

    /// <summary>Empty when the descriptor landed in every slot (whole-preset annotation — keeps the legacy log text); otherwise lists the specific weight slots.</summary>
    private static string FormatWeightSlotSuffix(List<int> slots, int totalSlotCount)
    {
        if (slots.Count == totalSlotCount)
        {
            return string.Empty;
        }
        return " at weight(s) " + string.Join(", ", slots);
    }

    /// <summary>
    /// True when <paramref name="bodySlide"/> satisfies EVERY rule in <paramref name="rules"/> at
    /// <paramref name="weight"/> — each rule's OR-of-AND-groups must pass, evaluated by the same
    /// <see cref="EvaluateDescriptorValueRule"/> the annotate pass and
    /// <see cref="DeriveDescriptorsForSlot"/> use, so a filter built on this can never disagree
    /// with what applying the rules would do. An empty/null rule list matches everything; a null
    /// preset or one with no parsed slider values matches nothing. Used by the Label by Sliders
    /// preset browser's per-rule "Filter Presets" checkboxes — multiple checked rules intersect,
    /// which can legitimately yield zero matching presets.
    /// </summary>
    public static bool PresetMatchesAllRules(BodySlideSetting? bodySlide, IReadOnlyCollection<DescriptorAssignmentRuleSet>? rules, int weight)
    {
        if (rules == null || rules.Count == 0) return true;
        if (bodySlide?.SliderValues == null) return false;
        foreach (var rule in rules)
        {
            if (rule == null) continue;
            if (!EvaluateDescriptorValueRule(bodySlide, rule, weight)) return false;
        }
        return true;
    }

    /// <summary>OR-combines a descriptor value's rule groups at one weight slot — true if any AND-gated group passes there.</summary>
    private static bool EvaluateDescriptorValueRule(BodySlideSetting bodySlide, DescriptorAssignmentRuleSet ruleList, int weightSlot)
    {
        foreach (var ruleGroup in ruleList.RuleListORlogic)
        {
            if (EvaluateAndGatedRuleList(bodySlide, ruleGroup, weightSlot))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>AND-combines a rule group at one weight slot — true only if every sub-rule passes there (an empty group is false).</summary>
    private static bool EvaluateAndGatedRuleList(BodySlideSetting bodySlide, AndGatedSliderRuleGroup ruleGroup, int weightSlot)
    {
        if (!ruleGroup.RuleListANDlogic.Any())
        {
            return false;
        }

        foreach (var subRule in ruleGroup.RuleListANDlogic)
        {
            if (!EvaluateRule(bodySlide, subRule, weightSlot))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Evaluates a single slider rule against the preset's slider values, honoring the rule's slider type:
    /// Small / Big / Either read the authored endpoint values (weight-independent), while Interpolated reads
    /// the value the slider actually has at <paramref name="weightSlot"/>.
    /// </summary>
    private static bool EvaluateRule(BodySlideSetting bodySlide, SliderClassificationRule rule, int weightSlot)
    {
        if (rule != null && rule.SliderName != null && bodySlide.SliderValues.ContainsKey(rule.SliderName))
        {
            var slider = bodySlide.SliderValues[rule.SliderName];
            switch (rule.SliderType)
            {
                case BodySliderType.Small: return EvaluateExpression(slider.Small, rule.Value, rule.Comparator);
                case BodySliderType.Big: return EvaluateExpression(slider.Big, rule.Value, rule.Comparator);
                case BodySliderType.Either: return EvaluateExpression(slider.Small, rule.Value, rule.Comparator) || EvaluateExpression(slider.Big, rule.Value, rule.Comparator);
                case BodySliderType.Interpolated: return EvaluateExpression(InterpolateSliderValue(slider, weightSlot), rule.Value, rule.Comparator);
                default: return false;
            }
        }

        return false;
    }

    /// <summary>
    /// The value a slider has at <paramref name="weight"/> (0-100): the linear blend between its authored
    /// Small (weight 0) and Big (weight 100) values — the same interpolation the game applies to morphs.
    /// </summary>
    public static float InterpolateSliderValue(BodySlideSlider slider, int weight)
    {
        return slider.Small + (slider.Big - slider.Small) * (weight / 100f);
    }

    /// <summary>
    /// Compares a slider value against a threshold using the rule's comparator (=, !=, &lt;=, &gt;=, &lt;, &gt;).
    /// Endpoint values are whole numbers, so they compare exactly; an interpolated value can be fractional,
    /// so = and != compare against the nearest whole slider value (halves round away from zero: 2.5 "equals" 3).
    /// </summary>
    private static bool EvaluateExpression(float sliderValue, int thresholdValue, string comparator)
    {
        switch (comparator)
        {
            case "=": return (int)Math.Round(sliderValue, MidpointRounding.AwayFromZero) == thresholdValue;
            case "!=": return (int)Math.Round(sliderValue, MidpointRounding.AwayFromZero) != thresholdValue;
            case "<=": return sliderValue <= thresholdValue;
            case ">=": return sliderValue >= thresholdValue;
            case "<": return sliderValue < thresholdValue;
            case ">": return sliderValue > thresholdValue;
            default: return false;
        }
    }
}
