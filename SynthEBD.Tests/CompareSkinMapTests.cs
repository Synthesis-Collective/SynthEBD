using FluentAssertions;
using Xunit;

namespace SynthEBD.Tests;

/// <summary>The BodySlide Compare window's persisted skin mapping (<see cref="CompareSkinMap"/>):
/// edits land in the backing settings dictionary, chains resolve, cycles stop.</summary>
public class CompareSkinMapTests
{
    private static (CompareSkinMap Map, Dictionary<string, string> Backing) Create()
    {
        var backing = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        return (new CompareSkinMap(backing), backing);
    }

    [Fact]
    public void Set_WritesThroughToSettings_AndOwnSkinRemovesTheKey()
    {
        var (map, backing) = Create();
        map.Set("CBBE", "CBBE 3BA");
        backing.Should().ContainKey("CBBE").WhoseValue.Should().Be("CBBE 3BA");

        map.Set("cbbe", null);
        backing.Should().BeEmpty();
    }

    [Fact]
    public void Resolve_FollowsChains_AndUnmappedIsItself()
    {
        var (map, _) = Create();
        map.Set("Unified UNP", "BHUNP");
        map.Set("BHUNP", "COCO Body");

        map.Resolve("Unified UNP").Should().Be("COCO Body");
        map.Resolve("CBBE 3BA").Should().Be("CBBE 3BA");
    }

    [Fact]
    public void Resolve_StopsBeforeACycleCloses()
    {
        var (map, _) = Create();
        map.Set("A", "B");
        map.Set("B", "A");

        map.Resolve("A").Should().Be("B");
        map.Resolve("B").Should().Be("A");
    }

    [Fact]
    public void ModAndBodyMappings_ReplaceEachOther()
    {
        var bodies = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var mods = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var map = new CompareSkinMap(bodies, mods);

        map.Set("BHUNP", "Unified UNP");
        map.SetMod("BHUNP", "Fair Skin Complexion UNP");
        bodies.Should().BeEmpty();
        mods.Should().ContainKey("BHUNP").WhoseValue.Should().Be("Fair Skin Complexion UNP");

        map.Set("BHUNP", "Unified UNP");
        mods.Should().BeEmpty();
        bodies.Should().ContainKey("BHUNP");

        map.SetMod("BHUNP", "Some Skin");
        map.Set("BHUNP", null);   // Own skin clears the mod too
        mods.Should().BeEmpty();
        bodies.Should().BeEmpty();
    }

    [Fact]
    public void Resolve_StopsAtABodyTypeWithAMod()
    {
        var map = new CompareSkinMap(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
        map.Set("Unified UNP", "BHUNP");
        map.SetMod("BHUNP", "Some UNP Skin");
        map.Set("Some UNP Skin Body", "Unified UNP");

        map.Resolve("Unified UNP").Should().Be("BHUNP");
        map.GetMod(map.Resolve("Unified UNP")).Should().Be("Some UNP Skin");
    }

    [Fact]
    public void Changed_FiresOnlyOnRealEdits()
    {
        var (map, _) = Create();
        int fired = 0;
        map.Changed += () => fired++;

        map.Set("CBBE", "CBBE 3BA");
        map.Set("CBBE", "cbbe 3ba");   // same target
        map.Set("CBBE 3BA", null);     // nothing to remove
        map.Set("CBBE", "CBBE");       // self = own skin -> removes
        fired.Should().Be(2);
    }
}
