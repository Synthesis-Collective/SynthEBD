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
    public void AnnotateBodySlides(List<BodySlideSetting> bodySlides, Dictionary<string, SliderClassificationRulesByBodyType> bodySlideClassificationRules, bool overwriteExistingAnnotations)
    {
        var toAnnotate = new List<BodySlideSetting>(bodySlides);
        if (overwriteExistingAnnotations)
        {
            foreach (var bs in toAnnotate)
            {
                bs.BodyShapeDescriptors.Clear();
            }
        }
        else
        {
            toAnnotate = bodySlides.Where(x => !x.BodyShapeDescriptors.Any()).ToList();
        }

        foreach (var bs in toAnnotate)
        {
            AnnotateBodySlide(bs, bodySlideClassificationRules);
        }
    }

    public void AnnotateBodySlide(BodySlideSetting bodySlide, Dictionary<string, SliderClassificationRulesByBodyType> bodySlideClassificationRules)
    {
        if (bodySlide == null || bodySlideClassificationRules == null || bodySlide.SliderGroup == null || bodySlide.SliderValues == null || !bodySlideClassificationRules.ContainsKey(bodySlide.SliderGroup))
        {
            return;
        }

        foreach (var ruleSet in bodySlideClassificationRules[bodySlide.SliderGroup].DescriptorClassifiers)
        {
            ApplyDescriptorCategoryRuleSet(bodySlide, ruleSet);
        }
    }

    private void ApplyDescriptorCategoryRuleSet(BodySlideSetting bodySlide, DescriptorClassificationRuleSet ruleSet)
    {
        bool ruleApplied = false;
        foreach (var rule in ruleSet.RuleList)
        {
            if (EvaluateDescriptorValueRule(bodySlide, rule))
            {
                var descriptorSignature = new BodyShapeDescriptor.LabelSignature() { Category = ruleSet.DescriptorCategory, Value = rule.SelectedDescriptorValue };
                bodySlide.BodyShapeDescriptors.Add(descriptorSignature);
                _logger.LogMessage("BodySlide Preset " + bodySlide.Label + " annotated as " + descriptorSignature.ToString());
                ruleApplied = true;
                bodySlide.AutoAnnotated = true;
            }
        }

        if (!ruleApplied && !ruleSet.DefaultDescriptorValue.IsNullOrWhitespace())
        {
            var descriptorSignature = new BodyShapeDescriptor.LabelSignature() { Category = ruleSet.DescriptorCategory, Value = ruleSet.DefaultDescriptorValue };
            bodySlide.BodyShapeDescriptors.Add(descriptorSignature);
            _logger.LogMessage("BodySlide Preset " + bodySlide.Label + " annotated as (default) " + descriptorSignature.ToString());
            bodySlide.AutoAnnotated = true;
        }
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
        if (bodySlide.SliderValues.ContainsKey(rule.SliderName))
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