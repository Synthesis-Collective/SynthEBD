using System;
using System.Collections.Generic;
using FluentAssertions;
using SynthEBD;
using Xunit;
using Vector3 = OpenTK.Mathematics.Vector3;

namespace SynthEBD.Tests;

/// <summary>
/// Phase-3 wiring tests: <see cref="RegionVolumeEvaluator.ResolveRegions"/> (batch resolve against
/// shape lookups, ShapeName stamping, missing-geometry handling) and
/// <see cref="BodySlideMeasurementEvaluator.TryEvaluateRegionVolume"/> (the evaluator branch that
/// turns a baked region + deformed positions into a measurement value, with its failure reasons).
/// These exercise the glue between the geometry core and the scan path without a live viewer.
/// </summary>
public class RegionVolumeWiringTests
{
    private const double Tol = 1e-4;

    /// <summary>Closed axis-aligned cube [0,size]^3, 8 verts / 12 triangles, outward winding.</summary>
    private static (Vector3[] pos, int[] tris) ClosedCube(float size)
    {
        var p = new[]
        {
            new Vector3(0,0,0),    new Vector3(size,0,0),    new Vector3(size,size,0),    new Vector3(0,size,0),
            new Vector3(0,0,size), new Vector3(size,0,size), new Vector3(size,size,size), new Vector3(0,size,size),
        };
        var t = new[]
        {
            1,2,6, 1,6,5,  0,4,7, 0,7,3,  3,7,6, 3,6,2,
            0,1,5, 0,5,4,  4,5,6, 4,6,7,  0,3,2, 0,2,1,
        };
        return (p, t);
    }

    private static NamedRegion CubeRegion(string name = "r", string shape = "Body") => new NamedRegion
    {
        Name = name,
        ShapeName = shape,
        BoxMinX = -1, BoxMinY = -1, BoxMinZ = -1,
        BoxMaxX = 3, BoxMaxY = 3, BoxMaxZ = 3,
    };

    // ---- ResolveRegions ----

    [Fact]
    public void ResolveRegions_ValidBox_ResolvesAndStampsShapeName()
    {
        var (p, t) = ClosedCube(2f);
        var map = RegionVolumeEvaluator.ResolveRegions(
            new[] { CubeRegion(shape: "CBBE 3BA") },
            shape => shape == "CBBE 3BA" ? p : null,
            shape => shape == "CBBE 3BA" ? t : null);

        map.Should().ContainKey("r");
        var r = map["r"];
        r.IsValid.Should().BeTrue(r.Diagnostic);
        r.ShapeName.Should().Be("CBBE 3BA");
        RegionVolumeEvaluator.ComputeVolume(r, p).Should().BeApproximately(8.0, Tol);
    }

    [Fact]
    public void ResolveRegions_MissingGeometry_YieldsInvalidWithDiagnostic()
    {
        var map = RegionVolumeEvaluator.ResolveRegions(
            new[] { CubeRegion(shape: "NotLoaded") },
            shape => null,
            shape => null);

        map.Should().ContainKey("r");
        map["r"].IsValid.Should().BeFalse();
        map["r"].Diagnostic.Should().Contain("no loaded geometry");
        map["r"].ShapeName.Should().Be("NotLoaded");
    }

    [Fact]
    public void ResolveRegions_FirstWinsOnDuplicateNames()
    {
        var (p, t) = ClosedCube(2f);
        var a = CubeRegion();
        var b = CubeRegion();
        b.ShapeName = "Second";
        var map = RegionVolumeEvaluator.ResolveRegions(
            new[] { a, b },
            shape => p, shape => t);

        map.Should().HaveCount(1);
        map["r"].ShapeName.Should().Be("Body"); // first row wins
    }

    [Fact]
    public void ResolveRegions_SkipsUnnamedRegions()
    {
        var (p, t) = ClosedCube(2f);
        var nameless = CubeRegion(name: "");
        var map = RegionVolumeEvaluator.ResolveRegions(new[] { nameless }, shape => p, shape => t);
        map.Should().BeEmpty();
    }

    // ---- TryEvaluateRegionVolume ----

    private static MeasurementDefinition VolDef(string region = "r") => new MeasurementDefinition
    {
        Name = "vol", Kind = MeasurementKind.RegionVolume, RegionRefName = region,
    };

    [Fact]
    public void TryEvaluateRegionVolume_ValidRegion_ReturnsVolume()
    {
        var (p, t) = ClosedCube(2f);
        var resolved = RegionVolumeEvaluator.ResolveRegions(new[] { CubeRegion() }, s => p, s => t);

        bool ok = BodySlideMeasurementEvaluator.TryEvaluateRegionVolume(
            VolDef(), resolved, shape => p, out float value, out var reason);

        ok.Should().BeTrue();
        value.Should().BeApproximately(8.0f, 1e-3f);
    }

    [Fact]
    public void TryEvaluateRegionVolume_EmptyRegionRef_FailsMalformed()
    {
        var def = VolDef(region: "");
        bool ok = BodySlideMeasurementEvaluator.TryEvaluateRegionVolume(
            def, new Dictionary<string, RegionVolumeEvaluator.ResolvedRegion>(), s => null,
            out _, out var reason);

        ok.Should().BeFalse();
        reason.Should().Be(BodySlideMeasurementEvaluator.MeasurementFailureReason.MalformedDefinition);
    }

    [Fact]
    public void TryEvaluateRegionVolume_NoResolvedRegion_FailsMissingRegion()
    {
        bool ok = BodySlideMeasurementEvaluator.TryEvaluateRegionVolume(
            VolDef(), null, s => null, out _, out var reason);

        ok.Should().BeFalse();
        reason.Should().Be(BodySlideMeasurementEvaluator.MeasurementFailureReason.MissingRegion);
    }

    [Fact]
    public void TryEvaluateRegionVolume_InvalidResolvedRegion_FailsRegionNotResolved()
    {
        var resolved = new Dictionary<string, RegionVolumeEvaluator.ResolvedRegion>
        {
            ["r"] = new RegionVolumeEvaluator.ResolvedRegion { IsValid = false, Diagnostic = "rejected", ShapeName = "Body" },
        };
        bool ok = BodySlideMeasurementEvaluator.TryEvaluateRegionVolume(
            VolDef(), resolved, s => new[] { new Vector3(0, 0, 0) }, out _, out var reason);

        ok.Should().BeFalse();
        reason.Should().Be(BodySlideMeasurementEvaluator.MeasurementFailureReason.RegionNotResolved);
    }

    [Fact]
    public void TryEvaluateRegionVolume_NoDeformedPositions_FailsRegionNotResolved()
    {
        var (p, t) = ClosedCube(2f);
        var resolved = RegionVolumeEvaluator.ResolveRegions(new[] { CubeRegion() }, s => p, s => t);

        // Region is valid, but the shape lookup returns no positions at eval time.
        bool ok = BodySlideMeasurementEvaluator.TryEvaluateRegionVolume(
            VolDef(), resolved, shape => null, out _, out var reason);

        ok.Should().BeFalse();
        reason.Should().Be(BodySlideMeasurementEvaluator.MeasurementFailureReason.RegionNotResolved);
    }

    [Fact]
    public void TryEvaluateRegionVolume_TracksDeformedPositions()
    {
        var (p, t) = ClosedCube(2f);
        var resolved = RegionVolumeEvaluator.ResolveRegions(new[] { CubeRegion() }, s => p, s => t);

        // Evaluate against a scaled "deformed" copy: volume scales with factor^3.
        var scaled = new Vector3[p.Length];
        for (int i = 0; i < p.Length; i++) scaled[i] = p[i] * 2f;

        BodySlideMeasurementEvaluator.TryEvaluateRegionVolume(
            VolDef(), resolved, shape => scaled, out float value, out _).Should().BeTrue();
        value.Should().BeApproximately(64.0f, 1e-2f);
    }
}
