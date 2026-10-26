namespace SynthEBD;

/// <summary>
/// Converts legacy Label by Sliders rules (<see cref="SliderClassificationRulesByBodyType"/>) into
/// Auto-Annotation rules: each <see cref="DescriptorAssignmentRuleSet"/> becomes one
/// <see cref="MeasurementRule"/> whose conditions are <see cref="MeasurementConditionKind.Slider"/>
/// conditions, and each category default fills <see cref="BodyTypeProfile.DefaultDescriptorValuesByCategory"/>
/// where the profile has none. Sources: the <c>BodySlideClassificationRules</c> block of a released
/// build's <c>OBodySettings.json</c>, and the <c>SliderRules</c> block of older body type rule files.
///
/// <para>The conversion keeps the old evaluation exactly: the comparators map one-to-one (Slider
/// conditions round for = / != like the old engine), a group with no conditions or with an
/// unrecognized comparator could never match and is dropped, and a rule left with no groups is
/// dropped. A converted rule identical to one the profile already has is skipped, so running the
/// conversion twice (e.g. settings loaded again before the converted rules were saved) adds nothing.</para>
/// </summary>
public static class SliderRuleMigration
{
    /// <summary>
    /// Folds <paramref name="rules"/> into <paramref name="profile"/>, creating an empty profile named
    /// after <paramref name="bodyType"/> when there is none. Returns the number of rules added.
    /// </summary>
    public static int FoldInto(SliderClassificationRulesByBodyType? rules, ref BodyTypeProfile? profile, string bodyType, Action<string>? log)
    {
        if (!BodyTypeRuleSet.HasSliderContent(rules)) return 0;

        profile ??= new BodyTypeProfile { Name = bodyType, BodyTypeName = bodyType };
        profile.Rules ??= new();
        profile.DefaultDescriptorValuesByCategory ??= new(StringComparer.Ordinal);

        var existingSignatures = new HashSet<string>(profile.Rules.Where(r => r != null).Select(Signature), StringComparer.Ordinal);
        int added = 0;

        foreach (var classifier in rules!.DescriptorClassifiers ?? new())
        {
            if (classifier == null || string.IsNullOrWhiteSpace(classifier.DescriptorCategory)) continue;
            string category = classifier.DescriptorCategory;

            if (!string.IsNullOrWhiteSpace(classifier.DefaultDescriptorValue))
            {
                if (!profile.DefaultDescriptorValuesByCategory.TryGetValue(category, out var existingDefault) || string.IsNullOrEmpty(existingDefault))
                {
                    profile.DefaultDescriptorValuesByCategory[category] = classifier.DefaultDescriptorValue;
                }
                else if (!string.Equals(existingDefault, classifier.DefaultDescriptorValue, StringComparison.Ordinal))
                {
                    log?.Invoke($"Auto-Annotation migration ({bodyType}): kept default {category}:{existingDefault}; the slider rules' default {category}:{classifier.DefaultDescriptorValue} was dropped.");
                }
            }

            foreach (var valueRule in classifier.RuleList ?? new())
            {
                if (valueRule == null || string.IsNullOrWhiteSpace(valueRule.SelectedDescriptorValue)) continue;
                var converted = Convert(category, valueRule, bodyType, log);
                if (converted == null) continue;
                if (!existingSignatures.Add(Signature(converted))) continue;
                profile.Rules.Add(converted);
                added++;
            }
        }

        if (added > 0)
        {
            log?.Invoke($"Auto-Annotation migration ({bodyType}): converted {added} Label by Sliders rule(s) into Slider-condition rules.");
        }
        return added;
    }

    /// <summary>One value rule (OR of AND groups of slider tests) as an Auto-Annotation rule, or null
    /// when none of its groups could ever match.</summary>
    public static MeasurementRule? Convert(string category, DescriptorAssignmentRuleSet valueRule, string bodyType, Action<string>? log)
    {
        var rule = new MeasurementRule
        {
            Descriptor = new BodyShapeDescriptor.LabelSignature { Category = category, Value = valueRule.SelectedDescriptorValue },
            Gender = RuleGender.Either,
            IsDraft = false,
        };

        foreach (var group in valueRule.RuleListORlogic ?? new())
        {
            if (group?.RuleListANDlogic == null || group.RuleListANDlogic.Count == 0) continue;
            var conditions = new List<MeasurementCondition>();
            bool valid = true;
            foreach (var sliderRule in group.RuleListANDlogic)
            {
                if (sliderRule == null || string.IsNullOrEmpty(sliderRule.SliderName) || !TryMapComparator(sliderRule.Comparator, out var comparator))
                {
                    valid = false;
                    break;
                }
                conditions.Add(new MeasurementCondition
                {
                    Kind = MeasurementConditionKind.Slider,
                    SliderName = sliderRule.SliderName,
                    SliderType = sliderRule.SliderType,
                    Comparator = comparator,
                    Value = sliderRule.Value,
                });
            }
            if (!valid)
            {
                log?.Invoke($"Auto-Annotation migration ({bodyType}): dropped a {category}:{valueRule.SelectedDescriptorValue} slider group that could never match (blank slider or unknown comparator).");
                continue;
            }
            rule.GroupsORlogic.Add(new AndGatedMeasurementGroup { ConditionsANDlogic = conditions });
        }

        return rule.GroupsORlogic.Count == 0 ? null : rule;
    }

    /// <summary>Maps a legacy comparator string ("=", "!=", "&lt;=", "&gt;=", "&lt;", "&gt;").</summary>
    public static bool TryMapComparator(string? comparator, out MeasurementComparator mapped)
    {
        switch (comparator?.Trim())
        {
            case "=": mapped = MeasurementComparator.EqualTo; return true;
            case "!=": mapped = MeasurementComparator.NotEqualTo; return true;
            case "<=": mapped = MeasurementComparator.LessThanOrEqual; return true;
            case ">=": mapped = MeasurementComparator.GreaterThanOrEqual; return true;
            case "<": mapped = MeasurementComparator.LessThan; return true;
            case ">": mapped = MeasurementComparator.GreaterThan; return true;
            default: mapped = MeasurementComparator.GreaterThan; return false;
        }
    }

    /// <summary>Order-sensitive identity of a rule's output and predicate (ignores Id/Description).</summary>
    private static string Signature(MeasurementRule rule)
    {
        var parts = new List<string> { rule.Descriptor?.Category ?? "", rule.Descriptor?.Value ?? "", rule.Gender.ToString(), rule.IsDraft.ToString() };
        foreach (var g in rule.GroupsORlogic ?? new())
        {
            parts.Add("|" + (g?.IsDisabled ?? false));
            foreach (var c in g?.ConditionsANDlogic ?? new())
            {
                if (c == null) continue;
                parts.Add(string.Join(",", c.Kind, c.MeasurementName, c.SliderName, c.SliderType, c.Comparator,
                    c.Value.ToString(System.Globalization.CultureInfo.InvariantCulture), c.RefCategory, c.RefValue, c.Negate));
            }
        }
        return string.Join(";", parts);
    }
}
