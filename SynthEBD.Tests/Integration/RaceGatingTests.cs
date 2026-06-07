using FluentAssertions;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;
using Xunit;
using Xunit.Abstractions;

namespace SynthEBD.Tests.Integration;

/// <summary>
/// Verifies race-based distribution gating: allowed/disallowed explicit races (including vampire
/// equivalents) and allowed/disallowed race groupings. A single assets-only run carries one gated leaf per
/// rule; each leaf's assigned NPCs are read from the combination log and checked against the rule, with a
/// coverage check that the rule actually admitted NPCs.
/// </summary>
[Collection(PatcherIntegrationCollection.Name)]
public class RaceGatingTests
{
    private readonly ITestOutputHelper _output;
    private readonly WpfApplicationFixture _wpf;

    public RaceGatingTests(ITestOutputHelper output, WpfApplicationFixture wpf)
    {
        _output = output;
        _wpf = wpf;
    }

    private static readonly string[] Groupings =
    {
        "Nord", "Breton", "Imperial", "Redguard", "Dark Elf", "High Elf",
        "Wood Elf", "Orc", "Argonian", "Khajiit", "Elder",
    };

    private static readonly FormKey NordRace = FormKey.Factory("013746:Skyrim.esm");
    private static readonly FormKey NordRaceVampire = FormKey.Factory("088794:Skyrim.esm");

    // All vanilla playable vampire races.
    private static readonly HashSet<FormKey> VampireRaces = new[]
    {
        "08883A", "08883C", "08883D", "088840", "088844", "088845",
        "088794", "0A82B9", "088846", "088884", "0A82BA",
    }.Select(x => FormKey.Factory(x + ":Skyrim.esm")).ToHashSet();

    [Fact]
    public async Task RaceAndGroupingRules_GateAssignmentsCorrectly()
    {
        await _wpf.RunOnStaAsync(async () =>
        {
            var harness = PatcherTestHarness.TryCreate(out var skipReason);
            if (harness is null) { _output.WriteLine("SKIPPED: " + skipReason); return; }

            using (harness)
            {
                var leaves = new List<AssetScenario.Leaf>();
                foreach (var g in Groupings)
                {
                    leaves.Add(new AssetScenario.Leaf { Id = "ALLOWG." + g, AllowedRaceGroupings = new() { g } });
                }
                leaves.Add(new AssetScenario.Leaf { Id = "ALLOWR.Nord", AllowedRaces = new() { NordRace, NordRaceVampire } });
                leaves.Add(new AssetScenario.Leaf { Id = "ALLOWVAMP", AllowedRaces = new(VampireRaces) });
                leaves.Add(new AssetScenario.Leaf { Id = "DISG.Nord", DisallowedRaceGroupings = new() { "Nord" } });
                leaves.Add(new AssetScenario.Leaf { Id = "DISR.Nord", DisallowedRaces = new() { NordRace, NordRaceVampire } });
                leaves.Add(new AssetScenario.Leaf { Id = "FALLBACK" }); // ungated, keeps every NPC assignable

                harness.UseAssetScenario(AssetScenario.BuildBothGenders("Race Gating", leaves));
                await harness.RunAsync();

                // ── Allowed race groupings ──
                foreach (var g in Groupings)
                {
                    var members = GroupingMembers(harness, g);
                    var assigned = harness.NpcsAssignedSubgroup("ALLOWG." + g);
                    assigned.Should().NotBeEmpty($"grouping '{g}' should admit some NPCs (coverage)");
                    assigned.Should().OnlyContain(npc => members.Contains(npc.Race.FormKey),
                        $"only NPCs whose race is in grouping '{g}' should get its subgroup");
                }

                // ── Allowed explicit races (base + vampire FormKeys) ──
                var allowR = harness.NpcsAssignedSubgroup("ALLOWR.Nord");
                allowR.Should().NotBeEmpty();
                allowR.Should().OnlyContain(npc => npc.Race.FormKey == NordRace || npc.Race.FormKey == NordRaceVampire,
                    "explicit allowed-races gating should admit only the listed Nord races");

                // ── Vampire-race assignment specifically ──
                var vamp = harness.NpcsAssignedSubgroup("ALLOWVAMP");
                vamp.Should().OnlyContain(npc => VampireRaces.Contains(npc.Race.FormKey),
                    "the vampire-only subgroup must never be assigned to a non-vampire race");
                vamp.Should().NotBeEmpty("at least one vampire-race NPC should exist and be assigned (vampire-equivalent coverage)");

                // ── Disallowed race grouping ──
                var nordMembers = GroupingMembers(harness, "Nord");
                var disG = harness.NpcsAssignedSubgroup("DISG.Nord");
                disG.Should().NotBeEmpty();
                disG.Should().OnlyContain(npc => !nordMembers.Contains(npc.Race.FormKey),
                    "the Nord-disallowed-grouping subgroup must never be assigned to a Nord");

                // ── Disallowed explicit races ──
                var disR = harness.NpcsAssignedSubgroup("DISR.Nord");
                disR.Should().NotBeEmpty();
                disR.Should().OnlyContain(npc => npc.Race.FormKey != NordRace && npc.Race.FormKey != NordRaceVampire,
                    "the Nord-disallowed-races subgroup must never be assigned to the listed Nord races");

                _output.WriteLine($"Race gating verified. Vampire-race assignments: {vamp.Count}.");
            }
        });
    }

    private static HashSet<FormKey> GroupingMembers(PatcherTestHarness harness, string label)
    {
        var grouping = harness.PatcherState.GeneralSettings.RaceGroupings.FirstOrDefault(x => x.Label == label);
        grouping.Should().NotBeNull($"default race grouping '{label}' should exist in settings");
        return grouping!.Races.ToHashSet();
    }
}
