using FluentAssertions;
using Xunit;

namespace SynthEBD.Tests;

// R3: the 37 record-path DSL strings that VM_FilePathReplacement.GetPathFromTypeString returns (and that its
// DestinationDetailAbstractDictionary keys on) were de-duplicated to reference FilePathDestinationMap.Dest_*
// (the canonical source). The substitution was made by value-equality, so output is unchanged. These cases
// guard that mapping: short head paths are pinned to literal strings (independent of the consts), and the long
// body paths are pinned to the FilePathDestinationMap consts they must resolve to.
public class GetPathFromTypeStringTests
{
    [Theory]
    [InlineData("Head Diffuse", "HeadTexture.Diffuse.GivenPath")]
    [InlineData("Head Normal", "HeadTexture.NormalOrGloss.GivenPath")]
    [InlineData("Head Subsurface", "HeadTexture.GlowOrDetailMap.GivenPath")]
    [InlineData("Head Specular", "HeadTexture.BacklightMaskOrSpecular.GivenPath")]
    [InlineData("Head Detail", "HeadTexture.Height.GivenPath")]
    public void Head_ReturnsExpectedLiteralRecordPath(string alias, string expected)
        => VM_FilePathReplacement.GetPathFromTypeString(alias).Should().Be(expected);

    [Theory]
    [InlineData("not a real alias")]
    [InlineData("")]
    [InlineData("Head Diffuse (Male)")] // the display-format name is NOT a valid menu alias
    public void UnknownAlias_ReturnsEmpty(string alias)
        => VM_FilePathReplacement.GetPathFromTypeString(alias).Should().Be("");

    [Fact]
    public void BodyAliases_ResolveToFilePathDestinationMapConsts()
    {
        VM_FilePathReplacement.GetPathFromTypeString("Torso Diffuse Male").Should().Be(FilePathDestinationMap.Dest_TorsoMaleDiffuse);
        VM_FilePathReplacement.GetPathFromTypeString("Hands Normal Male").Should().Be(FilePathDestinationMap.Dest_HandsMaleNormal);
        VM_FilePathReplacement.GetPathFromTypeString("Feet Subsurface Male").Should().Be(FilePathDestinationMap.Dest_FeetMaleSubsurface);
        VM_FilePathReplacement.GetPathFromTypeString("Tail Specular Male").Should().Be(FilePathDestinationMap.Dest_TailMaleSpecular);
        VM_FilePathReplacement.GetPathFromTypeString("Torso Diffuse Female").Should().Be(FilePathDestinationMap.Dest_TorsoFemaleDiffuse);
        VM_FilePathReplacement.GetPathFromTypeString("Hands Normal Female").Should().Be(FilePathDestinationMap.Dest_HandsFemaleNormal);
        VM_FilePathReplacement.GetPathFromTypeString("Feet Subsurface Female").Should().Be(FilePathDestinationMap.Dest_FeetFemaleSubsurface);
        VM_FilePathReplacement.GetPathFromTypeString("Tail Specular Female").Should().Be(FilePathDestinationMap.Dest_TailFemaleSpecular);
    }
}
