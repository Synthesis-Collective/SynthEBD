using FluentAssertions;
using SynthEBD;
using Xunit;

namespace SynthEBD.Tests;

/// <summary>
/// Phase-4a tests for the new region row VM, <see cref="VM_NamedRegion"/> — the genuinely new
/// pure-data type, where a field-mapping slip (e.g. a swapped Box min/max) would hide. The row is
/// constructed with a null parent: round-trip / field / trim assertions never touch the parent (it
/// is captured only by <see cref="VM_NamedRegion.DeleteCommand"/>, which these tests don't invoke).
///
/// The owning <see cref="VM_BodyTypeProfile"/> is a WPF/ReactiveUI view-model that isn't
/// headless-constructible (its ctor wires a viewer-swap observable), so the profile-side wiring —
/// the ctor load loop, duplicate-name flagging, DeleteCommand removal, and DumpToModel emitting the
/// VM collection — is run-verified in the app during Phase 4b rather than unit-tested here, matching
/// how the parallel <see cref="VM_NamedKeyVertex"/> path is covered.
/// </summary>
public class RegionVolumeUiVmTests
{
    private static NamedRegion Region(string name = "chest_bump", int? caps = 1) => new NamedRegion
    {
        Name = name,
        ShapeName = "CBBE 3BA",
        BoxMinX = -8f, BoxMinY = 90f, BoxMinZ = -12f,
        BoxMaxX = 8f, BoxMaxY = 110f, BoxMaxZ = 2f,
        ExpectedCapCount = caps,
    };

    [Fact]
    public void Ctor_CopiesAllFieldsFromModel()
    {
        var vm = new VM_NamedRegion(Region(caps: 2), null!);
        vm.Name.Should().Be("chest_bump");
        vm.ShapeName.Should().Be("CBBE 3BA");
        vm.BoxMinX.Should().Be(-8f);
        vm.BoxMinY.Should().Be(90f);
        vm.BoxMinZ.Should().Be(-12f);
        vm.BoxMaxX.Should().Be(8f);
        vm.BoxMaxY.Should().Be(110f);
        vm.BoxMaxZ.Should().Be(2f);
        vm.ExpectedCapCount.Should().Be(2);
    }

    [Fact]
    public void DumpToModel_RoundTripsAllFields()
    {
        var dumped = new VM_NamedRegion(Region(caps: 2), null!).DumpToModel();
        dumped.Name.Should().Be("chest_bump");
        dumped.ShapeName.Should().Be("CBBE 3BA");
        dumped.BoxMinX.Should().Be(-8f);
        dumped.BoxMinY.Should().Be(90f);
        dumped.BoxMinZ.Should().Be(-12f);
        dumped.BoxMaxX.Should().Be(8f);
        dumped.BoxMaxY.Should().Be(110f);
        dumped.BoxMaxZ.Should().Be(2f);
        dumped.ExpectedCapCount.Should().Be(2);
    }

    [Fact]
    public void DumpToModel_ReflectsEditsMadeOnTheVm()
    {
        var vm = new VM_NamedRegion(Region(), null!)
        {
            Name = "thigh",
            ShapeName = "BHUNP",
            BoxMaxZ = 5.5f,
            ExpectedCapCount = 2,
        };
        var dumped = vm.DumpToModel();
        dumped.Name.Should().Be("thigh");
        dumped.ShapeName.Should().Be("BHUNP");
        dumped.BoxMaxZ.Should().Be(5.5f);
        dumped.ExpectedCapCount.Should().Be(2);
    }

    [Fact]
    public void DumpToModel_TrimsNameAndShape()
    {
        var vm = new VM_NamedRegion(Region(), null!) { Name = "  spaced  ", ShapeName = "  CBBE 3BA  " };
        var dumped = vm.DumpToModel();
        dumped.Name.Should().Be("spaced");
        dumped.ShapeName.Should().Be("CBBE 3BA");
    }

    [Fact]
    public void ExpectedCapCount_Null_RoundTrips()
    {
        var vm = new VM_NamedRegion(Region(caps: null), null!);
        vm.ExpectedCapCount.Should().BeNull();
        vm.DumpToModel().ExpectedCapCount.Should().BeNull();
    }

    [Fact]
    public void DefaultDiagnosticState_IsUnknownAndEmpty()
    {
        var vm = new VM_NamedRegion(Region(), null!);
        vm.ResolutionState.Should().Be(RegionResolutionState.Unknown);
        vm.ResolveDiagnostic.Should().BeEmpty();
        vm.HasDuplicateName.Should().BeFalse();
    }
}
