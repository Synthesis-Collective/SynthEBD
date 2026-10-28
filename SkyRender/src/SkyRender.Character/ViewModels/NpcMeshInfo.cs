namespace SkyRender.Character;

/// <summary>
/// Slot-occupancy bookkeeping the character viewer attaches to each of its meshes (through
/// <see cref="GlMesh.HostData"/>). Each rendered shape carries the biped-object slots it occupies,
/// so "headgear hides hair" / "clothing hides body" visibility resolves from slot collisions rather
/// than from body-part strings. See the mesh-override section of docs/RENDERING_PIPELINE.md. Base shapes get
/// their slots from their body-part label at install time; mesh-override shapes from
/// <see cref="MeshOverride.BipedSlots"/>.
/// </summary>
public sealed class NpcMeshInfo
{
    /// <summary>Bitmask of biped-object slots this shape occupies, encoded as the
    /// <c>(BipedObjectFlag)N</c> bits (<c>1 &lt;&lt; (slot-30)</c>). 0 for shapes with no slot
    /// association (e.g. FaceGen accessories).</summary>
    public int BipedSlots { get; set; }

    /// <summary>Bitmask of slots whose lower-priority occupants this shape hides. Non-zero only for
    /// mesh-override shapes that should occlude what they cover (armor over body, headgear over
    /// hair). Base shapes leave this 0 — the base body never hides anything.</summary>
    public int HidesSlots { get; set; }

    /// <summary>Slot-occupancy precedence: a shape can only be hidden by another shape of strictly
    /// higher priority. 0 = skin / base (the body, an auxiliary skin mesh), 1 = armor,
    /// 2 = headgear.</summary>
    public int SlotDrawPriority { get; set; }

    /// <summary>Non-null for shapes synthesized by <see cref="VM_CharacterViewer.ApplyMeshOverrides"/>
    /// (the MeshOverride.Key they came from); null for base NPC shapes. Lets a re-applied override
    /// set find and tear down the shapes the previous set created.</summary>
    public string? OverrideKey { get; set; }
}

/// <summary>Access to the <see cref="NpcMeshInfo"/> attached to a viewer mesh.</summary>
public static class NpcMeshInfoExtensions
{
    /// <summary>The mesh's <see cref="NpcMeshInfo"/>, attached on first use.</summary>
    public static NpcMeshInfo Npc(this GlMesh mesh)
    {
        if (mesh.HostData is NpcMeshInfo info) return info;
        info = new NpcMeshInfo();
        mesh.HostData = info;
        return info;
    }
}
