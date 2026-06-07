using FluentAssertions;
using Xunit;

namespace SynthEBD.Tests;

/// <summary>
/// Unit tests for <see cref="BoolByProbability.Decide"/>. They pin the corrected probability semantics
/// (a continuous draw in [0,100) compared with '&lt;'): 0 means never, 100 means always, and an integer or
/// fractional T yields T%. The boundary cases are the regression guards for the old off-by-one
/// (<c>gen.Next(100) &lt;= T</c>), which ran ~1% high per bucket: it made <c>Decide(0)</c> true ~1% of the
/// time and <c>Decide(99)</c> always true, and it truncated fractional probabilities.
/// </summary>
public class BoolByProbabilityTests
{
    [Fact]
    public void ZeroProbability_IsNeverTrue()
    {
        // Post-fix this is mathematically guaranteed (the draw lies in [0,100), never below 0). Pre-fix it
        // was true ~1% of the time because Next(100)==0 satisfied "0 <= 0". Core low-end regression guard.
        for (int i = 0; i < 50_000; i++)
        {
            BoolByProbability.Decide(0).Should().BeFalse();
        }
    }

    [Fact]
    public void FullProbability_IsAlwaysTrue()
    {
        // The draw lies in [0,100), so it is always strictly below 100.
        for (int i = 0; i < 50_000; i++)
        {
            BoolByProbability.Decide(100).Should().BeTrue();
        }
    }

    [Fact]
    public void HighProbability_IsNotAlwaysTrue()
    {
        // High-end regression guard: the old gen.Next(100) <= 99 was ALWAYS true (Next(100) maxes at 99),
        // so a 99% setting behaved as 100%. With the fix ~1% of draws fall in [99,100) and return false.
        // P(zero falses in 50k at a true 99%) = 0.99^50000 ~= 0, so this is robust, not flaky.
        int falseCount = 0;
        for (int i = 0; i < 50_000; i++)
        {
            if (!BoolByProbability.Decide(99)) { falseCount++; }
        }

        falseCount.Should().BeGreaterThan(0, "a 99% probability must occasionally (~1%) return false");
    }

    [Theory]
    [InlineData(50.0, 0.50)]
    [InlineData(25.0, 0.25)]
    [InlineData(75.0, 0.75)]
    [InlineData(33.5, 0.335)] // fractional probability the old integer draw could not represent
    public void Probability_ApproximatesTheConfiguredRate(double probability, double expectedRate)
    {
        const int trials = 200_000;
        int trueCount = 0;
        for (int i = 0; i < trials; i++)
        {
            if (BoolByProbability.Decide(probability)) { trueCount++; }
        }

        double observed = (double)trueCount / trials;
        // ~9+ sigma band for a binomial at this N — comfortably non-flaky while still catching gross bias.
        observed.Should().BeApproximately(expectedRate, 0.01);
    }
}
