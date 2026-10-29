using System;
using System.Collections.Generic;
using nifly;
using Vector2 = System.Numerics.Vector2;
using Vector3 = System.Numerics.Vector3;
using Vector4 = System.Numerics.Vector4;

namespace SkyRender.World;

/// <summary>
/// One drawable shape of a terrain or object LOD block (<c>.btr</c> / <c>.bto</c>), as plain data in
/// Skyrim's Z-up space, relative to the block's south-west corner (the engine places the file there).
/// </summary>
public sealed class LodShape
{
    public required string Name { get; init; }
    /// <summary>Vertex positions with the shape's node chain applied (LOD shapes carry a scale equal
    /// to their level, so their half-precision vertices cover the block).</summary>
    public required Vector3[] Positions { get; init; }
    /// <summary>Vertex normals (unit, Z-up), or null when the shape stores none (terrain LOD, whose
    /// normals are in its model-space normal texture).</summary>
    public Vector3[]? Normals { get; init; }
    public Vector3[]? Tangents { get; init; }
    public Vector3[]? Bitangents { get; init; }
    /// <summary>Vertex colours (RGBA, 0..1), read only when the shader has
    /// <c>SLSF2_Vertex_Colors</c>; null otherwise. In vanilla object LOD the HD shapes (objHD,
    /// objsnowHD: 612 of 1743 Tamriel shapes) carry them; so does DynDOLOD's object and grass LOD.</summary>
    public Vector4[]? Colors { get; init; }
    public required Vector2[] Uvs { get; init; }
    /// <summary>Triangle list, three indices per triangle.</summary>
    public required int[] Indices { get; init; }
    /// <summary>The shape's scale to the file root: the engine's LOD landscape shader lowers vertices
    /// under the loaded cells by 230 MODEL units, which is 230 × this in world units.</summary>
    public float Scale { get; init; } = 1f;
    public string? DiffusePath { get; init; }
    public string? NormalPath { get; init; }
    public uint ShaderType { get; init; }
    public uint ShaderFlags1 { get; init; }
    public uint ShaderFlags2 { get; init; }
    public bool ModelSpaceNormals => (ShaderFlags1 & 0x1000u) != 0;     // SLSF1_Model_Space_Normals
    public bool DoubleSided => (ShaderFlags2 & 0x10u) != 0;            // SLSF2_Double_Sided
    public bool HasSpecular => (ShaderFlags1 & 0x1u) != 0;             // SLSF1_Specular
    public bool HasVertexColors => (ShaderFlags2 & 0x20u) != 0;        // SLSF2_Vertex_Colors
    /// <summary>A LOD water quad set: a shape under a terrain block's <c>WATER</c> node, with no
    /// shader property (the engine draws it with the worldspace's LOD water type).</summary>
    public bool IsWater { get; init; }
    public bool AlphaTest { get; init; }
    public float AlphaThreshold { get; init; }
    /// <summary>A BSSubIndexTriShape's segments as (first triangle, triangle count), in segment order;
    /// empty for a plain BSTriShape. In level-4 object LOD, segment k holds the objects of cell
    /// (k / 4, k % 4) of the block (see <see cref="LodMeshBuilder.BuildObjects"/>).</summary>
    public IReadOnlyList<(int FirstTriangle, int TriangleCount)> Segments { get; init; } = Array.Empty<(int, int)>();
}

/// <summary>Reads the shapes of a <c>.btr</c> / <c>.bto</c> file (they are NIFs) through niflysharp.
/// Shapes with no shader property are skipped, except, with <c>includeWater</c>, the flat LOD water
/// quads under a terrain block's <c>WATER</c> node (<see cref="LodShape.IsWater"/>).</summary>
public static class LodNifReader
{
    /// <summary>The node a terrain LOD block keeps its LOD water quads under.</summary>
    public const string WaterNodeName = "WATER";

    public static List<LodShape> Read(string diskPath, bool includeWater = false)
    {
        var shapesOut = new List<LodShape>();
        using var nif = new NifFile();
        if (nif.Load(diskPath) != 0) throw new InvalidOperationException($"nifly could not load '{diskPath}'");
        var header = nif.GetHeader();
        using var shapes = nif.GetShapes();
        foreach (var shape in shapes)
        {
            // The typed block (GetShader returns the base type, which hides the lighting fields).
            var shaderRef = shape.ShaderPropertyRef();
            var shaderObj = shaderRef is null || shaderRef.IsEmpty() ? null : header.GetBlockById(shaderRef.index);
            bool isWater = false;
            if (shaderObj is null)
            {
                // LOD water: no shader; the engine applies the worldspace's LOD water type.
                if (!includeWater || !string.Equals(nif.GetParentNode(shape)?.name?.get(), WaterNodeName, StringComparison.OrdinalIgnoreCase))
                    continue;
                isWater = true;
            }
            using var verts = nif.GetVertsForShape(shape);
            using var tris = new vectorTriangle();
            if (verts is null || verts.Count == 0 || !shape.GetTriangles(tris) || tris.Count == 0) continue;
            int n = verts.Count;

            var xf = TransformToRoot(nif, shape);
            var pos = new Vector3[n];
            for (int i = 0; i < n; i++) pos[i] = xf.Point(new Vector3(verts[i].x, verts[i].y, verts[i].z));

            Vector3[]? normals = ReadDirections(nif.GetNormalsForShape(shape), n, xf);
            Vector3[]? tangents = ReadDirections(nif.GetTangentsForShape(shape), n, xf);
            Vector3[]? bitangents = ReadDirections(nif.GetBitangentsForShape(shape), n, xf);

            var uvs = new Vector2[n];
            using (var nuv = nif.GetUvsForShape(shape))
                if (nuv != null && nuv.Count == n)
                    for (int i = 0; i < n; i++) uvs[i] = new Vector2(nuv[i].u, nuv[i].v);

            var idx = new int[tris.Count * 3];
            for (int i = 0; i < tris.Count; i++)
            {
                var t = tris[i];
                idx[i * 3] = t.p1; idx[i * 3 + 1] = t.p2; idx[i * 3 + 2] = t.p3;
            }

            uint type = 0, f1 = 0, f2 = 0;
            if (shaderObj is BSLightingShaderProperty bslsp)
            {
                type = bslsp.bslspShaderType;
                f1 = bslsp.shaderFlags1;
                f2 = bslsp.shaderFlags2;
            }
            string? diffuse = isWater ? null : Texture(nif, shape, 0), normal = isWater ? null : Texture(nif, shape, 1);

            Vector4[]? colors = null;
            if ((f2 & 0x20u) != 0)   // SLSF2_Vertex_Colors
                using (var nc = new vectorColor4())
                    if (nif.GetColorsForShape(shape, nc) && nc.Count == n)
                    {
                        colors = new Vector4[n];
                        for (int i = 0; i < n; i++) colors[i] = new Vector4(nc[i].r, nc[i].g, nc[i].b, nc[i].a);
                    }

            bool alphaTest = false;
            float threshold = 0f;
            if (shape.HasAlphaProperty() && shape.AlphaPropertyRef() is { } aref && !aref.IsEmpty() && header.GetBlockById(aref.index) is NiAlphaProperty ap)
            {
                alphaTest = (ap.flags & 0x200) != 0;   // NiAlphaProperty bit 9: alpha test
                threshold = ap.threshold / 255f;
            }

            var segments = new List<(int, int)>();
            if (header.GetBlockById(nif.GetBlockID(shape)) is BSSubIndexTriShape sit)
            {
                using var segs = sit.GetSegments();
                for (int k = 0; k < segs.Count; k++)
                {
                    var s = segs[k];
                    // The segment's start is stored in index units (three per triangle).
                    segments.Add(((int)(s.index / 3), (int)s.numTris));
                }
            }

            shapesOut.Add(new LodShape
            {
                Name = shape.name?.get() ?? "",
                Positions = pos,
                Normals = normals,
                Tangents = tangents,
                Bitangents = bitangents,
                Colors = colors,
                Uvs = uvs,
                Indices = idx,
                Scale = xf.Scale,
                DiffusePath = diffuse,
                NormalPath = normal,
                ShaderType = type,
                ShaderFlags1 = f1,
                ShaderFlags2 = f2,
                AlphaTest = alphaTest,
                AlphaThreshold = threshold,
                Segments = segments,
                IsWater = isWater,
            });
        }
        return shapesOut;
    }

    private static string? Texture(NifFile nif, NiShape shape, uint slot)
    {
        try
        {
            string p = nif.GetTexturePathByIndex(shape, slot);
            return string.IsNullOrWhiteSpace(p) ? null : p;
        }
        catch { return null; }
    }

    private static Vector3[]? ReadDirections(vectorVector3? v, int n, Xf xf)
    {
        using (v)
        {
            if (v is null || v.Count != n) return null;
            var r = new Vector3[n];
            for (int i = 0; i < n; i++)
            {
                var d = xf.Direction(new Vector3(v[i].x, v[i].y, v[i].z));
                r[i] = d.LengthSquared() > 1e-12f ? Vector3.Normalize(d) : Vector3.UnitZ;
            }
            return r;
        }
    }

    // The shape's transform to the file root, read out of nifly once so vertices transform in C#.
    private readonly record struct Xf(Vector3 R0, Vector3 R1, Vector3 R2, Vector3 T, float Scale)
    {
        public Vector3 Direction(Vector3 v) => new(Vector3.Dot(R0, v), Vector3.Dot(R1, v), Vector3.Dot(R2, v));
        public Vector3 Point(Vector3 v) => Direction(v * Scale) + T;
    }

    private static Xf TransformToRoot(NifFile nif, NiAVObject obj)
    {
        var result = obj.GetTransformToParent();
        var parent = nif.GetParentNode(obj);
        while (parent != null)
        {
            result = parent.GetTransformToParent().ComposeTransforms(result);
            parent = nif.GetParentNode(parent);
        }
        Vector3 Col(float x, float y, float z)
        {
            using var v = new nifly.Vector3 { x = x, y = y, z = z };
            using var r = result.rotation.opMult(v);
            return new Vector3(r.x, r.y, r.z);
        }
        var c0 = Col(1, 0, 0); var c1 = Col(0, 1, 0); var c2 = Col(0, 0, 1);
        return new Xf(new Vector3(c0.X, c1.X, c2.X), new Vector3(c0.Y, c1.Y, c2.Y), new Vector3(c0.Z, c1.Z, c2.Z),
            new Vector3(result.translation.x, result.translation.y, result.translation.z), result.scale);
    }
}
