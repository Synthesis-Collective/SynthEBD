using FluentAssertions;
using OpenTK.Mathematics;
using SynthEBD;
using Xunit;

namespace SynthEBD.Tests;

/// <summary>
/// Covers the <see cref="KeyVertexStrategy.Region"/> key-vertex strategy: the criterion picks a vertex
/// from a region's member set instead of an axis-aligned box. Tests exercise the pure
/// <see cref="MeasurementMath.FindBestInBox"/> entry (with a region member set), the pair-sibling
/// matcher, the region member resolver, and the cache fingerprint.
/// </summary>
public class RegionKeyVertexTests
{
    private static NamedKeyVertex RegionKv(BoundingBoxCriterion criterion, string region = "r", string shape = "S") => new()
    {
        Name = "kv",
        ShapeName = shape,
        Strategy = KeyVertexStrategy.Region,
        RegionRefName = region,
        Criterion = criterion,
    };

    // The gate is membership, not the box: a non-member with the more extreme coordinate must NOT win.
    [Fact]
    public void Region_MaxX_PicksMember_NotMoreExtremeNonMember()
    {
        var positions = new[]
        {
            new Vector3(0f, 0f, 0f),   // 0 member
            new Vector3(10f, 0f, 0f),  // 1 member (member-max X)
            new Vector3(20f, 0f, 0f),  // 2 NON-member (globally max X)
        };
        var members = new HashSet<int> { 0, 1 };

        var idx = MeasurementMath.FindBestInBox(positions, RegionKv(BoundingBoxCriterion.MaxX), BoundingBoxCriterion.MaxX, regionMembers: members);

        idx.Should().Be(1);
    }

    [Fact]
    public void Region_MinZ_PicksFrontmostMember()
    {
        var positions = new[]
        {
            new Vector3(0f, 0f, 5f),    // 0 member
            new Vector3(0f, 0f, -3f),   // 1 member (member-min Z)
            new Vector3(0f, 0f, -99f),  // 2 NON-member (globally min Z)
        };
        var members = new HashSet<int> { 0, 1 };

        var idx = MeasurementMath.FindBestInBox(positions, RegionKv(BoundingBoxCriterion.MinZ), BoundingBoxCriterion.MinZ, regionMembers: members);

        idx.Should().Be(1);
    }

    [Fact]
    public void Region_NoMembers_ReturnsNull()
    {
        var positions = new[] { new Vector3(1f, 2f, 3f) };
        var idx = MeasurementMath.FindBestInBox(positions, RegionKv(BoundingBoxCriterion.MaxX), BoundingBoxCriterion.MaxX, regionMembers: new HashSet<int>());
        idx.Should().BeNull();
    }

    // When the member set equals the box-contained set, every gate-only criterion (axis extremes and
    // the left/right-half variants — none of which use the box as a geometric frame) returns the same
    // index in Region mode as in BoundingBox mode. Guards the refactor.
    [Theory]
    [InlineData(BoundingBoxCriterion.MaxX)]
    [InlineData(BoundingBoxCriterion.MinX)]
    [InlineData(BoundingBoxCriterion.MaxY)]
    [InlineData(BoundingBoxCriterion.MinY)]
    [InlineData(BoundingBoxCriterion.MaxZ)]
    [InlineData(BoundingBoxCriterion.MinZ)]
    [InlineData(BoundingBoxCriterion.MinYLeftOfX)]
    [InlineData(BoundingBoxCriterion.MaxYRightOfX)]
    public void Region_MatchesBoundingBox_WhenMembersEqualBoxSet(BoundingBoxCriterion criterion)
    {
        var positions = new[]
        {
            new Vector3(-4f, 1f, 2f),
            new Vector3(-1f, 5f, -2f),
            new Vector3(2f, -3f, 1f),
            new Vector3(3f, 4f, 5f),
            new Vector3(50f, 50f, 50f),   // far outside the box
            new Vector3(-50f, -9f, 0f),   // far outside the box
        };
        var boxKv = new NamedKeyVertex
        {
            Strategy = KeyVertexStrategy.BoundingBox,
            Criterion = criterion,
            BoxMinX = -5f, BoxMinY = -5f, BoxMinZ = -5f,
            BoxMaxX = 5f, BoxMaxY = 5f, BoxMaxZ = 5f,
        };

        // Members = exactly the vertices the box contains.
        var members = new HashSet<int>();
        for (int i = 0; i < positions.Length; i++)
        {
            var p = positions[i];
            if (p.X >= -5f && p.X <= 5f && p.Y >= -5f && p.Y <= 5f && p.Z >= -5f && p.Z <= 5f) members.Add(i);
        }

        var boxIdx = MeasurementMath.FindBestInBox(positions, boxKv, criterion);
        var regionIdx = MeasurementMath.FindBestInBox(positions, RegionKv(criterion), criterion, regionMembers: members);

        regionIdx.Should().Be(boxIdx);
    }

    // Pinch over a region: bins frame over the member AABB; the winner is a member on the requested side.
    [Fact]
    public void Region_PinchMinX_PicksMemberOnLeft()
    {
        // Two vertical silhouettes. The middle Y band is pinched in (closer to the midline on the left).
        var positions = new[]
        {
            new Vector3(-10f, 0f, 0f),  // 0 left, bottom band
            new Vector3(10f, 0f, 0f),   // 1 right, bottom band
            new Vector3(-4f, 5f, 0f),   // 2 left, middle band — the pinch
            new Vector3(10f, 5f, 0f),   // 3 right, middle band
            new Vector3(-10f, 10f, 0f), // 4 left, top band
            new Vector3(10f, 10f, 0f),  // 5 right, top band
        };
        var members = new HashSet<int> { 0, 1, 2, 3, 4, 5 };

        var idx = MeasurementMath.FindBestInBox(positions, RegionKv(BoundingBoxCriterion.PinchMinX), BoundingBoxCriterion.PinchMinX, regionMembers: members);

        idx.Should().Be(2); // the left vertex whose X is closest to the midline
    }

    // BoneTransition with no skin data degrades to the axis extremum — restricted to members.
    [Fact]
    public void Region_BoneTransition_NoBoneData_FallsBackToMemberAxisExtremum()
    {
        var positions = new[]
        {
            new Vector3(0f, 0f, 0f),    // 0 member
            new Vector3(8f, 0f, 0f),    // 1 member (member-max X)
            new Vector3(40f, 0f, 0f),   // 2 NON-member
        };
        var members = new HashSet<int> { 0, 1 };

        var idx = MeasurementMath.FindBestInBox(positions, RegionKv(BoundingBoxCriterion.BoneTransitionMaxX),
            BoundingBoxCriterion.BoneTransitionMaxX, boneIndices: null, boneWeights: null, regionMembers: members);

        idx.Should().Be(1);
    }

    // Paired Region siblings are matched by same region + opposite criterion; a different region is not a sibling.
    [Fact]
    public void FindPairSibling_Region_MatchesSameRegionOppositeCriterion()
    {
        var left = RegionKv(BoundingBoxCriterion.PinchPairMinX, region: "waist");
        var right = RegionKv(BoundingBoxCriterion.PinchPairMaxX, region: "waist");
        var other = RegionKv(BoundingBoxCriterion.PinchPairMaxX, region: "hip");

        MeasurementMath.FindPairSibling(left, new[] { left, right, other }).Should().BeSameAs(right);
        MeasurementMath.FindPairSibling(left, new[] { left, other }).Should().BeNull();
    }

    [Fact]
    public void FindPairSibling_Region_DoesNotMatchBoundingBoxRow()
    {
        var regionRow = RegionKv(BoundingBoxCriterion.PinchPairMinX, region: "waist");
        var boxRow = new NamedKeyVertex
        {
            Strategy = KeyVertexStrategy.BoundingBox,
            Criterion = BoundingBoxCriterion.PinchPairMaxX,
            ShapeName = "S",
        };
        MeasurementMath.FindPairSibling(regionRow, new[] { regionRow, boxRow }).Should().BeNull();
    }

    [Fact]
    public void ResolveMemberVertices_ReturnsInBoxIndices()
    {
        var zeroed = new[]
        {
            new Vector3(0f, 0f, 0f),    // 0 in
            new Vector3(2f, 2f, 2f),    // 1 in
            new Vector3(20f, 0f, 0f),   // 2 out
        };
        var box = new RegionVolumeEvaluator.RegionAabb(new Vector3(-5f, -5f, -5f), new Vector3(5f, 5f, 5f));

        var members = RegionVolumeEvaluator.ResolveMemberVertices(zeroed, box, default, addSet: null, removeSet: null);

        members.Should().BeEquivalentTo(new[] { 0, 1 });
    }

    [Fact]
    public void ResolveMemberVertices_HonorsAddAndRemoveEdits()
    {
        var zeroed = new[]
        {
            new Vector3(0f, 0f, 0f),    // 0 in box
            new Vector3(20f, 0f, 0f),   // 1 out of box, force-added
        };
        var box = new RegionVolumeEvaluator.RegionAabb(new Vector3(-5f, -5f, -5f), new Vector3(5f, 5f, 5f));

        var added = RegionVolumeEvaluator.ResolveMemberVertices(zeroed, box, default, addSet: new HashSet<int> { 1 }, removeSet: null);
        added.Should().BeEquivalentTo(new[] { 0, 1 });

        var removed = RegionVolumeEvaluator.ResolveMemberVertices(zeroed, box, default, addSet: null, removeSet: new HashSet<int> { 0 });
        removed.Should().BeEmpty();
    }

    // A Region key vertex's measurement fingerprint folds in the referenced region's identity, so a
    // region box edit invalidates the cached value; a region-ref change does too.
    [Fact]
    public void Fingerprint_RegionKeyVertex_ChangesWithReferencedRegionBoxAndRef()
    {
        var kvA = RegionKv(BoundingBoxCriterion.MaxX, region: "r1");
        kvA.Name = "A";
        var kvB = RegionKv(BoundingBoxCriterion.MinX, region: "r1");
        kvB.Name = "B";
        var keyVerts = new Dictionary<string, NamedKeyVertex> { ["A"] = kvA, ["B"] = kvB };

        var def = new MeasurementDefinition
        {
            Name = "width",
            Kind = MeasurementKind.PointDistance,
            VertexRefNames = new List<string> { "A", "B" },
        };

        NamedRegion Region(float maxX) => new() { Name = "r1", ShapeName = "S", BoxMinX = -1f, BoxMaxX = maxX, BoxMinY = -1f, BoxMaxY = 1f, BoxMinZ = -1f, BoxMaxZ = 1f };

        var fp1 = MeasurementCacheStore.ComputeMeasurementFingerprint(def, keyVerts, new Dictionary<string, NamedRegion> { ["r1"] = Region(1f) });
        var fp2 = MeasurementCacheStore.ComputeMeasurementFingerprint(def, keyVerts, new Dictionary<string, NamedRegion> { ["r1"] = Region(9f) });
        fp1.Should().NotBe(fp2, "a referenced region's box change must invalidate the measurement");

        // Repoint both key vertices at a different region name → fingerprint changes again.
        kvA.RegionRefName = "r2";
        kvB.RegionRefName = "r2";
        var regionsR2 = new Dictionary<string, NamedRegion> { ["r2"] = new NamedRegion { Name = "r2", ShapeName = "S", BoxMaxX = 1f } };
        var fp3 = MeasurementCacheStore.ComputeMeasurementFingerprint(def, keyVerts, regionsR2);
        fp3.Should().NotBe(fp1, "changing which region the key vertices reference must invalidate the measurement");
    }
}
