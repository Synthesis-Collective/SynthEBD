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
///
/// <para>Axis convention (matches the persisted criterion): mesh-local X = left-right,
/// Y = up-down, Z = front-back. The default model orientation in the viewer faces -Z,
/// so smaller Z = in front of the body, larger Z = behind the body.</para>
/// </summary>
public enum BoxCriterionSelection
{
    [ShortLabel("Rightmost (Max X)")]
    [Description("Picks the single vertex inside the box with the largest X coordinate — the one furthest to the model's right. Use for: rightmost-shoulder, right-hip-side, outer right bicep. Selects 1 vertex.")]
    MaxX = 0,
    [ShortLabel("Leftmost (Min X)")]
    [Description("Picks the single vertex inside the box with the smallest X coordinate — the one furthest to the model's left. Use for: leftmost-shoulder, left-hip-side, outer left bicep. Selects 1 vertex.")]
    MinX = 1,
    [ShortLabel("Highest (Max Y)")]
    [Description("Picks the single vertex inside the box with the largest Y coordinate — the highest one. Use for: top-of-head, top-of-bust, peak of shoulder. Selects 1 vertex.")]
    MaxY = 2,
    [ShortLabel("Lowest (Min Y)")]
    [Description("Picks the single vertex inside the box with the smallest Y coordinate — the lowest one. Use for: bottom-of-foot, bottom-of-bust fold, bottom of buttock. Selects 1 vertex.")]
    MinY = 3,
    [ShortLabel("Backmost (Max Z)")]
    [Description("Picks the single vertex inside the box with the largest Z coordinate — the one furthest back. (Model faces -Z, so larger Z = behind the body.) Use for: spine-back, sacrum-back, back-of-buttock. Selects 1 vertex.")]
    MaxZ = 4,
    [ShortLabel("Frontmost (Min Z)")]
    [Description("Picks the single vertex inside the box with the smallest Z coordinate — the one furthest forward. (Model faces -Z, so smaller Z = in front of the body.) Use for: nipple-front, navel, belly-front. Selects 1 vertex.")]
    MinZ = 5,
    [ShortLabel("Waist-pinch Left")]
    [Description("Slices the box's vertical range into thin horizontal bands, finds the band where the body's left silhouette sits closest to the centerline (the narrowest band), then picks that band's left-side vertex. Use for: marking the inner edge of the left waist on a body whose waist height varies between presets. Selects 1 vertex. For honest left-to-right waist-width measurement, prefer the Paired variant — it guarantees the left and right picks come from the same horizontal band.")]
    PinchMinX = 6,
    [ShortLabel("Waist-pinch Right")]
    [Description("Mirror of Waist-pinch Left: scans for the narrowest horizontal band and picks that band's right-side vertex. Selects 1 vertex. For waist-width measurement, prefer the Paired variant.")]
    PinchMaxX = 7,
    [ShortLabel("Hip-bulge Left")]
    [Description("Slices the box's vertical range into thin horizontal bands, finds the band where the body's left silhouette sits furthest from the centerline (the widest band), then picks that band's left-side vertex. Use for: marking the outer edge of the left hip on a body whose widest-hip height varies between presets. Selects 1 vertex. For honest left-to-right hip-width measurement, prefer the Paired variant.")]
    BulgeMinX = 8,
    [ShortLabel("Hip-bulge Right")]
    [Description("Mirror of Hip-bulge Left: scans for the widest horizontal band and picks that band's right-side vertex. Selects 1 vertex. For hip-width measurement, prefer the Paired variant.")]
    BulgeMaxX = 9,
    [ShortLabel("Paired Waist-pinch Left")]
    [Description("Co-operates with a sibling row using 'Paired Waist-pinch Right' that shares the same Shape and box. The two rows together scan the box, find the single horizontal band where the body is narrowest, and pick from THAT band — left row gets the left-side vertex, right row gets the right-side vertex. The joint scan guarantees both picks come from the same height, so a PointDistance between them measures honest horizontal waist width rather than a diagonal across different heights. Use for: waist-width measurement landmarks. Selects 1 vertex per row; the pair selects 2 at the same Y. Without a sibling, falls back to plain Waist-pinch Left.")]
    PinchPairMinX = 10,
    [ShortLabel("Paired Waist-pinch Right")]
    [Description("Sibling of Paired Waist-pinch Left — see that entry for the joint-band mechanic. Selects 1 vertex per row; the pair selects 2.")]
    PinchPairMaxX = 11,
    [ShortLabel("Paired Hip-bulge Left")]
    [Description("Co-operates with a sibling row using 'Paired Hip-bulge Right' that shares the same Shape and box. The two rows together scan the box, find the single horizontal band where the body is widest, and pick from THAT band — left row gets the left-side vertex, right row gets the right-side vertex. Guarantees honest left-to-right hip-width measurement. Use for: hip-width landmarks. Selects 1 vertex per row; the pair selects 2. Without a sibling, falls back to plain Hip-bulge Left.")]
    BulgePairMinX = 12,
    [ShortLabel("Paired Hip-bulge Right")]
    [Description("Sibling of Paired Hip-bulge Left — see that entry for the joint-band mechanic. Selects 1 vertex per row; the pair selects 2.")]
    BulgePairMaxX = 13,
    [ShortLabel("Lowest on Left Half (X<0)")]
    [Description("Among vertices in the box whose X coordinate is negative (the model's left half), picks the one with the smallest Y — the lowest one on that side. Typically authored together with its right-half twin so the two rows together capture both sides of a symmetric feature. Use for: bottom-of-left-bust, lowest-fold of left shoulder, bottom of left buttock. Selects 1 vertex per row; the pair selects 2.")]
    MinYLeftOfX = 14,
    [ShortLabel("Lowest on Right Half (X>=0)")]
    [Description("Mirror of 'Lowest on Left Half': picks the lowest vertex on the model's right side. Selects 1 vertex per row; the pair selects 2.")]
    MinYRightOfX = 15,
    [ShortLabel("Highest on Left Half (X<0)")]
    [Description("Among vertices in the box whose X coordinate is negative (the model's left half), picks the one with the largest Y — the highest one on that side. Typically authored together with its right-half twin. Use for: top-of-left-bust, peak of left shoulder, top of left thigh. Selects 1 vertex per row; the pair selects 2.")]
    MaxYLeftOfX = 16,
    [ShortLabel("Highest on Right Half (X>=0)")]
    [Description("Mirror of 'Highest on Left Half': picks the highest vertex on the model's right side. Selects 1 vertex per row; the pair selects 2.")]
    MaxYRightOfX = 17,
    [ShortLabel("Frontmost on Left Half (X<0)")]
    [Description("Among vertices in the box whose X coordinate is negative (the model's left half), picks the one with the smallest Z — the one furthest forward on that side. (Model faces -Z, so smaller Z = in front.) Use for: front of left bust, front of left bicep. Authored together with its right-half twin. Selects 1 vertex per row; the pair selects 2.")]
    MinZLeftOfX = 18,
    [ShortLabel("Frontmost on Right Half (X>=0)")]
    [Description("Mirror of 'Frontmost on Left Half': picks the front-most vertex on the model's right side. Selects 1 vertex per row; the pair selects 2.")]
    MinZRightOfX = 19,
    [ShortLabel("Backmost on Left Half (X<0)")]
    [Description("Among vertices in the box whose X coordinate is negative (the model's left half), picks the one with the largest Z — the one furthest back on that side. (Model faces -Z, so larger Z = behind.) Use for: back-of-left-buttock, back of left tricep. Authored together with its right-half twin. Selects 1 vertex per row; the pair selects 2.")]
    MaxZLeftOfX = 20,
    [ShortLabel("Backmost on Right Half (X>=0)")]
    [Description("Mirror of 'Backmost on Left Half': picks the back-most vertex on the model's right side. Selects 1 vertex per row; the pair selects 2.")]
    MaxZRightOfX = 21,
    [ShortLabel("Rightmost on Centerline")]
    [Description("Restricts the search to vertices whose Y AND Z coordinates both fall in the middle third of the box (a tubular column running through the box along X), then picks the one with the largest X inside that tube. Use for: side-of-waist landmarks that should sit at the body's vertical mid-height and depth-center rather than at a corner of the box. Selects 1 vertex.")]
    MaxXAtCenter = 22,
    [ShortLabel("Leftmost on Centerline")]
    [Description("Mirror of 'Rightmost on Centerline': restricts the search to the central Y/Z tube and picks the smallest X. Selects 1 vertex.")]
    MinXAtCenter = 23,
    [ShortLabel("Highest on Centerline")]
    [Description("Restricts the search to vertices whose X AND Z coordinates both fall in the middle third of the box (a tubular column running through the box along Y), then picks the one with the largest Y inside that tube. Use for: top-of-head, peak of bust at the body's mid-line, top-of-shoulder — top landmarks that should sit on the body's centerline rather than at a corner of the box. Selects 1 vertex.")]
    MaxYAtCenter = 24,
    [ShortLabel("Lowest on Centerline")]
    [Description("Mirror of 'Highest on Centerline': restricts the search to the central X/Z tube and picks the smallest Y. Selects 1 vertex.")]
    MinYAtCenter = 25,
    [ShortLabel("Backmost on Centerline")]
    [Description("Restricts the search to vertices whose X AND Y coordinates both fall in the middle third of the box (a tubular column running through the box along Z), then picks the one with the largest Z inside that tube. (Model faces -Z, so larger Z = behind.) Use for: spine landmarks, upper-back peak, sacrum-back — rear landmarks that should sit on the body's centerline rather than at a corner of the box. Selects 1 vertex.")]
    MaxZAtCenter = 26,
    [ShortLabel("Frontmost on Centerline")]
    [Description("Restricts the search to the central X/Y tube and picks the smallest Z. (Model faces -Z, so smaller Z = in front.) Use for: navel, nipple, peak-of-belly — front protrusion landmarks that should sit on the body's centerline rather than at a corner of the box. Selects 1 vertex.")]
    MinZAtCenter = 27,
    [ShortLabel("Bone Transition Left (Min X)")]
    [Description("Finds the anatomical seam between two rigged body parts by following the skinning data. Identifies the dominant bone of the vertex closest to the box center (the 'root' bone), then walks vertices on the X<center side outward and returns the last vertex that still belongs to the root bone — i.e. the last vertex on the inboard side before the boundary into a neighboring bone region. Use for: left underarm (root=spine, neighbor=arm), wrist, ankle, neck-base, hip socket — any anatomical landmark on a bone-weight boundary. Robust across body types because the algorithm reads the rig, not the silhouette. Each side is picked independently — use the Paired variant when both picks need to be at the same Y. Selects 1 vertex. Falls back to plain Leftmost (Min X) on unskinned shapes.")]
    BoneTransitionMinX = 28,
    [ShortLabel("Bone Transition Right (Max X)")]
    [Description("Mirror of 'Bone Transition Left': walks vertices on the X>center side outward from the box-center vertex and returns the last vertex still belonging to the root bone. Each side is picked independently. Selects 1 vertex. Falls back to plain Rightmost (Max X) on unskinned shapes.")]
    BoneTransitionMaxX = 29,
    [ShortLabel("Paired Bone Transition Left (joint-Y)")]
    [Description("Co-operates with a sibling row using 'Paired Bone Transition Right' that shares the same Shape and box. Each side independently locates its bone-transition pick, then both rows are snapped onto the average of the two picks' Y coordinates — so a PointDistance between them measures the seam-to-seam X-distance at one consistent height. Use for: underarm-to-underarm width, wrist-to-wrist, ankle-to-ankle. Selects 1 vertex per row; the pair selects 2 at the same Y. Without a sibling, falls back to plain Bone Transition Left.")]
    BoneTransitionPairMinX = 30,
    [ShortLabel("Paired Bone Transition Right (joint-Y)")]
    [Description("Sibling of Paired Bone Transition Left — see that entry for the joint-Y mechanic. Selects 1 vertex per row; the pair selects 2.")]
    BoneTransitionPairMaxX = 31,
    [ShortLabel("Depth-pinch Front (Min Z)")]
    [Description("Slices the box's vertical range into thin horizontal bands, finds the band where the body is thinnest front-to-back (smallest Z-depth), then picks that band's front-side vertex (smallest Z). (Model faces -Z, so smaller Z = front.) Use for: front edge of the narrowest part of a limb segment. Selects 1 vertex. For honest front-to-back depth measurement, prefer the Paired variant.")]
    PinchMinZ = 32,
    [ShortLabel("Depth-pinch Back (Max Z)")]
    [Description("Mirror of Depth-pinch Front: scans for the thinnest (front-to-back) band and picks that band's back-side vertex (largest Z). Selects 1 vertex. For depth measurement, prefer the Paired variant.")]
    PinchMaxZ = 33,
    [ShortLabel("Depth-bulge Front (Min Z)")]
    [Description("Slices the box's vertical range into thin horizontal bands, finds the band where the body is thickest front-to-back (largest Z-depth), then picks that band's front-side vertex (smallest Z). (Model faces -Z, so smaller Z = front.) Use for: front edge of the bicep peak, belly, or calf. Selects 1 vertex. For honest front-to-back depth measurement, prefer the Paired variant.")]
    BulgeMinZ = 34,
    [ShortLabel("Depth-bulge Back (Max Z)")]
    [Description("Mirror of Depth-bulge Front: scans for the thickest (front-to-back) band and picks that band's back-side vertex (largest Z). Selects 1 vertex. For depth measurement, prefer the Paired variant.")]
    BulgeMaxZ = 35,
    [ShortLabel("Paired Depth-pinch Front")]
    [Description("Co-operates with a sibling row using 'Paired Depth-pinch Back' that shares the same Shape and box. The two rows scan jointly for the single horizontal band where the body is thinnest front-to-back and pick from THAT band — front row gets the front-side vertex (smallest Z), back row gets the back-side vertex (largest Z). Guarantees both picks come from the same height, so a PointDistance between them measures honest front-to-back depth. Selects 1 vertex per row; the pair selects 2 at the same Y. Without a sibling, falls back to plain Depth-pinch Front.")]
    PinchPairMinZ = 36,
    [ShortLabel("Paired Depth-pinch Back")]
    [Description("Sibling of Paired Depth-pinch Front — see that entry for the joint-band mechanic. Selects 1 vertex per row; the pair selects 2.")]
    PinchPairMaxZ = 37,
    [ShortLabel("Paired Depth-bulge Front")]
    [Description("Co-operates with a sibling row using 'Paired Depth-bulge Back' that shares the same Shape and box. The two rows scan jointly for the single horizontal band where the body is thickest front-to-back and pick from THAT band — front row gets the front-side vertex (smallest Z), back row gets the back-side vertex (largest Z). Guarantees honest front-to-back depth measurement (e.g. bicep thickness). Selects 1 vertex per row; the pair selects 2. Without a sibling, falls back to plain Depth-bulge Front.")]
    BulgePairMinZ = 38,
    [ShortLabel("Paired Depth-bulge Back")]
    [Description("Sibling of Paired Depth-bulge Front — see that entry for the joint-band mechanic. Selects 1 vertex per row; the pair selects 2.")]
    BulgePairMaxZ = 39,
    [ShortLabel("Mirror Left/Right (X)")]
    [Description("Authoring shortcut: one drag, two rows. Creates 'Rightmost (Max X)' + 'Leftmost (Min X)' rows sharing this box — captures both sides of a symmetric left/right feature at once. Picks 2 vertices total.")]
    MirrorX = 100,
    [ShortLabel("Mirror Top/Bottom (Y)")]
    [Description("Authoring shortcut: one drag, two rows. Creates 'Highest (Max Y)' + 'Lowest (Min Y)' rows sharing this box — captures the top and bottom of a vertical feature at once. Picks 2 vertices total.")]
    MirrorY = 101,
    [ShortLabel("Mirror Front/Back (Z)")]
    [Description("Authoring shortcut: one drag, two rows. Creates 'Backmost (Max Z)' + 'Frontmost (Min Z)' rows sharing this box — captures both ends of a front-to-back feature at once. Picks 2 vertices total.")]
    MirrorZ = 102,
    [ShortLabel("Mirror Paired Waist-pinch (joint-band)")]
    [Description("Authoring shortcut: one drag, two rows. Creates 'Paired Waist-pinch Left' + 'Paired Waist-pinch Right' rows sharing this box — both rows scan jointly for the single narrowest horizontal band and pick from that band, so a PointDistance between them measures honest waist width. Picks 2 vertices total at the same Y.")]
    MirrorPinchX = 103,
    [ShortLabel("Mirror Paired Hip-bulge (joint-band)")]
    [Description("Authoring shortcut: one drag, two rows. Creates 'Paired Hip-bulge Left' + 'Paired Hip-bulge Right' rows sharing this box — both rows scan jointly for the single widest horizontal band and pick from that band, so a PointDistance between them measures honest hip width. Picks 2 vertices total at the same Y.")]
    MirrorBulgeX = 104,
    [ShortLabel("Mirror Lowest L/R Half (Min Y)")]
    [Description("Authoring shortcut: one drag, two rows. Creates 'Lowest on Left Half' + 'Lowest on Right Half' rows sharing this box — each row picks the lowest vertex on its half. Use for: bottom-of-each-bust, lowest-fold-of-each-shoulder. Picks 2 vertices total (heights may differ between sides).")]
    MinYMirroredAcrossX = 105,
    [ShortLabel("Mirror Highest L/R Half (Max Y)")]
    [Description("Authoring shortcut: one drag, two rows. Creates 'Highest on Left Half' + 'Highest on Right Half' rows sharing this box — each row picks the highest vertex on its half. Use for: top-of-each-bust, peak-of-each-shoulder. Picks 2 vertices total (heights may differ between sides).")]
    MaxYMirroredAcrossX = 106,
    [ShortLabel("Mirror Frontmost L/R Half (Min Z)")]
    [Description("Authoring shortcut: one drag, two rows. Creates 'Frontmost on Left Half' + 'Frontmost on Right Half' rows sharing this box — each row picks the front-most vertex on its half. (Model faces -Z, so smaller Z = in front.) Use for: front-of-each-bust, front-of-each-bicep. Picks 2 vertices total.")]
    MinZMirroredAcrossX = 107,
    [ShortLabel("Mirror Backmost L/R Half (Max Z)")]
    [Description("Authoring shortcut: one drag, two rows. Creates 'Backmost on Left Half' + 'Backmost on Right Half' rows sharing this box — each row picks the back-most vertex on its half. (Model faces -Z, so larger Z = behind.) Use for: back-of-each-buttock, back-of-each-tricep. Picks 2 vertices total.")]
    MaxZMirroredAcrossX = 108,
    [ShortLabel("Mirror Bone Transition (X)")]
    [Description("Authoring shortcut: one drag, two rows. Creates 'Bone Transition Left' + 'Bone Transition Right' rows sharing this box — each row walks outward from the box-center vertex on its side and stops at the first bone change. Use for: underarm-to-underarm (box centered on spine at armpit Y), wrist-to-wrist, ankle-to-ankle. Robust across body types because the algorithm reads the rig rather than the silhouette. Picks 2 vertices total.")]
    MirrorBoneTransitionX = 109,
    [ShortLabel("Mirror Paired Depth-pinch (joint-band)")]
    [Description("Authoring shortcut: one drag, two rows. Creates 'Paired Depth-pinch Front' + 'Paired Depth-pinch Back' rows sharing this box — both rows scan jointly for the single thinnest front-to-back band and pick from that band, so a PointDistance between them measures honest front-back depth. (Model faces -Z, so smaller Z = front.) Picks 2 vertices total at the same Y.")]
    MirrorPinchZ = 110,
    [ShortLabel("Mirror Paired Depth-bulge (joint-band)")]
    [Description("Authoring shortcut: one drag, two rows. Creates 'Paired Depth-bulge Front' + 'Paired Depth-bulge Back' rows sharing this box — both rows scan jointly for the single thickest front-to-back band and pick from that band, so a PointDistance between them measures honest front-back depth (e.g. bicep thickness). (Model faces -Z, so smaller Z = front.) Picks 2 vertices total at the same Y.")]
    MirrorBulgeZ = 111,
}
