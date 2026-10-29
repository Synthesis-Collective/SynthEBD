using System;
using OpenTK.Mathematics;
using NVec3 = System.Numerics.Vector3;
using NMat4 = System.Numerics.Matrix4x4;

namespace SkyRender.World;

/// <summary>
/// Places a model the way the engine places a reference (a REFR's position, rotation and scale) in
/// the renderer's Y-up mesh space, as a <see cref="GlMesh.ModelTransform"/>. Inputs are in Skyrim
/// world units, Z up: what a host reads from a placed record. See RENDERING_PIPELINE.md,
/// "Placing references".
/// </summary>
public static class ReferencePlacement
{
    /// <summary>A Skyrim (Z-up) point in the renderer's Y-up mesh space: (x, z, -y), the same swap
    /// <see cref="NifMeshBuilder"/> applies to NIF geometry.</summary>
    public static Vector3 ToMeshSpace(NVec3 zUp) => new(zUp.X, zUp.Z, -zUp.Y);

    /// <summary>A mesh-space (Y-up) point back in Skyrim's Z-up world: (x, -z, y).</summary>
    public static NVec3 ToWorldSpace(Vector3 yUp) => new(yUp.X, -yUp.Z, yUp.Y);

    /// <summary>
    /// The engine's rotation for a reference's three angles (radians, as the record stores them),
    /// for row vectors in Z-up space (<see cref="NVec3"/> times the matrix). The engine builds it
    /// with <c>NiMatrix3::SetEulerAnglesXYZ(x, y, z)</c>, whose entries are
    /// <c>(Rz(z) Ry(y) Rx(x))</c> transposed in the column-vector sense: the angles act clockwise
    /// (a heading of 90 degrees turns north to east) and the Z turn is applied first, then Y, then
    /// X. The order only shows on references tilted about more than one axis.
    /// </summary>
    public static NMat4 Rotation(NVec3 anglesRadians)
        => NMat4.CreateRotationZ(-anglesRadians.Z) * NMat4.CreateRotationY(-anglesRadians.Y)
         * NMat4.CreateRotationX(-anglesRadians.X);

    /// <summary>
    /// The mesh-space model transform for a reference at <paramref name="position"/> (Z-up world
    /// units), turned by <paramref name="anglesRadians"/> (<see cref="Rotation"/>) and scaled by
    /// <paramref name="scale"/>. <paramref name="modelRoot"/> is the model's root-node transform
    /// (<see cref="ReadRootTransform"/>), which the engine does not apply to a placed reference:
    /// the placement starts below the root, so the root is undone first. Null keeps the model as
    /// <see cref="NifMeshBuilder"/> built it.
    /// </summary>
    public static Matrix4 ModelTransform(NVec3 position, NVec3 anglesRadians, float scale, Matrix4? modelRoot = null)
    {
        var placeZ = NMat4.CreateScale(scale) * Rotation(anglesRadians) * NMat4.CreateTranslation(position);
        var placed = ToMeshSpaceMatrix(placeZ);
        if (modelRoot is { } root)
        {
            var inverse = root.Inverted();
            return inverse * placed;
        }
        return placed;
    }

    /// <summary>
    /// The root node's own transform (local to parent) of the NIF at <paramref name="nifPath"/>, in
    /// mesh space for row vectors, or null when the file cannot be read or the root is the
    /// identity. <see cref="NifMeshBuilder"/> composes every node up to and including the root; a
    /// placed reference does not take the root's transform (a door whose root turns it 90 degrees
    /// stands flush in its frame in game only without it), so pass this to
    /// <see cref="ModelTransform"/> to undo it.
    /// </summary>
    public static Matrix4? ReadRootTransform(string nifPath)
    {
        try
        {
            using var nif = new nifly.NifFile();
            if (nif.Load(nifPath) != 0) return null;
            var root = nif.GetRootNode();
            if (root is null) return null;
            var t = root.GetTransformToParent();
            using var ex = new nifly.Vector3 { x = 1f };
            using var ey = new nifly.Vector3 { y = 1f };
            using var ez = new nifly.Vector3 { z = 1f };
            using var c0 = t.rotation.opMult(ex);
            using var c1 = t.rotation.opMult(ey);
            using var c2 = t.rotation.opMult(ez);
            float s = t.scale;
            return FromNifTransform(new NVec3(c0.x, c0.y, c0.z), new NVec3(c1.x, c1.y, c1.z), new NVec3(c2.x, c2.y, c2.z),
                new NVec3(t.translation.x, t.translation.y, t.translation.z), s);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// A NIF node transform (column-vector rotation given by its columns, translation, uniform
    /// scale; Z-up) as a mesh-space row-vector matrix, or null when it is the identity.
    /// </summary>
    public static Matrix4? FromNifTransform(NVec3 column0, NVec3 column1, NVec3 column2, NVec3 translation, float scale)
    {
        // Column-vector p' = s R p + t is, for row vectors, p' = p (s R^T) + t: the rows are R's columns.
        var m = new NMat4(
            column0.X * scale, column0.Y * scale, column0.Z * scale, 0f,
            column1.X * scale, column1.Y * scale, column1.Z * scale, 0f,
            column2.X * scale, column2.Y * scale, column2.Z * scale, 0f,
            translation.X, translation.Y, translation.Z, 1f);
        if (m.IsIdentity) return null;
        return ToMeshSpaceMatrix(m);
    }

    /// <summary>A Z-up row-vector transform re-expressed in Y-up mesh space: mesh to Z-up, the
    /// transform, Z-up back to mesh.</summary>
    private static Matrix4 ToMeshSpaceMatrix(NMat4 zUp)
    {
        // (x, y, z) mesh -> (x, -z, y) Z-up, and back with (x, z, -y).
        var toZ = new NMat4(1, 0, 0, 0, 0, 0, 1, 0, 0, -1, 0, 0, 0, 0, 0, 1);
        var toMesh = new NMat4(1, 0, 0, 0, 0, 0, -1, 0, 0, 1, 0, 0, 0, 0, 0, 1);
        var m = toZ * zUp * toMesh;
        return new Matrix4(
            m.M11, m.M12, m.M13, m.M14,
            m.M21, m.M22, m.M23, m.M24,
            m.M31, m.M32, m.M33, m.M34,
            m.M41, m.M42, m.M43, m.M44);
    }
}
