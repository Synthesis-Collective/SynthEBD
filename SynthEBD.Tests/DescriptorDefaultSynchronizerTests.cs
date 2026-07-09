using System;
using System.Collections.Generic;
using FluentAssertions;
using SynthEBD;
using Xunit;

namespace SynthEBD.Tests;

/// <summary>
/// Tests for <see cref="DescriptorDefaultSynchronizer.DecideCanonicalDefault"/> — the pure
/// decision core of the default-descriptor sync between the Label by Sliders and Label by
/// Measurements menus. The matrix: all-empty yields nothing; an empty side adopts the non-empty
/// side regardless of policy; a true conflict (both sides non-empty and different) follows the
/// preferSliderSide flag (the OBody Misc toggle, default = measurement side wins); and
/// intra-measurement divergence across same-body-type profiles counts as a conflict with the
/// first non-empty profile value as the measurement side's canonical.
/// </summary>
public class DescriptorDefaultSynchronizerTests
{
    private static string Decide(string slider, IReadOnlyList<string> measurements, bool preferSlider, out bool conflict) =>
        DescriptorDefaultSynchronizer.DecideCanonicalDefault(slider, measurements, preferSlider, out conflict);

    [Fact]
    public void AllEmpty_YieldsEmpty_NoConflict()
    {
        Decide("", new[] { "", "" }, preferSlider: false, out bool conflict).Should().BeEmpty();
        conflict.Should().BeFalse();

        Decide(null!, null!, preferSlider: true, out conflict).Should().BeEmpty();
        conflict.Should().BeFalse();
    }

    [Fact]
    public void EmptySliderSide_AdoptsMeasurementValue_RegardlessOfPolicy()
    {
        Decide("", new[] { "Normal" }, preferSlider: false, out bool conflict).Should().Be("Normal");
        conflict.Should().BeFalse();

        // Policy is irrelevant to adoption — only true conflicts consult it.
        Decide("", new[] { "Normal" }, preferSlider: true, out conflict).Should().Be("Normal");
        conflict.Should().BeFalse();
    }

    [Fact]
    public void EmptyMeasurementSide_AdoptsSliderValue_RegardlessOfPolicy()
    {
        Decide("Muscular", new[] { "", "" }, preferSlider: false, out bool conflict).Should().Be("Muscular");
        conflict.Should().BeFalse();

        Decide("Muscular", Array.Empty<string>(), preferSlider: true, out conflict).Should().Be("Muscular");
        conflict.Should().BeFalse();
    }

    [Fact]
    public void BothSidesEqual_NoConflict()
    {
        Decide("Normal", new[] { "Normal", "Normal" }, preferSlider: false, out bool conflict).Should().Be("Normal");
        conflict.Should().BeFalse();
    }

    [Fact]
    public void TrueConflict_MeasurementWins_ByDefaultPolicy()
    {
        Decide("Muscular", new[] { "Normal" }, preferSlider: false, out bool conflict).Should().Be("Normal");
        conflict.Should().BeTrue();
    }

    [Fact]
    public void TrueConflict_SliderWins_WhenToggled()
    {
        Decide("Muscular", new[] { "Normal" }, preferSlider: true, out bool conflict).Should().Be("Muscular");
        conflict.Should().BeTrue();
    }

    [Fact]
    public void MeasurementCanonical_IsFirstNonEmptyProfileValue_InListOrder()
    {
        // Profile 1 has no default; profile 2 supplies the measurement side's canonical.
        Decide("", new[] { "", "Average", "Normal" }, preferSlider: false, out bool conflict).Should().Be("Average");
        // The trailing divergent sibling ("Normal") makes this a conflict even with no slider value.
        conflict.Should().BeTrue();
    }

    [Fact]
    public void IntraMeasurementDivergence_CountsAsConflict_EvenWhenSliderAgreesWithFirst()
    {
        // Slider agrees with the first profile, but a sibling profile diverges — the sweep must
        // still report a conflict so the harmonization gets logged.
        Decide("Normal", new[] { "Normal", "Average" }, preferSlider: false, out bool conflict).Should().Be("Normal");
        conflict.Should().BeTrue();
    }

    [Fact]
    public void SliderWinsPolicy_OverridesEveryDivergentProfile()
    {
        Decide("Muscular", new[] { "Normal", "Average" }, preferSlider: true, out bool conflict).Should().Be("Muscular");
        conflict.Should().BeTrue();
    }
}
