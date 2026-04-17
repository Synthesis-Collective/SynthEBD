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
    private int _debugVao;
    private int _debugVbo;
    private readonly List<GlMesh> _meshes = new();
    private bool _initialized;
    private bool _disposed;

    /// <summary>When true, renders arrow gizmos showing each directional light's shining
    /// direction (from source to model) and magnitude (length scales with intensity).</summary>
    public bool ShowKeyLightVisualization { get; set; } = false;

    /// <summary>World-space point the arrows point at — matches the orbit camera target.</summary>
    public Vector3 KeyLightVisualizationTarget { get; set; } = new Vector3(0f, 85f, 0f);

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

        _debugVao = GL.GenVertexArray();
        _debugVbo = GL.GenBuffer();
        GL.BindVertexArray(_debugVao);
        GL.BindBuffer(BufferTarget.ArrayBuffer, _debugVbo);
        GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 3 * sizeof(float), 0);
        GL.EnableVertexAttribArray(0);
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
        var model = Matrix4.Identity; // meshes are pre-transformed to world space

        _shader.SetMatrix4("u_model", ref model);
        _shader.SetMatrix4("u_view", ref view);
        _shader.SetMatrix4("u_projection", ref projection);

        // Set light uniforms — transform directions to view space
        var viewMat3 = new Matrix3(view);
        for (int i = 0; i < 5; i++)
        {
            string prefix = "lights[" + i + "].";
            _shader.SetInt(prefix + "type", Lights[i].Type);
            if (Lights[i].Type == 2)
            {
                // Transform light direction from world space to view space
                var viewDir = viewMat3 * Lights[i].Direction;
                viewDir.Normalize();
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

        // Overlay: directional-light direction arrows. Drawn last with depth test off
        // so they behave like gizmos (always visible through the model).
        if (ShowKeyLightVisualization)
            DrawDirectionalLightArrows(ref view, ref projection);
    }

    /// <summary>
    /// Renders wireframe arrows showing each enabled directional light's shining
    /// direction (source → model) and magnitude. The stored
    /// <see cref="LightData.Direction"/> is the surface-to-light vector (see the
    /// shader's NdotL convention), so arrows point along its negation.
    /// Arrow color per light slot: key=yellow, fill=cyan, rim=magenta.
    /// </summary>
    private void DrawDirectionalLightArrows(ref Matrix4 view, ref Matrix4 projection)
    {
        if (_debugShader == null) return;

        _debugShader.Use();
        _debugShader.SetMatrix4("u_view", ref view);
        _debugShader.SetMatrix4("u_projection", ref projection);

        GL.BindVertexArray(_debugVao);
        GL.BindBuffer(BufferTarget.ArrayBuffer, _debugVbo);

        bool depthWasEnabled = GL.IsEnabled(EnableCap.DepthTest);
        bool cullWasEnabled = GL.IsEnabled(EnableCap.CullFace);
        GL.Disable(EnableCap.DepthTest);
        GL.Disable(EnableCap.CullFace);
        GL.LineWidth(2.5f);

        for (int i = 0; i < Lights.Length; i++)
        {
            if (Lights[i].Type != 2) continue;
            if (Lights[i].Intensity <= 0f) continue;

            var toLight = Lights[i].Direction;
            if (toLight.LengthSquared < 1e-8f) continue;

            var shineDir = -toLight;
            shineDir.Normalize();

            float intensity01 = Math.Clamp(Lights[i].Intensity, 0f, 1f);
            float length = 20f + 130f * intensity01;

            var tip = KeyLightVisualizationTarget;
            var tail = tip - shineDir * length;

            // Orthonormal basis perpendicular to the shaft for the 4 head fins
            var up = MathF.Abs(shineDir.Y) < 0.95f ? Vector3.UnitY : Vector3.UnitX;
            var u = Vector3.Normalize(Vector3.Cross(shineDir, up));
            var v = Vector3.Normalize(Vector3.Cross(shineDir, u));

            float headLen = length * 0.2f;
            float headWid = length * 0.1f;
            var back = tip - shineDir * headLen;
            var fin1 = back + u * headWid;
            var fin2 = back - u * headWid;
            var fin3 = back + v * headWid;
            var fin4 = back - v * headWid;

            float[] vertices =
            {
                tail.X, tail.Y, tail.Z,
                tip.X,  tip.Y,  tip.Z,
                tip.X, tip.Y, tip.Z,  fin1.X, fin1.Y, fin1.Z,
                tip.X, tip.Y, tip.Z,  fin2.X, fin2.Y, fin2.Z,
                tip.X, tip.Y, tip.Z,  fin3.X, fin3.Y, fin3.Z,
                tip.X, tip.Y, tip.Z,  fin4.X, fin4.Y, fin4.Z,
            };

            var c = _arrowColors[Math.Min(i, _arrowColors.Length - 1)];
            _debugShader.SetVector3("u_color", c.X, c.Y, c.Z);

            GL.BufferData(BufferTarget.ArrayBuffer,
                vertices.Length * sizeof(float), vertices, BufferUsageHint.DynamicDraw);
            GL.DrawArrays(PrimitiveType.Lines, 0, 10);
        }

        GL.LineWidth(1.0f);
        if (depthWasEnabled) GL.Enable(EnableCap.DepthTest);
        if (cullWasEnabled) GL.Enable(EnableCap.CullFace);

        GL.BindVertexArray(0);
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
    /// faces world +Z, so az=180° must produce a +Z direction to put the light in
    /// front of the character — hence the negated Z term.</summary>
    private static Vector3 DirectionFromAzEl(float azimuthDeg, float elevationDeg)
    {
        float az = MathHelper.DegreesToRadians(azimuthDeg);
        float el = MathHelper.DegreesToRadians(elevationDeg);
        return new Vector3(
            MathF.Cos(el) * MathF.Sin(az),
            MathF.Sin(el),
            -MathF.Cos(el) * MathF.Cos(az));
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            ClearMeshes();
            _shader?.Dispose();
            _debugShader?.Dispose();
            if (_debugVbo != 0) GL.DeleteBuffer(_debugVbo);
            if (_debugVao != 0) GL.DeleteVertexArray(_debugVao);
            _disposed = true;
        }
    }
}
