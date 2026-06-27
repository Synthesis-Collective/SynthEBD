using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using SynthEBD;
using Xunit;

namespace SynthEBD.Tests;

/// <summary>
/// Tests for <see cref="RuleDependencyOrder"/> — the descriptor-dependency topo sort and the
/// editor's <see cref="RuleDependencyOrder.WouldCreateCycle"/> pre-check. Both build the graph via
/// the shared BuildDependencyGraph helper, so a CROSS-category DescriptorRef depends on EVERY
/// producer of the referenced Category (not just the named Value); these tests pin that the cycle
/// check and the sort agree on that edge model.
/// </summary>
public class RuleDependencyOrderTests
{
    private static MeasurementCondition Ref(string cat, string val) =>
        new() { Kind = MeasurementConditionKind.DescriptorRef, RefCategory = cat, RefValue = val };

    private static MeasurementRule Rule(string cat, string val, params MeasurementCondition[] conds) =>
        new()
        {
            Descriptor = new BodyShapeDescriptor.LabelSignature { Category = cat, Value = val },
            GroupsORlogic = conds.Length == 0
                ? new List<AndGatedMeasurementGroup>()
                : new List<AndGatedMeasurementGroup> { new() { ConditionsANDlogic = conds.ToList() } },
        };

    private static int IndexOf(List<MeasurementRule> ordered, string cat, string val) =>
        ordered.FindIndex(r => r.Descriptor.Category == cat && r.Descriptor.Value == val);

    [Fact]
    public void Sort_OrdersCrossCategoryAggregator_AfterEveryProducerOfReferencedCategory()
    {
        // Agg references Cat:v1, but a cross-category ref depends on the WHOLE Cat category, so Agg
        // must land after BOTH Cat:v1 and Cat:v2 (so Cat's default would be materialized before Agg).
        var rules = new List<MeasurementRule>
        {
            Rule("Agg", "x", Ref("Cat", "v1")),
            Rule("Cat", "v1"),
            Rule("Cat", "v2"),
        };

        var ordered = RuleDependencyOrder.SortByDescriptorDependencies(rules, out var skipped);

        skipped.Should().BeEmpty();
        IndexOf(ordered, "Agg", "x").Should().BeGreaterThan(IndexOf(ordered, "Cat", "v1"));
        IndexOf(ordered, "Agg", "x").Should().BeGreaterThan(IndexOf(ordered, "Cat", "v2"));
    }

    [Fact]
    public void WouldCreateCycle_DetectsCategoryLevelCycle_ThatNodeLevelWouldMiss()
    {
        // A:a2 depends on category B (via [B:b1]). Adding B:b1 -> [A:a1] makes B depend on category A
        // (which includes A:a2, which depends on B) -> a B<->A cycle. A node-level check looking only
        // at the named (A,a1) producer would miss it; the category-aware check catches it.
        var rules = new List<MeasurementRule>
        {
            Rule("A", "a1"),
            Rule("A", "a2", Ref("B", "b1")),
            Rule("B", "b1"),
        };

        RuleDependencyOrder.WouldCreateCycle(rules, ruleIdx: 2, refCategory: "A", refValue: "a1")
            .Should().BeTrue();
    }

    [Fact]
    public void WouldCreateCycle_AllowsNonCyclicCrossCategoryRef()
    {
        var rules = new List<MeasurementRule>
        {
            Rule("A", "a1"),
            Rule("B", "b1", Ref("A", "a1")),
            Rule("C", "c1"),
        };

        // A:a1 -> [C:c1] is acyclic (nothing depends back on A here).
        RuleDependencyOrder.WouldCreateCycle(rules, ruleIdx: 0, refCategory: "C", refValue: "c1")
            .Should().BeFalse();
    }

    [Fact]
    public void WouldCreateCycle_AllowsRefToCategoryWithNoProducers()
    {
        // "Tone" has no producing rule (default-only). The sort adds no edge for it, so no cycle.
        var rules = new List<MeasurementRule> { Rule("A", "a1") };

        RuleDependencyOrder.WouldCreateCycle(rules, ruleIdx: 0, refCategory: "Tone", refValue: "Soft")
            .Should().BeFalse();
    }

    [Fact]
    public void WouldCreateCycle_DetectsSelfReference()
    {
        var rules = new List<MeasurementRule> { Rule("A", "a1") };

        RuleDependencyOrder.WouldCreateCycle(rules, ruleIdx: 0, refCategory: "A", refValue: "a1")
            .Should().BeTrue();
    }

    [Fact]
    public void WouldCreateCycle_DetectsIntraCategoryCycle()
    {
        // Belly:Chubby -> [Belly:Pregnant] exists; adding Belly:Pregnant -> [Belly:Chubby] closes a
        // within-category loop. Intra-category refs use the narrow (Category, Value) producers.
        var rules = new List<MeasurementRule>
        {
            Rule("Belly", "Pregnant"),
            Rule("Belly", "Chubby", Ref("Belly", "Pregnant")),
        };

        RuleDependencyOrder.WouldCreateCycle(rules, ruleIdx: 0, refCategory: "Belly", refValue: "Chubby")
            .Should().BeTrue();
    }

    [Fact]
    public void WouldCreateCycle_DoesNotOverFlagParallelChainsToSameTarget()
    {
        // Two DescriptorRef chains point at the same target (Target:t1). Adding Top -> Mid1 is a valid
        // forward edge, NOT a cycle — the regression guard for walking depends-on (not dependents).
        var rules = new List<MeasurementRule>
        {
            Rule("Target", "t1"),
            Rule("Mid1", "m1", Ref("Target", "t1")),
            Rule("Mid2", "m2", Ref("Target", "t1")),
            Rule("Top", "top1"),
        };

        RuleDependencyOrder.WouldCreateCycle(rules, ruleIdx: 3, refCategory: "Mid1", refValue: "m1")
            .Should().BeFalse();
    }
}
