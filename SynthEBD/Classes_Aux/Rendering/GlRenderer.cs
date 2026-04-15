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
    private readonly List<GlMesh> _meshes = new();
    private bool _initialized;
    private bool _disposed;

    // Lighting state — up to 5 lights
    public struct LightData
    {
        public int Type;       // 0=disabled, 1=ambient, 2=directional
        public Vector3 Direction; // in view space (set per-frame)
        public Vector3 Color;
        public float Intensity;
    }
    public LightData[] Lights { get; } = new LightData[5];

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
        Lights[2].Intensity = intensity01 * 0.38f;
        Lights[3].Intensity = intensity01 * 0.25f;
    }

    public void SetKeyLightDirection(float azimuthDeg, float elevationDeg)
    {
        float az = MathHelper.DegreesToRadians(azimuthDeg);
        float el = MathHelper.DegreesToRadians(elevationDeg);
        Lights[1].Direction = new Vector3(
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
            _disposed = true;
        }
    }
}
