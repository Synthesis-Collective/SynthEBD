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
/// ExtremeShape:Chest's <c>[Belly:Normal]</c> branch firing when the belly fell through to its
/// Normal default — and lets the redundant explicit middle-bin rules be deleted in favor of the
/// per-Category default.
///
/// Also covers external-descriptor seeding (stored Manual annotations gate DescriptorRef conditions
/// like rule output, and a seeded Category suppresses its own default) and the unified engine
/// (<see cref="AutoAnnotationEngine"/>): Slider, Measurement and DescriptorRef rules in one pass, and
/// which categories can be evaluated when a measurement isn't cached.
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
        List<MeasurementRule> rules, Dictionary<string, float> meas, Dictionary<string, string> defaults,
        IReadOnlyCollection<(string Category, string Value)>? seeds = null)
    {
        var matches = new List<(string, string)>();
        var defs = BodySlideMeasurementEvaluator.RunClassifierRules(
            rules, meas, defaults,
            (rule, _) => matches.Add((rule.Descriptor.Category, rule.Descriptor.Value)),
            seeds);
        return (matches, defs.Select(d => (d.Category, d.Value)).ToList());
    }

    [Fact]
    public void Aggregator_ReferencingDefaultValue_Fires_WhenCategoryFallsToDefault()
    {
        // Belly:Flat needs belly_projection < 12.5; here it's 15 so Belly falls to its Normal default.
        // ExtremeShape:Chest is listed FIRST (before the Belly rule) to also prove the topo sort
        // reorders it after the whole Belly category resolves + the default materializes.
        var rules = new List<MeasurementRule>
        {
            Rule("ExtremeShape", "Chest", Meas("chest_projection", MeasurementComparator.GreaterThan, 17f), Ref("Belly", "Normal")),
            Rule("Belly", "Flat", Meas("belly_projection", MeasurementComparator.LessThan, 12.5f)),
        };
        var defaults = new Dictionary<string, string> { ["Belly"] = "Normal" };
        var meas = new Dictionary<string, float> { ["chest_projection"] = 18f, ["belly_projection"] = 15f };

        var (matches, defs) = Run(rules, meas, defaults);

        matches.Should().Contain(("ExtremeShape", "Chest"));
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
            Rule("ExtremeShape", "Chest", Meas("chest_projection", MeasurementComparator.GreaterThan, 17f), Ref("Belly", "Normal")),
        };
        var defaults = new Dictionary<string, string> { ["Belly"] = "Normal" };
        var meas = new Dictionary<string, float> { ["chest_projection"] = 18f, ["belly_projection"] = 10f };

        var (matches, defs) = Run(rules, meas, defaults);

        matches.Should().Contain(("Belly", "Flat"));
        matches.Should().NotContain(("ExtremeShape", "Chest"));
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

    // ---------- External-descriptor seeding (Label-by-Sliders / manual annotations) ----------

    /// <summary>The canonical cross-system scenario: a slider rule assigned Belly:Muscular
    /// (MuscleAbs Interp >= 60 in Label by Sliders); the measurement-side Belly:Chubby / Belly:Fat
    /// rules exclude [Belly:Muscular]; and the nested aggregator Build:Powerful requires
    /// NOT [Belly:Chubby] AND NOT [Belly:Fat] — so it must see the slider-aware outcome of
    /// Chubby/Fat, not their raw measurement result.</summary>
    [Fact]
    public void ExternalSeed_BlocksNegatedRef_AndPropagatesToNestedAggregator()
    {
        var rules = new List<MeasurementRule>
        {
            Rule("Build", "Powerful",
                 Meas("arm_thickness", MeasurementComparator.GreaterThanOrEqual, 1.2f),
                 Ref("Belly", "Chubby", negate: true),
                 Ref("Belly", "Fat", negate: true)),
            Rule("Belly", "Chubby",
                 Meas("belly_projection", MeasurementComparator.GreaterThanOrEqual, 16f),
                 Ref("Belly", "Muscular", negate: true)),
            Rule("Belly", "Fat",
                 Meas("belly_projection", MeasurementComparator.GreaterThanOrEqual, 20f),
                 Ref("Belly", "Muscular", negate: true)),
        };
        var defaults = new Dictionary<string, string>();
        // A protruding belly that would read Chubby AND Fat on measurements alone.
        var meas = new Dictionary<string, float> { ["belly_projection"] = 22f, ["arm_thickness"] = 1.5f };

        // Without the seed (legacy behavior): measurements alone -> Chubby + Fat fire, Powerful is blocked.
        var (unseeded, _) = Run(rules, meas, defaults);
        unseeded.Should().Contain(("Belly", "Chubby"));
        unseeded.Should().Contain(("Belly", "Fat"));
        unseeded.Should().NotContain(("Build", "Powerful"));

        // With the slider-assigned Belly:Muscular seeded: Chubby/Fat are excluded, so Powerful fires.
        var seeds = new[] { ("Belly", "Muscular") };
        var (seeded, seededDefs) = Run(rules, meas, defaults, seeds);
        seeded.Should().NotContain(("Belly", "Chubby"));
        seeded.Should().NotContain(("Belly", "Fat"));
        seeded.Should().Contain(("Build", "Powerful"));

        // Seeds gate only — they are never emitted as classifier output.
        seeded.Should().NotContain(("Belly", "Muscular"));
        seededDefs.Should().NotContain(("Belly", "Muscular"));
    }

    [Fact]
    public void ExternalSeed_SatisfiesPositiveRef()
    {
        // No measurement rule produces Belly:Muscular — only the seed can satisfy the positive ref.
        var rules = new List<MeasurementRule>
        {
            Rule("Build", "Buff", Ref("Belly", "Muscular")),
        };
        var defaults = new Dictionary<string, string>();
        var meas = new Dictionary<string, float>();

        var (unseeded, _) = Run(rules, meas, defaults);
        unseeded.Should().BeEmpty();

        var (seeded, _) = Run(rules, meas, defaults, new[] { ("Belly", "Muscular") });
        seeded.Should().Contain(("Build", "Buff"));
    }

    [Fact]
    public void SeededCategory_SuppressesDefault_MaterializationAndEmission()
    {
        // Belly has a producer rule (Chubby, which won't fire at belly_projection = 10) and a
        // Normal default. Unseeded, the default materializes (Build:Soft's [Belly:Normal] ref
        // fires) and emits. Seeded with the slider's Belly:Muscular, the Category is covered by a
        // senior source: no Normal materialization (Soft must NOT fire), no Normal emission —
        // otherwise a muscular preset would carry Belly:Normal alongside Belly:Muscular.
        var rules = new List<MeasurementRule>
        {
            Rule("Belly", "Chubby",
                 Meas("belly_projection", MeasurementComparator.GreaterThanOrEqual, 16f),
                 Ref("Belly", "Muscular", negate: true)),
            Rule("Build", "Soft", Ref("Belly", "Normal")),
        };
        var defaults = new Dictionary<string, string> { ["Belly"] = "Normal" };
        var meas = new Dictionary<string, float> { ["belly_projection"] = 10f };

        var (unseeded, unseededDefs) = Run(rules, meas, defaults);
        unseeded.Should().Contain(("Build", "Soft"));
        unseededDefs.Should().Contain(("Belly", "Normal"));

        var (seeded, seededDefs) = Run(rules, meas, defaults, new[] { ("Belly", "Muscular") });
        seeded.Should().NotContain(("Build", "Soft"));
        seededDefs.Should().NotContain(("Belly", "Normal"));
    }

    [Fact]
    public void SeededCategory_SuppressesProducerlessDefault()
    {
        // Same suppression through the up-front materialization path: the Category has a default
        // but NO producer rule at all (Belly is labeled purely by sliders on this profile).
        var rules = new List<MeasurementRule>
        {
            Rule("Build", "Soft", Ref("Belly", "Normal")),
        };
        var defaults = new Dictionary<string, string> { ["Belly"] = "Normal" };
        var meas = new Dictionary<string, float>();

        var (unseeded, unseededDefs) = Run(rules, meas, defaults);
        unseeded.Should().Contain(("Build", "Soft"));
        unseededDefs.Should().Contain(("Belly", "Normal"));

        var (seeded, seededDefs) = Run(rules, meas, defaults, new[] { ("Belly", "Muscular") });
        seeded.Should().NotContain(("Build", "Soft"));
        seededDefs.Should().NotContain(("Belly", "Normal"));
    }

    [Fact]
    public void Seed_InUnrelatedCategory_DoesNotDisturbOtherDefaults()
    {
        // Seeding Belly must not suppress the Waist default (different Category).
        var rules = new List<MeasurementRule>();
        var defaults = new Dictionary<string, string> { ["Waist"] = "Neutral" };
        var meas = new Dictionary<string, float>();

        var (_, defs) = Run(rules, meas, defaults, new[] { ("Belly", "Muscular") });
        defs.Should().Contain(("Waist", "Neutral"));
    }

    // ---------- CollectExternalDescriptors: stored-annotation slot policy ----------

    private static AnnotatedDescriptorSignature Annotated(string cat, string val, BodyShapeAnnotationSource source) =>
        new(new BodyShapeDescriptor.LabelSignature { Category = cat, Value = val }, source);

    [Fact]
    public void CollectExternalDescriptors_SeedsOnlyManualEntries()
    {
        // Only hand labels seed. Automatic leftovers (rule-based, library, classifier) are never read,
        // so an evaluation can't feed its own earlier output back into its DescriptorRef conditions.
        var preset = new BodySlideSetting
        {
            BodyShapeDescriptorsByWeight = new Dictionary<int, HashSet<AnnotatedDescriptorSignature>>
            {
                [100] = new()
                {
                    Annotated("Belly", "Muscular", BodyShapeAnnotationSource.RulesBased),
                    Annotated("Chest", "Busty", BodyShapeAnnotationSource.Manual),
                    Annotated("Hips", "Wide", BodyShapeAnnotationSource.Library),
                    // Prior classifier output must NOT feed back into a re-run's seed.
                    Annotated("Belly", "Chubby", BodyShapeAnnotationSource.Classifier),
                },
            },
        };

        var seeds = BodySlideMeasurementEvaluator.CollectExternalDescriptors(preset, 100);

        seeds.Should().BeEquivalentTo(new[] { ("Chest", "Busty") });
    }

    [Fact]
    public void CollectExternalDescriptors_EmptyExactSlot_DoesNotBorrowFromNeighbors()
    {
        // An existing-but-empty slot means "nothing assigned at this weight" — unlike the patcher's
        // PerWeightDescriptorLookup, the seed must not walk outward to a non-empty neighbor.
        var preset = new BodySlideSetting
        {
            BodyShapeDescriptorsByWeight = new Dictionary<int, HashSet<AnnotatedDescriptorSignature>>
            {
                [0] = new(),
                [100] = new() { Annotated("Belly", "Muscular", BodyShapeAnnotationSource.Manual) },
            },
        };

        BodySlideMeasurementEvaluator.CollectExternalDescriptors(preset, 0).Should().BeEmpty();
    }

    [Fact]
    public void CollectExternalDescriptors_MissingSlot_FallsBackToNearest_TiesRoundDown()
    {
        var preset = new BodySlideSetting
        {
            BodyShapeDescriptorsByWeight = new Dictionary<int, HashSet<AnnotatedDescriptorSignature>>
            {
                [0] = new() { Annotated("Belly", "Flat", BodyShapeAnnotationSource.Manual) },
                [50] = new() { Annotated("Belly", "Normal", BodyShapeAnnotationSource.Manual) },
            },
        };

        // No slot at 20: nearest existing key is 0 (dist 20) over 50 (dist 30).
        BodySlideMeasurementEvaluator.CollectExternalDescriptors(preset, 20)
            .Should().BeEquivalentTo(new[] { ("Belly", "Flat") });

        // Equidistant between 0 and 50 -> lower key wins (the app's slot-lookup convention).
        BodySlideMeasurementEvaluator.CollectExternalDescriptors(preset, 25)
            .Should().BeEquivalentTo(new[] { ("Belly", "Flat") });

        BodySlideMeasurementEvaluator.CollectExternalDescriptors(preset, 40)
            .Should().BeEquivalentTo(new[] { ("Belly", "Normal") });
    }

    [Fact]
    public void CollectExternalDescriptors_NoSlots_ReturnsEmpty()
    {
        BodySlideMeasurementEvaluator.CollectExternalDescriptors(null, 50).Should().BeEmpty();
        BodySlideMeasurementEvaluator.CollectExternalDescriptors(
            new BodySlideSetting { BodyShapeDescriptorsByWeight = null! }, 50).Should().BeEmpty();
        BodySlideMeasurementEvaluator.CollectExternalDescriptors(
            new BodySlideSetting { BodyShapeDescriptorsByWeight = new() }, 50).Should().BeEmpty();
        // A fresh preset ships five default slots, all empty -> exact-slot hit, no seeds.
        BodySlideMeasurementEvaluator.CollectExternalDescriptors(new BodySlideSetting(), 50).Should().BeEmpty();
    }

    // ---------- Unified engine: slider, measurement and DescriptorRef rules in one pass ----------

    private const string SliderBodyType = "CBBE 3BA";

    private static BodySlideSetting MakeSliderPreset(params (string Name, float Small, float Big)[] sliders)
    {
        var preset = new BodySlideSetting { Label = "TestPreset", SliderGroup = SliderBodyType };
        foreach (var (name, small, big) in sliders)
        {
            preset.SliderValues[name] = new BodySlideSlider { SliderName = name, Small = small, Big = big };
        }
        return preset;
    }

    private static MeasurementCondition Slider(string name, BodySliderType type, MeasurementComparator cmp, float val) =>
        new() { Kind = MeasurementConditionKind.Slider, SliderName = name, SliderType = type, Comparator = cmp, Value = val };

    private static BodyTypeProfile MakeProfile(Dictionary<string, string>? defaults, params MeasurementRule[] rules)
    {
        var profile = new BodyTypeProfile { BodyTypeName = SliderBodyType, Rules = rules.ToList() };
        foreach (var (k, v) in defaults ?? new()) profile.DefaultDescriptorValuesByCategory[k] = v;
        return profile;
    }

    private static List<(string Category, string Value)> Derive(BodyTypeProfile profile, BodySlideSetting preset, int weight,
        Dictionary<string, float>? meas, IReadOnlyCollection<string>? known = null) =>
        AutoAnnotationEngine.DeriveForSlot(profile, preset, Gender.Female, weight, meas, known)
            .Descriptors.Select(d => (d.Category, d.Value)).ToList();

    /// <summary>A slider rule (Belly:Muscular at MuscleAbs Interp >= 60) and measurement rules that
    /// exclude [Belly:Muscular] run in ONE pass: the measurement rules see the slider rule's output
    /// through DescriptorRef exactly as they would another measurement rule's.</summary>
    [Fact]
    public void EndToEnd_SliderRuleGatesMeasurementRules_InOnePass()
    {
        var preset = MakeSliderPreset(("MuscleAbs", 0, 80));
        var profile = MakeProfile(null,
            Rule("Build", "Powerful",
                 Meas("arm_thickness", MeasurementComparator.GreaterThanOrEqual, 1.2f),
                 Ref("Belly", "Chubby", negate: true),
                 Ref("Belly", "Fat", negate: true)),
            Rule("Belly", "Muscular", Slider("MuscleAbs", BodySliderType.Interpolated, MeasurementComparator.GreaterThanOrEqual, 60)),
            Rule("Belly", "Chubby",
                 Meas("belly_projection", MeasurementComparator.GreaterThanOrEqual, 16f),
                 Ref("Belly", "Muscular", negate: true)),
            Rule("Belly", "Fat",
                 Meas("belly_projection", MeasurementComparator.GreaterThanOrEqual, 20f),
                 Ref("Belly", "Muscular", negate: true)));
        var meas = new Dictionary<string, float> { ["belly_projection"] = 22f, ["arm_thickness"] = 1.5f };

        // Weight 100: MuscleAbs = 80 -> Muscular -> Chubby/Fat blocked -> Powerful fires.
        var at100 = Derive(profile, preset, 100, meas);
        at100.Should().Contain(("Belly", "Muscular"));
        at100.Should().Contain(("Build", "Powerful"));
        at100.Should().NotContain(("Belly", "Chubby"));

        // Weight 0: MuscleAbs = 0 -> no Muscular -> the fat belly reads Chubby + Fat, blocking Powerful.
        var at0 = Derive(profile, preset, 0, meas);
        at0.Should().Contain(("Belly", "Chubby"));
        at0.Should().Contain(("Belly", "Fat"));
        at0.Should().NotContain(("Build", "Powerful"));
    }

    [Fact]
    public void SliderOnlyCategory_IsAssigned_WithoutAnyMeasurementCache()
    {
        var preset = MakeSliderPreset(("MuscleAbs", 0, 80));
        var profile = MakeProfile(new() { ["BellyMorph"] = "Low Definition", ["Arms"] = "Medium" },
            Rule("BellyMorph", "Muscular", Slider("MuscleAbs", BodySliderType.Interpolated, MeasurementComparator.GreaterThanOrEqual, 70)),
            Rule("Arms", "Large", Meas("Arm_Volume", MeasurementComparator.GreaterThanOrEqual, 350f)));

        var result = AutoAnnotationEngine.DeriveForSlot(profile, preset, Gender.Female, 100, measurements: null);

        result.Descriptors.Select(d => (d.Category, d.Value)).Should().BeEquivalentTo(new[] { ("BellyMorph", "Muscular") });
        // Arms needs a measurement the preset was never scanned for: no rule label and no default.
        result.UnevaluableCategories.Should().BeEquivalentTo(new[] { "Arms" });

        // At weight 0 the slider rule misses, so its category falls to its default.
        Derive(profile, preset, 0, null).Should().BeEquivalentTo(new[] { ("BellyMorph", "Low Definition") });
    }

    [Fact]
    public void FailedButKnownMeasurement_IsEvaluable_SoTheDefaultApplies()
    {
        // The cache entry carries Arm_Volume but it failed to compute: the rule is tested (and fails),
        // exactly as at scan time, so the category is decided and falls to its default.
        var preset = MakeSliderPreset();
        var profile = MakeProfile(new() { ["Arms"] = "Medium" },
            Rule("Arms", "Large", Meas("Arm_Volume", MeasurementComparator.GreaterThanOrEqual, 350f)));

        Derive(profile, preset, 50, new Dictionary<string, float>(), known: new[] { "Arm_Volume" })
            .Should().BeEquivalentTo(new[] { ("Arms", "Medium") });
    }

    [Fact]
    public void Category_DependingOnAnUnevaluableCategory_IsUnevaluableToo()
    {
        var preset = MakeSliderPreset(("MuscleAbs", 0, 80));
        var profile = MakeProfile(new() { ["Shape"] = "Ambiguous" },
            Rule("Arms", "Large", Meas("Arm_Volume", MeasurementComparator.GreaterThanOrEqual, 350f)),
            Rule("Shape", "Strong", Slider("MuscleAbs", BodySliderType.Big, MeasurementComparator.GreaterThan, 10), Ref("Arms", "Large")));

        var result = AutoAnnotationEngine.DeriveForSlot(profile, preset, Gender.Female, 100, measurements: null);

        result.Descriptors.Should().BeEmpty("Shape reads Arms, which can't be evaluated, so neither its rule nor its default applies");
        result.UnevaluableCategories.Should().BeEquivalentTo(new[] { "Arms", "Shape" });
    }

    [Fact]
    public void ManualCategory_SeedsReferences_EvenWhenItsOwnRulesAreUnevaluable()
    {
        // The user hand-labeled Arms:Large, so Shape's [Arms:Large] reference is decided by the user,
        // not by the unscanned Arms measurement.
        var preset = MakeSliderPreset(("MuscleAbs", 0, 80));
        preset.BodyShapeDescriptorsByWeight[100].Add(Annotated("Arms", "Large", BodyShapeAnnotationSource.Manual));
        var profile = MakeProfile(new() { ["Shape"] = "Ambiguous" },
            Rule("Arms", "Large", Meas("Arm_Volume", MeasurementComparator.GreaterThanOrEqual, 350f)),
            Rule("Shape", "Strong", Ref("Arms", "Large")));

        Derive(profile, preset, 100, null).Should().BeEquivalentTo(new[] { ("Shape", "Strong") });
    }

    [Fact]
    public void SliderCondition_UsesTheSampleWeight_AndFailsWithoutASample()
    {
        var rule = Rule("Belly", "Muscular", Slider("MuscleAbs", BodySliderType.Interpolated, MeasurementComparator.GreaterThanOrEqual, 60));
        var preset = MakeSliderPreset(("MuscleAbs", 0, 80));
        var empty = new Dictionary<string, float>();

        MeasurementMath.RuleMatches(rule, empty, null, SliderSample.For(preset, 100)).Should().BeTrue();
        MeasurementMath.RuleMatches(rule, empty, null, SliderSample.For(preset, 50)).Should().BeFalse();
        MeasurementMath.RuleMatches(rule, empty, null, null).Should().BeFalse();
    }
}
