using FluentAssertions;
using Xunit;

namespace SynthEBD.Tests;

public class NPCAssignmentTests
{
    // B25: NPCAssignment.SubgroupIDs defaulted to null (unlike its sibling assignment types), so importing a
    // zEBD specific-NPC assignment with forced subgroups dereferenced null in ToSynthEBDNPCAssignments
    // (s.SubgroupIDs.Add(...)), and AssetSelector dereferenced it unguarded too. The default is now a fresh list.
    [Fact]
    public void SubgroupIDs_DefaultsToNonNullList_AndIsAddable()
    {
        var assignment = new NPCAssignment();

        assignment.SubgroupIDs.Should().NotBeNull();

        var act = () => assignment.SubgroupIDs.Add("armor_steel");
        act.Should().NotThrow();
        assignment.SubgroupIDs.Should().ContainSingle().Which.Should().Be("armor_steel");
    }
}
