using System.Collections.Generic;
using System.ComponentModel;
using System.Reflection;

namespace CharacterViewer.Rendering;

/// <summary>UI-side grouping of <see cref="BoxCriterionSelection"/> values into algorithm
/// categories, for the TreeView dropdown on the viewer's BB-pick Criterion picker. The enum
/// stays a flat list (so persistence and switch-cases keep working unchanged); this is just
/// a presentation layer on top.
///
/// <para>Parallels <c>SynthEBD.BoundingBoxCriterionTree</c> on the persisted-criterion side
/// but adds a 'Mirror Shortcuts' category for the authoring-only <c>MirrorX</c> /
/// <c>MirrorPinchX</c> / etc. values that don't exist on the persisted enum.</para></summary>
public sealed class BoxCriterionSelectionCategory
{
    public string Header { get; }
    public string Description { get; }
    public IReadOnlyList<BoxCriterionSelectionLeaf> Children { get; }

    public BoxCriterionSelectionCategory(string header, string description, IReadOnlyList<BoxCriterionSelectionLeaf> children)
    {
        Header = header;
        Description = description;
        Children = children;
    }
}

public sealed class BoxCriterionSelectionLeaf
{
    public string Label { get; }
    public string Description { get; }
    public BoxCriterionSelection Value { get; }

    public BoxCriterionSelectionLeaf(string label, string description, BoxCriterionSelection value)
    {
        Label = label;
        Description = description;
        Value = value;
    }
}

public static class BoxCriterionSelectionTree
{
    public static IReadOnlyList<BoxCriterionSelectionCategory> Categories { get; } = Build();

    private static IReadOnlyList<BoxCriterionSelectionCategory> Build() => new[]
    {
        new BoxCriterionSelectionCategory(
            "Axis Extremes",
            "Find the single most-extreme vertex along one axis inside the whole box. Picks 1 vertex per row.",
            new[]
            {
                Leaf(BoxCriterionSelection.MaxX),
                Leaf(BoxCriterionSelection.MinX),
                Leaf(BoxCriterionSelection.MaxY),
                Leaf(BoxCriterionSelection.MinY),
                Leaf(BoxCriterionSelection.MaxZ),
                Leaf(BoxCriterionSelection.MinZ),
            }),
        new BoxCriterionSelectionCategory(
            "Mirrored Across Midline (Left / Right Half)",
            "Restrict the search to one half of the box (X<0 or X>=0) and find the extremum on that side. Authored in left/right pairs so two rows together cover both sides of a symmetric feature.",
            new[]
            {
                Leaf(BoxCriterionSelection.MinYLeftOfX),
                Leaf(BoxCriterionSelection.MinYRightOfX),
                Leaf(BoxCriterionSelection.MaxYLeftOfX),
                Leaf(BoxCriterionSelection.MaxYRightOfX),
                Leaf(BoxCriterionSelection.MinZLeftOfX),
                Leaf(BoxCriterionSelection.MinZRightOfX),
                Leaf(BoxCriterionSelection.MaxZLeftOfX),
                Leaf(BoxCriterionSelection.MaxZRightOfX),
            }),
        new BoxCriterionSelectionCategory(
            "Waist Pinch (single side)",
            "Slice the box into thin horizontal bands and pick from the narrowest band. Each row independently scans for its own narrowest band, so the left and right picks may come from different heights — use the Paired variants when you need both picks at the same height.",
            new[]
            {
                Leaf(BoxCriterionSelection.PinchMinX),
                Leaf(BoxCriterionSelection.PinchMaxX),
            }),
        new BoxCriterionSelectionCategory(
            "Hip Bulge (single side)",
            "Slice the box into thin horizontal bands and pick from the widest band. Each row independently scans for its own widest band, so the left and right picks may come from different heights — use the Paired variants when you need both picks at the same height.",
            new[]
            {
                Leaf(BoxCriterionSelection.BulgeMinX),
                Leaf(BoxCriterionSelection.BulgeMaxX),
            }),
        new BoxCriterionSelectionCategory(
            "Paired Waist-pinch / Hip-bulge (joint-band)",
            "Like Waist Pinch / Hip Bulge but the left and right rows co-operate. They scan the shared box once, find the single narrowest (or widest) band, and pick from THAT band — guaranteeing both picks come from the same height. Use when a PointDistance between the pair should measure honest horizontal width.",
            new[]
            {
                Leaf(BoxCriterionSelection.PinchPairMinX),
                Leaf(BoxCriterionSelection.PinchPairMaxX),
                Leaf(BoxCriterionSelection.BulgePairMinX),
                Leaf(BoxCriterionSelection.BulgePairMaxX),
            }),
        new BoxCriterionSelectionCategory(
            "Depth Pinch / Bulge — front-back along Z (single side)",
            "The front-back (depth) analog of Waist Pinch / Hip Bulge: slice the box into thin horizontal bands and pick from the thinnest (pinch) or thickest (bulge) band measured front-to-back along Z. Model faces -Z, so 'front' = smallest Z, 'back' = largest Z. Each row scans independently — use the Paired variants when you need both picks at the same height. Use for bicep bulge, belly depth, calf bulge.",
            new[]
            {
                Leaf(BoxCriterionSelection.PinchMinZ),
                Leaf(BoxCriterionSelection.PinchMaxZ),
                Leaf(BoxCriterionSelection.BulgeMinZ),
                Leaf(BoxCriterionSelection.BulgeMaxZ),
            }),
        new BoxCriterionSelectionCategory(
            "Paired Depth-pinch / Depth-bulge — front-back along Z (joint-band)",
            "Like Depth Pinch / Bulge but the front and back rows co-operate. They scan the shared box once, find the single thinnest (or thickest) front-to-back band, and pick from THAT band — guaranteeing both picks come from the same height. Use when a PointDistance between the pair should measure honest front-back depth.",
            new[]
            {
                Leaf(BoxCriterionSelection.PinchPairMinZ),
                Leaf(BoxCriterionSelection.PinchPairMaxZ),
                Leaf(BoxCriterionSelection.BulgePairMinZ),
                Leaf(BoxCriterionSelection.BulgePairMaxZ),
            }),
        new BoxCriterionSelectionCategory(
            "Centerline-Anchored (central tube)",
            "Restrict the search to a tubular column running through the box on its central axis (the middle third of the other two coordinates), then pick the extremum on the tube's axis. Use for landmarks that should sit on the body's mid-line rather than at a box corner — navel, crown, spine.",
            new[]
            {
                Leaf(BoxCriterionSelection.MaxXAtCenter),
                Leaf(BoxCriterionSelection.MinXAtCenter),
                Leaf(BoxCriterionSelection.MaxYAtCenter),
                Leaf(BoxCriterionSelection.MinYAtCenter),
                Leaf(BoxCriterionSelection.MaxZAtCenter),
                Leaf(BoxCriterionSelection.MinZAtCenter),
            }),
        new BoxCriterionSelectionCategory(
            "Bone Transition (rig-aware)",
            "Walks the X axis outward from the box-center vertex and returns the last vertex still belonging to the root vertex's dominant bone — i.e. the seam between two rigged body parts. Robust across body types because the algorithm reads the skin weights, not the silhouette. The Paired variants additionally snap both sides to the average Y of the two single-side picks, so a PointDistance between them measures seam-to-seam X width at one consistent height.",
            new[]
            {
                Leaf(BoxCriterionSelection.BoneTransitionMaxX),
                Leaf(BoxCriterionSelection.BoneTransitionMinX),
                Leaf(BoxCriterionSelection.BoneTransitionPairMaxX),
                Leaf(BoxCriterionSelection.BoneTransitionPairMinX),
            }),
        new BoxCriterionSelectionCategory(
            "Mirror Shortcuts (one drag → two rows)",
            "Authoring conveniences. Picking one of these tells the editor to materialize TWO key-vertex rows that share this box but use opposite (or paired) single-axis criteria — one click captures both sides of a symmetric landmark instead of two.",
            new[]
            {
                Leaf(BoxCriterionSelection.MirrorX),
                Leaf(BoxCriterionSelection.MirrorY),
                Leaf(BoxCriterionSelection.MirrorZ),
                Leaf(BoxCriterionSelection.MirrorPinchX),
                Leaf(BoxCriterionSelection.MirrorBulgeX),
                Leaf(BoxCriterionSelection.MinYMirroredAcrossX),
                Leaf(BoxCriterionSelection.MaxYMirroredAcrossX),
                Leaf(BoxCriterionSelection.MinZMirroredAcrossX),
                Leaf(BoxCriterionSelection.MaxZMirroredAcrossX),
                Leaf(BoxCriterionSelection.MirrorPinchZ),
                Leaf(BoxCriterionSelection.MirrorBulgeZ),
                Leaf(BoxCriterionSelection.MirrorBoneTransitionX),
            }),
    };

    private static BoxCriterionSelectionLeaf Leaf(BoxCriterionSelection c)
        => new(GetDisplayName(c), GetDescription(c), c);

    private static string GetDisplayName(BoxCriterionSelection c)
    {
        var field = typeof(BoxCriterionSelection).GetField(c.ToString());
        return field?.GetCustomAttribute<ShortLabelAttribute>()?.Label ?? c.ToString();
    }

    private static string GetDescription(BoxCriterionSelection c)
    {
        var field = typeof(BoxCriterionSelection).GetField(c.ToString());
        return field?.GetCustomAttribute<DescriptionAttribute>()?.Description ?? c.ToString();
    }
}
