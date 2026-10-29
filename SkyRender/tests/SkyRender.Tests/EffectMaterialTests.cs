using System;
using System.Numerics;
using SkyRender;
using FluentAssertions;
using Xunit;

namespace SkyRender.Tests;

/// <summary><see cref="NifMaterialBinder.ApplyEffectMaterial"/>: how an effect-shader shape's
/// BSEffectShaderProperty fields and SLSF bits map onto a mesh's effect state. Property values
/// only; no GL context is touched.</summary>
public class EffectMaterialTests
{
    // A vanilla FireplaceWood01Burning flame sprite: palette colour and alpha, falloff, soft,
    // z-test (SLSF1 0xC0000078), vertex colours (SLSF2 0x20), additive blend.
    private static NifMeshBuilder.BuiltMesh Shape(bool effect = true, uint f1 = 0xC0000078, uint f2 = 0x00000021,
        uint clamp = 3, int billboard = -1, float softDepth = 32f) => new()
    {
        Positions = Array.Empty<Vector3>(), Normals = Array.Empty<Vector3>(), Indices = Array.Empty<int>(),
        TextureCoordinates = Array.Empty<Vector2>(), Tangents = Array.Empty<Vector3>(),
        Bitangents = Array.Empty<Vector3>(), ShapeName = "Flames:0",
        HasEffectShader = effect,
        EffectShaderFlags1 = f1,
        EffectShaderFlags2 = f2,
        EffectBaseColor = new Vector3(0.22f, 0.22f, 0.22f),
        EffectBaseColorAlpha = 0.9f,
        EffectBaseColorScale = 1.75f,
        EffectLightingInfluence = 1f,
        EffectFalloff = new Vector4(0.17f, 0.09f, 1f, 0f),
        EffectSoftFalloffDepth = softDepth,
        EffectTextureClampMode = clamp,
        EffectUvScale = new Vector2(1f, 2f),
        EffectUvOffset = new Vector2(0f, 0.89f),
        BillboardMode = billboard,
        HasAlphaBlend = true,
        SrcBlendIndex = 6,
        DstBlendIndex = 0,
        MaterialAlpha = 0.25f,
    };

    [Fact]
    public void NewMesh_IsNotAnEffect()
    {
        new GlMesh().IsEffectShader.Should().BeFalse();
    }

    [Fact]
    public void LightingShape_OnlyClearsTheEffectFlag()
    {
        var m = new GlMesh { IsEffectShader = true, DiffuseTexture = 7, MaterialAlpha = 0.5f };
        NifMaterialBinder.ApplyEffectMaterial(m, Shape(effect: false), 3, true, 4);
        m.IsEffectShader.Should().BeFalse();
        m.DiffuseTexture.Should().Be(7);
        m.MaterialAlpha.Should().Be(0.5f);
    }

    [Fact]
    public void Flame_MapsMaterialAndFlags()
    {
        var m = new GlMesh();
        NifMaterialBinder.ApplyEffectMaterial(m, Shape(), 11, true, 12);
        m.IsEffectShader.Should().BeTrue();
        m.DiffuseTexture.Should().Be(11);
        m.EffectGreyscaleTexture.Should().Be(12);
        m.EffectBaseColor.Should().Be(new Vector4(0.22f, 0.22f, 0.22f, 0.9f));
        m.EffectBaseColorScale.Should().Be(1.75f);
        m.EffectGreyscaleToColor.Should().BeTrue();    // SLSF1 bit 4
        m.EffectGreyscaleToAlpha.Should().BeTrue();    // SLSF1 bit 5
        m.EffectUseFalloff.Should().BeTrue();          // SLSF1 bit 6
        m.EffectSoftFalloffDepth.Should().Be(32f);     // SLSF1 bit 30
        m.EffectDepthTest.Should().BeTrue();           // SLSF1 bit 31
        m.DepthWrite.Should().BeTrue();                // SLSF2 bit 0
        m.IsDoubleSided.Should().BeFalse();            // SLSF2 bit 4 clear, no billboard
        m.EffectLit.Should().BeFalse();                // SLSF2 bit 30 clear
        m.UvScale.Should().Be(new Vector2(1f, 2f));
        m.UvOffset.Should().Be(new Vector2(0f, 0.89f));
        m.HasAlphaBlend.Should().BeTrue();
        m.DstBlendIndex.Should().Be(0);                // additive
        m.MaterialAlpha.Should().Be(1f);               // the effect computes its own alpha
    }

    [Fact]
    public void ClearedFlags_TurnFeaturesOff()
    {
        var m = new GlMesh();
        NifMaterialBinder.ApplyEffectMaterial(m, Shape(f1: 0, f2: (1u << 4) | (1u << 30)), 1, true, 0);
        m.EffectGreyscaleToColor.Should().BeFalse();
        m.EffectUseFalloff.Should().BeFalse();
        m.EffectSoftFalloffDepth.Should().Be(0f);
        m.EffectDepthTest.Should().BeFalse();
        m.DepthWrite.Should().BeFalse();
        m.IsDoubleSided.Should().BeTrue();
        m.EffectLit.Should().BeTrue();
    }

    [Fact]
    public void SoftEffectWithZeroRange_StaysSoft()
    {
        var m = new GlMesh();
        NifMaterialBinder.ApplyEffectMaterial(m, Shape(f1: 1u << 30, softDepth: 0f), 1, true, 0);
        m.EffectSoftFalloffDepth.Should().BeGreaterThan(0f);
    }

    [Theory]
    [InlineData(0u, true, true)]
    [InlineData(1u, true, false)]
    [InlineData(2u, false, true)]
    [InlineData(3u, false, false)]
    public void ClampMode_MapsPerAxis(uint mode, bool clampS, bool clampT)
    {
        var m = new GlMesh();
        NifMaterialBinder.ApplyEffectMaterial(m, Shape(clamp: mode), 1, true, 0);
        m.EffectClampS.Should().Be(clampS);
        m.EffectClampT.Should().Be(clampT);
    }

    [Theory]
    [InlineData(-1, 0, false)]
    [InlineData(0, 1, true)]    // ALWAYS_FACE_CAMERA
    [InlineData(2, 1, true)]    // RIGID_FACE_CAMERA
    [InlineData(1, 2, true)]    // ROTATE_ABOUT_UP
    [InlineData(5, 2, true)]    // BSROTATE_ABOUT_UP
    public void Billboard_IsNeverCulled(int mode, int expected, bool doubleSided)
    {
        var m = new GlMesh();
        NifMaterialBinder.ApplyEffectMaterial(m, Shape(billboard: mode), 1, true, 0);
        m.EffectBillboard.Should().Be(expected);
        m.IsDoubleSided.Should().Be(doubleSided);
    }
}
