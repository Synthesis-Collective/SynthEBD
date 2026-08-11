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
///
/// Also covers external-descriptor seeding: labels from sources senior to the classifier are
/// passed as <c>externalDescriptors</c> so DescriptorRef conditions gate on them like rule output,
/// and a seeded Category suppresses its own default. Slider labels are derived LIVE from the
/// Label-by-Sliders rule set by <see cref="BodySlideAnnotator.DeriveDescriptorsForSlot"/> (no
/// "apply annotations" pass required — see the end-to-end test); stored Manual/Library annotations
/// seed via <see cref="BodySlideMeasurementEvaluator.CollectExternalDescriptors"/>'s slot policy.
///
/// <para>Only slider-rule MATCHES seed — the slider side's per-category default does not, because it
/// duplicates the (synchronized) measurement-side default and would suppress it. That pairing is
/// pinned by <c>SliderCategoryDefault_IsNotSeeded_SoMeasurementDefaultStillEmits</c> and
/// <c>SliderRuleMatch_StillSeeds_ButSliderDefaultDoesNot</c>.</para>
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
    public void CollectExternalDescriptors_NoRulesSupplied_LegacyFallback_ReadsStoredNonClassifierEntries()
    {
        // Without a slider rule set (null), stored RulesBased entries seed as-is alongside
        // Manual/Library — the legacy fallback for callers that can't resolve the rules.
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

        seeds.Should().BeEquivalentTo(new[]
        {
            ("Belly", "Muscular"),
            ("Chest", "Busty"),
            ("Hips", "Wide"),
        });
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

    // ---------- CollectExternalDescriptors: live slider-rule derivation ----------

    private const string SliderBodyType = "CBBE 3BA";

    private static BodySlideSetting MakeSliderPreset(params (string Name, int Small, int Big)[] sliders)
    {
        var preset = new BodySlideSetting { Label = "TestPreset", SliderGroup = SliderBodyType };
        foreach (var (name, small, big) in sliders)
        {
            preset.SliderValues[name] = new BodySlideSlider { SliderName = name, Small = small, Big = big };
        }
        return preset;
    }

    /// <summary>One slider-rule set for one category: each (value, slider, comparator, threshold)
    /// entry becomes a single-condition Interpolated rule assigning that descriptor value.</summary>
    private static Dictionary<string, SliderClassificationRulesByBodyType> SliderRules(
        string category, string? defaultValue, params (string Value, string Slider, string Comparator, int Threshold)[] rules) =>
        new()
        {
            [SliderBodyType] = new SliderClassificationRulesByBodyType
            {
                BodyTypeGroup = SliderBodyType,
                DescriptorClassifiers = new List<DescriptorClassificationRuleSet>
                {
                    new()
                    {
                        DescriptorCategory = category,
                        DefaultDescriptorValue = defaultValue,
                        RuleList = rules.Select(r => new DescriptorAssignmentRuleSet
                        {
                            SelectedDescriptorValue = r.Value,
                            RuleListORlogic = new List<AndGatedSliderRuleGroup>
                            {
                                new()
                                {
                                    RuleListANDlogic = new List<SliderClassificationRule>
                                    {
                                        new() { SliderName = r.Slider, SliderType = BodySliderType.Interpolated, Comparator = r.Comparator, Value = r.Threshold },
                                    },
                                },
                            },
                        }).ToList(),
                    },
                },
            },
        };

    private static HashSet<BodyShapeDescriptor.LabelSignature> SliderUniverse(string category, params string[] values) =>
        values.Select(v => new BodyShapeDescriptor.LabelSignature { Category = category, Value = v }).ToHashSet();

    /// <summary>The user's core ask: slider labels seed from the CURRENT rule set with no
    /// "apply annotations" pass — a clean preset with empty descriptor slots still seeds
    /// Belly:Muscular wherever the MuscleAbs blend crosses the rule threshold.</summary>
    [Fact]
    public void CollectExternalDescriptors_WithRules_DerivesSliderLabelsLive_NoApplyNeeded()
    {
        // MuscleAbs blends 0 -> 80, so "Interp >= 60" holds at weights 75 (60) and 100 (80).
        var preset = MakeSliderPreset(("MuscleAbs", 0, 80));
        var rules = SliderRules("Belly", defaultValue: "Normal", ("Muscular", "MuscleAbs", ">=", 60));
        var universe = SliderUniverse("Belly", "Muscular", "Normal");

        BodySlideMeasurementEvaluator.CollectExternalDescriptors(preset, 100, rules, universe)
            .Should().BeEquivalentTo(new[] { ("Belly", "Muscular") });
        BodySlideMeasurementEvaluator.CollectExternalDescriptors(preset, 75, rules, universe)
            .Should().BeEquivalentTo(new[] { ("Belly", "Muscular") });
        // Below the threshold no slider rule matched, so nothing seeds — the category default is
        // NOT a senior-source label (see SliderCategoryDefault_IsNotSeeded_SoMeasurementDefaultStillEmits).
        BodySlideMeasurementEvaluator.CollectExternalDescriptors(preset, 0, rules, universe)
            .Should().BeEmpty();
        // Arbitrary off-slot weight: interpolation runs at the exact weight (37 -> 29.6 < 60).
        BodySlideMeasurementEvaluator.CollectExternalDescriptors(preset, 37, rules, universe)
            .Should().BeEmpty();

        // The derivation is a pure query — the preset's stored slots stay untouched.
        preset.EnumerateAllDescriptors().Should().BeEmpty();
    }

    [Fact]
    public void CollectExternalDescriptors_WithRules_IgnoresStaleStoredRulesBased()
    {
        // The stored RulesBased Belly:Muscular is stale (written before the rule was revised);
        // with live rules supplied it must NOT seed — only the fresh derivation counts, so
        // Manual/Library entries survive and the stale slider label disappears.
        var preset = MakeSliderPreset(("MuscleAbs", 0, 30)); // never reaches the >= 60 threshold
        preset.BodyShapeDescriptorsByWeight[100].Add(Annotated("Belly", "Muscular", BodyShapeAnnotationSource.RulesBased));
        preset.BodyShapeDescriptorsByWeight[100].Add(Annotated("Chest", "Busty", BodyShapeAnnotationSource.Manual));
        var rules = SliderRules("Belly", defaultValue: null, ("Muscular", "MuscleAbs", ">=", 60));
        var universe = SliderUniverse("Belly", "Muscular");

        BodySlideMeasurementEvaluator.CollectExternalDescriptors(preset, 100, rules, universe)
            .Should().BeEquivalentTo(new[] { ("Chest", "Busty") });
    }

    [Fact]
    public void CollectExternalDescriptors_WithRules_ManualCategoryBlocksSliderDerivation()
    {
        // Manual precedence mirrors the annotate pass: a category hand-labeled anywhere on the
        // preset is never rules-derived. The Manual entry itself seeds only at its own slot.
        var preset = MakeSliderPreset(("MuscleAbs", 0, 80));
        preset.BodyShapeDescriptorsByWeight[0].Add(Annotated("Belly", "Chubby", BodyShapeAnnotationSource.Manual));
        var rules = SliderRules("Belly", defaultValue: "Normal", ("Muscular", "MuscleAbs", ">=", 60));
        var universe = SliderUniverse("Belly", "Muscular", "Normal");

        BodySlideMeasurementEvaluator.CollectExternalDescriptors(preset, 100, rules, universe)
            .Should().BeEmpty();
        BodySlideMeasurementEvaluator.CollectExternalDescriptors(preset, 0, rules, universe)
            .Should().BeEquivalentTo(new[] { ("Belly", "Chubby") });
    }

    [Fact]
    public void DeriveDescriptorsForSlot_MatchesAppliedAnnotations_PerSlot()
    {
        // Drift guard: the pure per-slot query must agree with what AnnotateBodySlide actually
        // writes, slot for slot (same rules, same universe, clean preset).
        var preset = MakeSliderPreset(("MuscleAbs", 0, 80));
        var rules = SliderRules("Belly", defaultValue: "Normal", ("Muscular", "MuscleAbs", ">=", 60));
        var universe = SliderUniverse("Belly", "Muscular", "Normal");

        var derivedBySlot = preset.BodyShapeDescriptorsByWeight.Keys.ToDictionary(
            w => w,
            w => BodySlideAnnotator.DeriveDescriptorsForSlot(preset, rules, universe, w)
                .Select(d => (d.Category, d.Value)).OrderBy(p => p).ToList());

        BodySlideAnnotator.AnnotateBodySlide(preset, rules, universe,
            overwriteExistingAutoAnnotations: true, specifiedDescriptorCategory: null, logMessage: null);

        foreach (var (weight, slot) in preset.BodyShapeDescriptorsByWeight)
        {
            var applied = slot.Where(d => d.Source == BodyShapeAnnotationSource.RulesBased)
                .Select(d => (d.Category, d.Value)).OrderBy(p => p).ToList();
            derivedBySlot[weight].Should().Equal(applied, "slot {0} must derive exactly what the annotator applied", weight);
        }
    }

    [Fact]
    public void DeriveDescriptorsForSlot_HonorsDescriptorUniverseGating()
    {
        var preset = MakeSliderPreset(("MuscleAbs", 0, 80));
        var rules = SliderRules("Belly", defaultValue: "Normal", ("Muscular", "MuscleAbs", ">=", 60));

        // Rule value missing from the universe -> rule skipped -> default fills instead.
        BodySlideAnnotator.DeriveDescriptorsForSlot(preset, rules, SliderUniverse("Belly", "Normal"), 100)
            .Select(d => (d.Category, d.Value))
            .Should().BeEquivalentTo(new[] { ("Belly", "Normal") });

        // Default value missing from the universe -> nothing fills below the threshold.
        BodySlideAnnotator.DeriveDescriptorsForSlot(preset, rules, SliderUniverse("Belly", "Muscular"), 0)
            .Should().BeEmpty();

        // Null universe -> no filtering (rules and default apply as authored).
        BodySlideAnnotator.DeriveDescriptorsForSlot(preset, rules, null, 100)
            .Select(d => (d.Category, d.Value))
            .Should().BeEquivalentTo(new[] { ("Belly", "Muscular") });
    }

    /// <summary>End-to-end composition of the user's scenario with NO apply pass anywhere:
    /// a clean preset + a Label-by-Sliders rule (Belly:Muscular at MuscleAbs Interp >= 60) seed
    /// the measurement classifier, whose Belly:Chubby / Belly:Fat rules exclude [Belly:Muscular]
    /// and whose nested Build:Powerful aggregator requires NOT Chubby AND NOT Fat.</summary>
    [Fact]
    public void EndToEnd_SliderRuleSeedsMeasurementClassifier_WithoutAppliedAnnotations()
    {
        var preset = MakeSliderPreset(("MuscleAbs", 0, 80));
        var sliderRules = SliderRules("Belly", defaultValue: null, ("Muscular", "MuscleAbs", ">=", 60));
        var universe = SliderUniverse("Belly", "Muscular");

        var measurementRules = new List<MeasurementRule>
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
        var meas = new Dictionary<string, float> { ["belly_projection"] = 22f, ["arm_thickness"] = 1.5f };

        // Weight 100: MuscleAbs = 80 >= 60 -> Muscular seeds -> Chubby/Fat blocked -> Powerful fires.
        var seedsAt100 = BodySlideMeasurementEvaluator.CollectExternalDescriptors(preset, 100, sliderRules, universe);
        var (at100, _) = Run(measurementRules, meas, new Dictionary<string, string>(), seedsAt100);
        at100.Should().Contain(("Build", "Powerful"));
        at100.Should().NotContain(("Belly", "Chubby"));
        at100.Should().NotContain(("Belly", "Fat"));

        // Weight 0: MuscleAbs = 0 -> no Muscular seed -> the fat belly reads Chubby+Fat, blocking Powerful.
        var seedsAt0 = BodySlideMeasurementEvaluator.CollectExternalDescriptors(preset, 0, sliderRules, universe);
        var (at0, _) = Run(measurementRules, meas, new Dictionary<string, string>(), seedsAt0);
        at0.Should().Contain(("Belly", "Chubby"));
        at0.Should().Contain(("Belly", "Fat"));
        at0.Should().NotContain(("Build", "Powerful"));
    }

    // ---------- Slider-side category defaults must not suppress the measurement-side default ----------

    /// <summary>The reported bug, end to end. Label by Sliders has an Arms classifier with NO rules and
    /// the default "Medium"; Label by Measurements has Arms:Small / Arms:Large rules that this preset
    /// misses and the same "Medium" default (the two are kept identical by
    /// DescriptorDefaultSynchronizer). Seeding the slider default marked Arms covered, so the
    /// measurement default was suppressed and the preset came out with NO Arms descriptor — and
    /// clearing the slider default did not help, because the synchronizer cleared the measurement
    /// default with it. Excluding slider defaults from the seed is what breaks that loop.</summary>
    [Fact]
    public void SliderCategoryDefault_IsNotSeeded_SoMeasurementDefaultStillEmits()
    {
        var preset = MakeSliderPreset(("MuscleAbs", 0, 40));
        var sliderRules = SliderRules("Arms", defaultValue: "Medium"); // default only, no rules
        var universe = SliderUniverse("Arms", "Small", "Medium", "Large");

        var seeds = BodySlideMeasurementEvaluator.CollectExternalDescriptors(preset, 100, sliderRules, universe);
        seeds.Should().BeEmpty("a category default is a catch-all, not a senior-source label");

        var measurementRules = new List<MeasurementRule>
        {
            Rule("Arms", "Small", Meas("Arm_Volume", MeasurementComparator.LessThan, 220f)),
            Rule("Arms", "Large", Meas("Arm_Volume", MeasurementComparator.GreaterThanOrEqual, 350f)),
        };
        var defaults = new Dictionary<string, string> { ["Arms"] = "Medium" };
        var meas = new Dictionary<string, float> { ["Arm_Volume"] = 260f }; // between the two rules

        var (matches, defs) = Run(measurementRules, meas, defaults, seeds);

        matches.Should().BeEmpty();
        defs.Should().Contain(("Arms", "Medium"));
    }

    /// <summary>The other half of the invariant: a slider RULE match still seeds and still suppresses,
    /// so a slider-labeled preset never also collects the measurement default. Same preset, same rule
    /// set, two weights — above the threshold the rule fires and covers the category; below it nothing
    /// seeds and the measurement default fills.</summary>
    [Fact]
    public void SliderRuleMatch_StillSeeds_ButSliderDefaultDoesNot()
    {
        var preset = MakeSliderPreset(("MuscleAbs", 0, 80));
        var sliderRules = SliderRules("Belly", defaultValue: "Normal", ("Muscular", "MuscleAbs", ">=", 60));
        var universe = SliderUniverse("Belly", "Muscular", "Normal");
        var defaults = new Dictionary<string, string> { ["Belly"] = "Normal" };
        var noRules = new List<MeasurementRule>();
        var noMeas = new Dictionary<string, float>();

        // Weight 100: MuscleAbs = 80 >= 60 -> the rule matched -> seeds and suppresses the default.
        var seedsAt100 = BodySlideMeasurementEvaluator.CollectExternalDescriptors(preset, 100, sliderRules, universe);
        seedsAt100.Should().BeEquivalentTo(new[] { ("Belly", "Muscular") });
        var (_, defsAt100) = Run(noRules, noMeas, defaults, seedsAt100);
        defsAt100.Should().NotContain(("Belly", "Normal"));

        // Weight 0: no rule matched -> the slider default does NOT seed -> measurement default emits.
        var seedsAt0 = BodySlideMeasurementEvaluator.CollectExternalDescriptors(preset, 0, sliderRules, universe);
        seedsAt0.Should().BeEmpty();
        var (_, defsAt0) = Run(noRules, noMeas, defaults, seedsAt0);
        defsAt0.Should().Contain(("Belly", "Normal"));
    }

    [Fact]
    public void DeriveDescriptorsForSlot_IncludeCategoryDefaults_TogglesOnlyTheDefaultFill()
    {
        var preset = MakeSliderPreset(("MuscleAbs", 0, 80));
        var rules = SliderRules("Belly", defaultValue: "Normal", ("Muscular", "MuscleAbs", ">=", 60));
        var universe = SliderUniverse("Belly", "Muscular", "Normal");

        // A matched rule is returned either way — the flag never touches rule output.
        BodySlideAnnotator.DeriveDescriptorsForSlot(preset, rules, universe, 100, includeCategoryDefaults: false)
            .Select(d => (d.Category, d.Value))
            .Should().BeEquivalentTo(new[] { ("Belly", "Muscular") });

        // No rule matched: the default fills only when asked for (true is the annotate-pass default).
        BodySlideAnnotator.DeriveDescriptorsForSlot(preset, rules, universe, 0)
            .Select(d => (d.Category, d.Value))
            .Should().BeEquivalentTo(new[] { ("Belly", "Normal") });
        BodySlideAnnotator.DeriveDescriptorsForSlot(preset, rules, universe, 0, includeCategoryDefaults: false)
            .Should().BeEmpty();
    }
}
