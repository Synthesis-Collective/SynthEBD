namespace SynthEBD;

/// <summary>
/// Per-NPC tally of matched ForceIf attribute weights, keyed by candidate object reference
/// (<see cref="FlattenedSubgroup"/>, <see cref="BodyGenConfig.BodyGenTemplate"/>, <see cref="BodySlideSetting"/>,
/// head-part settings, ...). Replaces the <c>MatchedForceIfCount</c>/<c>ForceIfMatchCount</c> scratch properties
/// that previously lived on the shared, config-owned candidate objects themselves, where per-NPC writes made
/// parallel NPC selection unsafe (R19). Lifetime: one <see cref="NPCInfo"/>, created fresh per NPC.
/// </summary>
public class ForceIfMatchTally
{
    private readonly Dictionary<object, int> _counts = new(ReferenceEqualityComparer.Instance);

    /// <summary>Overwrites the candidate's tally (candidates are re-evaluated from scratch on each validation pass).</summary>
    public void Set(object candidate, int count) => _counts[candidate] = count;

    /// <summary>Adds to the candidate's tally (descriptor-rule matches accumulate on top of the base attribute match).</summary>
    public void Add(object candidate, int count) => _counts[candidate] = Get(candidate) + count;

    /// <summary>The candidate's matched ForceIf weight for this NPC; 0 if the candidate was never evaluated.</summary>
    public int Get(object candidate) => _counts.TryGetValue(candidate, out var count) ? count : 0;
}
