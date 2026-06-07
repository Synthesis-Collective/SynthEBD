using FluentAssertions;
using Xunit;

namespace SynthEBD.Tests;

/// <summary>
/// Unit tests for <see cref="FlattenedAssetPack.FormatSubgroupPosition"/>, the bounds-checked display-label
/// helper behind <see cref="FlattenedAssetPack.GetSubgroupPositionString"/>. The out-of-range cases are the
/// regression guard for the old off-by-one (<c>Source.Subgroups.Count &gt;= index</c>), which let
/// <c>index == Count</c> through and then threw <see cref="System.ArgumentOutOfRangeException"/> indexing the list.
/// </summary>
public class FlattenedAssetPackTests
{
    private static List<AssetPack.Subgroup> TwoSubgroups() => new()
    {
        new AssetPack.Subgroup { ID = "1A", Name = "Alpha" },
        new AssetPack.Subgroup { ID = "2B", Name = "Beta" },
    };

    [Fact]
    public void ValidIndex_FormatsIdAndName()
    {
        var subgroups = TwoSubgroups();
        FlattenedAssetPack.FormatSubgroupPosition(subgroups, 0, includeFormatting: false).Should().Be("1A: Alpha");
        FlattenedAssetPack.FormatSubgroupPosition(subgroups, 1, includeFormatting: false).Should().Be("2B: Beta");
    }

    [Fact]
    public void ValidIndex_WithFormatting_WrapsInParentheses()
    {
        FlattenedAssetPack.FormatSubgroupPosition(TwoSubgroups(), 1).Should().Be(" (2B: Beta)");
    }

    [Fact]
    public void IndexEqualToCount_ReturnsEmpty_AndDoesNotThrow()
    {
        // Regression guard: the old "Count >= index" admitted index == Count, then threw indexing the list.
        var subgroups = TwoSubgroups();
        FlattenedAssetPack.FormatSubgroupPosition(subgroups, subgroups.Count).Should().BeEmpty();
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(3)]
    [InlineData(100)]
    public void OutOfRangeIndex_ReturnsEmpty(int index)
    {
        FlattenedAssetPack.FormatSubgroupPosition(TwoSubgroups(), index).Should().BeEmpty();
    }

    [Fact]
    public void NullOrEmptySubgroups_ReturnEmpty()
    {
        FlattenedAssetPack.FormatSubgroupPosition(null!, 0).Should().BeEmpty();
        FlattenedAssetPack.FormatSubgroupPosition(new List<AssetPack.Subgroup>(), 0).Should().BeEmpty();
    }
}
