using FluentAssertions;
using SkyRender;
using SkyRender.Character;
using Xunit;

namespace SkyRender.Tests;

/// <summary>
/// Dismember-partition hiding: the partition-ID to biped-slot mapping, the per-triangle slot table,
/// the index filter the renderer uploads, and which worn piece hides which partition of another.
/// </summary>
public class PartitionOcclusionTests
{
    private static int Bit(int slot) => 1 << (slot - 30);
    private static int Bits(params int[] slots) => slots.Aggregate(0, (m, s) => m | Bit(s));

    [Theory]
    [InlineData(32, 32)]   // SBP_32_BODY
    [InlineData(34, 34)]   // SBP_34_FOREARMS
    [InlineData(38, 38)]   // SBP_38_CALVES
    [InlineData(61, 61)]   // SBP_61_FX01
    [InlineData(131, 31)]  // SBP_131_HAIR folds onto the hair slot
    [InlineData(138, 38)]
    [InlineData(230, 0)]   // SBP_230_HEAD: never hidden by slot-30 headgear
    [InlineData(1, 0)]     // legacy BP_HEAD
    [InlineData(0, 0)]     // BP_TORSO
    [InlineData(62, 0)]
    [InlineData(1000, 0)]  // torso sections
    public void PartitionToHideableSlot_MapsSkyrimIds(int partId, int slot)
    {
        NifMeshBuilder.PartitionToHideableSlot((ushort)partId).Should().Be(slot);
    }

    [Fact]
    public void MapTrianglePartitionSlots_TwoSlots_GivesPerTriangleSlots()
    {
        // The vanilla light cuirass skin shape: partitions [32, 38, 34].
        var slots = NifMeshBuilder.MapTrianglePartitionSlots(new byte[] { 32, 38, 34 }, new[] { 0, 0, 1, 2, 1 });
        slots.Should().Equal(32, 32, 38, 34, 38);
    }

    [Fact]
    public void MapTrianglePartitionSlots_OneHideableSlot_IsNull()
    {
        NifMeshBuilder.MapTrianglePartitionSlots(new byte[] { 32, 32 }, new[] { 0, 1, 0 }).Should().BeNull();
        NifMeshBuilder.MapTrianglePartitionSlots(new byte[] { 30, 0 }, new[] { 0, 1 }).Should().BeNull();
    }

    [Fact]
    public void MapTrianglePartitionSlots_BadPartitionIndex_IsNull()
    {
        NifMeshBuilder.MapTrianglePartitionSlots(new byte[] { 32, 38 }, new[] { 0, 2 }).Should().BeNull();
        NifMeshBuilder.MapTrianglePartitionSlots(new byte[] { 32, 38 }, new[] { -1, 1 }).Should().BeNull();
    }

    [Fact]
    public void FilterIndicesByPartition_DropsHiddenSlotTriangles_KeepsOrder()
    {
        int[] indices = { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11 };
        byte[] slots = { 32, 38, 32, 34 };

        GlMesh.FilterIndicesByPartition(indices, slots, Bit(38))
            .Should().Equal(0, 1, 2, 6, 7, 8, 9, 10, 11);
        GlMesh.FilterIndicesByPartition(indices, slots, Bits(38, 34))
            .Should().Equal(0, 1, 2, 6, 7, 8);
        GlMesh.FilterIndicesByPartition(indices, slots, 0).Should().Equal(indices);
    }

    [Fact]
    public void FilterIndicesByPartition_UnmappedTriangles_AreNeverHidden()
    {
        int[] indices = { 0, 1, 2, 3, 4, 5 };
        GlMesh.FilterIndicesByPartition(indices, new byte[] { 0, 38 }, -1).Should().Equal(0, 1, 2);
        // A slot table shorter than the index buffer keeps the rest.
        GlMesh.FilterIndicesByPartition(indices, new byte[] { 38 }, Bit(38)).Should().Equal(3, 4, 5);
    }

    // The pieces of the vanilla Imperial light outfit, as the viewer tags their shapes.
    private static NpcMeshInfo Piece(string key, int priority, params int[] slots) => new()
    {
        OverrideKey = key, SlotDrawPriority = priority, BipedSlots = Bits(slots), HidesSlots = Bits(slots),
    };

    private static readonly NpcMeshInfo Boots = Piece("boots", 1, 37, 38);
    private static readonly NpcMeshInfo CuirassSkin = Piece("cuirass", 1, 32, 34, 38);
    private static readonly NpcMeshInfo CuirassOuter = Piece("cuirass", 1, 32, 34, 38);
    private static readonly NpcMeshInfo Gauntlets = Piece("gauntlets", 1, 33, 34);

    [Fact]
    public void Boots_HideTheCuirassCalves()
    {
        // 37 too: the cuirass does not occupy it (and carries no foot partition).
        PartitionOcclusion.HiddenSlots(CuirassSkin, Boots).Should().Be(Bits(37, 38));
    }

    [Fact]
    public void Cuirass_DoesNotHideTheBootCuff()
    {
        (PartitionOcclusion.HiddenSlots(Boots, CuirassSkin) & Bit(38)).Should().Be(0);
        (PartitionOcclusion.HiddenSlots(Boots, CuirassSkin) & Bit(37)).Should().Be(0);
    }

    [Fact]
    public void Gauntlets_HideTheCuirassForearms()
    {
        PartitionOcclusion.HiddenSlots(CuirassSkin, new[] { Boots, Gauntlets, CuirassOuter })
            .Should().Be(Bits(33, 34, 37, 38));
    }

    [Fact]
    public void ShapesOfOnePiece_DoNotHideEachOther()
    {
        PartitionOcclusion.HiddenSlots(CuirassSkin, CuirassOuter).Should().Be(0);
        PartitionOcclusion.HiddenSlots(CuirassSkin, CuirassSkin).Should().Be(0);
    }

    [Fact]
    public void SlotTheTargetDoesNotOccupy_IsHidden()
    {
        // A cuirass whose ArmorAddon lists only slot 32 still carries a calf partition.
        var cuirass32 = Piece("c32", 1, 32);
        PartitionOcclusion.HiddenSlots(cuirass32, Boots).Should().Be(Bits(37, 38));
    }

    [Fact]
    public void EqualSlotCounts_HideNeither()
    {
        var a = Piece("a", 1, 37, 38);
        var b = Piece("b", 1, 33, 38);
        PartitionOcclusion.HiddenSlots(a, b).Should().Be(Bit(33));
        PartitionOcclusion.HiddenSlots(b, a).Should().Be(Bit(37));
    }

    [Fact]
    public void Priority_HeadgearOverArmor_BaseNeverHides()
    {
        var hood = Piece("hood", 2, 30, 31);
        var cowl = Piece("cowl", 1, 31, 32, 38);
        PartitionOcclusion.HiddenSlots(cowl, hood).Should().Be(Bits(30, 31));
        PartitionOcclusion.HiddenSlots(hood, cowl).Should().Be(0);

        var body = new NpcMeshInfo { BipedSlots = Bit(32), SlotDrawPriority = 0 };
        var skinOverride = new NpcMeshInfo { OverrideKey = "skin", BipedSlots = Bit(38), HidesSlots = Bit(38) };
        PartitionOcclusion.HiddenSlots(body, Boots).Should().Be(Bits(37, 38));
        PartitionOcclusion.HiddenSlots(CuirassSkin, skinOverride).Should().Be(0);
        PartitionOcclusion.HiddenSlots(Boots, body).Should().Be(0);
    }
}
