using System;
using System.Collections.Generic;
using System.IO;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;

namespace SynthEBD;

/// <summary>
/// Core OpenGL renderer for the character viewer. Manages the shader program,
/// draws all meshes with proper material uniforms, and handles lighting.
/// </summary>
public class GlRenderer : IDisposable
{
    private GlShaderProgram? _shader;
    private GlShaderProgram? _debugShader;
    private GlShaderProgram? _wireframeShader;
    private int _debugVao;
    private int _debugVbo;
    private readonly List<GlMesh> _meshes = new();
    private bool _initialized;
    private bool _disposed;

    /// <summary>RGB color used for the wireframe overlay pass. Bright cyan by
    /// default so edges read clearly against both skin and clothing.</summary>
    public Vector3 WireframeColor { get; set; } = new Vector3(0.2f, 1.0f, 0.9f);

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

    public struct MeasurementLineSegment
    {
        public Vector3 A;
        public Vector3 B;
        public Vector3 Color;
    }

    /// <summary>When true, renders arrow gizmos showing each directional light's shining
    /// direction (from source to model) and magnitude (length scales with intensity).</summary>
    public bool ShowKeyLightVisualization { get; set; } = false;

    /// <summary>World-space point the arrows point at — matches the orbit camera target.</summary>
    public Vector3 KeyLightVisualizationTarget { get; set; } = new Vector3(0f, 85f, 0f);

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
        _shader.SetInt("texture_envmap", 6);
        _shader.SetInt("texture_envmask", 7);

        GL.Enable(EnableCap.DepthTest);
        GL.Enable(EnableCap.CullFace);
        GL.CullFace(CullFaceMode.Back);

        // Debug (line-based) shader for the key-light arrow gizmo
        string debugVertPath = Path.Combine(shaderDirectory, "debug.vert");
        string debugFragPath = Path.Combine(shaderDirectory, "debug.frag");
        _debugShader = GlShaderProgram.LoadFromFiles(debugVertPath, debugFragPath);

        // Wireframe shader — reads position (location 0) from the standard mesh
        // VAO and draws a flat-colored edge overlay on top of the solid mesh.
        string wireVertPath = Path.Combine(shaderDirectory, "wireframe.vert");
        string wireFragPath = Path.Combine(shaderDirectory, "wireframe.frag");
        _wireframeShader = GlShaderProgram.LoadFromFiles(wireVertPath, wireFragPath);

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

    /// <summary>
    /// Renders all meshes with the given camera matrices.
    /// </summary>
    public void Render(OrbitCamera camera, int viewportWidth, int viewportHeight)
    {
        if (!_initialized || _shader == null) return;
        if (viewportWidth <= 0 || viewportHeight <= 0) return;

        GL.Viewport(0, 0, viewportWidth, viewportHeight);
        GL.ClearColor(ClearColor.X, ClearColor.Y, ClearColor.Z, 1f);
        GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

        _shader.Use();

        // Camera matrices
        float aspect = (float)viewportWidth / viewportHeight;
        var view = camera.GetViewMatrix();
        var projection = camera.GetProjectionMatrix(aspect);
        // Meshes are pre-transformed to world space; apply the NPC-height
        // multiplier here so a single matrix update scales the whole character
        // without touching per-mesh data.
        var model = Matrix4.CreateScale(ModelScale);

        _shader.SetMatrix4("u_model", ref model);
        _shader.SetMatrix4("u_view", ref view);
        _shader.SetMatrix4("u_projection", ref projection);

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
            if (mesh.UseAlphaTest || mesh.HasAlphaBlend) continue;
            DrawMesh(mesh);
        }

        // Pass 1: Alpha-tested meshes (discard in shader, depth writes ON).
        // Meshes with both alpha test and alpha blend go here — the discard
        // handles the cutout and depth writes prevent Z-fighting with the face.
        foreach (var mesh in _meshes)
        {
            if (!mesh.IsRendering) continue;
            if (!mesh.UseAlphaTest) continue;
            DrawMesh(mesh);
        }

        // Pass 2: Alpha-blended-only meshes (no alpha test — pure transparency).
        // Depth writes OFF to allow correct back-to-front compositing.
        GL.Enable(EnableCap.Blend);
        GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
        GL.DepthMask(false);
        foreach (var mesh in _meshes)
        {
            if (!mesh.IsRendering) continue;
            if (!mesh.HasAlphaBlend || mesh.UseAlphaTest) continue;
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
            DrawDirectionalLightArrows(ref view, ref projection);
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
        if (KeyVertexMarkers.Count == 0 && BoxResolvedMarkers.Count == 0) return;

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

        DrawMarkerList(KeyVertexMarkers, KeyVertexMarkerColor,
            SelectedKeyVertexMarkerIndices, KeyVertexMarkerSelectedColor);
        DrawMarkerList(BoxResolvedMarkers, BoxResolvedMarkerColor);

        _debugShader.SetFloat("u_shaded", 0f);
        if (depthWasEnabled) GL.Enable(EnableCap.DepthTest);
        if (!cullWasEnabled) GL.Disable(EnableCap.CullFace);
        GL.BindVertexArray(0);
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
        if (MeasurementLines.Count == 0) return;

        _debugShader.Use();
        _debugShader.SetMatrix4("u_view", ref view);
        _debugShader.SetMatrix4("u_projection", ref projection);
        _debugShader.SetFloat("u_shaded", 0f);

        GL.BindVertexArray(_debugVao);
        GL.BindBuffer(BufferTarget.ArrayBuffer, _debugVbo);

        bool depthWasEnabled = GL.IsEnabled(EnableCap.DepthTest);
        GL.Disable(EnableCap.DepthTest);
        GL.LineWidth(2.5f);

        // Two verts per line, 6 floats per vert (pos + unused normal).
        var buf = new float[12];
        for (int i = 0; i < MeasurementLines.Count; i++)
        {
            var seg = MeasurementLines[i];
            var a = seg.A * ModelScale;
            var b = seg.B * ModelScale;

            buf[0] = a.X; buf[1] = a.Y; buf[2] = a.Z; buf[3] = 0f; buf[4] = 0f; buf[5] = 0f;
            buf[6] = b.X; buf[7] = b.Y; buf[8] = b.Z; buf[9] = 0f; buf[10] = 0f; buf[11] = 0f;

            _debugShader.SetVector3("u_color", seg.Color.X, seg.Color.Y, seg.Color.Z);
            GL.BufferData(BufferTarget.ArrayBuffer, buf.Length * sizeof(float), buf, BufferUsageHint.DynamicDraw);
            GL.DrawArrays(PrimitiveType.Lines, 0, 2);
        }

        GL.LineWidth(1.0f);
        if (depthWasEnabled) GL.Enable(EnableCap.DepthTest);
        GL.BindVertexArray(0);
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

    /// <summary>
    /// Draws edges for every mesh with <see cref="GlMesh.ShowWireframe"/> set.
    /// Uses polygon-mode Line with a negative polygon offset so the wire sits
    /// just in front of the solid surface without z-fighting.
    /// </summary>
    private void DrawWireframeOverlay(ref Matrix4 model, ref Matrix4 view, ref Matrix4 projection)
    {
        if (_wireframeShader == null) return;

        // Early exit if nothing wants wireframe — typical case.
        bool any = false;
        for (int i = 0; i < _meshes.Count; i++)
        {
            if (_meshes[i].IsRendering && _meshes[i].ShowWireframe) { any = true; break; }
        }
        if (!any) return;

        _wireframeShader.Use();
        _wireframeShader.SetMatrix4("u_model", ref model);
        _wireframeShader.SetMatrix4("u_view", ref view);
        _wireframeShader.SetMatrix4("u_projection", ref projection);
        _wireframeShader.SetVector3("u_color", WireframeColor.X, WireframeColor.Y, WireframeColor.Z);

        GL.PolygonMode(MaterialFace.FrontAndBack, PolygonMode.Line);
        GL.Enable(EnableCap.PolygonOffsetLine);
        GL.PolygonOffset(-1.0f, -1.0f);
        GL.Disable(EnableCap.CullFace);
        GL.LineWidth(1.0f);

        foreach (var mesh in _meshes)
        {
            if (!mesh.IsRendering) continue;
            if (!mesh.ShowWireframe) continue;

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
    /// </summary>
    private void DrawDirectionalLightArrows(ref Matrix4 view, ref Matrix4 projection)
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

        for (int i = 1; i <= 3; i++)
        {
            if (Lights[i].Type != 2) continue;
            if (Lights[i].Intensity <= 0f) continue;

            var toLight = Lights[i].Direction;
            if (toLight.LengthSquared < 1e-8f) continue;

            var shineDir = -toLight;
            shineDir.Normalize();

            // Visual length mapping: short saturated tail at 0% intensity, grows
            // with intensity but compressed above 100% so super-bright lights
            // don't fly off the viewport. Character is ~128 Skyrim units tall,
            // so a ~55-unit arrow at 100% reads clearly without crowding the model.
            float intensityVis = MathF.Min(Lights[i].Intensity, 3.0f);
            float length = 12f + 45f * MathF.Min(intensityVis, 1f)
                               + 12f * MathF.Max(intensityVis - 1f, 0f);

            bool selected = SelectedLightIndex == i;
            float radius = length * (selected ? 0.040f : 0.030f);
            float headLen = length * 0.22f;
            float headRad = length * (selected ? 0.085f : 0.065f);
            float shaftLen = length - headLen;

            var tip = KeyLightVisualizationTarget;
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
        GL.ActiveTexture(TextureUnit.Texture6);
        GL.BindTexture(TextureTarget.Texture2D, mesh.EnvMapTexture);
        GL.ActiveTexture(TextureUnit.Texture7);
        GL.BindTexture(TextureTarget.Texture2D, mesh.EnvMaskTexture);

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
        _shader.SetBool("has_detail_map", mesh.HasDetailMap);
        _shader.SetBool("is_eye", mesh.IsEye);
        _shader.SetBool("use_alpha_test", mesh.UseAlphaTest);

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
}
