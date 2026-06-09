using FluentAssertions;
using Xunit;

namespace SynthEBD.Tests;

/// <summary>
/// B44-2: CombinationLog.LogAssignedRecords did Signature.Split(':')[1] with no guard (unlike its
/// LogCombinationSelections sibling), so a signature lacking ':' threw IndexOutOfRange during patching.
/// TryGetSubgroupIdsFromSignature extracts the post-colon subgroup IDs and returns false (skip) for a
/// signature with no ':'.
/// </summary>
public class CombinationLogSignatureTests
{
    [Theory]
    [InlineData("AssetPack:1.2.3", true, "1.2.3")]
    [InlineData("Pack:", true, "")]        // trailing colon -> empty second part, still valid
    [InlineData("Pack:a:b", true, "a")]    // [1] is the second segment only
    [InlineData("NoColon", false, "")]     // the bug case: no ':' -> false, no throw
    [InlineData("", false, "")]
    public void TryGetSubgroupIdsFromSignature_GuardsMissingColon(string signature, bool expectedOk, string expectedIds)
    {
        var ok = CombinationLog.TryGetSubgroupIdsFromSignature(signature, out var ids);
        ok.Should().Be(expectedOk);
        ids.Should().Be(expectedIds);
    }
}
