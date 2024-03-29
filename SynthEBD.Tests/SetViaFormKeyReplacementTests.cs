using FluentAssertions;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;
using Xunit;

namespace SynthEBD.Tests;

public class SetViaFormKeyReplacementTests
{
    private readonly RecordGenerator _recordGenerator;
    public SetViaFormKeyReplacementTests(RecordGenerator recordGenerator)
    {
        _recordGenerator = recordGenerator;
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
        _recordGenerator.SetViaFormKeyReplacement(addon, list, "[0]", npc);
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
        _recordGenerator.SetViaFormKeyReplacement(addon, list, "[0]", npc);
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
        _recordGenerator.SetViaFormKeyReplacement(race, dyn, nameof(Armor.Race), npc);
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
        _recordGenerator.SetViaFormKeyReplacement(race, arm, nameof(Armor.Race), npc);
        arm.Race.FormKey.Should().Be(race.FormKey);
    }
}