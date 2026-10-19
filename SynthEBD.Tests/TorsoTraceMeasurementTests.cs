using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using SynthEBD;
using Xunit;
using Vector3 = OpenTK.Mathematics.Vector3;
using static SynthEBD.RegionVolumeEvaluator;
using Reason = SynthEBD.BodySlideMeasurementEvaluator.MeasurementFailureReason;

namespace SynthEBD.Tests;

/// <summary>
/// <see cref="MeasurementKind.TorsoTrace"/> wired through the evaluator, the model helpers and the cache fingerprint.
/// The geometry itself is covered by <see cref="TorsoTraceTests"/>; this uses its synthetic Spoon-stem torso.
/// </summary>
public class TorsoTraceMeasurementTests
{
    private const string Shape = "Body";

    /// <summary>The Spoon-stem torso with bust vertices hiding the side from Y 90 up and three Explicit sternum key
    /// vertices appended to the mesh. Returns the profile (three TorsoTrace rows), mesh source and baked bust region;
    /// <paramref name="indexCalls"/> counts how often the triangles are fetched.</summary>
    private static (BodyTypeProfile profile, MeasurementMeshSource mesh, Dictionary<string, ResolvedRegion> regions, Vector3[] pos, int[] tris)
        Setup(int[] indexCalls)
    {
        var (pos, tris) = TorsoTraceTests.Body(TorsoTraceTests.SpoonStem);
        var bust = TorsoTraceTests.AddBust(pos, 9.5f, 90f);
        var kvs = new List<NamedKeyVertex>();
        string[] names = { "LowerSternumFront", "MidSternumFront", "UpperSternumFront" };
        for (int i = 0; i < 3; i++)
        {
            kvs.Add(new NamedKeyVertex { Name = names[i], ShapeName = Shape, VertexIndex = pos.Count });
            pos.Add(TorsoTraceTests.Sternum[i]);
        }
        var p = pos.ToArray();
        var t = tris.ToArray();
        MeasurementDefinition Row(string name, TorsoTraceMetric metric) => new()
        {
            Name = name, Kind = MeasurementKind.TorsoTrace, RegionRefName = "L_Breast", TraceMetric = metric,
            VertexRefNames = names.ToList(),
        };
        var profile = new BodyTypeProfile
        {
            KeyVertices = kvs,
            Measurements = new List<MeasurementDefinition>
            {
                Row("stem_window_flare", TorsoTraceMetric.StemWindowFlare),
                Row("stem_window_length", TorsoTraceMetric.StemWindowLength),
                Row("lat_flare", TorsoTraceMetric.LatFlare),
            },
        };
        var mesh = new MeasurementMeshSource
        {
            Vertex = (s, i) => s == Shape && i >= 0 && i < p.Length ? p[i] : null,
            Positions = s => s == Shape ? p : null,
            Bones = _ => (null, null),
            Zeroed = (s, _) => s == Shape ? p : null,
            Indices = s => { indexCalls[0]++; return s == Shape ? t : null; },
            ShapeVertexCounts = () => new Dictionary<string, int> { [Shape] = p.Length },
        };
        var regions = new Dictionary<string, ResolvedRegion>
        {
            ["L_Breast"] = new ResolvedRegion { ShapeName = Shape, MemberVertexIndices = bust.ToArray() },
        };
        return (profile, mesh, regions, p, t);
    }

    [Fact]
    public void Evaluate_ReadsAllThreeMetrics_AndCutsTheMeshOnce()
    {
        var calls = new int[1];
        var (profile, mesh, regions, pos, tris) = Setup(calls);
        var r = BodySlideMeasurementEvaluator.Evaluate(mesh, profile, skipRules: true, resolvedRegions: regions);

        r.FailedMeasurements.Should().BeEmpty();
        var trace = TorsoTraceEvaluator.BuildTrace(pos, tris)!;
        TorsoTraceEvaluator.TryComputeStemWindow(trace, pos, regions["L_Breast"].MemberVertexIndices, TorsoTraceTests.Sternum, out var w);
        TorsoTraceEvaluator.TryComputeLatFlare(trace, out float lat);
        r.Measurements["stem_window_flare"].Should().Be(w.Flare);
        r.Measurements["stem_window_length"].Should().Be(w.Length);
        r.Measurements["lat_flare"].Should().Be(lat);
        w.Length.Should().BeApproximately(90f - 81.75f, 1e-3f);
        calls[0].Should().Be(1, "the three rows share one trace");
    }

    [Fact]
    public void MissingRegion_IsReported()
    {
        var (profile, mesh, _, _, _) = Setup(new int[1]);
        var r = BodySlideMeasurementEvaluator.Evaluate(mesh, profile, skipRules: true,
            resolvedRegions: new Dictionary<string, ResolvedRegion>());
        r.FailedMeasurements["stem_window_flare"].Should().Be(Reason.MissingRegion);
        r.FailedMeasurements["lat_flare"].Should().Be(Reason.MissingRegion);
    }

    [Fact]
    public void UnknownSternumRef_FailsTheStemMetrics_ButNotLatFlare()
    {
        var (profile, mesh, regions, _, _) = Setup(new int[1]);
        foreach (var m in profile.Measurements) m.VertexRefNames[1] = "Nope";
        var r = BodySlideMeasurementEvaluator.Evaluate(mesh, profile, skipRules: true, resolvedRegions: regions);
        r.FailedMeasurements["stem_window_flare"].Should().Be(Reason.MissingKeyVertex);
        r.FailedMeasurements["stem_window_length"].Should().Be(Reason.MissingKeyVertex);
        r.Measurements.Should().ContainKey("lat_flare");
    }

    [Fact]
    public void TooFewSternumRefs_IsMalformed()
    {
        var (profile, mesh, regions, _, _) = Setup(new int[1]);
        profile.Measurements[0].VertexRefNames = new List<string> { "LowerSternumFront", "MidSternumFront" };
        var r = BodySlideMeasurementEvaluator.Evaluate(mesh, profile, skipRules: true, resolvedRegions: regions);
        r.FailedMeasurements["stem_window_flare"].Should().Be(Reason.MalformedDefinition);
    }

    [Fact]
    public void NoTriangles_DoesNotResolve()
    {
        var calls = new int[1];
        var (profile, mesh, regions, _, _) = Setup(calls);
        var noTris = new MeasurementMeshSource
        {
            Vertex = mesh.Vertex, Positions = mesh.Positions, Bones = mesh.Bones, Zeroed = mesh.Zeroed,
            Indices = _ => null, ShapeVertexCounts = mesh.ShapeVertexCounts,
        };
        var r = BodySlideMeasurementEvaluator.Evaluate(noTris, profile, skipRules: true, resolvedRegions: regions);
        r.FailedMeasurements["lat_flare"].Should().Be(Reason.RegionNotResolved);
    }

    [Fact]
    public void ModelHelpers_TreatTorsoTraceAsARegionKindWithThreeRefs()
    {
        MeasurementMath.ReadsRegion(MeasurementKind.TorsoTrace).Should().BeTrue();
        MeasurementMath.VertexRefCount(MeasurementKind.TorsoTrace).Should().Be(3);
        var def = new MeasurementDefinition { Name = "t", Kind = MeasurementKind.TorsoTrace, RegionRefName = "L_Breast" };
        MeasurementMath.TryEvaluate(def, new Dictionary<string, NamedKeyVertex>(), (_, _) => null, null, null, null, out _)
            .Should().BeFalse("TorsoTrace goes through the evaluator's own path");
    }

    [Fact]
    public void Fingerprint_CoversMetricAndSternumRefs_AndLeavesOtherKindsUnchanged()
    {
        var regions = new Dictionary<string, NamedRegion> { ["L_Breast"] = new NamedRegion { Name = "L_Breast", ShapeName = Shape } };
        var kvs = new Dictionary<string, NamedKeyVertex>
        {
            ["A"] = new NamedKeyVertex { Name = "A", ShapeName = Shape, VertexIndex = 1 },
            ["B"] = new NamedKeyVertex { Name = "B", ShapeName = Shape, VertexIndex = 2 },
            ["C"] = new NamedKeyVertex { Name = "C", ShapeName = Shape, VertexIndex = 3 },
            ["D"] = new NamedKeyVertex { Name = "D", ShapeName = Shape, VertexIndex = 4 },
        };
        string Fp(MeasurementDefinition d) => MeasurementCacheStore.ComputeMeasurementFingerprint(d, kvs, regions, null);
        MeasurementDefinition Tt(TorsoTraceMetric m, params string[] refs) => new()
        {
            Name = "t", Kind = MeasurementKind.TorsoTrace, RegionRefName = "L_Breast", TraceMetric = m, VertexRefNames = refs.ToList(),
        };

        Fp(Tt(TorsoTraceMetric.StemWindowFlare, "A", "B", "C")).Should().NotBe(Fp(Tt(TorsoTraceMetric.StemWindowLength, "A", "B", "C")));
        Fp(Tt(TorsoTraceMetric.StemWindowFlare, "A", "B", "C")).Should().NotBe(Fp(Tt(TorsoTraceMetric.StemWindowFlare, "A", "B", "D")));
        Fp(Tt(TorsoTraceMetric.LatFlare, "A", "B", "C")).Should().Be(Fp(Tt(TorsoTraceMetric.LatFlare, "A", "B", "D")),
            "lat flare reads no sternum points");

        // Girth and RegionVolume over the same region ignore the new field.
        var girth = new MeasurementDefinition { Name = "g", Kind = MeasurementKind.Girth, RegionRefName = "L_Breast" };
        var girthEdited = new MeasurementDefinition { Name = "g", Kind = MeasurementKind.Girth, RegionRefName = "L_Breast", TraceMetric = TorsoTraceMetric.LatFlare };
        Fp(girth).Should().Be(Fp(girthEdited));
    }
}
