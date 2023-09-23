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
    public static void AnnotateBodySlides(List<BodySlideSetting> bodySlides, Dictionary<string, SliderClassificationRulesByBodyType> bodySlideClassificationRules, bool overwriteExistingAnnotations)
    {
        var toAnnotate = new List<BodySlideSetting>();
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

    private static void AnnotateBodySlide(BodySlideSetting bodySlide, Dictionary<string, SliderClassificationRulesByBodyType> bodySlideClassificationRules)
    {
        if (bodySlideClassificationRules.ContainsKey(bodySlide.SliderGroup))
        {
            foreach (var ruleSet in bodySlideClassificationRules[bodySlide.SliderGroup].DescriptorClassifiers)
            {
                ApplyDescriptorCategoryRuleSet(bodySlide, ruleSet);
            }
        }
    }

    private static void ApplyDescriptorCategoryRuleSet(BodySlideSetting bodySlide, DescriptorClassificationRuleSet ruleSet)
    {
        bool ruleApplied = false;
        foreach (var rule in ruleSet.RuleList)
        {
            if (EvaluateDescriptorValueRule(bodySlide, rule))
            {
                bodySlide.BodyShapeDescriptors.Add(new() { Category = ruleSet.DescriptorCategory, Value = rule.SelectedDescriptorValue });
                ruleApplied = true;
            }
        }

        if (!ruleApplied && !ruleSet.DefaultDescriptorValue.IsNullOrWhitespace())
        {
            bodySlide.BodyShapeDescriptors.Add(new() { Category = ruleSet.DescriptorCategory, Value = ruleSet.DefaultDescriptorValue });
        }
    }

    private static bool EvaluateDescriptorValueRule(BodySlideSetting bodySlide, DescriptorAssignmentRuleSet ruleList)
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

    private static bool EvaluateAndGatedRuleList(BodySlideSetting bodySlide, AndGatedSliderRuleGroup ruleGroup)
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

    private static bool EvaluateRule(BodySlideSetting bodySlide, SliderClassificationRule rule)
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

    private static bool EvaluateExpression(int sliderValue, int thresholdValue, string comparator)
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