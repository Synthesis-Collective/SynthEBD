using System;
using System.Collections.Generic;
using System.IO;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;

namespace CharacterViewer.Rendering;

/// <summary>
/// Core OpenGL renderer for the character viewer. Manages the shader program,
/// draws all meshes with proper material uniforms, and handles lighting.
/// </summary>
public class GlRenderer : IDisposable
{
    private GlShaderProgram? _shader;
    private GlShaderProgram? _debugShader;
    private GlShaderProgram? _wireframeShader;
    private GlShaderProgram? _shadowShader;
    private int _debugVao;

    // Shadow-mapping resources (CharacterViewer.Rendering 2.5.10+). Created
    // lazily on first Render() with EnableShadows=true so hosts that don't
    // opt in pay zero GPU memory cost. The depth texture is sampled by
    // basic.frag's sampler2DShadow with hardware-accelerated bilinear PCF
    // plus a 3x3 manual kernel for soft penumbra.
    private const int ShadowMapSize = 2048;
    private int _shadowFbo = -1;
    private int _shadowDepthTex = -1;
    private Matrix4 _lightViewProj = Matrix4.Identity;

    // SSAO resources (CharacterViewer.Rendering 2.5.11+). The pipeline:
    //   1. Depth + normal prepass: render opaque + alpha-test geometry
    //      with _depthOnlyShader. Writes depth to _depthPrepassDepthTex
    //      and view-space normals (encoded *0.5+0.5 into RGB8) to
    //      _depthPrepassNormalTex. The normal G-buffer is what avoids
    //      faceting - reconstructing the normal via cross(dFdx, dFdy)
    //      yields a flat per-triangle normal and the triangulation
    //      pops on smooth surfaces.
    //   2. SSAO compute: full-screen quad reads _depthPrepassDepthTex +
    //      _depthPrepassNormalTex + _ssaoNoiseTex, computes per-pixel
    //      hemispheric occlusion with _ssaoSampleKernel, writes a
    //      single-channel R8 result into _ssaoFbo's color texture.
    //   3. SSAO blur: averages a 4x4 neighborhood to cancel the noise
    //      tile period, writes to _ssaoBlurTex.
    //   4. Main pass: basic.frag samples _ssaoBlurTex via u_ssaoMap and
    //      multiplies into the diffuse + SSS terms.
    // FBOs are sized to the current viewport and re-created when the
    // viewport size changes (matches the host's MSAA FBO lifecycle).
    private GlShaderProgram? _depthOnlyShader;
    private GlShaderProgram? _ssaoShader;
    private GlShaderProgram? _ssaoBlurShader;
    private int _depthPrepassFbo = -1;
    private int _depthPrepassDepthTex = -1;
    private int _depthPrepassNormalTex = -1;
    private int _ssaoFbo = -1;
    private int _ssaoTex = -1;
    private int _ssaoBlurFbo = -1;
    private int _ssaoBlurTex = -1;
    private int _ssaoNoiseTex = -1;
    private int _ssaoFullscreenVao = -1;
    private (int Width, int Height) _ssaoFboSize;
    private Vector3[]? _ssaoSampleKernel;
    private const int SsaoKernelSize = 16;
    private const int SsaoNoiseSize = 4;
    private int _debugVbo;
    private readonly List<GlMesh> _meshes = new();
    private bool _initialized;
    private bool _disposed;
    // 1x1 black cubemap bound to texture unit 6 whenever the active mesh has
    // no envmap or its envmap is the 2D sphere-map fallback. Keeps unit 6's
    // samplerCube binding "complete" so drivers don't warn about sampling
    // from an incomplete cubemap target even when the shader's
    // has_environment_map / is_env_map_2d gates skip the sample.
    private int _defaultBlackCubemap = -1;

    /// <summary>RGB color used for the wireframe overlay pass. Bright cyan by
    /// default so edges read clearly against both skin and clothing.</summary>
    public Vector3 WireframeColor { get; set; } = new Vector3(0.2f, 1.0f, 0.9f);

    /// <summary>Color used for shapes flagged as
    /// <see cref="GlMesh.RenderAsWireframeFallback"/> (alpha shapes whose
    /// diffuse couldn't be decoded). Default green — distinct from the
    /// teal BodySlide-classifier wireframe color so the two overlays
    /// don't blur together when both are active on the same scene.</summary>
    public Vector3 MissingTextureWireframeColor { get; set; } = new Vector3(0.0f, 1.0f, 0.0f);

    /// <summary>When true, the fragment shader applies an ACES filmic
    /// tone-mapper plus a mild saturation boost at the end of the pixel
    /// pipeline, and the GL framebuffer is treated as sRGB so the linear
    /// lighting result is gamma-encoded once on write. Compresses HDR
    /// highlights, lifts shadows into perceptual space, and adds the
    /// contrast / warmth that makes the output read as a portrait
    /// rather than a flat render. Off: legacy linear-to-display output
    /// (pre-2.5.9 look). Hosts mirror their settings toggle here before
    /// each Render call.</summary>
    public bool EnableToneMapping { get; set; } = false;

    /// <summary>When true, <see cref="Render"/> runs an extra depth-only
    /// pass from the key directional light's POV before the main passes,
    /// then samples the resulting shadow map with PCF in basic.frag to
    /// cast real shadows from brow / nose / hair onto the face. Off:
    /// legacy occlusion-free directional lighting (pre-2.5.10 look).
    /// Hosts mirror their settings toggle here before each Render call.</summary>
    public bool EnableShadows { get; set; } = false;

    /// <summary>When true, <see cref="Render"/> runs a depth pre-pass +
    /// SSAO post-process before the main passes, then samples the AO
    /// texture per-fragment in basic.frag and multiplies into the
    /// diffuse term to darken concave crevices. Off: no AO modulation
    /// (pre-2.5.11 look). Hosts mirror their settings toggle here.</summary>
    public bool EnableAmbientOcclusion { get; set; } = false;

    /// <summary>SSAO sample radius in world units. Read by ComputeSsao
    /// each render so toggling at runtime is effective on the next
    /// frame. Defaults match the hardcoded value from 2.5.11.</summary>
    public float SsaoRadius { get; set; } = 4.0f;
    /// <summary>SSAO depth-comparison bias in world units.</summary>
    public float SsaoBias { get; set; } = 0.05f;
    /// <summary>SSAO power-curve exponent.</summary>
    public float SsaoIntensity { get; set; } = 1.5f;

    /// <summary>Eye catch-light toggle (2.5.13+). When true, basic.frag
    /// adds a tight high-glossiness specular spot from the key light
    /// for shapes flagged <see cref="GlMesh.IsEye"/>.</summary>
    public bool EnableEyeCatchlight { get; set; } = false;

    /// <summary>SSS strength multiplier (2.5.14+). 0 disables the
    /// corrected SSS pipeline (matches pre-2.5.14 visual when paired
    /// with the v5 stamped hash). 1.0 = honest SSS at source rolloff
    /// values. Higher = more pronounced warm-flesh look.</summary>
    public float SubsurfaceStrength { get; set; } = 0f;

    /// <summary>Skin-only saturation multiplier applied post-tint,
    /// pre-lighting. 1.0 is no-op. &gt;1 boosts chroma on shapes flagged
    /// <see cref="GlMesh.IsSkinShape"/>; hair, eyes, and brows are
    /// excluded. Compensates for downstream desaturation that washes
    /// race-distinguishing skin character toward neutral.</summary>
    public float SkinSaturationBoost { get; set; } = 1.0f;

    /// <summary>Vignette inner radius in NDC units (2.5.15+). The
    /// circular zone of distance &lt;= radius from screen center is
    /// unaffected; falloff smoothsteps from this radius out to the
    /// corner (sqrt(2)). Lower = vignette closes in toward center;
    /// higher = vignette stays wide. Folded under the tone-mapping
    /// path in basic.frag, so the legacy linear pipeline stays
    /// untouched when tone-mapping is off.</summary>
    public float VignetteRadius { get; set; } = 0.7f;

    /// <summary>Vignette darkening strength (2.5.15+). 0 = off (no
    /// vignette darkening anywhere); 1.0 = corners go to black. The
    /// pre-2.5.15 hardcoded behavior is approximately reproduced by
    /// Radius=0.7, Intensity=0.3.</summary>
    public float VignetteIntensity { get; set; } = 0f;

    // ── Skin-tint debug toggles (interactive, runtime-flippable) ─────
    // Promoted from shader-side const to renderer property so the
    // operator can be flipped at runtime without re-loading the scene
    // or recompiling the shader. The user changes the operator from the
    // CharacterViewerRenderPanel and we push it as a uniform every
    // application so the user can flip operators at runtime and
    // visually compare against in-game / NifSkope.
    /// <summary>When true, ShaderType==4 face shapes also receive the
    /// QNAM tint (currently only ShaderType==5 body shapes do). Useful
    /// for testing the "engine applies QNAM to both face and body"
    /// hypothesis.</summary>
    public bool SkinTintApplyToFace { get; set; } = false;

    /// <summary>Operator used by basic.frag to combine the QNAM tint
    /// with the diffuse:
    /// 0 = multiply (legacy production default),
    /// 1 = overlay,
    /// 2 = linear-space multiply (gamma-decode -&gt; multiply -&gt; re-encode),
    /// 3 = gamma-aware multiply (pow(albedo, 1/tint) approximation),
    /// 4 = lerp(albedo, albedo*tint, <see cref="SkinTintLerpStrength"/>),
    /// 5 = lerp weighted by NIF's per-shape skinTintAlpha (0 in our samples),
    /// 6 = Pegtop soft-light + engine body color-shift constant.
    /// Default 6: matches the engine's GetFacegenRGBTintBaseColor per
    /// Community Shaders' Lighting.hlsl replacement shader.</summary>
    public int SkinTintOperator { get; set; } = 6;

    /// <summary>Strength used by SkinTintOperator == 4 (lerp).
    /// 0 = no tint; 1 = full multiply.</summary>
    public float SkinTintLerpStrength { get; set; } = 0.5f;

    /// <summary>Debug override for the per-shape vertex-color multiply
    /// branch in basic.frag. 0 = auto (use the per-shape
    /// <c>HasVertexColors</c> flag, production behavior); 1 = force ON
    /// (apply the multiply for every shape — note: shapes without vertex
    /// color data upload (1,1,1,1) per vertex, so the multiply is
    /// visually inert on those, useful as a sanity-check on the
    /// (1,1,1,1) fallback); 2 = force OFF (skip the multiply for every
    /// shape — useful for diagnosing whether vanilla NPCs' face vertex
    /// colors are visibly modulating the diffuse).</summary>
    public int VertexColorMultiplyMode { get; set; } = 0;

    /// <summary>FaceTint blend operator selector -- runtime version of
    /// the previous <c>FACE_TINT_MODE</c> shader constant.
    /// 0 = Auto (MSN gets overlay, non-MSN gets multiply,
    /// matches NifSkope's sk_msn / sk_default split);
    /// 1 = Always overlay;
    /// 2 = Always multiply;
    /// 3 = Always skip;
    /// 4 = Pegtop soft-light (engine-faithful per Community Shaders'
    /// GetFacegenBaseColor source).
    /// Default 4: pairs with <see cref="SkinTintOperator"/> = 6 and
    /// <see cref="UseEngineStyleDetailMap"/> = true to reproduce the
    /// engine's face composition pipeline.</summary>
    public int FaceTintMode { get; set; } = 4;

    /// <summary>Experimental: when true, face shapes (ShaderType 4)
    /// whose <c>SLSF1_Facegen_Detail_Map</c> flag is set but whose
    /// BSShaderTextureSet slot 3 is empty are rendered with the FaceTint
    /// operator forced to multiply (overriding <see cref="FaceTintMode"/>
    /// for those specific meshes). Empirically resolves the seam on
    /// modder faces that omit slot 3 (Brynjolf / Aia / Angeline) without
    /// affecting NPCs whose slot 3 is populated. Toggle is reactive —
    /// the per-mesh "empty slot 3" flag is set at load time, the
    /// override decision is made each frame in basic.frag.</summary>
    public bool FaceTintMultiplyOnEmptyDetail { get; set; } = false;

    /// <summary>When true, the slot-3 detail map is applied to face
    /// shapes the way the actual Skyrim engine does (per Community
    /// Shaders' Lighting.hlsl GetFacegenBaseColor): a direct multiply
    /// of the post-FaceTint result by
    /// <c>3.984375 * ((1/255, 0, 1/255) + sampledDetail)</c>, applied AFTER
    /// the FaceTint blend rather than as a Photoshop overlay before it.
    /// Default true: combined with FaceTintMode==4 (Pegtop) and
    /// SkinTintOperator==6 (Pegtop with engine constant), reproduces the
    /// engine's face / body composition math.</summary>
    public bool UseEngineStyleDetailMap { get; set; } = true;

    /// <summary>Experimental: when true, face shapes whose slot 3 is
    /// empty get <c>textures\actors\character\male\BlankDetailmap.dds</c>
    /// substituted at load time, exercising the detail-map blend with
    /// the standard Bethesda CK fallback (mid-grey). Tests whether the
    /// engine substitutes a default detail texture when slot 3 is empty.
    /// Changing this toggle fires <see cref="VM_CharacterViewer.ReloadRequested"/>
    /// because texture binding is decided at scene-build time.</summary>
    public bool UseBlankDetailFallback { get; set; } = false;

    /// <summary>World-space (pre-ModelScale) positions where a sphere gizmo
    /// should be drawn. Used by the BodySlide classifier's key-vertex picking
    /// workflow. Positions are in the same space as <see cref="GlMesh.CpuPositions"/>
    /// so they track the character through ModelScale changes.</summary>
    public List<Vector3> KeyVertexMarkers { get; } = new();

    /// <summary>RGB color for the key-vertex marker spheres.</summary>
    public Vector3 KeyVertexMarkerColor { get; set; } = new Vector3(1.0f, 0.38f, 0.15f);

    /// <summary>Indices into <see cref="KeyVertexMarkers"/> that should render in
    /// <see cref="KeyVertexMarkerSelectedColor"/> instead of the default — driven by the
    /// pick-info ListBox selection in the viewer so the user can identify which marker
    /// corresponds to the clicked row.</summary>
    public HashSet<int> SelectedKeyVertexMarkerIndices { get; } = new();

    /// <summary>RGB color for selected key-vertex marker spheres (overrides
    /// <see cref="KeyVertexMarkerColor"/> per-marker when the index appears in
    /// <see cref="SelectedKeyVertexMarkerIndices"/>).</summary>
    public Vector3 KeyVertexMarkerSelectedColor { get; set; } = new Vector3(0.25f, 1.0f, 0.35f);

    /// <summary>Bounding-box-resolved markers — rendered by the same path as
    /// <see cref="KeyVertexMarkers"/> but in a distinguishable color so the user can see
    /// which of their named key vertices are static picks vs. AABB re-scans. Owned by the
    /// BodyTypeProfile editor; repopulated on every <c>RefreshMeasurementValues</c>.</summary>
    public List<Vector3> BoxResolvedMarkers { get; } = new();

    /// <summary>RGB color for BB-resolved marker spheres. Yellow matches the Pick Box UI accent.</summary>
    public Vector3 BoxResolvedMarkerColor { get; set; } = new Vector3(1.0f, 0.80f, 0.25f);

    /// <summary>Transient preview marker — one position, owned by the BodyTypeProfile editor
    /// to show the currently-selected KeyVertex row's location without populating
    /// <see cref="KeyVertexMarkers"/> (which would add a row to the picks ListBox). Cleared
    /// when the user deselects or switches profile. Renders in
    /// <see cref="PreviewKeyVertexMarkerColor"/> via the same DrawMarkerList path.</summary>
    public List<Vector3> PreviewKeyVertexMarkers { get; } = new();

    /// <summary>RGB color for the preview marker. Defaults to the same green as the
    /// selected-pick color so the visual language is consistent — "this dot is the row
    /// you have highlighted in the editor".</summary>
    public Vector3 PreviewKeyVertexMarkerColor { get; set; } = new Vector3(0.25f, 1.0f, 0.35f);

    /// <summary>Pending-box pick preview — the vertex (or vertices, for mirror criteria)
    /// that the current pending box + criterion would resolve to if the user clicked Confirm
    /// right now. Updated live as the user tweaks min/max coords or swaps the criterion,
    /// cleared when the pending box is dismissed. Purple to distinguish from the orange
    /// committed picks and yellow BB-resolved markers.</summary>
    public List<Vector3> PreviewPickMarkers { get; } = new();

    /// <summary>RGB color for the pending-box pick preview spheres. Purple — sits visually
    /// apart from orange (picks), yellow (BB-resolved), and green (selected/preview-row).</summary>
    public Vector3 PreviewPickMarkerColor { get; set; } = new Vector3(0.78f, 0.30f, 1.0f);

    /// <summary>Region-overlay cap-loop vertex markers — the resolved boundary-loop vertices of
    /// the RegionVolume region currently selected in the BodyTypeProfile editor. Rendered by the
    /// same <see cref="DrawMarkerList"/> path as the key-vertex markers; the matching loop edges
    /// are drawn from <see cref="RegionOverlayLines"/>. Owned by the editor; cleared on deselect.</summary>
    public List<Vector3> RegionCapMarkers { get; } = new();

    /// <summary>RGB color for the region cap-loop markers. Cyan — distinct from orange (picks),
    /// yellow (BB-resolved), green (preview-row), and purple (pending-box preview).</summary>
    public Vector3 RegionCapMarkerColor { get; set; } = new Vector3(0.20f, 0.90f, 1.0f);

    /// <summary>Region "Solid" view-mode geometry: the selected region's closed surface (patch +
    /// caps) as interleaved triangle vertices, 6 floats each (position.xyz + normal.xyz), in the same
    /// pre-ModelScale local space as the markers. Drawn lit (<c>u_shaded=1</c>) with depth test off in
    /// the marker pass, so the region reads as a solid magenta object visible through the body from any
    /// angle — reusing the always-on-top overlay path instead of making the body transparent. Empty in
    /// End-Cap mode. Owned by the editor; rebuilt per preset/weight like the cap-loop overlay.</summary>
    public List<float> RegionSolidTriangles { get; } = new();

    /// <summary>RGB color for the Solid-mode region surface. Magenta — distinct from the cyan cap
    /// markers/edges drawn on top of it.</summary>
    public Vector3 RegionSolidColor { get; set; } = new Vector3(1.0f, 0.10f, 0.85f);

    /// <summary>World-space radius of each marker sphere before ModelScale is
    /// applied. Small enough not to obscure neighbouring vertices on a dense
    /// classifier mesh, while still readable at typical viewer zooms.</summary>
    public float KeyVertexMarkerRadius { get; set; } = 0.275f;

    /// <summary>
    /// Line-segment overlays used by the BodyTypeProfile editor to visualize the
    /// currently-selected measurement. Positions are in the same pre-ModelScale
    /// mesh-local space as <see cref="KeyVertexMarkers"/> so they track ModelScale.
    /// </summary>
    public List<MeasurementLineSegment> MeasurementLines { get; } = new();

    /// <summary>Region-overlay cap-loop edges — the boundary-loop edges of the RegionVolume region
    /// currently selected in the BodyTypeProfile editor. Same <see cref="MeasurementLineSegment"/>
    /// shape and same draw path (<see cref="DrawMeasurementLines"/>) as <see cref="MeasurementLines"/>,
    /// kept in a separate channel so the region overlay and the measurement overlay don't clobber
    /// each other. Drawn at the thick contour width. Owned by the editor; cleared on deselect.</summary>
    public List<MeasurementLineSegment> RegionOverlayLines { get; } = new();

    /// <summary>Region "Solid" view-mode wireframe — the patch's triangle edges, drawn THIN (so the
    /// dense bump mesh reads as a wireframe rather than a fat tangle) over the magenta solid. Separate
    /// channel from <see cref="RegionOverlayLines"/> precisely because of the different line width.
    /// Owned by the editor; populated only in Solid mode, cleared otherwise.</summary>
    public List<MeasurementLineSegment> RegionWireLines { get; } = new();

    /// <summary>Line width (px) for <see cref="RegionWireLines"/>. Thin — a true wireframe.</summary>
    public float RegionWireWidth { get; set; } = 1.25f;

    public struct MeasurementLineSegment
    {
        public Vector3 A;
        public Vector3 B;
        public Vector3 Color;

        /// <summary>Optional tooltip label shown when the user hovers near this segment in
        /// the WPF viewer (see <c>VM_CharacterViewer.HitTestMeasurementLine</c> +
        /// <c>UC_CharacterViewer.HoverTimer_Tick</c>). Null/empty when the segment is
        /// untagged — those segments still render but are ignored by the hover hit-test.
        /// Renderer side never reads this field; it's purely transit metadata between the
        /// caller that builds the overlay (e.g. <c>VM_BodyTypeProfileEditor.RefreshMeasurementHighlight</c>)
        /// and the hover handler.</summary>
        public string? Label;
    }

    /// <summary>When true, renders arrow gizmos showing each directional light's shining
    /// direction (from source to model) and magnitude. Arrow tips anchor at
    /// <c>OrbitCamera.Target</c> and lengths scale with <c>OrbitCamera.Distance</c>
    /// so the gizmos stay framed regardless of close-up vs. wide camera setups.</summary>
    public bool ShowKeyLightVisualization { get; set; } = false;

    /// <summary>Index of the light currently selected for editing (1=key, 2=fill, 3=rim,
    /// 0=none). Selected arrows render with a highlighted appearance.</summary>
    public int SelectedLightIndex { get; set; } = 0;

    // Cached world-space arrow segments for picking. Indexed by light slot.
    private struct ArrowSegment { public Vector3 Tail; public Vector3 Tip; public float Radius; public bool Valid; }
    private readonly ArrowSegment[] _arrowSegments = new ArrowSegment[5];

    /// <summary>Retrieves the last rendered arrow's world-space tail/tip/radius for picking.</summary>
    public bool TryGetArrowSegment(int slot, out Vector3 tail, out Vector3 tip, out float radius)
    {
        tail = tip = Vector3.Zero; radius = 0f;
        if (slot < 0 || slot >= _arrowSegments.Length) return false;
        var s = _arrowSegments[slot];
        if (!s.Valid) return false;
        tail = s.Tail; tip = s.Tip; radius = s.Radius;
        return true;
    }

    // Lighting state — up to 5 lights
    public struct LightData
    {
        public int Type;       // 0=disabled, 1=ambient, 2=directional
        // Surface-to-light vector in WORLD space. Transformed to view space
        // once per frame (see Render) before upload to the shader, so lights
        // stay anchored to world coordinates while the orbit camera moves.
        public Vector3 Direction;
        public Vector3 Color;
        public float Intensity;
    }
    public LightData[] Lights { get; } = new LightData[5];

    // Cached view-space light directions (P5). Each frame we'd otherwise
    // multiply Lights[i].Direction by the view rotation and normalize for all
    // 5 slots. Static cameras with static lights make that work redundant —
    // we only recompute when either the view matrix or a light's world-space
    // direction/type changes.
    private readonly Vector3[] _cachedViewSpaceLightDirs = new Vector3[5];
    private Matrix4 _cachedLightUploadView;
    private readonly Vector3[] _cachedLightWorldDirs = new Vector3[5];
    private readonly int[] _cachedLightTypes = new int[5];
    private bool _hasCachedLightDirs;

    // Per-light arrow colors for the visualization gizmo (key=yellow, fill=cyan, rim=magenta).
    private static readonly Vector3[] _arrowColors =
    {
        new(0.2f, 0.2f, 0.2f),  // 0 ambient (unused for arrows)
        new(1.0f, 0.9f, 0.2f),  // 1 key   — yellow
        new(0.3f, 0.9f, 1.0f),  // 2 fill  — cyan
        new(1.0f, 0.3f, 0.9f),  // 3 rim   — magenta
        new(0.6f, 1.0f, 0.4f),  // 4 extra — green
    };

    /// <summary>Backlight color for hair rimlight.</summary>
    public Vector3 BacklightColor { get; set; } = new Vector3(0.6f, 0.5f, 0.4f);

    /// <summary>Background clear color (RGB 0-1).</summary>
    public Vector3 ClearColor { get; set; } = new Vector3(0.41f, 0.41f, 0.41f); // DimGray

    /// <summary>Uniform scale applied to the world-space mesh geometry via the
    /// model matrix. Drives the NPC-height multiplier — 1.0 is identity; 1.1
    /// is a 10% taller character. Meshes are already in world space, so this
    /// is applied in the model matrix rather than per-mesh.</summary>
    public float ModelScale { get; set; } = 1.0f;

    public GlRenderer()
    {
        // Default lighting: ambient + key + fill + rim
        Lights[0] = new LightData { Type = 1, Color = Vector3.One, Intensity = 0.2f };
        Lights[1] = new LightData { Type = 2, Direction = new Vector3(-0.4f, -0.7f, 0.6f), Color = Vector3.One, Intensity = 1.0f };
        Lights[2] = new LightData { Type = 2, Direction = new Vector3(0.5f, 0.3f, 1.0f), Color = Vector3.One, Intensity = 0.38f };
        Lights[3] = new LightData { Type = 2, Direction = new Vector3(0f, 0.5f, -1.0f), Color = Vector3.One, Intensity = 0.25f };
    }

    /// <summary>
    /// Initializes OpenGL state and compiles shaders. Must be called after GL context is available.
    /// </summary>
    public void Initialize(string shaderDirectory)
    {
        if (_initialized) return;

        // SHADER EDITING WARNING: basic.vert/basic.frag must be pure ASCII,
        // even inside comments. Non-ASCII bytes (em-dash —, curly quotes “ ”,
        // ellipsis …, NBSP, etc.) make the GLSL compiler emit a misleading
        // "unexpected $end at token <EOF>" with no line number. See
        // GlShaderProgram class remarks. Keep this note across refactors.
        string vertPath = Path.Combine(shaderDirectory, "basic.vert");
        string fragPath = Path.Combine(shaderDirectory, "basic.frag");
        _shader = GlShaderProgram.LoadFromFiles(vertPath, fragPath);

        // Set texture unit bindings (these don't change)
        _shader.Use();
        _shader.SetInt("texture_diffuse", 0);
        _shader.SetInt("texture_normal", 1);
        _shader.SetInt("texture_skin", 2);
        _shader.SetInt("texture_specular", 3);
        _shader.SetInt("texture_face_tint", 4);
        _shader.SetInt("texture_detail", 5);
        // Unit 6 carries the cubemap environment map (samplerCube). Unit 10
        // carries the 2D sphere-map fallback (sampler2D) used for mod-shipped
        // panoramic envmaps that aren't packaged as DDS cubemaps. The shader
        // chooses between them per-mesh via the is_env_map_2d flag.
        _shader.SetInt("texture_envmap", 6);
        _shader.SetInt("texture_envmap_2d", 10);
        _shader.SetInt("texture_envmask", 7);
        _shader.SetInt("u_shadowMap", 8);

        GL.Enable(EnableCap.DepthTest);
        GL.Enable(EnableCap.CullFace);
        GL.CullFace(CullFaceMode.Back);

        // Smooth filtering across cubemap face seams. Implicit on most modern
        // drivers in core 3.2+, but enable explicitly so the cubemap edge
        // interpolation is consistent across vendors.
        GL.Enable(EnableCap.TextureCubeMapSeamless);

        // 1x1 opaque-black fallback cubemap. Bound to unit 6 whenever the
        // active mesh has no real cubemap envmap, so the samplerCube uniform
        // is always backed by a complete cube target.
        _defaultBlackCubemap = GL.GenTexture();
        GL.BindTexture(TextureTarget.TextureCubeMap, _defaultBlackCubemap);
        byte[] blackPixel = { 0, 0, 0, 255 };
        var faceTargets = new[]
        {
            TextureTarget.TextureCubeMapPositiveX,
            TextureTarget.TextureCubeMapNegativeX,
            TextureTarget.TextureCubeMapPositiveY,
            TextureTarget.TextureCubeMapNegativeY,
            TextureTarget.TextureCubeMapPositiveZ,
            TextureTarget.TextureCubeMapNegativeZ,
        };
        foreach (var target in faceTargets)
        {
            GL.TexImage2D(target, 0, PixelInternalFormat.Rgba8,
                1, 1, 0, PixelFormat.Bgra, PixelType.UnsignedByte, blackPixel);
        }
        GL.TexParameter(TextureTarget.TextureCubeMap, TextureParameterName.TextureMinFilter,
            (int)TextureMinFilter.Linear);
        GL.TexParameter(TextureTarget.TextureCubeMap, TextureParameterName.TextureMagFilter,
            (int)TextureMagFilter.Linear);
        GL.TexParameter(TextureTarget.TextureCubeMap, TextureParameterName.TextureWrapS,
            (int)TextureWrapMode.ClampToEdge);
        GL.TexParameter(TextureTarget.TextureCubeMap, TextureParameterName.TextureWrapT,
            (int)TextureWrapMode.ClampToEdge);
        GL.TexParameter(TextureTarget.TextureCubeMap, TextureParameterName.TextureWrapR,
            (int)TextureWrapMode.ClampToEdge);

        // Debug (line-based) shader for the key-light arrow gizmo
        string debugVertPath = Path.Combine(shaderDirectory, "debug.vert");
        string debugFragPath = Path.Combine(shaderDirectory, "debug.frag");
        _debugShader = GlShaderProgram.LoadFromFiles(debugVertPath, debugFragPath);

        // Wireframe shader — reads position (location 0) from the standard mesh
        // VAO and draws a flat-colored edge overlay on top of the solid mesh.
        string wireVertPath = Path.Combine(shaderDirectory, "wireframe.vert");
        string wireFragPath = Path.Combine(shaderDirectory, "wireframe.frag");
        _wireframeShader = GlShaderProgram.LoadFromFiles(wireVertPath, wireFragPath);

        // Shadow-depth shader — reads position + texcoords (locations 0, 2)
        // from the standard mesh VAO, transforms by u_lightViewProj, and
        // emits depth-only output (no color attachment on the shadow FBO).
        // Alpha-test path samples the diffuse texture and discards
        // transparent texels so hair / brow strands cast strand-shaped
        // shadows instead of solid card-shaped occluders.
        string shadowVertPath = Path.Combine(shaderDirectory, "shadow_depth.vert");
        string shadowFragPath = Path.Combine(shaderDirectory, "shadow_depth.frag");
        _shadowShader = GlShaderProgram.LoadFromFiles(shadowVertPath, shadowFragPath);
        _shadowShader.Use();
        _shadowShader.SetInt("texture_diffuse", 0);

        // Depth-only shader for the SSAO depth pre-pass. Same alpha-test
        // logic as shadow_depth but renders from the camera's POV
        // (separate u_view + u_projection uniforms instead of a
        // pre-multiplied light view-proj matrix).
        string depthVertPath = Path.Combine(shaderDirectory, "depth_only.vert");
        string depthFragPath = Path.Combine(shaderDirectory, "depth_only.frag");
        _depthOnlyShader = GlShaderProgram.LoadFromFiles(depthVertPath, depthFragPath);
        _depthOnlyShader.Use();
        _depthOnlyShader.SetInt("texture_diffuse", 0);

        // SSAO post-process shader. Reads u_depthTex (unit 0) +
        // u_normalTex (unit 1) + u_noiseTex (unit 2), writes per-pixel
        // occlusion factor. The normal G-buffer comes from the depth
        // prepass so the hemisphere orients off smooth interpolated
        // vertex normals (not faceted dFdx/dFdy reconstruction).
        string fullVertPath = Path.Combine(shaderDirectory, "fullscreen.vert");
        string ssaoFragPath = Path.Combine(shaderDirectory, "ssao.frag");
        _ssaoShader = GlShaderProgram.LoadFromFiles(fullVertPath, ssaoFragPath);
        _ssaoShader.Use();
        _ssaoShader.SetInt("u_depthTex", 0);
        _ssaoShader.SetInt("u_normalTex", 1);
        _ssaoShader.SetInt("u_noiseTex", 2);

        // SSAO blur post-pass. Reads the raw SSAO texture (unit 0),
        // averages a 4x4 neighborhood per pixel to cancel the noise
        // tile pattern, writes the smoothed result. Always runs
        // between ComputeSsao and the main pass when SSAO is on.
        string ssaoBlurPath = Path.Combine(shaderDirectory, "ssao_blur.frag");
        _ssaoBlurShader = GlShaderProgram.LoadFromFiles(fullVertPath, ssaoBlurPath);
        _ssaoBlurShader.Use();
        _ssaoBlurShader.SetInt("u_ssaoTex", 0);

        // basic.frag samples the AO map via texture unit 9 (8 is the
        // shadow map, 0..7 are the standard material slots).
        _shader.Use();
        _shader.SetInt("u_ssaoMap", 9);

        // Pre-compute the hemispheric sample kernel + a 4x4 noise tile.
        // Done once at init since neither depends on the scene.
        BuildSsaoKernel();
        BuildSsaoNoiseTexture();

        _debugVao = GL.GenVertexArray();
        _debugVbo = GL.GenBuffer();
        GL.BindVertexArray(_debugVao);
        GL.BindBuffer(BufferTarget.ArrayBuffer, _debugVbo);
        // Vertex layout: position(3) + normal(3) = 6 floats per vertex.
        int stride = 6 * sizeof(float);
        GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, stride, 0);
        GL.EnableVertexAttribArray(0);
        GL.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, stride, 3 * sizeof(float));
        GL.EnableVertexAttribArray(1);
        GL.BindVertexArray(0);

        // Fullscreen-quad VAO for post-process passes (SSAO etc.). The
        // fullscreen.vert generates positions from gl_VertexID alone, so
        // no VBO is needed - we just need a non-zero VAO bound for the
        // glDrawArrays(TRIANGLES, 0, 3) call to be valid in core profile.
        _ssaoFullscreenVao = GL.GenVertexArray();

        _initialized = true;
    }

    public void AddMesh(GlMesh mesh)
    {
        _meshes.Add(mesh);
    }

    public void RemoveMesh(GlMesh mesh)
    {
        _meshes.Remove(mesh);
    }

    public void ClearMeshes()
    {
        foreach (var mesh in _meshes)
            mesh.Dispose();
        _meshes.Clear();
    }

    public IReadOnlyList<GlMesh> Meshes => _meshes;

    /// <summary>Optional diagnostic sink for one-shot GL-state dumps emitted at
    /// the top of <see cref="Render"/>. Hosts wire this to their per-render
    /// capture file so renderer-side state (GL_FRAMEBUFFER_SRGB, viewport,
    /// bound FBO, color-attachment format) lands alongside the host-side
    /// resolver / lighting / material trace. Null = no emission.</summary>
    public Action<string>? DiagnosticLog { get; set; }

    /// <summary>
    /// Renders all meshes with the given camera matrices.
    /// </summary>
    public void Render(OrbitCamera camera, int viewportWidth, int viewportHeight)
    {
        if (!_initialized || _shader == null) return;
        if (viewportWidth <= 0 || viewportHeight <= 0) return;

        EmitGlStateDiagnostic(viewportWidth, viewportHeight);

        // Camera matrices (computed up here so the pre-passes share them
        // with the main pass below).
        var modelMat = Matrix4.CreateScale(ModelScale);
        float aspectPre = (float)viewportWidth / viewportHeight;
        var viewMatPre = camera.GetViewMatrix();
        var projMatPre = camera.GetProjectionMatrix(aspectPre);

        // Pre-passes (shadow + SSAO) run BEFORE the main FBO is rebound,
        // so we capture the host's bound FBO once and restore at the end.
        bool needsHostFboRestore = EnableShadows || EnableAmbientOcclusion;
        int hostFbo = 0;
        if (needsHostFboRestore)
        {
            GL.GetInteger(GetPName.DrawFramebufferBinding, out hostFbo);
        }

        // Shadow depth pre-pass.
        if (EnableShadows)
        {
            ComputeLightViewProj();
            RenderShadowDepthPass(ref modelMat);
        }

        // SSAO depth pre-pass + post-process. Two passes: first renders
        // depth from the camera POV (so basic.frag's screen-space SSAO
        // sample matches the visible silhouette), then runs the SSAO
        // shader to compute the per-pixel occlusion factor.
        if (EnableAmbientOcclusion)
        {
            RenderDepthPrepass(ref modelMat, ref viewMatPre, ref projMatPre,
                viewportWidth, viewportHeight);
            ComputeSsao(ref projMatPre, viewportWidth, viewportHeight);
            BlurSsao(viewportWidth, viewportHeight);
        }

        if (needsHostFboRestore)
        {
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, hostFbo);
        }

        // Toggle sRGB framebuffer encoding alongside the tone-map shader
        // path. The tone-mapper outputs values in linear space; with
        // FRAMEBUFFER_SRGB enabled the GL driver gamma-encodes them on
        // write. Without the framebuffer flag the linear output would
        // display too dark (mid-tones crushed). Pairing must be
        // deterministic - never enable one without the other.
        if (EnableToneMapping) GL.Enable(EnableCap.FramebufferSrgb);
        else GL.Disable(EnableCap.FramebufferSrgb);

        GL.Viewport(0, 0, viewportWidth, viewportHeight);
        GL.ClearColor(ClearColor.X, ClearColor.Y, ClearColor.Z, 1f);
        GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

        _shader.Use();
        _shader.SetBool("u_enableToneMapping", EnableToneMapping);
        _shader.SetBool("u_enableShadows", EnableShadows);
        _shader.SetBool("u_enableAO", EnableAmbientOcclusion);
        _shader.SetBool("u_enableEyeCatchlight", EnableEyeCatchlight);
        _shader.SetFloat("u_subsurfaceStrength", SubsurfaceStrength);
        _shader.SetFloat("u_skinSaturationBoost", SkinSaturationBoost);
        // u_screenSize is consumed by both the SSAO sample lookup AND
        // the tone-mapping vignette (2.5.15+), so push it on every
        // frame regardless of which toggles are on.
        _shader.SetVector2("u_screenSize",
            (float)viewportWidth, (float)viewportHeight);
        _shader.SetFloat("u_vignetteRadius", VignetteRadius);
        _shader.SetFloat("u_vignetteIntensity", VignetteIntensity);
        _shader.SetBool("u_skinTintApplyToFace", SkinTintApplyToFace);
        _shader.SetInt("u_skinTintOperator", SkinTintOperator);
        _shader.SetFloat("u_skinTintLerpStrength", SkinTintLerpStrength);
        _shader.SetInt("u_vertexColorMode", VertexColorMultiplyMode);
        _shader.SetInt("u_faceTintMode", FaceTintMode);
        _shader.SetBool("u_faceTintMultiplyOnEmptyDetail", FaceTintMultiplyOnEmptyDetail);
        _shader.SetBool("u_detailMapEngineStyle", UseEngineStyleDetailMap);
        if (EnableShadows && _shadowDepthTex != -1)
        {
            _shader.SetMatrix4("u_lightViewProj", ref _lightViewProj);
            GL.ActiveTexture(TextureUnit.Texture8);
            GL.BindTexture(TextureTarget.Texture2D, _shadowDepthTex);
            GL.ActiveTexture(TextureUnit.Texture0);
        }
        if (EnableAmbientOcclusion && _ssaoBlurTex != -1)
        {
            // Bind the BLURRED AO map (not the raw _ssaoTex) so the main
            // shader doesn't see the noise-tile pattern.
            GL.ActiveTexture(TextureUnit.Texture9);
            GL.BindTexture(TextureTarget.Texture2D, _ssaoBlurTex);
            GL.ActiveTexture(TextureUnit.Texture0);
        }

        // Camera matrices
        float aspect = (float)viewportWidth / viewportHeight;
        var view = camera.GetViewMatrix();
        var projection = camera.GetProjectionMatrix(aspect);
        // modelMat was already computed at the top of Render() so the
        // shadow pass could share it.
        ref var model = ref modelMat;

        _shader.SetMatrix4("u_model", ref model);
        _shader.SetMatrix4("u_view", ref view);
        _shader.SetMatrix4("u_projection", ref projection);

        // World-space camera position for the cubemap-envmap reflection
        // direction (reflect(-(cameraPos - worldPos), worldNormal)). Pushed
        // once per frame; the orbit camera already exposes this so we don't
        // need to invert the view matrix.
        var camPos = camera.GetEyePosition();
        _shader.SetVector3("u_cameraPos", camPos.X, camPos.Y, camPos.Z);

        // Set light uniforms — transform directions to view space
        EnsureViewSpaceLightDirs(ref view);
        for (int i = 0; i < 5; i++)
        {
            string prefix = "lights[" + i + "].";
            _shader.SetInt(prefix + "type", Lights[i].Type);
            if (Lights[i].Type == 2)
            {
                var viewDir = _cachedViewSpaceLightDirs[i];
                _shader.SetVector3(prefix + "direction", viewDir.X, viewDir.Y, viewDir.Z);
            }
            _shader.SetVector3(prefix + "color", Lights[i].Color.X, Lights[i].Color.Y, Lights[i].Color.Z);
            _shader.SetFloat(prefix + "intensity", Lights[i].Intensity);
        }

        _shader.SetVector3("u_backlightColor", BacklightColor.X, BacklightColor.Y, BacklightColor.Z);

        // Pass 0: Opaque meshes (no alpha test or blend)
        GL.Disable(EnableCap.Blend);
        GL.DepthMask(true);
        foreach (var mesh in _meshes)
        {
            if (!mesh.IsRendering) continue;
            if (mesh.RenderAsWireframeFallback) continue;
            if (mesh.UseAlphaTest || mesh.HasAlphaBlend) continue;
            DrawMesh(mesh);
        }

        // Pass 1: Alpha-tested-only meshes (discard in shader, depth writes ON,
        // no blend). Cutout shapes whose NiAlphaProperty has the alpha-test bit
        // but NOT the alpha-blend bit. Smooth edges come from MSAA on the
        // private FBO (4× samples) — no SAMPLE_ALPHA_TO_COVERAGE here, since
        // it interprets sub-1.0 diffuse alpha as partial coverage and would
        // wash out shapes whose alpha encodes non-cutout data (e.g. vanilla
        // Khajiit / Argonian heads have alpha < 1 across the whole face).
        foreach (var mesh in _meshes)
        {
            if (!mesh.IsRendering) continue;
            if (mesh.RenderAsWireframeFallback) continue;
            if (!mesh.UseAlphaTest || mesh.HasAlphaBlend) continue;
            DrawMesh(mesh);
        }

        // Pass 2: Alpha-blended meshes (transparency). Depth writes OFF for
        // correct back-to-front compositing. Shapes with the alpha-blend bit
        // come here regardless of whether the alpha-test bit is also set —
        // the per-shape `use_alpha_test` uniform (set in DrawMesh) handles
        // any sub-threshold discard. This matches Portrait Creator's
        // classification ("alphaBlend wins"), and gives hair / beard / brow
        // edges the soft fade that comes from blending raw alpha values
        // with the surface beneath, instead of a hard cutout.
        GL.Enable(EnableCap.Blend);
        GL.DepthMask(false);
        // Per-mesh src/dst blend factors honored from NiAlphaProperty.
        // The vast majority of alpha-blended actor shapes use SRC_ALPHA /
        // INV_SRC_ALPHA (standard "over" transparency), but some authored
        // overlays -- notably the UBE-style wet-eye outer cornea -- ship
        // with SRC_ALPHA / ONE (additive), so a near-black diffuse adds
        // nothing to the iris underneath while bright catchlight pixels
        // add brightness. Honoring per-mesh factors removes the need for
        // any special-case shader hack to handle the cornea.
        int curSrc = -1, curDst = -1;
        foreach (var mesh in _meshes)
        {
            if (!mesh.IsRendering) continue;
            if (mesh.RenderAsWireframeFallback) continue;
            if (!mesh.HasAlphaBlend) continue;

            if (mesh.SrcBlendIndex != curSrc || mesh.DstBlendIndex != curDst)
            {
                GL.BlendFunc(MapBethesdaBlendFactor(mesh.SrcBlendIndex),
                             MapBethesdaBlendFactor(mesh.DstBlendIndex));
                curSrc = mesh.SrcBlendIndex;
                curDst = mesh.DstBlendIndex;
            }
            DrawMesh(mesh);
        }
        GL.Disable(EnableCap.Blend);
        GL.DepthMask(true);

        // Wireframe overlay: drawn after alpha-blend so its lines layer on top
        // of the solid surface. Uses glPolygonOffset to avoid z-fighting.
        DrawWireframeOverlay(ref model, ref view, ref projection);

        // Key-vertex marker gizmos (BodySlide classifier). Drawn with depth
        // test off so markers on the far side of the model remain visible to
        // the user while assigning key vertices.
        DrawKeyVertexMarkers(ref view, ref projection);

        // Measurement lines (BodyTypeProfile editor). Drawn after markers so the
        // connection between the two endpoint gizmos reads clearly.
        DrawMeasurementLines(ref view, ref projection);

        // Overlay: directional-light direction arrows. Drawn last with depth test off
        // so they behave like gizmos (always visible through the model).
        if (ShowKeyLightVisualization)
            DrawDirectionalLightArrows(camera, ref view, ref projection);
    }

    /// <summary>
    /// Draws a small shaded sphere at each entry in <see cref="KeyVertexMarkers"/>.
    /// Reuses the debug shader (which already supports <c>u_shaded=1</c> lighting)
    /// and pre-multiplies the stored mesh-local positions by ModelScale so the
    /// markers track the character when height is adjusted.
    /// </summary>
    private void DrawKeyVertexMarkers(ref Matrix4 view, ref Matrix4 projection)
    {
        if (_debugShader == null) return;
        if (KeyVertexMarkers.Count == 0
            && BoxResolvedMarkers.Count == 0
            && PreviewKeyVertexMarkers.Count == 0
            && PreviewPickMarkers.Count == 0
            && RegionCapMarkers.Count == 0
            && RegionSolidTriangles.Count == 0) return;

        _debugShader.Use();
        _debugShader.SetMatrix4("u_view", ref view);
        _debugShader.SetMatrix4("u_projection", ref projection);
        _debugShader.SetFloat("u_shaded", 1f);

        GL.BindVertexArray(_debugVao);
        GL.BindBuffer(BufferTarget.ArrayBuffer, _debugVbo);

        bool depthWasEnabled = GL.IsEnabled(EnableCap.DepthTest);
        bool cullWasEnabled = GL.IsEnabled(EnableCap.CullFace);
        GL.Disable(EnableCap.DepthTest);
        GL.Enable(EnableCap.CullFace);
        GL.CullFace(CullFaceMode.Back);

        // Solid region surface first, so the cyan cap markers/edges layer on top of it. Lit
        // (u_shaded=1) + depth-off makes it read as a solid object visible through the body — the
        // same always-on-top trick the markers use, no transparency needed.
        DrawRegionSolid();

        DrawMarkerList(KeyVertexMarkers, KeyVertexMarkerColor,
            SelectedKeyVertexMarkerIndices, KeyVertexMarkerSelectedColor);
        DrawMarkerList(BoxResolvedMarkers, BoxResolvedMarkerColor);
        DrawMarkerList(PreviewKeyVertexMarkers, PreviewKeyVertexMarkerColor);
        DrawMarkerList(PreviewPickMarkers, PreviewPickMarkerColor);
        DrawMarkerList(RegionCapMarkers, RegionCapMarkerColor);

        _debugShader.SetFloat("u_shaded", 0f);
        if (depthWasEnabled) GL.Enable(EnableCap.DepthTest);
        if (!cullWasEnabled) GL.Disable(EnableCap.CullFace);
        GL.BindVertexArray(0);
    }

    /// <summary>Uploads + draws <see cref="RegionSolidTriangles"/> as a lit magenta solid through the
    /// debug VAO. Caller has already set u_shaded=1, bound the debug VAO/VBO, and configured depth-off
    /// + back-face cull. Positions are scaled by ModelScale (matching the marker/line paths) via a
    /// scratch copy so the stored mesh-local coords aren't mutated.</summary>
    private float[]? _regionSolidScratch;
    private void DrawRegionSolid()
    {
        int floats = RegionSolidTriangles.Count;
        if (floats < 18) return; // need at least one triangle (3 verts * 6 floats)

        var src = RegionSolidTriangles;
        var buf = _regionSolidScratch;
        if (buf == null || buf.Length < floats) buf = _regionSolidScratch = new float[floats];
        float s = ModelScale;
        for (int i = 0; i < floats; i += 6)
        {
            buf[i + 0] = src[i + 0] * s; buf[i + 1] = src[i + 1] * s; buf[i + 2] = src[i + 2] * s; // position
            buf[i + 3] = src[i + 3];     buf[i + 4] = src[i + 4];     buf[i + 5] = src[i + 5];     // normal (unscaled)
        }

        _debugShader!.SetVector3("u_color", RegionSolidColor.X, RegionSolidColor.Y, RegionSolidColor.Z);
        GL.BufferData(BufferTarget.ArrayBuffer, floats * sizeof(float), buf, BufferUsageHint.DynamicDraw);
        GL.DrawArrays(PrimitiveType.Triangles, 0, floats / 6);
    }

    private void DrawMarkerList(
        List<Vector3> markers, Vector3 color,
        HashSet<int>? selectedIndices = null, Vector3 selectedColor = default)
    {
        if (markers.Count == 0) return;

        var unit = GetUnitSphereMesh();
        var scratch = _sphereScratch ??= new float[unit.Length];
        float worldRadius = KeyVertexMarkerRadius * ModelScale;

        // Set the default color once; swap to the selected color only when a marker's
        // index lands in selectedIndices, and only emit the uniform update on transition.
        _debugShader!.SetVector3("u_color", color.X, color.Y, color.Z);
        bool currentlySelected = false;

        for (int i = 0; i < markers.Count; i++)
        {
            bool markerSelected = selectedIndices != null && selectedIndices.Contains(i);
            if (markerSelected != currentlySelected)
            {
                var c = markerSelected ? selectedColor : color;
                _debugShader.SetVector3("u_color", c.X, c.Y, c.Z);
                currentlySelected = markerSelected;
            }

            var worldCenter = markers[i] * ModelScale;
            for (int v = 0; v < unit.Length; v += 6)
            {
                scratch[v + 0] = worldCenter.X + unit[v + 0] * worldRadius;
                scratch[v + 1] = worldCenter.Y + unit[v + 1] * worldRadius;
                scratch[v + 2] = worldCenter.Z + unit[v + 2] * worldRadius;
                scratch[v + 3] = unit[v + 3];
                scratch[v + 4] = unit[v + 4];
                scratch[v + 5] = unit[v + 5];
            }
            GL.BufferData(BufferTarget.ArrayBuffer,
                scratch.Length * sizeof(float), scratch, BufferUsageHint.DynamicDraw);
            GL.DrawArrays(PrimitiveType.Triangles, 0, scratch.Length / 6);
        }
    }

    /// <summary>
    /// Draws flat-colored line segments from <see cref="MeasurementLines"/> through
    /// the debug VAO. Uses <c>u_shaded=0</c> so lighting doesn't tint the color, and
    /// disables depth test so the segment is always visible through the body.
    /// The debug VAO's layout is position(3) + normal(3) -- normals are ignored in
    /// flat mode but must be written to keep the stride consistent.
    /// </summary>
    private void DrawMeasurementLines(ref Matrix4 view, ref Matrix4 projection)
    {
        if (_debugShader == null) return;
        if (MeasurementLines.Count == 0 && RegionOverlayLines.Count == 0 && RegionWireLines.Count == 0) return;

        _debugShader.Use();
        _debugShader.SetMatrix4("u_view", ref view);
        _debugShader.SetMatrix4("u_projection", ref projection);
        _debugShader.SetFloat("u_shaded", 0f);

        GL.BindVertexArray(_debugVao);
        GL.BindBuffer(BufferTarget.ArrayBuffer, _debugVbo);

        bool depthWasEnabled = GL.IsEnabled(EnableCap.DepthTest);
        GL.Disable(EnableCap.DepthTest);

        // Two verts per line, 6 floats per vert (pos + unused normal).
        var buf = new float[12];

        // Thick channels (measurement overlay + region cap-loop contour). Thicker than the historical
        // 2.5 so the overlay reads at typical zoom levels against busy body textures; 4.5 stays under
        // the typical driver-clamped maximum (5–10 for aliased lines).
        GL.LineWidth(4.5f);
        DrawLineSegmentList(MeasurementLines, buf);
        DrawLineSegmentList(RegionOverlayLines, buf);

        // Thin channel: the Solid-mode region wireframe (dense triangle edges read as a wireframe).
        if (RegionWireLines.Count > 0)
        {
            GL.LineWidth(RegionWireWidth);
            DrawLineSegmentList(RegionWireLines, buf);
        }

        GL.LineWidth(1.0f);
        if (depthWasEnabled) GL.Enable(EnableCap.DepthTest);
        GL.BindVertexArray(0);
    }

    /// <summary>Uploads + draws each segment in <paramref name="lines"/> through the bound debug
    /// VAO/VBO. Caller sets shader, line width, and depth state and passes a reusable 12-float
    /// scratch buffer. Shared by the measurement-line and region cap-loop overlay channels.</summary>
    private void DrawLineSegmentList(List<MeasurementLineSegment> lines, float[] buf)
    {
        for (int i = 0; i < lines.Count; i++)
        {
            var seg = lines[i];
            var a = seg.A * ModelScale;
            var b = seg.B * ModelScale;

            buf[0] = a.X; buf[1] = a.Y; buf[2] = a.Z; buf[3] = 0f; buf[4] = 0f; buf[5] = 0f;
            buf[6] = b.X; buf[7] = b.Y; buf[8] = b.Z; buf[9] = 0f; buf[10] = 0f; buf[11] = 0f;

            _debugShader!.SetVector3("u_color", seg.Color.X, seg.Color.Y, seg.Color.Z);
            GL.BufferData(BufferTarget.ArrayBuffer, buf.Length * sizeof(float), buf, BufferUsageHint.DynamicDraw);
            GL.DrawArrays(PrimitiveType.Lines, 0, 2);
        }
    }

    /// <summary>Cached unit-sphere vertex data (pos.xyz + normal.xyz per vert;
    /// for a unit sphere the position equals the normal). Built once from a
    /// 2x-subdivided octahedron = 128 triangles.</summary>
    private static float[]? _unitSphereMesh;

    /// <summary>Reused per-marker scratch buffer for the scaled + translated
    /// sphere mesh, to avoid per-frame allocation in the render loop.</summary>
    private float[]? _sphereScratch;

    /// <summary>
    /// Returns the cached unit-sphere vertex array (128 CCW-from-outside triangles,
    /// 4608 floats = 128 * 3 * 6). Built lazily from a 2x-subdivided octahedron so
    /// each vertex lies on the unit sphere and its outward normal equals its position.
    /// </summary>
    private static float[] GetUnitSphereMesh()
    {
        if (_unitSphereMesh != null) return _unitSphereMesh;

        var top = new Vector3(0f,  1f, 0f);
        var bot = new Vector3(0f, -1f, 0f);
        var xp  = new Vector3( 1f, 0f,  0f);
        var xn  = new Vector3(-1f, 0f,  0f);
        var zp  = new Vector3( 0f, 0f,  1f);
        var zn  = new Vector3( 0f, 0f, -1f);

        // Seed octahedron: 8 triangles, wound CCW-from-outside so the Lambert
        // term in the debug shader lights the outside surface.
        var tris = new List<(Vector3 a, Vector3 b, Vector3 c)>(8)
        {
            (top, zp, xp), (top, xp, zn), (top, zn, xn), (top, xn, zp),
            (bot, xp, zp), (bot, zn, xp), (bot, xn, zn), (bot, zp, xn),
        };

        // Two levels of midpoint subdivision: 8 -> 32 -> 128 triangles.
        // Each new midpoint is re-normalized so it sits on the unit sphere.
        for (int s = 0; s < 2; s++)
        {
            var next = new List<(Vector3, Vector3, Vector3)>(tris.Count * 4);
            foreach (var (a, b, c) in tris)
            {
                var mab = Vector3.Normalize((a + b) * 0.5f);
                var mbc = Vector3.Normalize((b + c) * 0.5f);
                var mca = Vector3.Normalize((c + a) * 0.5f);
                next.Add((a, mab, mca));
                next.Add((mab, b, mbc));
                next.Add((mca, mbc, c));
                next.Add((mab, mbc, mca));
            }
            tris = next;
        }

        var data = new float[tris.Count * 3 * 6];
        int w = 0;
        foreach (var (a, b, c) in tris)
        {
            Write(data, ref w, a);
            Write(data, ref w, b);
            Write(data, ref w, c);
        }
        _unitSphereMesh = data;
        return data;

        static void Write(float[] buf, ref int w, Vector3 v)
        {
            buf[w++] = v.X; buf[w++] = v.Y; buf[w++] = v.Z;
            buf[w++] = v.X; buf[w++] = v.Y; buf[w++] = v.Z;
        }
    }

    /// <summary>Lazily creates the shadow FBO + depth texture sized
    /// <see cref="ShadowMapSize"/>. The depth texture is configured for
    /// hardware PCF: COMPARE_REF_TO_TEXTURE mode, LINEAR filter (gives
    /// 4-tap bilinear PCF on top of the manual 3x3 kernel in basic.frag
    /// for 36 effective samples). Border-clamped to 1.0 so any sample
    /// outside the shadow frustum reads as fully lit (avoids the dark
    /// halo around the shadow region).</summary>
    private void EnsureShadowFbo()
    {
        if (_shadowFbo != -1) return;

        _shadowDepthTex = GL.GenTexture();
        GL.BindTexture(TextureTarget.Texture2D, _shadowDepthTex);
        GL.TexImage2D(TextureTarget.Texture2D, 0,
            PixelInternalFormat.DepthComponent24,
            ShadowMapSize, ShadowMapSize, 0,
            PixelFormat.DepthComponent, PixelType.Float, IntPtr.Zero);
        GL.TexParameter(TextureTarget.Texture2D,
            TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
        GL.TexParameter(TextureTarget.Texture2D,
            TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
        GL.TexParameter(TextureTarget.Texture2D,
            TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToBorder);
        GL.TexParameter(TextureTarget.Texture2D,
            TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToBorder);
        var border = new[] { 1f, 1f, 1f, 1f };
        GL.TexParameter(TextureTarget.Texture2D,
            TextureParameterName.TextureBorderColor, border);
        GL.TexParameter(TextureTarget.Texture2D,
            TextureParameterName.TextureCompareMode, (int)TextureCompareMode.CompareRefToTexture);
        GL.TexParameter(TextureTarget.Texture2D,
            TextureParameterName.TextureCompareFunc, (int)All.Lequal);

        _shadowFbo = GL.GenFramebuffer();
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, _shadowFbo);
        GL.FramebufferTexture2D(FramebufferTarget.Framebuffer,
            FramebufferAttachment.DepthAttachment,
            TextureTarget.Texture2D, _shadowDepthTex, 0);
        // Depth-only FBO: no color attachments. Tell the driver explicitly
        // so it doesn't fail FBO completeness on drivers that require it.
        GL.DrawBuffer(DrawBufferMode.None);
        GL.ReadBuffer(ReadBufferMode.None);

        var status = GL.CheckFramebufferStatus(FramebufferTarget.Framebuffer);
        if (status != FramebufferErrorCode.FramebufferComplete)
        {
            throw new InvalidOperationException(
                "Shadow FBO incomplete: " + status + " (size=" + ShadowMapSize + ")");
        }
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
    }

    /// <summary>Computes the orthographic light-space view-projection matrix
    /// for the key directional light. Uses a fixed scene radius (300 world
    /// units around the camera target) which covers any reasonable Skyrim
    /// NPC + accessories at the heights the renderer is used at; avoiding
    /// a per-frame bbox walk keeps the cost negligible. The output is
    /// uploaded to <c>u_lightViewProj</c> on the main shader so
    /// <c>basic.frag</c>'s shadow lookup can transform world positions into
    /// the light's clip space.</summary>
    private void ComputeLightViewProj()
    {
        // Key light = index 1 (0 is ambient). LightData.Direction is the
        // *surface-to-light* vector (per the LightData comment), i.e. it
        // points FROM the surface TOWARD the light source. Photons travel
        // along -Direction. So the light's world-space POSITION is
        // sceneCenter + Direction * distance (NOT - Direction; that would
        // place the shadow camera on the opposite side of the model and
        // every front-facing fragment would read as occluded by the back
        // of the head).
        var lightDir = Vector3.Normalize(Lights[1].Direction);

        // Place the light "eye" along the surface-to-light direction far
        // enough that the whole scene fits inside the orthographic
        // frustum. Center on a Skyrim-NPC chest height (Y=85 pre-scale)
        // scaled by ModelScale.
        var sceneCenter = new Vector3(0f, 85f * ModelScale, 0f);
        float radius = 300f * ModelScale;
        var lightEye = sceneCenter + lightDir * radius * 1.5f;

        // Up vector: world up unless the light is shining straight down.
        var up = MathF.Abs(lightDir.Y) > 0.99f ? Vector3.UnitZ : Vector3.UnitY;
        var lightView = Matrix4.LookAt(lightEye, sceneCenter, up);
        var lightProj = Matrix4.CreateOrthographic(
            radius * 2.5f, radius * 2.5f,
            0.1f, radius * 4f);

        // Match the main render's matrix-multiply convention (basic.vert
        // does u_projection * u_view * pos with column-major shader
        // matrices): in OpenTK row-vector C# that's view * proj.
        _lightViewProj = lightView * lightProj;
    }

    /// <summary>Renders the scene's opaque + alpha-test geometry to the
    /// shadow depth texture from the key light's POV. Skips alpha-blend
    /// shapes (transparent geometry doesn't cast meaningful shadows) and
    /// wireframe-fallback shapes (no diffuse to alpha-test against).
    /// Caller must restore the previously bound framebuffer + viewport
    /// after this method returns.</summary>
    private void RenderShadowDepthPass(ref Matrix4 model)
    {
        if (_shadowShader == null) return;
        EnsureShadowFbo();

        GL.BindFramebuffer(FramebufferTarget.Framebuffer, _shadowFbo);
        GL.Viewport(0, 0, ShadowMapSize, ShadowMapSize);
        GL.Clear(ClearBufferMask.DepthBufferBit);

        // Front-face culling reduces self-shadowing acne (the back face of
        // the geometry casts the shadow, so the front face's depth is
        // strictly less and the bias has more room to work). Restore the
        // default at the end.
        GL.CullFace(CullFaceMode.Front);

        _shadowShader.Use();
        _shadowShader.SetMatrix4("u_model", ref model);
        _shadowShader.SetMatrix4("u_lightViewProj", ref _lightViewProj);

        foreach (var mesh in _meshes)
        {
            if (!mesh.IsRendering) continue;
            if (mesh.RenderAsWireframeFallback) continue;
            // Pure alpha-blend shapes (no alpha-test bit) don't cast useful
            // shadows; their cast would just be a soft amorphous blob.
            if (mesh.HasAlphaBlend && !mesh.UseAlphaTest) continue;
            // Eyes are inside the head — their cast shadow would always be
            // self-occluding noise. Skip.
            if (mesh.IsEye) continue;

            _shadowShader.SetBool("use_alpha_test", mesh.UseAlphaTest);
            _shadowShader.SetFloat("alpha_threshold", mesh.AlphaThreshold);
            if (mesh.UseAlphaTest)
            {
                GL.ActiveTexture(TextureUnit.Texture0);
                GL.BindTexture(TextureTarget.Texture2D, mesh.DiffuseTexture);
            }

            GL.BindVertexArray(mesh.Vao);
            GL.DrawElements(PrimitiveType.Triangles, mesh.IndexCount,
                DrawElementsType.UnsignedInt, 0);
        }

        GL.BindVertexArray(0);
        GL.CullFace(CullFaceMode.Back);
    }

    /// <summary>Builds a 16-sample hemispheric kernel of view-space
    /// offsets used by ssao.frag. Each offset is randomly oriented within
    /// the hemisphere centered on the surface normal (TBN reorients in
    /// the shader), with sample distances accelerated toward the origin
    /// (closer samples carry more weight). Computed once at init.</summary>
    private void BuildSsaoKernel()
    {
        var rng = new Random(1337); // Fixed seed: deterministic kernel.
        _ssaoSampleKernel = new Vector3[SsaoKernelSize];
        for (int i = 0; i < SsaoKernelSize; i++)
        {
            // Random vector in upper hemisphere (z >= 0). Normalized to
            // unit length, then rescaled with a quadratic falloff so the
            // first samples cluster near the origin.
            var v = new Vector3(
                (float)(rng.NextDouble() * 2.0 - 1.0),
                (float)(rng.NextDouble() * 2.0 - 1.0),
                (float)rng.NextDouble());
            v = Vector3.Normalize(v);
            v *= (float)rng.NextDouble();
            float scale = (float)i / SsaoKernelSize;
            // Lerp from 0.1 to 1.0 with a quadratic curve.
            scale = 0.1f + scale * scale * (1.0f - 0.1f);
            v *= scale;
            _ssaoSampleKernel[i] = v;
        }
    }

    /// <summary>Builds a small RGB tile of random tangent vectors used by
    /// ssao.frag to rotate the kernel per-pixel. The tile is repeat-tiled
    /// across the screen so neighboring pixels use different rotations,
    /// breaking up the banding a fixed kernel would otherwise produce.
    /// 4x4 is enough for the post-pass to look noisy rather than
    /// patterned; the host's MSAA + the inherent low-frequency nature of
    /// AO smooth the result.</summary>
    private void BuildSsaoNoiseTexture()
    {
        var rng = new Random(2718);
        var noise = new float[SsaoNoiseSize * SsaoNoiseSize * 3];
        for (int i = 0; i < SsaoNoiseSize * SsaoNoiseSize; i++)
        {
            // Tangent-space vector lies in the X-Y plane (z = 0); the
            // shader cross-products with the surface normal to produce
            // a perpendicular tangent.
            noise[i * 3 + 0] = (float)(rng.NextDouble() * 2.0 - 1.0);
            noise[i * 3 + 1] = (float)(rng.NextDouble() * 2.0 - 1.0);
            noise[i * 3 + 2] = 0f;
        }

        _ssaoNoiseTex = GL.GenTexture();
        GL.BindTexture(TextureTarget.Texture2D, _ssaoNoiseTex);
        GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgb16f,
            SsaoNoiseSize, SsaoNoiseSize, 0, PixelFormat.Rgb, PixelType.Float, noise);
        GL.TexParameter(TextureTarget.Texture2D,
            TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
        GL.TexParameter(TextureTarget.Texture2D,
            TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Nearest);
        GL.TexParameter(TextureTarget.Texture2D,
            TextureParameterName.TextureWrapS, (int)TextureWrapMode.Repeat);
        GL.TexParameter(TextureTarget.Texture2D,
            TextureParameterName.TextureWrapT, (int)TextureWrapMode.Repeat);
    }

    /// <summary>Lazily creates / resizes the depth+normal prepass FBO and
    /// the SSAO output FBO at the current viewport size. Same lifecycle
    /// as the host's MSAA FBO: re-allocated when the viewport size
    /// changes, otherwise reused across renders.</summary>
    private void EnsureSsaoFbos(int width, int height)
    {
        if (_ssaoFboSize == (width, height) && _ssaoFbo != -1) return;

        // Tear down existing resources before reallocating.
        if (_depthPrepassDepthTex != -1) { GL.DeleteTexture(_depthPrepassDepthTex); _depthPrepassDepthTex = -1; }
        if (_depthPrepassNormalTex != -1) { GL.DeleteTexture(_depthPrepassNormalTex); _depthPrepassNormalTex = -1; }
        if (_depthPrepassFbo != -1) { GL.DeleteFramebuffer(_depthPrepassFbo); _depthPrepassFbo = -1; }
        if (_ssaoTex != -1) { GL.DeleteTexture(_ssaoTex); _ssaoTex = -1; }
        if (_ssaoFbo != -1) { GL.DeleteFramebuffer(_ssaoFbo); _ssaoFbo = -1; }
        if (_ssaoBlurTex != -1) { GL.DeleteTexture(_ssaoBlurTex); _ssaoBlurTex = -1; }
        if (_ssaoBlurFbo != -1) { GL.DeleteFramebuffer(_ssaoBlurFbo); _ssaoBlurFbo = -1; }

        // Depth prepass texture: depth-only single-sample so SSAO can
        // sample it with bilinear filtering.
        _depthPrepassDepthTex = GL.GenTexture();
        GL.BindTexture(TextureTarget.Texture2D, _depthPrepassDepthTex);
        GL.TexImage2D(TextureTarget.Texture2D, 0,
            PixelInternalFormat.DepthComponent24,
            width, height, 0,
            PixelFormat.DepthComponent, PixelType.Float, IntPtr.Zero);
        GL.TexParameter(TextureTarget.Texture2D,
            TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
        GL.TexParameter(TextureTarget.Texture2D,
            TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
        GL.TexParameter(TextureTarget.Texture2D,
            TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
        GL.TexParameter(TextureTarget.Texture2D,
            TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);

        // Normal G-buffer: RGB8 unsigned, encoded view-space normal
        // (*0.5+0.5 in the shader). Linear filter is fine - neighboring
        // smooth-shaded pixels lerp to a still-valid intermediate normal
        // before renormalization in ssao.frag.
        _depthPrepassNormalTex = GL.GenTexture();
        GL.BindTexture(TextureTarget.Texture2D, _depthPrepassNormalTex);
        GL.TexImage2D(TextureTarget.Texture2D, 0,
            PixelInternalFormat.Rgb8,
            width, height, 0,
            PixelFormat.Rgb, PixelType.UnsignedByte, IntPtr.Zero);
        GL.TexParameter(TextureTarget.Texture2D,
            TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
        GL.TexParameter(TextureTarget.Texture2D,
            TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
        GL.TexParameter(TextureTarget.Texture2D,
            TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
        GL.TexParameter(TextureTarget.Texture2D,
            TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);

        _depthPrepassFbo = GL.GenFramebuffer();
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, _depthPrepassFbo);
        GL.FramebufferTexture2D(FramebufferTarget.Framebuffer,
            FramebufferAttachment.DepthAttachment,
            TextureTarget.Texture2D, _depthPrepassDepthTex, 0);
        GL.FramebufferTexture2D(FramebufferTarget.Framebuffer,
            FramebufferAttachment.ColorAttachment0,
            TextureTarget.Texture2D, _depthPrepassNormalTex, 0);
        GL.DrawBuffer(DrawBufferMode.ColorAttachment0);
        GL.ReadBuffer(ReadBufferMode.None);
        var prepassStatus = GL.CheckFramebufferStatus(FramebufferTarget.Framebuffer);
        if (prepassStatus != FramebufferErrorCode.FramebufferComplete)
        {
            throw new InvalidOperationException(
                "SSAO depth-prepass FBO incomplete: " + prepassStatus);
        }

        // SSAO output FBO: single-channel R8 texture. basic.frag samples
        // it with linear filtering so the inherent noisiness of the
        // hemisphere kernel gets smoothed slightly without an explicit
        // blur pass.
        _ssaoTex = GL.GenTexture();
        GL.BindTexture(TextureTarget.Texture2D, _ssaoTex);
        GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.R8,
            width, height, 0, PixelFormat.Red, PixelType.UnsignedByte, IntPtr.Zero);
        GL.TexParameter(TextureTarget.Texture2D,
            TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
        GL.TexParameter(TextureTarget.Texture2D,
            TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
        GL.TexParameter(TextureTarget.Texture2D,
            TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
        GL.TexParameter(TextureTarget.Texture2D,
            TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);

        _ssaoFbo = GL.GenFramebuffer();
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, _ssaoFbo);
        GL.FramebufferTexture2D(FramebufferTarget.Framebuffer,
            FramebufferAttachment.ColorAttachment0,
            TextureTarget.Texture2D, _ssaoTex, 0);
        var ssaoStatus = GL.CheckFramebufferStatus(FramebufferTarget.Framebuffer);
        if (ssaoStatus != FramebufferErrorCode.FramebufferComplete)
        {
            throw new InvalidOperationException(
                "SSAO output FBO incomplete: " + ssaoStatus);
        }

        // SSAO blur output FBO. Same R8 format / size as the raw SSAO
        // texture; receives the box-blur result. basic.frag samples
        // this (not the raw _ssaoTex) so the noise tile period doesn't
        // bleed through into the final image.
        _ssaoBlurTex = GL.GenTexture();
        GL.BindTexture(TextureTarget.Texture2D, _ssaoBlurTex);
        GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.R8,
            width, height, 0, PixelFormat.Red, PixelType.UnsignedByte, IntPtr.Zero);
        GL.TexParameter(TextureTarget.Texture2D,
            TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
        GL.TexParameter(TextureTarget.Texture2D,
            TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
        GL.TexParameter(TextureTarget.Texture2D,
            TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
        GL.TexParameter(TextureTarget.Texture2D,
            TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);

        _ssaoBlurFbo = GL.GenFramebuffer();
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, _ssaoBlurFbo);
        GL.FramebufferTexture2D(FramebufferTarget.Framebuffer,
            FramebufferAttachment.ColorAttachment0,
            TextureTarget.Texture2D, _ssaoBlurTex, 0);
        var blurStatus = GL.CheckFramebufferStatus(FramebufferTarget.Framebuffer);
        if (blurStatus != FramebufferErrorCode.FramebufferComplete)
        {
            throw new InvalidOperationException(
                "SSAO blur FBO incomplete: " + blurStatus);
        }

        GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
        _ssaoFboSize = (width, height);
    }

    /// <summary>Renders the scene's opaque + alpha-test geometry to the
    /// depth pre-pass FBO from the camera's POV. The resulting depth
    /// texture feeds the SSAO post-pass; basic.frag's per-fragment
    /// gl_FragCoord-based sampling matches what was rendered here so
    /// AO is consistent with the visible silhouette. Caller must
    /// restore the previously bound FBO + viewport.</summary>
    private void RenderDepthPrepass(ref Matrix4 model, ref Matrix4 view, ref Matrix4 projection,
        int width, int height)
    {
        if (_depthOnlyShader == null) return;
        EnsureSsaoFbos(width, height);

        GL.BindFramebuffer(FramebufferTarget.Framebuffer, _depthPrepassFbo);
        GL.Viewport(0, 0, width, height);
        // Background clear value (0.5, 0.5, 1.0) decodes to (0, 0, +1) in
        // view space - a safe "facing camera" fallback for pixels with no
        // geometry, so ssao.frag's normal sample at the far plane doesn't
        // pull garbage. ssao.frag early-outs on far-plane depth anyway,
        // but defensible default in case the early-out is ever loosened.
        GL.ClearColor(0.5f, 0.5f, 1.0f, 1.0f);
        GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

        // Normal matrix is transpose(inverse(mat3(view * model))). Built
        // once per pass since model / view are uniform across all meshes
        // in this prepass (matches the main render pass - per-mesh local
        // transforms are baked into vertex positions).
        var viewModel = model * view;
        var normalMatrix = new Matrix3(viewModel);
        normalMatrix.Invert();
        normalMatrix.Transpose();

        _depthOnlyShader.Use();
        _depthOnlyShader.SetMatrix4("u_model", ref model);
        _depthOnlyShader.SetMatrix4("u_view", ref view);
        _depthOnlyShader.SetMatrix4("u_projection", ref projection);
        _depthOnlyShader.SetMatrix3("u_normalMatrix", ref normalMatrix);

        foreach (var mesh in _meshes)
        {
            if (!mesh.IsRendering) continue;
            if (mesh.RenderAsWireframeFallback) continue;
            // Skip every transparent mesh (alpha-blend AND alpha-test).
            //
            // Alpha-blend: cumulative transparency makes any depth value misleading.
            //
            // Alpha-test: the per-pixel discard produces a hard binary depth edge
            // wherever the threshold flips. SSAO (ssao.frag) samples this prepass
            // depth with a hemispheric kernel and a smoothstep range check, which
            // reads those edges as nearby occluders and darkens the surface
            // underneath - producing a "mask-like" halo around alpha-tested
            // perimeters (hair fringes, eyelashes, brow cards, lace, fur trim).
            // See basic.frag's is_eye AO gate for the same artifact class gated
            // downstream instead of at the source.
            //
            // Shadow and main color passes still include alpha-test geometry;
            // only this SSAO depth prepass excludes it.
            if (mesh.HasAlphaBlend || mesh.UseAlphaTest) continue;

            _depthOnlyShader.SetBool("use_alpha_test", mesh.UseAlphaTest);
            _depthOnlyShader.SetFloat("alpha_threshold", mesh.AlphaThreshold);
            if (mesh.UseAlphaTest)
            {
                GL.ActiveTexture(TextureUnit.Texture0);
                GL.BindTexture(TextureTarget.Texture2D, mesh.DiffuseTexture);
            }

            GL.BindVertexArray(mesh.Vao);
            GL.DrawElements(PrimitiveType.Triangles, mesh.IndexCount,
                DrawElementsType.UnsignedInt, 0);
        }
        GL.BindVertexArray(0);
    }

    /// <summary>Runs the SSAO post-process: reads the depth prepass
    /// depth + normal textures and the noise texture, writes per-pixel
    /// occlusion factor to the SSAO FBO. Caller must restore the
    /// previously bound FBO + viewport.</summary>
    private void ComputeSsao(ref Matrix4 projection, int width, int height)
    {
        if (_ssaoShader == null || _ssaoSampleKernel == null) return;

        GL.BindFramebuffer(FramebufferTarget.Framebuffer, _ssaoFbo);
        GL.Viewport(0, 0, width, height);
        // Clear isn't strictly needed (the full-screen quad covers every
        // pixel) but is cheap and avoids surprises if an early-out is
        // added later.
        GL.ClearColor(1f, 1f, 1f, 1f);
        GL.Clear(ClearBufferMask.ColorBufferBit);

        _ssaoShader.Use();

        // Bind depth (unit 0), normal G-buffer (unit 1), noise (unit 2).
        GL.ActiveTexture(TextureUnit.Texture0);
        GL.BindTexture(TextureTarget.Texture2D, _depthPrepassDepthTex);
        GL.ActiveTexture(TextureUnit.Texture1);
        GL.BindTexture(TextureTarget.Texture2D, _depthPrepassNormalTex);
        GL.ActiveTexture(TextureUnit.Texture2);
        GL.BindTexture(TextureTarget.Texture2D, _ssaoNoiseTex);

        var invProj = projection.Inverted();
        _ssaoShader.SetMatrix4("u_projection", ref projection);
        _ssaoShader.SetMatrix4("u_invProjection", ref invProj);
        // Kernel is uploaded as 16 separate vec3 uniforms (one per index)
        // because GlShaderProgram doesn't ship an array uploader. The
        // uniform names match the GLSL declaration "uniform vec3 u_kernel[16]".
        for (int i = 0; i < _ssaoSampleKernel.Length; i++)
        {
            var k = _ssaoSampleKernel[i];
            _ssaoShader.SetVector3("u_kernel[" + i + "]", k.X, k.Y, k.Z);
        }
        _ssaoShader.SetVector2("u_noiseScale",
            (float)width / SsaoNoiseSize, (float)height / SsaoNoiseSize);
        // Radius / bias / intensity all driven from the host-mirrored
        // public properties, so the user's settings sliders take effect
        // on the next render. Sensible-default suggestions: ~4 units
        // for radius, ~0.05 for bias, ~1.5 for intensity at Skyrim NPC
        // head scale (head ~22 units tall).
        _ssaoShader.SetFloat("u_radius", SsaoRadius);
        _ssaoShader.SetFloat("u_bias", SsaoBias);
        _ssaoShader.SetFloat("u_intensity", SsaoIntensity);

        GL.Disable(EnableCap.DepthTest);
        GL.BindVertexArray(_ssaoFullscreenVao);
        GL.DrawArrays(PrimitiveType.Triangles, 0, 3);
        GL.BindVertexArray(0);
        GL.Enable(EnableCap.DepthTest);
    }

    /// <summary>Smooths the raw SSAO texture with a 4x4 box blur to
    /// cancel the noise tile pattern. Reads <see cref="_ssaoTex"/>,
    /// writes to <see cref="_ssaoBlurTex"/> which the main pass binds
    /// instead of the raw output. Caller must restore previously bound
    /// FBO + viewport.</summary>
    private void BlurSsao(int width, int height)
    {
        if (_ssaoBlurShader == null) return;

        GL.BindFramebuffer(FramebufferTarget.Framebuffer, _ssaoBlurFbo);
        GL.Viewport(0, 0, width, height);
        GL.ClearColor(1f, 1f, 1f, 1f);
        GL.Clear(ClearBufferMask.ColorBufferBit);

        _ssaoBlurShader.Use();
        _ssaoBlurShader.SetVector2("u_texelSize", 1f / width, 1f / height);
        GL.ActiveTexture(TextureUnit.Texture0);
        GL.BindTexture(TextureTarget.Texture2D, _ssaoTex);

        GL.Disable(EnableCap.DepthTest);
        GL.BindVertexArray(_ssaoFullscreenVao);
        GL.DrawArrays(PrimitiveType.Triangles, 0, 3);
        GL.BindVertexArray(0);
        GL.Enable(EnableCap.DepthTest);
    }

    /// <summary>
    /// Draws edges for every mesh with <see cref="GlMesh.ShowWireframe"/> set
    /// (BodySlide classifier overlay), and for every mesh with
    /// <see cref="GlMesh.RenderAsWireframeFallback"/> set (missing-texture
    /// placeholder — these aren't drawn by the solid passes at all). Uses
    /// polygon-mode Line with a negative polygon offset so explicit overlay
    /// edges sit just in front of the solid surface without z-fighting.
    /// </summary>
    private void DrawWireframeOverlay(ref Matrix4 model, ref Matrix4 view, ref Matrix4 projection)
    {
        if (_wireframeShader == null) return;

        // Early exit if nothing wants wireframe — typical case.
        bool any = false;
        for (int i = 0; i < _meshes.Count; i++)
        {
            if (!_meshes[i].IsRendering) continue;
            if (_meshes[i].ShowWireframe || _meshes[i].RenderAsWireframeFallback)
            { any = true; break; }
        }
        if (!any) return;

        _wireframeShader.Use();
        _wireframeShader.SetMatrix4("u_model", ref model);
        _wireframeShader.SetMatrix4("u_view", ref view);
        _wireframeShader.SetMatrix4("u_projection", ref projection);

        GL.PolygonMode(MaterialFace.FrontAndBack, PolygonMode.Line);
        GL.Enable(EnableCap.PolygonOffsetLine);
        GL.PolygonOffset(-1.0f, -1.0f);
        GL.Disable(EnableCap.CullFace);
        GL.LineWidth(1.0f);

        // Two color groups. Update the uniform only on transition to keep
        // GL state-change traffic minimal even though the per-shape branch
        // is checked unconditionally.
        bool currentIsFallback = false;
        _wireframeShader.SetVector3("u_color",
            WireframeColor.X, WireframeColor.Y, WireframeColor.Z);

        foreach (var mesh in _meshes)
        {
            if (!mesh.IsRendering) continue;
            bool wantFallback = mesh.RenderAsWireframeFallback;
            bool wantOverlay = mesh.ShowWireframe;
            if (!wantFallback && !wantOverlay) continue;

            // Fallback shapes ALWAYS draw in the missing-texture color, even
            // when ShowWireframe is also true — the missing-texture state is
            // the more important diagnostic.
            if (wantFallback != currentIsFallback)
            {
                var c = wantFallback ? MissingTextureWireframeColor : WireframeColor;
                _wireframeShader.SetVector3("u_color", c.X, c.Y, c.Z);
                currentIsFallback = wantFallback;
            }

            GL.BindVertexArray(mesh.Vao);
            GL.DrawElements(PrimitiveType.Triangles, mesh.IndexCount,
                DrawElementsType.UnsignedInt, 0);
        }

        GL.BindVertexArray(0);
        GL.PolygonMode(MaterialFace.FrontAndBack, PolygonMode.Fill);
        GL.Disable(EnableCap.PolygonOffsetLine);
        GL.Enable(EnableCap.CullFace);
    }

    /// <summary>
    /// Recomputes view-space light directions only when the view matrix or any
    /// light's world-space direction/type has changed since the last frame.
    /// OpenTK uses row-vector convention (v * M), matching how GLSL interprets
    /// the uploaded matrix. Using M * v here would apply the transpose rotation
    /// and cause the lit side of the mesh to drift as the camera orbits.
    /// </summary>
    private void EnsureViewSpaceLightDirs(ref Matrix4 view)
    {
        bool dirty = !_hasCachedLightDirs || view != _cachedLightUploadView;
        if (!dirty)
        {
            for (int i = 0; i < 5; i++)
            {
                if (Lights[i].Type != _cachedLightTypes[i] ||
                    Lights[i].Direction != _cachedLightWorldDirs[i])
                {
                    dirty = true;
                    break;
                }
            }
        }
        if (!dirty) return;

        var viewMat3 = new Matrix3(view);
        for (int i = 0; i < 5; i++)
        {
            if (Lights[i].Type == 2)
            {
                var viewDir = Lights[i].Direction * viewMat3;
                viewDir.Normalize();
                _cachedViewSpaceLightDirs[i] = viewDir;
            }
            _cachedLightTypes[i] = Lights[i].Type;
            _cachedLightWorldDirs[i] = Lights[i].Direction;
        }
        _cachedLightUploadView = view;
        _hasCachedLightDirs = true;
    }

    /// <summary>
    /// Renders solid 3D arrows (cylinder shaft + cone head) showing each enabled
    /// directional light's shining direction (source → model) and magnitude.
    /// The stored <see cref="LightData.Direction"/> is the surface-to-light
    /// vector, so arrows point along its negation. Selected arrows brighten and
    /// thicken slightly for visual distinction.
    ///
    /// <para>Arrows tip at <paramref name="camera"/>.Target and scale with
    /// <paramref name="camera"/>.Distance so they stay roughly the same
    /// on-screen size regardless of how tightly the host frames the model.
    /// Without this, head-only Auto framing would push fixed-size arrows
    /// (originally sized for full-body framing at distance ≈ 350) clean
    /// out of the viewport.</para>
    /// </summary>
    private void DrawDirectionalLightArrows(OrbitCamera camera, ref Matrix4 view, ref Matrix4 projection)
    {
        if (_debugShader == null) return;

        // Reset cached segments; we repopulate only for lights we draw.
        for (int i = 0; i < _arrowSegments.Length; i++)
            _arrowSegments[i] = default;

        _debugShader.Use();
        _debugShader.SetMatrix4("u_view", ref view);
        _debugShader.SetMatrix4("u_projection", ref projection);
        _debugShader.SetFloat("u_shaded", 1f);

        GL.BindVertexArray(_debugVao);
        GL.BindBuffer(BufferTarget.ArrayBuffer, _debugVbo);

        bool depthWasEnabled = GL.IsEnabled(EnableCap.DepthTest);
        bool cullWasEnabled = GL.IsEnabled(EnableCap.CullFace);
        // Arrow gizmos always draw on top of the model so the user can find them
        // regardless of orbit angle.
        GL.Disable(EnableCap.DepthTest);
        GL.Enable(EnableCap.CullFace);
        GL.CullFace(CullFaceMode.Back);

        // Constant on-screen size: world-space length scales linearly with the
        // camera's orbit distance. Since perspective shrinks objects by
        // 1/cameraDistance, length ∝ cameraDistance keeps the arrow's pixel
        // footprint stable across framings (head close-up vs full-body wide).
        float camDistance = MathF.Max(camera.Distance, camera.MinDistance);

        for (int i = 1; i <= 3; i++)
        {
            if (Lights[i].Type != 2) continue;
            if (Lights[i].Intensity <= 0f) continue;

            var toLight = Lights[i].Direction;
            if (toLight.LengthSquared < 1e-8f) continue;

            var shineDir = -toLight;
            shineDir.Normalize();

            // Length-as-fraction-of-camera-distance, modulated by light intensity
            // so brighter lights still read as longer arrows (preserving the
            // visual cue from the previous fixed-world-units mapping). Above
            // 100% the slope flattens so 300% lights don't dominate the view.
            float intensityVis = MathF.Min(Lights[i].Intensity, 3.0f);
            float lengthFraction =
                  0.05f
                + 0.15f * MathF.Min(intensityVis, 1f)
                + 0.05f * MathF.Max(intensityVis - 1f, 0f);
            float length = camDistance * lengthFraction;

            bool selected = SelectedLightIndex == i;
            float radius = length * (selected ? 0.040f : 0.030f);
            float headLen = length * 0.22f;
            float headRad = length * (selected ? 0.085f : 0.065f);
            float shaftLen = length - headLen;

            // Anchor the tip at the camera's look-at point so arrows track
            // whatever the host framed (head/face for Auto-mode mugshots, full
            // body for Manual orbits) instead of the old fixed chest-height.
            var tip = camera.Target;
            var tail = tip - shineDir * length;
            var shaftEnd = tip - shineDir * headLen;

            _arrowSegments[i] = new ArrowSegment
            {
                Tail = tail, Tip = tip, Radius = headRad, Valid = true,
            };

            // Base color: blend the light's actual color toward a saturated
            // default per-slot hue so arrows remain identifiable even if the
            // user picks a near-white color.
            var baseHue = _arrowColors[Math.Min(i, _arrowColors.Length - 1)];
            var lightCol = Lights[i].Color;
            var c = Vector3.Lerp(baseHue, lightCol, 0.45f);
            if (selected) c *= 1.35f; // boost brightness for selection
            _debugShader.SetVector3("u_color", MathF.Min(c.X, 1.6f), MathF.Min(c.Y, 1.6f), MathF.Min(c.Z, 1.6f));

            var verts = BuildArrowMesh(tail, shaftEnd, tip, shineDir, radius, headRad);
            GL.BufferData(BufferTarget.ArrayBuffer,
                verts.Length * sizeof(float), verts, BufferUsageHint.DynamicDraw);
            GL.DrawArrays(PrimitiveType.Triangles, 0, verts.Length / 6);
        }

        _debugShader.SetFloat("u_shaded", 0f);
        if (depthWasEnabled) GL.Enable(EnableCap.DepthTest);
        if (!cullWasEnabled) GL.Disable(EnableCap.CullFace);
        GL.BindVertexArray(0);
    }

    /// <summary>
    /// Builds a triangle mesh (position + world-space normal, interleaved 6 floats
    /// per vertex) for a single arrow composed of a cylindrical shaft, a disk at
    /// the shaft's far end, and a conical head. All triangles are wound CCW from
    /// outside so back-face culling hides interiors.
    /// </summary>
    private static float[] BuildArrowMesh(Vector3 tail, Vector3 shaftEnd, Vector3 tip,
        Vector3 shineDir, float shaftRadius, float headRadius)
    {
        const int sides = 16;
        // Orthonormal basis perpendicular to shineDir
        var up = MathF.Abs(shineDir.Y) < 0.95f ? Vector3.UnitY : Vector3.UnitX;
        var u = Vector3.Normalize(Vector3.Cross(shineDir, up));
        var v = Vector3.Normalize(Vector3.Cross(shineDir, u));

        // Pre-compute ring offsets and outward normals.
        var ringDirs = new Vector3[sides];
        for (int s = 0; s < sides; s++)
        {
            float t = (s / (float)sides) * MathF.PI * 2f;
            ringDirs[s] = u * MathF.Cos(t) + v * MathF.Sin(t);
        }

        // Triangle count:
        //   Shaft:     sides * 2 quads * 3 verts = sides * 6
        //   Back cap:  sides triangles             = sides * 3
        //   Head ring: sides quads                  = sides * 6  (disk at shaft end)
        //   Cone:      sides triangles              = sides * 3
        // Total:       sides * 18
        var data = new float[sides * 18 * 6];
        int w = 0;

        void AddVert(Vector3 p, Vector3 n)
        {
            data[w++] = p.X; data[w++] = p.Y; data[w++] = p.Z;
            data[w++] = n.X; data[w++] = n.Y; data[w++] = n.Z;
        }

        // --- Shaft (cylinder between tail and shaftEnd) ---
        for (int s = 0; s < sides; s++)
        {
            int s2 = (s + 1) % sides;
            var nA = ringDirs[s];
            var nB = ringDirs[s2];
            var a0 = tail     + nA * shaftRadius;
            var a1 = shaftEnd + nA * shaftRadius;
            var b0 = tail     + nB * shaftRadius;
            var b1 = shaftEnd + nB * shaftRadius;
            // Quad as two triangles, outward-facing.
            AddVert(a0, nA); AddVert(b0, nB); AddVert(a1, nA);
            AddVert(a1, nA); AddVert(b0, nB); AddVert(b1, nB);
        }

        // --- Back cap (disk at tail, facing -shineDir) ---
        var tailNormal = -shineDir;
        for (int s = 0; s < sides; s++)
        {
            int s2 = (s + 1) % sides;
            var p1 = tail + ringDirs[s] * shaftRadius;
            var p2 = tail + ringDirs[s2] * shaftRadius;
            AddVert(tail, tailNormal); AddVert(p2, tailNormal); AddVert(p1, tailNormal);
        }

        // --- Head ring (annular disk between shaft radius and head radius at shaftEnd,
        //     facing -shineDir, so the head's back side is visible). ---
        for (int s = 0; s < sides; s++)
        {
            int s2 = (s + 1) % sides;
            var p1 = shaftEnd + ringDirs[s] * shaftRadius;
            var p2 = shaftEnd + ringDirs[s2] * shaftRadius;
            var q1 = shaftEnd + ringDirs[s] * headRadius;
            var q2 = shaftEnd + ringDirs[s2] * headRadius;
            AddVert(p1, tailNormal); AddVert(p2, tailNormal); AddVert(q1, tailNormal);
            AddVert(q1, tailNormal); AddVert(p2, tailNormal); AddVert(q2, tailNormal);
        }

        // --- Cone head (from ring at shaftEnd (head radius) to tip) ---
        // Compute slanted normals so the cone shades correctly.
        float coneHeight = (tip - shaftEnd).Length;
        float slant = MathF.Sqrt(coneHeight * coneHeight + headRadius * headRadius);
        float nAxial = headRadius / slant;    // component along +shineDir
        float nRadial = coneHeight / slant;   // component radially outward
        for (int s = 0; s < sides; s++)
        {
            int s2 = (s + 1) % sides;
            var p1 = shaftEnd + ringDirs[s] * headRadius;
            var p2 = shaftEnd + ringDirs[s2] * headRadius;
            var n1 = Vector3.Normalize(ringDirs[s] * nRadial + shineDir * nAxial);
            var n2 = Vector3.Normalize(ringDirs[s2] * nRadial + shineDir * nAxial);
            var nTip = Vector3.Normalize((n1 + n2) * 0.5f);
            AddVert(p1, n1); AddVert(p2, n2); AddVert(tip, nTip);
        }

        return data;
    }

    /// <summary>
    /// Maps a Bethesda NiAlphaProperty blend-factor enum index (as stored in
    /// flags bits 1-4 / 5-8) to the corresponding OpenTK BlendingFactor.
    /// The Bethesda enum order matches the GL convention 1-to-1 except that
    /// Bethesda reuses an integer-indexed enum (0=ONE, 1=ZERO, ...) instead
    /// of the GL constants directly.
    /// </summary>
    private static BlendingFactor MapBethesdaBlendFactor(int idx) => idx switch
    {
        0 => BlendingFactor.One,
        1 => BlendingFactor.Zero,
        2 => BlendingFactor.SrcColor,
        3 => BlendingFactor.OneMinusSrcColor,
        4 => BlendingFactor.DstColor,
        5 => BlendingFactor.OneMinusDstColor,
        6 => BlendingFactor.SrcAlpha,
        7 => BlendingFactor.OneMinusSrcAlpha,
        8 => BlendingFactor.DstAlpha,
        9 => BlendingFactor.OneMinusDstAlpha,
        10 => BlendingFactor.SrcAlphaSaturate,
        // Indices 11-15 are reserved by the Bethesda enum and shouldn't appear
        // on actor meshes. Conservative fallback: standard "over" blending.
        _ => idx == 0xF ? BlendingFactor.One : BlendingFactor.SrcAlpha,
    };

    private void DrawMesh(GlMesh mesh)
    {
        if (_shader == null) return;

        // Bind textures
        GL.ActiveTexture(TextureUnit.Texture0);
        GL.BindTexture(TextureTarget.Texture2D, mesh.DiffuseTexture);
        GL.ActiveTexture(TextureUnit.Texture1);
        GL.BindTexture(TextureTarget.Texture2D, mesh.NormalTexture);
        GL.ActiveTexture(TextureUnit.Texture2);
        GL.BindTexture(TextureTarget.Texture2D, mesh.SkinTexture);
        GL.ActiveTexture(TextureUnit.Texture3);
        GL.BindTexture(TextureTarget.Texture2D, mesh.SpecularTexture);
        GL.ActiveTexture(TextureUnit.Texture4);
        GL.BindTexture(TextureTarget.Texture2D, mesh.FaceTintTexture);
        GL.ActiveTexture(TextureUnit.Texture5);
        GL.BindTexture(TextureTarget.Texture2D, mesh.DetailTexture);
        // Envmap dual binding: unit 6 is samplerCube, unit 10 is sampler2D.
        // Whichever path the mesh isn't using gets a neutral binding (default
        // black cubemap or texture 0) so both sampler uniforms stay valid.
        GL.ActiveTexture(TextureUnit.Texture6);
        if (mesh.HasEnvironmentMap && !mesh.IsEnvMap2D && mesh.EnvMapTexture != 0)
            GL.BindTexture(TextureTarget.TextureCubeMap, mesh.EnvMapTexture);
        else
            GL.BindTexture(TextureTarget.TextureCubeMap, _defaultBlackCubemap);
        GL.ActiveTexture(TextureUnit.Texture7);
        GL.BindTexture(TextureTarget.Texture2D, mesh.EnvMaskTexture);
        GL.ActiveTexture(TextureUnit.Texture10);
        if (mesh.HasEnvironmentMap && mesh.IsEnvMap2D && mesh.EnvMapTexture != 0)
            GL.BindTexture(TextureTarget.Texture2D, mesh.EnvMapTexture);
        else
            GL.BindTexture(TextureTarget.Texture2D, 0);

        // Set material flags
        _shader.SetBool("has_normal_map", mesh.HasNormalMap);
        _shader.SetBool("has_skin_map", mesh.HasSkinMap);
        _shader.SetBool("has_specular", mesh.HasSpecular);
        _shader.SetBool("has_specular_map", mesh.HasSpecularMap);
        _shader.SetBool("has_face_tint_map", mesh.HasFaceTintMap);
        _shader.SetBool("has_greyscale_to_palette", mesh.HasGreyscaleToPalette);
        _shader.SetBool("has_tint_color", mesh.HasTintColor);
        _shader.SetBool("has_emissive", mesh.HasEmissive);
        _shader.SetBool("is_model_space", mesh.IsModelSpace);
        _shader.SetBool("has_hair_soft_lighting", mesh.HasHairSoftLighting);
        _shader.SetBool("has_soft_lighting", mesh.HasSoftLighting);
        _shader.SetBool("has_rim_lighting", mesh.HasRimLighting);
        _shader.SetBool("has_vertex_colors", mesh.HasVertexColors);
        _shader.SetBool("has_environment_map", mesh.HasEnvironmentMap);
        _shader.SetBool("has_env_mask", mesh.HasEnvMask);
        _shader.SetBool("is_env_map_2d", mesh.IsEnvMap2D);
        _shader.SetBool("has_detail_map", mesh.HasDetailMap);
        _shader.SetBool("is_eye", mesh.IsEye);
        _shader.SetBool("use_alpha_test", mesh.UseAlphaTest);
        _shader.SetBool("is_face_shape", mesh.IsFaceShape);
        _shader.SetBool("is_skin", mesh.IsSkinShape);
        _shader.SetFloat("skin_tint_alpha", mesh.SkinTintAlpha);
        _shader.SetBool("is_face_empty_detail", mesh.IsFaceWithEmptyDetailSlot);
        _shader.SetBool("is_hair_tint", mesh.IsHairTintShader);

        // Per-shape texture visibility toggles
        _shader.SetBool("u_enableDiffuse", mesh.DiffuseEnabled);
        _shader.SetBool("u_enableNormal", mesh.NormalEnabled);
        _shader.SetBool("u_enableSkin", mesh.SkinEnabled);
        _shader.SetBool("u_enableSpecular", mesh.SpecularEnabled);
        _shader.SetBool("u_enableFaceTint", mesh.FaceTintEnabled);
        _shader.SetBool("u_enableDetail", mesh.DetailEnabled);
        _shader.SetBool("u_enableEnvMap", mesh.EnvMapEnabled);
        _shader.SetBool("u_enableEmissive", mesh.EmissiveEnabled);
        _shader.SetBool("u_enableTintColor", mesh.TintColorEnabled);

        // Set material properties
        _shader.SetFloat("alpha_threshold", mesh.AlphaThreshold);
        _shader.SetFloat("greyscaleToPaletteScale", mesh.GreyscaleToPaletteScale);
        _shader.SetVector3("tint_color", mesh.TintColor.X, mesh.TintColor.Y, mesh.TintColor.Z);
        _shader.SetFloat("materialGlossiness", mesh.MaterialGlossiness);
        _shader.SetFloat("materialSpecularStrength", mesh.MaterialSpecularStrength);
        _shader.SetVector3("specularColor", mesh.SpecularColor.X, mesh.SpecularColor.Y, mesh.SpecularColor.Z);
        _shader.SetFloat("rimlightPower", mesh.RimlightPower);
        _shader.SetFloat("subsurfaceRolloff", mesh.SubsurfaceRolloff);
        _shader.SetVector3("emissiveColor", mesh.EmissiveColor.X, mesh.EmissiveColor.Y, mesh.EmissiveColor.Z);
        _shader.SetFloat("emissiveMultiple", mesh.EmissiveMultiple);
        _shader.SetFloat("envMapScale", mesh.EnvMapScale);
        _shader.SetFloat("eyeCubemapScale", mesh.EyeCubemapScale);
        _shader.SetVector2("u_uvScale", mesh.UvScale.X, mesh.UvScale.Y);
        _shader.SetVector2("u_uvOffset", mesh.UvOffset.X, mesh.UvOffset.Y);

        // Double-sided meshes (brow, eyelash, hair) need face culling disabled
        if (mesh.IsDoubleSided)
            GL.Disable(EnableCap.CullFace);
        else
            GL.Enable(EnableCap.CullFace);

        // Draw
        GL.BindVertexArray(mesh.Vao);
        GL.DrawElements(PrimitiveType.Triangles, mesh.IndexCount,
            DrawElementsType.UnsignedInt, 0);
        GL.BindVertexArray(0);

        // Restore culling default
        if (mesh.IsDoubleSided)
            GL.Enable(EnableCap.CullFace);
    }

    // Once-per-render-lifetime guard so the diagnostic dump doesn't spam every
    // frame of the live preview's continuous render loop. Reset to false when
    // the host re-binds <see cref="DiagnosticLog"/> (typically per capture session).
    private bool _glStateLogged;

    /// <summary>Queries and emits the renderer's current GL state to
    /// <see cref="DiagnosticLog"/> once per assignment of that delegate. The
    /// state captured — GL_FRAMEBUFFER_SRGB, viewport, bound draw-FBO, and
    /// the FBO's color-attachment color-encoding + component-type — surfaces
    /// driver / framebuffer-level info that CPU-side property dumps don't see.
    /// Intended as a general diagnostic for portrait-rendering differences
    /// between the offscreen mugshot pathway (private Rgba8 resolve FBO +
    /// ReadPixels) and the live-preview pathway (blits to GLWpfControl's
    /// WPF-managed backbuffer, whose format is opaque from this side).
    /// Whether any specific bug is explained by this state depends on the
    /// bug; for the Khajiit mugshot transparency issue traced in late 2026
    /// this query confirmed both pathways had matching MSAA Rgba8 FBOs and
    /// the actual cause was downstream (PNG alpha-channel write-through),
    /// not GL state.</summary>
    private void EmitGlStateDiagnostic(int viewportWidth, int viewportHeight)
    {
        var sink = DiagnosticLog;
        if (sink == null) return;
        if (_glStateLogged) return;
        _glStateLogged = true;
        try
        {
            bool framebufferSrgb = GL.IsEnabled(EnableCap.FramebufferSrgb);
            int[] vp = new int[4];
            GL.GetInteger(GetPName.Viewport, vp);
            GL.GetInteger(GetPName.DrawFramebufferBinding, out int drawFbo);
            int colorEncoding = 0;
            int componentType = 0;
            int redSize = 0;
            int alphaSize = 0;
            try
            {
                GL.GetFramebufferAttachmentParameter(
                    FramebufferTarget.DrawFramebuffer,
                    FramebufferAttachment.ColorAttachment0,
                    FramebufferParameterName.FramebufferAttachmentColorEncoding,
                    out colorEncoding);
                GL.GetFramebufferAttachmentParameter(
                    FramebufferTarget.DrawFramebuffer,
                    FramebufferAttachment.ColorAttachment0,
                    FramebufferParameterName.FramebufferAttachmentComponentType,
                    out componentType);
                GL.GetFramebufferAttachmentParameter(
                    FramebufferTarget.DrawFramebuffer,
                    FramebufferAttachment.ColorAttachment0,
                    FramebufferParameterName.FramebufferAttachmentRedSize,
                    out redSize);
                GL.GetFramebufferAttachmentParameter(
                    FramebufferTarget.DrawFramebuffer,
                    FramebufferAttachment.ColorAttachment0,
                    FramebufferParameterName.FramebufferAttachmentAlphaSize,
                    out alphaSize);
            }
            catch { /* default framebuffer attachment may not support all queries */ }

            // Color-encoding constants: 0x8C40 = GL_SRGB, 0x8DA8 = GL_LINEAR
            // (the GL_FRAMEBUFFER_ATTACHMENT_COLOR_ENCODING enum reuses
            // values from the broader GL constants list). Component-type
            // constants below are the standard GL_BYTE / GL_UNSIGNED_BYTE
            // / etc. enum range starting at 0x1400.
            string encName = colorEncoding switch
            {
                0x8C40 => "SRGB",
                0x8DA8 => "LINEAR",
                _ => "0x" + colorEncoding.ToString("X")
            };
            string typeName = componentType switch
            {
                0x1400 => "BYTE",
                0x1401 => "UNSIGNED_BYTE",
                0x1402 => "SHORT",
                0x1403 => "UNSIGNED_SHORT",
                0x1404 => "INT",
                0x1405 => "UNSIGNED_INT",
                0x1406 => "FLOAT",
                0x8DF4 => "FLOAT_VEC4",
                0x140B => "HALF_FLOAT",
                0x8C17 => "UNSIGNED_NORMALIZED",
                0x8F9C => "SIGNED_NORMALIZED",
                _ => "0x" + componentType.ToString("X")
            };
            sink($"CharacterViewer: GL-STATE — FramebufferSRGB={framebufferSrgb}, " +
                $"viewport=({vp[0]},{vp[1]},{vp[2]}x{vp[3]}), passedW={viewportWidth}, passedH={viewportHeight}, " +
                $"drawFBO={drawFbo}, color0(encoding={encName}, componentType={typeName}, redBits={redSize}, alphaBits={alphaSize})");
        }
        catch { /* swallow; diagnostic is best-effort */ }
    }

    /// <summary>
    /// Updates lighting from the VM's slider values.
    /// </summary>
    public void SetAmbientIntensity(float intensity01)
    {
        Lights[0].Intensity = intensity01;
    }

    public void SetKeyLightIntensity(float intensity01)
    {
        Lights[1].Intensity = intensity01;
    }

    public void SetKeyLightDirection(float azimuthDeg, float elevationDeg)
    {
        Lights[1].Direction = DirectionFromAzEl(azimuthDeg, elevationDeg);
    }

    /// <summary>Configures the key light (slot 1) in one call.</summary>
    public void SetKeyLight(float azimuthDeg, float elevationDeg, float intensity01, Vector3 color)
    {
        Lights[1].Type = 2;
        Lights[1].Direction = DirectionFromAzEl(azimuthDeg, elevationDeg);
        Lights[1].Intensity = intensity01;
        Lights[1].Color = color;
    }

    /// <summary>Configures the fill light (slot 2) in one call.</summary>
    public void SetFillLight(float azimuthDeg, float elevationDeg, float intensity01, Vector3 color)
    {
        Lights[2].Type = 2;
        Lights[2].Direction = DirectionFromAzEl(azimuthDeg, elevationDeg);
        Lights[2].Intensity = intensity01;
        Lights[2].Color = color;
    }

    /// <summary>Configures the rim light (slot 3) in one call.</summary>
    public void SetRimLight(float azimuthDeg, float elevationDeg, float intensity01, Vector3 color)
    {
        Lights[3].Type = 2;
        Lights[3].Direction = DirectionFromAzEl(azimuthDeg, elevationDeg);
        Lights[3].Intensity = intensity01;
        Lights[3].Color = color;
    }

    /// <summary>Converts azimuth/elevation (degrees) to the surface-to-light vector
    /// convention used by the shader (NdotL = dot(normal, direction)). The character
    /// faces world -Z (NIF +Y_nif mapped through R_X(-90) to -Z_yup), and the orbit
    /// camera at az=180 places the eye at -Z (front of character). For the light
    /// to share this convention - az=180 puts the light on the camera side (front)
    /// - this formula must match OrbitCamera.GetViewMatrix's offset formula exactly
    /// (no negated Z). A prior version negated the Z term on the mistaken assumption
    /// that the character faced +Z, which put all az=180 lights behind the character.</summary>
    private static Vector3 DirectionFromAzEl(float azimuthDeg, float elevationDeg)
    {
        float az = MathHelper.DegreesToRadians(azimuthDeg);
        float el = MathHelper.DegreesToRadians(elevationDeg);
        return new Vector3(
            MathF.Cos(el) * MathF.Sin(az),
            MathF.Sin(el),
            MathF.Cos(el) * MathF.Cos(az));
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            ClearMeshes();
            _shader?.Dispose();
            _debugShader?.Dispose();
            _wireframeShader?.Dispose();
            if (_debugVbo != 0) GL.DeleteBuffer(_debugVbo);
            if (_debugVao != 0) GL.DeleteVertexArray(_debugVao);
            _disposed = true;
        }
    }

    /// <summary>
    /// Drops all GL-resource references without issuing any GL calls. Used when the
    /// owning UC_CharacterViewer is unloaded/recreated: GLWpfControl 4.x creates a new
    /// GL context per control instance, so the shader-program ID, VAO, VBOs, and mesh
    /// buffers stored here belong to the old (now-destroyed) context. Calling
    /// <see cref="GL.DeleteBuffer(int)"/> etc. on those IDs in the new context either
    /// no-ops or emits GL_INVALID_OPERATION; the resources themselves die with their
    /// context. Next <see cref="Initialize"/> rebuilds everything on the fresh context.
    /// </summary>
    public void ForgetResourcesFromDeadContext()
    {
        _meshes.Clear();
        _shader = null;
        _debugShader = null;
        _wireframeShader = null;
        _shadowShader = null;
        _shadowFbo = -1;
        _shadowDepthTex = -1;
        _depthOnlyShader = null;
        _ssaoShader = null;
        _ssaoBlurShader = null;
        _depthPrepassFbo = -1;
        _depthPrepassDepthTex = -1;
        _depthPrepassNormalTex = -1;
        _ssaoFbo = -1;
        _ssaoTex = -1;
        _ssaoBlurFbo = -1;
        _ssaoBlurTex = -1;
        _ssaoNoiseTex = -1;
        _ssaoFullscreenVao = -1;
        _ssaoFboSize = (0, 0);
        _debugVao = 0;
        _debugVbo = 0;
        _initialized = false;
        _hasCachedLightDirs = false;
    }
}
