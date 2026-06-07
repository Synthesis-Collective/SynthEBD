using System.IO;
using System.Text.RegularExpressions;
using FluentAssertions;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;
using Xunit;
using Xunit.Abstractions;

namespace SynthEBD.Tests.Integration;

/// <summary>
/// Verifies the structure of the SkyPatcher ini the patcher emits when SkyPatcher asset mode is enabled.
/// A Nord-gated skin pack runs in script face mode with <c>bSkyPatcherModeAssets</c>, which makes the
/// patcher write one <c>filterByNPCs={npc}:skin={wornArmor}</c> directive per assigned NPC instead of
/// editing the NPC record directly. The test checks the file location, line format, FormKey formatting, and
/// that the directives target exactly the NPCs that were assigned.
/// </summary>
[Collection(PatcherIntegrationCollection.Name)]
public class SkyPatcherOutputTests
{
    private readonly ITestOutputHelper _output;
    private readonly WpfApplicationFixture _wpf;

    public SkyPatcherOutputTests(ITestOutputHelper output, WpfApplicationFixture wpf)
    {
        _output = output;
        _wpf = wpf;
    }

    // filterByNPCs=<modkey>|<hex>:skin=<modkey>|<hex>
    private static readonly Regex SkinLine = new(
        @"^filterByNPCs=(?<npc>[^|]+\|[0-9A-Fa-f]+):skin=(?<skin>[^|]+\|[0-9A-Fa-f]+)$", RegexOptions.Compiled);

    [Fact]
    public async Task SkyPatcherAssetMode_WritesWellFormedSkinDirectives()
    {
        await _wpf.RunOnStaAsync(async () =>
        {
            var harness = PatcherTestHarness.TryCreate(out var skipReason);
            if (harness is null) { _output.WriteLine("SKIPPED: " + skipReason); return; }

            using (harness)
            {
                var pack = AssetScenario.BuildPack("Demo SkyPatcher", Gender.Female, new[]
                {
                    new AssetScenario.Leaf
                    {
                        Id = "SP.skin",
                        AllowedRaceGroupings = new() { "Nord" },
                        Source = "textures\\SynthEBDDemo\\sp_body.dds",
                        Destination = AssetScenario.FemaleBodyDiffuseDestination,
                    },
                });

                harness.UseAssetScenario(new[] { pack }); // script face mode by default
                harness.PatcherState.TexMeshSettings.bSkyPatcherModeAssets = true;

                await harness.RunAsync();

                var iniPath = Path.Combine(harness.OutputDataFolder, "SKSE", "Plugins", "SkyPatcher", "npc", "SynthEBD", "SynthEBD.ini");
                // The ini is written on a background task; poll briefly for it to flush.
                for (var i = 0; i < 40 && !File.Exists(iniPath); i++) { await Task.Delay(100); }
                File.Exists(iniPath).Should().BeTrue("SkyPatcher asset mode should write the SynthEBD.ini directives file");

                var lines = (await File.ReadAllLinesAsync(iniPath)).Where(l => l.Trim().Length > 0).ToList();
                lines.Should().NotBeEmpty("there should be one skin directive per assigned NPC");

                // Every line is a well-formed skin directive whose skin record lives in the output plugin.
                var iniNpcs = new HashSet<FormKey>();
                foreach (var line in lines)
                {
                    var m = SkinLine.Match(line);
                    m.Success.Should().BeTrue($"line '{line}' should be a filterByNPCs=...:skin=... directive");
                    m.Groups["skin"].Value.Should().StartWith("SynthEBDTest.esp|",
                        "the assigned skin record is generated into the output plugin");
                    iniNpcs.Add(ParseBodyGenFormKey(m.Groups["npc"].Value));
                }

                // The directives must target exactly the NPCs assigned the Nord-gated skin subgroup.
                var assigned = harness.NpcsAssignedSubgroup("SP.skin").Select(npc => npc.FormKey).ToHashSet();
                iniNpcs.Should().BeEquivalentTo(assigned,
                    "every assigned NPC (and only those) should get a SkyPatcher skin directive");

                _output.WriteLine($"SkyPatcher ini: {lines.Count} skin directive(s) for {assigned.Count} assigned NPC(s).");
            }
        });
    }

    /// <summary>Parses a "ModKey|hex" BodyGen-format form key (leading zeros trimmed) back to a FormKey.</summary>
    private static FormKey ParseBodyGenFormKey(string token)
    {
        var parts = token.Split('|');
        var id = parts[1].PadLeft(6, '0');
        return FormKey.Factory(id + ":" + parts[0]);
    }
}
