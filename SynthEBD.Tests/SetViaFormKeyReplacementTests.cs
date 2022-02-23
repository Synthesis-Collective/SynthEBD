using System.Linq;
using FluentAssertions;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Skyrim;
using Xunit;

namespace SynthEBD.Tests;

public class SetViaFormKeyReplacementTests
{
    [Fact]
    public void ArrayDynamic()
    {
        var mod = new SkyrimMod(ModKey.Null, SkyrimGameType.SkyrimSE);
        var arm = mod.Armors.AddNew();
        arm.Armature.Add(mod.ArmorAddons.AddNew());
        dynamic list = arm.Armature;
        dynamic addon = new ArmorAddon(mod);
        RecordGenerator.SetViaFormKeyReplacement(addon, list, "[0]");
        arm.Armature.First().FormKey.Should().Be(addon.FormKey);
    }
    
    [Fact]
    public void ArrayStraight()
    {
        var mod = new SkyrimMod(ModKey.Null, SkyrimGameType.SkyrimSE);
        var arm = mod.Armors.AddNew();
        arm.Armature.Add(mod.ArmorAddons.AddNew());
        var list = arm.Armature;
        var addon = new ArmorAddon(mod);
        RecordGenerator.SetViaFormKeyReplacement(addon, list, "[0]");
        arm.Armature.First().FormKey.Should().Be(addon.FormKey);
    }
    
    [Fact]
    public void SingleDynamic()
    {
        var mod = new SkyrimMod(ModKey.Null, SkyrimGameType.SkyrimSE);
        var arm = mod.Armors.AddNew();
        arm.Race.SetTo(mod.Races.AddNew());
        dynamic dyn = arm;
        dynamic race = new Race(mod);
        RecordGenerator.SetViaFormKeyReplacement(race, dyn, nameof(Armor.Race));
        arm.Race.FormKey.Should().Be(race.FormKey);
    }
    
    [Fact]
    public void SingleStraight()
    {
        var mod = new SkyrimMod(ModKey.Null, SkyrimGameType.SkyrimSE);
        var arm = mod.Armors.AddNew();
        arm.Race.SetTo(mod.Races.AddNew());
        var race = new Race(mod);
        RecordGenerator.SetViaFormKeyReplacement(race, arm, nameof(Armor.Race));
        arm.Race.FormKey.Should().Be(race.FormKey);
    }
}