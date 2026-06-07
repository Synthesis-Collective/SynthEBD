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

                // Pack 1: gated at the whole-config level to the Nord races; its single leaf carries no rule
                // of its own. (Config-level rules resolve grouping labels against the pack's own RaceGroupings
                // list, so explicit race FormKeys are used here.)
                var configPack = AssetScenario.BuildPack("Config Gated", Gender.Female,
                    new[] { new AssetScenario.Leaf { Id = "CFG.leaf" } });
                foreach (var r in nordMembers) { configPack.DistributionRules.AllowedRaces.Add(r); }

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
}
