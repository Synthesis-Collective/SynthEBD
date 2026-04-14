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

        /// <summary>
        /// True if this shape uses the Greyscale-to-Palette hair tint shader
        /// (BSLightingShaderProperty.bslspShaderType == BSLSP_HAIRTINT).
        /// The diffuse texture is a greyscale mask that should be multiplied by the tint color.
        /// </summary>
        public bool IsHairTintShader { get; init; }

        /// <summary>
        /// Hair tint color from BSLightingShaderProperty.hairTintColor (RGB, 0–1 range).
        /// Only valid when <see cref="IsHairTintShader"/> is true.
        /// </summary>
        public (float R, float G, float B)? HairTintColor { get; init; }
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

        return BuildAllShapes(nif);
    }

    /// <summary>
    /// Loads all renderable shapes from an already-open NifFile.
    /// The caller owns the NifFile lifetime.
    /// </summary>
    public List<BuiltMesh> BuildFromNif(NifFile nif)
    {
        return BuildAllShapes(nif);
    }

    /// <summary>
    /// Shared implementation: finds the primary head shape (if any), computes accessory
    /// offset transforms, and builds all shapes with correct positioning.
    /// </summary>
    private List<BuiltMesh> BuildAllShapes(NifFile nif)
    {
        var results = new List<BuiltMesh>();
        using var shapes = nif.GetShapes();
        if (shapes.Count == 0) return results;

        // --- Pre-pass: Find the primary head shape for accessory positioning ---
        // FaceGen NIFs contain a main face mesh plus accessories (brow, eyes, mouth, scars)
        // that may have identity transforms with vertices near the origin. We detect the
        // primary head (tallest mesh among head-partition shapes) and use its global transform
        // to correctly position accessories that would otherwise appear at the feet.
        var accessoryOffset = FindAccessoryOffset(nif, shapes);

        for (int si = 0; si < shapes.Count; si++)
        {
            var shape = shapes[si];
            var built = BuildShape(nif, shape, accessoryOffset);
            if (built != null)
                results.Add(built);
        }

        return results;
    }

    /// <summary>
    /// Skyrim head dismember partition IDs: SBP_30_HEAD, SBP_130_HEAD, SBP_230_HEAD.
    /// </summary>
    private static bool IsHeadDismemberPartition(ushort partId)
    {
        return partId == 30 || partId == 130 || partId == 230;
    }

    /// <summary>
    /// Walks the NIF scene graph from an object up to the root, composing transforms
    /// to get the object's transform in NIF root space (Z-up).
    /// Equivalent to NPC Portrait Creator's GetAVObjectTransformToGlobal.
    /// </summary>
    private static MatTransform GetTransformToGlobal(NifFile nif, NiAVObject obj)
    {
        // GetTransformToParent returns a non-owning wrapper (cMemoryOwn=false),
        // ComposeTransforms returns an owning copy (cMemoryOwn=true).
        // Intermediate objects are small and will be cleaned up by the GC finalizer.
        var result = obj.GetTransformToParent();
        var parent = nif.GetParentNode(obj);

        while (parent != null)
        {
            var parentXform = parent.GetTransformToParent();
            result = parentXform.ComposeTransforms(result);
            parent = nif.GetParentNode(parent);
        }

        return result;
    }

    /// <summary>
    /// Pre-pass to find the primary head shape's global transform for accessory positioning.
    /// Returns null if no head shapes are found (e.g. body NIFs).
    /// </summary>
    private MatTransform? FindAccessoryOffset(NifFile nif, vectorNiShape shapes)
    {
        NiHeader header = nif.GetHeader();

        // Collect head-partition candidate shapes with their local Z extent (height)
        NiShape? primaryHead = null;
        float primaryHeadHeight = -1f;

        for (int si = 0; si < shapes.Count; si++)
        {
            var shape = shapes[si];
            var skinRef = shape.SkinInstanceRef();
            if (skinRef == null || skinRef.IsEmpty()) continue;

            NiObject skinObj = header.GetBlockById(skinRef.index);
            if (skinObj is not BSDismemberSkinInstance dismember) continue;

            // Check if any partition is a head partition
            bool isHeadCandidate = false;
            using var partitions = dismember.partitions;
            if (partitions != null)
            {
                using var items = partitions.items();
                for (int pi = 0; pi < items.Count; pi++)
                {
                    if (IsHeadDismemberPartition(items[pi].partID))
                    {
                        isHeadCandidate = true;
                        break;
                    }
                }
            }

            if (!isHeadCandidate) continue;

            // Measure local Z extent to find the tallest (primary) head shape
            using var verts = nif.GetVertsForShape(shape);
            if (verts == null || verts.Count == 0) continue;

            float minZ = float.MaxValue, maxZ = float.MinValue;
            for (int vi = 0; vi < verts.Count; vi++)
            {
                float z = verts[vi].z;
                if (z < minZ) minZ = z;
                if (z > maxZ) maxZ = z;
            }

            float height = maxZ - minZ;
            if (height > primaryHeadHeight)
            {
                primaryHeadHeight = height;
                primaryHead = shape;
            }
        }

        if (primaryHead == null) return null;

        _logger.LogMessage("CharacterViewer: Primary head shape identified: '" +
            (primaryHead.name?.get() ?? "?") + "' (height=" + primaryHeadHeight.ToString("F1") + ")");

        var offset = GetTransformToGlobal(nif, primaryHead);
        return offset;
    }

    private BuiltMesh? BuildShape(NifFile nif, NiShape shape, MatTransform? accessoryOffset)
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

        // Compute the effective transform for this shape.
        // For most shapes, this is the composed transform from shape to NIF root.
        // For FaceGen accessories with near-zero translation, we use the primary head's
        // transform so they're positioned correctly instead of appearing at the feet.
        var shapeTransform = ComputeEffectiveTransform(nif, shape, accessoryOffset);
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

        // Extract shader flags to detect model-space normals and hair tint
        bool isModelSpaceNormals = false;
        bool isHairTintShader = false;
        (float R, float G, float B)? hairTintColor = null;
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

                    // Detect hair tint shader (BSLSP_HAIRTINT = 6)
                    if (bslsp.bslspShaderType == (uint)BSLightingShaderPropertyShaderType.BSLSP_HAIRTINT)
                    {
                        isHairTintShader = true;
                        var tint = bslsp.hairTintColor;
                        if (tint != null)
                        {
                            hairTintColor = (tint.x, tint.y, tint.z);
                        }
                    }

                    _logger.LogMessage("CharacterViewer: Shape '" + (shape.name?.get() ?? "?") +
                        "' shaderType=" + bslsp.bslspShaderType +
                        " isModelSpaceNormals=" + isModelSpaceNormals +
                        " isHairTint=" + isHairTintShader +
                        (hairTintColor.HasValue
                            ? " tintColor=(" + hairTintColor.Value.R.ToString("F2") + "," +
                              hairTintColor.Value.G.ToString("F2") + "," +
                              hairTintColor.Value.B.ToString("F2") + ")"
                            : ""));
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
            IsModelSpaceNormals = isModelSpaceNormals,
            IsHairTintShader = isHairTintShader,
            HairTintColor = hairTintColor
        };
    }

    /// <summary>
    /// Computes the effective transform for a shape, applying the accessory positioning
    /// heuristic from NPC Portrait Creator. For shapes whose composed global transform
    /// has near-zero translation (accessories like brow, eyes, mouth in FaceGen NIFs),
    /// the primary head's global transform is used instead.
    /// </summary>
    private MatTransform ComputeEffectiveTransform(NifFile nif, NiShape shape, MatTransform? accessoryOffset)
    {
        // Get the full composed transform from shape to NIF root
        var globalTransform = GetTransformToGlobal(nif, shape);

        // If no accessory offset was found (not a head NIF), use the global transform as-is
        if (accessoryOffset == null)
            return globalTransform;

        // Accessory heuristic: if this shape's global transform has near-zero translation,
        // it's likely a FaceGen accessory (brow, eyes, mouth) whose vertices are in local
        // bone space. Apply the primary head's transform to position it correctly.
        const float ZERO_TRANSLATION_THRESHOLD = 0.1f;
        float translationLength = (float)Math.Sqrt(
            globalTransform.translation.x * globalTransform.translation.x +
            globalTransform.translation.y * globalTransform.translation.y +
            globalTransform.translation.z * globalTransform.translation.z);

        if (translationLength < ZERO_TRANSLATION_THRESHOLD)
        {
            _logger.LogMessage("CharacterViewer: Shape '" + (shape.name?.get() ?? "?") +
                "' has near-zero translation (" + translationLength.ToString("F3") +
                "), applying primary head offset for correct positioning");
            return accessoryOffset;
        }

        return globalTransform;
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
