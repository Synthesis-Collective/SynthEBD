namespace CharacterViewer.Rendering;

/// <summary>World-axis symmetry lock applied to a bounding-box region while it
/// is being authored in the viewer's BB-pick mode. Flag combinations encode
/// the set of axes mirrored about 0 (e.g., <c>X | Y</c> locks both X and Y
/// sides). Authoring-time UX only — never persisted on a saved key vertex.
///
/// Lives in the rendering tier (alongside <see cref="BoxCriterionSelection"/>)
/// because <see cref="VM_CharacterViewer"/>'s BB-pick state references it.
/// SynthEBD's BodyTypeProfile classifier system is the only consumer that
/// persists the corresponding final box criteria; the SymmetryAxes value
/// itself is in-flight UI state.</summary>
[System.Flags]
public enum SymmetryAxes
{
    None = 0,
    X = 1,
    Y = 2,
    Z = 4,
}

/// <summary>
/// Authoring-time-only companion to the persisted bounding-box criterion. The
/// viewer's BB-pick combo exposes the single-axis values plus <c>Mirror*</c>
/// shortcuts; picking a <c>Mirror*</c> value tells the host editor to
/// materialize two paired key-vertex rows sharing the same box but with
/// opposite single-axis criteria (e.g. <c>MirrorX</c> → <c>MaxX</c> +
/// <c>MinX</c>, <c>MirrorPinchX</c> → <c>PinchMinX</c> + <c>PinchMaxX</c>).
/// Persistence stores only the single-axis criterion; this enum never lands
/// in JSON.
///
/// <para>Numeric values 0–9 intentionally match the persisted bounding-box
/// criterion enum on the SynthEBD side so the editor can plain-cast for
/// non-mirror entries; mirror values are placed at 100+ to stay out of
/// the way.</para>
/// </summary>
public enum BoxCriterionSelection
{
    MaxX = 0,
    MinX = 1,
    MaxY = 2,
    MinY = 3,
    MaxZ = 4,
    MinZ = 5,
    PinchMinX = 6,
    PinchMaxX = 7,
    BulgeMinX = 8,
    BulgeMaxX = 9,
    PinchPairMinX = 10,
    PinchPairMaxX = 11,
    BulgePairMinX = 12,
    BulgePairMaxX = 13,
    MirrorX = 100,
    MirrorY = 101,
    MirrorZ = 102,
    MirrorPinchX = 103,
    MirrorBulgeX = 104,
}
