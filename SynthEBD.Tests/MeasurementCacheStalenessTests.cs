using System.Collections.Generic;
using FluentAssertions;
using Xunit;

namespace SynthEBD.Tests;

/// <summary>
/// Regression coverage for <see cref="MeasurementCacheStore.EntryHasAllCurrentMeasurements"/>, the
/// per-entry decision behind the load-time "results stale" badge. A cached measurement whose value
/// is null (the evaluator ran but produced no number for that preset/weight — a valid terminal
/// result) must NOT flag the cache stale as long as its fingerprint still matches. Previously a
/// single null value flipped the whole profile to "results stale" on every reload, un-clearable by
/// re-scanning (the value comes back null each time).
/// </summary>
public class MeasurementCacheStalenessTests
{
    private static readonly Dictionary<string, string> Current = new()
    {
        ["d1"] = "fpA",
        ["d2"] = "fpB",
    };

    [Fact]
    public void NullValue_WithMatchingFingerprint_IsCurrent()
    {
        var values = new Dictionary<string, float?> { ["d1"] = 1.23f, ["d2"] = null };
        var fps = new Dictionary<string, string> { ["d1"] = "fpA", ["d2"] = "fpB" };

        MeasurementCacheStore.EntryHasAllCurrentMeasurements(Current, values, fps)
            .Should().BeTrue("a null value is a valid scanned result, not incomplete data");
    }

    [Fact]
    public void AbsentMeasurementKey_IsNotCurrent()
    {
        var values = new Dictionary<string, float?> { ["d1"] = 1.23f }; // never scanned d2
        var fps = new Dictionary<string, string> { ["d1"] = "fpA" };

        MeasurementCacheStore.EntryHasAllCurrentMeasurements(Current, values, fps)
            .Should().BeFalse("a genuinely missing measurement key is incomplete");
    }

    [Fact]
    public void MismatchedFingerprint_IsNotCurrent()
    {
        var values = new Dictionary<string, float?> { ["d1"] = 1.23f, ["d2"] = 4.56f };
        var fps = new Dictionary<string, string> { ["d1"] = "fpA", ["d2"] = "STALE" };

        MeasurementCacheStore.EntryHasAllCurrentMeasurements(Current, values, fps)
            .Should().BeFalse("a fingerprint mismatch means the definition drifted");
    }

    [Fact]
    public void NullValue_ButMissingFingerprint_IsNotCurrent()
    {
        var values = new Dictionary<string, float?> { ["d1"] = 1.23f, ["d2"] = null };
        var fps = new Dictionary<string, string> { ["d1"] = "fpA" }; // d2 fp absent

        MeasurementCacheStore.EntryHasAllCurrentMeasurements(Current, values, fps)
            .Should().BeFalse("a value (even null) with no recorded fingerprint can't be validated");
    }
}
