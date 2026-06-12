using FluentAssertions;
using Xunit;

namespace SynthEBD.Tests;

/// <summary>
/// Unit tests for <see cref="BodyGenSelector.FilterCombinationsByMorphNames"/>, the pure core of
/// <c>FilterBySpecificNPCAssignments</c> (B58). The old code had the copy/mutate inverted: it pruned the
/// <em>input</em> combination's template sets while adding the <em>unfiltered</em> copy to the output. So a
/// Specific NPC Assignment pinning a morph was not actually enforced (the chooser could still pick a
/// non-pinned sibling), and the corrupted input set poisoned the relaxed-retry fallback paths.
/// </summary>
public class BodyGenSpecificAssignmentFilterTests
{
    private static BodyGenConfig.BodyGenTemplate MakeTemplate(string label, string group) => new()
    {
        Label = label,
        MemberOfTemplateGroups = new HashSet<string> { group },
    };

    private static BodyGenSelector.GroupCombinationObject MakeCombination(
        params (string Group, BodyGenConfig.BodyGenTemplate[] Templates)[] positions)
    {
        var combination = new BodyGenConfig.RacialMapping.BodyGenCombination
        {
            Members = positions.Select(p => p.Group).ToList(),
        };
        var allTemplates = positions.SelectMany(p => p.Templates).ToHashSet();
        return new BodyGenSelector.GroupCombinationObject(combination, allTemplates, new ForceIfMatchTally());
    }

    [Fact]
    public void MatchingCombination_OutputIsPrunedToNamedMorphs()
    {
        var curvy = MakeTemplate("CurvyTorso", "Torso");
        var slim = MakeTemplate("SlimTorso", "Torso");
        var combo = MakeCombination(("Torso", new[] { curvy, slim }));

        var result = BodyGenSelector.FilterCombinationsByMorphNames(
            new HashSet<BodyGenSelector.GroupCombinationObject> { combo },
            new List<string> { "CurvyTorso" },
            out bool success);

        success.Should().BeTrue();
        var filtered = result.Should().ContainSingle().Subject;
        // Regression guard (B58): the old code returned the unfiltered copy, so SlimTorso remained
        // selectable despite the Specific Assignment pinning CurvyTorso.
        filtered.Templates[0].Should().BeEquivalentTo(new[] { curvy });
    }

    [Fact]
    public void MatchingCombination_InputIsNotModified()
    {
        var curvy = MakeTemplate("CurvyTorso", "Torso");
        var slim = MakeTemplate("SlimTorso", "Torso");
        var combo = MakeCombination(("Torso", new[] { curvy, slim }));

        BodyGenSelector.FilterCombinationsByMorphNames(
            new HashSet<BodyGenSelector.GroupCombinationObject> { combo },
            new List<string> { "CurvyTorso" },
            out _);

        // Regression guard (B58): the old code pruned the input combination in place.
        combo.Templates[0].Should().BeEquivalentTo(new[] { curvy, slim });
    }

    [Fact]
    public void CombinationMissingMorphAtOnePosition_IsExcluded()
    {
        var curvy = MakeTemplate("CurvyTorso", "Torso");
        var thickLegs = MakeTemplate("ThickLegs", "Legs");
        var twoPositions = MakeCombination(("Torso", new[] { curvy }), ("Legs", new[] { thickLegs }));
        var torsoOnly = MakeCombination(("Torso", new[] { curvy }));

        // Pins name only a torso morph: the two-position combo's Legs position empties out.
        var result = BodyGenSelector.FilterCombinationsByMorphNames(
            new HashSet<BodyGenSelector.GroupCombinationObject> { twoPositions, torsoOnly },
            new List<string> { "CurvyTorso" },
            out bool success);

        success.Should().BeTrue();
        var filtered = result.Should().ContainSingle().Subject;
        filtered.Templates.Should().HaveCount(1);
        filtered.Templates[0].Should().BeEquivalentTo(new[] { curvy });
    }

    [Fact]
    public void NoCombinationMatches_ReturnsOriginalSetUntouched()
    {
        var curvy = MakeTemplate("CurvyTorso", "Torso");
        var slim = MakeTemplate("SlimTorso", "Torso");
        var combo = MakeCombination(("Torso", new[] { curvy, slim }));
        var input = new HashSet<BodyGenSelector.GroupCombinationObject> { combo };

        var result = BodyGenSelector.FilterCombinationsByMorphNames(
            input, new List<string> { "NoSuchMorph" }, out bool success);

        success.Should().BeFalse();
        result.Should().BeSameAs(input);
        // Regression guard (B58): the old code returned the input half-pruned (each combo emptied up to its
        // first failing position), so the caller's relaxed retries operated on corrupted combinations.
        combo.Templates[0].Should().BeEquivalentTo(new[] { curvy, slim });
    }
}
