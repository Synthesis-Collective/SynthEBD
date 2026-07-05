using System.Linq;
using FluentAssertions;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Order;
using Xunit;

namespace SynthEBD.Tests;

/// <summary>
/// Guards <see cref="UC_ModKeyPicker.ExtractModKeys"/> - the normalization that feeds the picker's
/// filtered dropdown. Its <c>SearchableMods</c> is loosely typed (object) so callers can bind it to
/// different shapes; a regression here (e.g. dropping the <see cref="ILoadOrderGetter"/> case) leaves
/// the dropdown empty, which is exactly the bug this replaced from Mutagen's picker migration.
/// </summary>
public class ModKeyPickerTests
{
    private static readonly ModKey Skyrim = ModKey.FromFileName("Skyrim.esm");
    private static readonly ModKey Dawnguard = ModKey.FromFileName("Dawnguard.esm");
    private static readonly ModKey MyMod = ModKey.FromFileName("MyMod.esp");

    [Fact]
    public void ExtractModKeys_FromModKeyEnumerable_ReturnsThem()
    {
        var source = new[] { Skyrim, MyMod };

        UC_ModKeyPicker.ExtractModKeys(source).Should().Equal(Skyrim, MyMod);
    }

    [Fact]
    public void ExtractModKeys_FromLoadOrderGetter_ReturnsListedModKeys()
    {
        // VM_BlockedPlugin / VM_NPCAttribute / VM_Settings_General bind an ILoadOrderGetter here.
        ILoadOrderGetter loadOrder = new LoadOrder<IModListingGetter>(new IModListingGetter[]
        {
            new ModListing(Skyrim, enabled: true, modExists: true),
            new ModListing(Dawnguard, enabled: true, modExists: true),
        });

        UC_ModKeyPicker.ExtractModKeys(loadOrder).Should().Equal(Skyrim, Dawnguard);
    }

    [Fact]
    public void ExtractModKeys_FromModListings_ProjectsModKeys()
    {
        var listings = new IModListingGetter[]
        {
            new ModListing(Skyrim, enabled: true, modExists: true),
            new ModListing(MyMod, enabled: false, modExists: true),
        };

        UC_ModKeyPicker.ExtractModKeys(listings).Should().Equal(Skyrim, MyMod);
    }

    [Fact]
    public void ExtractModKeys_FromNull_ReturnsEmpty()
    {
        UC_ModKeyPicker.ExtractModKeys(null).Should().BeEmpty();
    }
}
