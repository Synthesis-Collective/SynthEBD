using System;
using System.Collections.Generic;
using System.Threading;

namespace SkyRender;

/// <summary>
/// Inputs to <see cref="NifMaterialBinder.Bind"/> beyond the mesh and its textures. Every member
/// is optional: A host rendering plain scene geometry (statics, architecture, clutter) leaves them
/// all at their defaults and gets the engine's NIF-authored material.
/// </summary>
public sealed class MaterialBindOptions
{
    /// <summary>Checked before each texture group; each load can extract and decode a DDS.</summary>
    public CancellationToken Cancellation { get; init; }

    /// <summary>
    /// What to do with an alpha-tested or alpha-blended shape whose diffuse could not be
    /// resolved: true draws it as a wireframe placeholder, false hides it.
    /// </summary>
    public bool RenderMissingTextureAsWireframe { get; init; } = true;

    /// <summary>
    /// Optional slot-0 binder, called first. Return true when it bound the diffuse itself (for
    /// example a tinted texture or a separate tint layer); false falls through to plain slot-0
    /// binding.
    /// </summary>
    public Func<GlMesh, NifMeshBuilder.BuiltMesh, IReadOnlyDictionary<int, string>, bool>? BindDiffuse { get; init; }

    /// <summary>
    /// Tint colour for the skin-tint and face-tint shader types (BSLSP types 5 and 4). Null
    /// leaves those shapes untinted. The face-tint type only receives it when the renderer's
    /// face toggle is on.
    /// </summary>
    public (float R, float G, float B)? SkinTintColor { get; init; }

    /// <summary>
    /// Extra eye classification. Shapes of the eye shader type (BSLSP type 16) are always eyes;
    /// this lets a host also classify shapes authored with another shader type.
    /// </summary>
    public Func<NifMeshBuilder.BuiltMesh, bool>? IsEyeShape { get; init; }

    /// <summary>
    /// Detail texture bound to a face-tint-type shape whose detail flag is set but whose detail
    /// slot is empty. Null (the default) binds nothing, which matches the engine.
    /// </summary>
    public string? EmptyFaceDetailFallbackPath { get; init; }

    /// <summary>Receives verbose diagnostic lines. Null discards them.</summary>
    public Action<string>? LogVerbose { get; init; }

    /// <summary>
    /// Shade effect-shader shapes (<see cref="NifMeshBuilder.BuiltMesh.HasEffectShader"/>: fire,
    /// smoke, mist, light shafts) with the engine's effect shading
    /// (<see cref="NifMaterialBinder.ApplyEffectMaterial"/>). On by default; false binds them
    /// through the lighting-shader rules (the behaviour before the effect path existed).
    /// </summary>
    public bool ShadeEffectShaders { get; init; } = true;
}

/// <summary>
/// Maps a parsed NIF material (<see cref="NifMeshBuilder.BuiltMesh"/>) onto a renderable
/// <see cref="GlMesh"/>: texture slots, specular, glow, emissive, environment and detail maps,
/// shader-flag lighting bits, alpha test and blend. Mirrors the engine's
/// BSLightingShaderProperty rules; host-specific behaviour enters through
/// <see cref="MaterialBindOptions"/>.
/// </summary>
public static class NifMaterialBinder
{
    // Shader types (BSLightingShaderProperty.ShaderType).
    private const uint ShaderTypeFaceTint = 4;
    private const uint ShaderTypeSkinTint = 5;
    private const uint ShaderTypeEye = 16;

    public static void Bind(GlTextureManager textures, GameAssetResolver? assets,
        GlMesh glMesh, NifMeshBuilder.BuiltMesh built,
        IReadOnlyDictionary<int, string> effectiveTextures, MaterialBindOptions options)
    {
        var ct = options.Cancellation;
        ct.ThrowIfCancellationRequested();

        if (built.HasEffectShader && options.ShadeEffectShaders)
        {
            BindEffect(textures, assets, glMesh, built, effectiveTextures);
            return;
        }

        // Diffuse (slot 0). A host binder runs first; otherwise the slot binds as authored.
        if (options.BindDiffuse == null || !options.BindDiffuse(glMesh, built, effectiveTextures))
        {
            if (effectiveTextures.TryGetValue(0, out string? diffusePath))
            {
                glMesh.DiffuseTexture = textures.LoadTexture(diffusePath);
                RecordTextureSource(assets, glMesh, "Diffuse", diffusePath);
            }
            else
            {
                glMesh.DiffuseTexture = textures.WhiteTexture;
            }
        }

        // Normal map (slot 1)
        ct.ThrowIfCancellationRequested();
        if (effectiveTextures.TryGetValue(1, out string? normalPath))
        {
            glMesh.NormalTexture = textures.LoadTexture(normalPath);
            glMesh.HasNormalMap = true;
            glMesh.IsModelSpace = built.IsModelSpaceNormals;
            RecordTextureSource(assets, glMesh, "Normal Map", normalPath);
        }
        else
        {
            glMesh.NormalTexture = textures.WhiteTexture;
        }

        // Skin/subsurface map (slot 2) — only meaningful for skin/face shader types.
        // For other shader types (eye, hair, default, etc.) slot 2 has a different meaning
        // (glow, environment, etc.) and applying it as a skin map would add incorrect red SSS tinting.
        bool isSkinShader = built.ShaderType == ShaderTypeFaceTint
                         || built.ShaderType == ShaderTypeSkinTint;
        ct.ThrowIfCancellationRequested();
        if (isSkinShader && effectiveTextures.TryGetValue(2, out string? skinPath))
        {
            glMesh.SkinTexture = textures.LoadTexture(skinPath);
            glMesh.HasSkinMap = true;
            RecordTextureSource(assets, glMesh, "Skin/SSS", skinPath);
        }
        else
        {
            glMesh.SkinTexture = textures.WhiteTexture;
        }

        // Glow map (slot 2 on NON-skin shaders, e.g. BSLSP_GLOWMAP gear with an _emit.dds).
        // Gated on SLSF2_Glow_Map + OwnEmit: without OwnEmit the shader's emissive term never
        // fires (sk_default computes emittance only under hasEmit), so loading would be wasted.
        // Typical authoring is white emissiveColor x 1.0 with the pattern in the map; a black
        // authored emissiveColor legitimately kills the glow.
        ct.ThrowIfCancellationRequested();
        bool glowFlagSet = (built.ShaderFlags2 & (1u << 6)) != 0   // SLSF2_Glow_Map
                        && (built.ShaderFlags1 & (1u << 22)) != 0; // SLSF1_Own_Emit
        if (glowFlagSet && !isSkinShader && effectiveTextures.TryGetValue(2, out string? glowPath))
        {
            int glowTex = textures.LoadTexture(glowPath);
            if (glowTex != textures.WhiteTexture)
            {
                glMesh.GlowTexture = glowTex;
                glMesh.HasGlowMap = true;
                RecordTextureSource(assets, glMesh, "Glow Map", glowPath);
            }
        }

        // Specular map (slot 7). When SLSF2_Back_Lighting is set, slot 7 holds a backlight mask
        // instead of a specular mask (NifSkope sk_msn.frag ignores the slot for specular in that
        // case), so those shapes fall through to the normal-map-alpha specular mask in the shader.
        ct.ThrowIfCancellationRequested();
        bool slot7IsBacklight = (built.ShaderFlags2 & (1u << 27)) != 0; // SLSF2_Back_Lighting
        if (!slot7IsBacklight && effectiveTextures.TryGetValue(7, out string? specPath))
        {
            glMesh.SpecularTexture = textures.LoadTexture(specPath);
            glMesh.HasSpecularMap = true;
            // SLSF1_Specular gates the whole specular term in the engine — a shape can ship an
            // authored _s.dds with the flag clear and renders WITHOUT specular in game.
            glMesh.HasSpecular = (built.ShaderFlags1 & (1u << 0)) != 0;
            RecordTextureSource(assets, glMesh, "Specular", specPath);
        }
        else
        {
            glMesh.SpecularTexture = textures.WhiteTexture;
            // Check shader flags for specular enable even without a map
            glMesh.HasSpecular = (built.ShaderFlags1 & (1u << 0)) != 0;
        }

        if (!glMesh.HasFaceTintMap)
            glMesh.FaceTintTexture = textures.WhiteTexture;

        // Material properties
        glMesh.MaterialGlossiness = built.Glossiness;
        glMesh.MaterialSpecularStrength = built.SpecularStrength;
        glMesh.SpecularColor = built.SpecularColor;
        glMesh.SubsurfaceRolloff = built.SubsurfaceRolloff;
        glMesh.RimlightPower = built.RimlightPower;
        glMesh.HasVertexColors = built.HasVertexColors;
        glMesh.UvScale = built.UvScale;
        glMesh.UvOffset = built.UvOffset;

        // Emissive (SLSF1_OwnEmit, bit 22)
        if ((built.ShaderFlags1 & (1u << 22)) != 0)
        {
            glMesh.HasEmissive = true;
            glMesh.EmissiveColor = built.EmissiveColor;
            glMesh.EmissiveMultiple = built.EmissiveMultiple;
        }

        // Shader type flags (bit positions per Nifskope/Bethesda spec)
        glMesh.HasHairSoftLighting = (built.ShaderFlags1 & (1u << 18)) != 0; // SLSF1_Hair_Soft_Lighting
        glMesh.HasSoftLighting = (built.ShaderFlags2 & (1u << 25)) != 0; // SLSF2_Soft_Lighting
        glMesh.HasRimLighting = (built.ShaderFlags2 & (1u << 26)) != 0; // SLSF2_Rim_Lighting
        // SLSF2_Tree_Anim: the vertex colour's alpha is the wind-sway weight (the engine's vertex
        // shader scales the sway by it), not opacity. Multiplying it into the alpha test cut most
        // leaf cards away, leaving trees as bare trunks.
        glMesh.VertexAlphaIsWindWeight = (built.ShaderFlags2 & (1u << 29)) != 0;

        // Skin tint for the skin-tint shader type (production behavior) and the face-tint type
        // (debug path). Face shapes always receive the colour, but its application stays gated
        // by the GlRenderer.SkinTintApplyToFace uniform so the toggle can flip at runtime
        // without re-loading the scene.
        if (isSkinShader && options.SkinTintColor.HasValue)
        {
            var (r, g, b) = options.SkinTintColor.Value;
            glMesh.HasTintColor = true;
            glMesh.TintColor = new System.Numerics.Vector3(r, g, b);
        }
        glMesh.IsFaceShape = built.ShaderType == ShaderTypeFaceTint;
        glMesh.IsSkinShape = isSkinShader;
        glMesh.SkinTintAlpha = built.SkinTintAlpha;

        // Eye shader type, plus any host classification for eyes authored with another type.
        if (built.ShaderType == ShaderTypeEye || (options.IsEyeShape?.Invoke(built) ?? false))
        {
            glMesh.IsEye = true;
        }

        // Environment mapping (SLSF1_Environment_Mapping bit 7, or SLSF1_Eye_Environment_Mapping bit 17)
        ct.ThrowIfCancellationRequested();
        bool hasEnvMap = (built.ShaderFlags1 & (1u << 7)) != 0;
        bool hasEyeEnvMap = (built.ShaderFlags1 & (1u << 17)) != 0;
        if ((hasEnvMap || hasEyeEnvMap) && effectiveTextures.TryGetValue(4, out string? envMapPath))
        {
            var (envTex, envIsCube) = textures.LoadEnvMap(envMapPath);
            if (envTex != 0)
            {
                glMesh.EnvMapTexture = envTex;
                glMesh.HasEnvironmentMap = true;
                glMesh.IsEnvMap2D = !envIsCube;
                glMesh.EnvMapScale = built.EnvironmentMapScale;
                glMesh.EyeCubemapScale = built.EyeCubemapScale;
                // Engine-faithful scale selection: only the eye shader type uses
                // eyeCubemapScale; environment-map-typed shapes — including shapes a host
                // classified as eyes — use envMapScale in-game.
                glMesh.UseEyeCubemapScale = built.ShaderType == ShaderTypeEye;
                RecordTextureSource(assets, glMesh,
                    envIsCube ? "Environment Cubemap" : "Environment Map (2D fallback)",
                    envMapPath);
            }

            if (effectiveTextures.TryGetValue(5, out string? envMaskPath))
            {
                glMesh.EnvMaskTexture = textures.LoadTexture(envMaskPath);
                glMesh.HasEnvMask = true;
                RecordTextureSource(assets, glMesh, "Environment Mask", envMaskPath);
            }
        }

        // Detail map (SLSF1_Facegen_Detail_Map, bit 10)
        bool detailFlagSet = (built.ShaderFlags1 & (1u << 10)) != 0;
        bool detailSlotPopulated = effectiveTextures.TryGetValue(3, out string? detailPath);
        if (detailFlagSet && detailSlotPopulated && detailPath != null)
        {
            int detailTex = textures.LoadTexture(detailPath);
            if (detailTex != textures.WhiteTexture)
            {
                glMesh.DetailTexture = detailTex;
                glMesh.HasDetailMap = true;
            }
            else
            {
                // Slot 3 names a file that doesn't exist in the load order (e.g. a FaceGen NIF
                // baking a detail path vanilla only ships in another directory). The engine
                // treats a missing detail map as "no detail map"; binding the 1x1 white
                // fallback instead saturates the face to white under BOTH detail blend modes
                // (engine-style ~x4 multiply and legacy overlay), so treat the slot as empty.
                // Still recorded below so the hover tooltip surfaces the broken path.
                detailSlotPopulated = false;
                options.LogVerbose?.Invoke("SkyRender: Detail map '" + detailPath +
                    "' not found — treating slot 3 as empty (engine behavior)");
            }
            RecordTextureSource(assets, glMesh, "Detail Map", detailPath);
        }
        if (detailFlagSet && !detailSlotPopulated && built.ShaderType == ShaderTypeFaceTint
            && options.EmptyFaceDetailFallbackPath != null)
        {
            glMesh.DetailTexture = textures.LoadTexture(options.EmptyFaceDetailFallbackPath);
            glMesh.HasDetailMap = true;
            RecordTextureSource(assets, glMesh, "Detail Map (BlankDetailmap fallback)",
                options.EmptyFaceDetailFallbackPath);
        }

        // Track face shapes with an empty slot 3 for the optional "force multiply on empty
        // detail" debug path in basic.frag. Set independently of whether the toggle is on; the
        // shader-side toggle (FaceTintMultiplyOnEmptyDetail) decides whether to act on it.
        glMesh.IsFaceWithEmptyDetailSlot = built.ShaderType == ShaderTypeFaceTint
            && detailFlagSet
            && !detailSlotPopulated;

        // Double-sided (thin geometry visible from both sides)
        glMesh.IsDoubleSided = built.IsDoubleSided;

        // Depth-write + material alpha. Set unconditionally (independent of the alpha-test/blend
        // branch below) so the alpha-blend pass can decide, per shape, whether to write depth:
        // solid blended geometry (ZBuffer_Write set, opaque material) occludes what's behind it;
        // overlay decals and translucent materials do not. See GlRenderer Pass 2.
        glMesh.DepthWrite = built.ZBufferWrite;
        glMesh.IsDecal = built.IsDecal;
        glMesh.MaterialAlpha = built.MaterialAlpha;

        // Alpha test / blend
        if (built.HasAlphaTest || built.HasAlphaBlend)
        {
            if (glMesh.DiffuseTexture != textures.WhiteTexture)
            {
                // Suppress the GL discard on face-tint-type shapes (ShaderType=4) that carry the
                // NiAlphaProperty alpha-test bit.
                //
                // What we observed:
                //   - Vanilla Khajiit MaleHeadKhajiit carries NiAlphaProperty.AlphaTest=True
                //     with threshold 73 (≈0.286).
                //   - Its diffuse texture has alpha < 1 across the entire face (this is
                //     independently noted in the renderer's Pass-1 comment in GlRenderer.cs,
                //     which deliberately disables SAMPLE_ALPHA_TO_COVERAGE for that reason).
                //   - It also has SLSF1_Vertex_Alpha set with vertex-color alpha varying from
                //     1.0 down to ~0.325 (a low-alpha ring around the head/body seam — 133 of
                //     1356 vertices on Ri'saad).
                //   - basic.frag multiplies vertex alpha into texture alpha before the discard
                //     test, so face fragments near the seam produce α ≈ 0.276 (below the 0.286
                //     threshold) and discard. At ~750 px output resolution each discarded
                //     fragment spans a perceptible pixel-sized hole.
                //   - In vanilla in-game Skyrim, Khajiit faces render solid; this discard
                //     pattern does not appear there.
                //
                // What we have NOT verified:
                //   - The exact engine-side mechanism that produces the solid in-game face.
                //     Plausible candidates we haven't traced: (a) the engine ignoring the
                //     alpha-test bit on BSLSP_FACE entirely, (b) alpha-to-coverage running on
                //     a framebuffer configured differently from ours, (c) BSLSP_FACE being
                //     routed through a separate draw call that doesn't consult
                //     NiAlphaProperty, (d) Bethesda's actual vertex-alpha values differing from
                //     what niflysharp reads back. We haven't read engine source or Community
                //     Shaders' replacement shader for this specific path.
                //   - Authorial intent for the vertex_alpha ring. The seam-localized
                //     distribution makes a head/body seam fade a plausible reading, but
                //     Bethesda's authoring intent isn't documented.
                //
                // What this fix does:
                //   - For ShaderType==4 face shapes only, skip writing `built.HasAlphaTest` to
                //     `glMesh.UseAlphaTest`. The NIF's HasAlphaBlend / AlphaThreshold / blend
                //     factors are still propagated unchanged so any other shader path that
                //     consults them continues to see the NIF-authored values.
                //
                // The result is that face fragments along the seam render with their actual
                // partial alpha instead of being discarded. That partial alpha then lands in the
                // off-screen pipeline's readback bytes — see the alpha-strip step in
                // GameWindowOffscreenRenderer.RenderInternalCore for the companion piece of this
                // two-half fix.
                bool suppressAlphaTestForFace = built.ShaderType == ShaderTypeFaceTint && built.HasAlphaTest;
                glMesh.UseAlphaTest = built.HasAlphaTest && !suppressAlphaTestForFace;
                glMesh.HasAlphaBlend = built.HasAlphaBlend;
                glMesh.AlphaThreshold = built.AlphaThreshold;
                glMesh.SrcBlendIndex = built.SrcBlendIndex;
                glMesh.DstBlendIndex = built.DstBlendIndex;
            }
            else
            {
                // Alpha-tested / alpha-blended shape with no resolvable diffuse. Without a usable
                // alpha channel the discard threshold is undefined and a flat-white billboard
                // would mislead the user. Render as wireframe placeholder by default so the
                // shape's silhouette stays visible alongside the host's missing-texture overlay;
                // if the host has opted out, fall back to silent culling (matches pre-2.5.6
                // behavior).
                string disposition = options.RenderMissingTextureAsWireframe
                    ? "WIREFRAME-FALLBACK" : "CULLED";
                options.LogVerbose?.Invoke(
                    $"SkyRender: {disposition} shape='{glMesh.ShapeName}' " +
                    $"bodyPart='{glMesh.BodyPart}' " +
                    $"alphaTest={built.HasAlphaTest} alphaBlend={built.HasAlphaBlend} " +
                    $"hairTint={built.IsHairTintShader} " +
                    "(diffuse fell back to WhiteTexture — texture path missing or DDS load failed)");
                if (options.RenderMissingTextureAsWireframe)
                    glMesh.RenderAsWireframeFallback = true;
                else
                    glMesh.IsRendering = false;
            }
        }

        glMesh.IsHairTintShader = built.IsHairTintShader;
        glMesh.IsPrimaryHeadShape = built.IsPrimaryHeadShape;
    }

    private static void BindEffect(GlTextureManager textures, GameAssetResolver? assets,
        GlMesh glMesh, NifMeshBuilder.BuiltMesh built, IReadOnlyDictionary<int, string> effectiveTextures)
    {
        // nifly maps texture slot 0 of an effect shape to BSEffectShaderProperty.sourceTexture.
        int source = 0;
        if (effectiveTextures.TryGetValue(0, out string? sourcePath))
        {
            source = textures.LoadTexture(sourcePath);
            RecordTextureSource(assets, glMesh, "Effect Source", sourcePath);
        }
        int palette = 0;
        if (built.EffectGreyscaleTexture is { } greyPath)
        {
            palette = textures.LoadTexture(greyPath);
            if (palette == textures.WhiteTexture) palette = 0;
            RecordTextureSource(assets, glMesh, "Effect Greyscale", greyPath);
        }
        glMesh.NormalTexture = textures.WhiteTexture;
        glMesh.SkinTexture = textures.WhiteTexture;
        glMesh.SpecularTexture = textures.WhiteTexture;
        glMesh.FaceTintTexture = textures.WhiteTexture;
        ApplyEffectMaterial(glMesh, built, source == 0 ? textures.WhiteTexture : source,
                            source != 0 && source != textures.WhiteTexture, palette);
        glMesh.IsPrimaryHeadShape = built.IsPrimaryHeadShape;
    }

    // SLSF bits the effect path reads (same layout on BSEffectShaderProperty).
    private const uint EffectF1GreyscaleToColor = 1u << 4;
    private const uint EffectF1GreyscaleToAlpha = 1u << 5;
    private const uint EffectF1UseFalloff       = 1u << 6;
    private const uint EffectF1SoftEffect       = 1u << 30;
    private const uint EffectF1ZBufferTest      = 1u << 31;
    private const uint EffectF2ZBufferWrite     = 1u << 0;
    private const uint EffectF2DoubleSided      = 1u << 4;
    private const uint EffectF2EffectLighting   = 1u << 30;

    /// <summary>
    /// Sets a mesh up for the engine's effect shading from an effect-shader shape's material
    /// (<see cref="NifMeshBuilder.BuiltMesh.HasEffectShader"/>): base colour and scale, lighting
    /// influence, palette flags, falloff, soft depth, texture clamp, depth test/write, culling,
    /// UV transform and the NiAlphaProperty test/blend. Textures are the caller's: pass the GL
    /// handle of the source texture (slot 0) and of the greyscale palette (0 = none), so a host
    /// with its own texture cache can use this without <see cref="Bind"/>. A shape without an
    /// effect shader is left untouched apart from clearing <see cref="GlMesh.IsEffectShader"/>.
    /// </summary>
    public static void ApplyEffectMaterial(GlMesh glMesh, NifMeshBuilder.BuiltMesh built,
        int sourceTexture, bool hasSourceTexture, int greyscaleTexture)
    {
        glMesh.IsEffectShader = built.HasEffectShader;
        if (!built.HasEffectShader) return;

        uint f1 = built.EffectShaderFlags1, f2 = built.EffectShaderFlags2;
        glMesh.DiffuseTexture = sourceTexture;
        glMesh.EffectHasSourceTexture = hasSourceTexture;
        glMesh.EffectGreyscaleTexture = greyscaleTexture;
        glMesh.EffectBaseColor = new System.Numerics.Vector4(built.EffectBaseColor, built.EffectBaseColorAlpha);
        glMesh.EffectBaseColorScale = built.EffectBaseColorScale;
        glMesh.EffectLightingInfluence = built.EffectLightingInfluence;
        glMesh.EffectLit = (f2 & EffectF2EffectLighting) != 0;
        glMesh.EffectGreyscaleToColor = (f1 & EffectF1GreyscaleToColor) != 0;
        glMesh.EffectGreyscaleToAlpha = (f1 & EffectF1GreyscaleToAlpha) != 0;
        glMesh.EffectUseFalloff = (f1 & EffectF1UseFalloff) != 0;
        glMesh.EffectFalloff = built.EffectFalloff;
        // A soft effect with a zero range still takes the soft path (its discard of near-zero
        // alpha); the epsilon keeps the shader's division finite.
        glMesh.EffectSoftFalloffDepth = (f1 & EffectF1SoftEffect) != 0
            ? Math.Max(built.EffectSoftFalloffDepth, 1e-3f) : 0f;
        glMesh.EffectDepthTest = (f1 & EffectF1ZBufferTest) != 0;
        uint clamp = built.EffectTextureClampMode & 3;
        glMesh.EffectClampS = clamp == 0 || clamp == 1;
        glMesh.EffectClampT = clamp == 0 || clamp == 2;

        glMesh.DepthWrite = (f2 & EffectF2ZBufferWrite) != 0;
        // A billboard always shows the camera its front. With its node's frame the renderer turns
        // it toward the camera (GlRenderer.Billboards) and the shape's own two-sided flag applies.
        // Without one it is drawn in its authored pose and must not be culled: flame sprites
        // authored facing away from the view would otherwise vanish.
        glMesh.EffectBillboard = built.BillboardMode switch
        {
            < 0 => 0,
            1 or 5 or 9 => 2,   // ROTATE_ABOUT_UP, BSROTATE_ABOUT_UP, ROTATE_ABOUT_UP2
            _ => 1,
        };
        glMesh.BillboardMode = built.BillboardMode;
        glMesh.BillboardFrame = built.BillboardMode >= 0 && built.BillboardFrame is { } bf
            ? new OpenTK.Mathematics.Matrix4(bf.M11, bf.M12, bf.M13, bf.M14, bf.M21, bf.M22, bf.M23, bf.M24,
                                             bf.M31, bf.M32, bf.M33, bf.M34, bf.M41, bf.M42, bf.M43, bf.M44)
            : null;
        glMesh.IsDoubleSided = (f2 & EffectF2DoubleSided) != 0
                               || (glMesh.EffectBillboard != 0 && glMesh.BillboardFrame is null);
        glMesh.HasVertexColors = built.HasVertexColors;
        glMesh.VertexAlphaIsWindWeight = false;
        glMesh.UvScale = built.EffectUvScale;
        glMesh.UvOffset = built.EffectUvOffset;

        // Alpha: the NiAlphaProperty decides test and blend exactly as for any shape. The effect's
        // own alpha (base colour alpha, falloff, palette) is computed in the shader, so the
        // lighting-shader material alpha stays neutral.
        glMesh.MaterialAlpha = 1f;
        glMesh.UseAlphaTest = built.HasAlphaTest;
        glMesh.HasAlphaBlend = built.HasAlphaBlend;
        glMesh.AlphaThreshold = built.AlphaThreshold;
        glMesh.SrcBlendIndex = built.SrcBlendIndex;
        glMesh.DstBlendIndex = built.DstBlendIndex;
        glMesh.IsDecal = false;

        // None of the lighting-shader terms apply.
        glMesh.HasNormalMap = false;
        glMesh.HasSkinMap = false;
        glMesh.HasSpecular = false;
        glMesh.HasSpecularMap = false;
        glMesh.HasEmissive = false;
        glMesh.HasGlowMap = false;
        glMesh.HasEnvironmentMap = false;
        glMesh.HasDetailMap = false;
        glMesh.HasGreyscaleToPalette = false;
        glMesh.HasTintColor = false;
    }

    /// <summary>
    /// Resolves a texture's origin via the asset resolver and records it on the mesh (for a
    /// host's hover tooltip). If the slot already has an entry, replaces it (used when texture
    /// overrides update a slot). No-op without a resolver.
    /// </summary>
    public static void RecordTextureSource(GameAssetResolver? assets, GlMesh mesh, string slotLabel, string? gamePath)
    {
        if (assets == null || string.IsNullOrWhiteSpace(gamePath)) return;

        var source = assets.ResolveAssetSource(gamePath);

        for (int i = 0; i < mesh.TextureSources.Count; i++)
        {
            if (mesh.TextureSources[i].SlotLabel == slotLabel)
            {
                mesh.TextureSources[i] = (slotLabel, source);
                return;
            }
        }
        mesh.TextureSources.Add((slotLabel, source));
    }
}
