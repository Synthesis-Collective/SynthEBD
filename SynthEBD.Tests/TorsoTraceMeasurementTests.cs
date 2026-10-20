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

    /// <summary>The Spoon-stem torso with bust vertices hiding the side from Y 90 up and four Explicit key vertices
    /// (three sternum points, then the widest chest point) appended to the mesh. Returns the profile (three TorsoTrace rows), mesh source and baked bust region;
    /// <paramref name="indexCalls"/> counts how often the triangles are fetched.</summary>
    private static (BodyTypeProfile profile, MeasurementMeshSource mesh, Dictionary<string, ResolvedRegion> regions, Vector3[] pos, int[] tris)
        Setup(int[] indexCalls)
    {
        var (pos, tris) = TorsoTraceTests.Body(TorsoTraceTests.SpoonStem);
        var bust = TorsoTraceTests.AddBust(pos, 9.5f, 90f);
        var kvs = new List<NamedKeyVertex>();
        string[] names = { "LowerSternumFront", "MidSternumFront", "UpperSternumFront", "L_ChestSide" };
        for (int i = 0; i < 4; i++)
        {
            kvs.Add(new NamedKeyVertex { Name = names[i], ShapeName = Shape, VertexIndex = pos.Count });
            pos.Add(i < 3 ? TorsoTraceTests.Sternum[i] : TorsoTraceTests.Chest);
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
        TorsoTraceEvaluator.TryComputeStemWindow(trace, pos, regions["L_Breast"].MemberVertexIndices, TorsoTraceTests.Sternum, TorsoTraceTests.Chest.Y, out var w);
        TorsoTraceEvaluator.TryComputeLatFlare(trace, out float lat);
        r.Measurements["stem_window_flare"].Should().Be(w.Flare);
        r.Measurements["stem_window_length"].Should().Be(w.Length);
        r.Measurements["lat_flare"].Should().Be(lat);
        w.Length.Should().BeApproximately(90f - 81.75f, 1e-3f);
        calls[0].Should().Be(1, "the three rows share one trace");
    }

    [Fact]
    public void BustCoversNothing_WindowTopIsTheChestKeyVertexHeight()
    {
        var (profile, mesh, regions, _, _) = Setup(new int[1]);
        regions["L_Breast"] = new ResolvedRegion { ShapeName = Shape, MemberVertexIndices = Array.Empty<int>() };
        var r = BodySlideMeasurementEvaluator.Evaluate(mesh, profile, skipRules: true, resolvedRegions: regions);
        r.Measurements["stem_window_length"].Should().BeApproximately(TorsoTraceTests.Chest.Y - 81.75f, 1e-3f);
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
    public void TooFewRefs_IsMalformed()
    {
        var (profile, mesh, regions, _, _) = Setup(new int[1]);
        profile.Measurements[0].VertexRefNames = new List<string> { "LowerSternumFront", "MidSternumFront", "UpperSternumFront" };
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
    public void ModelHelpers_TreatTorsoTraceAsARegionKindWithFourRefs()
    {
        MeasurementMath.ReadsRegion(MeasurementKind.TorsoTrace).Should().BeTrue();
        MeasurementMath.VertexRefCount(MeasurementKind.TorsoTrace).Should().Be(4);
        var def = new MeasurementDefinition { Name = "t", Kind = MeasurementKind.TorsoTrace, RegionRefName = "L_Breast" };
        MeasurementMath.TryEvaluate(def, new Dictionary<string, NamedKeyVertex>(), (_, _) => null, null, null, null, out _)
            .Should().BeFalse("TorsoTrace goes through the evaluator's own path");
    }

    [Fact]
    public void Fingerprint_CoversMetricAndKeyVertexRefs_AndLeavesOtherKindsUnchanged()
    {
        var regions = new Dictionary<string, NamedRegion> { ["L_Breast"] = new NamedRegion { Name = "L_Breast", ShapeName = Shape } };
        var kvs = new Dictionary<string, NamedKeyVertex>
        {
            ["A"] = new NamedKeyVertex { Name = "A", ShapeName = Shape, VertexIndex = 1 },
            ["B"] = new NamedKeyVertex { Name = "B", ShapeName = Shape, VertexIndex = 2 },
            ["C"] = new NamedKeyVertex { Name = "C", ShapeName = Shape, VertexIndex = 3 },
            ["D"] = new NamedKeyVertex { Name = "D", ShapeName = Shape, VertexIndex = 4 },
            ["E"] = new NamedKeyVertex { Name = "E", ShapeName = Shape, VertexIndex = 5 },
        };
        string Fp(MeasurementDefinition d) => MeasurementCacheStore.ComputeMeasurementFingerprint(d, kvs, regions, null);
        MeasurementDefinition Tt(TorsoTraceMetric m, params string[] refs) => new()
        {
            Name = "t", Kind = MeasurementKind.TorsoTrace, RegionRefName = "L_Breast", TraceMetric = m, VertexRefNames = refs.ToList(),
        };

        Fp(Tt(TorsoTraceMetric.StemWindowFlare, "A", "B", "C", "D")).Should().NotBe(Fp(Tt(TorsoTraceMetric.StemWindowLength, "A", "B", "C", "D")));
        Fp(Tt(TorsoTraceMetric.StemWindowFlare, "A", "B", "C", "D")).Should().NotBe(Fp(Tt(TorsoTraceMetric.StemWindowFlare, "A", "B", "E", "D")));
        Fp(Tt(TorsoTraceMetric.StemWindowFlare, "A", "B", "C", "D")).Should().NotBe(Fp(Tt(TorsoTraceMetric.StemWindowFlare, "A", "B", "C", "E")),
            "the widest-chest point sets the window top when the bust covers nothing");
        Fp(Tt(TorsoTraceMetric.LatFlare, "A", "B", "C", "D")).Should().Be(Fp(Tt(TorsoTraceMetric.LatFlare, "A", "B", "E", "E")),
            "lat flare reads no key vertices");

        // Girth and RegionVolume over the same region ignore the new field.
        var girth = new MeasurementDefinition { Name = "g", Kind = MeasurementKind.Girth, RegionRefName = "L_Breast" };
        var girthEdited = new MeasurementDefinition { Name = "g", Kind = MeasurementKind.Girth, RegionRefName = "L_Breast", TraceMetric = TorsoTraceMetric.LatFlare };
        Fp(girth).Should().Be(Fp(girthEdited));
    }

    // ---------- editor row (VM_MeasurementDefinition, constructed with a null parent like RegionVolumeUiVmTests) ----------

    private static MeasurementDefinition TraceRow(TorsoTraceMetric metric) => new()
    {
        Name = "stem_window_flare", Kind = MeasurementKind.TorsoTrace, RegionRefName = "L_Breast", TraceMetric = metric,
        VertexRefNames = new List<string> { "LowerSternumFront", "MidSternumFront", "UpperSternumFront", "L_ChestSide" },
    };

    [Fact]
    public void EditorRow_RoundTripsMetricRegionAndFourRefs()
    {
        var dumped = new VM_MeasurementDefinition(TraceRow(TorsoTraceMetric.LatFlare), null!).DumpToModel();
        dumped.Kind.Should().Be(MeasurementKind.TorsoTrace);
        dumped.TraceMetric.Should().Be(TorsoTraceMetric.LatFlare);
        dumped.RegionRefName.Should().Be("L_Breast");
        dumped.VertexRefNames.Should().Equal("LowerSternumFront", "MidSternumFront", "UpperSternumFront", "L_ChestSide");
    }

    [Fact]
    public void EditorRow_ShowsRegionRefsAndMetric_NotTheDistanceAxis()
    {
        var vm = new VM_MeasurementDefinition(TraceRow(TorsoTraceMetric.StemWindowFlare), null!);
        vm.ShowRegionField.Should().BeTrue();
        vm.ShowVertexRefFields.Should().BeTrue();
        vm.ShowRefC.Should().BeTrue();
        vm.ShowRefD.Should().BeTrue("ref D is the widest-chest point");
        vm.ShowSecondPair.Should().BeFalse("the ratio axes stay off");
        vm.ShowTraceMetricField.Should().BeTrue();
        vm.ShowDistanceAxisPicker.Should().BeFalse();
    }

    [Fact]
    public void EditorRow_OtherKinds_DropTheMetricAndTheExtraRefs()
    {
        var vm = new VM_MeasurementDefinition(TraceRow(TorsoTraceMetric.LatFlare), null!) { Kind = MeasurementKind.AxisDistance };
        var dumped = vm.DumpToModel();
        dumped.TraceMetric.Should().Be(TorsoTraceMetric.StemWindowFlare);
        dumped.VertexRefNames.Should().HaveCount(2);
        dumped.RegionRefName.Should().BeEmpty();
        vm.ShowRefC.Should().BeFalse();
        vm.ShowRefD.Should().BeFalse();
        vm.ShowTraceMetricField.Should().BeFalse();
    }
}
