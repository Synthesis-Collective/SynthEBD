using System.Collections.Generic;
using System.ComponentModel;
using System.Reflection;

namespace SynthEBD;

/// <summary>UI-side grouping of <see cref="BoundingBoxCriterion"/> values into algorithm
/// categories, for the TreeView dropdown on the Key Vertices editor's Criterion picker. The
/// enum stays a flat list (so persistence and switch-cases keep working unchanged); this is
/// just a presentation layer on top.</summary>
public sealed class BoundingBoxCriterionCategory
{
    public string Header { get; }
    public string Description { get; }
    public IReadOnlyList<BoundingBoxCriterionLeaf> Children { get; }

    public BoundingBoxCriterionCategory(string header, string description, IReadOnlyList<BoundingBoxCriterionLeaf> children)
    {
        Header = header;
        Description = description;
        Children = children;
    }
}

public sealed class BoundingBoxCriterionLeaf
{
    public string Label { get; }
    public string Description { get; }
    public BoundingBoxCriterion Value { get; }

    public BoundingBoxCriterionLeaf(string label, string description, BoundingBoxCriterion value)
    {
        Label = label;
        Description = description;
        Value = value;
    }
}

public static class BoundingBoxCriterionTree
{
    public static IReadOnlyList<BoundingBoxCriterionCategory> Categories { get; } = Build();

    private static IReadOnlyList<BoundingBoxCriterionCategory> Build() => new[]
    {
        new BoundingBoxCriterionCategory(
            "Axis Extremes",
            "Find the single most-extreme vertex along one axis inside the whole box. Picks 1 vertex per row.",
            new[]
            {
                Leaf(BoundingBoxCriterion.MaxX),
                Leaf(BoundingBoxCriterion.MinX),
                Leaf(BoundingBoxCriterion.MaxY),
                Leaf(BoundingBoxCriterion.MinY),
                Leaf(BoundingBoxCriterion.MaxZ),
                Leaf(BoundingBoxCriterion.MinZ),
            }),
        new BoundingBoxCriterionCategory(
            "Mirrored Across Midline (Left / Right Half)",
            "Restrict the search to one half of the box (X<0 or X>=0) and find the extremum on that side. Authored in left/right pairs so two rows together cover both sides of a symmetric feature.",
            new[]
            {
                Leaf(BoundingBoxCriterion.MinYLeftOfX),
                Leaf(BoundingBoxCriterion.MinYRightOfX),
                Leaf(BoundingBoxCriterion.MaxYLeftOfX),
                Leaf(BoundingBoxCriterion.MaxYRightOfX),
                Leaf(BoundingBoxCriterion.MinZLeftOfX),
                Leaf(BoundingBoxCriterion.MinZRightOfX),
                Leaf(BoundingBoxCriterion.MaxZLeftOfX),
                Leaf(BoundingBoxCriterion.MaxZRightOfX),
            }),
        new BoundingBoxCriterionCategory(
            "Waist Pinch (single side)",
            "Slice the box into thin horizontal bands and pick from the narrowest band. Each row independently scans for its own narrowest band, so the left and right picks may come from different heights — use the Paired variants when you need both picks at the same height.",
            new[]
            {
                Leaf(BoundingBoxCriterion.PinchMinX),
                Leaf(BoundingBoxCriterion.PinchMaxX),
            }),
        new BoundingBoxCriterionCategory(
            "Hip Bulge (single side)",
            "Slice the box into thin horizontal bands and pick from the widest band. Each row independently scans for its own widest band, so the left and right picks may come from different heights — use the Paired variants when you need both picks at the same height.",
            new[]
            {
                Leaf(BoundingBoxCriterion.BulgeMinX),
                Leaf(BoundingBoxCriterion.BulgeMaxX),
            }),
        new BoundingBoxCriterionCategory(
            "Paired Waist-pinch / Hip-bulge (joint-band)",
            "Like Waist Pinch / Hip Bulge but the left and right rows co-operate. They scan the shared box once, find the single narrowest (or widest) band, and pick from THAT band — guaranteeing both picks come from the same height. Use when a PointDistance between the pair should measure honest horizontal width.",
            new[]
            {
                Leaf(BoundingBoxCriterion.PinchPairMinX),
                Leaf(BoundingBoxCriterion.PinchPairMaxX),
                Leaf(BoundingBoxCriterion.BulgePairMinX),
                Leaf(BoundingBoxCriterion.BulgePairMaxX),
            }),
        new BoundingBoxCriterionCategory(
            "Centerline-Anchored (central tube)",
            "Restrict the search to a tubular column running through the box on its central axis (the middle third of the other two coordinates), then pick the extremum on the tube's axis. Use for landmarks that should sit on the body's mid-line rather than at a box corner — navel, crown, spine.",
            new[]
            {
                Leaf(BoundingBoxCriterion.MaxXAtCenter),
                Leaf(BoundingBoxCriterion.MinXAtCenter),
                Leaf(BoundingBoxCriterion.MaxYAtCenter),
                Leaf(BoundingBoxCriterion.MinYAtCenter),
                Leaf(BoundingBoxCriterion.MaxZAtCenter),
                Leaf(BoundingBoxCriterion.MinZAtCenter),
            }),
        new BoundingBoxCriterionCategory(
            "Bone Transition (rig-aware)",
            "Walks the X axis outward from the box-center vertex and returns the last vertex still belonging to the root vertex's dominant bone — i.e. the seam between two rigged body parts. Robust across body types because the algorithm reads the skin weights, not the silhouette. Position the box so its center sits inside the 'inboard' region and the boundary you want is the first bone change going outward. The Paired variants additionally snap both sides to the average Y of the two single-side picks, so a PointDistance between them measures seam-to-seam X width at one consistent height.",
            new[]
            {
                Leaf(BoundingBoxCriterion.BoneTransitionMaxX),
                Leaf(BoundingBoxCriterion.BoneTransitionMinX),
                Leaf(BoundingBoxCriterion.BoneTransitionPairMaxX),
                Leaf(BoundingBoxCriterion.BoneTransitionPairMinX),
            }),
    };

    private static BoundingBoxCriterionLeaf Leaf(BoundingBoxCriterion c)
        => new(GetDisplayName(c), GetDescription(c), c);

    private static string GetDisplayName(BoundingBoxCriterion c)
    {
        var field = typeof(BoundingBoxCriterion).GetField(c.ToString());
        return field?.GetCustomAttribute<ShortLabelAttribute>()?.Label ?? c.ToString();
    }

    private static string GetDescription(BoundingBoxCriterion c)
    {
        var field = typeof(BoundingBoxCriterion).GetField(c.ToString());
        return field?.GetCustomAttribute<DescriptionAttribute>()?.Description ?? c.ToString();
    }
}
