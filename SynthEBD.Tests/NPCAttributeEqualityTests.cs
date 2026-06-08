using FluentAssertions;
using Mutagen.Bethesda.Plugins;
using Xunit;

namespace SynthEBD.Tests;

public class NPCAttributeEqualityTests
{
    // B23: NPCAttribute.Equals compared the SubAttributes HashSet positionally (order-dependent), and the typed
    // Equals methods ignored fields their GetHashCode included. The audit made each typed Equals compare all
    // hashed fields (adding the previously-ignored match criteria Comparator/ModActionType/gender), added
    // object.Equals(object) overrides so the family has consistent value equality, and reimplemented
    // NPCAttribute.Equals via SetEquals.
    private static readonly FormKey Nord = FormKey.Factory("013746:Skyrim.esm");
    private static readonly FormKey Orc = FormKey.Factory("013745:Skyrim.esm");
    private static readonly FormKey Vampire = FormKey.Factory("0A82BB:Skyrim.esm");

    [Fact]
    public void NPCAttribute_Equals_IsOrderIndependent_OuterAndInner()
    {
        // {Race[Nord, Orc], Keyword[Vampire]} vs {Keyword[Vampire], Race[Orc, Nord]} — both sub-attribute order
        // and inner FormKey order differ, yet they describe the same rule.
        var a = new NPCAttribute();
        a.SubAttributes.Add(new NPCAttributeRace { FormKeys = new() { Nord, Orc } });
        a.SubAttributes.Add(new NPCAttributeKeyword { FormKeys = new() { Vampire } });

        var b = new NPCAttribute();
        b.SubAttributes.Add(new NPCAttributeKeyword { FormKeys = new() { Vampire } });
        b.SubAttributes.Add(new NPCAttributeRace { FormKeys = new() { Orc, Nord } });

        a.Equals(b).Should().BeTrue();
        a.GetHashCode().Should().Be(b.GetHashCode()); // equal objects must hash equal (SetEquals relies on this)
    }

    [Fact]
    public void NPCAttribute_Equals_DistinguishesDifferentSubAttributes()
    {
        var a = new NPCAttribute();
        a.SubAttributes.Add(new NPCAttributeRace { FormKeys = new() { Nord } });

        var b = new NPCAttribute();
        b.SubAttributes.Add(new NPCAttributeRace { FormKeys = new() { Orc } });

        a.Equals(b).Should().BeFalse();
    }

    [Fact]
    public void CustomAttribute_Equality_NowHonorsComparator()
    {
        var eq = new NPCAttributeCustom { Path = "X", ValueStr = "5", Comparator = "==" };
        var ne = new NPCAttributeCustom { Path = "X", ValueStr = "5", Comparator = "!=" };

        // Opposite comparators match opposite NPCs; they must not compare equal.
        eq.Equals(ne).Should().BeFalse();
    }

    [Fact]
    public void ModAttribute_Equality_NowHonorsModActionType()
    {
        var createdBy = new NPCAttributeMod { ModKeys = new() { Nord.ModKey }, ModActionType = ModAttributeEnum.CreatedBy };
        var patchedBy = new NPCAttributeMod { ModKeys = new() { Nord.ModKey }, ModActionType = ModAttributeEnum.PatchedBy };

        createdBy.Equals(patchedBy).Should().BeFalse();
    }

    [Fact]
    public void MiscAttribute_Equality_NowHonorsGender()
    {
        var male = new NPCAttributeMisc { EvalGender = true, NPCGender = Gender.Male };
        var female = new NPCAttributeMisc { EvalGender = true, NPCGender = Gender.Female };

        male.Equals(female).Should().BeFalse();
    }

    [Fact]
    public void TypedAttribute_ObjectEquals_GivesHashSetValueSemantics()
    {
        // With object.Equals overridden, a HashSet<ITypedNPCAttribute> dedups value-equal entries.
        var set = new HashSet<ITypedNPCAttribute>
        {
            new NPCAttributeRace { FormKeys = new() { Nord } },
            new NPCAttributeRace { FormKeys = new() { Nord } },
        };

        set.Should().HaveCount(1);
    }

    [Fact]
    public void TypedAttribute_ObjectEquals_DifferentTypeIsNotEqual()
    {
        ITypedNPCAttribute race = new NPCAttributeRace { FormKeys = new() { Nord } };
        ITypedNPCAttribute keyword = new NPCAttributeKeyword { FormKeys = new() { Nord } };

        race.Equals((object)keyword).Should().BeFalse();
    }
}
