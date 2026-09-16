using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using FluentAssertions;
using Xunit;

namespace SynthEBD.Tests;

/// <summary>
/// Coverage for <c>VM_BodyTypeProfileEditor.CollectDescriptorMeasurementNames</c> — the shared
/// resolver behind the "Show Measurements" viewer overlay (both the Rules tab and Match Presets)
/// and the Rules tab's per-row "name=value" readout.
/// <para>The interesting case is a Category's <b>default</b> value. A default is assigned exactly
/// when no sibling rule fires, so it normally owns no rules and therefore references no
/// measurements — which left every one of those readouts blank for it. The resolver falls back to
/// the union across the Category's other rules, which are the measurements whose failure to fire
/// produced the default in the first place. That mirrors what the margin scorer already does with
/// the default's score (see <see cref="MeasurementRuleDefaultScoreTests"/>).</para>
/// <para>Like the scorer tests, the resolver is a non-public static on the editor VM and is
/// invoked via reflection over a synthetic rule graph.</para>
/// </summary>
public class DescriptorMeasurementNameTests
{
    // ---------- synthetic-graph builders ----------

    private static VM_MeasurementRule MakeRule(
        string category, string value, params AndGatedMeasurementGroup[] groups)
    {
        var model = new MeasurementRule
        {
            Descriptor = new BodyShapeDescriptor.LabelSignature { Category = category, Value = value },
            Gender = RuleGender.Either,
            GroupsORlogic = groups.ToList(),
        };
        return new VM_MeasurementRule(model, null!);
    }

    private static MeasurementCondition Meas(string name) => new()
    {
        MeasurementName = name,
        Comparator = MeasurementComparator.GreaterThanOrEqual,
        Value = 1f,
    };

    private static MeasurementCondition Ref(string category, string value) => new()
    {
        Kind = MeasurementConditionKind.DescriptorRef,
        RefCategory = category,
        RefValue = value,
    };

    private static AndGatedMeasurementGroup Grp(params MeasurementCondition[] conditions) => new()
    {
        ConditionsANDlogic = conditions.ToList(),
    };

    // ---------- reflection shim ----------

    private static List<string> Collect(
        IEnumerable<VM_MeasurementRule> allRules, string category, string value, string categoryDefault)
    {
        var mi = typeof(VM_BodyTypeProfileEditor).GetMethod(
            "CollectDescriptorMeasurementNames", BindingFlags.NonPublic | BindingFlags.Static);
        mi.Should().NotBeNull(
            "CollectDescriptorMeasurementNames should still exist under this name — update the test if it was renamed");
        return (List<string>)mi!.Invoke(null, new object?[] { allRules, category, value, categoryDefault })!;
    }

    /// <summary>Category shaped like the motivating CBBE/3BA setup: Belly:Flat and Belly:Chubby
    /// carry the live rules, Belly:Normal is the Category default with no rule of its own.</summary>
    private static List<VM_MeasurementRule> BellyRules() => new()
    {
        MakeRule("Belly", "Flat", Grp(Meas("belly_depth"))),
        MakeRule("Belly", "Chubby", Grp(Meas("belly_depth"), Meas("belly_ratio"))),
        MakeRule("Shape", "Pear", Grp(Meas("hip_width"))),
    };

    [Fact]
    public void ValueWithOwnRules_ReturnsOnlyItsOwnMeasurements()
    {
        Collect(BellyRules(), "Belly", "Flat", "Normal")
            .Should().Equal("belly_depth");
    }

    [Fact]
    public void DefaultValueWithNoRules_FallsBackToEveryOtherRuleInTheCategory()
    {
        // Belly:Normal owns nothing, so the readout used to come back empty. The fallback pulls
        // the union across Flat + Chubby — and stops at the Category boundary, so Shape:Pear's
        // hip_width is not dragged in.
        Collect(BellyRules(), "Belly", "Normal", "Normal")
            .Should().Equal("belly_depth", "belly_ratio");
    }

    [Fact]
    public void NonDefaultValueWithNoRules_StaysEmpty()
    {
        // Nothing defines Belly:Fat, so there is nothing to draw. Only the default is determined
        // by its siblings; falling back here would invent a relationship that doesn't exist.
        Collect(BellyRules(), "Belly", "Fat", "Normal")
            .Should().BeEmpty();
    }

    [Fact]
    public void DefaultValueWithItsOwnRules_DoesNotFallBack()
    {
        var rules = BellyRules();
        rules.Add(MakeRule("Belly", "Normal", Grp(Meas("belly_softness"))));

        // The default carries a live rule here, so its own measurement is the honest answer and
        // the sibling union must not dilute it.
        Collect(rules, "Belly", "Normal", "Normal")
            .Should().Equal("belly_softness");
    }

    [Fact]
    public void EmptyValue_IsCategoryLevelAndSpansEveryRule()
    {
        Collect(BellyRules(), "Belly", "", "Normal")
            .Should().Equal("belly_depth", "belly_ratio");
    }

    [Fact]
    public void DescriptorRefConditions_ContributeNoMeasurementNames()
    {
        // An aggregator value built purely from DescriptorRefs references no measurement of its
        // own. The overlay doesn't tunnel into the referenced descriptors, so it stays empty
        // rather than silently drawing the producers' lines.
        var rules = new List<VM_MeasurementRule>
        {
            MakeRule("Build", "Powerful", Grp(Ref("Arms", "Thick"), Ref("Belly", "Flat"))),
            MakeRule("Arms", "Thick", Grp(Meas("arm_volume"))),
        };

        Collect(rules, "Build", "Powerful", "").Should().BeEmpty();
    }

    [Fact]
    public void DuplicateMeasurementAcrossRules_IsDedupedInEncounterOrder()
    {
        var rules = new List<VM_MeasurementRule>
        {
            MakeRule("Belly", "Flat", Grp(Meas("belly_depth"))),
            MakeRule("Belly", "Chubby", Grp(Meas("belly_ratio"), Meas("belly_depth"))),
        };

        Collect(rules, "Belly", "", "").Should().Equal("belly_depth", "belly_ratio");
    }

    [Fact]
    public void NullRulesOrEmptyCategory_ReturnEmptyRatherThanThrowing()
    {
        Collect(null!, "Belly", "Normal", "Normal").Should().BeEmpty();
        Collect(BellyRules(), "", "Normal", "Normal").Should().BeEmpty();
    }
}
