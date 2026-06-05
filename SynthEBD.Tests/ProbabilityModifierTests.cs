using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Xunit;

namespace SynthEBD.Tests;

/// <summary>
/// Unit tests for the multiplicative probability-modifier math in
/// <see cref="ProbabilityWeighting.GetProbabilityModifierFactor(IEnumerable{AttributeWeightModifier}, System.Func{NPCAttribute, bool})"/>.
/// These target the predicate-based overload so the stacking logic is verified without a live
/// Mutagen environment (the production overload delegates the match test to AttributeMatcher).
/// </summary>
public class ProbabilityModifierTests
{
    /// <summary>Builds a non-blank modifier (one sub-attribute) with the given factor.</summary>
    private static AttributeWeightModifier Mod(double factor)
    {
        var attribute = new NPCAttribute();
        attribute.SubAttributes.Add(new NPCAttributeRace { Type = NPCAttributeType.Race });
        return new AttributeWeightModifier { Factor = factor, Attribute = attribute };
    }

    [Fact]
    public void NullList_ReturnsOne()
    {
        ProbabilityWeighting.GetProbabilityModifierFactor(null, (NPCAttribute a) => true).Should().Be(1.0);
    }

    [Fact]
    public void EmptyList_ReturnsOne()
    {
        ProbabilityWeighting.GetProbabilityModifierFactor(new List<AttributeWeightModifier>(), (NPCAttribute a) => true).Should().Be(1.0);
    }

    [Fact]
    public void NoMatches_ReturnsOne()
    {
        ProbabilityWeighting.GetProbabilityModifierFactor(new[] { Mod(3.0), Mod(2.0) }, (NPCAttribute a) => false).Should().Be(1.0);
    }

    [Fact]
    public void MultipleMatches_StackMultiplicatively()
    {
        ProbabilityWeighting.GetProbabilityModifierFactor(new[] { Mod(3.0), Mod(2.0) }, (NPCAttribute a) => true).Should().Be(6.0);
    }

    [Fact]
    public void ZeroFactorMatched_ReturnsZero()
    {
        ProbabilityWeighting.GetProbabilityModifierFactor(new[] { Mod(3.0), Mod(0.0) }, (NPCAttribute a) => true).Should().Be(0.0);
    }

    [Fact]
    public void BlankAttribute_IsSkipped()
    {
        // An attribute with no sub-attributes is a no-op even when the predicate would match.
        var blank = new AttributeWeightModifier { Factor = 5.0, Attribute = new NPCAttribute() };
        ProbabilityWeighting.GetProbabilityModifierFactor(new[] { blank }, (NPCAttribute a) => true).Should().Be(1.0);
    }

    [Fact]
    public void OnlyMatchingModifiersAreApplied()
    {
        var matching = Mod(3.0);
        var nonMatching = Mod(2.0);
        // Match only the first modifier's attribute by reference.
        double result = ProbabilityWeighting.GetProbabilityModifierFactor(
            new[] { matching, nonMatching },
            a => ReferenceEquals(a, matching.Attribute));
        result.Should().Be(3.0);
    }

    [Fact]
    public void FractionalFactor_ReducesProbability()
    {
        ProbabilityWeighting.GetProbabilityModifierFactor(new[] { Mod(0.5) }, (NPCAttribute a) => true).Should().Be(0.5);
    }
}
