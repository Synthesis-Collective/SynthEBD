using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using FluentAssertions;
using Xunit;

namespace SynthEBD.Tests;

/// <summary>
/// Coverage for the Match Presets margin scorer's DescriptorRef tunneling
/// (<c>VM_BodyTypeProfileEditor.ScoreRuleAgainstMeasurements</c>). Aggregator rules built from
/// DescriptorRef conditions (e.g. Build:Powerful = ref[Arms:Powerful]) used to score a flat 0.0
/// ("passed but un-rankable"), which made the Match-strength and Similarity sorts no-ops for
/// reference-built labels — every row tied at +0.00. The scorer now recurses into the referenced
/// descriptor's gender-eligible producer rules and uses their best margin as the condition's
/// margin, sign-flipped for negated refs.
/// <para>The scorer is a private static on the editor VM, so tests invoke it via reflection with
/// a synthetic rule graph; no profile/editor construction is needed (their ctors require the full
/// editor object graph).</para>
/// </summary>
public class MeasurementRuleScoreTunnelingTests
{
    /// <summary>The real-world Arms threshold from the CBBE/3BA Body Type Profile
    /// (Arm_Bicep_Ratio >= 0.4264) — used so the expected margins mirror the motivating setup.</summary>
    private const float BicepThreshold = 0.4264f;

    // ---------- synthetic-graph builders ----------

    private static VM_MeasurementRule MakeRule(
        string category, string value, RuleGender gender, params AndGatedMeasurementGroup[] groups)
    {
        var model = new MeasurementRule
        {
            Descriptor = new BodyShapeDescriptor.LabelSignature { Category = category, Value = value },
            Gender = gender,
            GroupsORlogic = groups.ToList(),
        };
        // Null parent is safe for scoring: the ctor only stores it, and the descendants'
        // live-readout refresh null-guards the profile navigation.
        return new VM_MeasurementRule(model, null!);
    }

    private static MeasurementCondition Meas(string name, MeasurementComparator cmp, float threshold) => new()
    {
        MeasurementName = name,
        Comparator = cmp,
        Value = threshold,
    };

    private static MeasurementCondition Ref(string category, string value, bool negate = false) => new()
    {
        Kind = MeasurementConditionKind.DescriptorRef,
        RefCategory = category,
        RefValue = value,
        Negate = negate,
    };

    private static AndGatedMeasurementGroup Grp(params MeasurementCondition[] conditions) => new()
    {
        ConditionsANDlogic = conditions.ToList(),
    };

    private static double? Score(
        VM_MeasurementRule rule,
        Dictionary<string, float?> measurements,
        IReadOnlyList<VM_MeasurementRule> allRules,
        Gender rowGender,
        MarginScoreMode mode = MarginScoreMode.PercentOfThreshold,
        Dictionary<string, double>? stdDevs = null,
        IReadOnlyDictionary<string, string>? defaultsByCategory = null)
    {
        var mi = typeof(VM_BodyTypeProfileEditor).GetMethod(
            "ScoreRuleAgainstMeasurements", BindingFlags.NonPublic | BindingFlags.Static);
        mi.Should().NotBeNull("the scorer should still exist under this name — update the test if it was renamed");
        return (double?)mi!.Invoke(
            null, new object?[] { rule, measurements, mode, stdDevs, allRules, defaultsByCategory, rowGender, null });
    }

    // ---------- tunneling ----------

    [Fact]
    public void DescriptorRefRule_TunnelsIntoReferencedRule()
    {
        var arms = MakeRule("Arms", "Athletic", RuleGender.Either,
            Grp(Meas("Arm_Bicep_Ratio", MeasurementComparator.GreaterThanOrEqual, BicepThreshold)));
        var build = MakeRule("Build", "Athletic", RuleGender.Either,
            Grp(Ref("Arms", "Athletic")));
        var all = new List<VM_MeasurementRule> { arms, build };
        var measurements = new Dictionary<string, float?> { ["Arm_Bicep_Ratio"] = 0.40f };

        var score = Score(build, measurements, all, Gender.Female);

        // (0.40 - 0.4264) / 0.4264 = -0.0619: the referenced Arms rule's own margin, not the
        // flat 0.0 pure-DescriptorRef groups scored before tunneling.
        score.Should().NotBeNull();
        score!.Value.Should().BeApproximately(-0.0619, 0.001);
    }

    [Fact]
    public void NegatedDescriptorRef_FlipsTheSign()
    {
        var arms = MakeRule("Arms", "Athletic", RuleGender.Either,
            Grp(Meas("Arm_Bicep_Ratio", MeasurementComparator.GreaterThanOrEqual, BicepThreshold)));
        var build = MakeRule("Build", "Slim", RuleGender.Either,
            Grp(Ref("Arms", "Athletic", negate: true)));
        var all = new List<VM_MeasurementRule> { arms, build };
        var measurements = new Dictionary<string, float?> { ["Arm_Bicep_Ratio"] = 0.40f };

        // "Must NOT be Arms:Athletic" is satisfied exactly as strongly as Arms:Athletic fails.
        Score(build, measurements, all, Gender.Female)!.Value.Should().BeApproximately(+0.0619, 0.001);
    }

    [Fact]
    public void MixedGroup_TunneledMarginFeedsTheMin()
    {
        var arms = MakeRule("Arms", "Athletic", RuleGender.Either,
            Grp(Meas("Arm_Bicep_Ratio", MeasurementComparator.GreaterThanOrEqual, BicepThreshold)));
        var build = MakeRule("Build", "Athletic", RuleGender.Either,
            Grp(Meas("A", MeasurementComparator.GreaterThanOrEqual, 1f), Ref("Arms", "Athletic")));
        var all = new List<VM_MeasurementRule> { arms, build };
        var measurements = new Dictionary<string, float?> { ["A"] = 2f, ["Arm_Bicep_Ratio"] = 0.40f };

        // min(+1.0 from the measurement condition, -0.0619 from the tunnel): the ref is the
        // weakest link and must set the group score.
        Score(build, measurements, all, Gender.Female)!.Value.Should().BeApproximately(-0.0619, 0.001);
    }

    [Fact]
    public void MultipleProducers_BestMarginWins()
    {
        var armsLoose = MakeRule("Arms", "Athletic", RuleGender.Either,
            Grp(Meas("Arm_Bicep_Ratio", MeasurementComparator.GreaterThanOrEqual, BicepThreshold)));
        var armsStrict = MakeRule("Arms", "Athletic", RuleGender.Either,
            Grp(Meas("Arm_Bicep_Ratio", MeasurementComparator.GreaterThanOrEqual, 0.50f)));
        var build = MakeRule("Build", "Athletic", RuleGender.Either,
            Grp(Ref("Arms", "Athletic")));
        var all = new List<VM_MeasurementRule> { armsLoose, armsStrict, build };
        var measurements = new Dictionary<string, float?> { ["Arm_Bicep_Ratio"] = 0.40f };

        // max(-0.0619 vs the loose producer, -0.2 vs the strict one) — mirrors the primary
        // score's max-across-matching-rules behavior.
        Score(build, measurements, all, Gender.Female)!.Value.Should().BeApproximately(-0.0619, 0.001);
    }

    [Fact]
    public void StdDevMode_TunneledConditionReadsSigmaFromTheTable()
    {
        var arms = MakeRule("Arms", "Athletic", RuleGender.Either,
            Grp(Meas("Arm_Bicep_Ratio", MeasurementComparator.GreaterThanOrEqual, BicepThreshold)));
        var build = MakeRule("Build", "Athletic", RuleGender.Either,
            Grp(Ref("Arms", "Athletic")));
        var all = new List<VM_MeasurementRule> { arms, build };
        var measurements = new Dictionary<string, float?> { ["Arm_Bicep_Ratio"] = 0.40f };
        var stdDevs = new Dictionary<string, double> { ["Arm_Bicep_Ratio"] = 0.04 };

        // (0.40 - 0.4264) / sigma(0.04) = -0.66 — proves a tunneled condition finds its sigma
        // in the population table (ComputePopulationStdDevs builds that table over the
        // DescriptorRef closure so the name is present).
        Score(build, measurements, all, Gender.Female, MarginScoreMode.StdDevNormalized, stdDevs)!
            .Value.Should().BeApproximately(-0.66, 0.01);
    }

    // ---------- gender scoping ----------

    [Fact]
    public void Tunneling_HonorsProducerGenderScoping()
    {
        var armsFemale = MakeRule("Arms", "Athletic", RuleGender.Female,
            Grp(Meas("Arm_Bicep_Ratio", MeasurementComparator.GreaterThanOrEqual, BicepThreshold)));
        var armsMale = MakeRule("Arms", "Athletic", RuleGender.Male,
            Grp(Meas("Arm_Bicep_Ratio", MeasurementComparator.GreaterThanOrEqual, 0.30f)));
        var build = MakeRule("Build", "Athletic", RuleGender.Either,
            Grp(Ref("Arms", "Athletic")));
        var all = new List<VM_MeasurementRule> { armsFemale, armsMale, build };
        var measurements = new Dictionary<string, float?> { ["Arm_Bicep_Ratio"] = 0.40f };

        Score(build, measurements, all, Gender.Female)!.Value.Should().BeApproximately(-0.0619, 0.001,
            "a Female row must tunnel into the Female-scoped producer only");
        Score(build, measurements, all, Gender.Male)!.Value.Should().BeApproximately(+0.3333, 0.001,
            "a Male row must tunnel into the Male-scoped producer only");
    }

    // ---------- degradation paths ----------

    [Fact]
    public void RefWithNoProducer_KeepsBinaryTreatment()
    {
        // "Ghost:Nope" has no producer rule (the shape a ref to a per-Category default value
        // takes) — it must not block scoring the group's other conditions.
        var rule = MakeRule("Build", "X", RuleGender.Either,
            Grp(Ref("Ghost", "Nope"), Meas("A", MeasurementComparator.GreaterThanOrEqual, 1f)));
        var all = new List<VM_MeasurementRule> { rule };
        var measurements = new Dictionary<string, float?> { ["A"] = 2f };

        Score(rule, measurements, all, Gender.Female)!.Value.Should().BeApproximately(1.0, 0.001);
    }

    [Fact]
    public void RefOnlyRuleWithNoProducer_IsUnscorable()
    {
        var rule = MakeRule("Build", "X", RuleGender.Either, Grp(Ref("Ghost", "Nope")));
        var all = new List<VM_MeasurementRule> { rule };

        // Nothing scorable in the group at all → no margin information. This used to score a
        // flat 0.0 ("passed but un-rankable"), which is indistinguishable from "exactly on
        // the boundary" and — negated into a Category-default margin — would read as "barely
        // default". The group now drops out of the max and the rule is unscored.
        Score(rule, new Dictionary<string, float?>(), all, Gender.Female).Should().BeNull();
    }

    [Fact]
    public void BinaryOnlyGroup_DropsOutOfTheMax_InsteadOfScoringZero()
    {
        // Group 1: a real numeric margin (negative). Group 2: only an Equal condition, which
        // carries no margin. The old 0.0 sentinel for group 2 would have won the max and hidden
        // the -0.5 signal; now group 2 is skipped and the numeric margin is reported.
        var rule = MakeRule("Cat", "V", RuleGender.Either,
            Grp(Meas("A", MeasurementComparator.GreaterThanOrEqual, 2f)),
            Grp(Meas("B", MeasurementComparator.EqualTo, 1f)));
        var all = new List<VM_MeasurementRule> { rule };
        var measurements = new Dictionary<string, float?> { ["A"] = 1f, ["B"] = 1f };

        Score(rule, measurements, all, Gender.Female)!.Value.Should().BeApproximately(-0.5, 0.001);
    }

    [Fact]
    public void ProducerExistsButUnscorable_MakesTheRuleUnscorable()
    {
        var arms = MakeRule("Arms", "Athletic", RuleGender.Either,
            Grp(Meas("Missing_Measurement", MeasurementComparator.GreaterThanOrEqual, 1f)));
        var build = MakeRule("Build", "Athletic", RuleGender.Either,
            Grp(Ref("Arms", "Athletic")));
        var all = new List<VM_MeasurementRule> { arms, build };

        // The producer's continuous condition can't be evaluated (stale cache shape) — the
        // referencing group degrades to un-scorable, mirroring the missing-measurement path.
        Score(build, new Dictionary<string, float?>(), all, Gender.Female).Should().BeNull();
    }

    [Fact]
    public void CyclicRefs_ReturnNullInsteadOfRecursingForever()
    {
        // The editor blocks cycles, but hand-edited JSON can still author one; the scorer must
        // degrade to un-scorable rather than overflow the stack.
        var a = MakeRule("CatA", "A", RuleGender.Either, Grp(Ref("CatB", "B")));
        var b = MakeRule("CatB", "B", RuleGender.Either, Grp(Ref("CatA", "A")));
        var all = new List<VM_MeasurementRule> { a, b };

        Score(a, new Dictionary<string, float?>(), all, Gender.Female).Should().BeNull();
    }

    // ---------- evaluator parity ----------

    [Fact]
    public void DisabledGroup_DoesNotContributeToTheScore()
    {
        var enabled = Grp(Meas("A", MeasurementComparator.GreaterThanOrEqual, 1f));
        var disabled = Grp(Meas("A", MeasurementComparator.GreaterThanOrEqual, 0.02f));
        disabled.IsDisabled = true;
        var rule = MakeRule("Cat", "V", RuleGender.Either, enabled, disabled);
        var all = new List<VM_MeasurementRule> { rule };
        var measurements = new Dictionary<string, float?> { ["A"] = 1.1f };

        // The enabled branch scores (1.1-1)/1 = +0.1; the muted branch's (1.1-0.02)/0.02 = +54
        // must not win the max — MeasurementMath.RuleMatches ignores disabled branches, so the
        // badge must too.
        Score(rule, measurements, all, Gender.Female)!.Value.Should().BeApproximately(0.1, 0.001);
    }
}
