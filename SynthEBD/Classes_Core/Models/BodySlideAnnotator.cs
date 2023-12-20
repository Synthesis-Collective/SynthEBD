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
    public BodySlideAnnotator(Logger logger)
    {
        _logger = logger;
    }
    public void AnnotateBodySlides(List<BodySlideSetting> bodySlides, Dictionary<string, SliderClassificationRulesByBodyType> bodySlideClassificationRules, bool overwriteExistingAnnotations, string? specifiedDescriptorCategory)
    {
        foreach (var bs in bodySlides)
        {
            AnnotateBodySlide(bs, bodySlideClassificationRules, overwriteExistingAnnotations, specifiedDescriptorCategory);
        }
    }

    public List<BodyShapeDescriptor.LabelSignature> AnnotateBodySlide(BodySlideSetting bodySlide, Dictionary<string, SliderClassificationRulesByBodyType> bodySlideClassificationRules, bool overwriteExistingAnnotations, string? specifiedDescriptorCategory)
    {
        List<BodyShapeDescriptor.LabelSignature> annotatedDescriptors = new();
        if (bodySlide == null)
        {
            return annotatedDescriptors;
        }

        bodySlide.AutoAnnotated = false; // should be default but because I forgot to set JsonIgnore for this property in SynthEBD <1.0.1.9, some BodySlides can be spuriously imported as "true" even if they should be false.

        if (bodySlideClassificationRules == null || bodySlide.SliderGroup == null || bodySlide.SliderValues == null || !bodySlideClassificationRules.ContainsKey(bodySlide.SliderGroup) || bodySlideClassificationRules[bodySlide.SliderGroup] == null)
        {
            return annotatedDescriptors;
        }

        foreach (var ruleSet in bodySlideClassificationRules[bodySlide.SliderGroup].DescriptorClassifiers)
        {
            if (specifiedDescriptorCategory != null && ruleSet.DescriptorCategory != specifiedDescriptorCategory)
            {
                continue;
            }
            if (overwriteExistingAnnotations)
            {
                bodySlide.BodyShapeDescriptors.RemoveWhere(x => x.Category == ruleSet.DescriptorCategory); // remove all descriptors from this category if they are to be re-annotated
            }

            if (bodySlide.BodyShapeDescriptors.Where(x => x.Category == ruleSet.DescriptorCategory && !x.AutoAnnotated).Any()) // skip over the category if it's already manually annotated
            {
                continue;
            }

            annotatedDescriptors.AddRange(ApplyDescriptorCategoryRuleSet(bodySlide, ruleSet));
        }
        return annotatedDescriptors;
    }

    private List<BodyShapeDescriptor.LabelSignature> ApplyDescriptorCategoryRuleSet(BodySlideSetting bodySlide, DescriptorClassificationRuleSet ruleSet)
    {
        List<BodyShapeDescriptor.LabelSignature> annotatedDescriptors = new();
        bool ruleApplied = false;
        foreach (var rule in ruleSet.RuleList)
        {
            if (EvaluateDescriptorValueRule(bodySlide, rule))
            {
                var descriptorSignature = new BodyShapeDescriptor.LabelSignature() { Category = ruleSet.DescriptorCategory, Value = rule.SelectedDescriptorValue };
                bodySlide.BodyShapeDescriptors.Add(new(descriptorSignature, true));
                _logger.LogMessage("BodySlide Preset " + bodySlide.Label + " annotated as " + descriptorSignature.ToString());
                ruleApplied = true;
                bodySlide.AutoAnnotated = true;
                annotatedDescriptors.Add(descriptorSignature);
            }
        }

        if (!ruleApplied && !ruleSet.DefaultDescriptorValue.IsNullOrWhitespace())
        {
            var descriptorSignature = new BodyShapeDescriptor.LabelSignature() { Category = ruleSet.DescriptorCategory, Value = ruleSet.DefaultDescriptorValue };
            bodySlide.BodyShapeDescriptors.Add(new(descriptorSignature, true));
            _logger.LogMessage("BodySlide Preset " + bodySlide.Label + " annotated as (default) " + descriptorSignature.ToString());
            bodySlide.AutoAnnotated = true;
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