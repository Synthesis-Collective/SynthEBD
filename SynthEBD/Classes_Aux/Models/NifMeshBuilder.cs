using System;
using System.Collections.Generic;
using System.Numerics;
using nifly;
using NiHeader = nifly.NiHeader;
using NiObject = nifly.NiObject;
using Vector2 = System.Numerics.Vector2;
using Vector3 = System.Numerics.Vector3;

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
        public required Vector3[] Positions { get; init; }
        public required Vector3[] Normals { get; init; }
        public required int[] Indices { get; init; }
        public required Vector2[] TextureCoordinates { get; init; }
        public required Vector3[] Tangents { get; init; }
        public required Vector3[] Bitangents { get; init; }
        public required string ShapeName { get; init; }

        /// <summary>
        /// Texture paths extracted from the shape's BSShaderTextureSet, indexed by slot.
        /// Slot 0 = diffuse, 1 = normal, 2 = glow/skin tint, 7 = specular, etc.
        /// </summary>
        public Dictionary<int, string> TexturePaths { get; init; } = new();

        /// <summary>
        /// True if this shape's BSLightingShaderProperty has the SLSF1_Model_Space_Normals flag set.
        /// MSN textures encode normals in the mesh's model coordinate space rather than tangent space.
        /// </summary>
        public bool IsModelSpaceNormals { get; init; }

        /// <summary>
        /// True if this shape uses the Greyscale-to-Palette hair tint shader.
        /// The diffuse texture is a greyscale mask that should be multiplied by the tint color.
        /// </summary>
        public bool IsHairTintShader { get; init; }

        /// <summary>
        /// Hair tint color from BSLightingShaderProperty.hairTintColor (RGB, 0–1 range).
        /// </summary>
        public (float R, float G, float B)? HairTintColor { get; init; }

        /// <summary>
        /// Bind-pose (unskinned) vertex positions in Y-up space. Null for unskinned shapes.
        /// </summary>
        public Vector3[]? BindPosePositions { get; init; }

        /// <summary>
        /// Bind-pose (unskinned) vertex normals in Y-up space. Null for unskinned shapes.
        /// </summary>
        public Vector3[]? BindPoseNormals { get; init; }

        /// <summary>
        /// Pre-computed skinning data for CPU-side bone-weight skinning.
        /// Null for unskinned shapes. Used to re-skin after BodySlide deformation.
        /// </summary>
        public SkinningInfo? Skinning { get; init; }

        /// <summary>
        /// True if this shape is the primary head mesh in a FaceGen NIF.
        /// </summary>
        public bool IsPrimaryHeadShape { get; init; }

        /// <summary>
        /// True if this shape's NiAlphaProperty has the alpha test flag set (bit 9).
        /// </summary>
        public bool HasAlphaTest { get; init; }

        /// <summary>
        /// True if this shape's NiAlphaProperty has the alpha blend flag set (bit 0).
        /// </summary>
        public bool HasAlphaBlend { get; init; }

        /// <summary>
        /// Alpha test threshold from NiAlphaProperty (0–1 range).
        /// </summary>
        public float AlphaThreshold { get; init; }

        /// <summary>
        /// True if this shape has SLSF2_Double_Sided (shaderFlags2 bit 4).
        /// </summary>
        public bool IsDoubleSided { get; init; }

        // --- Shader material properties from BSLightingShaderProperty ---
        public float Glossiness { get; init; } = 80f;
        public float SpecularStrength { get; init; } = 1f;
        public float SubsurfaceRolloff { get; init; }
        public float GreyscaleToPaletteScale { get; init; } = 1f;
        public float RimlightPower { get; init; } = 2f;
        public bool HasVertexColors { get; init; }

        /// <summary>Shader flags for detecting specular, soft lighting, hair soft lighting, etc.</summary>
        public uint ShaderFlags1 { get; init; }
        public uint ShaderFlags2 { get; init; }
        public uint ShaderType { get; init; }
    }

    /// <summary>
    /// Bit 12 of BSLightingShaderProperty.shaderFlags1 — SLSF1_Model_Space_Normals.
    /// The NPC Portrait Creator's ParseShaderFlags incorrectly mapped this to bit 28,
    /// but its actual detection used nifly's NiShader::IsModelSpace() which checks bit 12.
    /// Verified: skin shapes have shaderFlags1=0x82601303, and 0x1303 has bit 12 set.
    /// </summary>
    private const uint SLSF1_ModelSpaceNormals = 1u << 12;

    /// <summary>
    /// Pure C# representation of a nifly MatTransform, cached for fast per-vertex
    /// skinning without SWIG interop overhead in the inner loop.
    /// Stores a 3x3 rotation matrix + translation + uniform scale.
    /// </summary>
    internal struct CachedSkinTransform
    {
        public float R00, R01, R02;
        public float R10, R11, R12;
        public float R20, R21, R22;
        public float Tx, Ty, Tz;
        public float Scale;

        /// <summary>
        /// Applies this transform to a position: result = rotation * (pos * scale) + translation.
        /// </summary>
        public void Apply(float x, float y, float z, out float rx, out float ry, out float rz)
        {
            float sx = x * Scale, sy = y * Scale, sz = z * Scale;
            rx = R00 * sx + R01 * sy + R02 * sz + Tx;
            ry = R10 * sx + R11 * sy + R12 * sz + Ty;
            rz = R20 * sx + R21 * sy + R22 * sz + Tz;
        }

        /// <summary>
        /// Applies only the rotation to a direction vector (no scale, no translation).
        /// </summary>
        public void ApplyRotation(float x, float y, float z, out float rx, out float ry, out float rz)
        {
            rx = R00 * x + R01 * y + R02 * z;
            ry = R10 * x + R11 * y + R12 * z;
            rz = R20 * x + R21 * y + R22 * z;
        }
    }

    /// <summary>
    /// Stores pre-computed skinning data for a mesh, enabling re-skinning after
    /// BodySlide deformation without re-reading the NIF.
    /// </summary>
    public class SkinningInfo
    {
        /// <summary>Pre-computed skinning matrices (boneWorld * inverseBindPose) per bone, in NIF Z-up space.</summary>
        internal CachedSkinTransform[] BoneTransforms { get; init; } = Array.Empty<CachedSkinTransform>();

        /// <summary>Per-vertex bone indices, 4 per vertex, stored flat [v0_b0, v0_b1, v0_b2, v0_b3, v1_b0, ...].</summary>
        internal int[] VertBoneIndices { get; init; } = Array.Empty<int>();

        /// <summary>Per-vertex bone weights, 4 per vertex, stored flat (same layout as VertBoneIndices).</summary>
        internal float[] VertBoneWeights { get; init; } = Array.Empty<float>();

        /// <summary>Number of vertices this skinning data applies to.</summary>
        public int VertexCount { get; init; }
    }

    /// <summary>
    /// Loads all renderable shapes from a NIF file and converts them to HelixToolkit geometry.
    /// </summary>
    public List<BuiltMesh> BuildFromFile(string nifPath, NifFile? skeletonNif = null)
    {
        var results = new List<BuiltMesh>();

        using var nif = new NifFile();
        if (nif.Load(nifPath) != 0)
            return results;

        return BuildAllShapes(nif, skeletonNif);
    }

    /// <summary>
    /// Loads all renderable shapes from an already-open NifFile.
    /// The caller owns the NifFile lifetime.
    /// </summary>
    public List<BuiltMesh> BuildFromNif(NifFile nif, NifFile? skeletonNif = null)
    {
        return BuildAllShapes(nif, skeletonNif);
    }

    /// <summary>
    /// Shared implementation: finds the primary head shape (if any), computes accessory
    /// offset transforms, and builds all shapes with correct positioning.
    /// </summary>
    private List<BuiltMesh> BuildAllShapes(NifFile nif, NifFile? skeletonNif)
    {
        var results = new List<BuiltMesh>();
        using var shapes = nif.GetShapes();
        if (shapes.Count == 0) return results;

        // --- Pre-pass: Find the primary head shape for accessory positioning ---
        // FaceGen NIFs contain a main face mesh plus accessories (brow, eyes, mouth, scars)
        // that may have identity transforms with vertices near the origin. We detect the
        // primary head (tallest mesh among head-partition shapes) and use its global transform
        // to correctly position accessories that would otherwise appear at the feet.
        var (accessoryOffset, primaryHeadName) = FindAccessoryOffsetAndPrimaryHead(nif, shapes);

        for (int si = 0; si < shapes.Count; si++)
        {
            var shape = shapes[si];
            var built = BuildShape(nif, shape, accessoryOffset, skeletonNif, primaryHeadName);
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
    private MatTransform GetTransformToGlobal(NifFile nif, NiAVObject obj, string? shapeName = null)
    {
        // GetTransformToParent returns a non-owning wrapper (cMemoryOwn=false),
        // ComposeTransforms returns an owning copy (cMemoryOwn=true).
        // Intermediate objects are small and will be cleaned up by the GC finalizer.
        var result = obj.GetTransformToParent();
        var parent = nif.GetParentNode(obj);

        if (shapeName != null)
        {
            _logger.LogMessage("CharacterViewer: [Transform Chain] '" + shapeName +
                "' local: T=(" + result.translation.x.ToString("F2") + ", " +
                result.translation.y.ToString("F2") + ", " + result.translation.z.ToString("F2") +
                ") S=" + result.scale.ToString("F3") +
                " rotIdentity=" + result.rotation.IsIdentity());
        }

        while (parent != null)
        {
            var parentXform = parent.GetTransformToParent();
            if (shapeName != null)
            {
                string parentName = (parent as NiAVObject)?.name?.get() ?? "?";
                _logger.LogMessage("CharacterViewer: [Transform Chain] '" + shapeName +
                    "' parent '" + parentName +
                    "': T=(" + parentXform.translation.x.ToString("F2") + ", " +
                    parentXform.translation.y.ToString("F2") + ", " + parentXform.translation.z.ToString("F2") +
                    ") S=" + parentXform.scale.ToString("F3") +
                    " rotIdentity=" + parentXform.rotation.IsIdentity());
            }
            result = parentXform.ComposeTransforms(result);
            parent = nif.GetParentNode(parent);
        }

        if (shapeName != null)
        {
            _logger.LogMessage("CharacterViewer: [Transform Chain] '" + shapeName +
                "' composed global: T=(" + result.translation.x.ToString("F2") + ", " +
                result.translation.y.ToString("F2") + ", " + result.translation.z.ToString("F2") +
                ") S=" + result.scale.ToString("F3"));
        }

        return result;
    }

    /// <summary>
    /// Pre-pass to find the primary head shape's global transform for accessory positioning.
    /// Also returns the primary head shape's name for tagging in BuiltMesh.
    /// Returns null transform if no head shapes are found (e.g. body NIFs).
    /// </summary>
    private (MatTransform? offset, string? primaryHeadName) FindAccessoryOffsetAndPrimaryHead(NifFile nif, vectorNiShape shapes)
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
            var partIdList = new List<ushort>();
            using var partitions = dismember.partitions;
            if (partitions != null)
            {
                using var items = partitions.items();
                for (int pi = 0; pi < items.Count; pi++)
                {
                    partIdList.Add(items[pi].partID);
                    if (IsHeadDismemberPartition(items[pi].partID))
                    {
                        isHeadCandidate = true;
                    }
                }
            }

            string sName = shape.name?.get() ?? "?";
            _logger.LogMessage("CharacterViewer: [Skinning] Shape '" + sName +
                "' partitions=[" + string.Join(",", partIdList) +
                "] isHeadCandidate=" + isHeadCandidate);

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

        if (primaryHead == null) return (null, null);

        string headName = primaryHead.name?.get() ?? "?";
        _logger.LogMessage("CharacterViewer: Primary head shape identified: '" +
            headName + "' (height=" + primaryHeadHeight.ToString("F1") + ")");

        var offset = GetTransformToGlobal(nif, primaryHead, headName + " [primary head]");
        _logger.LogMessage("CharacterViewer: Accessory offset transform: T=(" +
            offset.translation.x.ToString("F2") + ", " +
            offset.translation.y.ToString("F2") + ", " + offset.translation.z.ToString("F2") +
            ") S=" + offset.scale.ToString("F3"));
        return (offset, headName);
    }

    private BuiltMesh? BuildShape(NifFile nif, NiShape shape, MatTransform? accessoryOffset,
        NifFile? skeletonNif = null, string? primaryHeadName = null)
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

        // --- CPU Skinning or Shape Transform ---
        // When a skeleton NIF is provided, CPU-side bone-weight skinning positions
        // vertices using the real skeleton's bone transforms. This closes the neck gap
        // between head and body meshes. Without a skeleton, we fall back to the shape
        // transform heuristic (ComputeEffectiveTransform).
        SkinningInfo? skinning = null;
        Vector3[]? bindPosePositionsYUp = null;
        Vector3[]? bindPoseNormalsYUp = null;
        float[]? skinnedPosX = null, skinnedPosY = null, skinnedPosZ = null;
        float[]? skinnedNrmX = null, skinnedNrmY = null, skinnedNrmZ = null;

        MatTransform? shapeTransform = null;
        bool hasTransform = false;

        if (skeletonNif != null && shape.HasSkinInstance())
        {
            skinning = TryApplyCpuSkinning(nif, shape, nifVerts, nifNormals, vertCount,
                skeletonNif,
                out skinnedPosX, out skinnedPosY, out skinnedPosZ,
                out skinnedNrmX, out skinnedNrmY, out skinnedNrmZ);
        }

        if (skinning == null)
        {
            // Fallback: use shape transform heuristic for unskinned shapes or when no skeleton
            shapeTransform = ComputeEffectiveTransform(nif, shape, accessoryOffset);
            hasTransform = shapeTransform != null
                            && (shapeTransform.scale != 1.0f
                                || !shapeTransform.rotation.IsIdentity()
                                || !IsZeroTranslation(shapeTransform.translation));
        }

        // Build positions — skinned or transform-based, then convert Z-up → Y-up
        var positions = new Vector3[vertCount];
        if (skinning != null)
        {
            bindPosePositionsYUp = new Vector3[vertCount];
            for (int i = 0; i < vertCount; i++)
            {
                var v = nifVerts[i];
                bindPosePositionsYUp[i] = new Vector3(v.x, v.z, -v.y);
                positions[i] = new Vector3(skinnedPosX![i], skinnedPosZ![i], -skinnedPosY![i]);
            }
        }
        else
        {
            for (int i = 0; i < vertCount; i++)
            {
                var v = nifVerts[i];
                float px = v.x, py = v.y, pz = v.z;
                if (hasTransform)
                    ApplyTransform(shapeTransform!, px, py, pz, out px, out py, out pz);
                positions[i] = new Vector3(px, pz, -py);
            }
        }

        // Build normals
        var normals = new Vector3[vertCount];
        if (skinning != null && skinnedNrmX != null)
        {
            bindPoseNormalsYUp = new Vector3[vertCount];
            for (int i = 0; i < vertCount; i++)
            {
                if (nifNormals != null && nifNormals.Count == vertCount)
                {
                    var n = nifNormals[i];
                    bindPoseNormalsYUp[i] = new Vector3(n.x, n.z, -n.y);
                }
                normals[i] = new Vector3(skinnedNrmX[i], skinnedNrmZ![i], -skinnedNrmY![i]);
            }
        }
        else if (nifNormals != null && nifNormals.Count == vertCount)
        {
            bool hasRotation = hasTransform && !shapeTransform!.rotation.IsIdentity();
            for (int i = 0; i < vertCount; i++)
            {
                var n = nifNormals[i];
                float nx = n.x, ny = n.y, nz = n.z;
                if (hasRotation)
                    ApplyRotation(shapeTransform!.rotation, nx, ny, nz, out nx, out ny, out nz);
                normals[i] = new Vector3(nx, nz, -ny);
            }
        }

        // Build UVs
        var uvs = new Vector2[vertCount];
        if (nifUvs != null && nifUvs.Count == vertCount)
        {
            for (int i = 0; i < vertCount; i++)
            {
                var uv = nifUvs[i];
                uvs[i] = new Vector2(uv.u, uv.v);
            }
        }

        // Build tangents and bitangents from NIF (Z-up → Y-up)
        var tangents = new Vector3[vertCount];
        var bitangents = new Vector3[vertCount];
        bool hasVertexColors = false;
        try
        {
            using var nifTangents = nif.GetTangentsForShape(shape);
            using var nifBitangents = nif.GetBitangentsForShape(shape);
            if (nifTangents != null && nifTangents.Count == vertCount &&
                nifBitangents != null && nifBitangents.Count == vertCount)
            {
                for (int i = 0; i < vertCount; i++)
                {
                    var t = nifTangents[i];
                    var b = nifBitangents[i];
                    // Z-up → Y-up conversion
                    tangents[i] = new Vector3(t.x, t.z, -t.y);
                    bitangents[i] = new Vector3(b.x, b.z, -b.y);
                }
            }
            else
            {
                // Compute tangents from positions/normals/UVs if NIF doesn't have them
                ComputeTangents(positions, normals, uvs, nifTris, tangents, bitangents);
            }
        }
        catch
        {
            ComputeTangents(positions, normals, uvs, nifTris, tangents, bitangents);
        }

        // Build triangle indices
        var indices = new int[nifTris.Count * 3];
        for (int i = 0; i < nifTris.Count; i++)
        {
            var tri = nifTris[i];
            indices[i * 3] = tri.p1;
            indices[i * 3 + 1] = tri.p2;
            indices[i * 3 + 2] = tri.p3;
        }

        // Extract texture paths from BSShaderTextureSet
        var texturePaths = new Dictionary<int, string>();
        for (uint slot = 0; slot < 9; slot++)
        {
            string texPath = nif.GetTexturePathByIndex(shape, slot);
            if (!string.IsNullOrWhiteSpace(texPath))
                texturePaths[(int)slot] = texPath;
        }

        // Extract shader flags and material properties
        bool isModelSpaceNormals = false;
        bool isHairTintShader = false;
        (float R, float G, float B)? hairTintColor = null;
        float glossiness = 80f;
        float specularStrength = 1f;
        float subsurfaceRolloff = 0f;
        float greyscaleToPaletteScale = 1f;
        float rimlightPower = 2f;
        uint shaderFlags1 = 0, shaderFlags2 = 0, shaderType = 0;
        try
        {
            NiHeader header = nif.GetHeader();
            NiBlockRefNiShader shaderRef = shape.ShaderPropertyRef();
            if (shaderRef != null && !shaderRef.IsEmpty())
            {
                NiObject shaderObj = header.GetBlockById(shaderRef.index);
                if (shaderObj is BSLightingShaderProperty bslsp)
                {
                    shaderFlags1 = bslsp.shaderFlags1;
                    shaderFlags2 = bslsp.shaderFlags2;
                    shaderType = bslsp.bslspShaderType;
                    isModelSpaceNormals = (shaderFlags1 & SLSF1_ModelSpaceNormals) != 0;
                    glossiness = bslsp.glossiness;
                    specularStrength = bslsp.specularStrength;

                    // Extract additional properties safely
                    try { subsurfaceRolloff = bslsp.subsurfaceRolloff; } catch { }
                    // greyscaleToPaletteScale is not exposed by niflysharp; default to 1.0
                    try { rimlightPower = bslsp.rimlightPower; } catch { }

                    if (bslsp.bslspShaderType == (uint)BSLightingShaderPropertyShaderType.BSLSP_HAIRTINT)
                    {
                        isHairTintShader = true;
                        var tint = bslsp.hairTintColor;
                        if (tint != null)
                            hairTintColor = (tint.x, tint.y, tint.z);
                    }

                    _logger.LogMessage("CharacterViewer: Shape '" + (shape.name?.get() ?? "?") +
                        "' shaderType=" + bslsp.bslspShaderType +
                        " MSN=" + isModelSpaceNormals +
                        " gloss=" + glossiness.ToString("F0") +
                        " specStr=" + specularStrength.ToString("F2") +
                        (isHairTintShader ? " HAIR_TINT" : ""));
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogMessage("CharacterViewer: Could not read shader flags for shape '" +
                (shape.name?.get() ?? "?") + "': " + ex.Message);
        }

        // Read NiAlphaProperty for alpha test/blend (brow, hair, and other transparent shapes)
        bool hasAlphaTest = false;
        bool hasAlphaBlend = false;
        float alphaThreshold = 0f;
        try
        {
            if (shape.HasAlphaProperty())
            {
                NiHeader alphaHeader = nif.GetHeader();
                var alphaRef = shape.AlphaPropertyRef();
                if (alphaRef != null && !alphaRef.IsEmpty())
                {
                    NiObject alphaObj = alphaHeader.GetBlockById(alphaRef.index);
                    if (alphaObj is NiAlphaProperty alphaProp)
                    {
                        ushort flags = alphaProp.flags;
                        // Bit 0 of NiAlphaProperty flags = alpha blend enable
                        hasAlphaBlend = (flags & 1) != 0;
                        // Bit 9 of NiAlphaProperty flags = alpha test enable
                        hasAlphaTest = (flags & (1 << 9)) != 0;
                        alphaThreshold = alphaProp.threshold / 255f;

                        // If alpha property exists but no flags set, default to alpha test
                        // (matches NPC Portrait Creator behavior)
                        if (!hasAlphaTest && !hasAlphaBlend)
                        {
                            hasAlphaTest = true;
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogMessage("CharacterViewer: Could not read NiAlphaProperty for shape '" +
                (shape.name?.get() ?? "?") + "': " + ex.Message);
        }

        // If normals are all zero (missing or unreadable), compute from geometry
        if (AreNormalsAllZero(normals))
        {
            _logger.LogMessage("CharacterViewer: Normals all zero for shape '" +
                (shape.name?.get() ?? "?") + "', computing from geometry");
            ComputeNormalsFromGeometry(positions, indices, normals);
        }

        string shapeName = shape.name?.get() ?? $"Shape_{positions.Length}v";

        bool isPrimaryHead = primaryHeadName != null && shapeName == primaryHeadName;
        bool isDoubleSided = (shaderFlags2 & (1u << 4)) != 0; // SLSF2_Double_Sided
        _logger.LogMessage("CharacterViewer: Built shape '" + shapeName +
            "': " + positions.Length + " verts, " + (indices.Length / 3) + " tris" +
            ", textures: [" + string.Join(", ", texturePaths.Keys) + "]" +
            ", MSN=" + isModelSpaceNormals +
            (hasAlphaTest ? ", alphaTest threshold=" + alphaThreshold.ToString("F2") : "") +
            (hasAlphaBlend ? ", alphaBlend" : "") +
            (isDoubleSided ? ", doubleSided" : "") +
            (isPrimaryHead ? ", PRIMARY_HEAD" : ""));

        return new BuiltMesh
        {
            Positions = positions,
            Normals = normals,
            Indices = indices,
            TextureCoordinates = uvs,
            Tangents = tangents,
            Bitangents = bitangents,
            ShapeName = shapeName,
            TexturePaths = texturePaths,
            IsModelSpaceNormals = isModelSpaceNormals,
            IsHairTintShader = isHairTintShader,
            HairTintColor = hairTintColor,
            BindPosePositions = bindPosePositionsYUp,
            BindPoseNormals = bindPoseNormalsYUp,
            Skinning = skinning,
            IsPrimaryHeadShape = isPrimaryHead,
            HasAlphaTest = hasAlphaTest,
            HasAlphaBlend = hasAlphaBlend,
            AlphaThreshold = alphaThreshold,
            IsDoubleSided = isDoubleSided,
            Glossiness = glossiness,
            SpecularStrength = specularStrength,
            SubsurfaceRolloff = subsurfaceRolloff,
            GreyscaleToPaletteScale = greyscaleToPaletteScale,
            RimlightPower = rimlightPower,
            HasVertexColors = hasVertexColors,
            ShaderFlags1 = shaderFlags1,
            ShaderFlags2 = shaderFlags2,
            ShaderType = shaderType,
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
        string shapeName = shape.name?.get() ?? "?";

        // Log vertex bounds in local space (before any transform) for diagnostics
        using var diagVerts = nif.GetVertsForShape(shape);
        if (diagVerts != null && diagVerts.Count > 0)
        {
            float minX = float.MaxValue, minY = float.MaxValue, minZ = float.MaxValue;
            float maxX = float.MinValue, maxY = float.MinValue, maxZ = float.MinValue;
            float sumX = 0, sumY = 0, sumZ = 0;
            for (int i = 0; i < diagVerts.Count; i++)
            {
                var v = diagVerts[i];
                sumX += v.x; sumY += v.y; sumZ += v.z;
                if (v.x < minX) minX = v.x; if (v.x > maxX) maxX = v.x;
                if (v.y < minY) minY = v.y; if (v.y > maxY) maxY = v.y;
                if (v.z < minZ) minZ = v.z; if (v.z > maxZ) maxZ = v.z;
            }
            int n = diagVerts.Count;
            _logger.LogMessage("CharacterViewer: [Skinning] '" + shapeName +
                "' localVerts(" + n + "): centroid=(" +
                (sumX / n).ToString("F1") + ", " + (sumY / n).ToString("F1") + ", " + (sumZ / n).ToString("F1") +
                ") bounds=[(" + minX.ToString("F1") + "," + minY.ToString("F1") + "," + minZ.ToString("F1") +
                ")..(" + maxX.ToString("F1") + "," + maxY.ToString("F1") + "," + maxZ.ToString("F1") + ")]");
        }

        // Get the full composed transform from shape to NIF root
        var globalTransform = GetTransformToGlobal(nif, shape, shapeName);

        // If no accessory offset was found (not a head NIF), use the global transform as-is
        if (accessoryOffset == null)
        {
            _logger.LogMessage("CharacterViewer: [Skinning] '" + shapeName +
                "' → using own global transform (no head NIF detected)");
            return globalTransform;
        }

        // Accessory heuristic: if this shape's global transform has near-zero translation,
        // it's likely a FaceGen accessory (brow, eyes, mouth) whose vertices are in local
        // bone space. Apply the primary head's transform to position it correctly.
        //
        // However, some shapes (like hair) have identity transforms but their vertices are
        // already pre-translated to world space. For these, applying the head offset would
        // double the translation. We detect this by checking if the vertex centroid is far
        // from the origin (>10 units), matching NPC Portrait Creator's PRETRANSLATED_THRESHOLD.
        const float ZERO_TRANSLATION_THRESHOLD = 0.1f;
        const float PRETRANSLATED_THRESHOLD = 10.0f;
        float translationLength = (float)Math.Sqrt(
            globalTransform.translation.x * globalTransform.translation.x +
            globalTransform.translation.y * globalTransform.translation.y +
            globalTransform.translation.z * globalTransform.translation.z);

        if (translationLength < ZERO_TRANSLATION_THRESHOLD)
        {
            // Check if vertices are already pre-translated to world space
            float centroidLength = 0f;
            if (diagVerts != null && diagVerts.Count > 0)
            {
                float sumX = 0, sumY = 0, sumZ = 0;
                for (int i = 0; i < diagVerts.Count; i++)
                {
                    var v = diagVerts[i];
                    sumX += v.x; sumY += v.y; sumZ += v.z;
                }
                int n = diagVerts.Count;
                float cx = sumX / n, cy = sumY / n, cz = sumZ / n;
                centroidLength = (float)Math.Sqrt(cx * cx + cy * cy + cz * cz);
            }

            if (centroidLength > PRETRANSLATED_THRESHOLD)
            {
                _logger.LogMessage("CharacterViewer: [Skinning] '" + shapeName +
                    "' → using identity (pre-translated vertices, centroidLen=" +
                    centroidLength.ToString("F1") + " > threshold=" + PRETRANSLATED_THRESHOLD.ToString("F1") + ")");
                return globalTransform;
            }

            _logger.LogMessage("CharacterViewer: [Skinning] '" + shapeName +
                "' → APPLYING accessory offset (translationLen=" + translationLength.ToString("F3") +
                ", centroidLen=" + centroidLength.ToString("F1") +
                " ≤ threshold=" + PRETRANSLATED_THRESHOLD.ToString("F1") + ")");
            return accessoryOffset;
        }

        _logger.LogMessage("CharacterViewer: [Skinning] '" + shapeName +
            "' → using own global transform (translationLen=" + translationLength.ToString("F2") + ")");
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

    /// <summary>
    /// Extracts a nifly MatTransform's values into a pure C# CachedSkinTransform,
    /// avoiding SWIG interop calls during per-vertex skinning.
    /// </summary>
    private static CachedSkinTransform ExtractTransform(MatTransform t)
    {
        // Extract rotation matrix by multiplying basis vectors.
        // Matrix3.opMult(e_x) gives (rows[0][0], rows[1][0], rows[2][0]) = column 0.
        using var ex = new nifly.Vector3(); ex.x = 1;
        using var ey = new nifly.Vector3(); ey.y = 1;
        using var ez = new nifly.Vector3(); ez.z = 1;

        using var c0 = t.rotation.opMult(ex);
        using var c1 = t.rotation.opMult(ey);
        using var c2 = t.rotation.opMult(ez);

        return new CachedSkinTransform
        {
            // Reconstruct rows from columns
            R00 = c0.x, R01 = c1.x, R02 = c2.x,
            R10 = c0.y, R11 = c1.y, R12 = c2.y,
            R20 = c0.z, R21 = c1.z, R22 = c2.z,
            Tx = t.translation.x,
            Ty = t.translation.y,
            Tz = t.translation.z,
            Scale = t.scale,
        };
    }

    /// <summary>
    /// Attempts CPU-side bone-weight skinning for a shape. Returns SkinningInfo on success,
    /// null if the shape is not properly skinned. On success, outputs skinned positions and
    /// normals as flat float arrays in NIF Z-up space.
    /// </summary>
    /// <param name="skeletonNif">The loaded skeleton NIF providing real bone world transforms.</param>
    private SkinningInfo? TryApplyCpuSkinning(NifFile nif, NiShape shape,
        vectorVector3 nifVerts, vectorVector3? nifNormals, int vertCount,
        NifFile skeletonNif,
        out float[]? outPosX, out float[]? outPosY, out float[]? outPosZ,
        out float[]? outNrmX, out float[]? outNrmY, out float[]? outNrmZ)
    {
        outPosX = outPosY = outPosZ = null;
        outNrmX = outNrmY = outNrmZ = null;

        string shapeName = shape.name?.get() ?? "?";
        NiHeader header = nif.GetHeader();

        // --- Step 1: Get bone list ---
        using var boneNames = new vectorstring();
        uint numBones = nif.GetShapeBoneList(shape, boneNames);
        if (numBones == 0)
        {
            _logger.LogMessage("CharacterViewer: [Skinning] '" + shapeName + "' has skin instance but no bones");
            return null;
        }

        _logger.LogMessage("CharacterViewer: [Skinning] '" + shapeName + "' skinning with " + numBones + " bones");

        // --- Step 2: Compute per-bone skinning matrices ---
        // skinMatrix[i] = boneWorldTransform * inverseBindPose
        // Bone world transforms come from the SKELETON NIF (not the shape's own NIF),
        // which provides the real bind-pose bone positions from the full skeleton hierarchy.
        var boneTransforms = new CachedSkinTransform[numBones];
        int validBones = 0;
        int skeletonBones = 0;
        for (uint i = 0; i < numBones; i++)
        {
            using var inverseBind = new MatTransform();
            if (!nif.GetShapeBoneTransform(shape, i, inverseBind))
            {
                _logger.LogMessage("CharacterViewer: [Skinning] '" + shapeName +
                    "' bone " + i + " — no inverse bind-pose, using identity");
                continue;
            }

            string boneName = boneNames[(int)i];
            using var boneWorld = new MatTransform();

            // Try the skeleton NIF first (real bone positions), fall back to the shape's NIF
            if (skeletonNif.GetNodeTransformToGlobal(boneName, boneWorld))
            {
                skeletonBones++;
            }
            else if (!nif.GetNodeTransformToGlobal(boneName, boneWorld))
            {
                _logger.LogMessage("CharacterViewer: [Skinning] '" + shapeName +
                    "' bone '" + boneName + "' — not found in skeleton or shape NIF, skipping");
                continue;
            }

            using var skinMatrix = boneWorld.ComposeTransforms(inverseBind);
            boneTransforms[i] = ExtractTransform(skinMatrix);
            validBones++;
        }

        _logger.LogMessage("CharacterViewer: [Skinning] '" + shapeName +
            "' bone transforms: " + validBones + " valid (" + skeletonBones + " from skeleton)");

        if (validBones == 0)
        {
            _logger.LogMessage("CharacterViewer: [Skinning] '" + shapeName + "' no valid bone transforms, skipping skinning");
            return null;
        }

        // --- Step 3: Read per-vertex bone weights from NiSkinData ---
        var skinRef = shape.SkinInstanceRef();
        if (skinRef == null || skinRef.IsEmpty()) return null;

        NiObject skinObj = header.GetBlockById(skinRef.index);
        // BSDismemberSkinInstance extends NiSkinInstance — try both
        NiSkinInstance? skinInst = skinObj as NiSkinInstance;
        if (skinInst == null)
        {
            _logger.LogMessage("CharacterViewer: [Skinning] '" + shapeName + "' skin instance is not NiSkinInstance");
            return null;
        }

        var dataRef = skinInst.dataRef;
        if (dataRef == null || dataRef.IsEmpty())
        {
            _logger.LogMessage("CharacterViewer: [Skinning] '" + shapeName + "' no NiSkinData reference");
            return null;
        }

        NiObject dataObj = header.GetBlockById(dataRef.index);
        if (dataObj is not NiSkinData skinData)
        {
            _logger.LogMessage("CharacterViewer: [Skinning] '" + shapeName + "' block is not NiSkinData");
            return null;
        }

        // Build per-vertex arrays: 4 bone indices + 4 weights per vertex
        int[] vertBoneIndices = new int[vertCount * 4];
        float[] vertBoneWeights = new float[vertCount * 4];

        var bonesVec = skinData.bones;
        if (bonesVec == null)
        {
            _logger.LogMessage("CharacterViewer: [Skinning] '" + shapeName + "' NiSkinData.bones is null");
            return null;
        }

        int boneCount = Math.Min(bonesVec.Count, (int)numBones);
        int totalWeightEntries = 0;
        for (int boneIdx = 0; boneIdx < boneCount; boneIdx++)
        {
            var boneData = bonesVec[boneIdx];
            var vertWeights = boneData.vertexWeights;
            if (vertWeights == null) continue;

            for (int wi = 0; wi < vertWeights.Count; wi++)
            {
                var sw = vertWeights[wi];
                int vertIdx = sw.index;
                float weight = sw.weight;
                if (weight <= 0 || vertIdx >= vertCount) continue;

                // Find an empty slot for this vertex (up to 4 bones per vertex)
                int baseIdx = vertIdx * 4;
                for (int k = 0; k < 4; k++)
                {
                    if (vertBoneWeights[baseIdx + k] == 0f)
                    {
                        vertBoneIndices[baseIdx + k] = boneIdx;
                        vertBoneWeights[baseIdx + k] = weight;
                        totalWeightEntries++;
                        break;
                    }
                }
            }
        }

        _logger.LogMessage("CharacterViewer: [Skinning] '" + shapeName +
            "' read " + totalWeightEntries + " weight entries across " + boneCount + " bones");

        // --- Step 4: Apply skinning — blend bone transforms per vertex ---
        outPosX = new float[vertCount];
        outPosY = new float[vertCount];
        outPosZ = new float[vertCount];
        outNrmX = new float[vertCount];
        outNrmY = new float[vertCount];
        outNrmZ = new float[vertCount];

        bool hasNormals = nifNormals != null && nifNormals.Count == vertCount;

        for (int vi = 0; vi < vertCount; vi++)
        {
            var v = nifVerts[vi];
            float px = v.x, py = v.y, pz = v.z;

            float accPx = 0, accPy = 0, accPz = 0;
            float accNx = 0, accNy = 0, accNz = 0;
            float totalWeight = 0;

            int baseIdx = vi * 4;
            for (int k = 0; k < 4; k++)
            {
                float w = vertBoneWeights[baseIdx + k];
                if (w <= 0) continue;
                int bIdx = vertBoneIndices[baseIdx + k];

                boneTransforms[bIdx].Apply(px, py, pz, out float tx, out float ty, out float tz);
                accPx += w * tx;
                accPy += w * ty;
                accPz += w * tz;

                if (hasNormals)
                {
                    var n = nifNormals![vi];
                    boneTransforms[bIdx].ApplyRotation(n.x, n.y, n.z, out float tnx, out float tny, out float tnz);
                    accNx += w * tnx;
                    accNy += w * tny;
                    accNz += w * tnz;
                }

                totalWeight += w;
            }

            if (totalWeight > 0)
            {
                outPosX[vi] = accPx / totalWeight;
                outPosY[vi] = accPy / totalWeight;
                outPosZ[vi] = accPz / totalWeight;

                if (hasNormals)
                {
                    float nLen = (float)Math.Sqrt(accNx * accNx + accNy * accNy + accNz * accNz);
                    if (nLen > 0.0001f)
                    {
                        outNrmX[vi] = accNx / nLen;
                        outNrmY[vi] = accNy / nLen;
                        outNrmZ[vi] = accNz / nLen;
                    }
                    else
                    {
                        var n = nifNormals![vi];
                        outNrmX[vi] = n.x;
                        outNrmY[vi] = n.y;
                        outNrmZ[vi] = n.z;
                    }
                }
            }
            else
            {
                // No bone weights — keep bind-pose position
                outPosX[vi] = px;
                outPosY[vi] = py;
                outPosZ[vi] = pz;
                if (hasNormals)
                {
                    var n = nifNormals![vi];
                    outNrmX[vi] = n.x;
                    outNrmY[vi] = n.y;
                    outNrmZ[vi] = n.z;
                }
            }
        }

        // Note: No shapeToRoot transform is applied here. The skeleton's bone world
        // transforms already place vertices in NIF root space. Applying the shape's
        // local-to-root transform on top would double-count the offset (confirmed by
        // headparts which have identity shape transforms and are correctly positioned
        // by skinning alone).

        // Log a position sample to verify skinning is working
        if (vertCount > 0)
        {
            _logger.LogMessage("CharacterViewer: [Skinning] '" + shapeName +
                "' sample vert[0]: bind=(" + nifVerts[0].x.ToString("F2") + "," +
                nifVerts[0].y.ToString("F2") + "," + nifVerts[0].z.ToString("F2") +
                ") → skinned=(" + outPosX[0].ToString("F2") + "," +
                outPosY[0].ToString("F2") + "," + outPosZ[0].ToString("F2") + ")");
        }

        return new SkinningInfo
        {
            BoneTransforms = boneTransforms,
            VertBoneIndices = vertBoneIndices,
            VertBoneWeights = vertBoneWeights,
            VertexCount = vertCount,
        };
    }

    /// <summary>
    /// Applies bone-weight skinning to Y-up positions and normals using pre-computed skinning data.
    /// Converts Y-up → Z-up, applies skinning in Z-up, converts back to Y-up.
    /// Can be called in-place (same collection for input and output).
    /// </summary>
    public static void ApplySkinning(
        Vector3[] inputPositions,
        Vector3[] inputNormals,
        SkinningInfo skinning,
        Vector3[] outputPositions,
        Vector3[] outputNormals)
    {
        int vertCount = skinning.VertexCount;
        if (vertCount == 0 || inputPositions.Length < vertCount) return;

        bool hasNormals = inputNormals != null && inputNormals.Length >= vertCount;

        for (int vi = 0; vi < vertCount; vi++)
        {
            var posYUp = inputPositions[vi];
            float px = posYUp.X, py = -posYUp.Z, pz = posYUp.Y;

            float accPx = 0, accPy = 0, accPz = 0;
            float accNx = 0, accNy = 0, accNz = 0;
            float totalWeight = 0;

            float nx = 0, ny = 0, nz = 0;
            if (hasNormals)
            {
                var nrmYUp = inputNormals![vi];
                nx = nrmYUp.X; ny = -nrmYUp.Z; nz = nrmYUp.Y;
            }

            int baseIdx = vi * 4;
            for (int k = 0; k < 4; k++)
            {
                float w = skinning.VertBoneWeights[baseIdx + k];
                if (w <= 0) continue;
                int bIdx = skinning.VertBoneIndices[baseIdx + k];

                skinning.BoneTransforms[bIdx].Apply(px, py, pz, out float tx, out float ty, out float tz);
                accPx += w * tx; accPy += w * ty; accPz += w * tz;

                if (hasNormals)
                {
                    skinning.BoneTransforms[bIdx].ApplyRotation(nx, ny, nz, out float tnx, out float tny, out float tnz);
                    accNx += w * tnx; accNy += w * tny; accNz += w * tnz;
                }
                totalWeight += w;
            }

            if (totalWeight > 0)
            {
                outputPositions[vi] = new Vector3(accPx / totalWeight, accPz / totalWeight, -accPy / totalWeight);
                if (hasNormals)
                {
                    float nLen = MathF.Sqrt(accNx * accNx + accNy * accNy + accNz * accNz);
                    outputNormals[vi] = nLen > 0.0001f
                        ? new Vector3(accNx / nLen, accNz / nLen, -accNy / nLen)
                        : inputNormals![vi];
                }
            }
            else
            {
                outputPositions[vi] = posYUp;
                if (hasNormals) outputNormals[vi] = inputNormals![vi];
            }
        }
    }

    private static bool IsZeroTranslation(nifly.Vector3 v)
    {
        const float eps = 0.0001f;
        return Math.Abs(v.x) < eps && Math.Abs(v.y) < eps && Math.Abs(v.z) < eps;
    }

    private static bool AreNormalsAllZero(Vector3[] normals)
    {
        const float eps = 0.0001f;
        foreach (var n in normals)
        {
            if (Math.Abs(n.X) > eps || Math.Abs(n.Y) > eps || Math.Abs(n.Z) > eps)
                return false;
        }
        return true;
    }

    private static void ComputeNormalsFromGeometry(Vector3[] positions, int[] indices, Vector3[] normals)
    {
        for (int i = 0; i < normals.Length; i++)
            normals[i] = Vector3.Zero;

        for (int i = 0; i + 2 < indices.Length; i += 3)
        {
            int i0 = indices[i], i1 = indices[i + 1], i2 = indices[i + 2];
            var edge1 = positions[i1] - positions[i0];
            var edge2 = positions[i2] - positions[i0];
            var faceNormal = Vector3.Cross(edge1, edge2);
            normals[i0] += faceNormal;
            normals[i1] += faceNormal;
            normals[i2] += faceNormal;
        }

        for (int i = 0; i < normals.Length; i++)
        {
            float len = normals[i].Length();
            normals[i] = len > 0.0001f ? normals[i] / len : new Vector3(0, 1, 0);
        }
    }

    /// <summary>
    /// Computes tangents and bitangents from position/normal/UV data using the Lengyel algorithm.
    /// </summary>
    private static void ComputeTangents(Vector3[] positions, Vector3[] normals, Vector2[] uvs,
        vectorTriangle tris, Vector3[] tangents, Vector3[] bitangents)
    {
        var tan1 = new Vector3[positions.Length];
        var tan2 = new Vector3[positions.Length];

        for (int i = 0; i < tris.Count; i++)
        {
            var tri = tris[i];
            int i0 = tri.p1, i1 = tri.p2, i2 = tri.p3;
            if (i0 >= positions.Length || i1 >= positions.Length || i2 >= positions.Length) continue;

            var v0 = positions[i0]; var v1 = positions[i1]; var v2 = positions[i2];
            var w0 = uvs[i0]; var w1 = uvs[i1]; var w2 = uvs[i2];

            float x1 = v1.X - v0.X, x2 = v2.X - v0.X;
            float y1 = v1.Y - v0.Y, y2 = v2.Y - v0.Y;
            float z1 = v1.Z - v0.Z, z2 = v2.Z - v0.Z;
            float s1 = w1.X - w0.X, s2 = w2.X - w0.X;
            float t1 = w1.Y - w0.Y, t2 = w2.Y - w0.Y;

            float denom = s1 * t2 - s2 * t1;
            if (MathF.Abs(denom) < 1e-10f) continue;
            float r = 1f / denom;

            var sdir = new Vector3((t2 * x1 - t1 * x2) * r, (t2 * y1 - t1 * y2) * r, (t2 * z1 - t1 * z2) * r);
            var tdir = new Vector3((s1 * x2 - s2 * x1) * r, (s1 * y2 - s2 * y1) * r, (s1 * z2 - s2 * z1) * r);

            tan1[i0] += sdir; tan1[i1] += sdir; tan1[i2] += sdir;
            tan2[i0] += tdir; tan2[i1] += tdir; tan2[i2] += tdir;
        }

        for (int i = 0; i < positions.Length; i++)
        {
            var n = normals[i];
            var t = tan1[i];

            // Gram-Schmidt orthogonalize
            var tangent = t - n * Vector3.Dot(n, t);
            float len = tangent.Length();
            tangents[i] = len > 0.0001f ? tangent / len : Vector3.Zero;

            // Bitangent = cross(n, t) * handedness
            bitangents[i] = Vector3.Cross(n, tangents[i]);
            if (Vector3.Dot(bitangents[i], tan2[i]) < 0)
                bitangents[i] = -bitangents[i];
        }
    }
}
