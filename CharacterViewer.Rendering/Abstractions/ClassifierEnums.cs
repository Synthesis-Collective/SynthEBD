using System.ComponentModel;

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
    [Description("Vertex with the largest X (rightmost) inside the box.")]
    MaxX = 0,
    [Description("Vertex with the smallest X (leftmost) inside the box.")]
    MinX = 1,
    [Description("Vertex with the largest Y (highest) inside the box.")]
    MaxY = 2,
    [Description("Vertex with the smallest Y (lowest) inside the box.")]
    MinY = 3,
    [Description("Vertex with the largest Z (front-most) inside the box.")]
    MaxZ = 4,
    [Description("Vertex with the smallest Z (back-most) inside the box.")]
    MinZ = 5,
    [Description("Pinch — left side: slice the box's Y range and pick the silhouette vertex whose X is closest to the midline. Suits waist-pinch landmarks.")]
    PinchMinX = 6,
    [Description("Pinch — right side: same Y-slice scan as PinchMinX but on the X>0 half.")]
    PinchMaxX = 7,
    [Description("Bulge — left side: slice the box's Y range and pick the silhouette vertex whose X is furthest from the midline. Suits widest-hip landmarks.")]
    BulgeMinX = 8,
    [Description("Bulge — right side: same Y-slice scan as BulgeMinX but on the X>0 half.")]
    BulgeMaxX = 9,
    [Description("Paired pinch — left side. Requires a sibling row (PinchPairMaxX) with the same box; the pair jointly picks from the same Y-slice so a PointDistance between them measures horizontal thickness.")]
    PinchPairMinX = 10,
    [Description("Paired pinch — right side. Sibling of PinchPairMinX.")]
    PinchPairMaxX = 11,
    [Description("Paired bulge — left side. Sibling of BulgePairMaxX; same joint-Y-slice constraint as PinchPair.")]
    BulgePairMinX = 12,
    [Description("Paired bulge — right side. Sibling of BulgePairMinX.")]
    BulgePairMaxX = 13,
    [Description("Lowest-Y vertex among those with X<0 inside the box. Pairs with MinYRightOfX (authored via MinYMirroredAcrossX).")]
    MinYLeftOfX = 14,
    [Description("Lowest-Y vertex among those with X≥0 inside the box. Pairs with MinYLeftOfX.")]
    MinYRightOfX = 15,
    [Description("Highest-Y vertex among those with X<0 inside the box. Pairs with MaxYRightOfX (authored via MaxYMirroredAcrossX).")]
    MaxYLeftOfX = 16,
    [Description("Highest-Y vertex among those with X≥0 inside the box. Pairs with MaxYLeftOfX.")]
    MaxYRightOfX = 17,
    [Description("Back-most-Z vertex among those with X<0 inside the box. Pairs with MinZRightOfX (authored via MinZMirroredAcrossX).")]
    MinZLeftOfX = 18,
    [Description("Back-most-Z vertex among those with X≥0 inside the box. Pairs with MinZLeftOfX.")]
    MinZRightOfX = 19,
    [Description("Front-most-Z vertex among those with X<0 inside the box. Pairs with MaxZRightOfX (authored via MaxZMirroredAcrossX).")]
    MaxZLeftOfX = 20,
    [Description("Front-most-Z vertex among those with X≥0 inside the box. Pairs with MaxZLeftOfX.")]
    MaxZRightOfX = 21,
    [Description("Vertex closest to the center of the box's max-X (right) face — balances 'far right' against 'near the Y/Z center'. Use for centerline-anchored side landmarks.")]
    MaxXAtCenter = 22,
    [Description("Vertex closest to the center of the box's min-X (left) face — balances 'far left' against 'near the Y/Z center'.")]
    MinXAtCenter = 23,
    [Description("Vertex closest to the center of the box's max-Y (top) face — balances 'highest' against 'near the X/Z center'. Use for top-of-feature landmarks (crown, shoulder peak).")]
    MaxYAtCenter = 24,
    [Description("Vertex closest to the center of the box's min-Y (bottom) face — balances 'lowest' against 'near the X/Z center'.")]
    MinYAtCenter = 25,
    [Description("Vertex closest to the center of the box's max-Z (front) face — balances 'front-most' against 'near the X/Y center'. Use for protrusion landmarks (navel, nipple).")]
    MaxZAtCenter = 26,
    [Description("Vertex closest to the center of the box's min-Z (back) face — balances 'back-most' against 'near the X/Y center'. Use for centerline-anchored rear landmarks (spine).")]
    MinZAtCenter = 27,
    [Description("Authoring shortcut: expands into MaxX + MinX rows sharing this box. One drag captures both sides of a symmetric L/R feature.")]
    MirrorX = 100,
    [Description("Authoring shortcut: expands into MaxY + MinY rows sharing this box (highest + lowest Y).")]
    MirrorY = 101,
    [Description("Authoring shortcut: expands into MaxZ + MinZ rows sharing this box (front + back).")]
    MirrorZ = 102,
    [Description("Authoring shortcut: expands into PinchPairMinX + PinchPairMaxX rows (joint Y-slice) for symmetric pinch landmarks.")]
    MirrorPinchX = 103,
    [Description("Authoring shortcut: expands into BulgePairMinX + BulgePairMaxX rows for symmetric bulge landmarks.")]
    MirrorBulgeX = 104,
    [Description("Authoring shortcut: expands into MinYLeftOfX + MinYRightOfX rows sharing this box. Captures the lowest Y on each side of X=0 — paired bottom landmark.")]
    MinYMirroredAcrossX = 105,
    [Description("Authoring shortcut: expands into MaxYLeftOfX + MaxYRightOfX rows sharing this box. Captures the highest Y on each side of X=0 — paired top landmark.")]
    MaxYMirroredAcrossX = 106,
    [Description("Authoring shortcut: expands into MinZLeftOfX + MinZRightOfX rows sharing this box. Captures the back-most Z on each side of X=0 — paired rear landmark.")]
    MinZMirroredAcrossX = 107,
    [Description("Authoring shortcut: expands into MaxZLeftOfX + MaxZRightOfX rows sharing this box. Captures the front-most Z on each side of X=0 — paired front landmark.")]
    MaxZMirroredAcrossX = 108,
}
