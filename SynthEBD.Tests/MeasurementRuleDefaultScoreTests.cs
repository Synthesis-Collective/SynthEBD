using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using FluentAssertions;
using Xunit;

namespace SynthEBD.Tests;

/// <summary>
/// Coverage for the Match Presets margin scorer's <b>Category-default</b> scoring
/// (<c>VM_BodyTypeProfileEditor.ScoreCategoryDefault</c> / <c>ScoreDescriptorValue</c>).
/// A descriptor value assigned through <c>BodyTypeProfile.DefaultDescriptorValuesByCategory</c>
/// has no rule to score, so its rows used to sort as unscored. The scorer now synthesizes the
/// default's margin as the negation of the best gender-eligible <i>rival</i> rule in the
/// Category (<c>−max(sibling scores)</c>): positive exactly when no rival fires, magnitude =
/// distance to the nearest rival. DescriptorRef conditions that point at a default value tunnel
/// into that same margin.
/// <para>Like <see cref="MeasurementRuleScoreTunnelingTests"/>, the scorers are private statics
/// on the editor VM and are invoked via reflection over a synthetic rule graph.</para>
/// </summary>
public class MeasurementRuleDefaultScoreTests
{
    /// <summary>Real CBBE/3BA thresholds: Arms:Athletic = Arm_Bicep_Ratio >= 0.4264,
    /// Arms:Small = Arm_Volume &lt; 220. Used so the expected margins mirror the motivating
    /// setup (Arms:Medium is the Category default with no live rule of its own).</summary>
    private const float BicepThreshold = 0.4264f;
    private const float SmallVolumeThreshold = 220f;

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

    private static AndGatedMeasurementGroup DisabledGrp(params MeasurementCondition[] conditions)
    {
        var g = Grp(conditions);
        g.IsDisabled = true;
        return g;
    }

    private static IReadOnlyDictionary<string, string> Defaults(params (string Category, string Value)[] pairs)
        => pairs.ToDictionary(p => p.Category, p => p.Value, StringComparer.Ordinal);

    // ---------- reflection shims ----------

    private static MethodInfo GetScorer(string name)
    {
        var mi = typeof(VM_BodyTypeProfileEditor).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static);
        mi.Should().NotBeNull($"{name} should still exist under this name — update the test if it was renamed");
        return mi!;
    }

    private static double? DefaultScore(
        string category,
        string defaultValue,
        Dictionary<string, float?> measurements,
        IReadOnlyList<VM_MeasurementRule> allRules,
        IReadOnlyDictionary<string, string>? defaults,
        Gender rowGender,
        MarginScoreMode mode = MarginScoreMode.PercentOfThreshold,
        Dictionary<string, double>? stdDevs = null)
    {
        return (double?)GetScorer("ScoreCategoryDefault").Invoke(
            null, new object?[] { category, defaultValue, measurements, mode, stdDevs, allRules, defaults, rowGender, null });
    }

    private static (double? Score, bool ViaDefault) ValueScore(
        string category,
        string value,
        Dictionary<string, float?> measurements,
        IReadOnlyList<VM_MeasurementRule> allRules,
        IReadOnlyDictionary<string, string>? defaults,
        Gender rowGender,
        MarginScoreMode mode = MarginScoreMode.PercentOfThreshold,
        Dictionary<string, double>? stdDevs = null)
    {
        var args = new object?[] { category, value, measurements, mode, stdDevs, allRules, defaults, rowGender, null, false };
        var score = (double?)GetScorer("ScoreDescriptorValue").Invoke(null, args);
        return (score, (bool)args[9]!);
    }

    private static double? RuleScore(
        VM_MeasurementRule rule,
        Dictionary<string, float?> measurements,
        IReadOnlyList<VM_MeasurementRule> allRules,
        IReadOnlyDictionary<string, string>? defaults,
        Gender rowGender,
        MarginScoreMode mode = MarginScoreMode.PercentOfThreshold,
        Dictionary<string, double>? stdDevs = null)
    {
        return (double?)GetScorer("ScoreRuleAgainstMeasurements").Invoke(
            null, new object?[] { rule, measurements, mode, stdDevs, allRules, defaults, rowGender, null });
    }

    // ---------- fixture ----------

    /// <summary>Two-sibling numeric Arms category: Small (volume below 220) and Athletic
    /// (bicep ratio at/above 0.4264), default Medium with no rule of its own.</summary>
    private static (List<VM_MeasurementRule> All, IReadOnlyDictionary<string, string> Defaults) ArmsFixture(
        RuleGender athleticGender = RuleGender.Either)
    {
        var small = MakeRule("Arms", "Small", RuleGender.Either,
            Grp(Meas("Arm_Volume", MeasurementComparator.LessThan, SmallVolumeThreshold)));
        var athletic = MakeRule("Arms", "Athletic", athleticGender,
            Grp(Meas("Arm_Bicep_Ratio", MeasurementComparator.GreaterThanOrEqual, BicepThreshold)));
        return (new List<VM_MeasurementRule> { small, athletic }, Defaults(("Arms", "Medium")));
    }

    /// <summary>A row that is Medium: volume 250 (Small margin (220−250)/220 = −0.136) and
    /// bicep 0.40 (Athletic margin (0.40−0.4264)/0.4264 = −0.0619). Nearest rival = Athletic.</summary>
    private static Dictionary<string, float?> MediumRow() => new()
    {
        ["Arm_Volume"] = 250f,
        ["Arm_Bicep_Ratio"] = 0.40f,
    };

    // ---------- the formula ----------

    [Fact]
    public void DefaultScore_IsNegatedBestSibling()
    {
        var (all, defaults) = ArmsFixture();

        var score = DefaultScore("Arms", "Medium", MediumRow(), all, defaults, Gender.Female);

        // −max(−0.136, −0.0619) = +0.0619: how far this row is from becoming Athletic.
        score.Should().NotBeNull();
        score!.Value.Should().BeApproximately(+0.0619, 0.001);
    }

    [Fact]
    public void DefaultScore_PositiveExactlyWhenNoSiblingFires_AndFlipsAcrossEachThreshold()
    {
        var (all, defaults) = ArmsFixture();

        // Cross the Athletic line: bicep 0.45 → Athletic = (0.45−0.4264)/0.4264 = +0.0554.
        var athleticRow = new Dictionary<string, float?> { ["Arm_Volume"] = 250f, ["Arm_Bicep_Ratio"] = 0.45f };
        DefaultScore("Arms", "Medium", athleticRow, all, defaults, Gender.Female)!
            .Value.Should().BeApproximately(-0.0554, 0.001, "a row that is Athletic is not default");

        // Cross the Small line instead: volume 200 → Small = (220−200)/220 = +0.0909.
        var smallRow = new Dictionary<string, float?> { ["Arm_Volume"] = 200f, ["Arm_Bicep_Ratio"] = 0.40f };
        DefaultScore("Arms", "Medium", smallRow, all, defaults, Gender.Female)!
            .Value.Should().BeApproximately(-0.0909, 0.001, "a row that is Small is not default");

        // Exactly on the Athletic line: margin 0 → default 0 (boundary, sign-neutral).
        var boundaryRow = new Dictionary<string, float?> { ["Arm_Volume"] = 250f, ["Arm_Bicep_Ratio"] = BicepThreshold };
        DefaultScore("Arms", "Medium", boundaryRow, all, defaults, Gender.Female)!
            .Value.Should().BeApproximately(0.0, 1e-6);
    }

    [Fact]
    public void DefaultScore_StdDevMode_PicksNearestRivalInSigmaUnits()
    {
        var (all, defaults) = ArmsFixture();
        // σ(bicep) = 0.04 → Athletic = −0.0264/0.04 = −0.66; σ(volume) = 50 → Small = −30/50 = −0.60.
        // In σ units Small is now the nearer rival, so the default reads +0.60, not +0.66.
        var stdDevs = new Dictionary<string, double> { ["Arm_Bicep_Ratio"] = 0.04, ["Arm_Volume"] = 50.0 };

        DefaultScore("Arms", "Medium", MediumRow(), all, defaults, Gender.Female,
                MarginScoreMode.StdDevNormalized, stdDevs)!
            .Value.Should().BeApproximately(+0.60, 0.01);
    }

    // ---------- gender scoping ----------

    [Fact]
    public void DefaultScore_ExcludesSiblingsScopedToTheOtherGender()
    {
        var (all, defaults) = ArmsFixture(athleticGender: RuleGender.Male);
        var row = new Dictionary<string, float?> { ["Arm_Volume"] = 250f, ["Arm_Bicep_Ratio"] = 0.45f };

        // Female rows never see the Male-only Athletic rule, so their only rival is Small
        // (−0.136) and the row is comfortably default at +0.136 — mirrors the row loop, where a
        // Male-only variant can't fire for a Female preset at scan time.
        DefaultScore("Arms", "Medium", row, all, defaults, Gender.Female)!
            .Value.Should().BeApproximately(+0.136, 0.001);
        // Male rows do see it and, at bicep 0.45, have left the default.
        DefaultScore("Arms", "Medium", row, all, defaults, Gender.Male)!
            .Value.Should().BeApproximately(-0.0554, 0.001);
    }

    // ---------- degradation paths (decisions 2 and 3) ----------

    [Fact]
    public void DefaultScore_UnscorableSiblingsDropOutOfTheMax_InsteadOfContributingZero()
    {
        var (all, defaults) = ArmsFixture();
        // A binary-only sibling (no margin information) and a sibling whose measurement is
        // missing from the row (stale cache). Under the old 0.0 sentinel the binary sibling
        // would have won the max and the default would read as an uninformative +0.00.
        all.Add(MakeRule("Arms", "Odd", RuleGender.Either,
            Grp(Meas("Arm_Volume", MeasurementComparator.EqualTo, 250f))));
        all.Add(MakeRule("Arms", "Stale", RuleGender.Either,
            Grp(Meas("Not_In_Cache", MeasurementComparator.GreaterThanOrEqual, 1f))));
        all.Add(MakeRule("Arms", "Muted", RuleGender.Either,
            DisabledGrp(Meas("Arm_Volume", MeasurementComparator.GreaterThanOrEqual, 0f))));

        DefaultScore("Arms", "Medium", MediumRow(), all, defaults, Gender.Female)!
            .Value.Should().BeApproximately(+0.0619, 0.001, "only the scorable rivals count");
    }

    [Fact]
    public void DefaultScore_AllSiblingsUnscorable_ReturnsNull_NotInfinity()
    {
        var all = new List<VM_MeasurementRule>
        {
            MakeRule("Arms", "Odd", RuleGender.Either, Grp(Meas("Arm_Volume", MeasurementComparator.EqualTo, 250f))),
            MakeRule("Arms", "Muted", RuleGender.Either,
                DisabledGrp(Meas("Arm_Volume", MeasurementComparator.GreaterThanOrEqual, 0f))),
            MakeRule("Arms", "Ghostly", RuleGender.Either, Grp(Ref("Ghost", "Nope"))),
        };

        DefaultScore("Arms", "Medium", MediumRow(), all, Defaults(("Arms", "Medium")), Gender.Female)
            .Should().BeNull("no rival offers an escape route, so the default's margin is unknown, not +∞");
    }

    [Fact]
    public void DefaultScore_RulelessCategory_ReturnsNull()
    {
        DefaultScore("Arms", "Medium", MediumRow(), new List<VM_MeasurementRule>(),
                Defaults(("Arms", "Medium")), Gender.Female)
            .Should().BeNull();
    }

    [Fact]
    public void DefaultScore_OwnValueRulesAreNotRivals()
    {
        var (all, defaults) = ArmsFixture();
        // A legacy, still-enabled rule for the default value itself with a huge positive
        // margin. It must not enter the rival max — matching it keeps the row Medium rather
        // than moving it out of Medium.
        all.Add(MakeRule("Arms", "Medium", RuleGender.Either,
            Grp(Meas("Arm_Volume", MeasurementComparator.GreaterThanOrEqual, 1f))));

        DefaultScore("Arms", "Medium", MediumRow(), all, defaults, Gender.Female)!
            .Value.Should().BeApproximately(+0.0619, 0.001);
    }

    // ---------- tunneling (decision 4: the Build case) ----------

    [Fact]
    public void DefaultScore_DescriptorRefSiblingTunnels_BuildCase()
    {
        var (all, defaults) = ArmsFixture();
        // Build is a pure-DescriptorRef aggregator; its default Medium is scored via the rivals'
        // tunneled margins — here the distance to the Arms:Athletic threshold.
        all.Add(MakeRule("Build", "Athletic", RuleGender.Either, Grp(Ref("Arms", "Athletic"))));
        var withBuild = new Dictionary<string, string>(defaults, StringComparer.Ordinal) { ["Build"] = "Medium" };

        DefaultScore("Build", "Medium", MediumRow(), all, withBuild, Gender.Female)!
            .Value.Should().BeApproximately(+0.0619, 0.001);
    }

    [Fact]
    public void DefaultScore_ReferenceCycleAmongRivals_ReturnsNull()
    {
        var a = MakeRule("Build", "Athletic", RuleGender.Either, Grp(Ref("CatX", "X")));
        var x = MakeRule("CatX", "X", RuleGender.Either, Grp(Ref("Build", "Athletic")));
        var all = new List<VM_MeasurementRule> { a, x };

        // Every rival is a cycle → each degrades to un-scorable → no rival scored → null.
        DefaultScore("Build", "Medium", MediumRow(), all, Defaults(("Build", "Medium")), Gender.Female)
            .Should().BeNull();
    }

    // ---------- ScoreDescriptorValue: what the badge shows ----------

    [Fact]
    public void ValueScore_DefaultWithAllDisabledLegacyRule_UsesTheDefaultMargin()
    {
        var (all, defaults) = ArmsFixture();
        // The live CBBE 3BA shape: Arms:Medium keeps a rule whose every branch is disabled.
        all.Add(MakeRule("Arms", "Medium", RuleGender.Either,
            DisabledGrp(Meas("Arm_Volume", MeasurementComparator.GreaterThanOrEqual, 220f),
                        Meas("Arm_Volume", MeasurementComparator.LessThan, 350f))));

        var (score, viaDefault) = ValueScore("Arms", "Medium", MediumRow(), all, defaults, Gender.Female);

        score.Should().NotBeNull("the disabled rule is unscored, but the default margin fills in");
        score!.Value.Should().BeApproximately(+0.0619, 0.001);
        viaDefault.Should().BeTrue("the badge must label the number as a default margin");
    }

    [Fact]
    public void ValueScore_IsMaxOfOwnRuleAndDefault_AndReportsWhichWon()
    {
        var (all, defaults) = ArmsFixture();
        var ownRule = MakeRule("Arms", "Medium", RuleGender.Either,
            Grp(Meas("Arm_Volume", MeasurementComparator.GreaterThanOrEqual, 100f)));
        all.Add(ownRule);

        // Own rule: (250−100)/100 = +1.5 beats the default's +0.0619 → own rule wins, not default.
        var (deep, viaDefaultDeep) = ValueScore("Arms", "Medium", MediumRow(), all, defaults, Gender.Female);
        deep!.Value.Should().BeApproximately(+1.5, 0.001);
        viaDefaultDeep.Should().BeFalse();

        // Own rule barely passes ((250−249)/249 = +0.004) while every rival is 0.0619 away →
        // the default term wins the max: "even if the own rule flipped, the row would still be
        // Medium". (The row itself is unchanged and still Medium — only the own rule moved.)
        all.Remove(ownRule);
        all.Add(MakeRule("Arms", "Medium", RuleGender.Either,
            Grp(Meas("Arm_Volume", MeasurementComparator.GreaterThanOrEqual, 249f))));
        var (shallow, viaDefaultShallow) = ValueScore("Arms", "Medium", MediumRow(), all, defaults, Gender.Female);
        shallow!.Value.Should().BeApproximately(+0.0619, 0.001);
        viaDefaultShallow.Should().BeTrue();
    }

    [Fact]
    public void ValueScore_NonDefaultValue_NeverTakesTheDefaultPath()
    {
        var (all, defaults) = ArmsFixture();

        var (score, viaDefault) = ValueScore("Arms", "Athletic", MediumRow(), all, defaults, Gender.Female);

        score!.Value.Should().BeApproximately(-0.0619, 0.001, "Athletic's own rule margin");
        viaDefault.Should().BeFalse();
    }

    [Fact]
    public void ValueScore_WithoutDefaultsMap_BehavesLikeTheOldScorer()
    {
        var (all, _) = ArmsFixture();

        // No defaults map → Medium has no rule and no default status → nothing to score.
        var (score, viaDefault) = ValueScore("Arms", "Medium", MediumRow(), all, null, Gender.Female);

        score.Should().BeNull();
        viaDefault.Should().BeFalse();
    }

    // ---------- DescriptorRef → default value ----------

    [Fact]
    public void RefToDefaultValue_TunnelsIntoTheDefaultMargin()
    {
        // The live Realism:UnrealisticChest shape: chest_projection > 17 AND [Belly:Normal],
        // where Belly:Normal is Belly's default (its legacy rule is fully disabled).
        var bellyFat = MakeRule("Belly", "Fat", RuleGender.Either,
            Grp(Meas("belly", MeasurementComparator.GreaterThanOrEqual, 10f)));
        var bellyNormalLegacy = MakeRule("Belly", "Normal", RuleGender.Either,
            DisabledGrp(Ref("Belly", "Fat", negate: true)));
        var realism = MakeRule("Realism", "UnrealisticChest", RuleGender.Either,
            Grp(Meas("chest_projection", MeasurementComparator.GreaterThan, 17f), Ref("Belly", "Normal")));
        var all = new List<VM_MeasurementRule> { bellyFat, bellyNormalLegacy, realism };
        var defaults = Defaults(("Belly", "Normal"));

        // chest (20−17)/17 = +0.176; Belly default = −((8−10)/10) = +0.20 → min = +0.176.
        RuleScore(realism, new Dictionary<string, float?> { ["chest_projection"] = 20f, ["belly"] = 8f },
                all, defaults, Gender.Female)!
            .Value.Should().BeApproximately(+0.176, 0.001);

        // Belly creeping toward Fat (9.5 → default +0.05) becomes the binding constraint.
        RuleScore(realism, new Dictionary<string, float?> { ["chest_projection"] = 20f, ["belly"] = 9.5f },
                all, defaults, Gender.Female)!
            .Value.Should().BeApproximately(+0.05, 0.001);

        // Without the defaults map the disabled legacy producer is all the scorer can see:
        // producer exists but is unscorable → the group is unscorable → null (old behavior).
        RuleScore(realism, new Dictionary<string, float?> { ["chest_projection"] = 20f, ["belly"] = 8f },
                all, null, Gender.Female)
            .Should().BeNull();
    }

    [Fact]
    public void RefToDefaultValue_Negated_FlipsTheSign()
    {
        var bellyFat = MakeRule("Belly", "Fat", RuleGender.Either,
            Grp(Meas("belly", MeasurementComparator.GreaterThanOrEqual, 10f)));
        var notNormal = MakeRule("Build", "Heavy", RuleGender.Either, Grp(Ref("Belly", "Normal", negate: true)));
        var all = new List<VM_MeasurementRule> { bellyFat, notNormal };

        // "Must NOT be Belly:Normal" fails exactly as strongly as the default holds (+0.20).
        RuleScore(notNormal, new Dictionary<string, float?> { ["belly"] = 8f }, all, Defaults(("Belly", "Normal")), Gender.Female)!
            .Value.Should().BeApproximately(-0.20, 0.001);
    }

    [Fact]
    public void RefToDefaultOfRulelessCategory_KeepsBinaryTreatment()
    {
        // Ghost has a default but no rules at all: the default always fires and carries no
        // information, so the ref stays binary and the group scores on its other condition.
        var rule = MakeRule("Build", "X", RuleGender.Either,
            Grp(Ref("Ghost", "Nope"), Meas("A", MeasurementComparator.GreaterThanOrEqual, 1f)));
        var all = new List<VM_MeasurementRule> { rule };

        RuleScore(rule, new Dictionary<string, float?> { ["A"] = 2f }, all, Defaults(("Ghost", "Nope")), Gender.Female)!
            .Value.Should().BeApproximately(1.0, 0.001);
    }

    [Fact]
    public void RefToDefaultValue_CycleThroughTheDefault_ReturnsNull()
    {
        // X = [Cat:Default]; Cat's only rival Y = [X's value]. Scoring X → default(Cat) → Y → X
        // (on the stack) → null → Y drops → no scorable rival → default null → X unscorable.
        var x = MakeRule("CatX", "X", RuleGender.Either, Grp(Ref("Cat", "Default")));
        var y = MakeRule("Cat", "Y", RuleGender.Either, Grp(Ref("CatX", "X")));
        var all = new List<VM_MeasurementRule> { x, y };

        RuleScore(x, new Dictionary<string, float?>(), all, Defaults(("Cat", "Default")), Gender.Female)
            .Should().BeNull();
    }
}
