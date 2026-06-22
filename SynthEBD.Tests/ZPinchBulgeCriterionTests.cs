using FluentAssertions;
using OpenTK.Mathematics;
using SynthEBD;
using Xunit;

namespace SynthEBD.Tests;

/// <summary>
/// Covers the front-back (depth) pinch/bulge criteria measured along Z — the direct analog of the
/// left-right (*X) waist-pinch / hip-bulge family. Band slicing is still along Y; only the measured
/// thickness axis differs (Z). The model faces -Z, so "front" = smallest Z (Min) and "back" = largest
/// Z (Max). Exercises the pure <see cref="MeasurementMath.FindBestInBox"/> entry plus the shared
/// pair-helper predicates.
/// </summary>
public class ZPinchBulgeCriterionTests
{
    private static NamedKeyVertex BoxKv(BoundingBoxCriterion criterion) => new()
    {
        Name = "kv",
        ShapeName = "S",
        Strategy = KeyVertexStrategy.BoundingBox,
        Criterion = criterion,
        BoxMinX = -1f, BoxMaxX = 1f,
        BoxMinY = -1f, BoxMaxY = 11f,
        BoxMinZ = -50f, BoxMaxZ = 50f,
    };

    // Three vertical Y-bands, each with a front (min Z) and back (max Z) silhouette vertex. The middle
    // band is pinched in toward the midline on BOTH sides (front -4, back 4), so a single-side
    // depth-pinch — which scans only one silhouette — picks from it on either side.
    private static Vector3[] PinchCloud() => new[]
    {
        new Vector3(0f, 0f,  -10f), // 0 front, bottom band
        new Vector3(0f, 0f,   10f), // 1 back,  bottom band
        new Vector3(0f, 5f,   -4f), // 2 front, middle band — front pinch
        new Vector3(0f, 5f,    4f), // 3 back,  middle band — back pinch
        new Vector3(0f, 10f, -10f), // 4 front, top band
        new Vector3(0f, 10f,  10f), // 5 back,  top band
    };

    [Fact]
    public void PinchMinZ_PicksFrontVertexOfThinnestBand()
    {
        var idx = MeasurementMath.FindBestInBox(PinchCloud(), BoxKv(BoundingBoxCriterion.PinchMinZ), BoundingBoxCriterion.PinchMinZ);
        idx.Should().Be(2); // the front (min Z) vertex of the pinched middle band
    }

    [Fact]
    public void PinchMaxZ_PicksBackVertexOfThinnestBand()
    {
        var idx = MeasurementMath.FindBestInBox(PinchCloud(), BoxKv(BoundingBoxCriterion.PinchMaxZ), BoundingBoxCriterion.PinchMaxZ);
        idx.Should().Be(3); // the back (max Z) vertex of the pinched middle band
    }

    // The bottom band's front silhouette is the most forward (Z = -12), so a single-side depth-bulge
    // on the front side selects it.
    [Fact]
    public void BulgeMinZ_PicksMostForwardFrontVertex()
    {
        var positions = new[]
        {
            new Vector3(0f, 0f,  -12f), // 0 front, bottom — most forward
            new Vector3(0f, 0f,   10f), // 1 back,  bottom
            new Vector3(0f, 5f,   -4f), // 2 front, middle
            new Vector3(0f, 5f,    6f), // 3 back,  middle
            new Vector3(0f, 10f, -10f), // 4 front, top
            new Vector3(0f, 10f,  10f), // 5 back,  top
        };
        var idx = MeasurementMath.FindBestInBox(positions, BoxKv(BoundingBoxCriterion.BulgeMinZ), BoundingBoxCriterion.BulgeMinZ);
        idx.Should().Be(0);
    }

    // Paired joint scan: front and back rows pick from the SAME band — the single thinnest (pinch) or
    // thickest (bulge) front-to-back band. A non-null sibling is required to engage the joint path.
    private static Vector3[] PairedCloud() => new[]
    {
        new Vector3(0f, 0f,  -10f), // 0 front, bottom (depth 20)
        new Vector3(0f, 0f,   10f), // 1 back,  bottom
        new Vector3(0f, 5f,   -4f), // 2 front, middle (depth 10 — thinnest)
        new Vector3(0f, 5f,    6f), // 3 back,  middle
        new Vector3(0f, 10f, -12f), // 4 front, top    (depth 26 — thickest)
        new Vector3(0f, 10f,  14f), // 5 back,  top
    };

    [Theory]
    [InlineData(BoundingBoxCriterion.PinchPairMinZ, 2)] // thinnest band, front
    [InlineData(BoundingBoxCriterion.PinchPairMaxZ, 3)] // thinnest band, back
    [InlineData(BoundingBoxCriterion.BulgePairMinZ, 4)] // thickest band, front
    [InlineData(BoundingBoxCriterion.BulgePairMaxZ, 5)] // thickest band, back
    public void PairedDepth_PicksSameBandFrontOrBack(BoundingBoxCriterion criterion, int expected)
    {
        var sibling = BoxKv(MeasurementMath.PartnerCriterion(criterion));
        var idx = MeasurementMath.FindBestInBox(PairedCloud(), BoxKv(criterion), criterion, findSibling: _ => sibling);
        idx.Should().Be(expected);
    }

    [Theory]
    [InlineData(BoundingBoxCriterion.PinchMinZ, 2)]
    [InlineData(BoundingBoxCriterion.BulgeMaxZ, 2)]
    [InlineData(BoundingBoxCriterion.PinchPairMinZ, 2)]
    [InlineData(BoundingBoxCriterion.BulgePairMaxZ, 2)]
    [InlineData(BoundingBoxCriterion.PinchMinX, 0)]
    [InlineData(BoundingBoxCriterion.BulgePairMaxX, 0)]
    public void PinchBulgeMeasureAxis_IsZForZFamily(BoundingBoxCriterion criterion, int expectedAxis)
    {
        MeasurementMath.PinchBulgeMeasureAxis(criterion).Should().Be(expectedAxis);
    }

    [Theory]
    [InlineData(BoundingBoxCriterion.PinchPairMinZ, BoundingBoxCriterion.PinchPairMaxZ)]
    [InlineData(BoundingBoxCriterion.PinchPairMaxZ, BoundingBoxCriterion.PinchPairMinZ)]
    [InlineData(BoundingBoxCriterion.BulgePairMinZ, BoundingBoxCriterion.BulgePairMaxZ)]
    [InlineData(BoundingBoxCriterion.BulgePairMaxZ, BoundingBoxCriterion.BulgePairMinZ)]
    public void PartnerCriterion_PairsZFamily(BoundingBoxCriterion input, BoundingBoxCriterion partner)
    {
        MeasurementMath.IsPairCriterion(input).Should().BeTrue();
        MeasurementMath.PartnerCriterion(input).Should().Be(partner);
    }

    [Theory]
    [InlineData(BoundingBoxCriterion.PinchPairMinZ, true)]
    [InlineData(BoundingBoxCriterion.PinchPairMaxZ, true)]
    [InlineData(BoundingBoxCriterion.BulgePairMinZ, false)]
    [InlineData(BoundingBoxCriterion.BulgePairMaxZ, false)]
    public void IsPairPinch_DistinguishesPinchFromBulge(BoundingBoxCriterion criterion, bool isPinch)
    {
        MeasurementMath.IsPairPinch(criterion).Should().Be(isPinch);
    }

    // A Z-pair sibling is matched by same shape + box + opposite criterion (box mode).
    [Fact]
    public void FindPairSibling_ZPair_MatchesSameBoxOppositeCriterion()
    {
        var front = BoxKv(BoundingBoxCriterion.BulgePairMinZ);
        var back = BoxKv(BoundingBoxCriterion.BulgePairMaxZ);
        var unrelated = BoxKv(BoundingBoxCriterion.PinchPairMaxZ);

        MeasurementMath.FindPairSibling(front, new[] { front, back, unrelated }).Should().BeSameAs(back);
        MeasurementMath.FindPairSibling(front, new[] { front, unrelated }).Should().BeNull();
    }
}
