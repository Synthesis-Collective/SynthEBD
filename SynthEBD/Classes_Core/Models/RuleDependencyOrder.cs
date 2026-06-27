using System.Collections.Generic;

namespace SynthEBD;

/// <summary>
/// Topological sort of <see cref="MeasurementRule"/> instances by descriptor dependencies.
/// A rule R depends on rule R' when any of R's conditions is a
/// <see cref="MeasurementConditionKind.DescriptorRef"/> referencing the descriptor R'
/// produces — R must fire AFTER R' so the matched-descriptor set is populated before R's
/// predicate runs.
///
/// Used by <see cref="BodySlideMeasurementEvaluator.Evaluate"/> at scan time and by the
/// editor's cache-driven <c>DeriveDescriptorsFor</c> at rule-edit re-derive time. The UI
/// (<c>VM_MeasurementCondition.AvailableDescriptorRefValues</c>) prevents cycles at edit
/// time so a normal run produces a complete sort, but hand-edited JSON can still
/// hand-author a cycle — the sort drops cycled rules into <c>skipped</c> rather than
/// throwing, so the user gets best-effort results plus a log line.
///
/// Stateless / pure: same input list always produces the same ordered output.
/// </summary>
public static class RuleDependencyOrder
{
    /// <summary>
    /// Returns <paramref name="rules"/> sorted so every rule R comes after any rule that
    /// produces a descriptor R references via a <see cref="MeasurementConditionKind.DescriptorRef"/>
    /// condition. Rules with no DescriptorRef conditions retain their relative input order
    /// (Kahn's algorithm with FIFO ready-queue) so the legacy "declaration order" stays
    /// the tiebreaker for stability.
    ///
    /// A <b>cross-category</b> DescriptorRef (R references a different Category than R produces)
    /// is ordered after <em>every</em> producer of the referenced Category — not just the single
    /// (Category, Value) it names — so the Category is fully resolved before R runs. This is what
    /// lets <see cref="BodySlideMeasurementEvaluator.RunClassifierRules"/> materialize that
    /// Category's default into the matched set before R, so a positive ref to a default-only value
    /// (an explicit rule disabled in favor of the per-Category default) fires. <b>Intra-category</b>
    /// refs keep the narrow (Category, Value) dependency to avoid a rule depending on its own
    /// Category (which would include itself and land in <paramref name="skipped"/>).
    ///
    /// <paramref name="skipped"/> receives any rules that couldn't be placed because they
    /// were part of a cycle (or transitively depended on a cycle). Callers should treat
    /// these as inert for evaluation — emitting them in arbitrary order would write
    /// descriptors that downstream readers can't trust. Empty list when the graph is acyclic.
    /// </summary>
    public static List<MeasurementRule> SortByDescriptorDependencies(
        IReadOnlyList<MeasurementRule> rules,
        out List<MeasurementRule> skipped)
    {
        skipped = new List<MeasurementRule>();
        if (rules == null || rules.Count == 0) return new List<MeasurementRule>();

        // Build the descriptor-dependency graph. The cross- vs intra-category edge rule lives in
        // the shared BuildDependencyGraph helper so SortByDescriptorDependencies and
        // WouldCreateCycle can never disagree on what an edge means.
        var (_, _, dependsOn) = BuildDependencyGraph(rules);

        // Kahn's algorithm. In-degree starts as |dependsOn[i]|. Walk rules in input order
        // pushing zero-in-degree ones into a queue; pop, append to output, decrement
        // in-degree of dependents. When the queue empties any remaining non-zero rules
        // are part of a cycle.
        var inDegree = new int[rules.Count];
        var dependents = new List<List<int>>(rules.Count);
        for (int i = 0; i < rules.Count; i++) dependents.Add(new List<int>());
        for (int i = 0; i < rules.Count; i++)
        {
            inDegree[i] = dependsOn[i].Count;
            foreach (var depIdx in dependsOn[i]) dependents[depIdx].Add(i);
        }

        var queue = new Queue<int>();
        for (int i = 0; i < rules.Count; i++)
        {
            if (inDegree[i] == 0) queue.Enqueue(i);
        }

        var ordered = new List<MeasurementRule>(rules.Count);
        var placed = new bool[rules.Count];
        while (queue.Count > 0)
        {
            int idx = queue.Dequeue();
            ordered.Add(rules[idx]);
            placed[idx] = true;
            foreach (var dep in dependents[idx])
            {
                inDegree[dep]--;
                if (inDegree[dep] == 0) queue.Enqueue(dep);
            }
        }

        for (int i = 0; i < rules.Count; i++)
        {
            if (!placed[i]) skipped.Add(rules[i]);
        }

        return ordered;
    }

    /// <summary>
    /// Builds the descriptor-dependency graph shared by <see cref="SortByDescriptorDependencies"/>
    /// and <see cref="WouldCreateCycle"/>, so the two never drift on the cross- vs intra-category
    /// edge rule. Returns:
    /// <list type="bullet">
    /// <item><description><c>ByValue</c>: producers indexed by (Category, Value).</description></item>
    /// <item><description><c>ByCategory</c>: producers indexed by Category alone.</description></item>
    /// <item><description><c>DependsOn</c>: forward adjacency — <c>DependsOn[i]</c> is the set of
    /// rule indices rule <c>i</c> must run after.</description></item>
    /// </list>
    /// A <b>cross-category</b> DescriptorRef (the rule references a different Category than it
    /// produces) depends on EVERY producer of the referenced Category, so the rule is ordered after
    /// that Category is fully resolved (and its default materialized — see
    /// <see cref="BodySlideMeasurementEvaluator.RunClassifierRules"/>). An <b>intra-category</b> ref
    /// keeps the narrow (Category, Value) dependency so a within-category aggregator doesn't depend
    /// on its own Category (which would include itself and form a cycle). Cross-category producers
    /// can never include the referencing rule itself (different Category), so they add no self-edge.
    /// </summary>
    private static (Dictionary<(string Category, string Value), List<int>> ByValue,
                    Dictionary<string, List<int>> ByCategory,
                    List<HashSet<int>> DependsOn)
        BuildDependencyGraph(IReadOnlyList<MeasurementRule> rules)
    {
        var producers = new Dictionary<(string Category, string Value), List<int>>();
        var producersByCategory = new Dictionary<string, List<int>>();
        for (int i = 0; i < rules.Count; i++)
        {
            var r = rules[i];
            if (r?.Descriptor == null) continue;
            var key = (r.Descriptor.Category ?? "", r.Descriptor.Value ?? "");
            if (string.IsNullOrEmpty(key.Item1) || string.IsNullOrEmpty(key.Item2)) continue;
            if (!producers.TryGetValue(key, out var list))
            {
                list = new List<int>();
                producers[key] = list;
            }
            list.Add(i);
            if (!producersByCategory.TryGetValue(key.Item1, out var catList))
            {
                catList = new List<int>();
                producersByCategory[key.Item1] = catList;
            }
            catList.Add(i);
        }

        // Adjacency: for each rule i, the set of rule indices it depends on. Edge i → j means
        // "j must come before i" (i references something j produces). Self-edges (a rule whose
        // intra-category DescriptorRef targets its own (Cat, Val)) are tolerated by Kahn's
        // algorithm — the rule never reaches in-degree zero and ends up in `skipped`.
        var dependsOn = new List<HashSet<int>>(rules.Count);
        for (int i = 0; i < rules.Count; i++) dependsOn.Add(new HashSet<int>());

        for (int i = 0; i < rules.Count; i++)
        {
            var r = rules[i];
            if (r?.GroupsORlogic == null) continue;
            var ownCat = r.Descriptor?.Category ?? "";
            foreach (var group in r.GroupsORlogic)
            {
                if (group?.ConditionsANDlogic == null) continue;
                foreach (var cond in group.ConditionsANDlogic)
                {
                    if (cond == null) continue;
                    if (cond.Kind != MeasurementConditionKind.DescriptorRef) continue;
                    var refCat = cond.RefCategory ?? "";
                    var refVal = cond.RefValue ?? "";
                    if (string.IsNullOrEmpty(refCat) || string.IsNullOrEmpty(refVal)) continue;

                    bool crossCategory = !string.Equals(refCat, ownCat, System.StringComparison.Ordinal);
                    if (crossCategory && producersByCategory.TryGetValue(refCat, out var catProducers))
                    {
                        foreach (var producerIdx in catProducers) dependsOn[i].Add(producerIdx);
                    }
                    else if (producers.TryGetValue((refCat, refVal), out var producerIdxs))
                    {
                        foreach (var producerIdx in producerIdxs) dependsOn[i].Add(producerIdx);
                    }
                }
            }
        }

        return (producers, producersByCategory, dependsOn);
    }

    /// <summary>
    /// True when adding a <see cref="MeasurementConditionKind.DescriptorRef"/> from rule
    /// <paramref name="ruleIdx"/> targeting descriptor <paramref name="refCategory"/> /
    /// <paramref name="refValue"/> would create a cycle in the dependency graph.
    /// Used by the editor to pre-filter the Value dropdown so the user can't pick a
    /// cycle-inducing target. O(rules + edges) per call via DFS reachability.
    ///
    /// Mirrors <see cref="SortByDescriptorDependencies"/>'s edge model (both build the graph via
    /// <see cref="BuildDependencyGraph"/>): the proposed edge is CROSS-category when
    /// <paramref name="refCategory"/> differs from the rule's own Category, in which case it targets
    /// EVERY producer of <paramref name="refCategory"/> — not just the named Value — so the editor
    /// grays out exactly the choices the sort would otherwise drop as cyclic. An intra-category ref
    /// targets only producers of the specific (Category, Value).
    ///
    /// Algorithm: adding edge ruleIdx → target creates a cycle iff the existing graph already has a
    /// path target → ... → ruleIdx along depends-on edges. DFS from each target producer.
    /// </summary>
    public static bool WouldCreateCycle(
        IReadOnlyList<MeasurementRule> rules,
        int ruleIdx,
        string refCategory,
        string refValue)
    {
        if (rules == null || ruleIdx < 0 || ruleIdx >= rules.Count) return false;
        if (string.IsNullOrEmpty(refCategory) || string.IsNullOrEmpty(refValue)) return false;

        var (producersByValue, producersByCategory, dependsOn) = BuildDependencyGraph(rules);

        // The producers the proposed edge would point at — the SAME set SortByDescriptorDependencies
        // would wire up. Cross-category: every producer of refCategory; intra-category: producers of
        // the specific (refCategory, refValue).
        var ownCat = rules[ruleIdx]?.Descriptor?.Category ?? "";
        bool crossCategory = !string.Equals(refCategory, ownCat, System.StringComparison.Ordinal);
        List<int> targetProducers = null;
        if (crossCategory) producersByCategory.TryGetValue(refCategory, out targetProducers);
        else producersByValue.TryGetValue((refCategory, refValue), out targetProducers);

        // Referencing a descriptor/Category no rule produces — the sort adds no edge for it (the
        // Category's default, if any, materializes without a graph edge), so no cycle is possible.
        if (targetProducers == null || targetProducers.Count == 0) return false;

        // Self-reference (an intra-category ref to the rule's own descriptor) always cycles.
        if (targetProducers.Contains(ruleIdx)) return true;

        // DFS from each target producer walking depends-on edges; reaching ruleIdx means the existing
        // graph has target → ... → ruleIdx, so the new ruleIdx → target edge closes the loop.
        //
        // Walk depends-on (forward) edges, NOT dependents — walking dependents finds rules that
        // already depend on the producer (the wrong direction) and over-flags redundant parallel
        // DescriptorRef chains pointing at the same target as cycles, blanking valid combobox choices.
        var visited = new bool[rules.Count];
        var stack = new Stack<int>();
        foreach (var p in targetProducers) stack.Push(p);
        while (stack.Count > 0)
        {
            int cur = stack.Pop();
            if (visited[cur]) continue;
            visited[cur] = true;
            if (cur == ruleIdx) return true;
            foreach (var d in dependsOn[cur])
            {
                if (!visited[d]) stack.Push(d);
            }
        }
        return false;
    }
}
