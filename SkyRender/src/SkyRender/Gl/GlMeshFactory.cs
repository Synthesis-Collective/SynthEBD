using System;
using System.Numerics;

namespace SkyRender;

/// <summary>
/// Uploads a parsed NIF shape (<see cref="NifMeshBuilder.BuiltMesh"/>) as a <see cref="GlMesh"/>,
/// and builds the interleaved vertex layout <see cref="GlMesh.Upload"/> expects. Requires a
/// current GL context. Bind materials afterwards with <see cref="NifMaterialBinder.Bind"/>.
/// </summary>
public static class GlMeshFactory
{
    /// <summary>Floats per vertex in the interleaved layout.</summary>
    public const int FloatsPerVertex = 18;

    /// <summary>
    /// Creates and uploads a GL mesh for <paramref name="built"/>, keeping CPU-side copies of
    /// positions and indices (for ray hit testing) and of bone indices and weights (for
    /// skinned shapes).
    /// </summary>
    public static GlMesh Create(NifMeshBuilder.BuiltMesh built)
    {
        var vertexData = Interleave(
            built.Positions, built.Normals, built.TextureCoordinates,
            built.Tangents, built.Bitangents, built.VertexColors);

        var glMesh = new GlMesh();
        glMesh.Upload(vertexData, built.Indices);
        glMesh.ShapeName = built.ShapeName;
        glMesh.IsPrimaryHeadShape = built.IsPrimaryHeadShape;

        // Store CPU-side geometry for ray-based hit testing
        glMesh.CpuPositions = (Vector3[])built.Positions.Clone();
        glMesh.CpuIndices = (int[])built.Indices.Clone();

        // Persist per-vertex bone indices + weights from the SkinningInfo so hosts can query
        // which bones drive each vertex. Cloned because the BuiltMesh is shared via the NIF
        // parse cache and later deformations re-read it. Null for unskinned shapes.
        if (built.Skinning != null && built.Skinning.VertBoneIndices.Length > 0)
        {
            glMesh.CpuBoneIndices = (int[])built.Skinning.VertBoneIndices.Clone();
            glMesh.CpuBoneWeights = (float[])built.Skinning.VertBoneWeights.Clone();
        }

        return glMesh;
    }

    /// <summary>
    /// Builds the interleaved vertex data array for a GL mesh.
    /// Layout per vertex: position(3) + normal(3) + texcoord(2) + color(4) + tangent(3) + bitangent(3) = 18 floats.
    /// Missing per-vertex attributes default to normal +Y, UV 0, white colour, zero tangent frame.
    /// </summary>
    public static float[] Interleave(
        Vector3[] positions, Vector3[] normals, Vector2[] uvs,
        Vector3[] tangents, Vector3[] bitangents, Vector4[]? vertexColors = null)
    {
        int vertCount = positions.Length;
        var data = new float[vertCount * FloatsPerVertex];

        for (int i = 0; i < vertCount; i++)
        {
            int offset = i * FloatsPerVertex;
            var p = positions[i];
            var n = i < normals.Length ? normals[i] : Vector3.UnitY;
            var uv = i < uvs.Length ? uvs[i] : Vector2.Zero;
            var t = i < tangents.Length ? tangents[i] : Vector3.Zero;
            var b = i < bitangents.Length ? bitangents[i] : Vector3.Zero;

            // Position
            data[offset]     = p.X;
            data[offset + 1] = p.Y;
            data[offset + 2] = p.Z;
            // Normal
            data[offset + 3] = n.X;
            data[offset + 4] = n.Y;
            data[offset + 5] = n.Z;
            // UV
            data[offset + 6] = uv.X;
            data[offset + 7] = uv.Y;
            // Vertex color
            if (vertexColors != null && i < vertexColors.Length)
            {
                var vc = vertexColors[i];
                data[offset + 8]  = vc.X;
                data[offset + 9]  = vc.Y;
                data[offset + 10] = vc.Z;
                data[offset + 11] = vc.W;
            }
            else
            {
                data[offset + 8]  = 1f;
                data[offset + 9]  = 1f;
                data[offset + 10] = 1f;
                data[offset + 11] = 1f;
            }
            // Tangent
            data[offset + 12] = t.X;
            data[offset + 13] = t.Y;
            data[offset + 14] = t.Z;
            // Bitangent
            data[offset + 15] = b.X;
            data[offset + 16] = b.Y;
            data[offset + 17] = b.Z;
        }

        return data;
    }
}
