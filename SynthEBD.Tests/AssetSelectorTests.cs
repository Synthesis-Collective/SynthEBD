using FluentAssertions;
using Xunit;

namespace SynthEBD.Tests;

/// <summary>
/// Unit tests for <see cref="AssetSelector.CombinationContainsForcedSubgroups"/>, the Specific-NPC-Assignment
/// forced-subgroup check used for Primary and MixIn packs. A pre-determined (consistency / linked-group)
/// combination is compatible only if it contains <em>every</em> forced subgroup id (a subset that must be
/// present). The missing-id case is the regression guard for the old loop, which ignored the forced ids and
/// merely checked that the combination was non-empty — so a forced subgroup was never actually enforced.
/// </summary>
public class AssetSelectorTests
{
    [Fact]
    public void AllForcedIdsPresent_ReturnsTrue()
    {
        var combination = new[] { "Helmet.Steel", "Cape.Red", "Boots.Plain" };
        var forced = new[] { "Helmet.Steel", "Cape.Red" };

        AssetSelector.CombinationContainsForcedSubgroups(combination, forced).Should().BeTrue();
    }

    [Fact]
    public void MissingForcedId_ReturnsFalse()
    {
        // Regression guard: the old code returned true for any non-empty combination, so a forced id that was
        // absent from the combination (e.g. a stale consistency record) was silently accepted.
        var combination = new[] { "Helmet.None", "Cape.Red" };
        var forced = new[] { "Helmet.Steel" };

        AssetSelector.CombinationContainsForcedSubgroups(combination, forced).Should().BeFalse();
    }

    [Fact]
    public void NoForcedIds_ImposesNoConstraint()
    {
        var combination = new[] { "Helmet.None" };

        AssetSelector.CombinationContainsForcedSubgroups(combination, Array.Empty<string>()).Should().BeTrue();
        AssetSelector.CombinationContainsForcedSubgroups(combination, null!).Should().BeTrue();
    }

    [Fact]
    public void ForcedIdAgainstEmptyCombination_ReturnsFalse()
    {
        AssetSelector.CombinationContainsForcedSubgroups(Array.Empty<string>(), new[] { "Helmet.Steel" }).Should().BeFalse();
    }
}
