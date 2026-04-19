using System.Collections.Generic;
using FluentAssertions;
using Xunit;

namespace SynthEBD.Tests;

/// <summary>
/// Unit tests for the slider-only <see cref="BodySlideGroupClassifier"/>. Each test builds a
/// minimal in-memory registry, marks entries installed, and asserts that
/// <see cref="BodySlideGroupClassifier.Classify"/> resolves the preset deterministically from
/// slider names alone.
/// </summary>
public class BodySlideGroupClassifierTests
{
    private static BodyTypeRegistryEntry MakeEntry(string name, Gender gender, bool installed, params string[] sliders)
    {
        var entry = new BodyTypeRegistryEntry
        {
            Name = name,
            Gender = gender,
            IsInstalled = installed,
        };
        foreach (var s in sliders) entry.ResolvedSliders.Add(s);
        return entry;
    }

    [Fact]
    public void NoInstalledBodies_ReturnsUnknown()
    {
        var classifier = new BodySlideGroupClassifier();
        classifier.SetRegistry(new List<BodyTypeRegistryEntry>
        {
            MakeEntry("CBBE", Gender.Female, installed: false, "Waist", "Hips"),
        });

        var result = classifier.Classify("AnyPreset", new[] { "Waist" });

        result.BodyType.Should().Be("Unknown");
        result.Reason.Should().Be("no-installed-bodies");
    }

    [Fact]
    public void EmptyPresetSliders_ReturnsUnknown()
    {
        var classifier = new BodySlideGroupClassifier();
        classifier.SetRegistry(new List<BodyTypeRegistryEntry>
        {
            MakeEntry("CBBE", Gender.Female, installed: true, "Waist"),
        });

        var result = classifier.Classify("AnyPreset", new string[] { });

        result.BodyType.Should().Be("Unknown");
        result.Reason.Should().Be("preset-has-no-sliders");
    }

    [Fact]
    public void SingleMatch_ReturnsThatBody()
    {
        var classifier = new BodySlideGroupClassifier();
        classifier.SetRegistry(new List<BodyTypeRegistryEntry>
        {
            MakeEntry("CBBE", Gender.Female, installed: true, "Waist", "Hips"),
            MakeEntry("HIMBO", Gender.Male, installed: true, "Chest", "Arms"),
        });

        var result = classifier.Classify("CBBEPreset", new[] { "Waist", "Hips" });

        result.BodyType.Should().Be("CBBE");
        result.Gender.Should().Be(Gender.Female);
        result.Reason.Should().Be("subset-match");
    }

    [Fact]
    public void NoSubsetMatch_ReturnsUnknownWithFemaleByDefault()
    {
        var classifier = new BodySlideGroupClassifier();
        classifier.SetRegistry(new List<BodyTypeRegistryEntry>
        {
            MakeEntry("CBBE", Gender.Female, installed: true, "Waist", "Hips"),
        });

        var result = classifier.Classify("Mystery", new[] { "AlienSlider" });

        result.BodyType.Should().Be("Unknown");
        result.Reason.Should().Be("no-subset-match");
        result.Gender.Should().Be(Gender.Female);
    }

    [Fact]
    public void NoSubsetMatch_InfersMaleFromMaleSlider()
    {
        var classifier = new BodySlideGroupClassifier();
        classifier.SetRegistry(new List<BodyTypeRegistryEntry>
        {
            MakeEntry("CBBE", Gender.Female, installed: true, "Waist"),
            MakeEntry("HIMBO", Gender.Male, installed: true, "Chest", "Arms"),
        });

        // Preset uses one HIMBO-known slider plus an unknown one -- doesn't match HIMBO strictly,
        // but the male-slider hit lets us label it Male for routing.
        var result = classifier.Classify("Junk", new[] { "Chest", "Mystery" });

        result.BodyType.Should().Be("Unknown");
        result.Gender.Should().Be(Gender.Male);
    }

    [Fact]
    public void MultipleMatches_PicksMostGeneral()
    {
        // CBBE 3BA's catalog is a superset of CBBE's. A preset using only CBBE-known sliders
        // matches both -- we should pick CBBE (the smaller / more general catalog).
        var cbbe = MakeEntry("CBBE", Gender.Female, installed: true, "Waist", "Hips");
        var threeBa = MakeEntry("CBBE 3BA", Gender.Female, installed: true,
            "Waist", "Hips", "Breast", "Belly");
        var classifier = new BodySlideGroupClassifier();
        classifier.SetRegistry(new List<BodyTypeRegistryEntry> { cbbe, threeBa });

        var result = classifier.Classify("Ambiguous", new[] { "Waist", "Hips" });

        result.BodyType.Should().Be("CBBE");
        result.Reason.Should().Contain("most-general");
    }

    [Fact]
    public void PresetUsingSupersetExclusiveSlider_ResolvesToSuperset()
    {
        // Same registry as above, but the preset uses "Belly" -- which only CBBE 3BA carries.
        // CBBE drops out of the candidate set; only CBBE 3BA matches.
        var cbbe = MakeEntry("CBBE", Gender.Female, installed: true, "Waist", "Hips");
        var threeBa = MakeEntry("CBBE 3BA", Gender.Female, installed: true,
            "Waist", "Hips", "Breast", "Belly");
        var classifier = new BodySlideGroupClassifier();
        classifier.SetRegistry(new List<BodyTypeRegistryEntry> { cbbe, threeBa });

        var result = classifier.Classify("BellyPreset", new[] { "Waist", "Hips", "Belly" });

        result.BodyType.Should().Be("CBBE 3BA");
        result.Reason.Should().Be("subset-match");
    }

    [Fact]
    public void UninstalledEntries_AreIgnored()
    {
        // Even though an uninstalled entry's slider catalog would match perfectly, it can't be picked.
        var classifier = new BodySlideGroupClassifier();
        classifier.SetRegistry(new List<BodyTypeRegistryEntry>
        {
            MakeEntry("CBBE", Gender.Female, installed: false, "Waist", "Hips"),
            MakeEntry("BHUNP", Gender.Female, installed: true, "Waist", "Hips", "Thigh"),
        });

        var result = classifier.Classify("Test", new[] { "Waist", "Hips" });

        result.BodyType.Should().Be("BHUNP");
    }

    [Fact]
    public void SetRegistry_NullRegistry_DoesNotThrow()
    {
        var classifier = new BodySlideGroupClassifier();
        classifier.SetRegistry(null!);

        classifier.HasCatalogs.Should().BeFalse();
        var result = classifier.Classify("X", new[] { "Y" });
        result.BodyType.Should().Be("Unknown");
    }
}
