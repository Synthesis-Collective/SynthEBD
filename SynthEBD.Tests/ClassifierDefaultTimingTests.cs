using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using SynthEBD;
using Xunit;

namespace SynthEBD.Tests;

/// <summary>
/// Tests for <see cref="BodySlideMeasurementEvaluator.RunClassifierRules"/> — specifically the
/// "default materialization" behavior: a Category's configured default value is injected into the
/// matched-descriptor set the moment that Category resolves with no rule match, so it is visible to
/// later aggregator (<see cref="MeasurementConditionKind.DescriptorRef"/>) rules that reference it.
/// This is what lets a "secondary" rule depend on a "primary" Category's default — e.g.
/// Realism:UnrealisticChest's <c>[Belly:Normal]</c> branch firing when the belly fell through to its
/// Normal default — and lets the redundant explicit middle-bin rules be deleted in favor of the
/// per-Category default.
/// </summary>
public class ClassifierDefaultTimingTests
{
    private static MeasurementCondition Meas(string name, MeasurementComparator cmp, float val) =>
        new() { Kind = MeasurementConditionKind.Measurement, MeasurementName = name, Comparator = cmp, Value = val };

    private static MeasurementCondition Ref(string cat, string val, bool negate = false) =>
        new() { Kind = MeasurementConditionKind.DescriptorRef, RefCategory = cat, RefValue = val, Negate = negate };

    private static MeasurementRule Rule(string cat, string val, params MeasurementCondition[] andConds) =>
        new()
        {
            Descriptor = new BodyShapeDescriptor.LabelSignature { Category = cat, Value = val },
            GroupsORlogic = new() { new AndGatedMeasurementGroup { ConditionsANDlogic = andConds.ToList() } },
        };

    private static (List<(string Category, string Value)> RuleMatches, List<(string Category, string Value)> Defaults) Run(
        List<MeasurementRule> rules, Dictionary<string, float> meas, Dictionary<string, string> defaults)
    {
        var matches = new List<(string, string)>();
        var defs = BodySlideMeasurementEvaluator.RunClassifierRules(
            rules, meas, defaults,
            (rule, _) => matches.Add((rule.Descriptor.Category, rule.Descriptor.Value)));
        return (matches, defs.Select(d => (d.Category, d.Value)).ToList());
    }

    [Fact]
    public void Aggregator_ReferencingDefaultValue_Fires_WhenCategoryFallsToDefault()
    {
        // Belly:Flat needs belly_projection < 12.5; here it's 15 so Belly falls to its Normal default.
        // Realism:UnrealisticChest is listed FIRST (before the Belly rule) to also prove the topo sort
        // reorders it after the whole Belly category resolves + the default materializes.
        var rules = new List<MeasurementRule>
        {
            Rule("Realism", "UnrealisticChest", Meas("chest_projection", MeasurementComparator.GreaterThan, 17f), Ref("Belly", "Normal")),
            Rule("Belly", "Flat", Meas("belly_projection", MeasurementComparator.LessThan, 12.5f)),
        };
        var defaults = new Dictionary<string, string> { ["Belly"] = "Normal" };
        var meas = new Dictionary<string, float> { ["chest_projection"] = 18f, ["belly_projection"] = 15f };

        var (matches, defs) = Run(rules, meas, defaults);

        matches.Should().Contain(("Realism", "UnrealisticChest"));
        matches.Should().NotContain(("Belly", "Flat"));
        defs.Should().Contain(("Belly", "Normal")); // belly still emitted as a default descriptor
    }

    [Fact]
    public void Aggregator_ReferencingDefaultValue_DoesNotFire_WhenCategoryMatchedADifferentValue()
    {
        // belly_projection = 10 -> Belly:Flat fires, so Belly is NOT Normal; the [Belly:Normal]
        // branch must NOT be satisfied and the default must not be materialized.
        var rules = new List<MeasurementRule>
        {
            Rule("Belly", "Flat", Meas("belly_projection", MeasurementComparator.LessThan, 12.5f)),
            Rule("Realism", "UnrealisticChest", Meas("chest_projection", MeasurementComparator.GreaterThan, 17f), Ref("Belly", "Normal")),
        };
        var defaults = new Dictionary<string, string> { ["Belly"] = "Normal" };
        var meas = new Dictionary<string, float> { ["chest_projection"] = 18f, ["belly_projection"] = 10f };

        var (matches, defs) = Run(rules, meas, defaults);

        matches.Should().Contain(("Belly", "Flat"));
        matches.Should().NotContain(("Realism", "UnrealisticChest"));
        defs.Should().NotContain(("Belly", "Normal")); // Belly covered by a rule -> no default
    }

    [Fact]
    public void Aggregator_ReferencingRuleProducedValue_StillFires()
    {
        // Regression guard: a cross-category ref to a value a rule actually produced keeps working.
        var rules = new List<MeasurementRule>
        {
            Rule("BustSize", "Large", Meas("L_Breast_Vol", MeasurementComparator.GreaterThanOrEqual, 750f)),
            Rule("Chest", "Busty", Ref("BustSize", "Large")),
        };
        var defaults = new Dictionary<string, string> { ["Chest"] = "Medium" };
        var meas = new Dictionary<string, float> { ["L_Breast_Vol"] = 800f };

        var (matches, defs) = Run(rules, meas, defaults);

        matches.Should().Contain(("BustSize", "Large"));
        matches.Should().Contain(("Chest", "Busty"));
        defs.Should().NotContain(("Chest", "Medium"));
    }

    [Fact]
    public void RulelessDefaultCategory_IsVisibleToAggregator()
    {
        // "Tone" has a default but NO producer rule at all -> materialized up front, visible to the
        // aggregator regardless of order. Mirrors deleting every explicit rule of a defaulted Category.
        var rules = new List<MeasurementRule>
        {
            Rule("Build", "Derived", Ref("Tone", "Soft")),
        };
        var defaults = new Dictionary<string, string> { ["Tone"] = "Soft" };
        var meas = new Dictionary<string, float>();

        var (matches, defs) = Run(rules, meas, defaults);

        matches.Should().Contain(("Build", "Derived"));
        defs.Should().Contain(("Tone", "Soft"));
    }

    [Fact]
    public void IntraCategoryNegatedRef_StillOrdersAfterReferencedRule_NoSelfCycle()
    {
        // Belly:Chubby references [NOT Belly:Pregnant] (same Category). Intra-category refs keep the
        // narrow dependency, so Chubby is ordered after Pregnant (not dropped as a self-cycle).
        var rules = new List<MeasurementRule>
        {
            Rule("Belly", "Chubby",
                 Meas("belly_projection", MeasurementComparator.GreaterThanOrEqual, 16f),
                 Ref("Belly", "Pregnant", negate: true)),
            Rule("Belly", "Pregnant", Meas("sternum_to_belly", MeasurementComparator.GreaterThanOrEqual, 2f)),
        };
        var defaults = new Dictionary<string, string> { ["Belly"] = "Normal" };

        // Pregnant does NOT fire -> Chubby's NOT-ref is satisfied -> Chubby fires.
        var (m1, _) = Run(rules, new Dictionary<string, float> { ["belly_projection"] = 17f, ["sternum_to_belly"] = 0f }, defaults);
        m1.Should().Contain(("Belly", "Chubby"));
        m1.Should().NotContain(("Belly", "Pregnant"));

        // Pregnant fires -> Chubby's NOT-ref fails -> only Pregnant.
        var (m2, _) = Run(rules, new Dictionary<string, float> { ["belly_projection"] = 17f, ["sternum_to_belly"] = 3f }, defaults);
        m2.Should().Contain(("Belly", "Pregnant"));
        m2.Should().NotContain(("Belly", "Chubby"));
    }

    [Fact]
    public void DefaultsList_MatchesUncoveredCategories_InDictionaryOrder()
    {
        // Output defaults = exactly the Categories no rule produced, in defaults-dictionary order.
        var rules = new List<MeasurementRule>
        {
            Rule("Hips", "Wide", Meas("hip_to_torso", MeasurementComparator.GreaterThan, 1.1f)),
        };
        var defaults = new Dictionary<string, string>
        {
            ["Hips"] = "Normal",   // matched by rule -> suppressed
            ["Waist"] = "Neutral", // uncovered -> emitted
            ["Thighs"] = "Normal", // uncovered -> emitted
        };
        var meas = new Dictionary<string, float> { ["hip_to_torso"] = 1.2f };

        var (matches, defs) = Run(rules, meas, defaults);

        matches.Should().Contain(("Hips", "Wide"));
        defs.Should().Equal(("Waist", "Neutral"), ("Thighs", "Normal"));
    }
}
