using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using SynthEBD;
using Xunit;

namespace SynthEBD.Tests;

/// <summary>
/// Tests for <see cref="BodySlideAnnotator"/>'s per-weight-slot rule evaluation. Endpoint conditions
/// (Small/Big/Either) are weight-independent, so endpoint-only rules must keep the legacy whole-preset
/// behavior (annotate every slot or none); Interpolated conditions read the linear Small→Big blend at
/// each slot's weight, so their descriptors land only in the slots where the rule passes, and the
/// category default fills just the slots no rule matched. All tests drive the static core with a null
/// log callback (the instance wrapper only adds Logger routing).
/// </summary>
public class BodySlideAnnotatorTests
{
    private const string BodyType = "CBBE";
    private const string Category = "Shoulders";

    private static BodySlideSetting MakePreset(params (string Name, int Small, int Big)[] sliders)
    {
        var preset = new BodySlideSetting { Label = "TestPreset", SliderGroup = BodyType };
        foreach (var (name, small, big) in sliders)
        {
            preset.SliderValues[name] = new BodySlideSlider { SliderName = name, Small = small, Big = big };
        }
        return preset;
    }

    private static SliderClassificationRule Condition(string slider, BodySliderType type, string comparator, int value) =>
        new() { SliderName = slider, SliderType = type, Comparator = comparator, Value = value };

    /// <summary>One descriptor-value rule whose OR-list holds one AND group per <paramref name="orGroups"/> entry.</summary>
    private static DescriptorAssignmentRuleSet ValueRule(string descriptorValue, params SliderClassificationRule[][] orGroups) =>
        new()
        {
            SelectedDescriptorValue = descriptorValue,
            RuleListORlogic = orGroups.Select(g => new AndGatedSliderRuleGroup { RuleListANDlogic = g.ToList() }).ToList(),
        };

    private static Dictionary<string, SliderClassificationRulesByBodyType> Rules(string? defaultValue, params DescriptorAssignmentRuleSet[] valueRules) =>
        new()
        {
            [BodyType] = new SliderClassificationRulesByBodyType
            {
                BodyTypeGroup = BodyType,
                DescriptorClassifiers = new List<DescriptorClassificationRuleSet>
                {
                    new()
                    {
                        DescriptorCategory = Category,
                        DefaultDescriptorValue = defaultValue,
                        RuleList = valueRules.ToList(),
                    },
                },
            },
        };

    private static HashSet<BodyShapeDescriptor.LabelSignature> Universe(params string[] values) =>
        values.Select(v => new BodyShapeDescriptor.LabelSignature { Category = Category, Value = v }).ToHashSet();

    private static List<BodyShapeDescriptor.LabelSignature> Annotate(BodySlideSetting preset, Dictionary<string, SliderClassificationRulesByBodyType> rules, HashSet<BodyShapeDescriptor.LabelSignature> universe) =>
        BodySlideAnnotator.AnnotateBodySlide(preset, rules, universe, overwriteExistingAutoAnnotations: true, specifiedDescriptorCategory: null, logMessage: null);

    private static string[] SlotValues(BodySlideSetting preset, int weight) =>
        preset.BodyShapeDescriptorsByWeight[weight].Where(d => d.Category == Category).Select(d => d.Value).OrderBy(v => v).ToArray();

    [Fact]
    public void InterpolatedRule_AnnotatesOnlySlotsWhereBlendedValuePasses()
    {
        // ShoulderWidth blends 0 → 100 across the weight range, so "<= 50" holds at weights 0, 25, 50.
        var preset = MakePreset(("ShoulderWidth", 0, 100));
        var rules = Rules(defaultValue: null,
            ValueRule("Narrow", new[] { Condition("ShoulderWidth", BodySliderType.Interpolated, "<=", 50) }));

        var applied = Annotate(preset, rules, Universe("Narrow"));

        applied.Should().ContainSingle(d => d.Value == "Narrow");
        SlotValues(preset, 0).Should().Equal("Narrow");
        SlotValues(preset, 25).Should().Equal("Narrow");
        SlotValues(preset, 50).Should().Equal("Narrow");
        SlotValues(preset, 75).Should().BeEmpty();
        SlotValues(preset, 100).Should().BeEmpty();
        preset.AnnotationState.Should().Be(BodyShapeAnnotationState.RulesBased);
    }

    [Fact]
    public void EndpointOnlyRule_StillAnnotatesAllSlots_WhenItMatches()
    {
        var preset = MakePreset(("ShoulderWidth", 20, 60));
        var rules = Rules(defaultValue: null,
            ValueRule("Broad", new[] { Condition("ShoulderWidth", BodySliderType.Big, ">=", 50) }));

        Annotate(preset, rules, Universe("Broad"));

        foreach (var weight in new[] { 0, 25, 50, 75, 100 })
        {
            SlotValues(preset, weight).Should().Equal("Broad");
        }
    }

    [Fact]
    public void EndpointOnlyRule_AnnotatesNoSlots_WhenItDoesNotMatch()
    {
        var preset = MakePreset(("ShoulderWidth", 20, 40));
        var rules = Rules(defaultValue: null,
            ValueRule("Broad", new[] { Condition("ShoulderWidth", BodySliderType.Big, ">=", 50) }));

        var applied = Annotate(preset, rules, Universe("Broad"));

        applied.Should().BeEmpty();
        preset.EnumerateAllDescriptors().Should().BeEmpty();
        preset.AnnotationState.Should().Be(BodyShapeAnnotationState.None);
    }

    [Fact]
    public void EitherType_StillMatchesWhenOnlyOneEndpointPasses()
    {
        var preset = MakePreset(("ShoulderWidth", 30, 80));
        var rules = Rules(defaultValue: null,
            ValueRule("Narrow", new[] { Condition("ShoulderWidth", BodySliderType.Either, "<=", 40) }));

        Annotate(preset, rules, Universe("Narrow"));

        foreach (var weight in new[] { 0, 25, 50, 75, 100 })
        {
            SlotValues(preset, weight).Should().Equal("Narrow");
        }
    }

    [Fact]
    public void DefaultDescriptor_FillsOnlySlotsNoRuleMatched()
    {
        // "Broad" holds at weights 75 and 100; the default backfills 0/25/50 instead of being skipped.
        var preset = MakePreset(("ShoulderWidth", 0, 100));
        var rules = Rules(defaultValue: "Average",
            ValueRule("Broad", new[] { Condition("ShoulderWidth", BodySliderType.Interpolated, ">", 50) }));

        var applied = Annotate(preset, rules, Universe("Broad", "Average"));

        applied.Select(d => d.Value).Should().BeEquivalentTo(new[] { "Broad", "Average" });
        SlotValues(preset, 0).Should().Equal("Average");
        SlotValues(preset, 25).Should().Equal("Average");
        SlotValues(preset, 50).Should().Equal("Average");
        SlotValues(preset, 75).Should().Equal("Broad");
        SlotValues(preset, 100).Should().Equal("Broad");
    }

    [Fact]
    public void DefaultDescriptor_NotApplied_WhenEndpointRuleMatchedEverySlot()
    {
        // Legacy parity: a matching endpoint-only rule covers every slot, leaving the default nothing to fill.
        var preset = MakePreset(("ShoulderWidth", 20, 60));
        var rules = Rules(defaultValue: "Average",
            ValueRule("Broad", new[] { Condition("ShoulderWidth", BodySliderType.Big, ">=", 50) }));

        Annotate(preset, rules, Universe("Broad", "Average"));

        preset.EnumerateAllDescriptors().Should().OnlyContain(d => d.Value == "Broad");
    }

    [Fact]
    public void AndGroup_MixingEndpointAndInterpolatedConditions_EvaluatesBothPerSlot()
    {
        // Big >= 80 holds everywhere (weight-independent); Interpolated < 50 holds at weights 0 and 25 only.
        var preset = MakePreset(("ShoulderWidth", 0, 100));
        var rules = Rules(defaultValue: null,
            ValueRule("Narrow", new[]
            {
                Condition("ShoulderWidth", BodySliderType.Big, ">=", 80),
                Condition("ShoulderWidth", BodySliderType.Interpolated, "<", 50),
            }));

        Annotate(preset, rules, Universe("Narrow"));

        SlotValues(preset, 0).Should().Equal("Narrow");
        SlotValues(preset, 25).Should().Equal("Narrow");
        SlotValues(preset, 50).Should().BeEmpty();
        SlotValues(preset, 75).Should().BeEmpty();
        SlotValues(preset, 100).Should().BeEmpty();
    }

    [Fact]
    public void AndGroup_FailingEndpointCondition_SuppressesEverySlot()
    {
        var preset = MakePreset(("ShoulderWidth", 0, 100));
        var rules = Rules(defaultValue: null,
            ValueRule("Narrow", new[]
            {
                Condition("ShoulderWidth", BodySliderType.Big, ">=", 200),
                Condition("ShoulderWidth", BodySliderType.Interpolated, "<", 50),
            }));

        Annotate(preset, rules, Universe("Narrow"));

        preset.EnumerateAllDescriptors().Should().BeEmpty();
    }

    [Fact]
    public void OrGroups_UnionTheirPassingSlots()
    {
        // Branch 1 covers the low-weight slots, branch 2 the high-weight slots; weight 50 matches neither.
        var preset = MakePreset(("ShoulderWidth", 0, 100));
        var rules = Rules(defaultValue: null,
            ValueRule("Extreme",
                new[] { Condition("ShoulderWidth", BodySliderType.Interpolated, "<=", 25) },
                new[] { Condition("ShoulderWidth", BodySliderType.Interpolated, ">=", 75) }));

        Annotate(preset, rules, Universe("Extreme"));

        SlotValues(preset, 0).Should().Equal("Extreme");
        SlotValues(preset, 25).Should().Equal("Extreme");
        SlotValues(preset, 50).Should().BeEmpty();
        SlotValues(preset, 75).Should().Equal("Extreme");
        SlotValues(preset, 100).Should().Equal("Extreme");
    }

    [Fact]
    public void EqualsComparator_ComparesInterpolatedValueByNearestWholeNumber()
    {
        // Blend 0 → 10 gives 0, 2.5, 5, 7.5, 10 across the default slots; 2.5 rounds to 3 (away from zero),
        // so "= 3" matches only the weight-25 slot.
        var preset = MakePreset(("BellyMuscle", 0, 10));
        var rules = Rules(defaultValue: null,
            ValueRule("Defined", new[] { Condition("BellyMuscle", BodySliderType.Interpolated, "=", 3) }));

        Annotate(preset, rules, Universe("Defined"));

        SlotValues(preset, 0).Should().BeEmpty();
        SlotValues(preset, 25).Should().Equal("Defined");
        SlotValues(preset, 50).Should().BeEmpty();
        SlotValues(preset, 75).Should().BeEmpty();
        SlotValues(preset, 100).Should().BeEmpty();
    }

    [Fact]
    public void CustomWeightSlots_DriveInterpolatedEvaluation()
    {
        // Evaluation follows the preset's own slot keys, not the default layout.
        var preset = MakePreset(("ShoulderWidth", 0, 100));
        preset.BodyShapeDescriptorsByWeight = new Dictionary<int, HashSet<AnnotatedDescriptorSignature>>
        {
            { 10, new() },
            { 90, new() },
        };
        var rules = Rules(defaultValue: null,
            ValueRule("Narrow", new[] { Condition("ShoulderWidth", BodySliderType.Interpolated, "<=", 50) }));

        Annotate(preset, rules, Universe("Narrow"));

        SlotValues(preset, 10).Should().Equal("Narrow");
        SlotValues(preset, 90).Should().BeEmpty();
    }

    [Fact]
    public void ManuallyAnnotatedCategory_IsNeverRewritten()
    {
        var preset = MakePreset(("ShoulderWidth", 0, 100));
        preset.BodyShapeDescriptorsByWeight[0].Add(new AnnotatedDescriptorSignature(
            new BodyShapeDescriptor.LabelSignature { Category = Category, Value = "Broad" }, BodyShapeAnnotationSource.Manual));
        var rules = Rules(defaultValue: null,
            ValueRule("Narrow", new[] { Condition("ShoulderWidth", BodySliderType.Interpolated, "<=", 50) }));

        var applied = Annotate(preset, rules, Universe("Narrow", "Broad"));

        applied.Should().BeEmpty();
        preset.EnumerateAllDescriptors().Should().OnlyContain(d => d.Value == "Broad" && d.Source == BodyShapeAnnotationSource.Manual);
        preset.AnnotationState.Should().Be(BodyShapeAnnotationState.Manual);
    }

    [Fact]
    public void InterpolateSliderValue_IsLinearBetweenEndpoints()
    {
        var slider = new BodySlideSlider { SliderName = "ShoulderWidth", Small = 20, Big = 80 };

        BodySlideAnnotator.InterpolateSliderValue(slider, 0).Should().Be(20f);
        BodySlideAnnotator.InterpolateSliderValue(slider, 50).Should().Be(50f);
        BodySlideAnnotator.InterpolateSliderValue(slider, 100).Should().Be(80f);
        BodySlideAnnotator.InterpolateSliderValue(slider, 25).Should().Be(35f);
    }

    // ---------- PresetMatchesAllRules (the preset browser's "Filter Presets" predicate) ----------

    [Fact]
    public void PresetMatchesAllRules_EmptyOrNullRuleList_MatchesEverything()
    {
        var preset = MakePreset(("ShoulderWidth", 0, 100));

        BodySlideAnnotator.PresetMatchesAllRules(preset, null, 50).Should().BeTrue();
        BodySlideAnnotator.PresetMatchesAllRules(preset, new List<DescriptorAssignmentRuleSet>(), 50).Should().BeTrue();
    }

    [Fact]
    public void PresetMatchesAllRules_SingleInterpolatedRule_TracksWeight()
    {
        // MuscleAbs blends 0 -> 80: ">= 60" holds at weight 100 (80) and 75 (60), not at 50 (40).
        var preset = MakePreset(("MuscleAbs", 0, 80));
        var rules = new List<DescriptorAssignmentRuleSet>
        {
            ValueRule("Muscular", new[] { Condition("MuscleAbs", BodySliderType.Interpolated, ">=", 60) }),
        };

        BodySlideAnnotator.PresetMatchesAllRules(preset, rules, 100).Should().BeTrue();
        BodySlideAnnotator.PresetMatchesAllRules(preset, rules, 75).Should().BeTrue();
        BodySlideAnnotator.PresetMatchesAllRules(preset, rules, 50).Should().BeFalse();
    }

    [Fact]
    public void PresetMatchesAllRules_MultipleCheckedRules_Intersect()
    {
        var preset = MakePreset(("MuscleAbs", 0, 80), ("ShoulderWidth", 30, 30));
        var muscular = ValueRule("Muscular", new[] { Condition("MuscleAbs", BodySliderType.Interpolated, ">=", 60) });
        var narrow = ValueRule("Narrow", new[] { Condition("ShoulderWidth", BodySliderType.Big, "<=", 40) });
        var broad = ValueRule("Broad", new[] { Condition("ShoulderWidth", BodySliderType.Big, ">=", 50) });

        // Both satisfiable rules pass at weight 100 -> intersection matches.
        BodySlideAnnotator.PresetMatchesAllRules(preset, new List<DescriptorAssignmentRuleSet> { muscular, narrow }, 100)
            .Should().BeTrue();
        // Adding a rule this preset can never satisfy empties the intersection.
        BodySlideAnnotator.PresetMatchesAllRules(preset, new List<DescriptorAssignmentRuleSet> { muscular, narrow, broad }, 100)
            .Should().BeFalse();
    }

    [Fact]
    public void PresetMatchesAllRules_PresetLackingTheSlider_DoesNotMatch()
    {
        var preset = MakePreset(("ShoulderWidth", 0, 100));
        var rules = new List<DescriptorAssignmentRuleSet>
        {
            ValueRule("Muscular", new[] { Condition("MuscleAbs", BodySliderType.Interpolated, ">=", 60) }),
        };

        BodySlideAnnotator.PresetMatchesAllRules(preset, rules, 100).Should().BeFalse();
        BodySlideAnnotator.PresetMatchesAllRules(null, rules, 100).Should().BeFalse();
    }
}
