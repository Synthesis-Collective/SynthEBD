using System.Linq;
using FluentAssertions;
using Mutagen.Bethesda.Plugins;
using SynthEBD;
using Xunit;

namespace SynthEBD.Tests;

/// <summary>
/// Tests for the Label by Sliders preview rail's pure helpers: the nearest-weight-slot ordering
/// used to resolve the default preview NPC from the Misc-settings per-weight table, and the NPC
/// candidate display-name precedence.
/// </summary>
public class SliderAnnotatorPreviewPanelTests
{
    [Fact]
    public void OrderWeightKeysByProximity_OrdersByDistanceToTarget()
    {
        var ordered = VM_SliderAnnotatorPreviewPanel.OrderWeightKeysByProximity(new[] { 0, 25, 50, 75, 100 }, 37).ToList();

        // |25-37|=12, |50-37|=13, |0-37|=37, |75-37|=38, |100-37|=63
        ordered.Should().Equal(25, 50, 0, 75, 100);
    }

    [Fact]
    public void OrderWeightKeysByProximity_TiesPreferTheLowerKey()
    {
        // 25 and 75 are both 25 away from 50; 0 and 100 are both 50 away.
        var ordered = VM_SliderAnnotatorPreviewPanel.OrderWeightKeysByProximity(new[] { 0, 25, 75, 100 }, 50).ToList();

        ordered.Should().Equal(25, 75, 0, 100);
    }

    [Fact]
    public void PreviewNpcCandidateInfo_DisplayName_PrefersNameThenEditorIdThenFormKey()
    {
        var formKey = FormKey.Factory("123456:Skyrim.esm");

        new PreviewNpcCandidateInfo(formKey, "Lydia", "HousecarlWhiterun", 100f)
            .DisplayName.Should().Be("Lydia");
        new PreviewNpcCandidateInfo(formKey, null, "HousecarlWhiterun", 100f)
            .DisplayName.Should().Be("HousecarlWhiterun");
        new PreviewNpcCandidateInfo(formKey, " ", null, 100f)
            .DisplayName.Should().Be(formKey.ToString());
    }
}
