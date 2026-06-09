using FluentAssertions;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;
using Xunit;
using Xunit.Abstractions;

namespace SynthEBD.Tests.Integration;

/// <summary>
/// Verifies two structural distribution behaviors:
/// <list type="bullet">
/// <item>Whole-config distribution rules gate an entire asset pack: if an NPC fails the config's rules,
/// none of the pack's subgroups are eligible.</item>
/// <item>Nested subgroup rule inheritance: a leaf inherits its parent position's rules (intersection for
/// allowed races), so a leaf with no race rule is restricted to the parent's races, and a leaf whose own
/// race rule is disjoint from the parent's is pruned entirely.</item>
/// </list>
/// </summary>
[Collection(PatcherIntegrationCollection.Name)]
public class ConfigRulesAndInheritanceTests
{
    private readonly ITestOutputHelper _output;
    private readonly WpfApplicationFixture _wpf;

    public ConfigRulesAndInheritanceTests(ITestOutputHelper output, WpfApplicationFixture wpf)
    {
        _output = output;
        _wpf = wpf;
    }

    [Fact]
    public async Task WholeConfigRules_AndNestedInheritance_GateAssignments()
    {
        await _wpf.RunOnStaAsync(async () =>
        {
            var harness = PatcherTestHarness.TryCreate(out var skipReason);
            if (harness is null) { _output.WriteLine("SKIPPED: " + skipReason); return; }

            using (harness)
            {
                var nordMembers = harness.PatcherState.GeneralSettings.RaceGroupings.First(g => g.Label == "Nord").Races.ToHashSet();

                // Pack 1: gated at the whole-config level to the "Nord" grouping label (a General race grouping); its
                // single leaf carries no rule of its own. (B48: config-level rules now resolve grouping labels against the
                // full effective set -- the synthetic pack's empty local RaceGroupings fall back to General -- so a
                // config-level AllowedRaceGroupings label is honored, where it was previously a silent no-op.)
                var configPack = AssetScenario.BuildPack("Config Gated", Gender.Female,
                    new[] { new AssetScenario.Leaf { Id = "CFG.leaf" } });
                configPack.DistributionRules.AllowedRaceGroupings.Add("Nord");

                // Pack 2: the position (parent) is gated to Nord. INH.ok has no race rule (inherits Nord);
                // INH.imperial requires Imperial, which is disjoint from the inherited Nord -> pruned.
                var inheritPack = AssetScenario.BuildPack("Inheritance", Gender.Female,
                    new[]
                    {
                        new AssetScenario.Leaf { Id = "INH.ok" },
                        new AssetScenario.Leaf { Id = "INH.imperial", AllowedRaceGroupings = new() { "Imperial" } },
                    },
                    parentRules: new AssetScenario.Leaf { AllowedRaceGroupings = new() { "Nord" } });

                harness.UseAssetScenario(new[] { configPack, inheritPack });
                await harness.RunAsync();

                // ── Whole-config gating ──
                var cfg = harness.NpcsAssignedSubgroup("CFG.leaf");
                cfg.Should().NotBeEmpty("the config-gated pack should assign to Nord NPCs");
                cfg.Should().OnlyContain(npc => nordMembers.Contains(npc.Race.FormKey),
                    "a config gated to Nord must not assign any of its subgroups to non-Nords");

                // ── Nested inheritance: leaf inherits the parent's Nord restriction ──
                var ok = harness.NpcsAssignedSubgroup("INH.ok");
                ok.Should().NotBeEmpty("the rule-less child should be assigned (to its inherited Nord races)");
                ok.Should().OnlyContain(npc => nordMembers.Contains(npc.Race.FormKey),
                    "the child leaf should inherit the parent position's Nord restriction");

                // ── Intersection pruning: child Imperial disjoint from parent Nord -> never assigned ──
                harness.AssignmentCount("INH.imperial").Should().Be(0,
                    "a child whose race rule is disjoint from the inherited parent rule is pruned");

                _output.WriteLine($"Config gating: {cfg.Count}; inherited leaf: {ok.Count}; pruned leaf: {harness.AssignmentCount("INH.imperial")}.");
            }
        });
    }

    [Fact]
    public async Task SubgroupRule_ResolvesAgainstConfigLocalRaceGrouping()
    {
        await _wpf.RunOnStaAsync(async () =>
        {
            var harness = PatcherTestHarness.TryCreate(out var skipReason);
            if (harness is null) { _output.WriteLine("SKIPPED: " + skipReason); return; }

            using (harness)
            {
                var nordMembers = harness.PatcherState.GeneralSettings.RaceGroupings.First(g => g.Label == "Nord").Races.ToHashSet();

                // B48 (Option B): a config ships a LOCAL race grouping the user lacks in General (the share scenario --
                // user A defined it, it was auto-imported into the config, user B does not have it), and a subgroup rule
                // references it by label. Subgroup/replacer flattening now resolves grouping labels against the config's
                // local RaceGroupings too (not only General), so the rule restricts to the local grouping's races instead
                // of silently matching everything. (Pre-B48 the subgroup path used General only -> "B48LocalOnly"
                // resolved to nothing -> no restriction -> assigned to all races.)
                var pack = AssetScenario.BuildPack("Local Grouping", Gender.Female,
                    new[] { new AssetScenario.Leaf { Id = "LOC.leaf", AllowedRaceGroupings = new() { "B48LocalOnly" } } });
                pack.RaceGroupings.Add(new RaceGrouping { Label = "B48LocalOnly", Races = nordMembers.ToHashSet() });

                harness.UseAssetScenario(new[] { pack });
                await harness.RunAsync();

                var loc = harness.NpcsAssignedSubgroup("LOC.leaf");
                loc.Should().NotBeEmpty("the subgroup should assign to the config-local grouping's races");
                loc.Should().OnlyContain(npc => nordMembers.Contains(npc.Race.FormKey),
                    "a subgroup rule referencing a config-LOCAL grouping (absent from General) must restrict to that grouping's races, not match all");
                _output.WriteLine($"Local-grouping subgroup assigned to {loc.Count} NPCs (all within the local grouping's races).");
            }
        });
    }
}
