using Newtonsoft.Json;
using System;
using System.Collections.Generic;
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
    /// Labeled training examples from the "Label-then-suggest" authoring mode. Persisted so
    /// the user can iteratively refine threshold suggestions over multiple sessions.
    /// Never consumed by the evaluator directly -- suggestions feed the manual rule editor.
    /// </summary>
    public List<LabeledExample> LabeledExamples { get; set; } = new();
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
/// Operates on mesh-local axes (NIF: X = left-right, Y = up-down, Z = front-back).
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
}

/// <summary>World-axis symmetry lock applied to a <see cref="KeyVertexStrategy.BoundingBox"/>
/// region while it is being authored in the viewer. Flag combinations encode the set of axes
/// mirrored about 0 (e.g., <c>X | Y</c> locks both X and Y sides). Authoring-time UX only —
/// never persisted on <see cref="NamedKeyVertex"/> (the final min/max are stored directly).</summary>
[Flags]
public enum SymmetryAxes
{
    None = 0,
    X = 1,
    Y = 2,
    Z = 4,
}

/// <summary>
/// Authoring-time-only companion to <see cref="BoundingBoxCriterion"/>. The UI combo exposes
/// the single-axis values plus <c>Mirror*</c> shortcuts; picking a <c>Mirror*</c>
/// value tells the editor to materialize two paired <see cref="NamedKeyVertex"/> rows sharing
/// the same box but with opposite single-axis criteria (e.g. <c>MirrorX</c> -> <c>MaxX</c> +
/// <c>MinX</c>, <c>MirrorPinchX</c> -> <c>PinchMinX</c> + <c>PinchMaxX</c>). Persistence stores
/// only the single-axis <see cref="BoundingBoxCriterion"/>; this enum never lands in JSON.
/// <para>Numeric values 0-9 intentionally match <see cref="BoundingBoxCriterion"/> so the editor
/// can plain-cast for non-mirror entries; mirror values are placed at 100+ to stay out of the way.</para>
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

/// <summary>
/// A single threshold test: <c>measurement [comparator] value</c>.
/// </summary>
[DebuggerDisplay("{MeasurementName} {Comparator} {Value}")]
public class MeasurementCondition
{
    public string MeasurementName { get; set; } = "";
    public MeasurementComparator Comparator { get; set; } = MeasurementComparator.GreaterThan;
    public float Value { get; set; } = 0f;
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

/// <summary>Polarity of a <see cref="LabeledExample"/> relative to its target descriptor.</summary>
public enum LabelPolarity
{
    /// <summary>Preset at this weight should produce the target descriptor.</summary>
    Positive = 0,

    /// <summary>Preset at this weight should NOT produce the target descriptor.</summary>
    Negative = 1,
}

/// <summary>
/// One training datum for the Label-then-suggest mode. Identifies a single
/// (descriptor, preset, weight) tuple that the user has tagged positive or negative.
/// </summary>
[DebuggerDisplay("{Descriptor.Category}:{Descriptor.Value} {Polarity} @ {PresetLabel}[{Weight}]")]
public class LabeledExample
{
    /// <summary>Descriptor this example is for.</summary>
    public BodyShapeDescriptor.LabelSignature Descriptor { get; set; } = new();

    /// <summary>
    /// BodySlide preset identifier. Uses the preset's <see cref="BodySlideSetting.Label"/> since
    /// that is the stable human-readable key the UI displays; if the label changes the example
    /// becomes an orphan that the suggest pass will skip.
    /// </summary>
    public string PresetLabel { get; set; } = "";

    /// <summary>Gender of the preset this example was captured from (presets are split into Male/Female lists).</summary>
    public Gender PresetGender { get; set; } = Gender.Female;

    /// <summary>Weight slot (0-100) at which the example was captured.</summary>
    public int Weight { get; set; } = 50;

    public LabelPolarity Polarity { get; set; } = LabelPolarity.Positive;
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

    /// <summary>
    /// Evaluates a measurement against a vertex lookup. Returns false when any required vertex
    /// is missing (orphaned reference, shape not loaded), denominator is near zero (ratio), or
    /// the definition is malformed (wrong vertex-ref count for its kind).
    /// <paramref name="shapeLookup"/> is only consulted for <see cref="KeyVertexStrategy.BoundingBox"/>
    /// entries; pass null when only Explicit vertices are in play.
    /// </summary>
    public static bool TryEvaluate(MeasurementDefinition def, IReadOnlyDictionary<string, NamedKeyVertex> keyVertsByName, VertexLookup lookup, out float value)
        => TryEvaluate(def, keyVertsByName, lookup, null, out value);

    public static bool TryEvaluate(MeasurementDefinition def, IReadOnlyDictionary<string, NamedKeyVertex> keyVertsByName, VertexLookup lookup, ShapePositionsLookup? shapeLookup, out float value)
    {
        value = 0f;
        if (def == null || def.VertexRefNames == null || lookup == null) return false;

        int needed = def.Kind == MeasurementKind.RatioDistance ? 4 : 2;
        if (def.VertexRefNames.Count < needed) return false;

        if (!TryResolve(def.VertexRefNames[0], keyVertsByName, lookup, shapeLookup, out var a)) return false;
        if (!TryResolve(def.VertexRefNames[1], keyVertsByName, lookup, shapeLookup, out var b)) return false;

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
                if (!TryResolve(def.VertexRefNames[2], keyVertsByName, lookup, shapeLookup, out var c)) return false;
                if (!TryResolve(def.VertexRefNames[3], keyVertsByName, lookup, shapeLookup, out var d)) return false;
                float num = AxisOrLength(a - b, def.NumeratorAxis);
                float denom = AxisOrLength(c - d, def.DenominatorAxis);
                if (denom < 1e-6f) return false;
                value = num / denom;
                return true;

            default:
                return false;
        }
    }

    private static bool TryResolve(string vertexRefName, IReadOnlyDictionary<string, NamedKeyVertex> keyVertsByName, VertexLookup lookup, ShapePositionsLookup? shapeLookup, out OpenTK.Mathematics.Vector3 pos)
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
            int? idx = FindBestInBox(positions, kv, kv.Criterion, findSibling);
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
    public static int? FindBestInBox(OpenTK.Mathematics.Vector3[] positions, NamedKeyVertex kv, BoundingBoxCriterion criterion, Func<NamedKeyVertex, NamedKeyVertex?>? findSibling = null)
    {
        if (positions == null || positions.Length == 0) return null;

        switch (criterion)
        {
            case BoundingBoxCriterion.PinchMinX: return FindPinchOrBulgeX(positions, kv, leftSide: true,  wantPinch: true);
            case BoundingBoxCriterion.PinchMaxX: return FindPinchOrBulgeX(positions, kv, leftSide: false, wantPinch: true);
            case BoundingBoxCriterion.BulgeMinX: return FindPinchOrBulgeX(positions, kv, leftSide: true,  wantPinch: false);
            case BoundingBoxCriterion.BulgeMaxX: return FindPinchOrBulgeX(positions, kv, leftSide: false, wantPinch: false);
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

        // Parabolic sub-bin refinement on the width curve, mirroring FindPinchOrBulgeX.
        float binHeight = yRange / BinCount;
        float centerY = minY + (winnerBin + 0.5f) * binHeight;
        float refinedY = centerY;

        if (winnerBin > 0 && winnerBin < BinCount - 1
            && minIdxPerBin[winnerBin - 1] >= 0 && maxIdxPerBin[winnerBin - 1] >= 0
            && minIdxPerBin[winnerBin + 1] >= 0 && maxIdxPerBin[winnerBin + 1] >= 0)
        {
            float w0 = maxXPerBin[winnerBin - 1] - minXPerBin[winnerBin - 1];
            float w1 = maxXPerBin[winnerBin]     - minXPerBin[winnerBin];
            float w2 = maxXPerBin[winnerBin + 1] - minXPerBin[winnerBin + 1];
            float denom = w0 - 2f * w1 + w2;
            if (MathF.Abs(denom) > 1e-6f)
            {
                float offsetBins = 0.5f * (w0 - w2) / denom;
                if (offsetBins > 0.5f) offsetBins = 0.5f;
                else if (offsetBins < -0.5f) offsetBins = -0.5f;
                refinedY = centerY + offsetBins * binHeight;
            }
        }

        // Rescan within a 1-bin Y band centered on the refined Y for both silhouette sides
        // jointly, so both callers still agree on a shared slice after refinement.
        float bandHalf = binHeight * 0.5f;
        float bandMinY = refinedY - bandHalf;
        float bandMaxY = refinedY + bandHalf;

        int chosenMinIdx = minIdxPerBin[winnerBin];
        int chosenMaxIdx = maxIdxPerBin[winnerBin];
        float chosenMinX = float.MaxValue;
        float chosenMaxX = float.MinValue;
        bool bandHasAny = false;
        for (int i = 0; i < positions.Length; i++)
        {
            var p = positions[i];
            if (p.X < minX || p.X > maxX) continue;
            if (p.Y < bandMinY || p.Y > bandMaxY) continue;
            if (p.Z < minZ || p.Z > maxZ) continue;

            bandHasAny = true;
            if (p.X < chosenMinX) { chosenMinX = p.X; chosenMinIdx = i; }
            if (p.X > chosenMaxX) { chosenMaxX = p.X; chosenMaxIdx = i; }
        }
        if (!bandHasAny)
        {
            // Empty band after refinement — retain the winner bin's picks.
            chosenMinIdx = minIdxPerBin[winnerBin];
            chosenMaxIdx = maxIdxPerBin[winnerBin];
        }

        return leftSide ? chosenMinIdx : chosenMaxIdx;
    }

    /// <summary>True for the four <c>*Pair*X</c> criteria that require joint sibling resolution.</summary>
    public static bool IsPairCriterion(BoundingBoxCriterion criterion)
        => criterion == BoundingBoxCriterion.PinchPairMinX
        || criterion == BoundingBoxCriterion.PinchPairMaxX
        || criterion == BoundingBoxCriterion.BulgePairMinX
        || criterion == BoundingBoxCriterion.BulgePairMaxX;

    /// <summary>Returns the opposite-side partner of a paired criterion (Min ↔ Max within the same
    /// Pinch/Bulge family). Throws for non-paired inputs since callers must gate on <see cref="IsPairCriterion"/>.</summary>
    public static BoundingBoxCriterion PartnerCriterion(BoundingBoxCriterion criterion) => criterion switch
    {
        BoundingBoxCriterion.PinchPairMinX => BoundingBoxCriterion.PinchPairMaxX,
        BoundingBoxCriterion.PinchPairMaxX => BoundingBoxCriterion.PinchPairMinX,
        BoundingBoxCriterion.BulgePairMinX => BoundingBoxCriterion.BulgePairMaxX,
        BoundingBoxCriterion.BulgePairMaxX => BoundingBoxCriterion.BulgePairMinX,
        _ => throw new ArgumentException($"Not a pair criterion: {criterion}", nameof(criterion)),
    };

    private static bool IsPairLeftSide(BoundingBoxCriterion criterion)
        => criterion == BoundingBoxCriterion.PinchPairMinX
        || criterion == BoundingBoxCriterion.BulgePairMinX;

    private static bool IsPairPinch(BoundingBoxCriterion criterion)
        => criterion == BoundingBoxCriterion.PinchPairMinX
        || criterion == BoundingBoxCriterion.PinchPairMaxX;

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
    /// measurement values. An empty <see cref="MeasurementRule.GroupsORlogic"/> never matches.
    /// </summary>
    public static bool RuleMatches(MeasurementRule rule, IReadOnlyDictionary<string, float> measurements)
    {
        if (rule?.GroupsORlogic == null || rule.GroupsORlogic.Count == 0) return false;
        foreach (var group in rule.GroupsORlogic)
        {
            if (group?.ConditionsANDlogic == null || group.ConditionsANDlogic.Count == 0) continue;
            bool allMatch = true;
            foreach (var cond in group.ConditionsANDlogic)
            {
                if (cond == null || string.IsNullOrEmpty(cond.MeasurementName)) { allMatch = false; break; }
                if (!measurements.TryGetValue(cond.MeasurementName, out float val)) { allMatch = false; break; }
                if (!Compare(val, cond.Comparator, cond.Value)) { allMatch = false; break; }
            }
            if (allMatch) return true;
        }
        return false;
    }
}
