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
