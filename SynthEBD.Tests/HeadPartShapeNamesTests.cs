using FluentAssertions;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Skyrim;
using Xunit;

namespace SynthEBD.Tests;

/// <summary>
/// Pins the walk semantics of <see cref="HeadPartShapeNames"/>, which feeds
/// CharacterViewer.Rendering's <c>ResolvedNpcMeshPaths.EyeShapeNames</c> (the
/// renderer's authoritative IsEye input). This is a deliberate twin of NPC
/// Plugin Chooser 2's <c>FaceGenConsistencyAnalyzer.CollectShapeNamesOfType</c>
/// (same effective-headpart walk: NPC parts + race defaults for unoccupied
/// slots + Extra Parts recursion + cycle guard) — these tests keep the two
/// implementations from drifting apart. Motivating case: FoxGlove Auri's
/// eyeball is an ENVMAP-typed shape named "FoxGloveEyeMesh" (singular), which
/// the renderer's plural-"Eyes" name heuristic misses.
/// </summary>
public class HeadPartShapeNamesTests
{
    private static SkyrimMod NewMod() =>
        new(ModKey.FromNameAndExtension("Test.esp"), SkyrimRelease.SkyrimSE);

    private static HeadPart NewHeadPart(SkyrimMod mod, string editorId, HeadPart.TypeEnum? type)
    {
        var hp = mod.HeadParts.AddNew();
        hp.EditorID = editorId;
        hp.Type = type;
        return hp;
    }

    [Fact]
    public void CollectFromNpcRecord_CollectsTypedPartAndExtras_ExcludesOtherTypes_CaseInsensitive()
    {
        var mod = NewMod();
        var eyes = NewHeadPart(mod, "FoxGloveEyeMesh", HeadPart.TypeEnum.Eyes);
        var extra = NewHeadPart(mod, "FoxGloveEyeExtra", null); // Extra Parts are typically untyped
        var hair = NewHeadPart(mod, "HairShape", HeadPart.TypeEnum.Hair);
        eyes.ExtraParts.Add(extra.FormKey.ToLink<IHeadPartGetter>());

        var npc = mod.Npcs.AddNew();
        npc.HeadParts.Add(eyes.FormKey.ToLink<IHeadPartGetter>());
        npc.HeadParts.Add(hair.FormKey.ToLink<IHeadPartGetter>());

        var names = HeadPartShapeNames.CollectFromNpcRecord(
            npc, mod.ToImmutableLinkCache(), HeadPart.TypeEnum.Eyes);

        names.Should().BeEquivalentTo(new[] { "FoxGloveEyeMesh", "FoxGloveEyeExtra" });
        names.Contains("foxgloveeyemesh").Should().BeTrue("shape-name reconciliation is case-insensitive");
    }

    [Fact]
    public void CollectFromNpcRecord_RaceDefaultUsedOnlyWhenNpcLacksSlot()
    {
        var mod = NewMod();
        var raceEyes = NewHeadPart(mod, "RaceEyesDefault", HeadPart.TypeEnum.Eyes);
        var race = mod.Races.AddNew();
        var headData = new HeadData();
        var hpRef = new HeadPartReference();
        hpRef.Head.SetTo(raceEyes.FormKey);
        headData.HeadParts.Add(hpRef);
        race.HeadData = new GenderedItem<HeadData?>(headData, null);

        // Male NPC (default flags) with no eyes of its own -> race default applies.
        var bare = mod.Npcs.AddNew();
        bare.Race.SetTo(race);
        HeadPartShapeNames.CollectFromNpcRecord(bare, mod.ToImmutableLinkCache(), HeadPart.TypeEnum.Eyes)
            .Should().BeEquivalentTo(new[] { "RaceEyesDefault" });

        // NPC occupying the Eyes slot -> race default skipped.
        var npcEyes = NewHeadPart(mod, "NpcEyes", HeadPart.TypeEnum.Eyes);
        var equipped = mod.Npcs.AddNew();
        equipped.Race.SetTo(race);
        equipped.HeadParts.Add(npcEyes.FormKey.ToLink<IHeadPartGetter>());
        HeadPartShapeNames.CollectFromNpcRecord(equipped, mod.ToImmutableLinkCache(), HeadPart.TypeEnum.Eyes)
            .Should().BeEquivalentTo(new[] { "NpcEyes" });
    }

    [Fact]
    public void CollectFromNpcRecord_CircularExtraParts_Terminates()
    {
        var mod = NewMod();
        var eyes = NewHeadPart(mod, "LoopingEyes", HeadPart.TypeEnum.Eyes);
        eyes.ExtraParts.Add(eyes.FormKey.ToLink<IHeadPartGetter>()); // self-referencing Extra Part

        var npc = mod.Npcs.AddNew();
        npc.HeadParts.Add(eyes.FormKey.ToLink<IHeadPartGetter>());

        HeadPartShapeNames.CollectFromNpcRecord(npc, mod.ToImmutableLinkCache(), HeadPart.TypeEnum.Eyes)
            .Should().BeEquivalentTo(new[] { "LoopingEyes" });
    }

    [Fact]
    public void CollectFromHeadPart_ReturnsPartAndExtras_EmptyWhenUnresolvable()
    {
        var mod = NewMod();
        var eyes = NewHeadPart(mod, "AssignedEyes", HeadPart.TypeEnum.Eyes);
        var extra = NewHeadPart(mod, "AssignedEyesExtra", null);
        eyes.ExtraParts.Add(extra.FormKey.ToLink<IHeadPartGetter>());
        var linkCache = mod.ToImmutableLinkCache();

        HeadPartShapeNames.CollectFromHeadPart(eyes.FormKey, linkCache)
            .Should().BeEquivalentTo(new[] { "AssignedEyes", "AssignedEyesExtra" });

        HeadPartShapeNames.CollectFromHeadPart(new FormKey(ModKey.FromNameAndExtension("Missing.esp"), 0x800), linkCache)
            .Should().BeEmpty();
        HeadPartShapeNames.CollectFromHeadPart(FormKey.Null, linkCache).Should().BeEmpty();
    }
}
