using System;
using System.Collections.Generic;
using Vector3 = OpenTK.Mathematics.Vector3;

namespace SynthEBD;

/// <summary>
/// Pure geometry core for the <c>RegionVolume</c> body measurement.
///
/// A region is an axis-aligned box authored on the <b>sliders-0 reference mesh</b>. The box
/// selects a patch of the body surface; the patch is clipped to the box, its open edges form
/// one or more boundary loops, and each loop is capped to close a watertight solid whose
/// volume is the measurement value.
///
/// The expensive topology work (clip, weld, boundary-loop extraction, validation) happens once
/// per body in <see cref="ResolveRegion"/>, which bakes a <see cref="ResolvedRegion"/> whose
/// vertices are stored as <b>barycentric references into the original triangles</b> — never as
/// absolute positions. <see cref="ComputeVolume"/> then evaluates those references against any
/// preset's deformed positions and sums signed tetrahedra. Because the clip/loop topology is
/// fixed, the volume is a continuous function of vertex positions (no per-preset gating) and the
/// region tracks the same anatomy across presets even when tissue sags or displaces.
///
/// Only the box need be persisted to disk; everything in <see cref="ResolvedRegion"/> is
/// recomputed each session from (box + zeroed mesh), mirroring the BoundingBox key-vertex
/// fingerprint design (commit 5e9610a7).
///
/// This type has no application dependencies (only <c>OpenTK.Mathematics.Vector3</c>) so it can
/// be unit-tested in isolation; see <c>RegionVolumeEvaluatorTests</c>.
/// </summary>
public static class RegionVolumeEvaluator
{
    /// <summary>Axis-aligned bounding box in mesh-local space (the persisted region definition).</summary>
    public readonly struct RegionAabb
    {
        public readonly Vector3 Min;
        public readonly Vector3 Max;

        public RegionAabb(Vector3 min, Vector3 max) { Min = min; Max = max; }

        public Vector3 Center => (Min + Max) * 0.5f;

        public bool Contains(Vector3 p, float eps = 0f) =>
            p.X >= Min.X - eps && p.X <= Max.X + eps &&
            p.Y >= Min.Y - eps && p.Y <= Max.Y + eps &&
            p.Z >= Min.Z - eps && p.Z <= Max.Z + eps;
    }

    /// <summary>
    /// A patch vertex expressed as a barycentric blend of one original triangle's three vertices.
    /// Original mesh vertices use a corner weight (1,0,0); split vertices created by clipping carry
    /// interpolated weights. Evaluating against a deformed position array tracks the point to wherever
    /// that triangle moved, with no stored absolute coordinates.
    /// </summary>
    public readonly struct PatchVertexRef
    {
        public readonly int A, B, C;          // original vertex indices of the parent triangle
        public readonly float Wa, Wb, Wc;     // barycentric weights (sum to 1)

        public PatchVertexRef(int a, int b, int c, float wa, float wb, float wc)
        {
            A = a; B = b; C = c; Wa = wa; Wb = wb; Wc = wc;
        }

        public Vector3 Evaluate(Vector3[] positions) =>
            Wa * positions[A] + Wb * positions[B] + Wc * positions[C];
    }

    /// <summary>Baked, session-cached region topology. Recomputed from (box + zeroed mesh) each session.</summary>
    public sealed class ResolvedRegion
    {
        /// <summary>Welded patch vertices, indexed by vertex id; each is a barycentric reference (see <see cref="PatchVertexRef"/>).</summary>
        public PatchVertexRef[] Vertices = Array.Empty<PatchVertexRef>();

        /// <summary>Surface patch as flat triplets of vertex ids (outward winding, inherited from the mesh).</summary>
        public int[] PatchTriangles = Array.Empty<int>();

        /// <summary>Each boundary loop as an ordered list of vertex ids (following the patch's boundary direction). One cap is fanned per loop.</summary>
        public int[][] CapLoops = Array.Empty<int[]>();

        public bool IsValid;

        /// <summary>Empty when valid; otherwise a human-readable reason the box was rejected.</summary>
        public string Diagnostic = "";

        /// <summary>Volume evaluated on the zeroed mesh — a reference baseline (the per-body constant offset).</summary>
        public double ZeroedVolume;

        public int LoopCount => CapLoops.Length;
    }

    /// <summary>Tunables for <see cref="ResolveRegion"/>.</summary>
    public sealed class RegionResolveOptions
    {
        /// <summary>If set, the resolved loop count must equal this exactly (e.g. 1 for a chest, 2 for a thigh).</summary>
        public int? ExpectedCapCount = null;

        /// <summary>Upper bound on boundary loops before the box is rejected as catching more than the intended region.</summary>
        public int MaxCapCount = 2;

        /// <summary>Spatial tolerance for welding clipped vertices by position (mesh-local units).</summary>
        public float WeldEpsilon = 1e-4f;

        /// <summary>Reject boxes whose patch is more than one connected component (usually means two body parts were captured).</summary>
        public bool RequireSingleComponent = true;
    }

    // ------------------------------------------------------------------ public math

    /// <summary>Six times the signed volume of the tetrahedron (origin, a, b, c): (a × b) · c. Computed in double precision.</summary>
    public static double SignedTetraVolume(Vector3 a, Vector3 b, Vector3 c)
    {
        double cx = (double)a.Y * b.Z - (double)a.Z * b.Y;
        double cy = (double)a.Z * b.X - (double)a.X * b.Z;
        double cz = (double)a.X * b.Y - (double)a.Y * b.X;
        return cx * c.X + cy * c.Y + cz * c.Z;
    }

    /// <summary>Volume enclosed by a closed, consistently-wound triangle mesh (flat-triplet indices). Sign-agnostic.</summary>
    public static double ClosedMeshVolume(IReadOnlyList<Vector3> positions, IReadOnlyList<int> triangles)
    {
        if (positions == null || triangles == null) return 0.0;
        double v = 0.0;
        for (int i = 0; i + 2 < triangles.Count; i += 3)
            v += SignedTetraVolume(positions[triangles[i]], positions[triangles[i + 1]], positions[triangles[i + 2]]);
        return Math.Abs(v) / 6.0;
    }

    /// <summary>
    /// Volume of a resolved region evaluated against a preset's deformed position array (indexed by original
    /// vertex). Patch triangles plus a per-loop centroid fan form a closed surface; positions are
    /// centroid-shifted before the signed-tetra sum to keep float magnitudes small.
    /// </summary>
    public static double ComputeVolume(ResolvedRegion region, Vector3[] deformedPositions)
    {
        if (region == null || !region.IsValid || deformedPositions == null) return 0.0;

        var verts = region.Vertices;
        int n = verts.Length;
        if (n == 0) return 0.0;

        var pos = new Vector3[n];
        Vector3 sum = Vector3.Zero;
        for (int i = 0; i < n; i++) { pos[i] = verts[i].Evaluate(deformedPositions); sum += pos[i]; }
        Vector3 origin = sum / n;
        for (int i = 0; i < n; i++) pos[i] -= origin;

        double v = 0.0;
        var tris = region.PatchTriangles;
        for (int i = 0; i + 2 < tris.Length; i += 3)
            v += SignedTetraVolume(pos[tris[i]], pos[tris[i + 1]], pos[tris[i + 2]]);

        foreach (var loop in region.CapLoops)
        {
            int m = loop.Length;
            if (m < 3) continue;
            Vector3 c = Vector3.Zero;
            for (int k = 0; k < m; k++) c += pos[loop[k]];
            c /= m;
            // For each patch boundary edge a_k -> a_{k+1}, the cap triangle (a_k, C, a_{k+1}) supplies the
            // reverse edge a_{k+1} -> a_k, mating with the patch so the closed surface is consistently wound.
            for (int k = 0; k < m; k++)
                v += SignedTetraVolume(pos[loop[k]], c, pos[loop[(k + 1) % m]]);
        }

        return Math.Abs(v) / 6.0;
    }

    // ------------------------------------------------------------------ resolution

    /// <summary>
    /// Resolve a region against the zeroed reference mesh: clip every triangle to the box (tracking
    /// barycentric coordinates), weld the clipped vertices, extract boundary loops, validate watertightness,
    /// and bake a <see cref="ResolvedRegion"/>. On failure, returns a region with <see cref="ResolvedRegion.IsValid"/>
    /// false and a populated <see cref="ResolvedRegion.Diagnostic"/>.
    /// </summary>
    public static ResolvedRegion ResolveRegion(Vector3[] zeroedPositions, int[] indices, RegionAabb box, RegionResolveOptions? options = null)
    {
        options ??= new RegionResolveOptions();
        var result = new ResolvedRegion();

        if (zeroedPositions == null || indices == null || indices.Length < 3)
        {
            result.Diagnostic = "empty or invalid mesh";
            return result;
        }

        const float planeEps = 1e-5f;

        // 1. Clip each triangle to the box, fan-triangulate the inside polygon, record per-vertex
        //    barycentric coords within the parent triangle.
        var outPos = new List<Vector3>();
        var outTri = new List<int>();        // parent original triangle index for each out vertex
        var outU = new List<float>();
        var outV = new List<float>();
        var outW = new List<float>();
        var triFan = new List<int>();        // pre-weld out-vertex index triplets

        var poly = new List<WVert>(8);
        for (int t = 0; t * 3 + 2 < indices.Length; t++)
        {
            int i0 = indices[t * 3], i1 = indices[t * 3 + 1], i2 = indices[t * 3 + 2];
            poly.Clear();
            poly.Add(new WVert(zeroedPositions[i0], 1f, 0f, 0f));
            poly.Add(new WVert(zeroedPositions[i1], 0f, 1f, 0f));
            poly.Add(new WVert(zeroedPositions[i2], 0f, 0f, 1f));

            poly = ClipPolygonHalfSpace(poly, 0, box.Min.X, true, planeEps);
            poly = ClipPolygonHalfSpace(poly, 0, box.Max.X, false, planeEps);
            poly = ClipPolygonHalfSpace(poly, 1, box.Min.Y, true, planeEps);
            poly = ClipPolygonHalfSpace(poly, 1, box.Max.Y, false, planeEps);
            poly = ClipPolygonHalfSpace(poly, 2, box.Min.Z, true, planeEps);
            poly = ClipPolygonHalfSpace(poly, 2, box.Max.Z, false, planeEps);

            if (poly.Count < 3) continue;

            int baseIdx = outPos.Count;
            for (int k = 0; k < poly.Count; k++)
            {
                outPos.Add(poly[k].Pos);
                outTri.Add(t);
                outU.Add(poly[k].U); outV.Add(poly[k].V); outW.Add(poly[k].W);
            }
            for (int k = 1; k + 1 < poly.Count; k++)
            {
                triFan.Add(baseIdx);
                triFan.Add(baseIdx + k);
                triFan.Add(baseIdx + k + 1);
            }
        }

        if (triFan.Count == 0)
        {
            result.Diagnostic = "the box does not intersect the mesh surface";
            return result;
        }

        // 2. Weld clipped vertices by quantized zeroed position; bake each into an original-index barycentric ref.
        float we = options.WeldEpsilon;
        var weld = new Dictionary<(long, long, long), int>();
        var idOf = new int[outPos.Count];
        var refs = new List<PatchVertexRef>();
        for (int k = 0; k < outPos.Count; k++)
        {
            var p = outPos[k];
            var key = (Quant(p.X, we), Quant(p.Y, we), Quant(p.Z, we));
            if (!weld.TryGetValue(key, out int id))
            {
                id = refs.Count;
                weld[key] = id;
                int t = outTri[k];
                refs.Add(new PatchVertexRef(indices[t * 3], indices[t * 3 + 1], indices[t * 3 + 2], outU[k], outV[k], outW[k]));
            }
            idOf[k] = id;
        }

        var patch = new List<int>(triFan.Count);
        for (int i = 0; i + 2 < triFan.Count; i += 3)
        {
            int a = idOf[triFan[i]], b = idOf[triFan[i + 1]], c = idOf[triFan[i + 2]];
            if (a == b || b == c || a == c) continue; // collapsed by welding
            patch.Add(a); patch.Add(b); patch.Add(c);
        }
        if (patch.Count == 0)
        {
            result.Diagnostic = "degenerate patch (no non-collapsed triangles)";
            return result;
        }

        result.Vertices = refs.ToArray();
        result.PatchTriangles = patch.ToArray();

        // Zeroed positions of welded vertices, for area / loop geometry.
        var pos0 = new Vector3[refs.Count];
        for (int id = 0; id < refs.Count; id++) pos0[id] = refs[id].Evaluate(zeroedPositions);

        double area = 0.0;
        for (int i = 0; i + 2 < patch.Count; i += 3)
            area += 0.5 * Vector3.Cross(pos0[patch[i + 1]] - pos0[patch[i]], pos0[patch[i + 2]] - pos0[patch[i]]).Length;
        if (area < 1e-9)
        {
            result.Diagnostic = "zero-area patch";
            return result;
        }

        // 3. Manifold check + boundary-loop extraction from directed edges.
        var dir = new Dictionary<(int, int), int>();
        for (int i = 0; i + 2 < patch.Count; i += 3)
        {
            AddDir(dir, patch[i], patch[i + 1]);
            AddDir(dir, patch[i + 1], patch[i + 2]);
            AddDir(dir, patch[i + 2], patch[i]);
        }
        foreach (var kv in dir)
        {
            if (kv.Value > 1)
            {
                result.Diagnostic = "non-manifold patch (a directed edge is shared by multiple triangles)";
                return result;
            }
        }

        var nextMap = new Dictionary<int, int>();
        foreach (var kv in dir)
        {
            var (x, y) = kv.Key;
            if (!dir.ContainsKey((y, x)))
            {
                if (nextMap.ContainsKey(x))
                {
                    result.Diagnostic = "non-manifold boundary (a vertex has multiple outgoing boundary edges)";
                    return result;
                }
                nextMap[x] = y;
            }
        }

        var loops = new List<int[]>();
        var visited = new HashSet<int>();
        foreach (var start in nextMap.Keys)
        {
            if (visited.Contains(start)) continue;
            var loop = new List<int>();
            int cur = start;
            int guard = 0;
            while (true)
            {
                if (!nextMap.TryGetValue(cur, out int nxt))
                {
                    result.Diagnostic = "open boundary chain (region surface is not watertight after capping)";
                    return result;
                }
                loop.Add(cur);
                visited.Add(cur);
                cur = nxt;
                if (cur == start) break;
                if (visited.Contains(cur))
                {
                    result.Diagnostic = "tangled boundary (loops share a vertex)";
                    return result;
                }
                if (++guard > nextMap.Count + 1)
                {
                    result.Diagnostic = "boundary loop did not close";
                    return result;
                }
            }
            loops.Add(loop.ToArray());
        }

        // 4. Connectivity + loop-count validation.
        if (options.RequireSingleComponent)
        {
            int comps = CountComponents(refs.Count, patch);
            if (comps > 1)
            {
                result.Diagnostic = $"the box captures {comps} disconnected pieces (expected one)";
                return result;
            }
        }

        int lc = loops.Count;
        if (options.ExpectedCapCount.HasValue && lc != options.ExpectedCapCount.Value)
        {
            result.Diagnostic = $"{lc} boundary loop(s) found, expected {options.ExpectedCapCount.Value}";
            return result;
        }
        if (lc > options.MaxCapCount)
        {
            result.Diagnostic = $"{lc} boundary loops found (max {options.MaxCapCount}); the box is likely catching more than the intended region";
            return result;
        }

        result.CapLoops = loops.ToArray();
        result.IsValid = true;
        result.ZeroedVolume = ComputeVolume(result, zeroedPositions);
        return result;
    }

    // ------------------------------------------------------------------ private helpers

    private readonly struct WVert
    {
        public readonly Vector3 Pos;
        public readonly float U, V, W;
        public WVert(Vector3 pos, float u, float v, float w) { Pos = pos; U = u; V = v; W = w; }
    }

    private static float Comp(Vector3 p, int axis) => axis == 0 ? p.X : axis == 1 ? p.Y : p.Z;

    private static WVert LerpW(in WVert a, in WVert b, float t) => new WVert(
        a.Pos + (b.Pos - a.Pos) * t,
        a.U + (b.U - a.U) * t,
        a.V + (b.V - a.V) * t,
        a.W + (b.W - a.W) * t);

    /// <summary>Sutherland–Hodgman clip of a convex polygon against one axis-aligned half-space, interpolating barycentric coords on cut edges.</summary>
    private static List<WVert> ClipPolygonHalfSpace(List<WVert> poly, int axis, float threshold, bool keepGreater, float eps)
    {
        int n = poly.Count;
        var outp = new List<WVert>(n + 2);
        if (n == 0) return outp;

        for (int i = 0; i < n; i++)
        {
            var cur = poly[i];
            var nxt = poly[(i + 1) % n];
            float cc = Comp(cur.Pos, axis) - threshold;
            float cn = Comp(nxt.Pos, axis) - threshold;
            bool curIn = keepGreater ? cc >= -eps : cc <= eps;
            bool nxtIn = keepGreater ? cn >= -eps : cn <= eps;

            if (curIn) outp.Add(cur);
            if (curIn != nxtIn)
            {
                float denom = Comp(nxt.Pos, axis) - Comp(cur.Pos, axis);
                float t = Math.Abs(denom) < 1e-12f ? 0f : (threshold - Comp(cur.Pos, axis)) / denom;
                if (t < 0f) t = 0f; else if (t > 1f) t = 1f;
                outp.Add(LerpW(cur, nxt, t));
            }
        }
        return outp;
    }

    private static long Quant(float v, float eps) => (long)Math.Round(v / eps);

    private static void AddDir(Dictionary<(int, int), int> dir, int x, int y)
    {
        dir.TryGetValue((x, y), out int c);
        dir[(x, y)] = c + 1;
    }

    private static int CountComponents(int vertexCount, List<int> patch)
    {
        var parent = new int[vertexCount];
        for (int i = 0; i < vertexCount; i++) parent[i] = i;

        int Find(int a)
        {
            while (parent[a] != a) { parent[a] = parent[parent[a]]; a = parent[a]; }
            return a;
        }
        void Union(int a, int b) { int ra = Find(a), rb = Find(b); if (ra != rb) parent[ra] = rb; }

        var used = new HashSet<int>();
        for (int i = 0; i + 2 < patch.Count; i += 3)
        {
            Union(patch[i], patch[i + 1]);
            Union(patch[i + 1], patch[i + 2]);
            used.Add(patch[i]); used.Add(patch[i + 1]); used.Add(patch[i + 2]);
        }

        var roots = new HashSet<int>();
        foreach (int v in used) roots.Add(Find(v));
        return roots.Count;
    }
}
