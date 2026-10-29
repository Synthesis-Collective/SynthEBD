using System;
using FluentAssertions;
using OpenTK.Mathematics;
using Xunit;

namespace SkyRender.Tests;

/// <summary>Billboard turning (GlRenderer.BillboardTurn) and the renderer's profile defaults
/// (RenderProfile, SceneEnvironment.VanillaClearDay). Math and property values only; no GL context
/// is touched.</summary>
public class BillboardAndSceneDefaultsTests
{
    // The vanilla hearth's flame node in builder space (Y up): local X = +X, local Y = up (+Y),
    // local Z = +Z, at (10, 30, 0).
    private static readonly Matrix4 Frame = new(
        new Vector4(1f, 0f, 0f, 0f), new Vector4(0f, 1f, 0f, 0f), new Vector4(0f, 0f, 1f, 0f), new Vector4(10f, 30f, 0f, 1f));

    private static Vector3 Apply(Matrix4 m, Vector3 p) => Vector3.TransformPosition(p, m);

    [Fact]
    public void RotateAboutUp_TurnsLocalZTowardTheCamera_AboutTheOrigin_KeepingUp()
    {
        // Camera on the +X side of the node, level with it, looking back along -X.
        var eye = new Vector3(110f, 30f, 0f);
        var m = GlRenderer.BillboardTurn(Frame, 1, Matrix4.Identity, eye, Vector3.UnitY, Vector3.UnitX);
        // The origin stays put.
        (Apply(m, new Vector3(10f, 30f, 0f)) - new Vector3(10f, 30f, 0f)).Length.Should().BeLessThan(1e-4f);
        // A point on local +Z (in front of the sprite) now lies toward the camera (+X).
        var front = Apply(m, new Vector3(10f, 30f, 5f));
        (front - new Vector3(15f, 30f, 0f)).Length.Should().BeLessThan(1e-4f);
        // Up is unchanged.
        var top = Apply(m, new Vector3(10f, 40f, 0f));
        (top - new Vector3(10f, 40f, 0f)).Length.Should().BeLessThan(1e-4f);
    }

    [Fact]
    public void AlwaysFaceCamera_FacesAgainstTheViewDirection_RightHanded()
    {
        // Camera above and in front, looking down 45 degrees: back axis = (0, 0.707, 0.707).
        var back = Vector3.Normalize(new Vector3(0f, 1f, 1f));
        var up = Vector3.Normalize(new Vector3(0f, 1f, -1f));
        var m = GlRenderer.BillboardTurn(Frame, 0, Matrix4.Identity, new Vector3(10f, 130f, 100f), up, back);
        var o = new Vector3(10f, 30f, 0f);
        var z = Apply(m, o + Vector3.UnitZ) - o;
        var y = Apply(m, o + Vector3.UnitY) - o;
        var x = Apply(m, o + Vector3.UnitX) - o;
        (z - back).Length.Should().BeLessThan(1e-4f);
        Vector3.Dot(y, back).Should().BeApproximately(0f, 1e-4f);
        y.Y.Should().BeGreaterThan(0f);                       // kept upright
        (Vector3.Cross(y, z) - x).Length.Should().BeLessThan(1e-4f);   // not mirrored
    }

    [Fact]
    public void BsRotateAboutUp_TurnsAboutLocalZ_WithMinusYTowardTheCamera()
    {
        // A torch-glow node: local Z is world up (+Y), local Y lies along -Z.
        var k = new Matrix4(new Vector4(1f, 0f, 0f, 0f), new Vector4(0f, 0f, -1f, 0f),
                            new Vector4(0f, 1f, 0f, 0f), new Vector4(0f, 0f, 0f, 1f));
        var eye = new Vector3(100f, 0f, 0f);
        var m = GlRenderer.BillboardTurn(k, 5, Matrix4.Identity, eye, Vector3.UnitY, Vector3.UnitX);
        // Local -Y points at the camera (+X); local Z stays world up.
        var minusY = Apply(m, Apply(k, -Vector3.UnitY));
        (minusY - Vector3.UnitX).Length.Should().BeLessThan(1e-4f);
        var lz = Apply(m, Apply(k, Vector3.UnitZ));
        (lz - Vector3.UnitY).Length.Should().BeLessThan(1e-4f);
    }

    [Fact]
    public void Turn_KeepsTheNodeScale()
    {
        var k = Matrix4.CreateScale(2f) * Matrix4.CreateTranslation(5f, 0f, 0f);
        var m = GlRenderer.BillboardTurn(k, 2, Matrix4.Identity, new Vector3(5f, 0f, 50f), Vector3.UnitY, Vector3.UnitZ);
        // Camera straight in front along +Z: the authored pose already faces it.
        var p = Apply(k, new Vector3(1f, 2f, 3f));
        (Apply(m, p) - p).Length.Should().BeLessThan(1e-3f);
    }

    [Fact]
    public void BinderSetsTheFrame_AndATurnedSpriteFollowsItsOwnCullFlag()
    {
        var built = new NifMeshBuilder.BuiltMesh
        {
            Positions = Array.Empty<System.Numerics.Vector3>(), Normals = Array.Empty<System.Numerics.Vector3>(),
            Indices = Array.Empty<int>(), TextureCoordinates = Array.Empty<System.Numerics.Vector2>(),
            Tangents = Array.Empty<System.Numerics.Vector3>(), Bitangents = Array.Empty<System.Numerics.Vector3>(),
            ShapeName = "Flames:0", HasEffectShader = true, EffectShaderFlags1 = 0xC0000078, EffectShaderFlags2 = 0x21,
            BillboardMode = 1, BillboardFrame = System.Numerics.Matrix4x4.CreateTranslation(1f, 2f, 3f),
        };
        var m = new GlMesh();
        NifMaterialBinder.ApplyEffectMaterial(m, built, 1, true, 0);
        m.BillboardMode.Should().Be(1);
        m.BillboardFrame.Should().NotBeNull();
        m.BillboardFrame!.Value.Row3.Xyz.Should().Be(new Vector3(1f, 2f, 3f));
        m.IsDoubleSided.Should().BeFalse();   // SLSF2 Double_Sided clear; the renderer turns it
    }

    [Fact]
    public void SceneProfile_StartsFromVanillaClearDay_CharacterProfileKeepsTheStudioRig()
    {
        var scene = new GlRenderer();
        scene.Profile.Should().Be(RenderProfile.Scene);
        scene.Fog.Should().NotBeNull();
        scene.Fog!.Far.Should().Be(80000f);
        scene.Fog.Power.Should().Be(0.4f);
        scene.Fog.Max.Should().Be(0.85f);
        scene.ImageSpace.Should().NotBeNull();
        scene.ImageSpace!.Contrast.Should().Be(1.4f);
        scene.SkyGradient.Should().NotBeNull();
        scene.Lights[1].Intensity.Should().Be(2.8f);
        scene.Lights[2].Intensity.Should().Be(0f);
        scene.Billboards.Should().BeTrue();

        var character = new GlRenderer(RenderProfile.Character);
        character.Fog.Should().BeNull();
        character.ImageSpace.Should().BeNull();
        character.SkyGradient.Should().BeNull();
        character.SkyColor.Should().BeNull();
        character.ClearColor.Should().Be(new Vector3(0.41f, 0.41f, 0.41f));
        character.Lights[0].Intensity.Should().Be(0.2f);
        character.Lights[2].Intensity.Should().Be(0.38f);
    }

    [Fact]
    public void VanillaClearDay_SkyIsTheFoggedDome()
    {
        var env = SceneEnvironment.VanillaClearDay;
        // Horizon key: (1 - 0.85) x horizon (125, 163, 183) + 0.85 x fog far (116, 168, 203) + 0.05.
        var h = env.SkyGradient!.At(0f);
        h.X.Should().BeApproximately((0.15f * 125f + 0.85f * 116f) / 255f + 0.05f, 1e-4f);
        h.Z.Should().BeApproximately((0.15f * 183f + 0.85f * 203f) / 255f + 0.05f, 1e-4f);
        env.Background.Should().Be(env.SkyGradient.At(20f));
    }

    [Fact]
    public void Defaults_EffectShadingAndPerPixelWaterAreOn()
    {
        new MaterialBindOptions().ShadeEffectShaders.Should().BeTrue();
        new SkyRender.World.WaterMaterial(System.Numerics.Vector3.Zero, System.Numerics.Vector3.Zero,
            System.Numerics.Vector3.Zero, 0f, 0f).PerPixelDepth.Should().BeTrue();
    }
}
