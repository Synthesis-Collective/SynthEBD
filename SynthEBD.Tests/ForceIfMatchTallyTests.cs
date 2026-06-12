using FluentAssertions;
using Xunit;

namespace SynthEBD.Tests;

/// <summary>
/// Unit tests for <see cref="ForceIfMatchTally"/> (R19), the per-NPC ForceIf scratch store that replaced
/// the MatchedForceIfCount/ForceIfMatchCount properties on shared config-owned candidate objects.
/// </summary>
public class ForceIfMatchTallyTests
{
    [Fact]
    public void GetWithoutSet_ReturnsZero()
    {
        var tally = new ForceIfMatchTally();

        tally.Get(new object()).Should().Be(0);
    }

    [Fact]
    public void SetOverwrites_AddAccumulates()
    {
        var tally = new ForceIfMatchTally();
        var candidate = new object();

        tally.Set(candidate, 3);
        tally.Add(candidate, 2); // descriptor-rule matches accumulate on top of the base attribute match
        tally.Get(candidate).Should().Be(5);

        tally.Set(candidate, 1); // re-evaluation resets
        tally.Get(candidate).Should().Be(1);
    }

    [Fact]
    public void CandidatesAreKeyedByReference_NotValueEquality()
    {
        var tally = new ForceIfMatchTally();
        // two distinct references that compare equal by value
        var a = new string('x', 3);
        var b = new string('x', 3);

        tally.Set(a, 7);

        tally.Get(b).Should().Be(0);
        tally.Get(a).Should().Be(7);
    }
}
