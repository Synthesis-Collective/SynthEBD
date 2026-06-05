using FluentAssertions;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Plugins.Order;
using Mutagen.Bethesda.Skyrim;
using Noggog;
using Xunit;

namespace SynthEBD.Tests;

public class SetViaFormKeyReplacementTests
{
    // SetViaFormKeyReplacement became an instance method (commit 7357c0f3), but xUnit cannot
    // supply a RecordGenerator via constructor injection without a fixture. The method only
    // touches _recordPathParser and _environmentProvider.LinkCache, so we build a minimal
    // RecordGenerator from the test mod's own link cache and leave the unused collaborators null.
    private static RecordGenerator BuildRecordGenerator(SkyrimMod mod)
    {
        var env = new TestEnvironmentStateProvider(mod.ToImmutableLinkCache());
        var pathParser = new RecordPathParser(env, null!, null!);
        return new RecordGenerator(env, null!, null!, null!, null!, null!, pathParser, null!, null!, null!, null!, null!, null!);
    }

    [Fact]
    public void ArrayDynamic()
    {
        var mod = new SkyrimMod(ModKey.Null, SkyrimRelease.SkyrimSE);
        var arm = mod.Armors.AddNew();
        arm.Armature.Add(mod.ArmorAddons.AddNew());
        var npc = mod.Npcs.AddNew();
        dynamic list = arm.Armature;
        dynamic addon = new ArmorAddon(mod);
        BuildRecordGenerator(mod).SetViaFormKeyReplacement(addon, list, "[0]", npc);
        arm.Armature.First().FormKey.Should().Be(addon.FormKey);
    }

    [Fact]
    public void ArrayStraight()
    {
        var mod = new SkyrimMod(ModKey.Null, SkyrimRelease.SkyrimSE);
        var arm = mod.Armors.AddNew();
        arm.Armature.Add(mod.ArmorAddons.AddNew());
        var npc = mod.Npcs.AddNew();
        var list = arm.Armature;
        var addon = new ArmorAddon(mod);
        BuildRecordGenerator(mod).SetViaFormKeyReplacement(addon, list, "[0]", npc);
        arm.Armature.First().FormKey.Should().Be(addon.FormKey);
    }

    [Fact]
    public void SingleDynamic()
    {
        var mod = new SkyrimMod(ModKey.Null, SkyrimRelease.SkyrimSE);
        var arm = mod.Armors.AddNew();
        arm.Race.SetTo(mod.Races.AddNew());
        var npc = mod.Npcs.AddNew();
        dynamic dyn = arm;
        dynamic race = new Race(mod);
        BuildRecordGenerator(mod).SetViaFormKeyReplacement(race, dyn, nameof(Armor.Race), npc);
        arm.Race.FormKey.Should().Be(race.FormKey);
    }

    [Fact]
    public void SingleStraight()
    {
        var mod = new SkyrimMod(ModKey.Null, SkyrimRelease.SkyrimSE);
        var arm = mod.Armors.AddNew();
        var npc = mod.Npcs.AddNew();
        arm.Race.SetTo(mod.Races.AddNew());
        var race = new Race(mod);
        BuildRecordGenerator(mod).SetViaFormKeyReplacement(race, arm, nameof(Armor.Race), npc);
        arm.Race.FormKey.Should().Be(race.FormKey);
    }

    // Minimal IOutputEnvironmentStateProvider that only exposes a LinkCache; all other members
    // are unused by SetViaFormKeyReplacement and throw if anything ever reaches for them.
    private sealed class TestEnvironmentStateProvider : IOutputEnvironmentStateProvider
    {
        public TestEnvironmentStateProvider(ILinkCache<ISkyrimMod, ISkyrimModGetter> linkCache)
        {
            LinkCache = linkCache;
        }

        public ILinkCache<ISkyrimMod, ISkyrimModGetter> LinkCache { get; }

        public ILoadOrderGetter<IModListingGetter<ISkyrimModGetter>> LoadOrder => throw new NotImplementedException();
        public ISkyrimMod OutputMod => throw new NotImplementedException();
        public DirectoryPath ExtraSettingsDataPath => throw new NotImplementedException();
        public DirectoryPath InternalDataPath => throw new NotImplementedException();
        public DirectoryPath DataFolderPath { get; set; }
        public EnvironmentMode RunMode => throw new NotImplementedException();
        public LogMode LoggerMode => throw new NotImplementedException();
        public string LogFolderPath => throw new NotImplementedException();
        public string OutputModName { get; set; } = string.Empty;
        public SkyrimRelease SkyrimVersion => throw new NotImplementedException();
        public string CreationClubListingsFilePath => throw new NotImplementedException();
        public string LoadOrderFilePath => throw new NotImplementedException();
        public List<string> StartUpLog { get; set; } = new();
    }
}
