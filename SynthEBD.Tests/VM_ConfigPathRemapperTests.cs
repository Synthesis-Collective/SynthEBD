using FluentAssertions;
using Xunit;

namespace SynthEBD.Tests;

/// <summary>
/// B32: GetMatchingDirCount (the Config Path Remapper's path-similarity tiebreak) built split2 from
/// (Path.GetDirectoryName(path2) ?? path1) -- the null fallback used path1, a copy-paste from the
/// split1 line. When path2 has no parent directory (a root/null), split2 was derived from path1, so
/// the method compared path1 to itself and reported an inflated shared-segment count. Fixed to
/// ?? path2. These run the now-public-static helper directly (Windows path separators; the test
/// project targets net8.0-windows).
/// </summary>
public class VM_ConfigPathRemapperTests
{
    [Theory]
    // Regression: path2 is a drive root, so GetDirectoryName(path2) is null and the fallback fires.
    // Correct = 0 (a Q: root shares no directory with a Z: path); the bug reported 3 (path1's own depth).
    [InlineData(@"Z:\alpha\beta\file.dds", @"Q:\", 0)]
    // Identical parent directories -> all three segments shared.
    [InlineData(@"textures\actors\character\body.dds", @"textures\actors\character\hands.dds", 3)]
    // Partial overlap -> only the shared leading segment counts.
    [InlineData(@"textures\actors\character\body.dds", @"textures\clutter\common\foo.dds", 1)]
    // Disjoint trees -> nothing shared.
    [InlineData(@"meshes\armor\iron\cuirass.nif", @"textures\actors\character\body.dds", 0)]
    public void GetMatchingDirCount_CountsSharedParentSegments(string path1, string path2, int expected)
    {
        VM_ConfigPathRemapper.GetMatchingDirCount(path1, path2).Should().Be(expected);
    }
}
