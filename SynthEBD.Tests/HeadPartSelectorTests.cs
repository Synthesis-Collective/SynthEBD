using FluentAssertions;
using Mutagen.Bethesda.Plugins;
using Xunit;

namespace SynthEBD.Tests;

/// <summary>
/// Unit tests for <see cref="HeadPartSelector.ResolveHeadPartConflict"/>, which picks the winning head-part
/// FormKey between an asset-pack assignment and a head-part-menu assignment. A real (non-null) assignment must
/// beat an unassigned (null) one from the other source <em>regardless</em> of the configured conflict winner —
/// the regression guard for the old dead <c>FormKey == null</c> early-outs (FormKey is a struct, so those were
/// always false/true), which let the winner switch return a null and drop a real head part.
/// </summary>
public class HeadPartSelectorTests
{
    private static readonly ModKey Mod = ModKey.FromNameAndExtension("Test.esp");
    private static FormKey Fk(uint id) => new FormKey(Mod, id);

    [Theory]
    [InlineData(HeadPartSourceCandidate.AssetPack)]
    [InlineData(HeadPartSourceCandidate.HeadPartsMenu)]
    public void AssetReal_MenuNull_ReturnsAsset_RegardlessOfWinner(HeadPartSourceCandidate winner)
    {
        var asset = Fk(0x800);
        HeadPartSelector.ResolveHeadPartConflict(asset, FormKey.Null, winner).Should().Be(asset);
    }

    [Theory]
    [InlineData(HeadPartSourceCandidate.AssetPack)]
    [InlineData(HeadPartSourceCandidate.HeadPartsMenu)]
    public void MenuReal_AssetNull_ReturnsMenu_RegardlessOfWinner(HeadPartSourceCandidate winner)
    {
        var menu = Fk(0x801);
        HeadPartSelector.ResolveHeadPartConflict(FormKey.Null, menu, winner).Should().Be(menu);
    }

    [Fact]
    public void BothReal_AssetPackWinner_ReturnsAsset()
    {
        var asset = Fk(0x800);
        var menu = Fk(0x801);
        HeadPartSelector.ResolveHeadPartConflict(asset, menu, HeadPartSourceCandidate.AssetPack).Should().Be(asset);
    }

    [Fact]
    public void BothReal_HeadPartsMenuWinner_ReturnsMenu()
    {
        var asset = Fk(0x800);
        var menu = Fk(0x801);
        HeadPartSelector.ResolveHeadPartConflict(asset, menu, HeadPartSourceCandidate.HeadPartsMenu).Should().Be(menu);
    }
}
