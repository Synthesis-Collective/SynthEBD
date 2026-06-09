using FluentAssertions;
using Xunit;

namespace SynthEBD.Tests;

/// <summary>
/// B35: FormatEntry built the coverage-report percentage as (Assigned * 100 / Assignable), both ints,
/// so integer division truncated the value before "N2" formatted it -- the two decimals were always
/// .00 (1 of 3 printed 33.00%, not 33.33%). FormatAssignedPercentage now uses double arithmetic.
/// "N2" is current-culture; the suite/CI run en-US, so these literals hold.
/// </summary>
public class AssetStatsTrackerTests
{
    [Theory]
    [InlineData(1, 3, "33.33")]    // the fix: was "33.00" under integer division
    [InlineData(2, 7, "28.57")]    // was "28.00"
    [InlineData(5, 5, "100.00")]   // full coverage
    [InlineData(0, 4, "0.00")]     // none assigned
    [InlineData(0, 0, "0")]        // nothing assignable -> sentinel, no division
    public void FormatAssignedPercentage_UsesDoubleArithmetic(int assigned, int assignable, string expected)
    {
        Patcher.AssetStatsTracker.FormatAssignedPercentage(assigned, assignable).Should().Be(expected);
    }
}
