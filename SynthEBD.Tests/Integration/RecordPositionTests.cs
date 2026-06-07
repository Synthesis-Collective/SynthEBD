using FluentAssertions;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Skyrim;
using Xunit;
using Xunit.Abstractions;

namespace SynthEBD.Tests.Integration;

/// <summary>
/// Verifies that an assigned subgroup's texture paths actually land on the indicated record positions of
/// the output NPCs — i.e. the patcher doesn't just record an assignment, it writes the right texture to the
/// right place. Uses a single-option pack gated to Nord so every assigned NPC deterministically receives the
/// same body+hands skin diffuse paths, then walks each NPC's WornArmor -> ArmorAddon(body part) ->
/// SkinTexture -> TextureSet.Diffuse chain in the output plugin.
/// </summary>
[Collection(PatcherIntegrationCollection.Name)]
public class RecordPositionTests
{
    private readonly ITestOutputHelper _output;
    private readonly WpfApplicationFixture _wpf;

    public RecordPositionTests(ITestOutputHelper output, WpfApplicationFixture wpf)
    {
        _output = output;
        _wpf = wpf;
    }

    private const string BodySource = "textures\\SynthEBDDemo\\rec_body.dds";
    private const string HandsSource = "textures\\SynthEBDDemo\\rec_hands.dds";
    private const string HandsDestination =
        "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Hands) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Female.Diffuse.GivenPath";

    [Fact]
    public async Task AssignedSkinTextures_LandOnTheIndicatedArmorAddonPositions()
    {
        await _wpf.RunOnStaAsync(async () =>
        {
            var harness = PatcherTestHarness.TryCreate(out var skipReason);
            if (harness is null) { _output.WriteLine("SKIPPED: " + skipReason); return; }

            using (harness)
            {
                // Single leaf, gated to Nord, writing two body-part skin diffuse paths. Because it is the only
                // option at its position, every Nord female deterministically receives exactly these textures.
                var leaf = new AssetScenario.Leaf
                {
                    Id = "REC.skin",
                    Name = "Record position skin",
                    AllowedRaceGroupings = new() { "Nord" },
                    Source = BodySource,
                    Destination = AssetScenario.FemaleBodyDiffuseDestination,
                };
                // Second path on the same leaf for the Hands position.
                var pack = AssetScenario.BuildPack("Demo Record Position", Gender.Female, new[] { leaf });
                pack.Subgroups[0].Subgroups[0].Paths.Add(new FilePathReplacement
                {
                    Source = HandsSource,
                    Destination = HandsDestination,
                });

                harness.UseAssetScenario(new[] { pack });
                await harness.RunAsync();

                var assigned = harness.NpcsAssignedSubgroup("REC.skin");
                assigned.Should().NotBeEmpty("the Nord-gated skin subgroup should be assigned to Nord NPCs");

                var linkCache = harness.EnvironmentProvider.LinkCache;
                var checkedCount = 0;
                foreach (var npc in assigned.Take(10))
                {
                    npc.WornArmor.IsNull.Should().BeFalse($"NPC {npc.EditorID} should have a generated skin armor");
                    var armor = linkCache.Resolve<IArmorGetter>(npc.WornArmor.FormKey);

                    // TexMesh TrimPaths strips the leading "textures\" segment from dds paths before they are
                    // written into the TextureSet record (game paths are relative to the textures folder).
                    SkinDiffuse(linkCache, armor, BipedObjectFlag.Body).Should().Be(Trimmed(BodySource),
                        $"NPC {npc.EditorID}'s body armature skin diffuse should be the assigned texture");
                    SkinDiffuse(linkCache, armor, BipedObjectFlag.Hands).Should().Be(Trimmed(HandsSource),
                        $"NPC {npc.EditorID}'s hands armature skin diffuse should be the assigned texture");
                    checkedCount++;
                }

                _output.WriteLine($"Verified body+hands skin diffuse on {checkedCount} of {assigned.Count} assigned NPC(s).");
            }
        });
    }

    /// <summary>Removes the leading "textures\" segment that the patcher trims from dds paths.</summary>
    private static string Trimmed(string source) =>
        source.StartsWith("textures\\", StringComparison.OrdinalIgnoreCase) ? source.Substring("textures\\".Length) : source;

    /// <summary>Resolves the female skin-texture diffuse path of the armor addon carrying the given body-part flag.</summary>
    private static string? SkinDiffuse(ILinkCache linkCache, IArmorGetter armor, BipedObjectFlag part)
    {
        foreach (var addonLink in armor.Armature)
        {
            if (!linkCache.TryResolve<IArmorAddonGetter>(addonLink.FormKey, out var addon)) { continue; }
            if (addon.BodyTemplate is null || !addon.BodyTemplate.FirstPersonFlags.HasFlag(part)) { continue; }
            var female = addon.SkinTexture?.Female;
            if (female is null || female.IsNull) { continue; }
            if (linkCache.TryResolve<ITextureSetGetter>(female.FormKey, out var texSet))
            {
                return texSet.Diffuse;
            }
        }
        return null;
    }
}
