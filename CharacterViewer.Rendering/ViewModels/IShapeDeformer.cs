using System.Numerics;

namespace CharacterViewer.Rendering;

/// <summary>
/// Host-supplied CPU deformation of a scene's body shapes, applied by
/// <see cref="VM_CharacterViewer.ApplyShapeDeformer"/> (primary scene) and
/// <see cref="VM_CharacterViewer.LoadGuestAsync"/> (guest overlay).
///
/// <para>For every body shape the viewer (1) starts from the shape's base pose at the requested
/// weight, re-deriving the engine's armor-weight blend of a <c>_0</c>/<c>_1</c> pair when the shape
/// has one; (2) calls <see cref="Deform"/> once with the whole position array; (3) recomputes
/// normals and re-applies skinning; (4) re-uploads the shape. <see cref="Deform"/> is called once per
/// shape per application, never per vertex.</para>
/// </summary>
public interface IShapeDeformer
{
    /// <summary>Short description for logs. The guest overlay also uses it, with the weight, as part
    /// of its reload short-circuit key: two deformers with the same label are treated as the same
    /// deformation there.</summary>
    string Label { get; }

    /// <summary>
    /// Called once per application, before any <see cref="Deform"/> call, with the disk path of the
    /// body NIF the shapes were loaded from (null when unknown) and the requested weight (0-100, as
    /// passed in). Return false when there is nothing to apply: the primary scene then keeps its
    /// shapes exactly as they are and <see cref="VM_CharacterViewer.ShapesDeformed"/> is not raised.
    /// The guest overlay re-derives its shapes either way.
    /// </summary>
    bool Prepare(string? bodyNifDiskPath, int weight);

    /// <summary>
    /// Deforms one shape in place. <paramref name="positions"/> holds the shape's un-skinned (bind
    /// space, Y-up) positions at <paramref name="weight"/>, freshly allocated for this call;
    /// <paramref name="baseMesh"/> is the shape as loaded (read-only).
    /// </summary>
    void Deform(string shapeName, NifMeshBuilder.BuiltMesh baseMesh, Vector3[] positions, int weight);
}
