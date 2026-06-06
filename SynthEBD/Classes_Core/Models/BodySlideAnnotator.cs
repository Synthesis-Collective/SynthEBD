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
/// preset's slider values against per-slider-group classification rules. Honors manual annotations (never
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
    /// <summary>Annotates every preset in <paramref name="bodySlides"/> via <see cref="AnnotateBodySlide"/>.</summary>
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

    /// <summary>
    /// Annotates a single preset: for each descriptor category whose rules match the preset's sliders, adds
    /// the matching descriptor (or the category default) to every slot — skipping categories already manually
    /// annotated, and optionally clearing prior auto-annotations first. Updates the preset's annotation state.
    /// </summary>
    /// <returns>The descriptors that were applied (empty if none/unclassifiable).</returns>
    public List<BodyShapeDescriptor.LabelSignature> AnnotateBodySlide(BodySlideSetting bodySlide, Dictionary<string, SliderClassificationRulesByBodyType> bodySlideClassificationRules, HashSet<BodyShapeDescriptor.LabelSignature> currentDescriptors, bool overwriteExistingAutoAnnotations, string? specifiedDescriptorCategory)
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

            annotatedDescriptors.AddRange(ApplyDescriptorCategoryRuleSet(bodySlide, ruleSet, currentDescriptors.Where(x => x.Category == ruleSet.DescriptorCategory).Select(x => x.Value).ToHashSet()));
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

    /// <summary>Applies one category's rule set to a preset: adds the descriptor for each matching rule, or the category's default descriptor if no rule matched. Returns the applied descriptors.</summary>
    private List<BodyShapeDescriptor.LabelSignature> ApplyDescriptorCategoryRuleSet(BodySlideSetting bodySlide, DescriptorClassificationRuleSet ruleSet, HashSet<string> currentValues)
    {
        List<BodyShapeDescriptor.LabelSignature> annotatedDescriptors = new();
        bool ruleApplied = false;

        foreach (var rule in ruleSet.RuleList)
        {
            if (!currentValues.Contains(rule.SelectedDescriptorValue))
            {
                continue;
            }

            if (EvaluateDescriptorValueRule(bodySlide, rule))
            {
                var descriptorSignature = new BodyShapeDescriptor.LabelSignature() { Category = ruleSet.DescriptorCategory, Value = rule.SelectedDescriptorValue };
                bodySlide.AddDescriptorToAllSlots(new AnnotatedDescriptorSignature(descriptorSignature, BodyShapeAnnotationSource.RulesBased));
                _logger.LogMessage("BodySlide Preset " + bodySlide.Label + " annotated as " + descriptorSignature.ToString());
                ruleApplied = true;
                annotatedDescriptors.Add(descriptorSignature);
            }
        }

        if (!ruleApplied && !ruleSet.DefaultDescriptorValue.IsNullOrWhitespace() && currentValues.Contains(ruleSet.DefaultDescriptorValue))
        {
            var descriptorSignature = new BodyShapeDescriptor.LabelSignature() { Category = ruleSet.DescriptorCategory, Value = ruleSet.DefaultDescriptorValue };
            bodySlide.AddDescriptorToAllSlots(new AnnotatedDescriptorSignature(descriptorSignature, BodyShapeAnnotationSource.RulesBased));
            _logger.LogMessage("BodySlide Preset " + bodySlide.Label + " annotated as (default) " + descriptorSignature.ToString());
            annotatedDescriptors.Add(descriptorSignature);
        }
        return annotatedDescriptors;
    }

    /// <summary>OR-combines a descriptor value's rule groups — true if any AND-gated group passes.</summary>
    private bool EvaluateDescriptorValueRule(BodySlideSetting bodySlide, DescriptorAssignmentRuleSet ruleList)
    {
        foreach (var ruleGroup in ruleList.RuleListORlogic)
        {
            if (EvaluateAndGatedRuleList(bodySlide, ruleGroup))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>AND-combines a rule group — true only if every sub-rule passes (an empty group is false).</summary>
    private bool EvaluateAndGatedRuleList(BodySlideSetting bodySlide, AndGatedSliderRuleGroup ruleGroup)
    {
        if (!ruleGroup.RuleListANDlogic.Any())
        {
            return false;
        }

        foreach (var subRule in ruleGroup.RuleListANDlogic)
        {
            if (!EvaluateRule(bodySlide, subRule))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Evaluates a single slider rule against the preset's slider values, honoring the rule's slider type (Small / Big / Either).</summary>
    private bool EvaluateRule(BodySlideSetting bodySlide, SliderClassificationRule rule)
    {
        if (rule != null && rule.SliderName != null && bodySlide.SliderValues.ContainsKey(rule.SliderName))
        {
            switch(rule.SliderType)
            {
                case BodySliderType.Small: return EvaluateExpression(bodySlide.SliderValues[rule.SliderName].Small, rule.Value, rule.Comparator);
                case BodySliderType.Big: return EvaluateExpression(bodySlide.SliderValues[rule.SliderName].Big, rule.Value, rule.Comparator);
                case BodySliderType.Either: return EvaluateExpression(bodySlide.SliderValues[rule.SliderName].Small, rule.Value, rule.Comparator) || EvaluateExpression(bodySlide.SliderValues[rule.SliderName].Big, rule.Value, rule.Comparator);
                default: return false;
            }
        }

        return false;
    }

    /// <summary>Compares a slider value against a threshold using the rule's comparator (=, !=, &lt;=, &gt;=, &lt;, &gt;).</summary>
    private bool EvaluateExpression(int sliderValue, int thresholdValue, string comparator)
    {
        switch (comparator)
        {
            case "=": return sliderValue == thresholdValue;
            case "!=": return sliderValue != thresholdValue;
            case "<=": return sliderValue <= thresholdValue;
            case ">=": return sliderValue >= thresholdValue;
            case "<": return sliderValue < thresholdValue;
            case ">": return sliderValue > thresholdValue;
            default: return false;
        }
    }
}