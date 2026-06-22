using FluentAssertions;
using OpenTK.Mathematics;
using SynthEBD;
using Xunit;

namespace SynthEBD.Tests;

/// <summary>
/// Covers the <see cref="KeyVertexStrategy.Coordinate"/> key-vertex strategy: the author picks one exact
/// vertex (like Explicit), but the vertex's zeroed-space position is stored and re-matched to the nearest
/// zeroed vertex at evaluation (like Region), making the handle agnostic to vertex index and to renumbering
/// when a body type variant is rebuilt. Tests exercise the pure <see cref="RegionVolumeEvaluator.MatchNearestVertex"/>
/// matcher, the end-to-end <see cref="MeasurementMath.TryEvaluate"/> path (with a zeroed lookup), graceful
/// degradation when no zeroed lookup is supplied, and the cache fingerprint.
/// </summary>
public class CoordinateKeyVertexTests
{
    private static NamedKeyVertex CoordKv(string name, float x, float y, float z, int hint, string shape = "S") => new()
    {
        Name = name,
        ShapeName = shape,
        Strategy = KeyVertexStrategy.Coordinate,
        CoordX = x,
        CoordY = y,
        CoordZ = z,
        VertexIndex = hint,
    };

    // ---- MatchNearestVertex (the matcher behind the strategy) ----

    [Fact]
    public void MatchNearestVertex_ValidHint_ReturnsHintImmediately()
    {
        var positions = new[] { new Vector3(0f, 0f, 0f), new Vector3(10f, 0f, 0f), new Vector3(0f, 10f, 0f) };
        // Hint 1 still sits exactly on the stored position → returned without a global scan.
        RegionVolumeEvaluator.MatchNearestVertex(positions, new Vector3(10f, 0f, 0f), indexHint: 1).Should().Be(1);
    }

    [Fact]
    public void MatchNearestVertex_StaleHint_FallsBackToGlobalNearest()
    {
        // The stored position's vertex was renumbered from index 1 to index 2; the hint (1) now points
        // at unrelated anatomy and must be rejected in favor of the global nearest.
        var positions = new[] { new Vector3(0f, 0f, 0f), new Vector3(99f, 99f, 99f), new Vector3(10f, 0f, 0f) };
        RegionVolumeEvaluator.MatchNearestVertex(positions, new Vector3(10f, 0f, 0f), indexHint: 1).Should().Be(2);
    }

    [Fact]
    public void MatchNearestVertex_NoHint_ReturnsGlobalNearest()
    {
        var positions = new[] { new Vector3(0f, 0f, 0f), new Vector3(10f, 0f, 0f), new Vector3(0f, 10f, 0f) };
        RegionVolumeEvaluator.MatchNearestVertex(positions, new Vector3(9.7f, 0.2f, 0f), indexHint: -1).Should().Be(1);
    }

    [Fact]
    public void MatchNearestVertex_VariantBodyBeyondEpsilon_StillMatchesCorrespondingVertex()
    {
        // A body-type variant shifted the corresponding vertex well past any matching epsilon, but it is
        // still the closest — the no-cap global nearest tracks it (a region vertex-edit eps would miss).
        var variant = new[] { new Vector3(0f, 0f, 0f), new Vector3(12f, 1f, 0f), new Vector3(0f, 10f, 0f) };
        RegionVolumeEvaluator.MatchNearestVertex(variant, new Vector3(10f, 0f, 0f), indexHint: 1).Should().Be(1);
    }

    [Fact]
    public void MatchNearestVertex_EmptyPositions_ReturnsMinusOne()
    {
        RegionVolumeEvaluator.MatchNearestVertex(System.Array.Empty<Vector3>(), new Vector3(0f, 0f, 0f)).Should().Be(-1);
    }

    // ---- End-to-end through MeasurementMath.TryEvaluate ----

    [Fact]
    public void TryEvaluate_Coordinate_ResolvesZeroedThenReadsDeformedPosition()
    {
        var kvA = CoordKv("A", 0f, 0f, 0f, hint: 0);
        var kvB = CoordKv("B", 10f, 0f, 0f, hint: 1);
        var map = new Dictionary<string, NamedKeyVertex> { ["A"] = kvA, ["B"] = kvB };

        var zeroed = new[] { new Vector3(0f, 0f, 0f), new Vector3(10f, 0f, 0f), new Vector3(0f, 10f, 0f) };
        // Deformed (the preset being classified): B's vertex moved out to X=14.
        var deformed = new[] { new Vector3(0f, 0f, 0f), new Vector3(14f, 0f, 0f), new Vector3(0f, 10f, 0f) };

        var def = new MeasurementDefinition
        {
            Name = "width",
            Kind = MeasurementKind.PointDistance,
            VertexRefNames = new List<string> { "A", "B" },
        };

        MeasurementMath.VertexLookup lookup = (shape, idx) => (idx >= 0 && idx < deformed.Length) ? deformed[idx] : (Vector3?)null;
        MeasurementMath.ShapePositionsLookup zeroedLookup = shape => zeroed;

        MeasurementMath.TryEvaluate(def, map, lookup, null, null, null, zeroedLookup, out float v).Should().BeTrue();
        v.Should().BeApproximately(14f, 1e-4f);
    }

    [Fact]
    public void TryEvaluate_Coordinate_RenumberedVariant_StillMeasuresSameAnatomy()
    {
        // The variant renumbers the two anchored vertices and shifts one. Both stored hints are stale.
        // Original anatomy: P0=(0,0,0), P1=(10,0,0).
        var kvA = CoordKv("A", 0f, 0f, 0f, hint: 0);   // P0 — now at variant index 2
        var kvB = CoordKv("B", 10f, 0f, 0f, hint: 1);  // P1 — now at variant index 0
        var map = new Dictionary<string, NamedKeyVertex> { ["A"] = kvA, ["B"] = kvB };

        var zeroedVariant = new[] { new Vector3(10f, 0f, 0f), new Vector3(0f, 10f, 0f), new Vector3(0f, 0f, 0f) };
        var deformedVariant = new[] { new Vector3(16f, 0f, 0f), new Vector3(0f, 10f, 0f), new Vector3(0f, 0f, 0f) };

        var def = new MeasurementDefinition
        {
            Name = "width",
            Kind = MeasurementKind.PointDistance,
            VertexRefNames = new List<string> { "A", "B" },
        };

        MeasurementMath.VertexLookup lookup = (shape, idx) => (idx >= 0 && idx < deformedVariant.Length) ? deformedVariant[idx] : (Vector3?)null;
        MeasurementMath.ShapePositionsLookup zeroedLookup = shape => zeroedVariant;

        MeasurementMath.TryEvaluate(def, map, lookup, null, null, null, zeroedLookup, out float v).Should().BeTrue();
        // A resolves to variant idx 2 (deformed (0,0,0)); B resolves to variant idx 0 (deformed (16,0,0)).
        v.Should().BeApproximately(16f, 1e-4f);
        kvA.VertexIndex.Should().Be(2, "the resolved index is re-cached after matching the renumbered mesh");
        kvB.VertexIndex.Should().Be(0);
    }

    [Fact]
    public void TryEvaluate_Coordinate_NoZeroedLookup_DegradesToCachedVertexIndex()
    {
        // Without a zeroed lookup (older overloads / headless callers) a Coordinate row falls back to its
        // cached VertexIndex and behaves exactly like an Explicit row.
        var kvA = CoordKv("A", 999f, 999f, 999f, hint: 0); // bogus coord — proves it isn't consulted here
        var kvB = CoordKv("B", 999f, 999f, 999f, hint: 1);
        var map = new Dictionary<string, NamedKeyVertex> { ["A"] = kvA, ["B"] = kvB };

        var deformed = new[] { new Vector3(0f, 0f, 0f), new Vector3(14f, 0f, 0f) };

        var def = new MeasurementDefinition
        {
            Name = "width",
            Kind = MeasurementKind.PointDistance,
            VertexRefNames = new List<string> { "A", "B" },
        };

        MeasurementMath.VertexLookup lookup = (shape, idx) => (idx >= 0 && idx < deformed.Length) ? deformed[idx] : (Vector3?)null;

        MeasurementMath.TryEvaluate(def, map, lookup, null, null, null, zeroedShapeLookup: null, out float v).Should().BeTrue();
        v.Should().BeApproximately(14f, 1e-4f);
    }

    // ---- Cache fingerprint ----

    [Fact]
    public void Fingerprint_CoordinateKeyVertex_ChangesWithCoord_NotWithVertexIndex()
    {
        var kvA = CoordKv("A", 1f, 0f, 0f, hint: 5);
        var kvB = CoordKv("B", 2f, 0f, 0f, hint: 6);
        var keyVerts = new Dictionary<string, NamedKeyVertex> { ["A"] = kvA, ["B"] = kvB };
        var noRegions = new Dictionary<string, NamedRegion>();

        var def = new MeasurementDefinition
        {
            Name = "width",
            Kind = MeasurementKind.PointDistance,
            VertexRefNames = new List<string> { "A", "B" },
        };

        var fp1 = MeasurementCacheStore.ComputeMeasurementFingerprint(def, keyVerts, noRegions);

        // The resolved VertexIndex is a session cache, not identity — changing it must NOT invalidate.
        kvA.VertexIndex = 4242;
        var fpAfterIndexChange = MeasurementCacheStore.ComputeMeasurementFingerprint(def, keyVerts, noRegions);
        fpAfterIndexChange.Should().Be(fp1, "the resolved VertexIndex is excluded from a Coordinate row's fingerprint");

        // The stored zeroed position IS the identity — changing it must invalidate.
        kvA.CoordX = 9f;
        var fpAfterCoordChange = MeasurementCacheStore.ComputeMeasurementFingerprint(def, keyVerts, noRegions);
        fpAfterCoordChange.Should().NotBe(fp1, "the stored coordinate defines which vertex a Coordinate row resolves to");
    }
}
