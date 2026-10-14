using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using CharacterViewer.Rendering;
using FluentAssertions;
using Xunit;

namespace SynthEBD.Tests;

/// <summary>
/// <see cref="HeadlessBodyDeformer"/> must reproduce the viewer's per-shape pipeline -- weight lerp of the _0/_1
/// bases, BodySlide slider deltas (<see cref="BodySlideDeformer.ApplyDeformationFromTri"/>), then
/// <see cref="NifMeshBuilder.ApplySkinning"/> -- from pre-skinned tables. Uses a random mesh skinned to bones with
/// rotation, non-unit scale and translation, partial and zero bone weights, and a .tri with sparse morphs.
/// </summary>
public class HeadlessBodyDeformerTests
{
    private const int N = 400;
    private const string Shape = "3BA";

    private static Vector3 R(Random rng, float s) =>
        new((float)(rng.NextDouble() * 2 - 1) * s, (float)(rng.NextDouble() * 2 - 1) * s, (float)(rng.NextDouble() * 2 - 1) * s);

    private static NifMeshBuilder.CachedSkinTransform Bone(Random rng)
    {
        var axis = Vector3.Normalize(R(rng, 1) + new Vector3(0.01f, 0, 0));
        var m = Matrix4x4.CreateFromAxisAngle(axis, (float)(rng.NextDouble() * Math.PI));
        var t = R(rng, 5);
        return new NifMeshBuilder.CachedSkinTransform
        {
            R00 = m.M11, R01 = m.M21, R02 = m.M31,
            R10 = m.M12, R11 = m.M22, R12 = m.M32,
            R20 = m.M13, R21 = m.M23, R22 = m.M33,
            Tx = t.X, Ty = t.Y, Tz = t.Z,
            Scale = 0.8f + (float)rng.NextDouble() * 0.5f,
        };
    }

    private static (NifMeshBuilder.BuiltMesh Mesh, BodyTriFile Tri) Fixture(bool skinned, int seed = 7)
    {
        var rng = new Random(seed);
        var w0 = Enumerable.Range(0, N).Select(_ => R(rng, 20)).ToArray();
        var w1 = w0.Select(p => p + R(rng, 2)).ToArray();
        NifMeshBuilder.SkinningInfo? skin = null;
        if (skinned)
        {
            var bones = Enumerable.Range(0, 6).Select(_ => Bone(rng)).ToArray();
            var idx = new int[N * 4];
            var wts = new float[N * 4];
            for (int v = 0; v < N; v++)
            {
                if (v % 50 == 0) continue; // unweighted vertex: ApplySkinning leaves it unchanged
                int count = 1 + v % 4;
                for (int k = 0; k < count; k++)
                {
                    idx[v * 4 + k] = rng.Next(bones.Length);
                    wts[v * 4 + k] = 0.1f + (float)rng.NextDouble(); // deliberately not normalised
                }
            }
            skin = new NifMeshBuilder.SkinningInfo { BoneTransforms = bones, VertBoneIndices = idx, VertBoneWeights = wts, VertexCount = N };
        }
        var mesh = new NifMeshBuilder.BuiltMesh
        {
            Positions = w0.ToArray(), Normals = new Vector3[N], Indices = Array.Empty<int>(),
            TextureCoordinates = new Vector2[N], Tangents = new Vector3[N], Bitangents = new Vector3[N],
            ShapeName = Shape, BindPosePositions = w0.ToArray(),
            Weight0BindPosePositions = w0, Weight1BindPosePositions = w1, Skinning = skin,
        };
        var morphs = new List<BodyTriMorph>();
        for (int s = 0; s < 8; s++)
        {
            var deltas = new Dictionary<ushort, Vector3>();
            for (int k = 0; k < 60; k++) deltas[(ushort)rng.Next(N)] = R(rng, 3);
            morphs.Add(new BodyTriMorph { Name = "Slider" + s, VertexDeltas = deltas });
        }
        morphs.Add(new BodyTriMorph { Name = "TooFar", VertexDeltas = new() { [5] = Vector3.One, [(ushort)(N + 3)] = Vector3.One } });
        var tri = new BodyTriFile { FilePath = "test.tri", Shapes = new() { new BodyTriShape { ShapeName = Shape, Morphs = morphs } } };
        return (mesh, tri);
    }

    /// <summary>The viewer's DeformShape positions: lerp, slider deltas, skinning.</summary>
    private static Vector3[] Reference(NifMeshBuilder.BuiltMesh mesh, BodyTriFile tri, MorphSet morphs, int weight)
    {
        float t = weight / 100f;
        var p = mesh.Weight0BindPosePositions!.Select((a, i) => Vector3.Lerp(a, mesh.Weight1BindPosePositions![i], t)).ToArray();
        new BodySlideDeformer(null!, null!).ApplyDeformationFromTri(p, morphs, weight, tri, Shape);
        if (mesh.Skinning != null)
        {
            var normals = new Vector3[N];
            NifMeshBuilder.ApplySkinning(p, normals, mesh.Skinning, p, normals);
        }
        return p;
    }

    private static MorphSet Morphs(params (string Name, float Big, float Small)[] sliders) =>
        new() { Label = "test", Sliders = sliders.ToDictionary(s => s.Name, s => new MorphSlider(s.Big, s.Small)) };

    private static void ShouldMatch(OpenTK.Mathematics.Vector3[] actual, Vector3[] expected)
    {
        actual.Should().HaveCount(expected.Length);
        float worst = 0;
        for (int i = 0; i < expected.Length; i++)
        {
            worst = Math.Max(worst, Math.Abs(actual[i].X - expected[i].X));
            worst = Math.Max(worst, Math.Abs(actual[i].Y - expected[i].Y));
            worst = Math.Max(worst, Math.Abs(actual[i].Z - expected[i].Z));
        }
        worst.Should().BeLessThan(1e-3f, "positions are around 20-50 units, so this is float rounding only");
    }

    [Theory]
    [InlineData(true, 0)]
    [InlineData(true, 37)]
    [InlineData(true, 100)]
    [InlineData(false, 63)]
    public void MatchesViewerPipeline(bool skinned, int weight)
    {
        var (mesh, tri) = Fixture(skinned);
        var deformer = new HeadlessBodyDeformer(new[] { (Shape, mesh) }, tri);
        var morphs = Morphs(("Slider0", 100, 0), ("Slider1", 35, 80), ("Slider3", -40, 20), ("slider5", 0, 100),
            ("NotInTri", 100, 100), ("Slider6", 0, 0));

        ShouldMatch(deformer.Deform(Shape, morphs, weight)!, Reference(mesh, tri, morphs, weight));
    }

    [Fact]
    public void NoSliders_GivesSkinnedBase()
    {
        var (mesh, tri) = Fixture(skinned: true);
        var deformer = new HeadlessBodyDeformer(new[] { (Shape, mesh) }, tri);
        ShouldMatch(deformer.Deform(Shape, Morphs(), 50)!, Reference(mesh, tri, Morphs(), 50));
    }

    [Fact]
    public void OutOfRangeSlider_LeavesShapeAtBase_LikeTheViewer()
    {
        var (mesh, tri) = Fixture(skinned: true);
        var deformer = new HeadlessBodyDeformer(new[] { (Shape, mesh) }, tri);
        var morphs = Morphs(("Slider0", 100, 100), ("TooFar", 50, 50));

        var expected = Reference(mesh, tri, morphs, 40);
        ShouldMatch(deformer.Deform(Shape, morphs, 40)!, expected);
        ShouldMatch(deformer.Deform(Shape, Morphs(), 40)!, expected); // i.e. no deltas applied at all
    }

    [Fact]
    public void UnknownShape_ReturnsNull()
    {
        var (mesh, tri) = Fixture(skinned: true);
        new HeadlessBodyDeformer(new[] { (Shape, mesh) }, tri).Deform("Hands", Morphs(), 50).Should().BeNull();
    }

    [Fact]
    public void SkinOffset_KeepsBoneScale_UnlikeApplyRotation()
    {
        // Linearity check: skin(p + d) - skin(p) == SkinOffset(d) at every vertex.
        var (mesh, _) = Fixture(skinned: true);
        var skin = mesh.Skinning!;
        var rng = new Random(3);
        var p = mesh.BindPosePositions!.ToArray();
        var d = Enumerable.Range(0, N).Select(_ => R(rng, 2)).ToArray();
        var pd = p.Select((x, i) => x + d[i]).ToArray();
        var n = new Vector3[N];
        NifMeshBuilder.ApplySkinning(p, n, skin, p, n);
        NifMeshBuilder.ApplySkinning(pd, n, skin, pd, n);
        for (int i = 0; i < N; i++)
        {
            var expected = pd[i] - p[i];
            var actual = NifMeshBuilder.SkinOffset(skin, N, i, d[i]);
            (actual - expected).Length().Should().BeLessThan(1e-3f);
        }
    }
}
