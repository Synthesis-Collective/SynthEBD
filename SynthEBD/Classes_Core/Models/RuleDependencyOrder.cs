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

        // Forward adjacency: i → j means i depends on j (i references something j produces).
        // We need to know: starting from a producer P, can we reach ruleIdx via forward edges?
        // Equivalently: is ruleIdx in the set of rules that P transitively depends on... no wait,
        // edges are "i depends on j" meaning when adding ruleIdx → P, the cycle goes
        // ruleIdx → P → ... → ruleIdx, i.e. we need a path P → ... → ruleIdx where each step
        // is "earlier rule depends on later rule"... actually the dependency direction is
        // ruleIdx depends on P, so the edge is ruleIdx → P. For a cycle, we need a path
        // P → ... → ruleIdx in the SAME direction. That means we need to find rules
        // that depend on P (transitively) and check if ruleIdx is among them.
        //
        // Build reverse adjacency from producer index lookups: for each rule R, which other
        // rules' DescriptorRef conditions does R satisfy (i.e. R is a producer they depend
        // on)? That gives us forward reachability from any producer.

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

        // For each rule R, the set of producer rule indices it depends on directly.
        // i depends on j → edge i → j → ... if traversing in dependency direction we hit ruleIdx then cycle.
        // We're checking: starting from each producer P of (refCategory, refValue), can the
        // dependency graph (following edges 'i depends on j') eventually arrive at ruleIdx?
        // Equivalent: build dependents-of-X map (X is depended on by Y1, Y2, ...). DFS from
        // each producer P forward through dependents to see if ruleIdx is reachable.
        var dependents = new List<List<int>>(rules.Count);
        for (int i = 0; i < rules.Count; i++) dependents.Add(new List<int>());
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
                    foreach (var producerIdx in producerIdxs)
                    {
                        // producerIdx is depended on by i → producerIdx's dependents include i.
                        dependents[producerIdx].Add(i);
                    }
                }
            }
        }

        // DFS from each producer of the target descriptor; if we can walk dependents and
        // reach ruleIdx, the proposed new edge creates a cycle.
        var visited = new bool[rules.Count];
        var stack = new Stack<int>();
        foreach (var p in producers) stack.Push(p);
        while (stack.Count > 0)
        {
            int cur = stack.Pop();
            if (visited[cur]) continue;
            visited[cur] = true;
            if (cur == ruleIdx) return true;
            foreach (var d in dependents[cur])
            {
                if (!visited[d]) stack.Push(d);
            }
        }
        return false;
    }
}
