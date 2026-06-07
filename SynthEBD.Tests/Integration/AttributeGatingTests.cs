using System.IO;
using FluentAssertions;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;
using Xunit;
using Xunit.Abstractions;

namespace SynthEBD.Tests.Integration;

/// <summary>
/// Verifies attribute-based distribution gating for each <see cref="NPCAttributeType"/>, both as an allowed
/// restriction (only matching NPCs may receive the subgroup) and as a disallowed restriction (matching NPCs
/// are excluded). Each rule lives in its own top-level position with a high-weight gated leaf plus a
/// fallback, so matching NPCs deterministically receive the gated leaf and coverage is reliable. Attribute
/// values are probed from the live load order so they match real NPCs.
/// </summary>
[Collection(PatcherIntegrationCollection.Name)]
public class AttributeGatingTests
{
    private readonly ITestOutputHelper _output;
    private readonly WpfApplicationFixture _wpf;

    public AttributeGatingTests(ITestOutputHelper output, WpfApplicationFixture wpf)
    {
        _output = output;
        _wpf = wpf;
    }

    private static readonly FormKey NordRace = FormKey.Factory("013746:Skyrim.esm");
    private const string GroupLabel = "ZZTest Nord Group";

    private sealed record Dimension(
        string Key,
        ITypedNPCAttribute Attribute,
        Func<INpcGetter, bool> Matches,
        bool Enabled = true);

    [Fact]
    public async Task AllowedAndDisallowedAttributes_GateAssignmentsByType()
    {
        await _wpf.RunOnStaAsync(async () =>
        {
            var harness = PatcherTestHarness.TryCreate(out var skipReason);
            if (harness is null) { _output.WriteLine("SKIPPED: " + skipReason); return; }

            using (harness)
            {
                var probe = AttributeProbe.Scan(harness);

                // Curated set of well-known unique NPCs (resolved by EditorID) for exact NPC-attribute
                // gating, avoiding the large-FormKey-set / duplicate-corpse edge cases.
                var linkCache = harness.EnvironmentProvider.LinkCache;
                var npcEditorIds = new[]
                {
                    "Lydia", "Uthgerd", "Ysolda", "Carlotta", "Belethor", "Adrianne", "Hulda", "Nazeem",
                    "Idolaf", "Heimskr", "Mikael", "Saadia", "Brenuin", "Ahlam", "Olfina", "Severio",
                };
                var npcSet = npcEditorIds
                    .Where(id => linkCache.TryResolve<INpcGetter>(id, out _))
                    .Select(id => { linkCache.TryResolve<INpcGetter>(id, out var n); return n!.FormKey; })
                    .ToHashSet();

                // Define an attribute group (used by the Group dimension) referencing a Nord race attribute.
                harness.PatcherState.GeneralSettings.AttributeGroups.Add(new AttributeGroup
                {
                    Label = GroupLabel,
                    Attributes = new() { Attr(new NPCAttributeRace { FormKeys = new() { NordRace } }) },
                });

                var dims = new List<Dimension>
                {
                    new("Class", new NPCAttributeClass { FormKeys = new() { probe.ClassFk } },
                        npc => npc.Class.FormKey == probe.ClassFk, !probe.ClassFk.IsNull),
                    new("Faction", new NPCAttributeFactions { FormKeys = new() { probe.FactionFk } },
                        npc => npc.Factions.Any(f => f.Faction.FormKey == probe.FactionFk), !probe.FactionFk.IsNull),
                    new("Voice", new NPCAttributeVoiceType { FormKeys = new() { probe.VoiceFk } },
                        npc => npc.Voice.FormKey == probe.VoiceFk, !probe.VoiceFk.IsNull),
                    new("Race", new NPCAttributeRace { FormKeys = new() { probe.RaceFk } },
                        npc => npc.Race.FormKey == probe.RaceFk, !probe.RaceFk.IsNull),
                    new("FaceTex", new NPCAttributeFaceTexture { FormKeys = new() { probe.FaceTextureFk } },
                        npc => npc.HeadTexture.FormKey == probe.FaceTextureFk, !probe.FaceTextureFk.IsNull),
                    new("Keyword", new NPCAttributeKeyword { FormKeys = new() { probe.KeywordFk } },
                        npc => npc.Keywords?.Any(k => k.FormKey == probe.KeywordFk) ?? false, !probe.KeywordFk.IsNull),
                    new("Npc", new NPCAttributeNPC { FormKeys = npcSet },
                        npc => npcSet.Contains(npc.FormKey), npcSet.Count > 0),
                    new("Mod", new NPCAttributeMod { ModKeys = new() { probe.SourceMod }, ModActionType = ModAttributeEnum.CreatedBy },
                        npc => npc.FormKey.ModKey.Equals(probe.SourceMod)),
                    new("MiscGender", new NPCAttributeMisc { EvalGender = true, NPCGender = Gender.Female },
                        npc => npc.Configuration.Flags.HasFlag(NpcConfiguration.Flag.Female)),
                    new("Group", new NPCAttributeGroup { SelectedLabels = new() { GroupLabel } },
                        npc => npc.Race.FormKey == NordRace),
                    new("Custom", new NPCAttributeCustom { Path = "Race", CustomType = CustomAttributeType.Record, ValueFKs = new() { NordRace }, Comparator = "=" },
                        npc => npc.Race.FormKey == NordRace),
                };

                var active = dims.Where(d => d.Enabled).ToList();
                foreach (var skipped in dims.Where(d => !d.Enabled))
                {
                    _output.WriteLine($"Skipping {skipped.Key} (no representative value in this load order).");
                }

                var positions = new List<(string, IReadOnlyList<AssetScenario.Leaf>)>();
                foreach (var d in active)
                {
                    positions.Add(Position("ALLOW." + d.Key, Gated("ALLOW." + d.Key, CloneRestrict(d.Attribute), disallowed: false)));
                    positions.Add(Position("DIS." + d.Key, Gated("DIS." + d.Key, CloneRestrict(d.Attribute), disallowed: true)));
                }

                harness.UseAssetScenario(AssetScenario.BuildBothGendersMulti("Attribute Gating", positions));
                await harness.RunAsync();

                foreach (var d in active)
                {
                    var allowed = harness.NpcsAssignedSubgroup("ALLOW." + d.Key);
                    allowed.Should().NotBeEmpty($"the allowed-{d.Key} subgroup should admit matching NPCs (coverage)");
                    allowed.Should().OnlyContain(npc => d.Matches(npc),
                        $"only NPCs matching the {d.Key} attribute should receive the allowed-{d.Key} subgroup");

                    var disallowed = harness.NpcsAssignedSubgroup("DIS." + d.Key);
                    disallowed.Should().NotBeEmpty($"the disallowed-{d.Key} subgroup should admit non-matching NPCs (coverage)");
                    disallowed.Should().OnlyContain(npc => !d.Matches(npc),
                        $"NPCs matching the {d.Key} attribute must be excluded from the disallowed-{d.Key} subgroup");

                    _output.WriteLine($"{d.Key}: allowed={allowed.Count}, disallowed={disallowed.Count}");
                }
            }
        });
    }

    [Fact]
    public async Task ForceIfAttributes_ForcePreferentialSelectionByWeight()
    {
        await _wpf.RunOnStaAsync(async () =>
        {
            var harness = PatcherTestHarness.TryCreate(out var skipReason);
            if (harness is null) { _output.WriteLine("SKIPPED: " + skipReason); return; }

            using (harness)
            {
                bool IsNord(INpcGetter npc) => npc.Race.FormKey == NordRace;

                // ForceIf(Nord) does not restrict eligibility but, when matched, forces the NPC onto the
                // highest-weighted ForceIf subgroup at the position; equal ForceIf weights split by chance.
                AssetScenario.Leaf ForceIfNord(string id, int weighting) => new()
                {
                    Id = id,
                    AllowedAttributes = new() { Attr(new NPCAttributeRace { FormKeys = new() { NordRace }, ForceMode = AttributeForcing.ForceIf, Weighting = weighting }) },
                };
                AssetScenario.Leaf Plain(string id) => new() { Id = id };

                var positions = new List<(string, IReadOnlyList<AssetScenario.Leaf>)>
                {
                    // Forcing: a matched ForceIf steals every Nord from the plain alternative.
                    ("P_force", new[] { ForceIfNord("F1.force", 1), Plain("F1.plain") }),
                    // Weighted priority: higher ForceIf weight wins for matched NPCs.
                    ("P_weight", new[] { ForceIfNord("FW.hi", 10), ForceIfNord("FW.lo", 1) }),
                    // Tie: equal ForceIf weight splits matched NPCs between the two leaves.
                    ("P_tie", new[] { ForceIfNord("FT.d", 5), ForceIfNord("FT.e", 5) }),
                };

                harness.UseAssetScenario(AssetScenario.BuildBothGendersMulti("ForceIf", positions));
                await harness.RunAsync();

                // Forcing
                harness.NpcsAssignedSubgroup("F1.force").Where(IsNord).Should().NotBeEmpty("matched Nords should be forced onto the ForceIf leaf");
                harness.NpcsAssignedSubgroup("F1.plain").Where(IsNord).Should().BeEmpty("no Nord should land on the plain alternative when a ForceIf leaf matches it");

                // Weighted priority
                harness.NpcsAssignedSubgroup("FW.hi").Where(IsNord).Should().NotBeEmpty("Nords should be forced onto the higher-weight ForceIf leaf");
                harness.NpcsAssignedSubgroup("FW.lo").Where(IsNord).Should().BeEmpty("the lower-weight ForceIf leaf should win no matched Nords");

                // Tie split
                harness.NpcsAssignedSubgroup("FT.d").Where(IsNord).Should().NotBeEmpty("tied ForceIf leaves should each receive some matched Nords");
                harness.NpcsAssignedSubgroup("FT.e").Where(IsNord).Should().NotBeEmpty("tied ForceIf leaves should each receive some matched Nords");

                _output.WriteLine("ForceIf forcing, weighting, and tie-splitting verified.");
            }
        });
    }

    private static NPCAttribute Attr(ITypedNPCAttribute typed) => new() { SubAttributes = new() { typed } };

    /// <summary>Returns a copy of the typed attribute with ForceMode=Restrict (so it gates eligibility).</summary>
    private static ITypedNPCAttribute CloneRestrict(ITypedNPCAttribute t)
    {
        t.ForceMode = AttributeForcing.Restrict;
        return t;
    }

    private static AssetScenario.Leaf Gated(string id, ITypedNPCAttribute typed, bool disallowed) => new()
    {
        Id = id,
        ProbabilityWeighting = 1000, // dominate the fallback so matching NPCs reliably receive this leaf
        AllowedAttributes = disallowed ? new() : new() { Attr(typed) },
        DisallowedAttributes = disallowed ? new() { Attr(typed) } : new(),
    };

    // The position id is deliberately distinct from the gated leaf id (the gated leaf id is the queryable
    // one). A shared id would leak the position into the combination signature and break id matching.
    private static (string, IReadOnlyList<AssetScenario.Leaf>) Position(string leafId, AssetScenario.Leaf gated)
        => ("P_" + leafId, new[] { gated, new AssetScenario.Leaf { Id = leafId + ".fb", ProbabilityWeighting = 1 } });
}
