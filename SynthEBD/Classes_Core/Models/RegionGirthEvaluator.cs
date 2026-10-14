using System;
using System.Collections.Generic;
using System.Linq;
using Vector3 = OpenTK.Mathematics.Vector3;

namespace SynthEBD;

/// <summary>
/// Pure geometry core for the <c>Girth</c> body measurement: the tape-measure circumference of a body band.
///
/// The band is a <see cref="NamedRegion"/>, but only its member vertices are read (the box ∪ curated edits,
/// evaluated on the sliders-0 mesh by <see cref="RegionVolumeEvaluator.ResolveRegion"/>), never its cap loops,
/// so a band that cuts through both legs or an armpit needs no valid closed volume.
///
/// The band is cut into horizontal slices at fixed zeroed-space heights. Each slice holds the member vertices
/// within <see cref="SlabHalfThickness"/> of its height on the zeroed mesh, so the same vertices are measured on
/// every preset however far the deformation moves them. A slice's girth is the perimeter of the convex hull of
/// those vertices' deformed positions projected onto the horizontal (X, Z) plane, which is the path a tape
/// measure takes: it bridges concavities such as the gap between the breasts or between the thighs. The
/// slices are then reduced to one value (<see cref="GirthAggregate"/>): Max for the fullest bust or hips, Min
/// for the natural waist.
///
/// Mirrors the offline <c>obody_arm_tools/ffit_girth.py</c> (tracker D52), which validated the approach against
/// the FFIT girth definitions. No application dependencies, so it is unit-testable in isolation.
/// </summary>
public static class RegionGirthEvaluator
{
    /// <summary>Half the thickness (raw units, zeroed space) of the slab of vertices one slice collects. Thin
    /// enough that a slice is one level of the body, thick enough to collect a full ring on the body mesh.</summary>
    public const float SlabHalfThickness = 0.35f;

    /// <summary>Smallest slice spacing honoured; smaller <c>GirthStep</c> values are clamped to this.</summary>
    public const float MinStep = 0.1f;

    /// <summary>
    /// Girth of <paramref name="region"/> against a preset's <paramref name="deformedPositions"/> (indexed by
    /// original vertex). Returns false when the region has no members, no slice collects three or more
    /// vertices with a non-degenerate hull, or a member index is out of range for the positions.
    /// </summary>
    public static bool TryComputeGirth(
        RegionVolumeEvaluator.ResolvedRegion region,
        Vector3[] deformedPositions,
        GirthAggregate aggregate,
        float step,
        out float girth)
    {
        girth = 0f;
        var slices = MeasureSlices(region, deformedPositions, step);
        if (slices == null || slices.Count == 0) return false;

        double value = aggregate switch
        {
            GirthAggregate.Min => slices.Min(s => s.Perimeter),
            GirthAggregate.Mean => slices.Average(s => s.Perimeter),
            _ => slices.Max(s => s.Perimeter),
        };
        girth = (float)value;
        return true;
    }

    /// <summary>
    /// The tape paths behind <see cref="TryComputeGirth"/>, for drawing: each measured slice's convex hull as a
    /// closed loop of deformed 3D vertex positions, in hull order. Max / Min return the one slice they pick (the
    /// first on a tie, matching the value); Mean returns every slice it averages. A straight hull edge across
    /// the front of the chest or between the thighs is the bridge a tape makes. Empty when the girth doesn't resolve.
    /// </summary>
    public static List<Vector3[]> MeasuredLoops(
        RegionVolumeEvaluator.ResolvedRegion region,
        Vector3[] deformedPositions,
        GirthAggregate aggregate,
        float step)
    {
        var loops = new List<Vector3[]>();
        var slices = MeasureSlices(region, deformedPositions, step);
        if (slices == null || slices.Count == 0) return loops;

        IEnumerable<Slice> chosen = aggregate switch
        {
            GirthAggregate.Mean => slices,
            GirthAggregate.Min => new[] { slices.Aggregate((a, b) => b.Perimeter < a.Perimeter ? b : a) },
            _ => new[] { slices.Aggregate((a, b) => b.Perimeter > a.Perimeter ? b : a) },
        };
        foreach (var s in chosen)
            loops.Add(s.HullVertexIds.Select(v => deformedPositions[v]).ToArray());
        return loops;
    }

    /// <summary>One measured slice: its hull perimeter and the original vertex ids on the hull, in order.</summary>
    private readonly record struct Slice(double Perimeter, int[] HullVertexIds);

    /// <summary>Every slice of the band with a non-degenerate hull, bottom to top. Null when the region has no
    /// members or a member index is out of range for the positions.</summary>
    private static List<Slice>? MeasureSlices(RegionVolumeEvaluator.ResolvedRegion region, Vector3[] deformedPositions, float step)
    {
        if (region == null || deformedPositions == null) return null;
        var members = region.MemberVertexIndices;
        var ys = region.MemberZeroedY;
        if (members == null || ys == null || members.Length == 0 || ys.Length != members.Length) return null;

        float minY = float.MaxValue, maxY = float.MinValue;
        foreach (var y in ys) { if (y < minY) minY = y; if (y > maxY) maxY = y; }

        if (!(step >= MinStep)) step = MinStep; // also catches NaN
        float half = Math.Min(SlabHalfThickness, step * 0.5f);

        var slices = new List<Slice>();
        var pts = new List<(double X, double Z)>();
        var ids = new List<int>();
        for (float level = minY + step * 0.5f; level <= maxY; level += step)
        {
            pts.Clear();
            ids.Clear();
            for (int i = 0; i < members.Length; i++)
            {
                if (Math.Abs(ys[i] - level) >= half) continue;
                int vid = members[i];
                if ((uint)vid >= (uint)deformedPositions.Length) return null;
                var p = deformedPositions[vid];
                pts.Add((p.X, p.Z));
                ids.Add(vid);
            }
            var hull = ConvexHullOrder(pts);
            double perimeter = Perimeter(pts, hull);
            if (perimeter > 1e-6) slices.Add(new Slice(perimeter, hull.Select(h => ids[h]).ToArray()));
        }
        return slices;
    }

    /// <summary>Perimeter of the 2D convex hull of <paramref name="points"/> (Andrew's monotone chain). 0 for
    /// fewer than three points or a collinear set.</summary>
    public static double ConvexHullPerimeter(IReadOnlyList<(double X, double Z)> points)
        => points == null ? 0.0 : Perimeter(points, ConvexHullOrder(points));

    /// <summary>Indices into <paramref name="points"/> of its convex hull, in order around the hull (Andrew's
    /// monotone chain, collinear points dropped). Empty for fewer than three points or a collinear set.</summary>
    private static int[] ConvexHullOrder(IReadOnlyList<(double X, double Z)> points)
    {
        if (points == null || points.Count < 3) return Array.Empty<int>();
        var order = Enumerable.Range(0, points.Count).ToArray();
        Array.Sort(order, (i, j) =>
        {
            var a = points[i]; var b = points[j];
            return a.X != b.X ? a.X.CompareTo(b.X) : a.Z.CompareTo(b.Z);
        });

        var hull = new int[order.Length * 2];
        int k = 0;
        for (int i = 0; i < order.Length; i++) // lower hull
        {
            while (k >= 2 && Cross(points[hull[k - 2]], points[hull[k - 1]], points[order[i]]) <= 0) k--;
            hull[k++] = order[i];
        }
        for (int i = order.Length - 2, lower = k + 1; i >= 0; i--) // upper hull
        {
            while (k >= lower && Cross(points[hull[k - 2]], points[hull[k - 1]], points[order[i]]) <= 0) k--;
            hull[k++] = order[i];
        }
        int n = k - 1; // the last point repeats the first
        return n < 3 ? Array.Empty<int>() : hull[..n];
    }

    private static double Perimeter(IReadOnlyList<(double X, double Z)> points, int[] hull)
    {
        double perimeter = 0.0;
        for (int i = 0; i < hull.Length; i++)
        {
            var a = points[hull[i]];
            var b = points[hull[(i + 1) % hull.Length]];
            double dx = b.X - a.X, dz = b.Z - a.Z;
            perimeter += Math.Sqrt(dx * dx + dz * dz);
        }
        return perimeter;
    }

    private static double Cross((double X, double Z) o, (double X, double Z) a, (double X, double Z) b)
        => (a.X - o.X) * (b.Z - o.Z) - (a.Z - o.Z) * (b.X - o.X);
}
