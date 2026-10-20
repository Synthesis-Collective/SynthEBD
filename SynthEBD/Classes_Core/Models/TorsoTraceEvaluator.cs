using System;
using System.Collections.Generic;
using Vector3 = OpenTK.Mathematics.Vector3;

namespace SynthEBD;

/// <summary>
/// Pure geometry core for the <c>TorsoTrace</c> body measurements: the outline of the torso as seen from the front
/// with the bust taken out, and the shape readings the Shape rules take from it.
///
/// The <b>trace</b> is the body cut by a curved coronal surface Z = z_mid(Y) that follows the torso's own mid-depth
/// at each height (<see cref="MidSurface"/>): the midpoint of the front and back skin on the centre line, held
/// straight across the bust heights so the bust never enters the cut. Each triangle that crosses the surface
/// contributes one segment; the segments' endpoints are the trace. A single flat plane would cut the neck and the
/// hips off-centre, which is why the surface bends with the body.
///
/// From the trace:
/// <list type="bullet">
/// <item><b>Stem window</b> (<see cref="TryComputeStemWindow"/>): the stretch of torso side between where the hips
/// start to flare (bottom) and where the bust stops hiding the side in the front view (top). Its height says how much
/// "stem" shows; its flare (widest / narrowest torso width inside it) says whether the stem runs straight (Spoon) or
/// tapers in from the lats.</item>
/// <item><b>Lat flare</b> (<see cref="TryComputeLatFlare"/>): waist width / torso width just under the armpits.
/// Lower = the waist pinches in from flaring lats (an hourglass profile).</item>
/// </list>
///
/// Mirrors the offline prototypes <c>obody_arm_tools/silhouettes.py</c> (mid_surface, plane_segments),
/// <c>shape_session_2026-10/bust_bottom.py</c> (algo_D + window) and <c>trace_widths.py</c>, which were fitted to
/// the user's Shape verdicts. Heights are raw CBBE 3BA body units, like the profile's region boxes. No application
/// dependencies, so it is unit-testable in isolation.
/// </summary>
public static class TorsoTraceEvaluator
{
    /// <summary>Folded into a TorsoTrace measurement's cache fingerprint: bump it whenever a change here alters
    /// the values, so cached readings are re-measured.</summary>
    /// <remarks>2: widths need torso points on both sides, ignoring points within <see cref="CentreLineGap"/>
    /// of the centre line. 3: an innermost-point side further out than the torso exit crossing by more than
    /// <see cref="ArmPickTolerance"/> is an arm point and is replaced by the exit.</remarks>
    public const int AlgorithmVersion = 3;

    /// <summary>Trace points within this distance of the centre line (|X| &lt;= it) are not torso sides: on very
    /// heavy / top-heavy bodies the mid-depth surface grazes the front or back skin there, and a near-zero width
    /// would follow. Real torso sides sit at |X| &gt;= ~6 on every cached preset.</summary>
    public const double CentreLineGap = 3.0;

    /// <summary>The trace is only segment endpoints, so a long torso-side segment can span a whole sampling band
    /// and leave just a hanging-arm (or upper-arm "sleeve") point in it. An innermost-point side reading further
    /// out than the torso's exit crossing at that height by more than this is taken as such an arm point.</summary>
    public const double ArmPickTolerance = 1.0;

    /// <summary>Bust heights: the mid-depth is interpolated straight across (BustLowY, BustHighY) so the bust
    /// can't pull the cut forward.</summary>
    public const double BustLowY = 86.0, BustHighY = 106.0;

    /// <summary>A height slice needs front and back skin at least this far apart to give a mid-depth.</summary>
    public const double MinDepth = 3.0;

    /// <summary>Highest allowed stem-window top: above it the trace picks up the upper-arm "sleeve" (from ~Y 97).
    /// Also the window top when the bust hides nothing.</summary>
    public const double StemTopCapY = 95.0;

    /// <summary>A bust vertex is bust (not torso) only when it is more than this far in front of the sternum line.</summary>
    public const double BustForwardOfSternum = 1.0;

    /// <summary>The window bottom is the lowest height still within this factor of the narrowest torso width.</summary>
    public const double FlareStartTolerance = 1.03;

    // Stem-window scan (Y range, step, the band holding the narrowest point), trace half-width sampling.
    private const double WindowScanLow = 76.0, WindowScanHigh = 106.0, WindowScanStep = 0.25;
    private const double PinchBandLow = 78.0, PinchBandHigh = 92.0;
    private const double WindowSampleHalf = 0.25;

    // Lat flare: torso width under the armpits (mean of these heights) and the narrowest waist width.
    private static readonly double[] UnderArmpitHeights = { 93.0, 94.0, 95.0, 96.0 };
    private const double WaistLow = 80.0, WaistHigh = 92.0, WaistStep = 0.5, LatSampleHalf = 0.5;
    private const double WaistMaxAbsX = 20.0; // the hanging arms' loops sit near |X| ~ 25

    /// <summary>Result of <see cref="TryComputeStemWindow"/>.</summary>
    /// <param name="BottomY">Where the hip flare starts.</param>
    /// <param name="TopY">Where the bust stops hiding the torso side (capped at <see cref="StemTopCapY"/>).</param>
    /// <param name="Length">TopY - BottomY: how much stem shows.</param>
    /// <param name="Flare">Widest / narrowest torso half-width inside the window (1 = a straight stem).</param>
    /// <param name="BustCovers">False when no bust vertex reaches past the torso side (the top is the cap).</param>
    public readonly record struct StemWindow(float BottomY, float TopY, float Length, float Flare, bool BustCovers);

    /// <summary>
    /// The torso trace of one body shape: the endpoints of every segment where a triangle crosses the mid-depth
    /// surface, projected onto X-Y. Points 2k and 2k+1 are the ends of segment k.
    /// </summary>
    public sealed class Trace
    {
        internal readonly double[] Xs;
        internal readonly double[] Ys;

        internal Trace(double[] xs, double[] ys)
        {
            Xs = xs;
            Ys = ys;
        }

        /// <summary>Number of trace points (two per crossing triangle).</summary>
        public int PointCount => Xs.Length;

        /// <summary>Half the gap between the innermost trace points on each side of X = 0 within
        /// <paramref name="half"/> of <paramref name="y"/>: the torso's half-width there, with any arm loop (which
        /// lies further out) ignored. NaN when a side has no point.</summary>
        public double InnerHalfWidth(double y, double half) => 0.5 * InnerWidth(y, half);

        /// <summary>Gap between the innermost trace points on each side of X = 0 within <paramref name="half"/> of
        /// <paramref name="y"/>, ignoring points within <see cref="CentreLineGap"/> of the centre line. A side that
        /// lies more than <see cref="ArmPickTolerance"/> beyond the torso exit at <paramref name="y"/>
        /// (<see cref="TorsoExits"/>) is an arm point and is replaced by the exit. NaN when a side has no
        /// point.</summary>
        public double InnerWidth(double y, double half)
        {
            double leftMax = double.NegativeInfinity, rightMin = double.PositiveInfinity;
            for (int i = 0; i < Xs.Length; i++)
            {
                if (Math.Abs(Ys[i] - y) >= half) continue;
                double x = Xs[i];
                if (x < -CentreLineGap) { if (x > leftMax) leftMax = x; }
                else if (x > CentreLineGap) { if (x < rightMin) rightMin = x; }
            }
            if (double.IsInfinity(leftMax) || double.IsInfinity(rightMin)) return double.NaN;

            var (exitLeft, exitRight) = TorsoExits(y);
            double left = -leftMax, right = rightMin;
            if (left > exitLeft + ArmPickTolerance) left = exitLeft;   // false when the exit is NaN
            if (right > exitRight + ArmPickTolerance) right = exitRight;
            return left + right;
        }

        /// <summary>X of every trace segment crossing the line Y = <paramref name="y"/>, sorted. Half-open in Y,
        /// so an endpoint shared by two segments counts once.</summary>
        public List<double> Crossings(double y)
        {
            var xs = new List<double>();
            for (int k = 0; k + 1 < Xs.Length; k += 2)
            {
                double ya = Ys[k], yb = Ys[k + 1];
                if ((ya < y) == (yb < y)) continue;
                double t = (y - ya) / (yb - ya);
                xs.Add(Xs[k] + t * (Xs[k + 1] - Xs[k]));
            }
            xs.Sort();
            return xs;
        }

        /// <summary>Where the line Y = <paramref name="y"/> leaves the body walking out from X = 0: the first
        /// inside-to-outside crossing beyond <see cref="CentreLineGap"/> on each side, as (left |X|, right X).
        /// X = 0 is inside the body when an odd number of crossings lie at X &lt; 0 (the trace loops are closed).
        /// NaN for a side with no such crossing.</summary>
        public (double Left, double Right) TorsoExits(double y)
        {
            var xs = Crossings(y);
            var left = new List<double>();
            var right = new List<double>();
            foreach (double x in xs)
            {
                if (x < 0) left.Add(-x);
                else if (x > 0) right.Add(x);
            }
            left.Sort();
            bool centreInside = left.Count % 2 == 1;
            return (FirstExit(left, centreInside), FirstExit(right, centreInside));
        }

        private static double FirstExit(List<double> outward, bool inside)
        {
            foreach (double v in outward)
            {
                if (inside && v > CentreLineGap) return v;
                inside = !inside;
            }
            return double.NaN;
        }

        /// <summary>Full X extent of the trace points within <paramref name="half"/> of <paramref name="y"/> and
        /// inside |X| &lt; <paramref name="maxAbsX"/>. Needs a point on each side beyond <see cref="CentreLineGap"/>
        /// (a lone point would read as zero width); NaN otherwise.</summary>
        public double ExtentWidth(double y, double half, double maxAbsX)
        {
            double lo = double.PositiveInfinity, hi = double.NegativeInfinity;
            for (int i = 0; i < Xs.Length; i++)
            {
                if (Math.Abs(Ys[i] - y) >= half || Math.Abs(Xs[i]) >= maxAbsX) continue;
                if (Xs[i] < lo) lo = Xs[i];
                if (Xs[i] > hi) hi = Xs[i];
            }
            return lo < -CentreLineGap && hi > CentreLineGap ? hi - lo : double.NaN;
        }
    }

    /// <summary>
    /// The torso mid-depth z_mid at every whole-unit height from floor(min Y) to ceil(max Y): the midpoint of the
    /// front and back skin among vertices within 0.75 of the height on the centre line (|X| &lt; 4, widening to
    /// |X| &lt; 12 where the centre line has no depth), interpolated straight across the bust heights and smoothed
    /// with a 5-sample moving average. Returns false when no height gives a depth.
    /// </summary>
    public static bool TryMidSurface(Vector3[] positions, out double firstY, out double[] zMid)
    {
        firstY = 0; zMid = Array.Empty<double>();
        if (positions == null || positions.Length == 0) return false;
        double minY = double.PositiveInfinity, maxY = double.NegativeInfinity;
        foreach (var p in positions)
        {
            if (p.Y < minY) minY = p.Y;
            if (p.Y > maxY) maxY = p.Y;
        }
        firstY = Math.Floor(minY);
        int n = (int)(Math.Ceiling(maxY) - firstY) + 1;
        var zs = new double[n];
        var known = new bool[n];
        for (int i = 0; i < n; i++)
        {
            double y = firstY + i;
            if (y > BustLowY && y < BustHighY) continue;
            foreach (double halfX in new[] { 4.0, 12.0 })
            {
                double zLo = double.PositiveInfinity, zHi = double.NegativeInfinity;
                foreach (var p in positions)
                {
                    if (Math.Abs(p.Y - y) >= 0.75 || Math.Abs(p.X) >= halfX) continue;
                    if (p.Z < zLo) zLo = p.Z;
                    if (p.Z > zHi) zHi = p.Z;
                }
                if (!double.IsInfinity(zLo) && zHi - zLo >= MinDepth)
                {
                    zs[i] = 0.5 * (zLo + zHi);
                    known[i] = true;
                    break;
                }
            }
        }
        var kx = new List<double>();
        var kz = new List<double>();
        for (int i = 0; i < n; i++)
        {
            if (!known[i]) continue;
            kx.Add(firstY + i);
            kz.Add(zs[i]);
        }
        if (kx.Count == 0) return false;
        for (int i = 0; i < n; i++) zs[i] = Interp(firstY + i, kx, kz);

        // 5-sample moving average with the ends held (numpy pad mode "edge")
        zMid = new double[n];
        for (int i = 0; i < n; i++)
        {
            double s = 0;
            for (int k = -2; k <= 2; k++) s += zs[Math.Clamp(i + k, 0, n - 1)];
            zMid[i] = s / 5.0;
        }
        return true;
    }

    /// <summary>
    /// Cut <paramref name="positions"/>' triangles (<paramref name="indices"/>, three per triangle) with the
    /// mid-depth surface. Returns null when the mid-depth can't be found or there are no triangles.
    /// </summary>
    public static Trace? BuildTrace(Vector3[] positions, int[] indices)
    {
        if (indices == null || indices.Length < 3) return null;
        if (!TryMidSurface(positions, out double firstY, out double[] zMid)) return null;

        var d = new double[positions.Length];
        for (int v = 0; v < positions.Length; v++)
            d[v] = positions[v].Z - InterpUniform(positions[v].Y, firstY, zMid);

        var xs = new List<double>();
        var ys = new List<double>();
        for (int t = 0; t + 2 < indices.Length; t += 3)
        {
            int a = indices[t], b = indices[t + 1], c = indices[t + 2];
            if ((uint)a >= (uint)positions.Length || (uint)b >= (uint)positions.Length || (uint)c >= (uint)positions.Length) continue;
            double sa = d[a], sb = d[b], sc = d[c];
            double lo = Math.Min(sa, Math.Min(sb, sc)), hi = Math.Max(sa, Math.Max(sb, sc));
            if (!(lo < 0 && hi > 0)) continue;
            AddCrossing(positions[a], sa, positions[b], sb, xs, ys);
            AddCrossing(positions[b], sb, positions[c], sc, xs, ys);
            AddCrossing(positions[c], sc, positions[a], sa, xs, ys);
        }
        return new Trace(xs.ToArray(), ys.ToArray());
    }

    private static void AddCrossing(Vector3 p, double sp, Vector3 q, double sq, List<double> xs, List<double> ys)
    {
        if ((sp < 0) == (sq < 0)) return;
        double t = sp / (sp - sq);
        xs.Add(p.X + t * (q.X - p.X));
        ys.Add(p.Y + t * (q.Y - p.Y));
    }

    /// <summary>
    /// Lat flare: the narrowest torso width over the waist heights (trace extent inside |X| &lt; 20) divided by the
    /// mean torso width just under the armpits (innermost trace points, so the upper-arm "sleeve" is left out).
    /// Returns false when either width can't be read.
    /// </summary>
    public static bool TryComputeLatFlare(Trace trace, out float latFlare)
    {
        latFlare = 0f;
        if (trace == null) return false;
        double sum = 0; int count = 0;
        foreach (double y in UnderArmpitHeights)
        {
            double w = trace.InnerWidth(y, LatSampleHalf);
            if (double.IsNaN(w)) continue;
            sum += w; count++;
        }
        if (count == 0) return false;
        double armpit = sum / count;

        double waist = double.PositiveInfinity;
        int steps = (int)Math.Round((WaistHigh - WaistLow) / WaistStep);
        for (int i = 0; i <= steps; i++)
        {
            double w = trace.ExtentWidth(WaistLow + i * WaistStep, LatSampleHalf, WaistMaxAbsX);
            if (!double.IsNaN(w) && w < waist) waist = w;
        }
        if (double.IsInfinity(waist) || armpit <= 1e-9) return false;
        latFlare = (float)(waist / armpit);
        return true;
    }

    /// <summary>
    /// The stem window. <paramref name="bustMembers"/> are the bust region's member vertices (indices into
    /// <paramref name="positions"/>); <paramref name="sternumPoints"/> are the deformed sternum key vertices (any
    /// order, at least two), joined into a line by height. A bust vertex counts when it lies more than
    /// <see cref="BustForwardOfSternum"/> in front of that line (the face points to -Z) and its |X| reaches past the
    /// trace's torso side at its height; the window top is the lowest such vertex, capped at
    /// <see cref="StemTopCapY"/>. The bottom is where the hip flare starts: from the narrowest torso half-width over
    /// Y 78-92, walk down while the width stays within <see cref="FlareStartTolerance"/> of it (a straight stem has
    /// no single pinch). Returns false when the torso width can't be read.
    /// </summary>
    public static bool TryComputeStemWindow(
        Trace trace,
        Vector3[] positions,
        IReadOnlyList<int> bustMembers,
        IReadOnlyList<Vector3> sternumPoints,
        out StemWindow window)
    {
        window = default;
        if (trace == null || positions == null) return false;

        int n = (int)Math.Round((WindowScanHigh - WindowScanLow) / WindowScanStep) + 1;
        var ys = new double[n];
        var tw = new double[n];
        var goodY = new List<double>();
        var goodW = new List<double>();
        for (int i = 0; i < n; i++)
        {
            ys[i] = WindowScanLow + i * WindowScanStep;
            tw[i] = trace.InnerHalfWidth(ys[i], WindowSampleHalf);
            if (!double.IsNaN(tw[i])) { goodY.Add(ys[i]); goodW.Add(tw[i]); }
        }

        int ip = -1;
        for (int i = 0; i < n; i++)
        {
            if (ys[i] < PinchBandLow || ys[i] > PinchBandHigh || double.IsNaN(tw[i])) continue;
            if (ip < 0 || tw[i] < tw[ip]) ip = i;
        }
        if (ip < 0) return false;
        double wp = tw[ip];
        if (wp <= 1e-9) return false;
        int lo = ip;
        while (lo > 0 && !double.IsNaN(tw[lo - 1]) && tw[lo - 1] <= wp * FlareStartTolerance) lo--;
        double bottom = ys[lo];

        double? bustBottom = BustBottomY(positions, bustMembers, sternumPoints, goodY, goodW);
        double top = Math.Min(bustBottom ?? StemTopCapY, StemTopCapY);

        double wmax = double.NegativeInfinity;
        for (int i = 0; i < n; i++)
            if (ys[i] >= bottom && ys[i] <= top && !double.IsNaN(tw[i]) && tw[i] > wmax) wmax = tw[i];
        if (double.IsInfinity(wmax)) wmax = Interp(top, goodY, goodW);

        window = new StemWindow((float)bottom, (float)top, (float)(top - bottom), (float)(wmax / wp), bustBottom.HasValue);
        return true;
    }

    /// <summary>Lowest bust vertex that is in front of the sternum line and reaches past the torso side; null
    /// when none does (the bust hides nothing).</summary>
    private static double? BustBottomY(
        Vector3[] positions, IReadOnlyList<int>? members, IReadOnlyList<Vector3>? sternum,
        List<double> traceY, List<double> traceHalfWidth)
    {
        if (members == null || members.Count == 0 || sternum == null || sternum.Count < 2 || traceY.Count == 0) return null;
        var line = new List<Vector3>(sternum);
        line.Sort((a, b) => a.Y.CompareTo(b.Y));
        var lineY = new List<double>(line.Count);
        var lineZ = new List<double>(line.Count);
        foreach (var p in line) { lineY.Add(p.Y); lineZ.Add(p.Z); }

        double? lowest = null;
        foreach (int v in members)
        {
            if ((uint)v >= (uint)positions.Length) continue;
            var p = positions[v];
            if (!(p.Z < Interp(p.Y, lineY, lineZ) - BustForwardOfSternum)) continue;
            if (!(Math.Abs(p.X) > Interp(p.Y, traceY, traceHalfWidth))) continue;
            if (lowest == null || p.Y < lowest) lowest = p.Y;
        }
        return lowest;
    }

    /// <summary>numpy.interp: linear interpolation on ascending <paramref name="xp"/>, clamped to the end values.</summary>
    public static double Interp(double x, IReadOnlyList<double> xp, IReadOnlyList<double> fp)
    {
        int n = xp.Count;
        if (n == 0) return double.NaN;
        if (x <= xp[0]) return fp[0];
        if (x >= xp[n - 1]) return fp[n - 1];
        int hi = 1;
        while (hi < n - 1 && xp[hi] < x) hi++;
        double x0 = xp[hi - 1], x1 = xp[hi];
        if (x1 <= x0) return fp[hi];
        return fp[hi - 1] + (x - x0) / (x1 - x0) * (fp[hi] - fp[hi - 1]);
    }

    /// <summary>Interp over samples at firstY, firstY + 1, ... (the mid-surface grid), clamped to the ends.</summary>
    private static double InterpUniform(double y, double firstY, double[] values)
    {
        double u = y - firstY;
        if (u <= 0) return values[0];
        int last = values.Length - 1;
        if (u >= last) return values[last];
        int i = (int)Math.Floor(u);
        double f = u - i;
        return values[i] + f * (values[i + 1] - values[i]);
    }
}
