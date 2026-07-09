using System.Collections.Generic;
using System.Numerics;
using CharacterViewer.Rendering;
using FluentAssertions;
using Xunit;

namespace SynthEBD.Tests;

/// <summary>
/// Unit tests for <see cref="SliderHeatmapBuilder"/> — the pure geometry/color core of the
/// Label-by-Sliders vertex highlight. Exercises the threshold filter, triangle-inclusion rule,
/// interleaved 9-float layout, magnitude ramp, and the color assigned to each corner, all without
/// touching the OpenGL stack.
/// </summary>
public class SliderHeatmapBuilderTests
{
    private const int Stride = 9; // pos(3) + normal(3) + color(3)

    // A single triangle (0,1,2) laid out in the XY plane so a flat +Z normal is expected.
    private static readonly List<Vector3> OneTriPositions = new()
    {
        new Vector3(0f, 0f, 0f),
        new Vector3(1f, 0f, 0f),
        new Vector3(0f, 1f, 0f),
    };
    private static readonly List<int> OneTriIndices = new() { 0, 1, 2 };

    private static List<float> RunBuild(
        IReadOnlyList<Vector3> pos, IReadOnlyList<int> idx,
        IReadOnlyDictionary<ushort, Vector3> deltas, out int tris,
        float minDelta = SliderHeatmapBuilder.DefaultMinDelta)
    {
        var output = new List<float>();
        tris = SliderHeatmapBuilder.Build(pos, idx, deltas, output, minDelta);
        return output;
    }

    [Fact]
    public void EmptyDeltas_EmitsNothing()
    {
        var output = RunBuild(OneTriPositions, OneTriIndices, new Dictionary<ushort, Vector3>(), out int tris);
        tris.Should().Be(0);
        output.Should().BeEmpty();
    }

    [Fact]
    public void AllDeltasBelowThreshold_EmitsNothing()
    {
        // Magnitude 1e-6 is well under the 1e-4 default threshold, so nothing counts as moved.
        var deltas = new Dictionary<ushort, Vector3> { [0] = new Vector3(1e-6f, 0f, 0f) };
        var output = RunBuild(OneTriPositions, OneTriIndices, deltas, out int tris);
        tris.Should().Be(0);
        output.Should().BeEmpty();
    }

    [Fact]
    public void OneAffectedCorner_EmitsWholeTriangle()
    {
        var deltas = new Dictionary<ushort, Vector3> { [0] = new Vector3(0.5f, 0f, 0f) };
        var output = RunBuild(OneTriPositions, OneTriIndices, deltas, out int tris);

        tris.Should().Be(1);
        output.Count.Should().Be(3 * Stride); // one triangle, three 9-float verts
    }

    [Fact]
    public void EmittedVertices_CarryInputPositionsAndFlatNormal()
    {
        var deltas = new Dictionary<ushort, Vector3> { [1] = new Vector3(0f, 0.3f, 0f) };
        var output = RunBuild(OneTriPositions, OneTriIndices, deltas, out _);

        // Corner 0's position is the first three floats.
        output[0].Should().Be(0f);
        output[1].Should().Be(0f);
        output[2].Should().Be(0f);

        // The triangle lies in the XY plane wound CCW, so the flat normal is +Z on every corner.
        for (int v = 0; v < 3; v++)
        {
            output[v * Stride + 3].Should().BeApproximately(0f, 1e-5f);
            output[v * Stride + 4].Should().BeApproximately(0f, 1e-5f);
            output[v * Stride + 5].Should().BeApproximately(1f, 1e-5f);
        }
    }

    [Fact]
    public void MaxMagnitudeCorner_GetsHotColor_UnaffectedCorner_GetsCold()
    {
        // Corner 1 moves most (hot red), corner 2 a little, corner 0 not at all (cold blue).
        var deltas = new Dictionary<ushort, Vector3>
        {
            [1] = new Vector3(1.0f, 0f, 0f),
            [2] = new Vector3(0.2f, 0f, 0f),
        };
        var output = RunBuild(OneTriPositions, OneTriIndices, deltas, out int tris);
        tris.Should().Be(1);

        Vector3 ColorOf(int corner) => new(
            output[corner * Stride + 6], output[corner * Stride + 7], output[corner * Stride + 8]);

        // Corner 1 is the slider's max -> the hot end of the ramp (red dominant).
        ColorOf(1).Should().Be(SliderHeatmapBuilder.Ramp(1f));
        // Corner 0 is unaffected -> the cold end (blue dominant).
        ColorOf(0).Should().Be(SliderHeatmapBuilder.Ramp(0f));
        // Corner 2 sits between: normalized magnitude 0.2 / 1.0.
        ColorOf(2).Should().Be(SliderHeatmapBuilder.Ramp(0.2f));
    }

    [Fact]
    public void OutOfRangeDeltaIndices_AreSkipped()
    {
        // Index 99 has no vertex; index 0 does. Only the in-range one drives inclusion.
        var deltas = new Dictionary<ushort, Vector3>
        {
            [99] = new Vector3(5f, 0f, 0f),
            [0] = new Vector3(0.5f, 0f, 0f),
        };
        var output = RunBuild(OneTriPositions, OneTriIndices, deltas, out int tris);
        tris.Should().Be(1);

        // Corner 0 is the only affected vertex, so it is the max -> hot end, not scaled by the
        // out-of-range index's larger magnitude.
        Vector3 corner0 = new(output[6], output[7], output[8]);
        corner0.Should().Be(SliderHeatmapBuilder.Ramp(1f));
    }

    [Fact]
    public void TriangleWithNoAffectedCorner_IsNotEmitted()
    {
        // Two triangles: (0,1,2) affected via corner 0; (3,4,5) untouched.
        var pos = new List<Vector3>
        {
            new(0,0,0), new(1,0,0), new(0,1,0),
            new(5,5,0), new(6,5,0), new(5,6,0),
        };
        var idx = new List<int> { 0, 1, 2, 3, 4, 5 };
        var deltas = new Dictionary<ushort, Vector3> { [0] = new Vector3(0.5f, 0f, 0f) };

        var output = RunBuild(pos, idx, deltas, out int tris);
        tris.Should().Be(1); // only the first triangle
        output.Count.Should().Be(3 * Stride);
    }

    [Theory]
    [InlineData(-1f)]  // clamps to cold
    [InlineData(0f)]
    public void Ramp_ColdEnd_IsBlueDominant(float t)
    {
        var c = SliderHeatmapBuilder.Ramp(t);
        c.Z.Should().BeGreaterThan(c.X); // blue > red
        c.Z.Should().BeGreaterThan(c.Y); // blue > green
    }

    [Theory]
    [InlineData(1f)]
    [InlineData(2f)]   // clamps to hot
    public void Ramp_HotEnd_IsRedDominant(float t)
    {
        var c = SliderHeatmapBuilder.Ramp(t);
        c.X.Should().BeGreaterThan(c.Y); // red > green
        c.X.Should().BeGreaterThan(c.Z); // red > blue
    }

    [Fact]
    public void Ramp_Midpoint_IsGreenDominant()
    {
        var c = SliderHeatmapBuilder.Ramp(0.5f);
        c.Y.Should().BeGreaterThan(c.X); // green > red
        c.Y.Should().BeGreaterThan(c.Z); // green > blue
    }
}
