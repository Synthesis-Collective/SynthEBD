using Noggog;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SynthEBD;

public class BodySlideAnnotator
{
    private readonly Logger _logger;
    private readonly PatcherState _patcherState;
    public BodySlideAnnotator(Logger logger, PatcherState patcherState)
    {
        _logger = logger;
        _patcherState = patcherState;
    }
    public void AnnotateBodySlides(List<BodySlideSetting> bodySlides, Dictionary<string, SliderClassificationRulesByBodyType> bodySlideClassificationRules, HashSet<BodyShapeDescriptor.LabelSignature> currentDescriptors, bool overwriteExistingAutoAnnotations, string? specifiedDescriptorCategory)
    {
        foreach (var bs in bodySlides)
        {
            AnnotateBodySlide(bs, bodySlideClassificationRules, currentDescriptors, overwriteExistingAutoAnnotations, specifiedDescriptorCategory);
        }
    }

    public List<BodyShapeDescriptor.LabelSignature> AnnotateBodySlide(BodySlideSetting bodySlide, Dictionary<string, SliderClassificationRulesByBodyType> bodySlideClassificationRules, HashSet<BodyShapeDescriptor.LabelSignature> currentDescriptors, bool overwriteExistingAutoAnnotations, string? specifiedDescriptorCategory)
    {
        List<BodyShapeDescriptor.LabelSignature> annotatedDescriptors = new();
        if (bodySlide == null)
        {
            return annotatedDescriptors;
        }

        bodySlide.AnnotationState = bodySlide.BodyShapeDescriptors.Where(x => x.AnnotationState == BodyShapeAnnotationState.Manual).Any()? BodyShapeAnnotationState.Manual : BodyShapeAnnotationState.None;

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
                bodySlide.BodyShapeDescriptors.RemoveWhere(x => x.Category == ruleSet.DescriptorCategory && x.AnnotationState != BodyShapeAnnotationState.Manual); // remove all auto-annotated descriptors from this category if they are to be refreshed
            }

            if (bodySlide.BodyShapeDescriptors.Where(x => x.Category == ruleSet.DescriptorCategory && x.AnnotationState == BodyShapeAnnotationState.Manual).Any()) // skip over the category if it's already manually annotated
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
                bodySlide.BodyShapeDescriptors.Add(new(descriptorSignature, BodyShapeAnnotationState.RulesBased));
                _logger.LogMessage("BodySlide Preset " + bodySlide.Label + " annotated as " + descriptorSignature.ToString());
                ruleApplied = true;
                annotatedDescriptors.Add(descriptorSignature);
            }
        }

        if (!ruleApplied && !ruleSet.DefaultDescriptorValue.IsNullOrWhitespace() && currentValues.Contains(ruleSet.DefaultDescriptorValue))
        {
            var descriptorSignature = new BodyShapeDescriptor.LabelSignature() { Category = ruleSet.DescriptorCategory, Value = ruleSet.DefaultDescriptorValue };
            bodySlide.BodyShapeDescriptors.Add(new(descriptorSignature, BodyShapeAnnotationState.RulesBased));
            _logger.LogMessage("BodySlide Preset " + bodySlide.Label + " annotated as (default) " + descriptorSignature.ToString());
            annotatedDescriptors.Add(descriptorSignature);
        }
        return annotatedDescriptors;
    }

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