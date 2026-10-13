using System;
using System.Collections.Generic;
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
        if (region == null || deformedPositions == null) return false;
        var members = region.MemberVertexIndices;
        var ys = region.MemberZeroedY;
        if (members == null || ys == null || members.Length == 0 || ys.Length != members.Length) return false;

        float minY = float.MaxValue, maxY = float.MinValue;
        foreach (var y in ys) { if (y < minY) minY = y; if (y > maxY) maxY = y; }

        if (!(step >= MinStep)) step = MinStep; // also catches NaN
        float half = Math.Min(SlabHalfThickness, step * 0.5f);

        var slices = new List<double>();
        var pts = new List<(double X, double Z)>();
        for (float level = minY + step * 0.5f; level <= maxY; level += step)
        {
            pts.Clear();
            for (int i = 0; i < members.Length; i++)
            {
                if (Math.Abs(ys[i] - level) >= half) continue;
                int vid = members[i];
                if ((uint)vid >= (uint)deformedPositions.Length) return false;
                var p = deformedPositions[vid];
                pts.Add((p.X, p.Z));
            }
            double perimeter = ConvexHullPerimeter(pts);
            if (perimeter > 1e-6) slices.Add(perimeter);
        }
        if (slices.Count == 0) return false;

        double value = aggregate switch
        {
            GirthAggregate.Min => Min(slices),
            GirthAggregate.Mean => Mean(slices),
            _ => Max(slices),
        };
        girth = (float)value;
        return true;
    }

    /// <summary>Perimeter of the 2D convex hull of <paramref name="points"/> (Andrew's monotone chain). 0 for
    /// fewer than three points or a collinear set.</summary>
    public static double ConvexHullPerimeter(IReadOnlyList<(double X, double Z)> points)
    {
        if (points == null || points.Count < 3) return 0.0;
        var sorted = new List<(double X, double Z)>(points);
        sorted.Sort((a, b) => a.X != b.X ? a.X.CompareTo(b.X) : a.Z.CompareTo(b.Z));

        var hull = new (double X, double Z)[sorted.Count * 2];
        int k = 0;
        for (int i = 0; i < sorted.Count; i++) // lower hull
        {
            while (k >= 2 && Cross(hull[k - 2], hull[k - 1], sorted[i]) <= 0) k--;
            hull[k++] = sorted[i];
        }
        for (int i = sorted.Count - 2, lower = k + 1; i >= 0; i--) // upper hull
        {
            while (k >= lower && Cross(hull[k - 2], hull[k - 1], sorted[i]) <= 0) k--;
            hull[k++] = sorted[i];
        }
        int n = k - 1; // the last point repeats the first
        if (n < 3) return 0.0;

        double perimeter = 0.0;
        for (int i = 0; i < n; i++)
        {
            var a = hull[i];
            var b = hull[(i + 1) % n];
            double dx = b.X - a.X, dz = b.Z - a.Z;
            perimeter += Math.Sqrt(dx * dx + dz * dz);
        }
        return perimeter;
    }

    private static double Cross((double X, double Z) o, (double X, double Z) a, (double X, double Z) b)
        => (a.X - o.X) * (b.Z - o.Z) - (a.Z - o.Z) * (b.X - o.X);

    private static double Max(List<double> v) { double m = double.MinValue; foreach (var x in v) if (x > m) m = x; return m; }
    private static double Min(List<double> v) { double m = double.MaxValue; foreach (var x in v) if (x < m) m = x; return m; }
    private static double Mean(List<double> v) { double s = 0; foreach (var x in v) s += x; return s / v.Count; }
}
