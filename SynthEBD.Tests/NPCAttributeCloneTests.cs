using FluentAssertions;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;
using Xunit;

namespace SynthEBD.Tests;

public class NPCAttributeCloneTests
{
    // B22: every typed NPCAttribute*.CloneAsNew dropped the Not negation, most shared their FormKey/ModKey/label
    // collection with the source by reference, and NPCAttributeMisc additionally dropped Mood/Aggression/EvalGender/
    // NPCGender. These clones run in the patcher's subgroup flattening of probability-weight modifiers, so a
    // negated or Misc attribute mis-targeted NPCs after flattening. The fix copies Not, deep-copies every
    // collection, and copies the Misc value fields.
    private static readonly FormKey Fk1 = FormKey.Factory("000801:Skyrim.esm");
    private static readonly FormKey Fk2 = FormKey.Factory("000802:Dawnguard.esm"); // distinct mod so Fk2.ModKey differs from Fk1.ModKey

    private static List<ITypedNPCAttribute> AllTypesWithNot() => new()
    {
        new NPCAttributeClass { Not = true, FormKeys = new() { Fk1 } },
        new NPCAttributeCustom { Not = true, CustomType = CustomAttributeType.Text, ValueStr = "x", Comparator = "==" },
        new NPCAttributeFaceTexture { Not = true, FormKeys = new() { Fk1 } },
        new NPCAttributeFactions { Not = true, FormKeys = new() { Fk1 } },
        new NPCAttributeGroup { Not = true, SelectedLabels = new() { "G" } },
        new NPCAttributeKeyword { Not = true, FormKeys = new() { Fk1 } },
        new NPCAttributeMisc { Not = true },
        new NPCAttributeMod { Not = true, ModKeys = new() { Fk1.ModKey } },
        new NPCAttributeNPC { Not = true, FormKeys = new() { Fk1 } },
        new NPCAttributeRace { Not = true, FormKeys = new() { Fk1 } },
        new NPCAttributeVoiceType { Not = true, FormKeys = new() { Fk1 } },
    };

    [Fact]
    public void CloneAsNew_PreservesNot_ForEveryType()
    {
        foreach (var attr in AllTypesWithNot())
        {
            var clone = NPCAttribute.CloneAsNew(attr);
            clone.Not.Should().BeTrue($"the {attr.Type} clone must preserve the Not negation");
        }
    }

    [Fact]
    public void CloneAsNew_FormKeySet_IsIndependentCopy()
    {
        var original = new NPCAttributeClass { FormKeys = new() { Fk1 } };
        var clone = NPCAttributeClass.CloneAsNew(original);

        clone.FormKeys.Should().NotBeSameAs(original.FormKeys);
        clone.FormKeys.Add(Fk2);
        original.FormKeys.Should().NotContain(Fk2);
    }

    [Fact]
    public void CloneAsNew_ModKeySet_IsIndependentCopy()
    {
        var original = new NPCAttributeMod { ModKeys = new() { Fk1.ModKey } };
        var clone = NPCAttributeMod.CloneAsNew(original);

        clone.ModKeys.Should().NotBeSameAs(original.ModKeys);
        clone.ModKeys.Add(Fk2.ModKey);
        original.ModKeys.Should().NotContain(Fk2.ModKey);
    }

    [Fact]
    public void CloneAsNew_GroupLabels_IsIndependentCopy()
    {
        var original = new NPCAttributeGroup { SelectedLabels = new() { "A" } };
        var clone = NPCAttributeGroup.CloneAsNew(original);

        clone.SelectedLabels.Should().NotBeSameAs(original.SelectedLabels);
        clone.SelectedLabels.Add("B");
        original.SelectedLabels.Should().NotContain("B");
    }

    [Fact]
    public void CloneAsNew_Custom_DeepCopiesRecordValues()
    {
        var original = new NPCAttributeCustom { CustomType = CustomAttributeType.Record, ValueFKs = new() { Fk1 } };
        var clone = NPCAttributeCustom.CloneAsNew(original);

        clone.ValueFKs.Should().NotBeSameAs(original.ValueFKs);
        clone.ValueFKs.Add(Fk2);
        original.ValueFKs.Should().NotContain(Fk2);
    }

    [Fact]
    public void CloneAsNew_Misc_CopiesMoodAggressionAndGender()
    {
        var nonDefaultMood = Enum.GetValues<Mood>().First(m => m != Mood.Neutral);
        var nonDefaultAggression = Enum.GetValues<Aggression>().First(a => a != Aggression.Unaggressive);

        var original = new NPCAttributeMisc
        {
            Not = true,
            EvalMood = true, Mood = nonDefaultMood,
            EvalAggression = true, Aggression = nonDefaultAggression,
            EvalGender = true, NPCGender = Gender.Male,
        };

        var clone = NPCAttributeMisc.CloneAsNew(original);

        clone.Not.Should().BeTrue();
        clone.Mood.Should().Be(nonDefaultMood);
        clone.Aggression.Should().Be(nonDefaultAggression);
        clone.EvalGender.Should().BeTrue();
        clone.NPCGender.Should().Be(Gender.Male);
    }

    [Fact]
    public void CloneAsNew_NPCAttribute_PreservesNegatedSubAttribute()
    {
        var attribute = new NPCAttribute();
        attribute.SubAttributes.Add(new NPCAttributeRace { Not = true, FormKeys = new() { Fk1 } });

        var clone = NPCAttribute.CloneAsNew(attribute);

        clone.SubAttributes.Single().Not.Should().BeTrue();
    }
}
