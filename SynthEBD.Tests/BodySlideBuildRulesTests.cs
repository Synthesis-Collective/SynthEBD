using System.Xml.Linq;
using CharacterViewer.Rendering;
using FluentAssertions;
using Xunit;

namespace SynthEBD.Tests;

/// <summary>
/// <see cref="BodySlideBuildRules"/> and the slider metadata it runs on: the value rules a BodySlide build
/// applies (set defaults for sliders a preset doesn't list -- hidden ones included -- and inversion), which a
/// source mesh deformed straight from its slider data needs to come out as BodySlide would build it.
/// Checked once against a real CBBE 3BA install: source mesh + these rules reproduced the shipped
/// femalebody_0 / _1 to 0.0000 max vertex error, versus up to 0.72 (0.59 at the neck) without them.
/// </summary>
public class BodySlideBuildRulesTests
{
    private static MorphSet Preset(params (string Name, float Big, float Small)[] sliders) => new()
    {
        Label = "p",
        Sliders = sliders.ToDictionary(s => s.Name, s => new MorphSlider(s.Big, s.Small), StringComparer.OrdinalIgnoreCase),
    };

    [Fact]
    public void HiddenAndUnlistedSliders_TakeTheSetDefault()
    {
        var infos = new[]
        {
            new BodySlideSliderInfo("NeckSeam", DefaultBig: 0, DefaultSmall: 100, Invert: false, Hidden: true),
            new BodySlideSliderInfo("AnkleSize", DefaultBig: 0, DefaultSmall: 100, Invert: false, Hidden: false),
            new BodySlideSliderInfo("Butt", DefaultBig: 0, DefaultSmall: 0, Invert: false, Hidden: false),
        };

        var built = BodySlideBuildRules.Apply(Preset(("Butt", 40, 20)), infos);

        built.Sliders["NeckSeam"].Should().Be(new MorphSlider(0, 100));
        built.Sliders["AnkleSize"].Should().Be(new MorphSlider(0, 100));
        built.Sliders["Butt"].Should().Be(new MorphSlider(40, 20));     // listed: the preset's value
    }

    [Fact]
    public void InvertedSliders_ApplyOneMinusTheValue()
    {
        var infos = new[] { new BodySlideSliderInfo("Breasts", 100, 100, Invert: true, Hidden: false) };

        BodySlideBuildRules.Apply(Preset(("Breasts", 30, 0)), infos).Sliders["Breasts"].Should().Be(new MorphSlider(70, 100));
        BodySlideBuildRules.Apply(Preset(), infos).Sliders["Breasts"].Should().Be(new MorphSlider(0, 0));   // default 100 -> no delta
    }

    [Fact]
    public void NonDeltaSlidersAreDropped_AndUndescribedSlidersPassThrough()
    {
        var infos = new[] { new BodySlideSliderInfo("ZapNipples", 0, 0, false, false, IsShapeDelta: false) };

        var built = BodySlideBuildRules.Apply(Preset(("ZapNipples", 100, 100), ("Other", 5, 6)), infos);

        built.Sliders.Should().NotContainKey("ZapNipples");
        built.Sliders["Other"].Should().Be(new MorphSlider(5, 6));
    }

    [Fact]
    public void ReadSliderInfo_ReadsBigSmallWithGenWeights_ElseDefault()
    {
        var weighted = XElement.Parse("""
            <SliderSet name="b"><OutputFile GenWeights="true">femalebody</OutputFile>
              <Slider name="NeckSeam" invert="false" small="100" big="0" hidden="true"/>
              <Slider name="Breasts" invert="true" small="100" big="100"/>
              <Slider name="Zap" zap="true"/>
            </SliderSet>
            """);
        var unweighted = XElement.Parse("""
            <SliderSet name="h"><OutputFile>femalehands</OutputFile><Slider name="Nails" default="25"/></SliderSet>
            """);

        BodyTypeSourceMeshes.ReadSliderInfo(weighted).Should().BeEquivalentTo(new[]
        {
            new BodySlideSliderInfo("NeckSeam", 0, 100, Invert: false, Hidden: true),
            new BodySlideSliderInfo("Breasts", 100, 100, Invert: true, Hidden: false),
            new BodySlideSliderInfo("Zap", 0, 0, Invert: false, Hidden: false, IsShapeDelta: false),
        });
        BodyTypeSourceMeshes.ReadSliderInfo(unweighted).Single().Should().Be(new BodySlideSliderInfo("Nails", 25, 25, false, false));
    }
}
