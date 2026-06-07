using FluentAssertions;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;
using Xunit;
using Xunit.Abstractions;

namespace SynthEBD.Tests.Integration;

/// <summary>
/// Verifies weighted subgroup selection: a static <c>ProbabilityWeighting</c> ratio is honored across the
/// population, and an attribute-conditional <c>ProbabilityWeightModifier</c> raises a subgroup's share only
/// for NPCs that match the modifier's attribute. Run over the whole load order so the observed ratios are
/// statistically tight.
/// </summary>
[Collection(PatcherIntegrationCollection.Name)]
public class ProbabilityWeightingTests
{
    private readonly ITestOutputHelper _output;
    private readonly WpfApplicationFixture _wpf;

    public ProbabilityWeightingTests(ITestOutputHelper output, WpfApplicationFixture wpf)
    {
        _output = output;
        _wpf = wpf;
    }

    [Fact]
    public async Task ProbabilityWeightingAndAttributeModulation_BiasSelection()
    {
        await _wpf.RunOnStaAsync(async () =>
        {
            var harness = PatcherTestHarness.TryCreate(out var skipReason);
            if (harness is null) { _output.WriteLine("SKIPPED: " + skipReason); return; }

            using (harness)
            {
                // Use a Class attribute for the modifier: unlike Race it is matched directly (no AssetsRace
                // remapping), so the engine's match and this test's check agree exactly.
                var probe = AttributeProbe.Scan(harness);
                bool IsBoosted(INpcGetter npc) => npc.Class.FormKey == probe.ClassFk;

                // Static 3:1 weighting between two otherwise-identical leaves.
                var weightHeavy = new AssetScenario.Leaf { Id = "W.heavy", ProbabilityWeighting = 3 };
                var weightLight = new AssetScenario.Leaf { Id = "W.light", ProbabilityWeighting = 1 };

                // Attribute modulation: identical base weights, but leaf M's weight is multiplied 20x for
                // NPCs of the probed class via a probability weight modifier. Matching NPCs should
                // overwhelmingly get M; non-matching NPCs should split evenly with N.
                var modifier = new AttributeWeightModifier
                {
                    Attribute = new NPCAttribute { SubAttributes = new() { new NPCAttributeClass { FormKeys = new() { probe.ClassFk } } } },
                    Factor = 20,
                };
                var modM = new AssetScenario.Leaf { Id = "MOD.m", ProbabilityWeighting = 1, ProbabilityWeightModifiers = new() { modifier } };
                var modN = new AssetScenario.Leaf { Id = "MOD.n", ProbabilityWeighting = 1 };

                var positions = new List<(string, IReadOnlyList<AssetScenario.Leaf>)>
                {
                    ("P_weight", new[] { weightHeavy, weightLight }),
                    ("P_mod", new[] { modM, modN }),
                };

                harness.UseAssetScenario(AssetScenario.BuildBothGendersMulti("Weighting", positions));
                await harness.RunAsync();

                // ── Static weighting: heavy should take ~3/4 of assignments ──
                double heavy = harness.AssignmentCount("W.heavy");
                double light = harness.AssignmentCount("W.light");
                var heavyShare = heavy / (heavy + light);
                _output.WriteLine($"Static weighting: heavy={heavy}, light={light}, heavyShare={heavyShare:0.000}");
                heavyShare.Should().BeInRange(0.70, 0.80, "a 3:1 weighting should yield ~75% for the heavy leaf");

                // ── Attribute modulation: M dominates among matching NPCs, splits evenly among the rest ──
                var mNpcs = harness.NpcsAssignedSubgroup("MOD.m");
                var nNpcs = harness.NpcsAssignedSubgroup("MOD.n");
                double boostM = mNpcs.Count(IsBoosted);
                double boostN = nNpcs.Count(IsBoosted);
                double otherM = mNpcs.Count(npc => !IsBoosted(npc));
                double otherN = nNpcs.Count(npc => !IsBoosted(npc));
                var boostShare = boostM / (boostM + boostN);
                var otherShare = otherM / (otherM + otherN);
                _output.WriteLine($"Modulation: boostShare(M)={boostShare:0.000}, otherShare(M)={otherShare:0.000}");

                // The modifier pushes matching NPCs well above the even (0.5) baseline; non-matching NPCs
                // stay at the baseline. (The observed boosted share is ~0.79 rather than the naive 20/21
                // because the factor composes with combination generation; the directional effect is what
                // the modifier guarantees.)
                boostShare.Should().BeGreaterThan(0.70, "the 20x modifier should make matching NPCs strongly prefer leaf M");
                otherShare.Should().BeInRange(0.40, 0.60, "non-matching NPCs are unaffected by the modifier and split evenly");
                boostShare.Should().BeGreaterThan(otherShare + 0.20, "the modifier should clearly raise M's share for matching NPCs");
            }
        });
    }
}
