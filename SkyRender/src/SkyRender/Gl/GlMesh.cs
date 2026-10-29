using System;
using System.Numerics;
using OpenTK.Graphics.OpenGL4;

namespace SkyRender;

/// <summary>
/// Per-mesh GPU state: VAO, VBO, EBO, and material properties.
/// Vertex layout matches the shader:
///   location 0: vec3 position
///   location 1: vec3 normal
///   location 2: vec2 texcoord
///   location 3: vec4 color (vertex color)
///   location 4: vec3 tangent
///   location 5: vec3 bitangent
/// Stride = 3+3+2+4+3+3 = 18 floats = 72 bytes
/// </summary>
public class GlMesh : IDisposable
{
    public int Vao { get; private set; }
    public int Vbo { get; private set; }
    public int Ebo { get; private set; }
    /// <summary>Terrain only: the per-vertex layer-weight buffer (attribute locations 6-7), or 0.</summary>
    public int LayerWeightVbo { get; private set; }

    // ---- Terrain and water (SkyRender.World) ----

    /// <summary>Draws as a terrain quadrant: the diffuse is the base texture and up to
    /// <see cref="World.TerrainQuadrant.MaxLayers"/> layer textures are blended over it in order,
    /// weighted by the per-vertex layer weights times each layer texture's alpha.</summary>
    public bool IsTerrain { get; set; }
    /// <summary>Terrain only: GL handles of the layer diffuse textures, in blend order.</summary>
    public int[] TerrainLayerTextures { get; } = new int[World.TerrainQuadrant.MaxLayers];
    /// <summary>Terrain only: how many entries of <see cref="TerrainLayerTextures"/> are used.</summary>
    public int TerrainLayerCount { get; set; }
    /// <summary>Terrain only: how the layers combine with the base.</summary>
    public World.TerrainBlendMode TerrainBlend { get; set; }
    /// <summary>Terrain only: GL handles of the layer normal maps (0 = none), in blend order.</summary>
    public int[] TerrainLayerNormalTextures { get; } = new int[World.TerrainQuadrant.MaxLayers];
    /// <summary>Terrain only: <see cref="NormalTexture"/> is the base texture's normal map.</summary>
    public bool TerrainHasBaseNormal { get; set; }

    /// <summary>Distant LOD (<see cref="World.GlWorldFactory.CreateDistantLod"/>): which kind, for
    /// the engine's LOD shading terms (terrain LOD's noise modulation, tree LOD's half-strength sun
    /// with no N·L, LOD water's per-pixel Fresnel and sun specular). Null for every other mesh.</summary>
    public World.LodKind? DistantLod { get; set; }
    /// <summary>Terrain LOD only: GL handle of the LOD landscape noise texture (0 = no noise term).</summary>
    public int LodNoiseTexture { get; set; }

    /// <summary>Draws as a water surface: the vertex colour is the body colour (lit like any
    /// surface), and the reflection is mixed over the lit result from the live camera with the
    /// engine's Fresnel and distance blend (RENDERING_PIPELINE.md, "Water").</summary>
    public bool IsWater { get; set; }
    /// <summary>Water only: the water type's reflection colour, used (times
    /// <see cref="WaterReflectivity"/>) when <see cref="WaterReflectsSky"/> is off.</summary>
    public System.Numerics.Vector3 WaterReflectionColor { get; set; } = System.Numerics.Vector3.One;
    /// <summary>Water only: the reflection is the renderer's sky colour, as the engine's water shader
    /// reflects its sky when water reflections are on.</summary>
    public bool WaterReflectsSky { get; set; }
    /// <summary>Water only: the water type's noise falloff distance (world units), which the engine's
    /// reflection distance blend ramps against 8192. 0 turns the blend off (Fresnel everywhere).</summary>
    public float WaterNoiseFalloff { get; set; }
    /// <summary>Water only: reflectivity (0..1).</summary>
    public float WaterReflectivity { get; set; }
    /// <summary>Water only: Fresnel F0 (0..1).</summary>
    public float WaterFresnel { get; set; }
    /// <summary>Water only: the engine's per-pixel depth terms (RENDERING_PIPELINE.md, "Water",
    /// per-pixel depth). The body colour, the refraction weight and the bed's share of the pixel
    /// follow the water depth measured along each pixel's view ray against the renderer's depth
    /// prepass of the opaque scene, rather than the vertex colour and alpha. Off (the default) keeps
    /// the per-vertex path.</summary>
    public bool WaterSceneDepth { get; set; }
    /// <summary>Water only, <see cref="WaterSceneDepth"/>: the water type's shallow colour (0..1).</summary>
    public System.Numerics.Vector3 WaterShallowColor { get; set; }
    /// <summary>Water only, <see cref="WaterSceneDepth"/>: the water type's deep colour (0..1).</summary>
    public System.Numerics.Vector3 WaterDeepColor { get; set; }
    /// <summary>Water only, <see cref="WaterSceneDepth"/>: the above-water fog near distance.</summary>
    public float WaterFogNear { get; set; }
    /// <summary>Water only, <see cref="WaterSceneDepth"/>: the above-water fog far distance
    /// (world units of water along the view ray at which the water is opaque).</summary>
    public float WaterFogFar { get; set; }
    /// <summary>Water only, <see cref="WaterSceneDepth"/>: the above-water fog amount (the exponent
    /// of the refraction weight).</summary>
    public float WaterFogAmount { get; set; }
    /// <summary>Water only, <see cref="WaterSceneDepth"/>: the sun specular power (exponent of the
    /// reflected view ray against the sun direction). 0 = no sun specular.</summary>
    public float WaterSunSpecularPower { get; set; }
    /// <summary>Water only, <see cref="WaterSceneDepth"/>: the sun specular magnitude.</summary>
    public float WaterSunSpecularMagnitude { get; set; }
    /// <summary>Water only, <see cref="WaterSceneDepth"/>: the depth specular-lighting factor (0..1):
    /// how far the sun specular fades out over shallow water.</summary>
    public float WaterDepthSpecularLighting { get; set; }

    // ---- Projected material (the engine's PROJECTED_UV path) ----

    /// <summary>Covers this shape's own diffuse with a single-pass projected material (the snow,
    /// ash or moss a host derives from a material object) per pixel, the way the engine's lighting
    /// shader does. NIF data never sets it; off means the shape draws exactly as before. Requires
    /// <see cref="ProjectedNoiseTexture"/>. See RENDERING_PIPELINE.md, "Projected materials".</summary>
    public bool HasProjectedMaterial { get; set; }
    /// <summary>Projected only: the direction a covered surface faces, in world (viewer Y-up)
    /// space. The default is up.</summary>
    public Vector3 ProjectedDirection { get; set; } = Vector3.UnitY;
    /// <summary>Projected only: the engine's ProjectedUVParams constant. X = noise weight,
    /// Z = noise coordinates per world unit, W = coverage threshold (Y unused). Build it with
    /// <see cref="ProjectedParamsFor"/>.</summary>
    public Vector4 ProjectedParams { get; set; }
    /// <summary>Projected only: the material colour (the engine's ProjectedUVParams2), 0..1.</summary>
    public Vector3 ProjectedColor { get; set; } = Vector3.One;
    /// <summary>Projected only: the projected diffuse's tiling relative to the noise coordinates
    /// (the engine's fProjectedUVDiffuseNormalTilingScale).</summary>
    public float ProjectedDiffuseTiling { get; set; } = 1f;
    /// <summary>Projected only: GL handle of the coverage noise (red channel), or 0.</summary>
    public int ProjectedNoiseTexture { get; set; }
    /// <summary>Projected only: GL handle of the projected diffuse, or 0 for the engine's flat
    /// colour path (bEnableProjecteUVDiffuseNormals off): a hard edge in the plain colour.</summary>
    public int ProjectedDiffuseTexture { get; set; }

    /// <summary>The engine's ProjectedUVParams for a single-pass material: the material's falloff
    /// scale, falloff bias and noise UV scale, and the placing record's max angle in degrees. A
    /// pixel is covered where dot(normal, direction) * vertexAlpha exceeds
    /// cos + (1 - cos) * (bias + scale * noise).</summary>
    public static Vector4 ProjectedParamsFor(float falloffScale, float falloffBias, float noiseUvScale, float maxAngleDegrees)
    {
        float c = MathF.Cos(MathF.Min(MathF.Max(maxAngleDegrees, 0f), 180f) * MathF.PI / 180f);
        float inv = noiseUvScale > 1e-6f ? 1f / noiseUvScale : 0f;
        return new Vector4((1f - c) * falloffScale, 0f, inv, (1f - c) * falloffBias + c);
    }
    // ---- Effect shader (BSEffectShaderProperty) ----

    /// <summary>Shades this shape with the engine's effect shader (fire, smoke, mist, light
    /// shafts, glows) instead of the lighting shader: source texture x base colour, optional
    /// greyscale palette, view-angle falloff and soft depth fade, unlit unless the material's
    /// lighting influence says otherwise. NIF data never sets it by itself; a host opts in
    /// (<see cref="NifMaterialBinder.ApplyEffectMaterial"/>). Off means the shape draws exactly as
    /// before. Effect shapes never cast shadows and stay out of the ambient-occlusion prepass.
    /// See RENDERING_PIPELINE.md, "Effect shaders".</summary>
    public bool IsEffectShader { get; set; }
    /// <summary>Effect only: <c>baseColor</c> (RGB, alpha) the source texture is multiplied by.</summary>
    public System.Numerics.Vector4 EffectBaseColor { get; set; } = System.Numerics.Vector4.One;
    /// <summary>Effect only: <c>baseColorScale</c>. The engine applies it to the palette colour
    /// (greyscale-to-palette) only.</summary>
    public float EffectBaseColorScale { get; set; } = 1f;
    /// <summary>Effect only: lighting influence, 0..1.</summary>
    public float EffectLightingInfluence { get; set; }
    /// <summary>Effect only: SLSF2_Effect_Lighting, the engine's lit effect permutation: the
    /// lighting influence mixes in the scene light. Without it the light is a white constant.</summary>
    public bool EffectLit { get; set; }
    /// <summary>Effect only: whether a source texture is bound (the engine's TEXTURE permutation).
    /// Without one the base is white.</summary>
    public bool EffectHasSourceTexture { get; set; } = true;
    /// <summary>Effect only: GL handle of the greyscale palette, or 0.</summary>
    public int EffectGreyscaleTexture { get; set; }
    /// <summary>Effect only: SLSF1_Greyscale_To_PaletteColor; needs <see cref="EffectGreyscaleTexture"/>.</summary>
    public bool EffectGreyscaleToColor { get; set; }
    /// <summary>Effect only: SLSF1_Greyscale_To_PaletteAlpha; needs <see cref="EffectGreyscaleTexture"/>.</summary>
    public bool EffectGreyscaleToAlpha { get; set; }
    /// <summary>Effect only: SLSF1_Use_Falloff.</summary>
    public bool EffectUseFalloff { get; set; }
    /// <summary>Effect only: (startAngle, stopAngle, startOpacity, stopOpacity), angles as cosines.</summary>
    public System.Numerics.Vector4 EffectFalloff { get; set; } = System.Numerics.Vector4.One;
    /// <summary>Effect only: SLSF1_Soft_Effect depth range in game units (0 = hard). Needs the
    /// ambient-occlusion depth prepass; without it the effect draws hard-edged.</summary>
    public float EffectSoftFalloffDepth { get; set; }
    /// <summary>Effect only: clamp the source texture coordinates per axis (texture clamp mode
    /// 0-2), as the engine's sampler does.</summary>
    public bool EffectClampS { get; set; }
    /// <inheritdoc cref="EffectClampS"/>
    public bool EffectClampT { get; set; }
    /// <summary>Effect only: SLSF1_ZBuffer_Test. False draws the shape without a depth test.</summary>
    public bool EffectDepthTest { get; set; } = true;
    /// <summary>Effect only: the shape sits under an <c>NiBillboardNode</c>, which the engine turns
    /// toward the camera each frame. 0 = none, 1 = faces the camera, 2 = turns about the up axis
    /// only. Used when the shape is drawn in its authored pose (no <see cref="BillboardFrame"/>, or
    /// <c>GlRenderer.Billboards</c> off): the falloff is then evaluated as if it were turned
    /// (|N.V| = 1, or the view direction's horizontal length about the up axis) and the shape is
    /// never culled.</summary>
    public int EffectBillboard { get; set; }

    /// <summary>The <c>NiBillboardNode.billboardMode</c> the shape sits under (0-5, 9), or -1.</summary>
    public int BillboardMode { get; set; } = -1;

    /// <summary>The billboard node's frame in this mesh's vertex space (row i = the node's local
    /// axis i with its scale, row 3 = its origin; OpenTK row-vector convention), or null. With it,
    /// and <c>GlRenderer.Billboards</c> on, the renderer turns the shape about the node's origin
    /// toward the camera every draw, as the engine does (RENDERING_PIPELINE.md, "Billboards").
    /// A host that bakes a placement into the vertices composes the frame with that placement.</summary>
    public OpenTK.Mathematics.Matrix4? BillboardFrame { get; set; }

    public int IndexCount { get; private set; }

    // Material texture handles (GL texture IDs)
    public int DiffuseTexture { get; set; }
    public int NormalTexture { get; set; }
    public int SkinTexture { get; set; }
    public int SpecularTexture { get; set; }
    public int FaceTintTexture { get; set; }

    // Material flags
    public bool HasNormalMap { get; set; }
    public bool HasSkinMap { get; set; }
    public bool HasSpecular { get; set; }
    public bool HasSpecularMap { get; set; }
    public bool HasFaceTintMap { get; set; }
    public bool HasGreyscaleToPalette { get; set; }
    public bool HasTintColor { get; set; }
    public bool HasEmissive { get; set; }
    public bool IsModelSpace { get; set; }
    public bool HasHairSoftLighting { get; set; }
    public bool HasSoftLighting { get; set; }
    public bool HasRimLighting { get; set; }
    public bool HasVertexColors { get; set; }
    public bool UseAlphaTest { get; set; }
    public bool HasAlphaBlend { get; set; }
    public bool IsDoubleSided { get; set; }

    /// <summary>Whether this shape writes to the depth buffer, from the NIF's
    /// SLSF2_ZBuffer_Write flag (NifMeshBuilder). Only consulted in the
    /// alpha-blend pass: solid blended geometry (e.g. an SMP beard) keeps this
    /// true so it occludes what's behind it, while overlay decals (brows,
    /// eyelashes, face marks) ship it false so they composite without writing
    /// depth. Opaque and alpha-test passes always write depth. Defaults true.</summary>
    public bool DepthWrite { get; set; } = true;

    /// <summary>Whether this shape is decal geometry (SLSF1_Decal /
    /// SLSF1_Dynamic_Decal). Decals composite over the surface beneath and must
    /// never write depth in the blend pass, even when <see cref="DepthWrite"/>
    /// is set — hairline shells ship with both flags, and their transparent
    /// fragments writing depth cuts rotation-dependent holes in overlapping
    /// blended shapes.</summary>
    public bool IsDecal { get; set; }
    /// <summary>The vertex colour's alpha is a tree's wind-sway weight (SLSF2_Tree_Anim), not
    /// opacity: the shader keeps the vertex RGB but leaves alpha to the texture.</summary>
    public bool VertexAlphaIsWindWeight { get; set; }

    /// <summary>Material alpha (BSLightingShaderProperty.alpha). &lt; 1 marks a
    /// genuinely translucent material, which keeps depth-write off in the blend
    /// pass even when <see cref="DepthWrite"/> is set — mirroring NifSkope's
    /// <c>translucent</c> test. Defaults 1.0 (opaque).</summary>
    public float MaterialAlpha { get; set; } = 1f;

    /// <summary>SrcBlend factor for the alpha-blend pass, as a Bethesda enum
    /// index (0=ONE, 1=ZERO, 2=SRC_COLOR, ..., 6=SRC_ALPHA, 7=INV_SRC_ALPHA, ...).
    /// Read from NiAlphaProperty.flags bits 1-4 by NifMeshBuilder; mapped to
    /// OpenTK BlendingFactor at draw time. Default 6 (SRC_ALPHA) for shapes
    /// without an alpha property.</summary>
    public int SrcBlendIndex { get; set; } = 6;

    /// <summary>DstBlend factor for the alpha-blend pass (same Bethesda enum
    /// as SrcBlend; bits 5-8 of NiAlphaProperty.flags). Default 7
    /// (INV_SRC_ALPHA) for standard "over" transparency. replacer-style wet-eye
    /// outer cornea ships with 0 (ONE) so it composites additively over the
    /// iris underneath.</summary>
    public int DstBlendIndex { get; set; } = 7;

    /// <summary>True when the source shape's BSLightingShaderProperty is
    /// type BSLSP_HAIRTINT. Used elsewhere for hair-color tinting.</summary>
    public bool IsHairTintShader { get; set; }
    public bool IsRendering { get; set; } = true;
    public bool IsEye { get; set; }

    /// <summary>Host-owned visibility switch, independent of <see cref="IsRendering"/> (which the
    /// missing-texture cull owns), so a host can hide and re-show a shape (e.g. one occluded by
    /// another) without clobbering the cull state. Render passes gate on <see cref="ShouldRender"/>,
    /// which combines the two.</summary>
    public bool IsHiddenByHost { get; set; }

    /// <summary>Master visibility gate for all render passes: visible only when
    /// not culled (<see cref="IsRendering"/>) and not hidden by the host
    /// (<see cref="IsHiddenByHost"/>).</summary>
    public bool ShouldRender => IsRendering && !IsHiddenByHost;

    /// <summary>Opaque per-mesh data a host attaches (e.g. slot-occupancy bookkeeping). The
    /// renderer never reads it.</summary>
    public object? HostData { get; set; }

    /// <summary>Optional per-mesh placement (for example a reference's position, rotation and
    /// scale in a cell), applied before the renderer's shared model matrix. Null (the default)
    /// means the vertices are already in scene space, as for an assembled character.</summary>
    public OpenTK.Mathematics.Matrix4? ModelTransform { get; set; }

    // When true, the wireframe overlay pass draws this mesh's edges on top of
    // the solid surface. Hosts toggle this per mesh to outline selected shapes
    // over the solid surface.
    public bool ShowWireframe { get; set; } = false;

    // When true, the mesh is SKIPPED in the solid passes (Pass 0/1/2) and
    // rendered ONLY as a wireframe (in the missing-texture wireframe color).
    // Set by the host when an alpha-tested or alpha-blended shape's diffuse
    // texture couldn't be decoded — without the diffuse alpha channel the
    // discard threshold is undefined, so the shape would render as a flat
    // white billboard. Wireframe-only is a clearer "something's missing"
    // visual cue and lines up with the host's missing-texture overlay.
    public bool RenderAsWireframeFallback { get; set; } = false;

    // Per-mesh wireframe color, overriding both group colors
    // (GlRenderer.WireframeColor / MissingTextureWireframeColor) for this mesh.
    // Null = use the group color as before.
    //
    // Lets a host draw a mesh edges-only in its own colour, reusing
    // RenderAsWireframeFallback's draw behavior -- skip the solid passes, draw
    // edges only -- without the missing-texture colour, which specifically
    // means "this shape's diffuse failed to decode". A mesh drawn in that green
    // would read as a texture error rather than as an intentional outline.
    public Vector3? WireframeColorOverride { get; set; }

    // Eye-space depth bias in world units, applied in basic.vert by sliding each vertex
    // along its view ray: positive pushes the surface away from the camera, negative pulls
    // it closer. Screen position is unchanged; only the depth test moves. 0 = off.
    // Useful as a match tolerance when comparing two coincident meshes, so surfaces
    // that coincide (within the bias) stay untinted instead of z-fighting.
    public float DepthBias { get; set; }

    // Hidden-surface ("x-ray") tint. When set, the renderer draws this mesh a second time
    // after the blend pass, with depth test GREATER and no depth writes, in this flat tint
    // at HiddenSurfaceAlpha -- i.e. it paints only the parts of the mesh that lie BEHIND
    // what is already in the depth buffer (the primary model), shifted by
    // HiddenSurfaceDepthBias. Null = no such pass. A host can use it to show where
    // one mesh lies inside another.
    public Vector3? HiddenSurfaceTint { get; set; }
    public float HiddenSurfaceAlpha { get; set; } = 0.5f;
    public float HiddenSurfaceDepthBias { get; set; }

    // Material properties
    public float AlphaThreshold { get; set; }
    public float GreyscaleToPaletteScale { get; set; } = 1f;
    public Vector3 TintColor { get; set; } = Vector3.One;
    // Default glossiness / specular tuned for skin (the common case for a mesh whose NIF
    // carries no explicit values). The
    // pre-2.5.9 defaults (80f / 1f) produced overly tight, plastic-looking
    // highlights on faces when the source NIF didn't carry explicit shader
    // values — most modded face NIFs DO provide their own values, so this
    // only changes the fallback for shapes that were rendering glossy by
    // accident. Skin in real photography is a low-gloss material with
    // broad, soft highlights.
    public float MaterialGlossiness { get; set; } = 30f;
    public float MaterialSpecularStrength { get; set; } = 0.2f;
    public Vector3 SpecularColor { get; set; } = Vector3.One;
    public float RimlightPower { get; set; } = 2f;
    // SSS rolloff bumped 0.3 → 0.6 so subsurface contribution is visible
    // on most face shapes (was so subtle it read as plasticky).
    public float SubsurfaceRolloff { get; set; } = 0.6f;
    public Vector3 EmissiveColor { get; set; }
    public float EmissiveMultiple { get; set; }
    public Vector2 UvScale { get; set; } = Vector2.One;
    public Vector2 UvOffset { get; set; }
    public float EnvMapScale { get; set; } = 1f;
    public float EyeCubemapScale { get; set; } = 1f;

    /// <summary>True only for BSLSP_EYE (shader type 16) shapes: the engine
    /// scales their cubemap term by <see cref="EyeCubemapScale"/> instead of
    /// <see cref="EnvMapScale"/>. Deliberately NOT tied to <see cref="IsEye"/> —
    /// semantic eyes can be authored as BSLSP_ENVMAP (FoxGlove Auri), and
    /// those take the plain envMapScale in-game like any other envmap shape.</summary>
    public bool UseEyeCubemapScale { get; set; }

    // Environment map textures
    public int EnvMapTexture { get; set; }
    public int EnvMaskTexture { get; set; }
    public bool HasEnvironmentMap { get; set; }
    public bool HasEnvMask { get; set; }
    /// <summary>True when EnvMapTexture is a 2D sphere-map fallback rather
    /// than a real GL_TEXTURE_CUBE_MAP. Set by the loader when the source
    /// DDS is missing the cubemap flag (mod-shipped panoramic envmap). The
    /// shader uses this flag to switch between cube sampling and the legacy
    /// spherical UV math.</summary>
    public bool IsEnvMap2D { get; set; }

    // Detail map
    public int DetailTexture { get; set; }
    public bool HasDetailMap { get; set; }

    // Glow map (NIF slot 2 on non-skin shaders, SLSF2_Glow_Map): modulates
    // the emissive term per texel in basic.frag (bound on texture unit 12).
    public int GlowTexture { get; set; }
    public bool HasGlowMap { get; set; }

    // Per-shape texture visibility toggles (for context menu)
    public bool DiffuseEnabled { get; set; } = true;
    public bool NormalEnabled { get; set; } = true;
    public bool SkinEnabled { get; set; } = true;
    public bool SpecularEnabled { get; set; } = true;
    public bool FaceTintEnabled { get; set; } = true;
    public bool DetailEnabled { get; set; } = true;
    public bool EnvMapEnabled { get; set; } = true;
    public bool EmissiveEnabled { get; set; } = true;
    public bool TintColorEnabled { get; set; } = true;

    // CPU-side geometry for ray-based hit testing
    public System.Numerics.Vector3[]? CpuPositions { get; set; }
    public int[]? CpuIndices { get; set; }

    private System.Numerics.Vector3? _localCenter;

    /// <summary>Model-local centroid of the shape's vertices, computed once from
    /// <see cref="CpuPositions"/> and cached. Used by the alpha-blend pass to
    /// sort shapes back-to-front by camera distance so overlapping transparent
    /// surfaces composite in the correct order. Returns the origin if no CPU
    /// positions are present.</summary>
    public System.Numerics.Vector3 LocalCenter
    {
        get
        {
            if (_localCenter == null)
            {
                var c = System.Numerics.Vector3.Zero;
                var p = CpuPositions;
                if (p != null && p.Length > 0)
                {
                    for (int i = 0; i < p.Length; i++) c += p[i];
                    c /= p.Length;
                }
                _localCenter = c;
            }
            return _localCenter.Value;
        }
    }

    /// <summary>Drops the cached <see cref="LocalCenter"/> so the next access
    /// recomputes from <see cref="CpuPositions"/>. For hosts that rewrite vertex
    /// data in place after install — without this the alpha-pass distance sort
    /// keeps sorting by the pre-rewrite centroid. Additive; existing callers are
    /// unaffected.</summary>
    public void InvalidateLocalCenter() => _localCenter = null;

    // CPU-side per-vertex skin weights. Both arrays are flat with 4 entries per
    // vertex (CpuBoneIndices[vi*4 + k] is the k-th bone for vertex vi, with
    // CpuBoneWeights[vi*4 + k] the corresponding weight). Populated from the
    // SkinningInfo when the BuiltMesh has one; null for unskinned shapes.
    // Lets hosts query which bones drive each vertex.
    public int[]? CpuBoneIndices { get; set; }
    public float[]? CpuBoneWeights { get; set; }

    // Metadata
    public string ShapeName { get; set; } = "";
    public string BodyPart { get; set; } = "";
    public bool IsPrimaryHeadShape { get; set; }

    /// <summary>True when this shape's BSLightingShaderProperty had
    /// shaderType = 4 (BSLSP_FACE). Drives the optional debug "apply
    /// QNAM tint to face" path in basic.frag — the path is gated at
    /// render time so the toggle can flip without re-loading the scene.</summary>
    public bool IsFaceShape { get; set; }

    /// <summary>True when this shape's BSLightingShaderProperty had
    /// shaderType = 4 (BSLSP_FACE) or 5 (BSLSP_SKINTINT). Identifies
    /// "skin shapes" (face + body + hands + feet) for the optional
    /// host-tunable saturation boost in basic.frag, which compensates
    /// for downstream desaturation that washes distinctive race skin tones
    /// toward neutral. Hair / eyes / brows are excluded
    /// since they don't share the desaturation symptom.</summary>
    public bool IsSkinShape { get; set; }

    /// <summary>NIF-side <c>skinTintAlpha</c> field on
    /// BSLightingShaderProperty. Always 0.0f in vanilla and most-modder
    /// authoring; surfaced so the SkinTintAlpha-weighted operator in the
    /// debug face-tint path can multiply against it (when 0, the operator
    /// produces no visible tint, which is itself a useful test point).</summary>
    public float SkinTintAlpha { get; set; }

    /// <summary>True when this is a face shape (ShaderType 4) whose
    /// BSLightingShaderProperty has the SLSF1_Facegen_Detail_Map flag
    /// set but slot 3 of its BSShaderTextureSet is empty. Empirically
    /// correlates with mod-replacer faces that produce a face/body seam
    /// under the default overlay-FaceTint blend (Brynjolf, Aia Arria,
    /// Angeline Morrard from Ordinary People). Drives the optional
    /// "force multiply on empty detail" debug toggle in basic.frag.</summary>
    public bool IsFaceWithEmptyDetailSlot { get; set; }

    // Source metadata for the hover tooltip. These are display-only and have
    // no effect on rendering; the host populates them at load time.
    public AssetSource? MeshSource { get; set; }
    public System.Collections.Generic.List<(string SlotLabel, AssetSource Source)> TextureSources { get; } = new();

    private const int STRIDE = 18; // floats per vertex
    private bool _disposed;

    /// <summary>True once <see cref="Dispose"/> has released this mesh's GL objects.</summary>
    public bool IsDisposed => _disposed;

    /// <summary>
    /// Creates the VAO/VBO/EBO from interleaved vertex data and indices.
    /// </summary>
    public void Upload(float[] vertexData, int[] indices)
    {
        IndexCount = indices.Length;

        Vao = GL.GenVertexArray();
        Vbo = GL.GenBuffer();
        Ebo = GL.GenBuffer();

        GL.BindVertexArray(Vao);

        GL.BindBuffer(BufferTarget.ArrayBuffer, Vbo);
        GL.BufferData(BufferTarget.ArrayBuffer, vertexData.Length * sizeof(float),
            vertexData, BufferUsageHint.DynamicDraw);

        GL.BindBuffer(BufferTarget.ElementArrayBuffer, Ebo);
        GL.BufferData(BufferTarget.ElementArrayBuffer, indices.Length * sizeof(int),
            indices, BufferUsageHint.StaticDraw);

        int stride = STRIDE * sizeof(float);
        int offset = 0;

        // location 0: position (vec3)
        GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, stride, offset);
        GL.EnableVertexAttribArray(0);
        offset += 3 * sizeof(float);

        // location 1: normal (vec3)
        GL.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, stride, offset);
        GL.EnableVertexAttribArray(1);
        offset += 3 * sizeof(float);

        // location 2: texcoord (vec2)
        GL.VertexAttribPointer(2, 2, VertexAttribPointerType.Float, false, stride, offset);
        GL.EnableVertexAttribArray(2);
        offset += 2 * sizeof(float);

        // location 3: vertex color (vec4)
        GL.VertexAttribPointer(3, 4, VertexAttribPointerType.Float, false, stride, offset);
        GL.EnableVertexAttribArray(3);
        offset += 4 * sizeof(float);

        // location 4: tangent (vec3)
        GL.VertexAttribPointer(4, 3, VertexAttribPointerType.Float, false, stride, offset);
        GL.EnableVertexAttribArray(4);
        offset += 3 * sizeof(float);

        // location 5: bitangent (vec3)
        GL.VertexAttribPointer(5, 3, VertexAttribPointerType.Float, false, stride, offset);
        GL.EnableVertexAttribArray(5);

        GL.BindVertexArray(0);
    }

    /// <summary>
    /// Terrain only: uploads <see cref="World.TerrainQuadrant.MaxLayers"/> layer weights per vertex
    /// into a second buffer on this mesh's VAO, as two vec4 attributes at locations 6 and 7. Call
    /// after <see cref="Upload"/>.
    /// </summary>
    public void UploadLayerWeights(float[] weights)
    {
        const int perVertex = World.TerrainQuadrant.MaxLayers;
        if (LayerWeightVbo == 0) LayerWeightVbo = GL.GenBuffer();
        GL.BindVertexArray(Vao);
        GL.BindBuffer(BufferTarget.ArrayBuffer, LayerWeightVbo);
        GL.BufferData(BufferTarget.ArrayBuffer, weights.Length * sizeof(float), weights, BufferUsageHint.StaticDraw);
        int stride = perVertex * sizeof(float);
        GL.VertexAttribPointer(6, 4, VertexAttribPointerType.Float, false, stride, 0);
        GL.EnableVertexAttribArray(6);
        GL.VertexAttribPointer(7, 4, VertexAttribPointerType.Float, false, stride, 4 * sizeof(float));
        GL.EnableVertexAttribArray(7);
        GL.BindVertexArray(0);
    }

    /// <summary>
    /// Re-uploads vertex data (e.g. after a CPU-side deformation). Preserves the same VAO/VBO.
    /// </summary>
    public void UpdateVertexData(float[] vertexData)
    {
        GL.BindBuffer(BufferTarget.ArrayBuffer, Vbo);
        GL.BufferSubData(BufferTarget.ArrayBuffer, IntPtr.Zero,
            vertexData.Length * sizeof(float), vertexData);
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            GL.DeleteVertexArray(Vao);
            GL.DeleteBuffer(Vbo);
            GL.DeleteBuffer(Ebo);
            if (LayerWeightVbo != 0) GL.DeleteBuffer(LayerWeightVbo);
            _disposed = true;
        }
    }
}
