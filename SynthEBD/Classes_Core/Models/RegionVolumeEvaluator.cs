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
    /// Optional rotation of a region box about its own center, as intrinsic Euler angles in degrees
    /// (applied X then Y then Z). All-zero = an axis-aligned box (the default; behaves byte-identically
    /// to the no-rotation path). A rotated box lets the cut plane align with a tilted feature — e.g. a
    /// chest wall that isn't square to the body axes — so the FlatPlane cap stays perpendicular to the
    /// real protrusion direction.
    ///
    /// <para>The whole feature is implemented by transforming the mesh into the box's local frame at
    /// resolve time and running the same axis-aligned clip there; the baked patch is stored as
    /// barycentric refs into the original triangles, which are frame-independent, so volume / overlay /
    /// cap modes / cross-preset tracking need no changes.</para>
    /// </summary>
    public readonly struct BoxRotation
    {
        public readonly float DegX, DegY, DegZ;
        public BoxRotation(float degX, float degY, float degZ) { DegX = degX; DegY = degY; DegZ = degZ; }

        public bool IsIdentity => DegX == 0f && DegY == 0f && DegZ == 0f;

        /// <summary>Columns of the box→world rotation matrix (the box's local axes in world space).
        /// World = center + Rx·Ry·Rz · local. Transpose maps world→local (used at clip time).</summary>
        public (Vector3 ax, Vector3 ay, Vector3 az) Basis()
        {
            float rx = DegX * (MathF.PI / 180f), ry = DegY * (MathF.PI / 180f), rz = DegZ * (MathF.PI / 180f);
            float cx = MathF.Cos(rx), sx = MathF.Sin(rx);
            float cy = MathF.Cos(ry), sy = MathF.Sin(ry);
            float cz = MathF.Cos(rz), sz = MathF.Sin(rz);
            // R = Rx * Ry * Rz (row-style multiply; columns are the rotated basis vectors).
            var ax = new Vector3(cy * cz, cy * sz, -sy);
            var ay = new Vector3(sx * sy * cz - cx * sz, sx * sy * sz + cx * cz, sx * cy);
            var az = new Vector3(cx * sy * cz + sx * sz, cx * sy * sz - sx * cz, cx * cy);
            return (ax, ay, az);
        }
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

    /// <summary>How a region's open boundary loop(s) are capped to close the volume — and thus what
    /// "volume" means. See <see cref="ComputeVolume"/>.</summary>
    public enum RegionCapMode
    {
        /// <summary>Cap each loop with a fan from the loop's own (deformed) centroid. The lid follows
        /// the exact anatomical ring, so on a non-rigidly-inflated bust the ring isn't planar and the
        /// lid looks "scooped" (saddle). Volume = enclosed by the bump surface + that ring fan.</summary>
        AnatomicalFan = 0,

        /// <summary>Cap each loop against a FLAT plane perpendicular to the cut axis, positioned at the
        /// loop's mean cut-axis coordinate. The lid is flat in every view (a clean "salami cut"), and
        /// the volume is the tissue protruding past that plane. Tracks the bump but doesn't scoop.</summary>
        FlatPlane = 1,
    }

    /// <summary>Baked, session-cached region topology. Recomputed from (box + zeroed mesh) each session.</summary>
    public sealed class ResolvedRegion
    {
        /// <summary>Shape this region's geometry lives on (e.g. "CBBE 3BA"). Set by <see cref="ResolveRegions"/>
        /// so consumers can fetch the matching deformed positions without a separate region lookup;
        /// <see cref="ComputeVolume"/> itself does not read it.</summary>
        public string ShapeName = "";

        /// <summary>Welded patch vertices, indexed by vertex id; each is a barycentric reference (see <see cref="PatchVertexRef"/>).</summary>
        public PatchVertexRef[] Vertices = Array.Empty<PatchVertexRef>();

        /// <summary>Surface patch as flat triplets of vertex ids (outward winding, inherited from the mesh).</summary>
        public int[] PatchTriangles = Array.Empty<int>();

        /// <summary>Each boundary loop as an ordered list of vertex ids (following the patch's boundary direction). One cap is fanned per loop.</summary>
        public int[][] CapLoops = Array.Empty<int[]>();

        /// <summary>Unit normal of the cut plane in WORLD space, auto-detected at resolve time as the
        /// box-local axis whose loop coordinates vary least (the face the loop lies on), mapped back to
        /// world. For an unrotated chest box this is ±Z. Used by <see cref="RegionCapMode.FlatPlane"/>
        /// to orient the flat lid perpendicular to the (possibly rotated) cut face.</summary>
        public Vector3 CutNormal = new Vector3(0, 0, 1);

        /// <summary>The cap mode this region should be measured + drawn with. Carried here (set from
        /// the owning <c>NamedRegion.CapMode</c> by <see cref="ResolveRegions"/>) so volume/overlay
        /// callers don't need a separate lookup. Doesn't affect the baked topology — only how the
        /// open loops are closed — so a mode change doesn't require re-resolving the box.</summary>
        public RegionCapMode CapMode = RegionCapMode.FlatPlane;

        public bool IsValid;

        /// <summary>Empty when valid; otherwise a human-readable reason the box was rejected.</summary>
        public string Diagnostic = "";

        /// <summary>Volume evaluated on the zeroed mesh — a reference baseline (the per-body constant offset).</summary>
        public double ZeroedVolume;

        /// <summary>Original vertex indices that belong to this region — the membership rule
        /// (box ∪ additive edits \ subtractive edits) evaluated in zeroed space by
        /// <see cref="ResolveMemberVertices"/>. Populated <b>before</b> any validity gating so it is
        /// available even when <see cref="IsValid"/> is false: a <see cref="KeyVertexStrategy.Region"/>
        /// key vertex only needs the member set, not a valid closed volume. These are indices into the
        /// shape's full per-vertex arrays, so they index the deformed positions directly.</summary>
        public int[] MemberVertexIndices = Array.Empty<int>();

        public int LoopCount => CapLoops.Length;
    }

    /// <summary>
    /// A single hand-curated add/remove edit layered on top of a region's box (Option B —
    /// "box + additive/subtractive edit layer"). Stored renumber-stably as a <b>zeroed-space
    /// position</b>, never a raw index, so it survives body-mod reinstalls that renumber vertices
    /// (the same invariant the box obeys). The original index is carried only as a non-authoritative
    /// <see cref="IndexHint"/>, validated by position at resolve time and never trusted blindly.
    ///
    /// <para><see cref="Additive"/> = true forces the matched vertex <i>into</i> the region even when
    /// it lies outside the box (grows the patch past the box at that vertex); = false forces it
    /// <i>out</i> even when the box contains it (trims it — an interior removal opens a hole, i.e. an
    /// extra boundary loop). An empty edit set resolves byte-identically to the plain box.</para>
    /// </summary>
    public readonly struct VertexEditRef
    {
        public readonly Vector3 ZeroedPosition;
        public readonly bool Additive;
        public readonly int IndexHint;

        public VertexEditRef(Vector3 zeroedPosition, bool additive, int indexHint = -1)
        {
            ZeroedPosition = zeroedPosition; Additive = additive; IndexHint = indexHint;
        }
    }

    /// <summary>Result of <see cref="MatchVertexEdits"/>: the curated edits resolved to current-mesh
    /// vertex indices, split by sign, plus a count of edits that found no vertex within epsilon (these
    /// are <b>not</b> silently dropped — the caller logs <see cref="MissCount"/>).</summary>
    public sealed class MatchedEdits
    {
        /// <summary>Original vertex indices forced into the region.</summary>
        public HashSet<int> AddIndices = new();

        /// <summary>Original vertex indices forced out of the region (wins over Add on a conflict).</summary>
        public HashSet<int> RemoveIndices = new();

        /// <summary>Edits whose stored position matched no current vertex within <c>eps</c>.</summary>
        public int MissCount;

        public bool IsEmpty => AddIndices.Count == 0 && RemoveIndices.Count == 0;
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

        /// <summary>Cap mode stamped onto the resolved region (and used for its baseline ZeroedVolume).
        /// Doesn't affect the baked topology — only how volume/overlay close the loops.</summary>
        public RegionCapMode CapMode = RegionCapMode.FlatPlane;

        /// <summary>Curated add/remove edits (Option B), already matched to current-mesh vertex indices
        /// by <see cref="MatchVertexEdits"/>. Null/empty = plain box (today's behavior, byte-identical).
        /// When present, the clip preserves the smooth analytic cut on box faces no edit touches, and
        /// switches to a vertex-granular induced-patch rule on triangles an edit does touch.</summary>
        public HashSet<int>? AddVertexIndices = null;
        public HashSet<int>? RemoveVertexIndices = null;
    }

    /// <summary>True when <paramref name="worldPoint"/> lies inside <paramref name="box"/> after the
    /// box's <paramref name="rotation"/> is undone (the same world→box-local transform <see cref="ResolveRegion"/>
    /// uses internally). Lets callers test region membership of a vertex without re-running a resolve —
    /// the editor uses it so a curated edit is only stored when it genuinely deviates from the box
    /// (forcing in a vertex the box already contains, or out one it already excludes, is a no-op and is
    /// dropped rather than spuriously switching the region into edited/induced mode).</summary>
    public static bool BoxContainsRotated(RegionAabb box, BoxRotation rotation, Vector3 worldPoint, float eps = 0f)
    {
        if (rotation.IsIdentity) return box.Contains(worldPoint, eps);
        var (ax, ay, az) = rotation.Basis();
        var c = box.Center;
        var d = worldPoint - c;
        var local = new Vector3(Vector3.Dot(d, ax), Vector3.Dot(d, ay), Vector3.Dot(d, az)) + c;
        return box.Contains(local, eps);
    }

    // ------------------------------------------------------------------ vertex-edit matching

    /// <summary>
    /// Matches each curated <see cref="VertexEditRef"/> (a zeroed-space position) to the nearest current
    /// zeroed-mesh vertex within <paramref name="eps"/>, returning the add/remove index sets the resolve
    /// honors. The stored <see cref="VertexEditRef.IndexHint"/> is tried first and accepted only if it
    /// still lands within <paramref name="eps"/> of the stored position (renumbering invalidates it
    /// silently → fall through to the brute-force nearest search). Unmatched edits are counted in
    /// <see cref="MatchedEdits.MissCount"/>, never dropped without a trace — the caller logs them
    /// (the "no silent caps" convention). Brute-force is O(verts) per edit, fine for a once-per-session
    /// resolve; a uniform-grid hash is an easy optimization if the curated set ever grows large.
    /// </summary>
    public static MatchedEdits MatchVertexEdits(Vector3[] currentZeroedPositions, IReadOnlyList<VertexEditRef> edits, float eps)
    {
        var result = new MatchedEdits();
        if (currentZeroedPositions == null || currentZeroedPositions.Length == 0 || edits == null || edits.Count == 0)
            return result;

        float epsSq = eps * eps;
        for (int e = 0; e < edits.Count; e++)
        {
            var edit = edits[e];
            int best = -1;

            // 1. Trust the hint only if it still sits on the stored position (survives a no-renumber load).
            int hint = edit.IndexHint;
            if (hint >= 0 && hint < currentZeroedPositions.Length &&
                (currentZeroedPositions[hint] - edit.ZeroedPosition).LengthSquared <= epsSq)
            {
                best = hint;
            }
            else
            {
                // 2. Brute-force nearest within eps (renumber-stable fallback).
                float bestSq = epsSq;
                for (int i = 0; i < currentZeroedPositions.Length; i++)
                {
                    float d = (currentZeroedPositions[i] - edit.ZeroedPosition).LengthSquared;
                    if (d <= bestSq) { bestSq = d; best = i; }
                }
            }

            if (best < 0) { result.MissCount++; continue; }
            if (edit.Additive) result.AddIndices.Add(best);
            else result.RemoveIndices.Add(best);
        }
        return result;
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
    /// Converts a box drawn against one position set (e.g. a deformed preset's mesh) into the box
    /// that selects the <b>same surface patch</b> in a reference position set (the sliders-0 mesh).
    /// A region's box must be stored in zeroed space so the baked patch tracks every preset, but it's
    /// convenient to author against a deformed body.
    ///
    /// <para><b>Why not just the tight AABB of the captured set?</b> A region box is supposed to be
    /// <i>loose</i> on its "air" faces (clearing the bump) and <i>cut</i> on the face(s) that slice
    /// the body (the chest wall), so the in-box patch ends in one clean cap loop. A tight AABB hugs
    /// the captured vertices on all six faces, so the mesh pokes through several faces → many cut
    /// loops → a fragmented patch. So this preserves the drawn box's per-face air margin: it finds
    /// the captured set's tight bound in BOTH spaces, measures the gap between the drawn box face and
    /// the tight bound on each of the six faces (≈0 for a cut face, >0 for an air face), and carries
    /// those gaps onto the reference-space tight bound. Cut faces stay cutting at the same anatomy;
    /// air faces stay loose.</para>
    ///
    /// <para>Both arrays must share topology (same length / index space — guaranteed for a
    /// fixed-topology body across presets). Returns null when the box contains no vertices or the
    /// arrays mismatch.</para>
    /// </summary>
    public static RegionAabb? ConvertBoxByVertexSet(
        Vector3[] authoringPositions, Vector3[] referencePositions, RegionAabb authoringBox)
    {
        if (authoringPositions == null || referencePositions == null) return null;
        if (authoringPositions.Length == 0 || authoringPositions.Length != referencePositions.Length) return null;

        // Tight bound of the captured set in BOTH spaces.
        var aMin = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
        var aMax = new Vector3(float.MinValue, float.MinValue, float.MinValue);
        var rMin = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
        var rMax = new Vector3(float.MinValue, float.MinValue, float.MinValue);
        int hits = 0;
        for (int i = 0; i < authoringPositions.Length; i++)
        {
            var a = authoringPositions[i];
            if (!authoringBox.Contains(a)) continue;
            hits++;
            if (a.X < aMin.X) aMin.X = a.X; if (a.Y < aMin.Y) aMin.Y = a.Y; if (a.Z < aMin.Z) aMin.Z = a.Z;
            if (a.X > aMax.X) aMax.X = a.X; if (a.Y > aMax.Y) aMax.Y = a.Y; if (a.Z > aMax.Z) aMax.Z = a.Z;
            var r = referencePositions[i];
            if (r.X < rMin.X) rMin.X = r.X; if (r.Y < rMin.Y) rMin.Y = r.Y; if (r.Z < rMin.Z) rMin.Z = r.Z;
            if (r.X > rMax.X) rMax.X = r.X; if (r.Y > rMax.Y) rMax.Y = r.Y; if (r.Z > rMax.Z) rMax.Z = r.Z;
        }
        if (hits == 0) return null;

        // Per-face air gap from the drawn box to the captured set's tight bound (clamped ≥ 0 — the
        // drawn box should enclose the set, but guard float slop), then carry onto the reference
        // tight bound. Over-loose air faces are harmless (still one cut); a cut face has gap ≈ 0 so
        // it lands at the same anatomy on the reference mesh.
        var marginMin = new Vector3(
            MathF.Max(0f, aMin.X - authoringBox.Min.X),
            MathF.Max(0f, aMin.Y - authoringBox.Min.Y),
            MathF.Max(0f, aMin.Z - authoringBox.Min.Z));
        var marginMax = new Vector3(
            MathF.Max(0f, authoringBox.Max.X - aMax.X),
            MathF.Max(0f, authoringBox.Max.Y - aMax.Y),
            MathF.Max(0f, authoringBox.Max.Z - aMax.Z));

        return new RegionAabb(rMin - marginMin, rMax + marginMax);
    }

    /// <summary>
    /// Volume of a resolved region evaluated against a preset's deformed position array (indexed by original
    /// vertex). Patch triangles plus a per-loop centroid fan form a closed surface; positions are
    /// centroid-shifted before the signed-tetra sum to keep float magnitudes small.
    /// </summary>
    public static double ComputeVolume(ResolvedRegion region, Vector3[] deformedPositions)
        => ComputeVolume(region, deformedPositions, RegionCapMode.AnatomicalFan);

    /// <summary>As <see cref="ComputeVolume(ResolvedRegion, Vector3[])"/>, with the cap mode chosen
    /// per <paramref name="capMode"/>. <see cref="RegionCapMode.AnatomicalFan"/> fans each loop from
    /// its own centroid (cap follows the deformed ring — can look scooped). <see cref="RegionCapMode.FlatPlane"/>
    /// caps each loop against a flat plane perpendicular to <see cref="ResolvedRegion.CutAxis"/> at the
    /// loop's mean cut-axis coordinate (flat lid; volume = tissue protruding past that plane).</summary>
    public static double ComputeVolume(ResolvedRegion region, Vector3[] deformedPositions, RegionCapMode capMode)
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

        Vector3 nrm = region.CutNormal.LengthSquared > 1e-12f ? region.CutNormal.Normalized() : new Vector3(0, 0, 1);
        foreach (var loop in region.CapLoops)
        {
            int m = loop.Length;
            if (m < 3) continue;

            // Cap centroid. AnatomicalFan: the loop's true centroid (lid follows the deformed ring).
            // FlatPlane: project every loop vertex (and the apex) onto a flat plane whose normal is the
            // cut normal, at the loop's mean signed distance along that normal — so the lid is flat and
            // perpendicular to the (possibly rotated) cut face.
            Vector3 c = Vector3.Zero;
            for (int k = 0; k < m; k++) c += pos[loop[k]];
            c /= m;

            if (capMode == RegionCapMode.FlatPlane)
            {
                float planeD = Vector3.Dot(c, nrm); // mean signed distance of the loop along the normal
                Vector3 cFlat = ProjectToPlane(c, nrm, planeD);
                for (int k = 0; k < m; k++)
                {
                    Vector3 a = ProjectToPlane(pos[loop[k]], nrm, planeD);
                    Vector3 b = ProjectToPlane(pos[loop[(k + 1) % m]], nrm, planeD);
                    v += SignedTetraVolume(a, cFlat, b);
                }
            }
            else
            {
                // For each patch boundary edge a_k -> a_{k+1}, the cap triangle (a_k, C, a_{k+1}) supplies the
                // reverse edge a_{k+1} -> a_k, mating with the patch so the closed surface is consistently wound.
                for (int k = 0; k < m; k++)
                    v += SignedTetraVolume(pos[loop[k]], c, pos[loop[(k + 1) % m]]);
            }
        }

        return Math.Abs(v) / 6.0;
    }

    /// <summary>Reads the <paramref name="axis"/> component (0=X,1=Y,2=Z) of <paramref name="p"/>.</summary>
    public static float AxisComp(Vector3 p, int axis) => axis == 0 ? p.X : axis == 1 ? p.Y : p.Z;

    /// <summary>Returns <paramref name="p"/> with its <paramref name="axis"/> component set to <paramref name="value"/>.</summary>
    public static Vector3 WithAxis(Vector3 p, int axis, float value) => axis switch
    {
        0 => new Vector3(value, p.Y, p.Z),
        1 => new Vector3(p.X, value, p.Z),
        _ => new Vector3(p.X, p.Y, value),
    };

    /// <summary>Projects <paramref name="p"/> onto the plane {x : dot(x, <paramref name="unitNormal"/>) =
    /// <paramref name="planeD"/>} along the normal (slides it parallel to the normal until it lands on
    /// the plane). Used by <see cref="RegionCapMode.FlatPlane"/> to flatten the cap ring onto the cut
    /// plane, generalizing the axis-aligned "set one coordinate" projection to an arbitrary (rotated)
    /// plane normal.</summary>
    public static Vector3 ProjectToPlane(Vector3 p, Vector3 unitNormal, float planeD)
        => p - unitNormal * (Vector3.Dot(p, unitNormal) - planeD);

    /// <summary>
    /// Returns the ordered overlay points for one cap loop, evaluated against
    /// <paramref name="deformedPositions"/>, for drawing the region's contour. In
    /// <see cref="RegionCapMode.FlatPlane"/> mode the loop is projected onto the flat cut plane (every
    /// point's cut-axis coordinate set to the loop's mean), so the drawn contour is flat ("salami
    /// cut") instead of the scooped deformed ring. In <see cref="RegionCapMode.AnatomicalFan"/> mode
    /// the true deformed ring positions are returned. Empty when the loop index is out of range.
    /// </summary>
    public static List<Vector3> GetCapLoopOverlayPoints(
        ResolvedRegion region, int loopIndex, Vector3[] deformedPositions, RegionCapMode capMode)
    {
        var outPts = new List<Vector3>();
        if (region == null || !region.IsValid || deformedPositions == null) return outPts;
        if (loopIndex < 0 || loopIndex >= region.CapLoops.Length) return outPts;

        var loop = region.CapLoops[loopIndex];
        int m = loop.Length;
        if (m < 2) return outPts;

        var ring = new Vector3[m];
        for (int k = 0; k < m; k++) ring[k] = region.Vertices[loop[k]].Evaluate(deformedPositions);

        if (capMode == RegionCapMode.FlatPlane)
        {
            Vector3 nrm = region.CutNormal.LengthSquared > 1e-12f ? region.CutNormal.Normalized() : new Vector3(0, 0, 1);
            float mean = 0f;
            for (int k = 0; k < m; k++) mean += Vector3.Dot(ring[k], nrm);
            mean /= m;
            for (int k = 0; k < m; k++) ring[k] = ProjectToPlane(ring[k], nrm, mean);
        }
        outPts.AddRange(ring);
        return outPts;
    }

    /// <summary>
    /// Builds the region's closed surface (the bump patch + the cap fan(s) that close it, in the given
    /// <paramref name="capMode"/>) as a flat list of interleaved triangle vertices — 6 floats per vertex
    /// (position.xyz then normal.xyz) — evaluated against <paramref name="deformedPositions"/>. This is
    /// the geometry the "Solid" region-view mode draws: feed it to <c>VM_CharacterViewer.SetRegionSolid</c>.
    /// Per-triangle (flat) normals are computed so the debug shader's lighting gives the magenta solid
    /// readable shape from any angle. Returns an empty list for an invalid region. Winding matches the
    /// volume integration (patch keeps mesh winding; cap fans mate with the patch boundary).
    /// </summary>
    public static List<float> BuildSolidSurface(ResolvedRegion region, Vector3[] deformedPositions, RegionCapMode capMode)
    {
        var outFloats = new List<float>();
        if (region == null || !region.IsValid || deformedPositions == null) return outFloats;

        int n = region.Vertices.Length;
        if (n == 0) return outFloats;
        var pos = new Vector3[n];
        for (int i = 0; i < n; i++) pos[i] = region.Vertices[i].Evaluate(deformedPositions);

        void EmitTri(Vector3 a, Vector3 b, Vector3 c)
        {
            var nrm = Vector3.Cross(b - a, c - a);
            float len = nrm.Length;
            nrm = len > 1e-12f ? nrm / len : new Vector3(0, 1, 0);
            void V(Vector3 p) { outFloats.Add(p.X); outFloats.Add(p.Y); outFloats.Add(p.Z); outFloats.Add(nrm.X); outFloats.Add(nrm.Y); outFloats.Add(nrm.Z); }
            V(a); V(b); V(c);
        }

        // Surface patch (the real bump), in mesh winding.
        var tris = region.PatchTriangles;
        for (int i = 0; i + 2 < tris.Length; i += 3)
            EmitTri(pos[tris[i]], pos[tris[i + 1]], pos[tris[i + 2]]);

        // Cap fan(s) — same construction the volume uses, so the solid is exactly the measured solid.
        Vector3 nrmCut = region.CutNormal.LengthSquared > 1e-12f ? region.CutNormal.Normalized() : new Vector3(0, 0, 1);
        foreach (var loop in region.CapLoops)
        {
            int m = loop.Length;
            if (m < 3) continue;
            Vector3 c = Vector3.Zero;
            for (int k = 0; k < m; k++) c += pos[loop[k]];
            c /= m;

            if (capMode == RegionCapMode.FlatPlane)
            {
                float planeD = Vector3.Dot(c, nrmCut);
                Vector3 cFlat = ProjectToPlane(c, nrmCut, planeD);
                for (int k = 0; k < m; k++)
                    EmitTri(ProjectToPlane(pos[loop[k]], nrmCut, planeD), cFlat, ProjectToPlane(pos[loop[(k + 1) % m]], nrmCut, planeD));
            }
            else
            {
                for (int k = 0; k < m; k++)
                    EmitTri(pos[loop[k]], c, pos[loop[(k + 1) % m]]);
            }
        }

        return outFloats;
    }

    /// <summary>
    /// Returns the deduplicated triangle edges of the region's surface patch (the real bump mesh),
    /// evaluated against <paramref name="deformedPositions"/>, as world-space line segments — the cyan
    /// wireframe drawn over the magenta solid in "Solid" view mode. Uses the patch's own vertex ids to
    /// dedup shared edges (each interior edge once), so the boundary contour (the cut loop) is included
    /// without the synthetic cap-fan spokes. Empty for an invalid region.
    /// </summary>
    public static List<(Vector3 A, Vector3 B)> BuildSolidWireframe(ResolvedRegion region, Vector3[] deformedPositions)
    {
        var outEdges = new List<(Vector3, Vector3)>();
        if (region == null || !region.IsValid || deformedPositions == null) return outEdges;

        int n = region.Vertices.Length;
        if (n == 0) return outEdges;
        var pos = new Vector3[n];
        for (int i = 0; i < n; i++) pos[i] = region.Vertices[i].Evaluate(deformedPositions);

        var seen = new HashSet<long>();
        var tris = region.PatchTriangles;
        void AddEdge(int a, int b)
        {
            int lo = Math.Min(a, b), hi = Math.Max(a, b);
            long key = ((long)lo << 32) | (uint)hi;
            if (seen.Add(key)) outEdges.Add((pos[a], pos[b]));
        }
        for (int i = 0; i + 2 < tris.Length; i += 3)
        {
            AddEdge(tris[i], tris[i + 1]);
            AddEdge(tris[i + 1], tris[i + 2]);
            AddEdge(tris[i + 2], tris[i]);
        }
        return outEdges;
    }

    /// <summary>Deduped patch wireframe (as <see cref="BuildSolidWireframe"/>) with each endpoint tagged
    /// for whether its mesh vertex is in <paramref name="addedOriginalIndices"/> — i.e. a vertex the
    /// curated edits forced into the region. Lets the editor recolor the half-edges around added
    /// vertices (green) without re-deriving topology. Also reports which added indices actually appear
    /// in the patch (<see cref="PresentAddedIndices"/>) so the caller can mark *isolated* added vertices
    /// — ones whose neighbors aren't all members yet, so no triangle formed — separately, and the mean
    /// edge length for sizing node glyphs.</summary>
    public sealed class TaggedWireframe
    {
        public List<(Vector3 A, Vector3 B, bool AAdded, bool BAdded)> Edges = new();
        public HashSet<int> PresentAddedIndices = new();
        public float AverageEdgeLength;
    }

    /// <summary>The original-mesh vertex index of a patch ref when it is an un-split <b>corner</b>
    /// (one barycentric weight is ~1), else -1. Curated additive vertices are always whole-triangle
    /// corners, so this is how an added resolved vertex is identified.</summary>
    public static int CornerOriginalIndex(in PatchVertexRef r)
    {
        if (r.Wa >= 0.999f) return r.A;
        if (r.Wb >= 0.999f) return r.B;
        if (r.Wc >= 0.999f) return r.C;
        return -1;
    }

    public static TaggedWireframe BuildTaggedWireframe(
        ResolvedRegion region, Vector3[] deformedPositions, IReadOnlyCollection<int>? addedOriginalIndices)
    {
        var result = new TaggedWireframe();
        if (region == null || !region.IsValid || deformedPositions == null) return result;
        int n = region.Vertices.Length;
        if (n == 0) return result;

        var addSet = addedOriginalIndices as ISet<int> ?? (addedOriginalIndices != null ? new HashSet<int>(addedOriginalIndices) : null);

        var pos = new Vector3[n];
        var added = new bool[n];
        for (int i = 0; i < n; i++)
        {
            pos[i] = region.Vertices[i].Evaluate(deformedPositions);
            if (addSet != null)
            {
                int ci = CornerOriginalIndex(region.Vertices[i]);
                if (ci >= 0 && addSet.Contains(ci)) { added[i] = true; result.PresentAddedIndices.Add(ci); }
            }
        }

        var seen = new HashSet<long>();
        var tris = region.PatchTriangles;
        double sum = 0; int cnt = 0;
        void AddEdge(int a, int b)
        {
            int lo = Math.Min(a, b), hi = Math.Max(a, b);
            long key = ((long)lo << 32) | (uint)hi;
            if (!seen.Add(key)) return;
            result.Edges.Add((pos[a], pos[b], added[a], added[b]));
            sum += (pos[a] - pos[b]).Length; cnt++;
        }
        for (int i = 0; i + 2 < tris.Length; i += 3)
        {
            AddEdge(tris[i], tris[i + 1]);
            AddEdge(tris[i + 1], tris[i + 2]);
            AddEdge(tris[i + 2], tris[i]);
        }
        result.AverageEdgeLength = cnt > 0 ? (float)(sum / cnt) : 0f;
        return result;
    }

    // ------------------------------------------------------------------ resolution

    /// <summary>
    /// The set of original vertex indices that belong to a region, by the same rule
    /// <see cref="ResolveRegion"/> uses for its edited-patch membership: the (optionally rotated) box
    /// contains the vertex in box-local space OR it is force-added, AND it is not force-removed (remove
    /// wins). Evaluated in <b>zeroed</b> space so it is preset- and renumber-stable; the returned indices
    /// index the shape's full per-vertex arrays, so a caller can apply them to the deformed positions.
    /// Exposed for <see cref="KeyVertexStrategy.Region"/> key vertices, which need the candidate set even
    /// when the region is not a valid closed volume.
    /// </summary>
    public static int[] ResolveMemberVertices(Vector3[] zeroedPositions, RegionAabb box, BoxRotation rotation, HashSet<int>? addSet, HashSet<int>? removeSet, float planeEps = 1e-5f)
    {
        if (zeroedPositions == null || zeroedPositions.Length == 0) return Array.Empty<int>();

        bool rotated = !rotation.IsIdentity;
        Vector3 center = box.Center;
        var (ax, ay, az) = rotation.Basis();
        Vector3 ToLocal(Vector3 wp)
        {
            if (!rotated) return wp;
            Vector3 d = wp - center;
            return new Vector3(Vector3.Dot(d, ax), Vector3.Dot(d, ay), Vector3.Dot(d, az)) + center;
        }

        var members = new List<int>();
        for (int vid = 0; vid < zeroedPositions.Length; vid++)
        {
            bool inRegion = (box.Contains(ToLocal(zeroedPositions[vid]), planeEps) || (addSet != null && addSet.Contains(vid)))
                            && !(removeSet != null && removeSet.Contains(vid));
            if (inRegion) members.Add(vid);
        }
        return members.ToArray();
    }

    /// <summary>
    /// Resolve a region against the zeroed reference mesh: clip every triangle to the box (tracking
    /// barycentric coordinates), weld the clipped vertices, extract boundary loops, validate watertightness,
    /// and bake a <see cref="ResolvedRegion"/>. On failure, returns a region with <see cref="ResolvedRegion.IsValid"/>
    /// false and a populated <see cref="ResolvedRegion.Diagnostic"/>.
    /// </summary>
    public static ResolvedRegion ResolveRegion(Vector3[] zeroedPositions, int[] indices, RegionAabb box, RegionResolveOptions? options = null)
        => ResolveRegion(zeroedPositions, indices, box, default, options);

    /// <summary>
    /// As <see cref="ResolveRegion(Vector3[], int[], RegionAabb, RegionResolveOptions)"/>, but the box
    /// may be rotated about its center by <paramref name="rotation"/>. The mesh is transformed into the
    /// box's local frame for clipping (so the same axis-aligned clip handles the rotated box), while the
    /// baked vertex refs stay barycentric against the original triangles — frame-independent, so volume
    /// / overlay / tracking are unaffected. An identity rotation takes the exact same path as before.
    /// </summary>
    public static ResolvedRegion ResolveRegion(Vector3[] zeroedPositions, int[] indices, RegionAabb box, BoxRotation rotation, RegionResolveOptions? options = null)
    {
        options ??= new RegionResolveOptions();
        var result = new ResolvedRegion();

        if (zeroedPositions == null || indices == null || indices.Length < 3)
        {
            result.Diagnostic = "empty or invalid mesh";
            return result;
        }

        const float planeEps = 1e-5f;

        // Box-local transform. With an identity rotation this is the identity, so the clip runs on the
        // raw world positions exactly as before. With a rotation, every mesh vertex is mapped into the
        // box's local frame (translate to box center, then apply the inverse rotation = the basis as a
        // world→local map) so the same axis-aligned clip cuts the rotated box.
        bool rotated = !rotation.IsIdentity;
        Vector3 center = box.Center;
        var (ax, ay, az) = rotation.Basis(); // box-local axes in world space
        Vector3 ToLocal(Vector3 wp)
        {
            if (!rotated) return wp;
            Vector3 d = wp - center;
            // world→local: components along each box axis, then re-center so the box AABB still applies.
            return new Vector3(Vector3.Dot(d, ax), Vector3.Dot(d, ay), Vector3.Dot(d, az)) + center;
        }

        // Curated add/remove edits (Option B). Empty/null = plain box → the loop below takes the exact
        // same per-triangle path as before (clip every triangle), so unedited regions are byte-identical.
        var addSet = options.AddVertexIndices;
        var remSet = options.RemoveVertexIndices;
        bool hasEdits = (addSet != null && addSet.Count > 0) || (remSet != null && remSet.Count > 0);

        // Membership of an original vertex when edits are present: in the region if the box contains it
        // (in box-local space, so rotation is honored) OR it is force-added, AND it is not force-removed
        // (remove wins). The box-local point reuses ToLocal so the same axis-aligned containment that the
        // clip uses also drives membership. planeEps matches the clip's boundary tolerance.
        bool Member(int vid) =>
            (box.Contains(ToLocal(zeroedPositions[vid]), planeEps) || (addSet != null && addSet.Contains(vid))) &&
            !(remSet != null && remSet.Contains(vid));

        // Member set (original indices) — captured here, BEFORE any validity gating below, so a
        // Region-strategy key vertex can use it even when the volume patch is rejected. Same rule as
        // Member(), shared via the public helper. Cheap O(n) pass; only walked once per resolve.
        result.MemberVertexIndices = ResolveMemberVertices(zeroedPositions, box, rotation, addSet, remSet, planeEps);

        // 1. Build the surface patch.
        //
        // No edits → clip every triangle to the box (Sutherland-Hodgman), keeping the smooth analytic
        // cut on box faces. This is byte-identical to the original behavior.
        //
        // Any edits present → "bake-on-first-edit": the whole patch switches to the vertex-granular
        // INDUCED rule (a triangle is in iff all three of its original vertices are members of the
        // box∪adds\removes set). We do NOT mix the two: a clipped sliver edge truncated at a box face
        // (e.g. x=3.5) meeting a grown whole triangle's full edge (x=4) puts the cut point mid-edge on
        // the grown triangle — a T-junction that gives a vertex two outgoing boundary edges and trips
        // the manifold check. Resolving the entire edited patch by whole-triangle membership keeps it
        // watertight (no split vertices, so no T-junctions); the trade is a vertex-granular boundary,
        // which is the accepted cost of curating individual vertices. Removing an interior vertex drops
        // its incident triangles and opens a hole (an extra boundary loop); adding an out-of-box vertex
        // pulls in the triangles all of whose verts have now become members.
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

            // Edited patch: induced whole-triangle rule (all three verts members or skip).
            if (hasEdits)
            {
                if (!(Member(i0) && Member(i1) && Member(i2))) continue;
                int wholeBase = outPos.Count;
                outPos.Add(ToLocal(zeroedPositions[i0])); outTri.Add(t); outU.Add(1f); outV.Add(0f); outW.Add(0f);
                outPos.Add(ToLocal(zeroedPositions[i1])); outTri.Add(t); outU.Add(0f); outV.Add(1f); outW.Add(0f);
                outPos.Add(ToLocal(zeroedPositions[i2])); outTri.Add(t); outU.Add(0f); outV.Add(0f); outW.Add(1f);
                triFan.Add(wholeBase); triFan.Add(wholeBase + 1); triFan.Add(wholeBase + 2);
                continue;
            }

            poly.Clear();
            // Clip in box-local space; the barycentric weights (1,0,0)/(0,1,0)/(0,0,1) are unchanged by
            // the transform, so the baked refs still interpolate correctly against WORLD positions.
            poly.Add(new WVert(ToLocal(zeroedPositions[i0]), 1f, 0f, 0f));
            poly.Add(new WVert(ToLocal(zeroedPositions[i1]), 0f, 1f, 0f));
            poly.Add(new WVert(ToLocal(zeroedPositions[i2]), 0f, 0f, 1f));

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

        // Auto-detect the cut plane's normal: the BOX-LOCAL axis whose loop-vertex coordinates vary the
        // LEAST is the one the cut plane is perpendicular to (all boundary verts lie on that cut face).
        // Measured in box-local space (where the box is axis-aligned) so it works for a rotated box;
        // the winning local axis is then mapped back to WORLD via the box basis for the stored normal.
        // For an unrotated chest box cutting the chest-wall (a Z face) this resolves to world ±Z.
        {
            var amin = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
            var amax = new Vector3(float.MinValue, float.MinValue, float.MinValue);
            foreach (var loop in loops)
                foreach (var vid in loop)
                {
                    var p = ToLocal(pos0[vid]); // box-local coords of the welded loop vertex
                    if (p.X < amin.X) amin.X = p.X; if (p.Y < amin.Y) amin.Y = p.Y; if (p.Z < amin.Z) amin.Z = p.Z;
                    if (p.X > amax.X) amax.X = p.X; if (p.Y > amax.Y) amax.Y = p.Y; if (p.Z > amax.Z) amax.Z = p.Z;
                }
            var spread = amax - amin;
            int localAxis = (spread.X <= spread.Y && spread.X <= spread.Z) ? 0 : (spread.Y <= spread.Z ? 1 : 2);
            // Map the box-local axis to its world direction (the basis column). Unrotated → world axis.
            result.CutNormal = localAxis == 0 ? ax : localAxis == 1 ? ay : az;
        }

        result.CapMode = options.CapMode;
        result.IsValid = true;
        result.ZeroedVolume = ComputeVolume(result, zeroedPositions, options.CapMode);
        return result;
    }

    /// <summary>
    /// Resolves a batch of <see cref="NamedRegion"/>s against the current (sliders-0) mesh state,
    /// returning a {region name → <see cref="ResolvedRegion"/>} map for one body/weight. Each
    /// region's box is resolved via <see cref="ResolveRegion"/> against the positions/indices
    /// fetched through the supplied lookups (typically <c>viewer.GetShapePositions</c> /
    /// <c>viewer.GetShapeIndices</c>); the resolved <see cref="ResolvedRegion.ShapeName"/> is set
    /// so callers can fetch matching deformed positions later. First-wins on duplicate names.
    /// A region whose shape has no loaded geometry yields an invalid <see cref="ResolvedRegion"/>
    /// with a diagnostic rather than being omitted, so callers can surface the reason.
    /// </summary>
    /// <summary>Default tolerance for matching a stored vertex-edit position to a current-mesh vertex
    /// (mesh-local NIF units). Small enough not to grab a neighbor on a dense body, loose enough to ride
    /// out the float rounding a body-mod re-export introduces. The matcher always takes the *nearest*
    /// within this radius, so the exact-same-mesh case (distance 0) is never ambiguous.</summary>
    public const float DefaultVertexEditMatchEps = 1e-3f;

    public static Dictionary<string, ResolvedRegion> ResolveRegions(
        IEnumerable<NamedRegion> regions,
        Func<string, Vector3[]?> shapePositions,
        Func<string, int[]?> shapeIndices,
        Action<string>? logMisses = null,
        float matchEps = DefaultVertexEditMatchEps)
    {
        var result = new Dictionary<string, ResolvedRegion>(StringComparer.Ordinal);
        if (regions == null) return result;
        foreach (var region in regions)
        {
            if (region == null) continue;
            var name = region.Name?.Trim() ?? "";
            if (name.Length == 0 || result.ContainsKey(name)) continue;

            var positions = shapePositions?.Invoke(region.ShapeName);
            var indices = shapeIndices?.Invoke(region.ShapeName);
            ResolvedRegion resolved;
            if (positions == null || positions.Length == 0 || indices == null || indices.Length < 3)
            {
                resolved = new ResolvedRegion { Diagnostic = $"shape '{region.ShapeName}' has no loaded geometry" };
            }
            else
            {
                var box = new RegionAabb(
                    new Vector3(region.BoxMinX, region.BoxMinY, region.BoxMinZ),
                    new Vector3(region.BoxMaxX, region.BoxMaxY, region.BoxMaxZ));
                var rotation = new BoxRotation(region.RotX, region.RotY, region.RotZ);
                var options = new RegionResolveOptions { ExpectedCapCount = region.ExpectedCapCount, CapMode = region.CapMode };

                // Option B: match the curated edits (zeroed-space positions) to current-mesh vertices.
                // The positions array IS the sliders-0 mesh (the edits were authored in that space), so
                // the match is exact on the same mesh and survives a renumber by position. Unmatched
                // edits are logged, not silently dropped.
                if (region.VertexEdits != null && region.VertexEdits.Count > 0)
                {
                    var editRefs = new List<VertexEditRef>(region.VertexEdits.Count);
                    foreach (var e in region.VertexEdits)
                    {
                        if (e == null) continue;
                        editRefs.Add(new VertexEditRef(new Vector3(e.X, e.Y, e.Z), e.Additive, e.IndexHint));
                    }
                    var matched = MatchVertexEdits(positions, editRefs, matchEps);
                    options.AddVertexIndices = matched.AddIndices;
                    options.RemoveVertexIndices = matched.RemoveIndices;
                    if (matched.MissCount > 0)
                        logMisses?.Invoke($"Region '{name}': {matched.MissCount} curated vertex edit(s) matched no vertex on shape '{region.ShapeName}' within {matchEps} units and were skipped.");
                }

                resolved = ResolveRegion(positions, indices, box, rotation, options);
            }
            resolved.ShapeName = region.ShapeName ?? "";
            result[name] = resolved;
        }
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
