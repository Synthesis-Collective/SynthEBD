namespace CharacterViewer.Rendering;

/// <summary>
/// Classifies a <see cref="VM_CharacterViewer.MeshOverrideWarningDetails"/>
/// entry so hosts can route each warning to the right surface. The legacy
/// flat-string <see cref="VM_CharacterViewer.MeshOverrideWarnings"/> list
/// cannot distinguish "this PNG depicts an incomplete/misaligned model"
/// (worth persisting as a missing asset and re-rendering when the load
/// order changes) from "the mod's own physics config link is stale"
/// (cosmetically irrelevant to a static render — persisting it as a
/// missing asset would re-stale the mugshot every session with no way for
/// the user to fix it short of editing the mod's NIF).
/// </summary>
public enum MeshOverrideWarningKind
{
    /// <summary>The override NIF did not resolve; nothing was rendered for it.</summary>
    MeshNotFound,

    /// <summary>The override NIF resolved but produced no renderable shapes.</summary>
    NoRenderableShapes,

    /// <summary>A shape was skipped: it is weighted to bones present in neither
    /// the skeleton nor the mesh's own NIF (it would collapse to the origin).</summary>
    UnresolvedBones,

    /// <summary>A shape rendered via the mesh-NIF bone fallback because the
    /// resolved skeleton lacks bones it is weighted to — the classic
    /// "install XPMSSE" case; the shape may be misaligned.</summary>
    SkeletonMissingBones,

    /// <summary>A shape's skeleton-absent bones are SMP/HDT physics bones, but
    /// they could only be classified as such via a sibling physics config:
    /// the config the NIF actually links (NiStringExtraData) does not exist —
    /// a stale link in the mod itself. The render is correct (physics bones
    /// draw at their authored rest pose); in game the outfit's physics likely
    /// will not load. Informational: hosts should NOT treat this as a missing
    /// asset or re-render trigger.</summary>
    StalePhysicsConfig,
}

/// <summary>One structured mesh-override warning: a routing
/// <see cref="Kind"/> plus the human-readable "&lt;Key&gt;: &lt;reason&gt;"
/// message the flat string list has always carried.</summary>
public sealed record MeshOverrideWarning(MeshOverrideWarningKind Kind, string Message);
