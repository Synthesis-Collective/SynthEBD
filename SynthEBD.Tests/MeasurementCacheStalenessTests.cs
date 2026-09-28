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

    // --- EntriesSubjectToScan: hidden-and-disabled presets are skipped by the scan, so a staleness
    // verdict must not judge their entries (they would stay "not yet scanned" forever). ---

    private static Dictionary<(string PresetLabel, Gender Gender, int Weight), string> Cache() => new()
    {
        [("Visible", Gender.Female, 0)] = "visible-0",
        [("Visible", Gender.Female, 100)] = "visible-100",
        [("Hidden", Gender.Female, 0)] = "hidden-0",
        [("Hidden", Gender.Female, 100)] = "hidden-100",
        [("Hidden", Gender.Male, 0)] = "hidden-male-0",
    };

    [Fact]
    public void EntriesSubjectToScan_SkipsHiddenPresetAtEveryWeight_ForThatGenderOnly()
    {
        var skipped = new HashSet<(string, Gender)> { ("Hidden", Gender.Female) };

        MeasurementCacheStore.EntriesSubjectToScan(Cache(), skipped)
            .Should().BeEquivalentTo(new[] { "visible-0", "visible-100", "hidden-male-0" },
                "a skipped preset drops out at every weight, but only for the gender it was hidden under");
    }

    [Fact]
    public void EntriesSubjectToScan_NullSkipSet_JudgesEverything()
    {
        MeasurementCacheStore.EntriesSubjectToScan(Cache(), null)
            .Should().HaveCount(5, "with no skip list every entry is judged, as before");
    }

    [Fact]
    public void HiddenPresetMissingANewMeasurement_DoesNotMakeTheCacheStale()
    {
        // After a measurement edit + re-scan: the visible preset was re-scanned (d2 present), the hidden
        // one was skipped and still lacks d2. Judging only scanned entries, the cache is current.
        var cache = new Dictionary<(string PresetLabel, Gender Gender, int Weight), (Dictionary<string, float?> V, Dictionary<string, string> F)>
        {
            [("Visible", Gender.Female, 0)] = (new() { ["d1"] = 1f, ["d2"] = 2f }, new() { ["d1"] = "fpA", ["d2"] = "fpB" }),
            [("Hidden", Gender.Female, 0)] = (new() { ["d1"] = 1f }, new() { ["d1"] = "fpA" }),
        };
        var skipped = new HashSet<(string, Gender)> { ("Hidden", Gender.Female) };

        bool Current_(IReadOnlySet<(string, Gender)>? skip) =>
            MeasurementCacheStore.EntriesSubjectToScan(cache, skip)
                .All(e => MeasurementCacheStore.EntryHasAllCurrentMeasurements(Current, e.V, e.F));

        Current_(skipped).Should().BeTrue("the hidden preset's entry can't be refreshed by a scan, so it isn't judged");
        Current_(null).Should().BeFalse("un-hiding it brings the incomplete entry back under judgement");
    }
}
