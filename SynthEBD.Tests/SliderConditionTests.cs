using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using SynthEBD;
using Xunit;

namespace SynthEBD.Tests;

/// <summary>
/// Slider-condition rules, exercised the way existing users get them: legacy Label by Sliders rules
/// are converted by <see cref="SliderRuleMigration"/> and then evaluated per weight slot by
/// <see cref="AutoAnnotationEngine"/>. Each case is a behavior the old slider annotator had, so these
/// pin that the conversion keeps it: endpoint conditions (Small/Big/Either) are weight-independent and
/// label every slot or none, Interpolated conditions read the Small-to-Big blend at each slot, the
/// category default fills only the slots no rule matched, = / != round the slider value, and a manual
/// label owns its (weight slot, category).
/// </summary>
public class SliderConditionTests
{
    private const string BodyType = "CBBE";
    private const string Category = "Shoulders";

    private static BodySlideSetting MakePreset(params (string Name, float Small, float Big)[] sliders)
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

    /// <summary>One legacy descriptor-value rule whose OR-list holds one AND group per <paramref name="orGroups"/> entry.</summary>
    private static DescriptorAssignmentRuleSet ValueRule(string descriptorValue, params SliderClassificationRule[][] orGroups) =>
        new()
        {
            SelectedDescriptorValue = descriptorValue,
            RuleListORlogic = orGroups.Select(g => new AndGatedSliderRuleGroup { RuleListANDlogic = g.ToList() }).ToList(),
        };

    /// <summary>Legacy rules for one category, converted into a fresh Auto-Annotation profile.</summary>
    private static BodyTypeProfile Profile(string? defaultValue, params DescriptorAssignmentRuleSet[] valueRules)
    {
        var legacy = new SliderClassificationRulesByBodyType
        {
            BodyTypeGroup = BodyType,
            DescriptorClassifiers = new List<DescriptorClassificationRuleSet>
            {
                new() { DescriptorCategory = Category, DefaultDescriptorValue = defaultValue, RuleList = valueRules.ToList() },
            },
        };
        BodyTypeProfile? profile = null;
        SliderRuleMigration.FoldInto(legacy, ref profile, BodyType, null);
        return profile ?? new BodyTypeProfile { BodyTypeName = BodyType };
    }

    private static string[] SlotValues(BodyTypeProfile profile, BodySlideSetting preset, int weight) =>
        AutoAnnotationEngine.DeriveForSlot(profile, preset, Gender.Female, weight, measurements: null)
            .Descriptors.Where(d => d.Category == Category).Select(d => d.Value).OrderBy(v => v).ToArray();

    [Fact]
    public void InterpolatedRule_LabelsOnlySlotsWhereBlendedValuePasses()
    {
        // ShoulderWidth blends 0 -> 100 across the weight range, so "<= 50" holds at weights 0, 25, 50.
        var preset = MakePreset(("ShoulderWidth", 0, 100));
        var profile = Profile(null, ValueRule("Narrow", new[] { Condition("ShoulderWidth", BodySliderType.Interpolated, "<=", 50) }));

        SlotValues(profile, preset, 0).Should().Equal("Narrow");
        SlotValues(profile, preset, 25).Should().Equal("Narrow");
        SlotValues(profile, preset, 50).Should().Equal("Narrow");
        SlotValues(profile, preset, 75).Should().BeEmpty();
        SlotValues(profile, preset, 100).Should().BeEmpty();
    }

    [Fact]
    public void EndpointRule_LabelsEverySlotOrNone()
    {
        var matching = MakePreset(("ShoulderWidth", 20, 60));
        var notMatching = MakePreset(("ShoulderWidth", 20, 40));
        var profile = Profile(null, ValueRule("Broad", new[] { Condition("ShoulderWidth", BodySliderType.Big, ">=", 50) }));

        foreach (var weight in new[] { 0, 25, 50, 75, 100 })
        {
            SlotValues(profile, matching, weight).Should().Equal("Broad");
            SlotValues(profile, notMatching, weight).Should().BeEmpty();
        }
    }

    [Fact]
    public void EitherType_MatchesWhenOnlyOneEndpointPasses()
    {
        var preset = MakePreset(("ShoulderWidth", 30, 80));
        var profile = Profile(null, ValueRule("Narrow", new[] { Condition("ShoulderWidth", BodySliderType.Either, "<=", 40) }));

        foreach (var weight in new[] { 0, 25, 50, 75, 100 })
        {
            SlotValues(profile, preset, weight).Should().Equal("Narrow");
        }
    }

    [Fact]
    public void Default_FillsOnlySlotsNoRuleMatched()
    {
        var preset = MakePreset(("ShoulderWidth", 0, 100));
        var profile = Profile("Average", ValueRule("Broad", new[] { Condition("ShoulderWidth", BodySliderType.Interpolated, ">", 50) }));

        SlotValues(profile, preset, 0).Should().Equal("Average");
        SlotValues(profile, preset, 25).Should().Equal("Average");
        SlotValues(profile, preset, 50).Should().Equal("Average");
        SlotValues(profile, preset, 75).Should().Equal("Broad");
        SlotValues(profile, preset, 100).Should().Equal("Broad");
    }

    [Fact]
    public void AndGroup_MixingEndpointAndInterpolatedConditions_EvaluatesBothPerSlot()
    {
        // Big >= 80 holds everywhere; Interpolated < 50 holds at weights 0 and 25 only.
        var preset = MakePreset(("ShoulderWidth", 0, 100));
        var profile = Profile(null, ValueRule("Narrow", new[]
        {
            Condition("ShoulderWidth", BodySliderType.Big, ">=", 80),
            Condition("ShoulderWidth", BodySliderType.Interpolated, "<", 50),
        }));

        SlotValues(profile, preset, 0).Should().Equal("Narrow");
        SlotValues(profile, preset, 25).Should().Equal("Narrow");
        SlotValues(profile, preset, 50).Should().BeEmpty();
        SlotValues(profile, preset, 100).Should().BeEmpty();
    }

    [Fact]
    public void OrGroups_UnionTheirPassingSlots()
    {
        var preset = MakePreset(("ShoulderWidth", 0, 100));
        var profile = Profile(null, ValueRule("Extreme",
            new[] { Condition("ShoulderWidth", BodySliderType.Interpolated, "<=", 25) },
            new[] { Condition("ShoulderWidth", BodySliderType.Interpolated, ">=", 75) }));

        SlotValues(profile, preset, 0).Should().Equal("Extreme");
        SlotValues(profile, preset, 25).Should().Equal("Extreme");
        SlotValues(profile, preset, 50).Should().BeEmpty();
        SlotValues(profile, preset, 75).Should().Equal("Extreme");
        SlotValues(profile, preset, 100).Should().Equal("Extreme");
    }

    [Fact]
    public void EqualsComparator_ComparesByNearestWholeNumber()
    {
        // Blend 0 -> 10 gives 0, 2.5, 5, 7.5, 10; 2.5 rounds to 3 (away from zero), so "= 3" matches only weight 25.
        var preset = MakePreset(("BellyMuscle", 0, 10));
        var profile = Profile(null, ValueRule("Defined", new[] { Condition("BellyMuscle", BodySliderType.Interpolated, "=", 3) }));

        SlotValues(profile, preset, 0).Should().BeEmpty();
        SlotValues(profile, preset, 25).Should().Equal("Defined");
        SlotValues(profile, preset, 50).Should().BeEmpty();
    }

    [Fact]
    public void PresetLackingTheSlider_FailsTheCondition()
    {
        var preset = MakePreset(("ShoulderWidth", 0, 100));
        var profile = Profile(null, ValueRule("Muscular", new[] { Condition("MuscleAbs", BodySliderType.Interpolated, ">=", 0) }));

        SlotValues(profile, preset, 100).Should().BeEmpty();
    }

    [Fact]
    public void ManualLabel_OwnsItsCategoryOnlyInItsOwnSlot()
    {
        var preset = MakePreset(("ShoulderWidth", 0, 100));
        preset.BodyShapeDescriptorsByWeight[0].Add(new AnnotatedDescriptorSignature(
            new BodyShapeDescriptor.LabelSignature { Category = Category, Value = "Broad" }, BodyShapeAnnotationSource.Manual));
        var profile = Profile("Average", ValueRule("Narrow", new[] { Condition("ShoulderWidth", BodySliderType.Interpolated, "<=", 50) }));

        SlotValues(profile, preset, 0).Should().BeEmpty("the hand label owns weight 0");
        SlotValues(profile, preset, 25).Should().Equal("Narrow");
        SlotValues(profile, preset, 100).Should().Equal("Average");
    }

    [Fact]
    public void ManuallyEmptiedCategory_GetsNoAutomaticLabel()
    {
        var preset = MakePreset(("ShoulderWidth", 0, 100));
        preset.ManualCategoriesByWeight[50] = new HashSet<string> { Category };
        var profile = Profile("Average", ValueRule("Narrow", new[] { Condition("ShoulderWidth", BodySliderType.Interpolated, "<=", 50) }));

        SlotValues(profile, preset, 50).Should().BeEmpty();
        SlotValues(profile, preset, 25).Should().Equal("Narrow");
    }

    [Fact]
    public void InterpolateSliderValue_IsLinearBetweenEndpoints()
    {
        var slider = new BodySlideSlider { SliderName = "ShoulderWidth", Small = 20, Big = 80 };

        MeasurementMath.InterpolateSliderValue(slider, 0).Should().Be(20f);
        MeasurementMath.InterpolateSliderValue(slider, 50).Should().Be(50f);
        MeasurementMath.InterpolateSliderValue(slider, 100).Should().Be(80f);
        MeasurementMath.InterpolateSliderValue(slider, 25).Should().Be(35f);
    }

    // ---------- Migration details ----------

    [Fact]
    public void Migration_MapsComparators_AndDropsGroupsThatCouldNeverMatch()
    {
        var legacy = ValueRule("Narrow",
            new[] { Condition("ShoulderWidth", BodySliderType.Small, "!=", 5) },
            new[] { Condition("ShoulderWidth", BodySliderType.Small, "~", 5) },  // unknown comparator
            System.Array.Empty<SliderClassificationRule>());                     // empty AND group

        var rule = SliderRuleMigration.Convert(Category, legacy, BodyType, null);

        rule.Should().NotBeNull();
        rule!.GroupsORlogic.Should().ContainSingle();
        var condition = rule.GroupsORlogic[0].ConditionsANDlogic.Single();
        condition.Kind.Should().Be(MeasurementConditionKind.Slider);
        condition.Comparator.Should().Be(MeasurementComparator.NotEqualTo);
        condition.SliderType.Should().Be(BodySliderType.Small);
        condition.Value.Should().Be(5f);
        rule.Gender.Should().Be(RuleGender.Either);
        rule.IsDraft.Should().BeFalse();
    }

    [Fact]
    public void Migration_KeepsAnExistingDefault_AndIsIdempotent()
    {
        var legacy = new SliderClassificationRulesByBodyType
        {
            BodyTypeGroup = BodyType,
            DescriptorClassifiers = new()
            {
                new() { DescriptorCategory = Category, DefaultDescriptorValue = "Average",
                    RuleList = new() { ValueRule("Narrow", new[] { Condition("ShoulderWidth", BodySliderType.Big, "<=", 40) }) } },
                new() { DescriptorCategory = "Belly", DefaultDescriptorValue = "Flat", RuleList = new() },
            },
        };
        BodyTypeProfile? profile = new() { BodyTypeName = BodyType };
        profile.DefaultDescriptorValuesByCategory[Category] = "Medium";

        SliderRuleMigration.FoldInto(legacy, ref profile, BodyType, null).Should().Be(1);
        SliderRuleMigration.FoldInto(legacy, ref profile, BodyType, null).Should().Be(0, "a second run adds nothing");

        profile!.Rules.Should().ContainSingle();
        profile.DefaultDescriptorValuesByCategory[Category].Should().Be("Medium", "the profile's own default wins");
        profile.DefaultDescriptorValuesByCategory["Belly"].Should().Be("Flat", "a missing default is filled");
    }

    [Fact]
    public void Migration_CreatesAProfile_WhenThereIsNone()
    {
        BodyTypeProfile? profile = null;
        var legacy = new SliderClassificationRulesByBodyType
        {
            BodyTypeGroup = BodyType,
            DescriptorClassifiers = new() { new() { DescriptorCategory = Category, RuleList = new() { ValueRule("Narrow", new[] { Condition("ShoulderWidth", BodySliderType.Big, "<=", 40) }) } } },
        };

        SliderRuleMigration.FoldInto(legacy, ref profile, BodyType, null);

        profile.Should().NotBeNull();
        profile!.BodyTypeName.Should().Be(BodyType);
        profile.Measurements.Should().BeEmpty();
    }

    [Fact]
    public void EmptySkeleton_IsNotMigrated()
    {
        BodyTypeProfile? profile = null;
        var skeleton = new SliderClassificationRulesByBodyType
        {
            BodyTypeGroup = BodyType,
            DescriptorClassifiers = new() { new() { DescriptorCategory = Category, RuleList = new() } },
        };

        SliderRuleMigration.FoldInto(skeleton, ref profile, BodyType, null).Should().Be(0);
        profile.Should().BeNull();
    }
}
