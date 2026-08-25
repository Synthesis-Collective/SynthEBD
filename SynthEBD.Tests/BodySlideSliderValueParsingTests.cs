using System.Globalization;
using System.Threading;
using FluentAssertions;
using SynthEBD;
using Xunit;

namespace SynthEBD.Tests;

/// <summary>
/// Regression tests for <see cref="Settings_OBody.TryParseSliderValue"/>, the single place a
/// BodySlide <c>&lt;SetSlider value="..."&gt;</c> attribute becomes a number.
///
/// <para>The bug these pin: the parse used <c>int.TryParse</c>, which returns false for any value
/// containing a decimal point. BodySlide writes fractional values routinely, so the whole SetSlider
/// was skipped and the slider silently stayed at 0 — roughly 20% of installed presets were measured
/// and rendered from a partially blank slider set, with no error surfaced anywhere. The worst cases
/// kept 4 of 320 sliders.</para>
///
/// <para>The second half of the fix is the culture: values always use a '.' separator, so parsing
/// with the ambient culture would reintroduce the same silent drop wherever ',' is the decimal
/// separator. <see cref="ParsesFractionalValue_UnderCommaDecimalCulture"/> is the guard for that.</para>
///
/// <para>Not covered here: the surrounding <c>ImportBodySlides</c> file walk, which needs the full
/// SettingsIO_OBody dependency graph. The value conversion is the part that regressed.</para>
/// </summary>
public class BodySlideSliderValueParsingTests
{
    [Theory]
    [InlineData("0", 0f)]
    [InlineData("45", 45f)]
    [InlineData("100", 100f)]
    [InlineData("-30", -30f)]
    public void ParsesWholeValues(string raw, float expected)
    {
        Settings_OBody.TryParseSliderValue(raw, out var value).Should().BeTrue();
        value.Should().Be(expected);
    }

    [Theory]
    [InlineData("26.282051", 26.282051f)]
    [InlineData("85.454544", 85.454544f)]
    [InlineData("-2.2068965", -2.2068965f)]
    [InlineData("0.5", 0.5f)]
    public void ParsesFractionalValues_TheIntTryParseRegression(string raw, float expected)
    {
        Settings_OBody.TryParseSliderValue(raw, out var value).Should().BeTrue(
            "BodySlide writes fractional slider values and dropping them blanks the slider");
        value.Should().BeApproximately(expected, 1e-5f);
    }

    [Theory]
    [InlineData("200", 200f)]
    [InlineData("-160", -160f)]
    [InlineData("-1000", -1000f)]
    [InlineData("121.0447", 121.0447f)]
    public void ParsesOutOfRangeValues_WithoutClamping(string raw, float expected)
    {
        // Preset authors push sliders past the 0-100 UI limits and SynthEBD applies them literally,
        // matching BodySlideDeformer (which clamps weight but not the slider value). Whether the game
        // clamps is an open question tracked in SLIDER_FIDELITY_PLAN.md — if a clamp is ever added it
        // belongs in the deformer, not here, so the parser stays faithful to the file.
        Settings_OBody.TryParseSliderValue(raw, out var value).Should().BeTrue();
        value.Should().BeApproximately(expected, 1e-3f);
    }

    [Fact]
    public void ParsesFractionalValue_UnderCommaDecimalCulture()
    {
        var original = Thread.CurrentThread.CurrentCulture;
        try
        {
            Thread.CurrentThread.CurrentCulture = new CultureInfo("de-DE");

            Settings_OBody.TryParseSliderValue("26.282051", out var value).Should().BeTrue(
                "the parse is pinned to the invariant culture; BodySlide always writes '.' separators");
            value.Should().BeApproximately(26.282051f, 1e-5f);
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = original;
        }
    }

    [Theory]
    [InlineData("45,5")]      // comma decimal: rejected outright rather than silently read as 455
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("abc")]
    public void RejectsMalformedValues(string raw)
    {
        Settings_OBody.TryParseSliderValue(raw, out _).Should().BeFalse();
    }

    [Theory]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    [InlineData("-Infinity")]
    public void RejectsNonFiniteValues(string raw)
    {
        // float.TryParse accepts these where int.TryParse did not, so the guard is explicit: a NaN
        // slider value propagates through the deformer into every vertex that slider touches and
        // silently corrupts the whole mesh.
        Settings_OBody.TryParseSliderValue(raw, out var value).Should().BeFalse();
        value.Should().Be(0f);
    }

    [Fact]
    public void SliderEndpointsRoundTripThroughTheModel()
    {
        Settings_OBody.TryParseSliderValue("26.282051", out var big).Should().BeTrue();
        Settings_OBody.TryParseSliderValue("15.769231", out var small).Should().BeTrue();

        var slider = new BodySlideSlider { SliderName = "Arms", Big = big, Small = small };

        // Would both have been 0 under int parsing, and both 26/16 if the model were still int.
        slider.Big.Should().BeApproximately(26.282051f, 1e-5f);
        slider.Small.Should().BeApproximately(15.769231f, 1e-5f);
        BodySlideAnnotator.InterpolateSliderValue(slider, 50)
            .Should().BeApproximately((26.282051f + 15.769231f) / 2f, 1e-4f);
    }
}
