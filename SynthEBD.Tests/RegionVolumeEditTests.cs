using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using SynthEBD;
using Xunit;
using Vector3 = OpenTK.Mathematics.Vector3;
using static SynthEBD.RegionVolumeEvaluator;

namespace SynthEBD.Tests;

/// <summary>
/// Tests for the Option-B vertex-edit layer. A region keeps its box; once any hand-curated edit is
/// present the patch resolves by the vertex-granular INDUCED rule (box∪adds\removes), the
/// "bake-on-first-edit" model. Edits are stored renumber-stably as zeroed-space positions
/// (<see cref="RegionVolumeEvaluator.VertexEditRef"/>) and matched to current-mesh vertices at resolve
/// time (<see cref="RegionVolumeEvaluator.MatchVertexEdits"/>). Synthetic meshes exercise the whole
/// path without the rendering stack.
/// </summary>
public class RegionVolumeEditTests
{
    // ---- synthetic mesh ----

    /// <summary>An open triangulated sheet in the z=0 plane: (n+1)x(n+1) verts at integer (i,j), each
    /// cell split into two triangles. Index of vertex (i,j) = j*(n+1)+i. Boundary = the perimeter (one
    /// loop); the induced patch over a sub-rectangle of members is again one loop, and removing an
    /// interior member opens a second (hole) loop.</summary>
    private static (Vector3[] pos, int[] tris) Sheet(int n)
    {
        int side = n + 1;
        var p = new Vector3[side * side];
        for (int j = 0; j < side; j++)
            for (int i = 0; i < side; i++)
                p[j * side + i] = new Vector3(i, j, 0);

        var t = new List<int>();
        for (int j = 0; j < n; j++)
            for (int i = 0; i < n; i++)
            {
                int v00 = j * side + i, v10 = j * side + i + 1, v01 = (j + 1) * side + i, v11 = (j + 1) * side + i + 1;
                t.AddRange(new[] { v00, v10, v11, v00, v11, v01 });
            }
        return (p, t.ToArray());
    }

    private const int N = 6;            // 7x7 verts; index(i,j) = j*7 + i
    private static int Idx(int i, int j) => j * (N + 1) + i;

    private static RegionAabb Box(float minX, float minY, float minZ, float maxX, float maxY, float maxZ)
        => new RegionAabb(new Vector3(minX, minY, minZ), new Vector3(maxX, maxY, maxZ));

    /// <summary>Interior sub-rectangle box: captures sheet verts with x,y in (0.5,5.5) → i,j in 1..5.</summary>
    private static RegionAabb InteriorBox() => Box(0.5f, 0.5f, -1f, 5.5f, 5.5f, 1f);

    private static ResolvedRegion ResolveWithEdits(Vector3[] pos, int[] tris, RegionAabb box,
        IEnumerable<int>? add = null, IEnumerable<int>? remove = null)
    {
        var opts = new RegionResolveOptions
        {
            AddVertexIndices = add != null ? new HashSet<int>(add) : null,
            RemoveVertexIndices = remove != null ? new HashSet<int>(remove) : null,
        };
        return ResolveRegion(pos, tris, box, default, opts);
    }

    /// <summary>The induced baseline: a single redundant in-box add forces induced mode without changing
    /// membership, so this is exactly the box's baked vertex set (what bake-on-first-edit seeds).</summary>
    private static ResolvedRegion InducedBaseline(Vector3[] pos, int[] tris)
        => ResolveWithEdits(pos, tris, InteriorBox(), add: new[] { Idx(1, 1) });

    private static float MaxResolvedX(ResolvedRegion r, Vector3[] pos) => r.Vertices.Max(v => v.Evaluate(pos).X);
    private static int TriCount(ResolvedRegion r) => r.PatchTriangles.Length / 3;

    // ---- no edits == plain box (backward compatibility) ----

    [Fact]
    public void EmptyEditSets_ResolveIdenticallyToPlainBox()
    {
        var (pos, tris) = Sheet(N);
        var box = InteriorBox();

        var plain = ResolveRegion(pos, tris, box);
        var withEmpty = ResolveWithEdits(pos, tris, box, add: new int[0], remove: new int[0]);

        withEmpty.IsValid.Should().Be(plain.IsValid);
        withEmpty.LoopCount.Should().Be(plain.LoopCount);
        withEmpty.PatchTriangles.Length.Should().Be(plain.PatchTriangles.Length);
    }

    [Fact]
    public void InducedBaseline_IsValid_OneLoop_AndASubsetOfTheSmoothClip()
    {
        var (pos, tris) = Sheet(N);
        var plain = ResolveRegion(pos, tris, InteriorBox());
        var induced = InducedBaseline(pos, tris);

        induced.IsValid.Should().BeTrue(induced.Diagnostic);
        induced.LoopCount.Should().Be(1);
        // Induced keeps only whole in-box triangles, so it never extends past the smooth clip.
        TriCount(induced).Should().BeLessThanOrEqualTo(plain.PatchTriangles.Length / 3);
    }

    [Fact]
    public void RedundantEdits_AreNoOps_AddInBox_RemoveOutOfBox()
    {
        // Adding a vertex already inside the box and removing one already outside change no membership,
        // so the patch must match the induced baseline exactly.
        var (pos, tris) = Sheet(N);
        var baseline = InducedBaseline(pos, tris);

        var r = ResolveWithEdits(pos, tris, InteriorBox(),
            add: new[] { Idx(2, 2) }, remove: new[] { Idx(0, 0) });

        r.IsValid.Should().BeTrue(r.Diagnostic);
        r.LoopCount.Should().Be(baseline.LoopCount);
        TriCount(r).Should().Be(TriCount(baseline));
        MaxResolvedX(r, pos).Should().BeApproximately(MaxResolvedX(baseline, pos), 1e-4f);
    }

    // ---- additive edits grow the patch past the box ----

    [Fact]
    public void AddingOutOfBoxColumn_GrowsPatch_AndExtendsBoundary()
    {
        var (pos, tris) = Sheet(N);
        var baseline = InducedBaseline(pos, tris);
        MaxResolvedX(baseline, pos).Should().BeApproximately(5f, 1e-4f); // induced right edge at i=5

        // Force in the out-of-box x=6 column rows 1..5; cells (5,1..4) then have all-member triangles.
        var addCol = Enumerable.Range(1, 5).Select(j => Idx(6, j));
        var r = ResolveWithEdits(pos, tris, InteriorBox(), add: addCol);

        r.IsValid.Should().BeTrue(r.Diagnostic);
        r.LoopCount.Should().Be(1);                                  // still one outer loop, just wider
        TriCount(r).Should().BeGreaterThan(TriCount(baseline));
        MaxResolvedX(r, pos).Should().BeApproximately(6f, 1e-4f);    // boundary extended to the added column
    }

    // ---- subtractive edits carve the patch ----

    [Fact]
    public void RemovingInteriorVertex_OpensAHole_ExtraLoop()
    {
        var (pos, tris) = Sheet(N);
        var baseline = InducedBaseline(pos, tris);
        baseline.LoopCount.Should().Be(1);

        int center = Idx(3, 3); // strictly interior to the induced patch, surrounded by member cells
        var r = ResolveWithEdits(pos, tris, InteriorBox(), remove: new[] { center });

        r.IsValid.Should().BeTrue(r.Diagnostic);
        r.LoopCount.Should().Be(2);                                  // outer loop + the hole around the removed vertex
        TriCount(r).Should().BeLessThan(TriCount(baseline));
        // The removed vertex's position is no longer referenced by any patch vertex.
        r.Vertices.Any(v => (v.Evaluate(pos) - pos[center]).LengthSquared < 1e-6f).Should().BeFalse();
    }

    // ---- tagged wireframe (edit recoloring) ----

    [Fact]
    public void BuildTaggedWireframe_FlagsEdgesTouchingAddedVertices()
    {
        var (pos, tris) = Sheet(N);
        var addCol = Enumerable.Range(1, 5).Select(j => Idx(6, j)).ToHashSet();
        var r = ResolveWithEdits(pos, tris, InteriorBox(), add: addCol);
        r.IsValid.Should().BeTrue(r.Diagnostic);

        var tagged = RegionVolumeEvaluator.BuildTaggedWireframe(r, pos, addCol);

        tagged.Edges.Should().NotBeEmpty();
        tagged.AverageEdgeLength.Should().BeGreaterThan(0f);
        // The added column formed triangles, so those indices appear in the patch and get tagged.
        tagged.PresentAddedIndices.Should().NotBeEmpty();
        tagged.PresentAddedIndices.Should().BeSubsetOf(addCol);
        // At least one edge has a tagged (added) endpoint, and untouched interior edges stay untagged.
        tagged.Edges.Any(e => e.AAdded || e.BAdded).Should().BeTrue();
        tagged.Edges.Any(e => !e.AAdded && !e.BAdded).Should().BeTrue();
    }

    [Fact]
    public void BuildTaggedWireframe_NoAddedSet_TagsNothing()
    {
        var (pos, tris) = Sheet(N);
        var r = InducedBaseline(pos, tris); // forced induced, but no out-of-box adds
        var tagged = RegionVolumeEvaluator.BuildTaggedWireframe(r, pos, new HashSet<int>());

        tagged.Edges.Should().NotBeEmpty();
        tagged.PresentAddedIndices.Should().BeEmpty();
        tagged.Edges.All(e => !e.AAdded && !e.BAdded).Should().BeTrue();
    }

    // ---- position matcher ----

    [Fact]
    public void MatchVertexEdits_MatchesByPosition_SplitsBySign()
    {
        var (pos, _) = Sheet(N);
        var edits = new List<VertexEditRef>
        {
            new VertexEditRef(pos[Idx(6, 1)], additive: true),
            new VertexEditRef(pos[Idx(3, 3)], additive: false),
        };
        var m = MatchVertexEdits(pos, edits, eps: 1e-3f);

        m.MissCount.Should().Be(0);
        m.AddIndices.Should().BeEquivalentTo(new[] { Idx(6, 1) });
        m.RemoveIndices.Should().BeEquivalentTo(new[] { Idx(3, 3) });
    }

    [Fact]
    public void MatchVertexEdits_StaleHint_FallsBackToPositionSearch()
    {
        var (pos, _) = Sheet(N);
        // Hint points at the wrong vertex, but the stored position is the target's — must still match it.
        int target = Idx(4, 2);
        var edits = new List<VertexEditRef> { new VertexEditRef(pos[target], additive: true, indexHint: 3) };
        var m = MatchVertexEdits(pos, edits, eps: 1e-3f);

        m.MissCount.Should().Be(0);
        m.AddIndices.Should().BeEquivalentTo(new[] { target });
    }

    [Fact]
    public void MatchVertexEdits_NoVertexWithinEps_CountsMiss_DoesNotDropSilently()
    {
        var (pos, _) = Sheet(N);
        var edits = new List<VertexEditRef> { new VertexEditRef(new Vector3(100, 100, 100), additive: true) };
        var m = MatchVertexEdits(pos, edits, eps: 1e-3f);

        m.MissCount.Should().Be(1);
        m.IsEmpty.Should().BeTrue();
    }

    // ---- renumber survival (the persistence invariant) ----

    [Fact]
    public void Edits_SurviveAVertexRenumbering_ViaPositionMatch()
    {
        var (pos, tris) = Sheet(N);
        var box = InteriorBox();

        // Curated edits captured as zeroed-space positions on the original mesh: grow a column, carve a hole.
        var edits = Enumerable.Range(1, 5).Select(j => new VertexEditRef(pos[Idx(6, j)], additive: true)).ToList();
        edits.Add(new VertexEditRef(pos[Idx(3, 3)], additive: false));

        // Reference resolve on the ORIGINAL numbering.
        var refMatch = MatchVertexEdits(pos, edits, 1e-3f);
        var reference = ResolveWithEdits(pos, tris, box, refMatch.AddIndices, refMatch.RemoveIndices);
        reference.IsValid.Should().BeTrue(reference.Diagnostic);

        // Renumber: reverse the vertex array and remap triangle indices. Same geometry, new indices.
        int n = pos.Length;
        var shuffledPos = new Vector3[n];
        for (int i = 0; i < n; i++) shuffledPos[i] = pos[n - 1 - i];
        var shuffledTris = tris.Select(i => n - 1 - i).ToArray();

        // The stored positions must re-match the renumbered vertices and reproduce the same topology.
        var match = MatchVertexEdits(shuffledPos, edits, 1e-3f);
        match.MissCount.Should().Be(0);
        var r = ResolveWithEdits(shuffledPos, shuffledTris, box, match.AddIndices, match.RemoveIndices);

        r.IsValid.Should().BeTrue(r.Diagnostic);
        r.LoopCount.Should().Be(reference.LoopCount);
        r.PatchTriangles.Length.Should().Be(reference.PatchTriangles.Length);
        MaxResolvedX(r, shuffledPos).Should().BeApproximately(MaxResolvedX(reference, pos), 1e-4f);
    }
}
