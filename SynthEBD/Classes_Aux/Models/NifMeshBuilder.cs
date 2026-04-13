using System;
using System.Collections.Generic;
using HelixToolkit;
using nifly;
using SysVector2 = System.Numerics.Vector2;
using SysVector3 = System.Numerics.Vector3;
using NiHeader = nifly.NiHeader;
using NiObject = nifly.NiObject;

namespace SynthEBD;

/// <summary>
/// Converts NIF file mesh data (via niflysharp) into HelixToolkit-compatible geometry
/// for 3D rendering in the character viewer.
/// </summary>
public class NifMeshBuilder
{
    private readonly Logger _logger;

    public NifMeshBuilder(Logger logger)
    {
        _logger = logger;
    }
    /// <summary>
    /// Result of building a single NIF shape into renderable data.
    /// </summary>
    public class BuiltMesh
    {
        public required Vector3Collection Positions { get; init; }
        public required Vector3Collection Normals { get; init; }
        public required IntCollection Indices { get; init; }
        public required Vector2Collection TextureCoordinates { get; init; }
        public required string ShapeName { get; init; }

        /// <summary>
        /// Texture paths extracted from the shape's BSShaderTextureSet, indexed by slot.
        /// Slot 0 = diffuse, 1 = normal, 2 = glow/skin tint, 7 = specular, etc.
        /// </summary>
        public Dictionary<int, string> TexturePaths { get; init; } = new();

        /// <summary>
        /// True if this shape's BSLightingShaderProperty has the SLSF1_Model_Space_Normals
        /// flag set (bit 28 of shaderFlags1). MSN textures encode normals in the mesh's
        /// model coordinate space rather than tangent space.
        /// </summary>
        public bool IsModelSpaceNormals { get; init; }
    }

    /// <summary>
    /// Bit 12 of BSLightingShaderProperty.shaderFlags1 — SLSF1_Model_Space_Normals.
    /// The NPC Portrait Creator's ParseShaderFlags incorrectly mapped this to bit 28,
    /// but its actual detection used nifly's NiShader::IsModelSpace() which checks bit 12.
    /// Verified: skin shapes have shaderFlags1=0x82601303, and 0x1303 has bit 12 set.
    /// </summary>
    private const uint SLSF1_ModelSpaceNormals = 1u << 12;

    /// <summary>
    /// Loads all renderable shapes from a NIF file and converts them to HelixToolkit geometry.
    /// </summary>
    public List<BuiltMesh> BuildFromFile(string nifPath)
    {
        var results = new List<BuiltMesh>();

        using var nif = new NifFile();
        if (nif.Load(nifPath) != 0)
            return results;

        using var shapes = nif.GetShapes();
        for (int si = 0; si < shapes.Count; si++)
        {
            var shape = shapes[si];
            var built = BuildShape(nif, shape);
            if (built != null)
                results.Add(built);
        }

        return results;
    }

    /// <summary>
    /// Loads all renderable shapes from an already-open NifFile.
    /// The caller owns the NifFile lifetime.
    /// </summary>
    public List<BuiltMesh> BuildFromNif(NifFile nif)
    {
        var results = new List<BuiltMesh>();

        using var shapes = nif.GetShapes();
        for (int si = 0; si < shapes.Count; si++)
        {
            var shape = shapes[si];
            var built = BuildShape(nif, shape);
            if (built != null)
                results.Add(built);
        }

        return results;
    }

    private BuiltMesh? BuildShape(NifFile nif, NiShape shape)
    {
        // Extract vertices
        using var nifVerts = nif.GetVertsForShape(shape);
        if (nifVerts == null || nifVerts.Count == 0)
            return null;

        // Extract triangles
        using var nifTris = new vectorTriangle();
        if (!shape.GetTriangles(nifTris) || nifTris.Count == 0)
            return null;

        int vertCount = nifVerts.Count;

        // Extract normals (may be null)
        using var nifNormals = nif.GetNormalsForShape(shape);

        // Extract UVs (may be null)
        using var nifUvs = nif.GetUvsForShape(shape);

        // Get shape transform (local to NIF root, in NIF Z-up space)
        var shapeTransform = shape.transform;
        bool hasTransform = shapeTransform != null
                            && (shapeTransform.scale != 1.0f
                                || !shapeTransform.rotation.IsIdentity()
                                || !IsZeroTranslation(shapeTransform.translation));

        // Build positions — apply shape transform then convert Z-up → Y-up
        var positions = new Vector3Collection(vertCount);
        for (int i = 0; i < vertCount; i++)
        {
            var v = nifVerts[i];
            float px = v.x, py = v.y, pz = v.z;

            if (hasTransform)
            {
                ApplyTransform(shapeTransform!, px, py, pz, out px, out py, out pz);
            }

            // NIF is Z-up, HelixToolkit is Y-up:
            // X stays, Y = Z_nif, Z = -Y_nif
            positions.Add(new SysVector3(px, pz, -py));
        }

        // Build normals (same coordinate conversion, rotation only)
        var normals = new Vector3Collection(vertCount);
        if (nifNormals != null && nifNormals.Count == vertCount)
        {
            bool hasRotation = hasTransform && !shapeTransform!.rotation.IsIdentity();
            for (int i = 0; i < vertCount; i++)
            {
                var n = nifNormals[i];
                float nx = n.x, ny = n.y, nz = n.z;

                if (hasRotation)
                {
                    ApplyRotation(shapeTransform!.rotation, nx, ny, nz, out nx, out ny, out nz);
                }

                // Z-up → Y-up
                normals.Add(new SysVector3(nx, nz, -ny));
            }
        }
        else
        {
            for (int i = 0; i < vertCount; i++)
                normals.Add(SysVector3.Zero);
        }

        // Build UVs
        var uvs = new Vector2Collection(vertCount);
        if (nifUvs != null && nifUvs.Count == vertCount)
        {
            for (int i = 0; i < vertCount; i++)
            {
                var uv = nifUvs[i];
                uvs.Add(new SysVector2(uv.u, uv.v));
            }
        }
        else
        {
            for (int i = 0; i < vertCount; i++)
                uvs.Add(SysVector2.Zero);
        }

        // Build triangle indices
        var indices = new IntCollection(nifTris.Count * 3);
        for (int i = 0; i < nifTris.Count; i++)
        {
            var tri = nifTris[i];
            indices.Add(tri.p1);
            indices.Add(tri.p2);
            indices.Add(tri.p3);
        }

        // Extract texture paths from BSShaderTextureSet
        var texturePaths = new Dictionary<int, string>();
        for (uint slot = 0; slot < 9; slot++)
        {
            string texPath = nif.GetTexturePathByIndex(shape, slot);
            if (!string.IsNullOrWhiteSpace(texPath))
                texturePaths[(int)slot] = texPath;
        }

        // Extract shader flags to detect model-space normals
        bool isModelSpaceNormals = false;
        try
        {
            NiHeader header = nif.GetHeader();
            NiBlockRefNiShader shaderRef = shape.ShaderPropertyRef();
            if (shaderRef != null && !shaderRef.IsEmpty())
            {
                NiObject shaderObj = header.GetBlockById(shaderRef.index);
                if (shaderObj is BSLightingShaderProperty bslsp)
                {
                    isModelSpaceNormals = (bslsp.shaderFlags1 & SLSF1_ModelSpaceNormals) != 0;
                    _logger.LogMessage("CharacterViewer: Shape '" + (shape.name?.get() ?? "?") +
                        "' shaderFlags1=0x" + bslsp.shaderFlags1.ToString("X8") +
                        " shaderType=" + bslsp.bslspShaderType +
                        " isModelSpaceNormals=" + isModelSpaceNormals);
                }
                else
                {
                    _logger.LogMessage("CharacterViewer: Shape '" + (shape.name?.get() ?? "?") +
                        "' shader is not BSLightingShaderProperty (type: " +
                        (shaderObj?.GetType().Name ?? "null") + ")");
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogMessage("CharacterViewer: Could not read shader flags for shape '" +
                (shape.name?.get() ?? "?") + "': " + ex.Message);
        }

        // If normals are all zero (missing or unreadable), compute from geometry
        if (AreNormalsAllZero(normals))
        {
            _logger.LogMessage("CharacterViewer: Normals all zero for shape '" +
                (shape.name?.get() ?? "?") + "', computing from geometry");
            ComputeNormalsFromGeometry(positions, indices, normals);
        }

        string shapeName = shape.name?.get() ?? $"Shape_{positions.Count}v";

        _logger.LogMessage("CharacterViewer: Built shape '" + shapeName +
            "': " + positions.Count + " verts, " + (indices.Count / 3) + " tris" +
            ", textures: [" + string.Join(", ", texturePaths.Keys) + "]" +
            ", MSN=" + isModelSpaceNormals);

        return new BuiltMesh
        {
            Positions = positions,
            Normals = normals,
            Indices = indices,
            TextureCoordinates = uvs,
            ShapeName = shapeName,
            TexturePaths = texturePaths,
            IsModelSpaceNormals = isModelSpaceNormals
        };
    }

    /// <summary>
    /// Applies a nifly MatTransform (rotation * (point * scale) + translation) to a point.
    /// </summary>
    private static void ApplyTransform(MatTransform transform, float x, float y, float z,
        out float rx, out float ry, out float rz)
    {
        float sx = x * transform.scale;
        float sy = y * transform.scale;
        float sz = z * transform.scale;

        using var srcVec = new nifly.Vector3();
        srcVec.x = sx;
        srcVec.y = sy;
        srcVec.z = sz;
        using var rotated = transform.rotation.opMult(srcVec);

        rx = rotated.x + transform.translation.x;
        ry = rotated.y + transform.translation.y;
        rz = rotated.z + transform.translation.z;
    }

    /// <summary>
    /// Applies only the rotation component of a transform to a direction vector.
    /// </summary>
    private static void ApplyRotation(Matrix3 rotation, float x, float y, float z,
        out float rx, out float ry, out float rz)
    {
        using var srcVec = new nifly.Vector3();
        srcVec.x = x;
        srcVec.y = y;
        srcVec.z = z;
        using var rotated = rotation.opMult(srcVec);
        rx = rotated.x;
        ry = rotated.y;
        rz = rotated.z;
    }

    private static bool IsZeroTranslation(nifly.Vector3 v)
    {
        const float eps = 0.0001f;
        return Math.Abs(v.x) < eps && Math.Abs(v.y) < eps && Math.Abs(v.z) < eps;
    }

    private static bool AreNormalsAllZero(Vector3Collection normals)
    {
        const float eps = 0.0001f;
        foreach (var n in normals)
        {
            if (Math.Abs(n.X) > eps || Math.Abs(n.Y) > eps || Math.Abs(n.Z) > eps)
                return false;
        }
        return true;
    }

    /// <summary>
    /// Computes smooth vertex normals by averaging face normals of adjacent triangles.
    /// </summary>
    private static void ComputeNormalsFromGeometry(Vector3Collection positions, IntCollection indices,
        Vector3Collection normals)
    {
        // Zero out existing normals
        for (int i = 0; i < normals.Count; i++)
            normals[i] = SysVector3.Zero;

        // Accumulate face normals onto vertices
        for (int i = 0; i < indices.Count; i += 3)
        {
            int i0 = indices[i], i1 = indices[i + 1], i2 = indices[i + 2];
            var v0 = positions[i0];
            var v1 = positions[i1];
            var v2 = positions[i2];

            var edge1 = v1 - v0;
            var edge2 = v2 - v0;
            var faceNormal = SysVector3.Cross(edge1, edge2);

            normals[i0] += faceNormal;
            normals[i1] += faceNormal;
            normals[i2] += faceNormal;
        }

        // Normalize
        for (int i = 0; i < normals.Count; i++)
        {
            var n = normals[i];
            float len = n.Length();
            normals[i] = len > 0.0001f ? n / len : new SysVector3(0, 1, 0);
        }
    }
}
