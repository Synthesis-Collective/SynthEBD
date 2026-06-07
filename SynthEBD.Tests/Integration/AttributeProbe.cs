using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Order;
using Mutagen.Bethesda.Skyrim;

namespace SynthEBD.Tests.Integration;

/// <summary>
/// Scans the live load order once to pick high-coverage, real values for each NPC attribute dimension, so
/// attribute-gating tests can build rules that are guaranteed to match many NPCs (avoiding flaky
/// data-dependent assertions). Values are chosen as the most common non-null value across the winning NPC
/// overrides — the same population the patcher iterates.
/// </summary>
public sealed class AttributeProbe
{
    public FormKey ClassFk { get; private init; }
    public FormKey VoiceFk { get; private init; }
    public FormKey RaceFk { get; private init; }
    public FormKey FaceTextureFk { get; private init; }
    public FormKey KeywordFk { get; private init; }
    public FormKey FactionFk { get; private init; }
    /// <summary>A set of NPC form keys (the scanned, processable NPCs) for exact NPC-attribute gating.</summary>
    public HashSet<FormKey> NpcFks { get; private init; } = new();
    /// <summary>A source plugin that authored a meaningful number of NPCs (for Mod CreatedBy gating).</summary>
    public ModKey SourceMod { get; private init; }

    public static AttributeProbe Scan(PatcherTestHarness harness)
    {
        var npcs = harness.EnvironmentProvider.LoadOrder.PriorityOrder
            .OnlyEnabledAndExisting()
            .WinningOverrides<INpcGetter>()
            .ToArray();

        var classCounts = new Dictionary<FormKey, int>();
        var voiceCounts = new Dictionary<FormKey, int>();
        var raceCounts = new Dictionary<FormKey, int>();
        var faceCounts = new Dictionary<FormKey, int>();
        var keywordCounts = new Dictionary<FormKey, int>();
        var factionCounts = new Dictionary<FormKey, int>();
        var modCounts = new Dictionary<ModKey, int>();
        var npcFks = new List<FormKey>();

        foreach (var npc in npcs)
        {
            Bump(classCounts, npc.Class.FormKey);
            Bump(voiceCounts, npc.Voice.FormKey);
            Bump(raceCounts, npc.Race.FormKey);
            Bump(faceCounts, npc.HeadTexture.FormKey);
            Bump(modCounts, npc.FormKey.ModKey);
            // Collect unique, named NPCs for exact NPC-attribute gating: distinct characters that are not
            // subject to the engine's same-name/duplicate-record assignment reuse, so the gate is exact.
            if (npc.Configuration.Flags.HasFlag(NpcConfiguration.Flag.Unique) && !string.IsNullOrEmpty(npc.EditorID))
            {
                npcFks.Add(npc.FormKey);
            }
            if (npc.Keywords != null)
            {
                foreach (var kw in npc.Keywords) { Bump(keywordCounts, kw.FormKey); }
            }
            foreach (var faction in npc.Factions) { Bump(factionCounts, faction.Faction.FormKey); }
        }

        return new AttributeProbe
        {
            ClassFk = TopNonNull(classCounts),
            VoiceFk = TopNonNull(voiceCounts),
            RaceFk = TopNonNull(raceCounts),
            FaceTextureFk = TopNonNull(faceCounts),
            KeywordFk = TopNonNull(keywordCounts),
            FactionFk = TopNonNull(factionCounts),
            // A generous slice of processable NPCs for exact NPC gating (coverage is the intersection with
            // those that actually pass the patcher's NPC filters).
            NpcFks = npcFks.Take(1000).ToHashSet(),
            // Pick the second-most-prolific source plugin (usually a DLC) rather than the dominant base game,
            // so both "created by this mod" and "not created by this mod" have plenty of NPCs to assign.
            SourceMod = SecondMostCommonMod(modCounts),
        };
    }

    private static void Bump<T>(Dictionary<T, int> counts, T key) where T : notnull
    {
        counts[key] = counts.TryGetValue(key, out var c) ? c + 1 : 1;
    }

    private static FormKey TopNonNull(Dictionary<FormKey, int> counts)
    {
        return counts.Where(x => !x.Key.IsNull).OrderByDescending(x => x.Value)
            .Select(x => x.Key).FirstOrDefault();
    }

    private static ModKey SecondMostCommonMod(Dictionary<ModKey, int> counts)
    {
        var ordered = counts.Where(x => !x.Key.IsNull).OrderByDescending(x => x.Value).Select(x => x.Key).ToArray();
        return ordered.Length > 1 ? ordered[1] : ordered.FirstOrDefault();
    }
}
