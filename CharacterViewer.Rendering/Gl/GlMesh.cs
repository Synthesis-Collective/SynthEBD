using System;
using System.Numerics;
using OpenTK.Graphics.OpenGL4;

namespace CharacterViewer.Rendering;

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
    /// (INV_SRC_ALPHA) for standard "over" transparency. UBE-style wet-eye
    /// outer cornea ships with 0 (ONE) so it composites additively over the
    /// iris underneath.</summary>
    public int DstBlendIndex { get; set; } = 7;

    /// <summary>True when the source shape's BSLightingShaderProperty is
    /// type BSLSP_HAIRTINT. Used elsewhere for hair-color tinting.</summary>
    public bool IsHairTintShader { get; set; }
    public bool IsRendering { get; set; } = true;
    public bool IsEye { get; set; }

    // ── Biped-slot tracking + occupancy/hiding (mesh-override channel) ────────
    // Each rendered shape carries the biped-object slots it occupies so the
    // renderer can resolve "headgear hides hair" / "clothing hides body"
    // visibility from slot collisions rather than from body-part strings. See
    // RENDERING_PIPELINE.md "Mesh-override channel". Base shapes get their slots
    // assigned from their body-part label at install time; mesh-override shapes
    // get theirs from MeshOverride.BipedSlots.

    /// <summary>Bitmask of biped-object slots this shape occupies, encoded as
    /// the asset-pack <c>(BipedObjectFlag)N</c> bits (<c>1 &lt;&lt; (slot-30)</c>).
    /// 0 for shapes with no slot association (e.g. FaceGen accessories).</summary>
    public int BipedSlots { get; set; }

    /// <summary>Bitmask of slots whose lower-priority occupants this shape
    /// hides. Non-zero only for mesh-override shapes that should occlude what
    /// they cover (armor over body, headgear over hair). Base shapes leave this
    /// 0 — the base body never hides anything.</summary>
    public int HidesSlots { get; set; }

    /// <summary>Slot-occupancy precedence: a shape can only be hidden by another
    /// shape of strictly higher priority. 0 = skin / base (the body, an
    /// auxiliary skin mesh), 1 = armor, 2 = headgear. Keeps a priority-0
    /// auxiliary mesh from being hidden by the priority-0 body, and lets
    /// armor/headgear occlude.</summary>
    public int SlotDrawPriority { get; set; }

    /// <summary>Non-null for shapes synthesized by
    /// <see cref="VM_CharacterViewer.ApplyMeshOverrides"/> (the MeshOverride.Key
    /// they came from); null for base NPC shapes. Lets a re-applied override set
    /// find and tear down the shapes the previous set created.</summary>
    public string? OverrideKey { get; set; }

    /// <summary>True when this shape is currently occluded by a higher-priority
    /// override that <see cref="HidesSlots"/> one of its <see cref="BipedSlots"/>.
    /// Kept separate from <see cref="IsRendering"/> (which the missing-texture
    /// cull owns) so slot-hiding can be recomputed on every override re-apply
    /// without clobbering the cull state. Render passes gate on
    /// <see cref="ShouldRender"/>, which ANDs the two.</summary>
    public bool HiddenBySlotOccupancy { get; set; }

    /// <summary>Master visibility gate for all render passes: visible only when
    /// not culled (<see cref="IsRendering"/>) and not occluded by slot occupancy
    /// (<see cref="HiddenBySlotOccupancy"/>).</summary>
    public bool ShouldRender => IsRendering && !HiddenBySlotOccupancy;

    // When true, the wireframe overlay pass draws this mesh's edges on top of
    // the solid surface. Used by the BodySlide classifier for key-vertex
    // assignment; toggled per-mesh so non-body shapes (head, hair, outfit)
    // stay solid.
    public bool ShowWireframe { get; set; } = false;

    // When true, the mesh is SKIPPED in the solid passes (Pass 0/1/2) and
    // rendered ONLY as a wireframe (in the missing-texture wireframe color).
    // Set by the host when an alpha-tested or alpha-blended shape's diffuse
    // texture couldn't be decoded — without the diffuse alpha channel the
    // discard threshold is undefined, so the shape would render as a flat
    // white billboard. Wireframe-only is a clearer "something's missing"
    // visual cue and lines up with the host's missing-texture overlay.
    public bool RenderAsWireframeFallback { get; set; } = false;

    // Material properties
    public float AlphaThreshold { get; set; }
    public float GreyscaleToPaletteScale { get; set; } = 1f;
    public Vector3 TintColor { get; set; } = Vector3.One;
    // Default glossiness / specular tuned for portrait skin (2.5.9+). The
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

    // CPU-side per-vertex skin weights. Both arrays are flat with 4 entries per
    // vertex (CpuBoneIndices[vi*4 + k] is the k-th bone for vertex vi, with
    // CpuBoneWeights[vi*4 + k] the corresponding weight). Populated from the
    // SkinningInfo when the BuiltMesh has one; null for unskinned shapes.
    // Consumed by the bone-transition Criterion on the SynthEBD side to find
    // anatomical seams (e.g. armpit = torso/arm bone boundary).
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
    /// for downstream desaturation that washes Imperials pale, Redguards
    /// Mediterranean, and Orcs olive. Hair / eyes / brows are excluded
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
    // no effect on rendering; VM_CharacterViewer populates them at load time.
    public AssetSource? MeshSource { get; set; }
    public System.Collections.Generic.List<(string SlotLabel, AssetSource Source)> TextureSources { get; } = new();

    private const int STRIDE = 18; // floats per vertex
    private bool _disposed;

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
    /// Re-uploads vertex data (e.g. after BodySlide deformation). Preserves the same VAO/VBO.
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
            _disposed = true;
        }
    }
}
