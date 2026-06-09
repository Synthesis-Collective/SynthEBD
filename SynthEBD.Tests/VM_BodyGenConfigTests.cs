using System.Linq;
using FluentAssertions;
using Xunit;

namespace SynthEBD.Tests;

/// <summary>
/// B37: in the BodyGen-config editor, OnDescriptorValueDeletion was a verbatim copy of the OBody
/// editor's handler -- it stripped subgroups' BodySlide descriptors (Allowed/Disallowed/Prioritized,
/// the last having no BodyGen counterpart) instead of their BodyGen descriptors. So deleting a single
/// BodyGen descriptor value corrupted unrelated BodySlide rules and left dangling BodyGen references.
/// The extracted RemoveBodyGenDescriptorFromSubgroups strips the BodyGen sets and leaves BodySlide alone.
/// </summary>
public class VM_BodyGenConfigTests
{
    private static BodyShapeDescriptor.LabelSignature Sig(string category, string value)
        => new BodyShapeDescriptor.LabelSignature { Category = category, Value = value };

    [Fact]
    public void RemoveBodyGenDescriptorFromSubgroups_StripsBodyGenAndLeavesBodySlide()
    {
        var sg = new AssetPack.Subgroup
        {
            AllowedBodyGenDescriptors = new() { Sig("Build", "Athletic"), Sig("Build", "Muscular") },
            DisallowedBodyGenDescriptors = new() { Sig("Build", "Athletic") },
            AllowedBodySlideDescriptors = new() { Sig("Build", "Athletic") },
        };

        VM_BodyGenConfig.RemoveBodyGenDescriptorFromSubgroups(new[] { sg }, "Build: Athletic");

        sg.AllowedBodyGenDescriptors.Select(x => x.ToString()).Should().ContainSingle().Which.Should().Be("Build: Muscular");
        sg.DisallowedBodyGenDescriptors.Should().BeEmpty();
        // the bug removed this too; the fix must leave BodySlide rules untouched
        sg.AllowedBodySlideDescriptors.Select(x => x.ToString()).Should().ContainSingle().Which.Should().Be("Build: Athletic");
    }
}
