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
