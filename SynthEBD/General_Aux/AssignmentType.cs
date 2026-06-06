namespace SynthEBD;

/// <summary>
/// Identifies one of SynthEBD's independent assignment axes. Used to tag assignments and
/// consistency/blocking rules so each axis can be tracked and constrained separately.
/// </summary>
public enum AssignmentType
{
    /// <summary>The NPC's primary asset pack (textures/meshes).</summary>
    PrimaryAssets,
    /// <summary>Supplementary "mix-in" asset packs layered on top of the primary set.</summary>
    MixInAssets,
    /// <summary>Replacer asset packs that override specific existing assets.</summary>
    ReplacerAssets,
    /// <summary>BodyGen morph-based body shape.</summary>
    BodyGen,
    /// <summary>BodySlide preset-based body shape.</summary>
    BodySlide,
    /// <summary>NPC height scaling.</summary>
    Height,
    /// <summary>Head part assignment (hair, eyes, brows, etc.).</summary>
    HeadParts
}