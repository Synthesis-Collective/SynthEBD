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

/// <summary>
/// A user-named vertex handle referencing a single vertex inside a specific body shape mesh.
/// </summary>
[DebuggerDisplay("{Name} @ {ShapeName}[{VertexIndex}]")]
public class NamedKeyVertex
{
    /// <summary>User-provided name (e.g. "LShoulder", "Waist_Front"). Unique within a profile.</summary>
    public string Name { get; set; } = "";

    /// <summary>Name of the shape mesh (e.g. "CBBE", "3BA") this vertex belongs to. Matches <see cref="GlMesh"/> shape naming.</summary>
    public string ShapeName { get; set; } = "";

    /// <summary>Zero-based index into the shape's post-deformation bind-pose vertex buffer.</summary>
    public int VertexIndex { get; set; } = -1;
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
