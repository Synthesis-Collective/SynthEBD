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

        // Build producer index: (Category, Value) → list of rules that emit that descriptor.
        // A descriptor can have multiple producers (alternative rule paths to the same label).
        var producers = new Dictionary<(string Category, string Value), List<int>>();
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
        }

        // Adjacency: for each rule i, the set of rule indices it depends on.
        // Edge i → j means "j must come before i" (i references something j produces).
        // Self-edges (a rule that depends on its own descriptor via a DescriptorRef to its
        // own (Cat, Val)) are tolerated by Kahn's algorithm — the rule has in-degree ≥ 1
        // from itself, never reaches zero, and ends up in <paramref name="skipped"/>.
        var dependsOn = new List<HashSet<int>>(rules.Count);
        for (int i = 0; i < rules.Count; i++) dependsOn.Add(new HashSet<int>());

        for (int i = 0; i < rules.Count; i++)
        {
            var r = rules[i];
            if (r?.GroupsORlogic == null) continue;
            foreach (var group in r.GroupsORlogic)
            {
                if (group?.ConditionsANDlogic == null) continue;
                foreach (var cond in group.ConditionsANDlogic)
                {
                    if (cond == null) continue;
                    if (cond.Kind != MeasurementConditionKind.DescriptorRef) continue;
                    var refKey = (cond.RefCategory ?? "", cond.RefValue ?? "");
                    if (string.IsNullOrEmpty(refKey.Item1) || string.IsNullOrEmpty(refKey.Item2)) continue;
                    if (!producers.TryGetValue(refKey, out var producerIdxs)) continue;
                    foreach (var producerIdx in producerIdxs)
                    {
                        dependsOn[i].Add(producerIdx);
                    }
                }
            }
        }

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
    /// True when adding a <see cref="MeasurementConditionKind.DescriptorRef"/> from rule
    /// <paramref name="ruleIdx"/> targeting descriptor <paramref name="refCategory"/> /
    /// <paramref name="refValue"/> would create a cycle in the dependency graph.
    /// Used by the editor to pre-filter the Value dropdown so the user can't pick a
    /// cycle-inducing target. O(rules + edges) per call via DFS reachability.
    ///
    /// Algorithm: a new edge ruleIdx → producer creates a cycle iff there's an existing
    /// path producer → ... → ruleIdx. Run DFS from each producer of (refCategory, refValue)
    /// looking for ruleIdx.
    /// </summary>
    public static bool WouldCreateCycle(
        IReadOnlyList<MeasurementRule> rules,
        int ruleIdx,
        string refCategory,
        string refValue)
    {
        if (rules == null || ruleIdx < 0 || ruleIdx >= rules.Count) return false;
        if (string.IsNullOrEmpty(refCategory) || string.IsNullOrEmpty(refValue)) return false;

        // Find producers of the target descriptor.
        var producers = new List<int>();
        for (int i = 0; i < rules.Count; i++)
        {
            var r = rules[i];
            if (r?.Descriptor == null) continue;
            if (string.Equals(r.Descriptor.Category, refCategory, System.StringComparison.Ordinal)
                && string.Equals(r.Descriptor.Value, refValue, System.StringComparison.Ordinal))
            {
                producers.Add(i);
            }
        }
        if (producers.Count == 0) return false; // referencing a descriptor no rule produces — no cycle (yet)

        // Self-reference always creates a cycle.
        if (producers.Contains(ruleIdx)) return true;

        // Edge i → j means "i depends on j" (i has a DescriptorRef pointing to j's descriptor;
        // at evaluation time j must run before i). Adding the proposed edge ruleIdx → producer
        // creates a cycle iff the existing graph already has a path producer → ... → ruleIdx
        // walked in the SAME (depends-on) direction. We DFS from each producer along
        // depends-on edges (forward direction) and report a cycle if we reach ruleIdx.
        //
        // Subtle: walking through "dependents" (rules that reference X) is the WRONG
        // direction — that finds rules that are transitive PREDECESSORS of the producer
        // (i.e., already depend on it), not rules the producer depends on. The earlier
        // version of this function walked dependents and over-flagged "redundant parallel
        // path" edges as cycles, blanking valid combobox choices in the rule editor when a
        // body type profile contained two DescriptorRef chains pointing to the same target.

        // Pre-index: producer key → producer rule indices.
        var producerIndex = new Dictionary<(string Cat, string Val), List<int>>();
        for (int i = 0; i < rules.Count; i++)
        {
            var r = rules[i];
            if (r?.Descriptor == null) continue;
            (string Cat, string Val) key = (r.Descriptor.Category ?? "", r.Descriptor.Value ?? "");
            if (string.IsNullOrEmpty(key.Cat) || string.IsNullOrEmpty(key.Val)) continue;
            if (!producerIndex.TryGetValue(key, out var list))
            {
                list = new List<int>();
                producerIndex[key] = list;
            }
            list.Add(i);
        }

        // Forward adjacency: dependsOn[i] = producer rule indices that rule i directly
        // depends on via its own DescriptorRef conditions.
        var dependsOn = new List<HashSet<int>>(rules.Count);
        for (int i = 0; i < rules.Count; i++) dependsOn.Add(new HashSet<int>());
        for (int i = 0; i < rules.Count; i++)
        {
            var r = rules[i];
            if (r?.GroupsORlogic == null) continue;
            foreach (var group in r.GroupsORlogic)
            {
                if (group?.ConditionsANDlogic == null) continue;
                foreach (var cond in group.ConditionsANDlogic)
                {
                    if (cond == null) continue;
                    if (cond.Kind != MeasurementConditionKind.DescriptorRef) continue;
                    var refKey = (cond.RefCategory ?? "", cond.RefValue ?? "");
                    if (string.IsNullOrEmpty(refKey.Item1) || string.IsNullOrEmpty(refKey.Item2)) continue;
                    if (!producerIndex.TryGetValue(refKey, out var producerIdxs)) continue;
                    foreach (var producerIdx in producerIdxs) dependsOn[i].Add(producerIdx);
                }
            }
        }

        // DFS from each producer of the target descriptor walking depends-on edges; reach
        // ruleIdx → adding ruleIdx → producer closes the cycle producer → ... → ruleIdx → producer.
        var visited = new bool[rules.Count];
        var stack = new Stack<int>();
        foreach (var p in producers) stack.Push(p);
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
