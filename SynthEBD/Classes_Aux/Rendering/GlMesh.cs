using System;
using System.Numerics;
using OpenTK.Graphics.OpenGL4;

namespace SynthEBD;

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
    public bool IsRendering { get; set; } = true;
    public bool IsEye { get; set; }

    // Material properties
    public float AlphaThreshold { get; set; }
    public float GreyscaleToPaletteScale { get; set; } = 1f;
    public Vector3 TintColor { get; set; } = Vector3.One;
    public float MaterialGlossiness { get; set; } = 80f;
    public float MaterialSpecularStrength { get; set; } = 1f;
    public Vector3 SpecularColor { get; set; } = Vector3.One;
    public float RimlightPower { get; set; } = 2f;
    public float SubsurfaceRolloff { get; set; } = 0.3f;
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

    // Detail map
    public int DetailTexture { get; set; }
    public bool HasDetailMap { get; set; }

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

    // Metadata
    public string ShapeName { get; set; } = "";
    public string BodyPart { get; set; } = "";
    public bool IsPrimaryHeadShape { get; set; }

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
