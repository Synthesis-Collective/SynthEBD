using System.IO;
using FluentAssertions;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Order;
using Mutagen.Bethesda.Skyrim;
using Xunit;
using Xunit.Abstractions;

namespace SynthEBD.Tests.Integration;

/// <summary>
/// End-to-end integration tests that run the real <c>Patcher.RunPatcher()</c> against a Mutagen Skyrim SE
/// environment and the committed demo settings, asserting on the records written to the output plugin, the
/// support files transferred from <c>InternalData</c> to the output folder, and that distribution rules
/// (race gating) were honored. Tests skip gracefully when no Skyrim SE environment is available.
/// </summary>
[Collection(PatcherIntegrationCollection.Name)]
public class PatcherEndToEndTests
{
    private readonly ITestOutputHelper _output;
    private readonly WpfApplicationFixture _wpf;

    public PatcherEndToEndTests(ITestOutputHelper output, WpfApplicationFixture wpf)
    {
        _output = output;
        _wpf = wpf;
    }

    [Fact]
    public async Task RunPatcher_BodyGenMode_WritesRecordsFilesAndHonorsRaceGating()
    {
        await _wpf.RunOnStaAsync(async () =>
        {
        var harness = PatcherTestHarness.TryCreate(out var skipReason);
        if (harness is null)
        {
            // xUnit 2.8 has no dynamic Assert.Skip; surface the reason and pass as a no-op so the suite
            // stays green on machines without a Skyrim SE install (e.g. CI).
            _output.WriteLine("SKIPPED: " + skipReason);
            return;
        }

        using (harness)
        {
            // Demo GeneralSettings already selects BodyGen mode + assets (script) + height.
            await harness.RunAsync();

            var outputMod = harness.OutputMod;

            // ── Output plugin records ──
            outputMod.Npcs.Count.Should().BeGreaterThan(0, "the patcher should override NPC appearance records");
            // Asset patching generates new skin Armor + TextureSet records (cloned from the record template
            // and repathed) and points NPCs at them — these record types only appear because assets assigned.
            outputMod.Armors.Count.Should().BeGreaterThan(0, "asset patching generates skin armor records");
            outputMod.TextureSets.Count.Should().BeGreaterThan(0, "asset patching generates texture set records");
            harness.CombinationLog.AssignedPrimaryCombinations.Should().NotBeEmpty(
                "primary asset combinations should have been assigned to NPCs");
            // Height race overrides (bChangeRaceHeight=true, bApplyWithoutOverride=false).
            outputMod.Races.Count.Should().BeGreaterThan(0, "race height changes should override race records");

            // ── Output plugin written to disk (standalone run mode) ──
            File.Exists(harness.OutputPluginPath).Should().BeTrue("a standalone run writes the patch plugin to disk");
            // It must reload as a valid Skyrim SE plugin.
            using (var reloaded = SkyrimMod.CreateFromBinaryOverlay(harness.OutputPluginPath, SkyrimRelease.SkyrimSE))
            {
                reloaded.Npcs.Count.Should().BeGreaterThan(0);
            }

            // ── Files transferred from InternalData to the output folder ──
            var scriptsDir = Path.Combine(harness.OutputDataFolder, "Scripts");
            Directory.Exists(scriptsDir).Should().BeTrue("script-based features copy compiled Papyrus scripts");
            Directory.GetFiles(scriptsDir, "*.pex").Should().NotBeEmpty("at least the common SynthEBD scripts are copied");
            File.Exists(Path.Combine(harness.OutputDataFolder, "Seq", "SynthEBD.seq"))
                .Should().BeTrue("script features write the quest sequence file");

            // ── BodyGen morph output ──
            var bodyGenDir = Path.Combine(harness.OutputDataFolder, "Meshes", "actors", "character",
                "BodyGenData", "SynthEBDTest.esp");
            File.Exists(Path.Combine(bodyGenDir, "morphs.ini")).Should().BeTrue("BodyGen mode writes morphs.ini");
            Patcher.BodyGenTracker.NPCAssignments.Should().NotBeEmpty("BodyGen morphs should be assigned to NPCs");

            // ── Distribution rule: the Nord-gated subgroup is only assigned to Nord NPCs ──
            AssertNordGatedSubgroupOnlyOnNords(harness);
        }
        });
    }

    [Fact]
    public async Task RunPatcher_BodySlideAndHeadPartMode_WritesAssignmentsAndScripts()
    {
        await _wpf.RunOnStaAsync(async () =>
        {
        var harness = PatcherTestHarness.TryCreate(out var skipReason);
        if (harness is null)
        {
            // xUnit 2.8 has no dynamic Assert.Skip; surface the reason and pass as a no-op so the suite
            // stays green on machines without a Skyrim SE install (e.g. CI).
            _output.WriteLine("SKIPPED: " + skipReason);
            return;
        }

        using (harness)
        {
            // Switch the body axis to BodySlide (OBody script mode is set in the demo OBody settings).
            harness.PatcherState.GeneralSettings.BodySelectionMode = BodyShapeSelectionMode.BodySlide;
            harness.PatcherState.GeneralSettings.BSSelectionMode = BodySlideSelectionMode.OBody;
            // An empty test data folder means no BodySlides are auto-detected or classified; mark the demo
            // presets as installed + classified so assignments survive the patcher's distributability filters.
            harness.MarkAllBodySlidesAsDistributable();
            // Inject real, resolvable vanilla head parts so head-part patching has something to assign.
            InjectVanillaHeadParts(harness);

            await harness.RunAsync();

            // ── BodySlide assignments ──
            Patcher.BodySlideTracker.Should().NotBeEmpty("BodySlide presets should be assigned to NPCs");
            File.Exists(Path.Combine(harness.OutputDataFolder, "SynthEBD", "BodySlideAssignments.json"))
                .Should().BeTrue("OBody script mode writes BodySlideAssignments.json");
            Directory.GetFiles(Path.Combine(harness.OutputDataFolder, "Scripts"), "*BodySlide*.pex")
                .Should().NotBeEmpty("OBody script mode copies the BodySlide Papyrus scripts");

            // ── Head part assignments ──
            File.Exists(Path.Combine(harness.OutputDataFolder, "SynthEBD", "HeadPartAssignments.json"))
                .Should().BeTrue("head-part script mode writes HeadPartAssignments.json");
            Directory.GetFiles(Path.Combine(harness.OutputDataFolder, "Scripts"), "*HeadPart*.pex")
                .Should().NotBeEmpty("head-part script mode copies the head-part Papyrus scripts");
        }
        });
    }

    [Fact]
    public async Task RunPatcher_BodySlideSelectionFailsForAllNpcs_HeadPartsStillAssigned()
    {
        await _wpf.RunOnStaAsync(async () =>
        {
        var harness = PatcherTestHarness.TryCreate(out var skipReason);
        if (harness is null)
        {
            _output.WriteLine("SKIPPED: " + skipReason);
            return;
        }

        using (harness)
        {
            harness.PatcherState.GeneralSettings.BodySelectionMode = BodyShapeSelectionMode.BodySlide;
            harness.PatcherState.GeneralSettings.BSSelectionMode = BodySlideSelectionMode.OBody;
            harness.MarkAllBodySlidesAsDistributable();
            // Force SelectBodySlidePresets to fail for every NPC: the race-only pre-check
            // (CurrentNPCHasAvailablePresets) still passes, but full validation rejects every preset
            // on weight range (NPC weights are 0-100). This is the path a user hits when their
            // remaining distributable presets can't validate for some NPC, and it must not take the
            // head-part axis down with it (assignedBodySlides used to go null here).
            foreach (var preset in harness.PatcherState.OBodySettings.BodySlidesMale
                         .Concat(harness.PatcherState.OBodySettings.BodySlidesFemale))
            {
                preset.WeightRange = new NPCWeightRange { Lower = 101, Upper = 101 };
            }
            InjectVanillaHeadParts(harness);

            await harness.RunAsync();

            // ── BodySlides: nothing assignable, and that's expected ──
            Patcher.BodySlideTracker.Should().BeEmpty("every preset fails its weight range, so no BodySlide can be assigned");

            // ── Head parts must still be assigned despite the BodySlide axis coming up empty ──
            File.Exists(Path.Combine(harness.OutputDataFolder, "SynthEBD", "HeadPartAssignments.json"))
                .Should().BeTrue("head-part assignment must survive BodySlide selection failure");
        }
        });
    }

    /// <summary>
    /// Verifies the race-gating distribution rule: every NPC assigned a combination containing the
    /// Nord-only subgroup (<c>HD.Nord</c>) must actually be a Nord. Reads the patcher's combination log,
    /// which records, per assigned combination, the NPCs it was given to.
    /// </summary>
    private void AssertNordGatedSubgroupOnlyOnNords(PatcherTestHarness harness)
    {
        var linkCache = harness.EnvironmentProvider.LinkCache;
        var nordAssignmentCount = 0;

        foreach (var configEntry in harness.CombinationLog.AssignedPrimaryCombinations)
        {
            foreach (var combination in configEntry.Value)
            {
                if (!combination.SubgroupIDs.Contains("HD.Nord"))
                {
                    continue;
                }

                foreach (var npcLogId in combination.NPCsAssignedTo)
                {
                    var formKey = ParseFormKey(npcLogId);
                    formKey.Should().NotBeNull($"the NPC log id '{npcLogId}' should contain a FormKey");

                    linkCache.TryResolve<INpcGetter>(formKey!.Value, out var npc).Should().BeTrue();
                    linkCache.TryResolve<IRaceGetter>(npc!.Race.FormKey, out var race).Should().BeTrue();
                    var raceEditorId = race!.EditorID ?? string.Empty;

                    raceEditorId.Should().Contain("Nord",
                        $"subgroup HD.Nord is gated to the Nord race but was assigned to '{npcLogId}' (race {raceEditorId})");
                    nordAssignmentCount++;
                }
            }
        }

        _output.WriteLine($"Nord-gated subgroup HD.Nord was assigned to {nordAssignmentCount} NPC(s), all Nords.");
        nordAssignmentCount.Should().BeGreaterThan(0,
            "the Nord-gated subgroup should have been assigned to at least one Nord NPC (coverage check)");
    }

    /// <summary>Parses the trailing <c>FormKey</c> out of a <c>"Name | EditorID | FormKey"</c> log id.</summary>
    private static FormKey? ParseFormKey(string npcLogId)
    {
        var lastSegment = npcLogId.Split('|').LastOrDefault()?.Trim();
        if (string.IsNullOrEmpty(lastSegment))
        {
            return null;
        }
        return FormKey.TryFactory(lastSegment, out var formKey) ? formKey : null;
    }

    /// <summary>
    /// Adds one real, link-cache-resolvable vanilla head part per common type (Hair / Eyebrows) to the
    /// loaded head-part settings, so head-part patching can resolve and assign them. Resolving from the
    /// live link cache avoids hardcoding FormKeys that differ across game versions.
    /// </summary>
    private void InjectVanillaHeadParts(PatcherTestHarness harness)
    {
        AddFirstHeadPartOfType(harness, HeadPart.TypeEnum.Hair);
        AddFirstHeadPartOfType(harness, HeadPart.TypeEnum.Eyebrows);
    }

    private void AddFirstHeadPartOfType(PatcherTestHarness harness, HeadPart.TypeEnum type)
    {
        var headPart = harness.EnvironmentProvider.LoadOrder.PriorityOrder
            .OnlyEnabledAndExisting()
            .WinningOverrides<IHeadPartGetter>()
            .FirstOrDefault(hp => hp.Type == type && hp.EditorID != null
                                  && hp.Flags.HasFlag(HeadPart.Flag.Playable));
        if (headPart is null)
        {
            _output.WriteLine($"No playable vanilla head part of type {type} found; skipping injection.");
            return;
        }

        if (!harness.PatcherState.HeadPartSettings.Types.TryGetValue(type, out var typeSettings))
        {
            return;
        }

        typeSettings.HeadParts.Add(new HeadPartSetting
        {
            HeadPartFormKey = headPart.FormKey,
            EditorID = headPart.EditorID,
            bAllowMale = true,
            bAllowFemale = true,
            bAllowUnique = true,
            bAllowNonUnique = true,
            bAllowRandom = true,
            ProbabilityWeighting = 1,
        });
        _output.WriteLine($"Injected vanilla {type} head part {headPart.EditorID} ({headPart.FormKey}).");
    }
}
