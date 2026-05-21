using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;

namespace SynthEBD;

/// <summary>
/// Authoring data for the BodySlide Classifier's 3D-mesh measurement pipeline.
///
/// A profile is authored once per body type (CBBE 3BA, BHUNP, HIMBO, ...) and re-used across
/// every BodySlide preset that shares that body's topology. Key vertices, measurement
/// definitions, and classifier rules all live here; the evaluator (Phase 5) reads them to
/// produce <see cref="AnnotatedDescriptorSignature"/>s with <c>Source = Classifier</c> that
/// merge into <see cref="BodySlideSetting.BodyShapeDescriptorsByWeight"/>.
///
/// Profiles reference a <see cref="BodyTypeRegistryEntry.Name"/> via <see cref="BodyTypeName"/>
/// so they stay in sync with the installed-body detection in <see cref="Settings_OBody.BodyTypeRegistry"/>.
/// </summary>
[DebuggerDisplay("{Name} ({BodyTypeName}) -- {KeyVertices.Count} verts / {Measurements.Count} meas / {Rules.Count} rules")]
public class BodyTypeProfile
{
    /// <summary>Stable identifier for this profile. Used to reference it from other settings without relying on Name.</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>Display name shown in the editor UI.</summary>
    public string Name { get; set; } = "";

    /// <summary>
    /// Body type this profile applies to. Matches <see cref="BodyTypeRegistryEntry.Name"/>
    /// (e.g. "CBBE 3BA"). Empty = unassigned / authoring in progress.
    /// </summary>
    public string BodyTypeName { get; set; } = "";

    /// <summary>
    /// Topology fingerprint captured at authoring time. Used to warn when a preset about to
    /// be classified has a different vertex layout than the profile was built against
    /// (e.g. user authored against CBBE 3BA but a preset references a UUNP shape).
    /// </summary>
    public TopologyFingerprint Fingerprint { get; set; } = new();

    /// <summary>
    /// Named vertex handles keyed into the body mesh. Indices are stable across presets that
    /// share topology, so authoring once and re-using is correct.
    /// </summary>
    public List<NamedKeyVertex> KeyVertices { get; set; } = new();

    /// <summary>
    /// Measurement definitions. Each produces a single float per (preset, weight) evaluation.
    /// </summary>
    public List<MeasurementDefinition> Measurements { get; set; } = new();

    /// <summary>
    /// Classifier rules. Each maps a DNF predicate over measurement values to one
    /// <see cref="BodyShapeDescriptor.LabelSignature"/>. When the predicate matches, the
    /// evaluator emits that descriptor with <c>Source = Classifier</c>.
    /// </summary>
    public List<MeasurementRule> Rules { get; set; } = new();

    /// <summary>
    /// Per-preset descriptor annotations from the new "Label-then-suggest" workflow. Each entry
    /// records the full descriptor signatures the user assigned to one (preset, gender, weight)
    /// slice. Persisted as drafts and used as input to the Suggest Measurements / Suggest Rules
    /// algorithms. Never consumed by the evaluator directly.
    /// </summary>
    public List<PresetAnnotation> PresetAnnotations { get; set; } = new();

    /// <summary>
    /// Persisted preferences for the Label-then-suggest UI: which weight slots to enumerate,
    /// which measurement columns are visible in the annotation table, and which algorithms
    /// the Suggest Measurements / Suggest Rules panels default to.
    /// </summary>
    public AnnotatorPreferences AnnotatorPrefs { get; set; } = new();
}

/// <summary>
/// Lightweight signature used to detect when a preset's mesh topology differs from what the
/// profile was authored against. Not a cryptographic hash -- the goal is just to flag
/// obvious mismatches (e.g. different body mod entirely), not to detect every morph change.
/// </summary>
public class TopologyFingerprint
{
    /// <summary>Total vertex count across all body shapes at capture time.</summary>
    public int VertexCount { get; set; } = 0;

    /// <summary>Per-shape vertex counts (shape name -> count). Lets the evaluator detect a mismatched shape even if the total happens to collide.</summary>
    public Dictionary<string, int> ShapeVertexCounts { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// A handful of representative vertex indices sampled at capture time. Reserved for a
    /// future position-hash check; currently unused by the evaluator but persisted so older
    /// profiles remain valid if that check is added later.
    /// </summary>
    public List<int> SampleIndices { get; set; } = new();
}

/// <summary>How a <see cref="NamedKeyVertex"/> selects its target vertex at evaluation time.</summary>
public enum KeyVertexStrategy
{
    /// <summary>Use the stored <see cref="NamedKeyVertex.VertexIndex"/> as-is. Legacy behavior.</summary>
    Explicit = 0,

    /// <summary>Scan all vertices inside a mesh-local AABB and pick by
    /// <see cref="NamedKeyVertex.Criterion"/>. Lets a single entry track an anatomical feature
    /// (widest X in hip band, frontmost Z on breast, etc.) across BodySlide presets even when
    /// the extremal vertex migrates to a neighbor index.</summary>
    BoundingBox = 1,
}

/// <summary>Extremum to select inside a <see cref="KeyVertexStrategy.BoundingBox"/> region.
/// Operates on mesh-local axes (NIF: X = left-right, Y = up-down, Z = front-back). The default
/// model orientation in the viewer is facing the -Z direction, so smaller Z = in front of the
/// body (toward the viewer / "Min Z = front-most"), larger Z = behind the body ("Max Z = back-most").
/// <para>The <c>Pinch*</c> / <c>Bulge*</c> values scan the box's Y range in slices and pick the
/// silhouette vertex whose X is closest to (pinch) or farthest from (bulge) the midline —
/// suitable for waist-pinch and widest-hip anchors respectively.</para>
/// <para>The <c>*Pair*X</c> values require a sibling row in the same profile with the opposite
/// pair criterion and identical ShapeName + box coordinates. When a sibling is present the
/// two rows jointly scan the shared box and pick vertices from the <em>same Y-slice</em> — the
/// slice that minimizes (pinch) or maximizes (bulge) <c>maxX - minX</c>. This guarantees a
/// <c>PointDistance</c> between the pair measures horizontal thickness rather than a diagonal
/// across different Y-levels. When no sibling is found the row gracefully falls back to its
/// non-paired equivalent (<c>PinchPairMinX</c> → <c>PinchMinX</c>, etc.).</para></summary>
public enum BoundingBoxCriterion
{
    // DisplayName = short label shown on the closed picker button and the TreeView leaf.
    // Description = long, plain-language tooltip: how it picks, what it's good for, how
    // many vertices it selects. The TreeView's category grouping (Axis Extremes /
    // Mirrored Across Midline / Waist Pinch / Hip Bulge / Paired Pinch & Bulge /
    // Centerline-Anchored) lives in BoundingBoxCriterionTree.cs alongside this enum.
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
    [Description("Picks the single vertex inside the box with the largest Z coordinate — the one furthest back. (The default model orientation faces -Z, so larger Z = behind the body.) Use for: spine-back, sacrum-back, back-of-buttock. Selects 1 vertex.")]
    MaxZ = 4,
    [ShortLabel("Frontmost (Min Z)")]
    [Description("Picks the single vertex inside the box with the smallest Z coordinate — the one furthest forward. (The default model orientation faces -Z, so smaller Z = in front of the body.) Use for: nipple-front, navel, belly-front. Selects 1 vertex.")]
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
    [Description("Finds the anatomical seam between two rigged body parts by following the skinning data. Identifies the dominant bone of the vertex closest to the box center (the 'root' bone), then walks vertices on the X<center side outward and returns the last vertex that still belongs to the root bone — i.e. the last vertex on the inboard side before the boundary into a neighboring bone region. Use for: left underarm (root=spine, neighbor=arm), wrist, ankle, neck-base, hip socket — any anatomical landmark that sits on a bone-weight boundary. Robust across body types because the algorithm reads the rig, not the silhouette. Each side is picked independently, so the left and right rows may land at slightly different Y heights — use the Paired variant when you need both picks at the same Y. Selects 1 vertex. Falls back to plain Leftmost (Min X) on unskinned shapes.")]
    BoneTransitionMinX = 28,
    [ShortLabel("Bone Transition Right (Max X)")]
    [Description("Mirror of 'Bone Transition Left': walks vertices on the X>center side outward from the box-center vertex and returns the last vertex still belonging to the root bone before the boundary into a neighboring bone region. Use for: right underarm (root=spine, neighbor=arm), and mirrored siblings of every Bone Transition Left use case. Each side is picked independently. Selects 1 vertex. Falls back to plain Rightmost (Max X) on unskinned shapes.")]
    BoneTransitionMaxX = 29,
    [ShortLabel("Paired Bone Transition Left (joint-Y)")]
    [Description("Co-operates with a sibling row using 'Paired Bone Transition Right' that shares the same Shape and box. Each side independently locates its bone-transition pick (most-lateral root-bone vertex on its half), then both rows are snapped onto the average of the two picks' Y coordinates — so a PointDistance between them measures the seam-to-seam X-distance at one consistent height rather than a diagonal across slightly different Y values. Use for: underarm-to-underarm width measurement, wrist-to-wrist, ankle-to-ankle. Selects 1 vertex per row; the pair selects 2 at the same Y. Without a sibling, falls back to plain Bone Transition Left.")]
    BoneTransitionPairMinX = 30,
    [ShortLabel("Paired Bone Transition Right (joint-Y)")]
    [Description("Sibling of Paired Bone Transition Left — see that entry for the joint-Y mechanic. Selects 1 vertex per row; the pair selects 2.")]
    BoneTransitionPairMaxX = 31,
}

// SymmetryAxes and BoxCriterionSelection enums moved to
// CharacterViewer.Rendering/Abstractions/ClassifierEnums.cs (Phase B2d):
// VM_CharacterViewer's BB-pick UI state references them and the viewer
// no longer takes a SynthEBD dependency. The global using
// `<Using Include="CharacterViewer.Rendering" />` in SynthEBD.csproj
// keeps the call sites here resolvable without a per-file using directive.

/// <summary>
/// A user-named vertex handle referencing a single vertex inside a specific body shape mesh.
/// </summary>
[DebuggerDisplay("{Name} @ {ShapeName}[{VertexIndex}] ({Strategy})")]
public class NamedKeyVertex
{
    /// <summary>User-provided name (e.g. "LShoulder", "Waist_Front"). Unique within a profile.</summary>
    public string Name { get; set; } = "";

    /// <summary>Name of the shape mesh (e.g. "CBBE", "3BA") this vertex belongs to. Matches <see cref="GlMesh"/> shape naming.</summary>
    public string ShapeName { get; set; } = "";

    /// <summary>
    /// When <see cref="Strategy"/> = <see cref="KeyVertexStrategy.Explicit"/>, the literal vertex index.
    /// When Strategy = <see cref="KeyVertexStrategy.BoundingBox"/>, a cache of the last resolved index
    /// (updated each time the evaluator scans the box) — valid for marker display but recomputed per evaluation.
    /// </summary>
    public int VertexIndex { get; set; } = -1;

    /// <summary>Selection strategy. Defaults to Explicit so existing profiles load unchanged.</summary>
    public KeyVertexStrategy Strategy { get; set; } = KeyVertexStrategy.Explicit;

    /// <summary>Mesh-local AABB min corner. Only consulted when <see cref="Strategy"/> = BoundingBox.</summary>
    public float BoxMinX { get; set; }
    public float BoxMinY { get; set; }
    public float BoxMinZ { get; set; }

    /// <summary>Mesh-local AABB max corner. Only consulted when <see cref="Strategy"/> = BoundingBox.</summary>
    public float BoxMaxX { get; set; }
    public float BoxMaxY { get; set; }
    public float BoxMaxZ { get; set; }

    /// <summary>Which extremum to pick inside the box. Only consulted when <see cref="Strategy"/> = BoundingBox.</summary>
    public BoundingBoxCriterion Criterion { get; set; } = BoundingBoxCriterion.MaxX;
}

/// <summary>
/// Kinds of geometric measurement the evaluator supports.
/// </summary>
public enum MeasurementKind
{
    /// <summary>Euclidean distance between two key vertices (raw units).</summary>
    PointDistance = 0,

    /// <summary>Scale-invariant ratio ||A-B|| / ||C-D||. Four vertex refs required.</summary>
    RatioDistance = 1,

    /// <summary>Distance between two key vertices projected onto a single world axis (raw units).</summary>
    AxisDistance = 2,
}

/// <summary>World axis selector for <see cref="MeasurementKind.AxisDistance"/>.</summary>
public enum MeasurementAxis
{
    X = 0,
    Y = 1,
    Z = 2,
}

/// <summary>
/// A single named measurement. Value is always a scalar float; callers look it up by Name.
/// </summary>
[DebuggerDisplay("{Name} ({Kind})")]
public class MeasurementDefinition
{
    /// <summary>Unique name within the profile; referenced by <see cref="MeasurementCondition.MeasurementName"/>.</summary>
    public string Name { get; set; } = "";

    public MeasurementKind Kind { get; set; } = MeasurementKind.PointDistance;

    /// <summary>
    /// Names of the key vertices this measurement reads, in definition order.
    /// Point/Axis: two entries [A, B]. Ratio: four entries [A, B, C, D] for ||A-B||/||C-D||.
    /// </summary>
    public List<string> VertexRefNames { get; set; } = new();

    /// <summary>Only consulted when <see cref="Kind"/> is <see cref="MeasurementKind.AxisDistance"/>.</summary>
    public MeasurementAxis Axis { get; set; } = MeasurementAxis.X;

    /// <summary>
    /// Only consulted when <see cref="Kind"/> is <see cref="MeasurementKind.RatioDistance"/>. Selects how
    /// the numerator pair (A, B) is reduced to a scalar: an axis projection (X/Y/Z) or full 3D length.
    /// Null = legacy 3D Euclidean, so existing profiles deserialize unchanged.
    /// </summary>
    public MeasurementAxis? NumeratorAxis { get; set; } = null;

    /// <summary>
    /// Only consulted when <see cref="Kind"/> is <see cref="MeasurementKind.RatioDistance"/>. Selects how
    /// the denominator pair (C, D) is reduced to a scalar. Null = legacy 3D Euclidean.
    /// </summary>
    public MeasurementAxis? DenominatorAxis { get; set; } = null;
}

/// <summary>Threshold comparator applied between a measurement value and a constant.</summary>
public enum MeasurementComparator
{
    LessThan = 0,
    LessThanOrEqual = 1,
    GreaterThan = 2,
    GreaterThanOrEqual = 3,
    EqualTo = 4,
    NotEqualTo = 5,
}

/// <summary>Which side of the predicate vocabulary a <see cref="MeasurementCondition"/> uses.
/// <see cref="Measurement"/> (default) is the classical <c>measurement [comparator] value</c>
/// test; <see cref="DescriptorRef"/> is an aggregator-style "did another rule's descriptor
/// already fire on this evaluation?" test, allowing one rule to derive its output from the
/// match-set of other rules (e.g. a Realism:Unrealistic aggregator built from UnrealisticChest
/// OR UnrealisticButt OR UnrealisticWaist). Mixing both kinds inside a single AND group is
/// permitted — the engine just AND-s the booleans either way.</summary>
public enum MeasurementConditionKind
{
    /// <summary>Default: the condition compares a measurement value against a threshold.</summary>
    Measurement = 0,
    /// <summary>The condition tests whether a specific (Category, Value) descriptor was
    /// produced by some other rule earlier in the topological evaluation order. Reads
    /// <see cref="MeasurementCondition.RefCategory"/>, <see cref="MeasurementCondition.RefValue"/>,
    /// and <see cref="MeasurementCondition.Negate"/>; ignores
    /// <see cref="MeasurementCondition.MeasurementName"/>,
    /// <see cref="MeasurementCondition.Comparator"/>, and
    /// <see cref="MeasurementCondition.Value"/>.</summary>
    DescriptorRef = 1,
}

/// <summary>
/// A single predicate condition. Defaults to a <see cref="MeasurementConditionKind.Measurement"/>
/// threshold test (<c>measurement [comparator] value</c>); when <see cref="Kind"/> is
/// <see cref="MeasurementConditionKind.DescriptorRef"/> the condition instead tests whether
/// another rule has already produced descriptor (<see cref="RefCategory"/>, <see cref="RefValue"/>),
/// optionally negated via <see cref="Negate"/>.
/// </summary>
[DebuggerDisplay("{Kind}: {MeasurementName} {Comparator} {Value} | ref={RefCategory}:{RefValue} neg={Negate}")]
public class MeasurementCondition
{
    /// <summary>Selects which fields are read at evaluation time. Defaults to
    /// <see cref="MeasurementConditionKind.Measurement"/> so existing JSON deserializes
    /// without migration — the old measurement-only schema becomes the default kind.</summary>
    public MeasurementConditionKind Kind { get; set; } = MeasurementConditionKind.Measurement;

    // --- Measurement-kind fields ---
    public string MeasurementName { get; set; } = "";
    public MeasurementComparator Comparator { get; set; } = MeasurementComparator.GreaterThan;
    public float Value { get; set; } = 0f;

    // --- DescriptorRef-kind fields ---
    /// <summary>When <see cref="Kind"/> = <see cref="MeasurementConditionKind.DescriptorRef"/>,
    /// the descriptor Category this condition references. Ignored otherwise.</summary>
    public string RefCategory { get; set; } = "";

    /// <summary>When <see cref="Kind"/> = <see cref="MeasurementConditionKind.DescriptorRef"/>,
    /// the descriptor Value this condition references. Ignored otherwise.</summary>
    public string RefValue { get; set; } = "";

    /// <summary>When <see cref="Kind"/> = <see cref="MeasurementConditionKind.DescriptorRef"/>
    /// and <see cref="Negate"/> is true, the condition matches when the referenced descriptor
    /// is <em>absent</em> from the current match set (the "exclude" flavor). Defaults to false:
    /// match when the descriptor is present. Ignored for <see cref="MeasurementConditionKind.Measurement"/>.</summary>
    public bool Negate { get; set; } = false;
}

/// <summary>
/// AND-gated bundle of <see cref="MeasurementCondition"/>s. All conditions in the bundle must
/// evaluate true for the bundle to match.
/// </summary>
public class AndGatedMeasurementGroup
{
    public List<MeasurementCondition> ConditionsANDlogic { get; set; } = new();
}

/// <summary>
/// A classifier rule in disjunctive normal form (OR of ANDs), mirroring the slider rule shape
/// used by <see cref="SliderClassificationRulesByBodyType"/>. When any group matches, the rule
/// emits <see cref="Descriptor"/> as a Classifier-sourced annotation for the evaluated weight slot.
/// </summary>
[DebuggerDisplay("{Descriptor.Category}:{Descriptor.Value} ({GroupsORlogic.Count} group(s))")]
public class MeasurementRule
{
    /// <summary>Stable identifier -- lets the UI preserve selection across rule reorders and name edits.</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>The descriptor this rule produces when its predicate matches.</summary>
    public BodyShapeDescriptor.LabelSignature Descriptor { get; set; } = new();

    /// <summary>OR of AND-gated groups. Empty list = rule never matches (treated as disabled).</summary>
    public List<AndGatedMeasurementGroup> GroupsORlogic { get; set; } = new();

    /// <summary>
    /// True when this rule was produced by the "Label-then-suggest" helper and has not yet been
    /// reviewed/edited by the user. Suggestions are never applied automatically -- the evaluator
    /// skips rules with <see cref="IsDraft"/> = true until the user promotes them.
    /// </summary>
    public bool IsDraft { get; set; } = false;
}

/// <summary>
/// One draft annotation in the Label-then-suggest workflow: the full set of descriptor
/// signatures the user has tagged onto a single (preset, gender, weight) slice. Persisted on
/// the profile as draft state -- used as training input for the Suggest Measurements and
/// Suggest Rules algorithms, never consumed by the evaluator.
/// </summary>
[DebuggerDisplay("{PresetLabel}[{Weight}] -> {Descriptors.Count} descriptors")]
public class PresetAnnotation
{
    /// <summary>BodySlide preset identifier. Uses <see cref="BodySlideSetting.Label"/> -- the same
    /// stable human-readable key used elsewhere in the editor. If the preset is renamed the
    /// annotation becomes orphaned and the suggest passes skip it.</summary>
    public string PresetLabel { get; set; } = "";

    /// <summary>Gender of the preset (presets are split into male/female lists).</summary>
    public Gender PresetGender { get; set; } = Gender.Female;

    /// <summary>Weight slot (0-100) at which the annotation was captured. The viewer interpolates
    /// between the preset's authored weight extremes when computing measurements at this slot.</summary>
    public int Weight { get; set; } = 50;

    /// <summary>Descriptor signatures the user has assigned to this (preset, weight) slice. A
    /// preset can carry multiple descriptors (e.g. BodyShape=Athletic + Tone=Toned); each one
    /// participates in its own per-Category training group during Suggest Measurements.</summary>
    public List<BodyShapeDescriptor.LabelSignature> Descriptors { get; set; } = new();
}

/// <summary>Algorithm used by the Suggest Measurements pass to score how well each measurement
/// discriminates between annotated descriptor-value groups within a Category.</summary>
public enum MeasurementSelectionAlgorithm
{
    /// <summary>One-way ANOVA F-statistic. Default. Handles >=2 groups per Category; ranks
    /// measurements by between-group variance over within-group variance.</summary>
    Anova = 0,

    /// <summary>Cohen's d (|mean1 - mean2| / pooled std). Pairwise; for Categories with >2
    /// values, the largest pairwise d across all value pairs is reported.</summary>
    CohenD = 1,

    /// <summary>Information gain from a best single-threshold split. Picks the threshold that
    /// maximises entropy reduction over the descriptor-value labels.</summary>
    InformationGain = 2,
}

/// <summary>Algorithm used by the Suggest Rules pass to turn a locked-in set of discriminating
/// measurements into draft <see cref="MeasurementRule"/>s with concrete thresholds.</summary>
public enum RuleSynthesisAlgorithm
{
    /// <summary>For each (Category, Value) target, pick the threshold per measurement that
    /// maximises Youden's J (TPR - FPR) treating annotations matching the target as positives.
    /// Default. Yields one rule per (target, measurement) pair, OR-combined into the final rule.</summary>
    OptimalThresholdPerValue = 0,

    /// <summary>Legacy median-split: threshold halfway between positive and negative group
    /// medians, comparator chosen to favour positives. One rule per (target, measurement) pair.</summary>
    MedianSplit = 1,

    /// <summary>Single-split decision stump per (Category, Value) target: chooses the (measurement,
    /// threshold) combination that minimises Gini impurity over the positive/negative labels.
    /// Yields one rule per target rather than one per measurement.</summary>
    DecisionStump = 2,
}

/// <summary>
/// Persisted preferences for the Label-then-suggest UI on a <see cref="BodyTypeProfile"/>.
/// Lives on the profile (not on Settings_OBody) because column visibility is per-measurement-set
/// and weight-slot needs vary by body type / authoring task.
/// </summary>
public class AnnotatorPreferences
{
    /// <summary>Weight slots to enumerate in the annotation table, one row per (preset, slot).
    /// Defaults to [0, 25, 50, 75, 100]. Values are clamped to [0, 100] at scan time.</summary>
    public List<int> WeightSlots { get; set; } = new() { 0, 25, 50, 75, 100 };

    /// <summary>Names of <see cref="MeasurementDefinition"/>s that should be visible as columns
    /// in the annotation table. Empty list = show every measurement (default for new profiles).
    /// Names that no longer resolve to a measurement are silently ignored.</summary>
    public List<string> VisibleMeasurementColumns { get; set; } = new();

    /// <summary>Default algorithm used by the Suggest Measurements panel. The user can switch
    /// at runtime; switching updates this value so the choice survives across sessions.</summary>
    public MeasurementSelectionAlgorithm SelectionAlgorithm { get; set; } = MeasurementSelectionAlgorithm.Anova;

    /// <summary>Default algorithm used by the Suggest Rules panel. Same persistence semantics
    /// as <see cref="SelectionAlgorithm"/>.</summary>
    public RuleSynthesisAlgorithm SynthesisAlgorithm { get; set; } = RuleSynthesisAlgorithm.OptimalThresholdPerValue;
}

/// <summary>
/// Pure-math helpers for evaluating <see cref="MeasurementDefinition"/>s and
/// <see cref="MeasurementCondition"/>s. Phase 4's editor uses these for the live readout
/// column; Phase 5's <c>BodySlideMeasurementEvaluator</c> uses them in the classifier pipeline.
/// </summary>
public static class MeasurementMath
{
    /// <summary>Vertex lookup: returns the local-space position of a (shape, index) pair, or null when missing.</summary>
    public delegate OpenTK.Mathematics.Vector3? VertexLookup(string shapeName, int vertexIndex);

    /// <summary>Full-shape positions lookup: returns every vertex position for a shape, or null when the
    /// shape isn't loaded. Needed to resolve <see cref="KeyVertexStrategy.BoundingBox"/> entries — the
    /// per-index <see cref="VertexLookup"/> can't scan an AABB on its own.</summary>
    public delegate OpenTK.Mathematics.Vector3[]? ShapePositionsLookup(string shapeName);

    /// <summary>Per-shape bone-info lookup, paired with <see cref="ShapePositionsLookup"/>. Returns the
    /// per-vertex bone indices and weights (4 entries each, flat-packed: <c>boneIndices[vi*4+k]</c> is the
    /// k-th bone for vertex vi with weight <c>boneWeights[vi*4+k]</c>) for the shape, or <c>(null, null)</c>
    /// when the shape is unskinned / not loaded. Consulted only by <see cref="BoundingBoxCriterion.BoneTransitionMinX"/>
    /// and <see cref="BoundingBoxCriterion.BoneTransitionMaxX"/>; everything else ignores it.</summary>
    public delegate (int[]? BoneIndices, float[]? BoneWeights) ShapeBoneInfoLookup(string shapeName);

    /// <summary>
    /// Evaluates a measurement against a vertex lookup. Returns false when any required vertex
    /// is missing (orphaned reference, shape not loaded), denominator is near zero (ratio), or
    /// the definition is malformed (wrong vertex-ref count for its kind).
    /// <paramref name="shapeLookup"/> is only consulted for <see cref="KeyVertexStrategy.BoundingBox"/>
    /// entries; pass null when only Explicit vertices are in play.
    /// </summary>
    public static bool TryEvaluate(MeasurementDefinition def, IReadOnlyDictionary<string, NamedKeyVertex> keyVertsByName, VertexLookup lookup, out float value)
        => TryEvaluate(def, keyVertsByName, lookup, null, null, out value);

    public static bool TryEvaluate(MeasurementDefinition def, IReadOnlyDictionary<string, NamedKeyVertex> keyVertsByName, VertexLookup lookup, ShapePositionsLookup? shapeLookup, out float value)
        => TryEvaluate(def, keyVertsByName, lookup, shapeLookup, null, out value);

    public static bool TryEvaluate(MeasurementDefinition def, IReadOnlyDictionary<string, NamedKeyVertex> keyVertsByName, VertexLookup lookup, ShapePositionsLookup? shapeLookup, ShapeBoneInfoLookup? boneLookup, out float value)
    {
        value = 0f;
        if (def == null || def.VertexRefNames == null || lookup == null) return false;

        int needed = def.Kind == MeasurementKind.RatioDistance ? 4 : 2;
        if (def.VertexRefNames.Count < needed) return false;

        if (!TryResolve(def.VertexRefNames[0], keyVertsByName, lookup, shapeLookup, boneLookup, out var a)) return false;
        if (!TryResolve(def.VertexRefNames[1], keyVertsByName, lookup, shapeLookup, boneLookup, out var b)) return false;

        switch (def.Kind)
        {
            case MeasurementKind.PointDistance:
                value = (a - b).Length;
                return true;

            case MeasurementKind.AxisDistance:
                value = def.Axis switch
                {
                    MeasurementAxis.X => Math.Abs(a.X - b.X),
                    MeasurementAxis.Y => Math.Abs(a.Y - b.Y),
                    MeasurementAxis.Z => Math.Abs(a.Z - b.Z),
                    _ => 0f,
                };
                return true;

            case MeasurementKind.RatioDistance:
                if (!TryResolve(def.VertexRefNames[2], keyVertsByName, lookup, shapeLookup, boneLookup, out var c)) return false;
                if (!TryResolve(def.VertexRefNames[3], keyVertsByName, lookup, shapeLookup, boneLookup, out var d)) return false;
                float num = AxisOrLength(a - b, def.NumeratorAxis);
                float denom = AxisOrLength(c - d, def.DenominatorAxis);
                if (denom < 1e-6f) return false;
                value = num / denom;
                return true;

            default:
                return false;
        }
    }

    private static bool TryResolve(string vertexRefName, IReadOnlyDictionary<string, NamedKeyVertex> keyVertsByName, VertexLookup lookup, ShapePositionsLookup? shapeLookup, ShapeBoneInfoLookup? boneLookup, out OpenTK.Mathematics.Vector3 pos)
    {
        pos = default;
        if (string.IsNullOrEmpty(vertexRefName)) return false;
        if (!keyVertsByName.TryGetValue(vertexRefName, out var kv) || kv == null) return false;

        if (kv.Strategy == KeyVertexStrategy.BoundingBox)
        {
            if (shapeLookup == null) return false;
            var positions = shapeLookup(kv.ShapeName);
            if (positions == null || positions.Length == 0) return false;
            // Paired criteria need to see peer rows to find their sibling. Build the lookup
            // only when it matters so non-paired resolution stays cheap.
            Func<NamedKeyVertex, NamedKeyVertex?>? findSibling = null;
            if (IsPairCriterion(kv.Criterion))
            {
                findSibling = self => FindPairSibling(self, keyVertsByName.Values);
            }
            // Bone-info is only fetched when the criterion actually consults it; for everything
            // else the lookup stays a no-op so non-skinned shapes (and host code that doesn't
            // bother to surface a boneLookup) keep working unchanged.
            int[]? boneIndices = null;
            float[]? boneWeights = null;
            if (IsBoneTransitionCriterion(kv.Criterion) && boneLookup != null)
            {
                (boneIndices, boneWeights) = boneLookup(kv.ShapeName);
            }
            int? idx = FindBestInBox(positions, kv, kv.Criterion, findSibling, boneIndices, boneWeights);
            if (idx == null) return false;
            kv.VertexIndex = idx.Value; // cache for marker display / downstream lookups
            pos = positions[idx.Value];
            return true;
        }

        var p = lookup(kv.ShapeName, kv.VertexIndex);
        if (p == null) return false;
        pos = p.Value;
        return true;
    }

    /// <summary>Scan a mesh's positions for the vertex inside the <see cref="NamedKeyVertex"/>'s AABB
    /// that best satisfies <paramref name="criterion"/>. Returns null if no vertex falls inside the box.
    /// Exposed for viewer-side marker refresh, which needs to re-resolve BB entries when the mesh deforms.
    /// <para><paramref name="findSibling"/> is consulted only for paired criteria (<c>*Pair*X</c>); given
    /// <paramref name="kv"/>, it must return the partner row (same ShapeName, identical box, opposite pair
    /// criterion) or null. When a sibling is missing the method falls back to the non-paired criterion so
    /// half-built profiles still resolve.</para></summary>
    public static int? FindBestInBox(OpenTK.Mathematics.Vector3[] positions, NamedKeyVertex kv, BoundingBoxCriterion criterion, Func<NamedKeyVertex, NamedKeyVertex?>? findSibling = null, int[]? boneIndices = null, float[]? boneWeights = null)
    {
        if (positions == null || positions.Length == 0) return null;

        switch (criterion)
        {
            case BoundingBoxCriterion.PinchMinX: return FindPinchOrBulgeX(positions, kv, leftSide: true,  wantPinch: true);
            case BoundingBoxCriterion.PinchMaxX: return FindPinchOrBulgeX(positions, kv, leftSide: false, wantPinch: true);
            case BoundingBoxCriterion.BulgeMinX: return FindPinchOrBulgeX(positions, kv, leftSide: true,  wantPinch: false);
            case BoundingBoxCriterion.BulgeMaxX: return FindPinchOrBulgeX(positions, kv, leftSide: false, wantPinch: false);
            case BoundingBoxCriterion.BoneTransitionMinX:
            case BoundingBoxCriterion.BoneTransitionMaxX:
            {
                bool leftSide = criterion == BoundingBoxCriterion.BoneTransitionMinX;
                // Skinning data is required for the algorithm to mean anything. Without it
                // (unskinned shape, host that doesn't surface a boneLookup), degrade to plain
                // axis-extreme so the row still resolves — same fallback shape as the unpaired
                // degradation path for Pinch/Bulge pairs.
                if (boneIndices == null || boneWeights == null)
                {
                    return FindAxisExtremum(positions, kv, axis: 0, wantMax: !leftSide);
                }
                return FindBoneTransitionX(positions, boneIndices, boneWeights, kv, leftSide);
            }
            case BoundingBoxCriterion.BoneTransitionPairMinX:
            case BoundingBoxCriterion.BoneTransitionPairMaxX:
            {
                bool leftSide = criterion == BoundingBoxCriterion.BoneTransitionPairMinX;
                if (boneIndices == null || boneWeights == null)
                {
                    return FindAxisExtremum(positions, kv, axis: 0, wantMax: !leftSide);
                }
                var sibling = findSibling?.Invoke(kv);
                if (sibling != null)
                {
                    return FindPairedBoneTransitionX(positions, boneIndices, boneWeights, kv, leftSide);
                }
                // No sibling — degrade to the non-paired equivalent so the row still resolves.
                return FindBoneTransitionX(positions, boneIndices, boneWeights, kv, leftSide);
            }
            case BoundingBoxCriterion.PinchPairMinX:
            case BoundingBoxCriterion.PinchPairMaxX:
            case BoundingBoxCriterion.BulgePairMinX:
            case BoundingBoxCriterion.BulgePairMaxX:
            {
                var sibling = findSibling?.Invoke(kv);
                if (sibling != null)
                {
                    return FindPairedPinchOrBulgeX(positions, kv, leftSide: IsPairLeftSide(criterion), wantPinch: IsPairPinch(criterion));
                }
                // No sibling — degrade to the non-paired equivalent so the row still resolves.
                return FindPinchOrBulgeX(positions, kv, leftSide: IsPairLeftSide(criterion), wantPinch: IsPairPinch(criterion));
            }
            case BoundingBoxCriterion.MinYLeftOfX:  return FindExtremumOnXSide(positions, kv, leftSide: true,  wantMax: false, useY: true);
            case BoundingBoxCriterion.MinYRightOfX: return FindExtremumOnXSide(positions, kv, leftSide: false, wantMax: false, useY: true);
            case BoundingBoxCriterion.MaxYLeftOfX:  return FindExtremumOnXSide(positions, kv, leftSide: true,  wantMax: true,  useY: true);
            case BoundingBoxCriterion.MaxYRightOfX: return FindExtremumOnXSide(positions, kv, leftSide: false, wantMax: true,  useY: true);
            case BoundingBoxCriterion.MinZLeftOfX:  return FindExtremumOnXSide(positions, kv, leftSide: true,  wantMax: false, useY: false);
            case BoundingBoxCriterion.MinZRightOfX: return FindExtremumOnXSide(positions, kv, leftSide: false, wantMax: false, useY: false);
            case BoundingBoxCriterion.MaxZLeftOfX:  return FindExtremumOnXSide(positions, kv, leftSide: true,  wantMax: true,  useY: false);
            case BoundingBoxCriterion.MaxZRightOfX: return FindExtremumOnXSide(positions, kv, leftSide: false, wantMax: true,  useY: false);
            case BoundingBoxCriterion.MaxXAtCenter: return FindClosestToBoxFaceCenter(positions, kv, axis: 0, wantMax: true);
            case BoundingBoxCriterion.MinXAtCenter: return FindClosestToBoxFaceCenter(positions, kv, axis: 0, wantMax: false);
            case BoundingBoxCriterion.MaxYAtCenter: return FindClosestToBoxFaceCenter(positions, kv, axis: 1, wantMax: true);
            case BoundingBoxCriterion.MinYAtCenter: return FindClosestToBoxFaceCenter(positions, kv, axis: 1, wantMax: false);
            case BoundingBoxCriterion.MaxZAtCenter: return FindClosestToBoxFaceCenter(positions, kv, axis: 2, wantMax: true);
            case BoundingBoxCriterion.MinZAtCenter: return FindClosestToBoxFaceCenter(positions, kv, axis: 2, wantMax: false);
        }

        float minX = kv.BoxMinX, minY = kv.BoxMinY, minZ = kv.BoxMinZ;
        float maxX = kv.BoxMaxX, maxY = kv.BoxMaxY, maxZ = kv.BoxMaxZ;

        bool wantMax = criterion == BoundingBoxCriterion.MaxX
                    || criterion == BoundingBoxCriterion.MaxY
                    || criterion == BoundingBoxCriterion.MaxZ;

        int bestIdx = -1;
        float bestVal = wantMax ? float.MinValue : float.MaxValue;

        for (int i = 0; i < positions.Length; i++)
        {
            var p = positions[i];
            if (p.X < minX || p.X > maxX) continue;
            if (p.Y < minY || p.Y > maxY) continue;
            if (p.Z < minZ || p.Z > maxZ) continue;

            float val = criterion switch
            {
                BoundingBoxCriterion.MaxX or BoundingBoxCriterion.MinX => p.X,
                BoundingBoxCriterion.MaxY or BoundingBoxCriterion.MinY => p.Y,
                _ => p.Z,
            };

            bool isBest = wantMax ? val > bestVal : val < bestVal;
            if (isBest) { bestVal = val; bestIdx = i; }
        }

        return bestIdx >= 0 ? bestIdx : null;
    }

    /// <summary>Slice the AABB's Y range into <c>BinCount</c> equal bands; per band, record the
    /// silhouette vertex on the chosen side (smallest X for left, largest X for right) — that vertex
    /// is by definition the outer surface at that Y-level. Across bands, return the one whose
    /// recorded X is closest to the midline (<paramref name="wantPinch"/>=true) or farthest from it
    /// (<paramref name="wantPinch"/>=false). Suits waist-pinch (<c>PinchMin/MaxX</c>) and widest-hip
    /// (<c>BulgeMin/MaxX</c>) anchors. 20 bins balances resolution vs. noise for typical box sizes.</summary>
    private static int? FindPinchOrBulgeX(OpenTK.Mathematics.Vector3[] positions, NamedKeyVertex kv, bool leftSide, bool wantPinch)
    {
        const int BinCount = 20;

        float minX = kv.BoxMinX, maxX = kv.BoxMaxX;
        float minY = kv.BoxMinY, maxY = kv.BoxMaxY;
        float minZ = kv.BoxMinZ, maxZ = kv.BoxMaxZ;

        float yRange = maxY - minY;
        if (yRange <= 1e-6f) return null;

        var bestIdxPerBin = new int[BinCount];
        var bestValPerBin = new float[BinCount];
        for (int i = 0; i < BinCount; i++)
        {
            bestIdxPerBin[i] = -1;
            bestValPerBin[i] = leftSide ? float.MaxValue : float.MinValue;
        }

        for (int i = 0; i < positions.Length; i++)
        {
            var p = positions[i];
            if (p.X < minX || p.X > maxX) continue;
            if (p.Y < minY || p.Y > maxY) continue;
            if (p.Z < minZ || p.Z > maxZ) continue;

            int bin = (int)((p.Y - minY) / yRange * BinCount);
            if (bin < 0) bin = 0;
            else if (bin >= BinCount) bin = BinCount - 1;

            if (leftSide)
            {
                if (p.X < bestValPerBin[bin]) { bestValPerBin[bin] = p.X; bestIdxPerBin[bin] = i; }
            }
            else
            {
                if (p.X > bestValPerBin[bin]) { bestValPerBin[bin] = p.X; bestIdxPerBin[bin] = i; }
            }
        }

        int winnerBin = -1;
        // Initial bound is set so any real per-bin extremum "wins" on the first comparison.
        float chosenVal = wantPinch
            ? (leftSide ? float.MinValue : float.MaxValue)  // pinch-left wants LARGEST MinX; pinch-right wants SMALLEST MaxX
            : (leftSide ? float.MaxValue : float.MinValue); // bulge-left wants SMALLEST MinX; bulge-right wants LARGEST MaxX

        for (int b = 0; b < BinCount; b++)
        {
            if (bestIdxPerBin[b] < 0) continue;
            float v = bestValPerBin[b];
            bool isBest = wantPinch
                ? (leftSide ? v > chosenVal : v < chosenVal)
                : (leftSide ? v < chosenVal : v > chosenVal);
            if (isBest) { chosenVal = v; winnerBin = b; }
        }

        if (winnerBin < 0) return null;

        // Parabolic sub-bin refinement. Bin-discretization can place the true silhouette
        // extremum between two bins — the stored best vertex for the winning bin then sits
        // a fraction of a bin off the real pinch/bulge. Fitting a quadratic through the
        // winner and its two immediate neighbors (equally spaced) yields a closed-form
        // offset in bin units, which we use to shift the Y search band. We then rescan
        // positions within ±half-a-bin of the refined Y and return the silhouette vertex
        // in that narrow band, which can legitimately differ from the raw winner when
        // the band straddles a bin boundary.
        float binHeight = yRange / BinCount;
        float centerY = minY + (winnerBin + 0.5f) * binHeight;
        float refinedY = centerY;

        // Only attempt refinement when both immediate neighbors are occupied. Empty
        // neighbors (edge bins, sparse coverage) fall back to the unrefined winner center.
        if (winnerBin > 0 && winnerBin < BinCount - 1
            && bestIdxPerBin[winnerBin - 1] >= 0 && bestIdxPerBin[winnerBin + 1] >= 0)
        {
            float y0 = bestValPerBin[winnerBin - 1];
            float y1 = bestValPerBin[winnerBin];
            float y2 = bestValPerBin[winnerBin + 1];
            float denom = y0 - 2f * y1 + y2;
            if (MathF.Abs(denom) > 1e-6f)
            {
                // Standard discrete-parabola vertex offset, in bin widths.
                float offsetBins = 0.5f * (y0 - y2) / denom;
                // Clamp to ±half a bin so a near-flat fit can't extrapolate out of the
                // 3-bin window the fit was made over.
                if (offsetBins > 0.5f) offsetBins = 0.5f;
                else if (offsetBins < -0.5f) offsetBins = -0.5f;
                refinedY = centerY + offsetBins * binHeight;
            }
        }

        // Rescan for the silhouette vertex within a 1-bin-wide Y band centered on the
        // refined Y. The band spans two adjacent bins when refinedY is shifted, so the
        // chosen vertex can come from either the winner bin or its neighbor — which is
        // exactly the point of the refinement. Falls back to the winner bin's stored
        // best if the band happens to be empty (shouldn't occur given construction).
        float bandHalf = binHeight * 0.5f;
        float bandMinY = refinedY - bandHalf;
        float bandMaxY = refinedY + bandHalf;

        int chosenIdx = bestIdxPerBin[winnerBin];
        float chosenX = leftSide ? float.MaxValue : float.MinValue;
        for (int i = 0; i < positions.Length; i++)
        {
            var p = positions[i];
            if (p.X < minX || p.X > maxX) continue;
            if (p.Y < bandMinY || p.Y > bandMaxY) continue;
            if (p.Z < minZ || p.Z > maxZ) continue;

            if (leftSide)
            {
                if (p.X < chosenX) { chosenX = p.X; chosenIdx = i; }
            }
            else
            {
                if (p.X > chosenX) { chosenX = p.X; chosenIdx = i; }
            }
        }

        return chosenIdx;
    }

    /// <summary>Joint-scan variant of <see cref="FindPinchOrBulgeX"/> used by the paired criteria.
    /// Collects both the leftmost and rightmost silhouette vertex per Y-bin, then picks the single bin
    /// that minimizes (pinch) or maximizes (bulge) <c>maxX − minX</c>. Returns the left or right index
    /// of that winning bin depending on <paramref name="leftSide"/>. Because both the left and right
    /// row call into this method, they naturally return indices from the same bin — callers get a
    /// horizontally-aligned pair without having to communicate. Includes parabolic sub-bin refinement
    /// on the width-vs-bin curve, matching the single-side method's behavior.</summary>
    /// <summary>Debug-overlay snapshot of one Y-bin used by <see cref="FindPairedPinchOrBulgeX"/>.
    /// Exposes both side-vertex indices and the resulting paired width so a viewer overlay can
    /// draw one line per slice and highlight the winner. <c>HasMin</c>/<c>HasMax</c> distinguish
    /// "no vertex found on this side" (sparse bin) from a valid pick.</summary>
    public struct BulgeBinSnapshot
    {
        public int BinIndex;
        public float BinYCenter;
        public int MinVertexIndex;
        public int MaxVertexIndex;
        public float MinX;
        public float MaxX;
        public bool HasMin;
        public bool HasMax;
        public bool IsWinner;
        public float Width;   // maxX - minX; only meaningful when HasMin && HasMax
    }

    /// <summary>Public read-only inspection of the per-Y-bin pairing used internally by
    /// <see cref="FindPairedPinchOrBulgeX"/>. Identical binning (20 bands) and identical box
    /// filtering, so the returned <see cref="BulgeBinSnapshot.IsWinner"/> matches what
    /// <see cref="FindBestInBox"/> would select for the corresponding <c>BulgePair*X</c> /
    /// <c>PinchPair*X</c> criterion. Exposed for editor overlays — the marker resolution path
    /// continues to use the private routine. Returns null when the box is degenerate or no
    /// vertices fall inside it.</summary>
    public static BulgeBinSnapshot[]? GetPairXBinSnapshot(OpenTK.Mathematics.Vector3[] positions, NamedKeyVertex kv, bool wantPinch)
    {
        if (positions == null || positions.Length == 0) return null;
        const int BinCount = 20;
        float minX = kv.BoxMinX, maxX = kv.BoxMaxX;
        float minY = kv.BoxMinY, maxY = kv.BoxMaxY;
        float minZ = kv.BoxMinZ, maxZ = kv.BoxMaxZ;
        float yRange = maxY - minY;
        if (yRange <= 1e-6f) return null;

        var minIdxPerBin = new int[BinCount];
        var maxIdxPerBin = new int[BinCount];
        var minXPerBin = new float[BinCount];
        var maxXPerBin = new float[BinCount];
        for (int i = 0; i < BinCount; i++)
        {
            minIdxPerBin[i] = -1;
            maxIdxPerBin[i] = -1;
            minXPerBin[i] = float.MaxValue;
            maxXPerBin[i] = float.MinValue;
        }

        for (int i = 0; i < positions.Length; i++)
        {
            var p = positions[i];
            if (p.X < minX || p.X > maxX) continue;
            if (p.Y < minY || p.Y > maxY) continue;
            if (p.Z < minZ || p.Z > maxZ) continue;
            int bin = (int)((p.Y - minY) / yRange * BinCount);
            if (bin < 0) bin = 0;
            else if (bin >= BinCount) bin = BinCount - 1;
            if (p.X < minXPerBin[bin]) { minXPerBin[bin] = p.X; minIdxPerBin[bin] = i; }
            if (p.X > maxXPerBin[bin]) { maxXPerBin[bin] = p.X; maxIdxPerBin[bin] = i; }
        }

        int winnerBin = -1;
        float chosenWidth = wantPinch ? float.MaxValue : float.MinValue;
        for (int b = 0; b < BinCount; b++)
        {
            if (minIdxPerBin[b] < 0 || maxIdxPerBin[b] < 0) continue;
            float width = maxXPerBin[b] - minXPerBin[b];
            bool isBest = wantPinch ? width < chosenWidth : width > chosenWidth;
            if (isBest) { chosenWidth = width; winnerBin = b; }
        }

        float binHeight = yRange / BinCount;
        var result = new BulgeBinSnapshot[BinCount];
        for (int b = 0; b < BinCount; b++)
        {
            bool hasMin = minIdxPerBin[b] >= 0;
            bool hasMax = maxIdxPerBin[b] >= 0;
            result[b] = new BulgeBinSnapshot
            {
                BinIndex = b,
                BinYCenter = minY + (b + 0.5f) * binHeight,
                MinVertexIndex = hasMin ? minIdxPerBin[b] : -1,
                MaxVertexIndex = hasMax ? maxIdxPerBin[b] : -1,
                MinX = hasMin ? minXPerBin[b] : 0f,
                MaxX = hasMax ? maxXPerBin[b] : 0f,
                HasMin = hasMin,
                HasMax = hasMax,
                IsWinner = b == winnerBin,
                Width = (hasMin && hasMax) ? (maxXPerBin[b] - minXPerBin[b]) : 0f,
            };
        }
        return result;
    }

    private static int? FindPairedPinchOrBulgeX(OpenTK.Mathematics.Vector3[] positions, NamedKeyVertex kv, bool leftSide, bool wantPinch)
    {
        const int BinCount = 20;

        float minX = kv.BoxMinX, maxX = kv.BoxMaxX;
        float minY = kv.BoxMinY, maxY = kv.BoxMaxY;
        float minZ = kv.BoxMinZ, maxZ = kv.BoxMaxZ;

        float yRange = maxY - minY;
        if (yRange <= 1e-6f) return null;

        var minIdxPerBin = new int[BinCount];
        var maxIdxPerBin = new int[BinCount];
        var minXPerBin = new float[BinCount];
        var maxXPerBin = new float[BinCount];
        for (int i = 0; i < BinCount; i++)
        {
            minIdxPerBin[i] = -1;
            maxIdxPerBin[i] = -1;
            minXPerBin[i] = float.MaxValue;
            maxXPerBin[i] = float.MinValue;
        }

        for (int i = 0; i < positions.Length; i++)
        {
            var p = positions[i];
            if (p.X < minX || p.X > maxX) continue;
            if (p.Y < minY || p.Y > maxY) continue;
            if (p.Z < minZ || p.Z > maxZ) continue;

            int bin = (int)((p.Y - minY) / yRange * BinCount);
            if (bin < 0) bin = 0;
            else if (bin >= BinCount) bin = BinCount - 1;

            if (p.X < minXPerBin[bin]) { minXPerBin[bin] = p.X; minIdxPerBin[bin] = i; }
            if (p.X > maxXPerBin[bin]) { maxXPerBin[bin] = p.X; maxIdxPerBin[bin] = i; }
        }

        // Per-bin width. Only bins with both sides occupied are candidates — a half-populated bin
        // has no meaningful thickness.
        int winnerBin = -1;
        float chosenWidth = wantPinch ? float.MaxValue : float.MinValue;
        for (int b = 0; b < BinCount; b++)
        {
            if (minIdxPerBin[b] < 0 || maxIdxPerBin[b] < 0) continue;
            float width = maxXPerBin[b] - minXPerBin[b];
            bool isBest = wantPinch ? width < chosenWidth : width > chosenWidth;
            if (isBest) { chosenWidth = width; winnerBin = b; }
        }

        if (winnerBin < 0) return null;

        // Return the winning bin's stored optima directly. An earlier version of this
        // function did a parabolic-refinement-on-widths pass followed by a band rescan
        // within ±0.5 bin of the refined Y, but the rescan ran an independent smallest-X
        // search over that Y window — which can land on a different vertex than
        // minIdxPerBin[winnerBin] when the inner edge has many near-co-X silhouette
        // vertices spread across Z (i.e. virtually every body mesh in the dataset). The
        // resolver's pick then visibly drifts in Z away from the bin's actual stored
        // min/max — confirmed by the BulgeBin debug overlay. Skipping the rescan keeps
        // the resolver aligned with what the overlay (and the snapshot helper used by
        // it) shows. The sub-bin precision loss is bounded by binHeight (yRange / 20),
        // typically 0.5-0.7 model units on a thigh box, which is below the noise floor
        // of subsequent measurement ratios.
        return leftSide ? minIdxPerBin[winnerBin] : maxIdxPerBin[winnerBin];
    }

    /// <summary>True for the six <c>*Pair*X</c> criteria that require joint sibling resolution
    /// (four Pinch/Bulge family + two BoneTransition family).</summary>
    public static bool IsPairCriterion(BoundingBoxCriterion criterion)
        => criterion == BoundingBoxCriterion.PinchPairMinX
        || criterion == BoundingBoxCriterion.PinchPairMaxX
        || criterion == BoundingBoxCriterion.BulgePairMinX
        || criterion == BoundingBoxCriterion.BulgePairMaxX
        || criterion == BoundingBoxCriterion.BoneTransitionPairMinX
        || criterion == BoundingBoxCriterion.BoneTransitionPairMaxX;

    /// <summary>True for the four criteria that need per-vertex skin weights to evaluate
    /// (two single-side + two paired). Gating <c>boneLookup</c> on this in <see cref="TryResolve"/>
    /// keeps the unrelated resolution paths from paying for a weight fetch they won't use.</summary>
    public static bool IsBoneTransitionCriterion(BoundingBoxCriterion criterion)
        => criterion == BoundingBoxCriterion.BoneTransitionMinX
        || criterion == BoundingBoxCriterion.BoneTransitionMaxX
        || criterion == BoundingBoxCriterion.BoneTransitionPairMinX
        || criterion == BoundingBoxCriterion.BoneTransitionPairMaxX;

    /// <summary>Returns the opposite-side partner of a paired criterion (Min ↔ Max within the same
    /// Pinch/Bulge/BoneTransition family). Throws for non-paired inputs since callers must gate on
    /// <see cref="IsPairCriterion"/>.</summary>
    public static BoundingBoxCriterion PartnerCriterion(BoundingBoxCriterion criterion) => criterion switch
    {
        BoundingBoxCriterion.PinchPairMinX => BoundingBoxCriterion.PinchPairMaxX,
        BoundingBoxCriterion.PinchPairMaxX => BoundingBoxCriterion.PinchPairMinX,
        BoundingBoxCriterion.BulgePairMinX => BoundingBoxCriterion.BulgePairMaxX,
        BoundingBoxCriterion.BulgePairMaxX => BoundingBoxCriterion.BulgePairMinX,
        BoundingBoxCriterion.BoneTransitionPairMinX => BoundingBoxCriterion.BoneTransitionPairMaxX,
        BoundingBoxCriterion.BoneTransitionPairMaxX => BoundingBoxCriterion.BoneTransitionPairMinX,
        _ => throw new ArgumentException($"Not a pair criterion: {criterion}", nameof(criterion)),
    };

    private static bool IsPairLeftSide(BoundingBoxCriterion criterion)
        => criterion == BoundingBoxCriterion.PinchPairMinX
        || criterion == BoundingBoxCriterion.BulgePairMinX
        || criterion == BoundingBoxCriterion.BoneTransitionPairMinX;

    private static bool IsPairPinch(BoundingBoxCriterion criterion)
        => criterion == BoundingBoxCriterion.PinchPairMinX
        || criterion == BoundingBoxCriterion.PinchPairMaxX;

    /// <summary>Scan vertices inside the AABB filtered to one side of the X=0 midline and return the
    /// index whose Y (when <paramref name="useY"/>) or Z (otherwise) is most extreme.
    /// <paramref name="leftSide"/> selects X&lt;0 (true) vs. X&gt;=0 (false);
    /// <paramref name="wantMax"/> selects the largest value (true) vs. smallest (false). Returns null
    /// when no vertex on the requested side falls inside the box. Used by the <c>MinY/MaxY/MinZ/MaxZ*OfX</c>
    /// criteria to author paired top/bottom or front/back landmarks where each side of the body
    /// contributes one anchor (e.g. lowest point of each foot, front-most point of each breast).</summary>
    private static int? FindExtremumOnXSide(OpenTK.Mathematics.Vector3[] positions, NamedKeyVertex kv, bool leftSide, bool wantMax, bool useY)
    {
        float minX = kv.BoxMinX, minY = kv.BoxMinY, minZ = kv.BoxMinZ;
        float maxX = kv.BoxMaxX, maxY = kv.BoxMaxY, maxZ = kv.BoxMaxZ;

        int bestIdx = -1;
        float bestVal = wantMax ? float.MinValue : float.MaxValue;

        for (int i = 0; i < positions.Length; i++)
        {
            var p = positions[i];
            if (p.X < minX || p.X > maxX) continue;
            if (p.Y < minY || p.Y > maxY) continue;
            if (p.Z < minZ || p.Z > maxZ) continue;
            // Midline filter: left = strictly negative X, right = zero-or-positive X. The asymmetry
            // around 0 is intentional — a vertex exactly on the midline contributes to the right
            // side only, so the two paired rows partition the box without overlap.
            if (leftSide ? !(p.X < 0f) : !(p.X >= 0f)) continue;

            float v = useY ? p.Y : p.Z;
            bool isBest = wantMax ? v > bestVal : v < bestVal;
            if (isBest) { bestVal = v; bestIdx = i; }
        }

        return bestIdx >= 0 ? bestIdx : null;
    }

    /// <summary>Picks the primary-axis extremum vertex from inside a "central tube" along
    /// that axis — vertices that lie within a tolerance of the box midpoints on the two
    /// perpendicular axes. Implements the user's mental model of <c>Min/Max{X,Y,Z}AtCenter</c>:
    /// "the back-most (or top-most, etc.) vertex on the centerline".
    /// <para>The tolerance is set by a progressively-widened schedule of perpendicular
    /// fractions: 15% of each perpendicular half-extent first, then 30%, 50%, and finally
    /// 100% (the entire box). The loop accepts the tightest tube whose candidate count is
    /// ≥ 2, because the criterion is meaningful only when there are multiple near-center
    /// vertices to extremize over — a tube with one candidate returns the same vertex for
    /// MinZAtCenter and MaxZAtCenter, leaving paired PointDistance measurements at zero.
    /// The 100% step caps the loop for boxes that only intersect one side of the mesh; the
    /// algorithm then returns whatever single-vertex result that full-box pass produced
    /// (or null if the box is empty), so a poorly-drawn box degrades to a stable best-effort
    /// pick instead of an infinite loop or a silent null.</para>
    /// <para>This formulation replaces an earlier single-objective "Euclidean distance to
    /// face-center" pick that, on Z-elongated boxes, let a slightly-off-face but on-center
    /// vertex lose to a slightly-off-center vertex that was deeper on Z — the primary
    /// axis's magnitude dominated the distance. The tube formulation makes centering a
    /// hard constraint instead of a soft weight, which matches "AtCenter" semantically.</para></summary>
    private static int? FindClosestToBoxFaceCenter(OpenTK.Mathematics.Vector3[] positions, NamedKeyVertex kv, int axis, bool wantMax)
    {
        float cx = (kv.BoxMinX + kv.BoxMaxX) * 0.5f;
        float cy = (kv.BoxMinY + kv.BoxMaxY) * 0.5f;
        float cz = (kv.BoxMinZ + kv.BoxMaxZ) * 0.5f;

        // Schedule of perpendicular-tube fractions, applied to each perpendicular
        // half-extent. 0.15 is the "AtCenter" intent at full strength — tight enough that
        // a well-tessellated mesh resolves the criterion to a vertex clearly on the
        // centerline. 0.30 and 0.50 cover sparse-mesh cases (special-meso w=50 bicep was
        // the canonical example: one vertex landed in the 15% tube, so MinZAtCenter and
        // MaxZAtCenter both returned it and arm_thickness collapsed to zero). 1.00 is the
        // full half-extent — the tube becomes the entire box — and acts as the loop's
        // upper bound for one-sided-intersection boxes that can never reach 2 candidates.
        var fractions = new[] { 0.15f, 0.30f, 0.50f, 1.0f };

        int bestIdx = -1;

        foreach (var fraction in fractions)
        {
            float xTol = (kv.BoxMaxX - kv.BoxMinX) * 0.5f * fraction;
            float yTol = (kv.BoxMaxY - kv.BoxMinY) * 0.5f * fraction;
            float zTol = (kv.BoxMaxZ - kv.BoxMinZ) * 0.5f * fraction;

            int passBestIdx = -1;
            float passBestPrimary = wantMax ? float.MinValue : float.MaxValue;
            int candidateCount = 0;

            for (int i = 0; i < positions.Length; i++)
            {
                var p = positions[i];
                if (p.X < kv.BoxMinX || p.X > kv.BoxMaxX) continue;
                if (p.Y < kv.BoxMinY || p.Y > kv.BoxMaxY) continue;
                if (p.Z < kv.BoxMinZ || p.Z > kv.BoxMaxZ) continue;

                switch (axis)
                {
                    case 0: // X primary → Y/Z perpendicular
                        if (MathF.Abs(p.Y - cy) > yTol) continue;
                        if (MathF.Abs(p.Z - cz) > zTol) continue;
                        break;
                    case 1: // Y primary → X/Z perpendicular
                        if (MathF.Abs(p.X - cx) > xTol) continue;
                        if (MathF.Abs(p.Z - cz) > zTol) continue;
                        break;
                    default: // Z primary → X/Y perpendicular
                        if (MathF.Abs(p.X - cx) > xTol) continue;
                        if (MathF.Abs(p.Y - cy) > yTol) continue;
                        break;
                }

                candidateCount++;
                float primary = axis == 0 ? p.X : (axis == 1 ? p.Y : p.Z);
                bool isBest = wantMax ? primary > passBestPrimary : primary < passBestPrimary;
                if (isBest) { passBestPrimary = primary; passBestIdx = i; }
            }

            // Overwrite the outer bestIdx with this iteration's pick. Tube candidates are
            // monotone non-decreasing in `fraction`, so a wider tube either retains or
            // adds to the previous pass's candidate set, and the new extremum is at least
            // as informative as the old one. The final iteration (fraction=1.0) thus
            // always produces a valid pick when any vertex sits in the box at all.
            bestIdx = passBestIdx;

            if (candidateCount >= 2) break;
        }

        return bestIdx >= 0 ? bestIdx : null;
    }

    /// <summary>Single-axis extremum inside the AABB. Lifted out of the default-case loop in
    /// <see cref="FindBestInBox"/> so the bone-transition fallback can call it directly without
    /// re-entering the public dispatch.</summary>
    private static int? FindAxisExtremum(OpenTK.Mathematics.Vector3[] positions, NamedKeyVertex kv, int axis, bool wantMax)
    {
        float minX = kv.BoxMinX, minY = kv.BoxMinY, minZ = kv.BoxMinZ;
        float maxX = kv.BoxMaxX, maxY = kv.BoxMaxY, maxZ = kv.BoxMaxZ;

        int bestIdx = -1;
        float bestVal = wantMax ? float.MinValue : float.MaxValue;

        for (int i = 0; i < positions.Length; i++)
        {
            var p = positions[i];
            if (p.X < minX || p.X > maxX) continue;
            if (p.Y < minY || p.Y > maxY) continue;
            if (p.Z < minZ || p.Z > maxZ) continue;

            float val = axis == 0 ? p.X : (axis == 1 ? p.Y : p.Z);
            bool isBest = wantMax ? val > bestVal : val < bestVal;
            if (isBest) { bestVal = val; bestIdx = i; }
        }
        return bestIdx >= 0 ? bestIdx : null;
    }

    /// <summary>Bone-transition search. Anchors on the vertex closest to the AABB center, reads
    /// its dominant bone, then walks vertices on the chosen side of the box's X-center outward
    /// in X order. Returns the last vertex whose dominant bone matches the anchor's — the one
    /// immediately inboard of the first bone change. Use cases are anatomical seams: underarm
    /// (torso↔arm), wrist (forearm↔hand), neck base (spine↔head), etc. Configuration is
    /// implicit in box placement (the box center defines the root bone); no name lists needed.
    /// <para>"Dominant bone" is the bone with the highest weight among the (up to) 4 entries
    /// for that vertex — i.e. majority-flip detection. A vertex with weights {Spine2: 0.55,
    /// UpperArm: 0.45} is still classified Spine2; the transition triggers only after the
    /// UpperArm weight wins outright. Simpler than an epsilon-based "any non-root weight"
    /// rule and matches the user's wording. May be softened later if calibration shows it
    /// picks too far inboard.</para>
    /// <para>Trusts the user's box: no Y-slabbing inside the algorithm. If the box covers
    /// multiple Y heights with different bone transitions (e.g. armpit + shoulder), the
    /// outward X-walk may pick whichever transition has the smaller |X| — the user controls
    /// this by drawing a vertically tight box around the anatomical region they're after.</para></summary>
    private static int? FindBoneTransitionX(OpenTK.Mathematics.Vector3[] positions, int[] boneIndices, float[] boneWeights, NamedKeyVertex kv, bool leftSide)
    {
        float minX = kv.BoxMinX, minY = kv.BoxMinY, minZ = kv.BoxMinZ;
        float maxX = kv.BoxMaxX, maxY = kv.BoxMaxY, maxZ = kv.BoxMaxZ;
        float cx = (minX + maxX) * 0.5f;
        float cy = (minY + maxY) * 0.5f;
        float cz = (minZ + maxZ) * 0.5f;

        // Sanity-check the weight buffer matches the position count — a host that
        // accidentally hands in mismatched arrays would otherwise index out of bounds.
        if (boneIndices.Length < positions.Length * 4 || boneWeights.Length < positions.Length * 4)
        {
            return FindAxisExtremum(positions, kv, axis: 0, wantMax: !leftSide);
        }

        // Single pass to find both (a) the anchor vertex (closest to box center, any side)
        // and (b) the per-side candidate list (vertices on the requested side of cx, sorted by
        // distance-from-center along X). Collecting indices into a List + sorting at the end
        // is O(N log K) where K = box vertex count — typically a few hundred at most, so the
        // overhead is negligible relative to the per-vertex inside-box test.
        int anchorIdx = -1;
        float anchorDistSq = float.MaxValue;
        var sideCandidates = new List<int>();

        for (int i = 0; i < positions.Length; i++)
        {
            var p = positions[i];
            if (p.X < minX || p.X > maxX) continue;
            if (p.Y < minY || p.Y > maxY) continue;
            if (p.Z < minZ || p.Z > maxZ) continue;

            float dx = p.X - cx, dy = p.Y - cy, dz = p.Z - cz;
            float distSq = dx * dx + dy * dy + dz * dz;
            if (distSq < anchorDistSq)
            {
                anchorDistSq = distSq;
                anchorIdx = i;
            }

            // Strict inequality so the anchor isn't included as a "side" candidate
            // when it happens to sit exactly on cx (rare but happens for symmetric boxes).
            if (leftSide ? p.X < cx : p.X > cx) sideCandidates.Add(i);
        }

        if (anchorIdx < 0) return null;
        int rootBone = GetDominantBone(anchorIdx, boneIndices, boneWeights);
        if (rootBone < 0)
        {
            // Anchor has no weight at all — degenerate skinning data. Fall back to axis
            // extremum rather than returning null, so the row at least picks SOMETHING.
            return FindAxisExtremum(positions, kv, axis: 0, wantMax: !leftSide);
        }

        // Sort by distance from the center going outward: for the left side that's
        // descending X (start at largest X < cx, walk toward minX); for the right side
        // that's ascending X (start at smallest X > cx, walk toward maxX).
        if (leftSide)
            sideCandidates.Sort((a, b) => positions[b].X.CompareTo(positions[a].X));
        else
            sideCandidates.Sort((a, b) => positions[a].X.CompareTo(positions[b].X));

        // Walk outward. Track the last index whose dominant bone still matches the root —
        // that's the vertex just inboard of the transition, which is anatomically the seam
        // point. If we never hit a transition (the whole side is one bone), return the most
        // extreme same-bone vertex we saw, i.e. the axis-extremum of the root region.
        int lastRootVertex = anchorIdx;
        foreach (var i in sideCandidates)
        {
            int bone = GetDominantBone(i, boneIndices, boneWeights);
            if (bone < 0) continue; // unweighted vertex — treat as ambiguous, skip
            if (bone != rootBone) return lastRootVertex;
            lastRootVertex = i;
        }
        return lastRootVertex;
    }

    /// <summary>Paired variant of <see cref="FindBoneTransitionX"/>. Joint-Y synchronization for
    /// Mirror-authored underarm-style measurements: each side independently locates its
    /// bone-transition pick, then both sides are snapped onto the average of the two picks' Y
    /// coordinates, so a PointDistance between them measures honest seam-to-seam X-distance
    /// instead of a diagonal across slightly different heights.
    /// <para>Algorithm: (1) run the single-side <see cref="FindBoneTransitionX"/> twice to get
    /// independent left/right picks; (2) compute <c>avgY = (L.Y + R.Y) / 2</c>; (3) on the
    /// requested side, return the most-lateral root-bone vertex within a tight Y-band around
    /// <c>avgY</c>. Y-tolerance is 5% of the box's Y range with a floor of 0.2 NIF units so
    /// even narrow boxes have a real Y window. Falls back to the single-side pick when no
    /// root-bone vertex sits within the Y-band on the requested side (degenerate, but better
    /// than returning null and breaking the dependent measurement).</para></summary>
    private static int? FindPairedBoneTransitionX(OpenTK.Mathematics.Vector3[] positions, int[] boneIndices, float[] boneWeights, NamedKeyVertex kv, bool leftSide)
    {
        // Step 1: get each side's unpaired pick.
        int? leftPick = FindBoneTransitionX(positions, boneIndices, boneWeights, kv, leftSide: true);
        int? rightPick = FindBoneTransitionX(positions, boneIndices, boneWeights, kv, leftSide: false);
        if (leftPick == null && rightPick == null) return null;
        if (leftPick == null) return leftSide ? null : rightPick;
        if (rightPick == null) return leftSide ? leftPick : null;

        // Step 2: average Y of the two picks. This is the joint Y both sides will be snapped to.
        float avgY = (positions[leftPick.Value].Y + positions[rightPick.Value].Y) * 0.5f;

        // Step 3: identify the root bone (same anchor logic as FindBoneTransitionX). Could be
        // cached out of the sub-calls, but keeping the two passes independent keeps the
        // single-side algorithm self-contained.
        float cx = (kv.BoxMinX + kv.BoxMaxX) * 0.5f;
        float cy = (kv.BoxMinY + kv.BoxMaxY) * 0.5f;
        float cz = (kv.BoxMinZ + kv.BoxMaxZ) * 0.5f;
        int anchorIdx = -1;
        float anchorDistSq = float.MaxValue;
        for (int i = 0; i < positions.Length; i++)
        {
            var p = positions[i];
            if (p.X < kv.BoxMinX || p.X > kv.BoxMaxX) continue;
            if (p.Y < kv.BoxMinY || p.Y > kv.BoxMaxY) continue;
            if (p.Z < kv.BoxMinZ || p.Z > kv.BoxMaxZ) continue;
            float dx = p.X - cx, dy = p.Y - cy, dz = p.Z - cz;
            float d2 = dx * dx + dy * dy + dz * dz;
            if (d2 < anchorDistSq) { anchorDistSq = d2; anchorIdx = i; }
        }
        if (anchorIdx < 0) return leftSide ? leftPick : rightPick;
        int rootBone = GetDominantBone(anchorIdx, boneIndices, boneWeights);
        if (rootBone < 0) return leftSide ? leftPick : rightPick;

        // Step 4: snap to most-lateral root-bone vertex within the joint Y-band on the
        // requested side. Floor on the tolerance protects narrow boxes — without it, a box
        // with a Y range of e.g. 8 NIF units would have a 0.4-unit window which can be tighter
        // than typical vertex spacing on the back surface and pick nothing.
        float yTolerance = MathF.Max(0.2f, (kv.BoxMaxY - kv.BoxMinY) * 0.05f);
        int bestIdx = -1;
        float bestX = leftSide ? float.MaxValue : float.MinValue;
        for (int i = 0; i < positions.Length; i++)
        {
            var p = positions[i];
            if (p.X < kv.BoxMinX || p.X > kv.BoxMaxX) continue;
            if (p.Y < kv.BoxMinY || p.Y > kv.BoxMaxY) continue;
            if (p.Z < kv.BoxMinZ || p.Z > kv.BoxMaxZ) continue;
            if (leftSide ? p.X >= cx : p.X <= cx) continue;
            if (MathF.Abs(p.Y - avgY) > yTolerance) continue;
            if (GetDominantBone(i, boneIndices, boneWeights) != rootBone) continue;

            bool isBest = leftSide ? p.X < bestX : p.X > bestX;
            if (isBest) { bestX = p.X; bestIdx = i; }
        }

        return bestIdx >= 0 ? bestIdx : (leftSide ? leftPick : rightPick);
    }

    /// <summary>Return the bone index with the highest weight among the (up to) four entries
    /// stored for <paramref name="vertexIndex"/>. Returns -1 when every weight is zero.
    /// The 4-per-vertex flat layout matches what NifMeshBuilder writes into SkinningInfo
    /// (and what GlMesh.CpuBoneIndices/CpuBoneWeights surface).</summary>
    private static int GetDominantBone(int vertexIndex, int[] boneIndices, float[] boneWeights)
    {
        int baseIdx = vertexIndex * 4;
        int bestBone = -1;
        float bestWeight = 0f;
        for (int k = 0; k < 4; k++)
        {
            float w = boneWeights[baseIdx + k];
            if (w > bestWeight)
            {
                bestWeight = w;
                bestBone = boneIndices[baseIdx + k];
            }
        }
        return bestBone;
    }

    /// <summary>Find the pair partner for <paramref name="kv"/> within <paramref name="candidates"/>.
    /// A sibling matches on ShapeName (case-insensitive), exact float equality on all six box
    /// coordinates, and carries the opposite-side pair criterion. Returns null when no match exists.
    /// Exact equality is intentional — pair rows are always authored together from a shared box, so
    /// any coordinate mismatch indicates a genuinely different selection, not float drift.</summary>
    public static NamedKeyVertex? FindPairSibling(NamedKeyVertex kv, IEnumerable<NamedKeyVertex> candidates)
    {
        if (kv == null || candidates == null) return null;
        if (!IsPairCriterion(kv.Criterion)) return null;
        var partner = PartnerCriterion(kv.Criterion);
        foreach (var other in candidates)
        {
            if (other == null || ReferenceEquals(other, kv)) continue;
            if (other.Strategy != KeyVertexStrategy.BoundingBox) continue;
            if (other.Criterion != partner) continue;
            if (!string.Equals(other.ShapeName, kv.ShapeName, StringComparison.OrdinalIgnoreCase)) continue;
            if (other.BoxMinX != kv.BoxMinX || other.BoxMaxX != kv.BoxMaxX) continue;
            if (other.BoxMinY != kv.BoxMinY || other.BoxMaxY != kv.BoxMaxY) continue;
            if (other.BoxMinZ != kv.BoxMinZ || other.BoxMaxZ != kv.BoxMaxZ) continue;
            return other;
        }
        return null;
    }

    /// <summary>Reduces a vector to a scalar: |X|, |Y|, or |Z| when an axis is given; full 3D length when null.
    /// Used by <see cref="MeasurementKind.RatioDistance"/> to honor per-pair NumeratorAxis / DenominatorAxis,
    /// so a ratio meant as "Z-projection over X-width" computes that instead of mixing axes.</summary>
    public static float AxisOrLength(OpenTK.Mathematics.Vector3 v, MeasurementAxis? axis) => axis switch
    {
        MeasurementAxis.X => Math.Abs(v.X),
        MeasurementAxis.Y => Math.Abs(v.Y),
        MeasurementAxis.Z => Math.Abs(v.Z),
        _ => v.Length,
    };

    /// <summary>Applies a comparator to a measurement value.</summary>
    public static bool Compare(float measurement, MeasurementComparator comparator, float threshold)
    {
        return comparator switch
        {
            MeasurementComparator.LessThan => measurement < threshold,
            MeasurementComparator.LessThanOrEqual => measurement <= threshold,
            MeasurementComparator.GreaterThan => measurement > threshold,
            MeasurementComparator.GreaterThanOrEqual => measurement >= threshold,
            MeasurementComparator.EqualTo => Math.Abs(measurement - threshold) < 1e-6f,
            MeasurementComparator.NotEqualTo => Math.Abs(measurement - threshold) >= 1e-6f,
            _ => false,
        };
    }

    /// <summary>
    /// True when the rule's predicate (DNF: OR of AND-gated condition groups) matches the given
    /// measurement values and (optionally) the running set of already-matched descriptors.
    /// An empty <see cref="MeasurementRule.GroupsORlogic"/> never matches.
    /// <para>
    /// <paramref name="matchedDescriptors"/> is the set of <c>(Category, Value)</c> pairs that
    /// earlier rules in the evaluation order have produced. It feeds
    /// <see cref="MeasurementConditionKind.DescriptorRef"/> conditions; pass null (or an empty
    /// set) for evaluation contexts that don't support aggregator rules (legacy callers, unit
    /// tests). Aggregator rules will simply fail to match in that case, which is the right
    /// behavior — we don't know what fired upstream.
    /// </para>
    /// </summary>
    public static bool RuleMatches(
        MeasurementRule rule,
        IReadOnlyDictionary<string, float> measurements,
        IReadOnlySet<(string Category, string Value)>? matchedDescriptors = null)
    {
        if (rule?.GroupsORlogic == null || rule.GroupsORlogic.Count == 0) return false;
        foreach (var group in rule.GroupsORlogic)
        {
            if (group?.ConditionsANDlogic == null || group.ConditionsANDlogic.Count == 0) continue;
            bool allMatch = true;
            foreach (var cond in group.ConditionsANDlogic)
            {
                if (cond == null) { allMatch = false; break; }
                if (!ConditionMatches(cond, measurements, matchedDescriptors)) { allMatch = false; break; }
            }
            if (allMatch) return true;
        }
        return false;
    }

    /// <summary>Evaluates a single <see cref="MeasurementCondition"/> against the supplied
    /// measurement values and matched-descriptor set. Branches on
    /// <see cref="MeasurementCondition.Kind"/>: <see cref="MeasurementConditionKind.Measurement"/>
    /// does the threshold compare; <see cref="MeasurementConditionKind.DescriptorRef"/> tests
    /// the matched-descriptor set membership, honoring <see cref="MeasurementCondition.Negate"/>.</summary>
    private static bool ConditionMatches(
        MeasurementCondition cond,
        IReadOnlyDictionary<string, float> measurements,
        IReadOnlySet<(string Category, string Value)>? matchedDescriptors)
    {
        switch (cond.Kind)
        {
            case MeasurementConditionKind.DescriptorRef:
                // An aggregator condition with a blank Category or Value is malformed — fail
                // closed (treat as not-matching) so a half-edited rule can't accidentally match
                // everything. The UI prevents this from being saved but keep the runtime
                // strict for hand-edited JSON.
                if (string.IsNullOrEmpty(cond.RefCategory) || string.IsNullOrEmpty(cond.RefValue))
                    return false;
                bool present = matchedDescriptors != null
                    && matchedDescriptors.Contains((cond.RefCategory, cond.RefValue));
                return cond.Negate ? !present : present;

            case MeasurementConditionKind.Measurement:
            default:
                if (string.IsNullOrEmpty(cond.MeasurementName)) return false;
                if (!measurements.TryGetValue(cond.MeasurementName, out float val)) return false;
                return Compare(val, cond.Comparator, cond.Value);
        }
    }
}
