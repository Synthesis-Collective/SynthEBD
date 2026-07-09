using FluentAssertions;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Plugins.Order;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Skyrim;
using Noggog;
using Xunit;

namespace SynthEBD.Tests;

/// <summary>
/// Covers <see cref="NPCAttributeSubExpression"/>, the inline anonymous sub-expression enabling
/// parenthetical logic ("x AND (y OR z)"): JSON round-trips (including that legacy attribute JSON
/// is unaffected), the matcher's nested OR evaluation with negation and ForceIf weight forwarding,
/// deep cloning, and opacity under subgroup attribute inheritance.
/// </summary>
public class NPCAttributeSubExpressionTests
{
    private static readonly FormKey NordRace = FormKey.Factory("013746:Skyrim.esm");
    private static readonly FormKey ImperialRace = FormKey.Factory("013744:Skyrim.esm");
    private static readonly FormKey BardClass = FormKey.Factory("01325D:Skyrim.esm");
    private static readonly FormKey WarriorClass = FormKey.Factory("01326F:Skyrim.esm");
    private static readonly FormKey CitizenClass = FormKey.Factory("01326B:Skyrim.esm");

    /// <summary>"Race is Nord AND (Class is Bard OR Class is Warrior)" as one condition.</summary>
    private static NPCAttribute BuildNordAndBardOrWarrior()
    {
        return new NPCAttribute()
        {
            SubAttributes = new()
            {
                new NPCAttributeRace() { FormKeys = new() { NordRace } },
                new NPCAttributeSubExpression()
                {
                    Attributes = new()
                    {
                        new NPCAttribute() { SubAttributes = new() { new NPCAttributeClass() { FormKeys = new() { BardClass } } } },
                        new NPCAttribute() { SubAttributes = new() { new NPCAttributeClass() { FormKeys = new() { WarriorClass } } } },
                    }
                }
            }
        };
    }

    private static Npc MakeNpc(FormKey race, FormKey npcClass)
    {
        var mod = new SkyrimMod(ModKey.Null, SkyrimRelease.SkyrimSE);
        var npc = mod.Npcs.AddNew();
        npc.Race.SetTo(race);
        npc.Class.SetTo(npcClass);
        return npc;
    }

    private static AttributeMatcher BareMatcher() => new(new StubEnvironmentProvider(), null!, null!, null!, null!, null!);

    private static bool Matches(HashSet<NPCAttribute> attributeList, Npc npc, out int forceIfWeight)
    {
        BareMatcher().MatchNPCtoAttributeList(attributeList, npc, null, new HashSet<AttributeGroup>(), false,
            out _, out bool matched, out forceIfWeight, out _, out _, out _, null);
        return matched;
    }

    [Fact]
    public void SubExpression_JsonRoundTrip_PreservesValueEquality()
    {
        var original = new HashSet<NPCAttribute>() { BuildNordAndBardOrWarrior() };

        var json = JSONhandler<HashSet<NPCAttribute>>.Serialize(original, out bool serialized, out string serializeError);
        serialized.Should().BeTrue(serializeError);
        json.Should().Contain("SubExpression");

        var clone = JSONhandler<HashSet<NPCAttribute>>.Deserialize(json, out bool deserialized, out string deserializeError);
        deserialized.Should().BeTrue(deserializeError);
        clone.SetEquals(original).Should().BeTrue();
    }

    // Attribute JSON written before sub-expressions existed must deserialize and re-serialize with
    // identical semantics (and without ever mentioning the new type).
    [Fact]
    public void LegacyAttributeJson_RoundTripsUnchanged()
    {
        const string legacy = """
        [
          {
            "SubAttributes": [
              {
                "Type": "Class",
                "FormKeys": [ "01325D:Skyrim.esm" ],
                "ForceMode": "Restrict",
                "Weighting": 1,
                "Not": false
              },
              {
                "Type": "Faction",
                "FormKeys": [ "09CC97:Skyrim.esm" ],
                "RankMin": -1,
                "RankMax": 100,
                "ForceMode": "ForceIf",
                "Weighting": 2,
                "Not": true
              }
            ]
          }
        ]
        """;

        var loaded = JSONhandler<HashSet<NPCAttribute>>.Deserialize(legacy, out bool deserialized, out string deserializeError);
        deserialized.Should().BeTrue(deserializeError);
        loaded.Should().HaveCount(1);
        loaded.First().SubAttributes.Should().HaveCount(2);

        var rewritten = JSONhandler<HashSet<NPCAttribute>>.Serialize(loaded, out bool serialized, out string serializeError);
        serialized.Should().BeTrue(serializeError);
        rewritten.Should().NotContain("SubExpression");

        var reloaded = JSONhandler<HashSet<NPCAttribute>>.Deserialize(rewritten, out bool redeserialized, out string redeserializeError);
        redeserialized.Should().BeTrue(redeserializeError);
        reloaded.SetEquals(loaded).Should().BeTrue();
    }

    [Theory]
    [InlineData(true, true, false, true)]   // Nord Bard: x true, y true -> match
    [InlineData(true, false, true, true)]   // Nord Warrior: x true, z true -> match
    [InlineData(true, false, false, false)] // Nord Citizen: x true, neither y nor z -> no match
    [InlineData(false, true, false, false)] // Imperial Bard: x false -> no match
    public void Matcher_EvaluatesNestedOrWithinAnd(bool isNord, bool isBard, bool isWarrior, bool expected)
    {
        var race = isNord ? NordRace : ImperialRace;
        var npcClass = isBard ? BardClass : isWarrior ? WarriorClass : CitizenClass;
        var npc = MakeNpc(race, npcClass);

        Matches(new() { BuildNordAndBardOrWarrior() }, npc, out _).Should().Be(expected);
    }

    [Fact]
    public void Matcher_NotSubExpression_InvertsNestedResult()
    {
        var condition = BuildNordAndBardOrWarrior();
        condition.SubAttributes.OfType<NPCAttributeSubExpression>().Single().Not = true;
        var attributeList = new HashSet<NPCAttribute>() { condition };

        // NOT (Bard OR Warrior): the Bard now fails, the Citizen now matches.
        Matches(attributeList, MakeNpc(NordRace, BardClass), out _).Should().BeFalse();
        Matches(attributeList, MakeNpc(NordRace, CitizenClass), out _).Should().BeTrue();
    }

    // A ForceIf sub-expression forwards its forcing into the nested conditions (like a Group
    // reference does): the outer row's weight is multiplied by the nested ForceIf tally.
    [Fact]
    public void Matcher_ForceIfSubExpression_TalliesForwardedWeight()
    {
        var condition = BuildNordAndBardOrWarrior();
        var subExpression = condition.SubAttributes.OfType<NPCAttributeSubExpression>().Single();
        subExpression.ForceMode = AttributeForcing.ForceIf;
        subExpression.Weighting = 2;

        bool matched = Matches(new() { condition }, MakeNpc(NordRace, BardClass), out int forceIfWeight);

        matched.Should().BeTrue(); // the Race row is still a matched restriction
        // Nested recursion: the matched inner row contributes its Weighting (1) under the forwarded
        // ForceIf; the outer row then contributes Weighting (2) x nested tally (1) = 2.
        forceIfWeight.Should().Be(2);
    }

    [Fact]
    public void CloneAsNew_DeepClonesNestedConditions()
    {
        var original = new NPCAttributeSubExpression()
        {
            Attributes = new() { new NPCAttribute() { SubAttributes = new() { new NPCAttributeClass() { FormKeys = new() { BardClass } } } } },
            Not = true,
            Weighting = 3,
            ForceMode = AttributeForcing.ForceIfAndRestrict,
        };

        var clone = NPCAttributeSubExpression.CloneAsNew(original);
        clone.Equals(original).Should().BeTrue();

        // Mutating the clone's nested condition must not affect the original.
        clone.Attributes.First().SubAttributes.OfType<NPCAttributeClass>().Single().FormKeys.Add(WarriorClass);
        original.Attributes.First().SubAttributes.OfType<NPCAttributeClass>().Single().FormKeys.Should().HaveCount(1);
    }

    // Subgroup rule inheritance AND-merges parent and child OR-terms; a sub-expression must ride
    // along as one opaque AND-term.
    [Fact]
    public void InheritAttributes_TreatsSubExpressionAsOpaqueTerm()
    {
        var parent = new HashSet<NPCAttribute>() { new() { SubAttributes = new() { new NPCAttributeKeyword() { FormKeys = new() { CitizenClass } } } } };
        var child = new HashSet<NPCAttribute>() { BuildNordAndBardOrWarrior() };

        var merged = NPCAttribute.InheritAttributes(parent, child);

        merged.Should().HaveCount(1);
        var expected = parent.First().SubAttributes.Concat(child.First().SubAttributes).ToHashSet();
        merged.First().SubAttributes.SetEquals(expected).Should().BeTrue();
    }

    /// <summary>Minimal environment stub: the matcher only touches <c>LinkCache</c> for log-string
    /// resolution (unused when detailed logging is off), so everything can be inert.</summary>
    private class StubEnvironmentProvider : IEnvironmentStateProvider
    {
        public ILoadOrderGetter<IModListingGetter<ISkyrimModGetter>> LoadOrder => null!;
        public ILinkCache<ISkyrimMod, ISkyrimModGetter> LinkCache => null!;
        public DirectoryPath ExtraSettingsDataPath => default;
        public DirectoryPath InternalDataPath => default;
        public DirectoryPath DataFolderPath { get; set; } = default;
        public EnvironmentMode RunMode => EnvironmentMode.Standalone;
        public LogMode LoggerMode => default;
        public string LogFolderPath => string.Empty;
        public string OutputModName { get; set; } = "SynthEBD";
        public SkyrimRelease SkyrimVersion => SkyrimRelease.SkyrimSE;
        public string CreationClubListingsFilePath => string.Empty;
        public string LoadOrderFilePath => string.Empty;
        public List<string> StartUpLog { get; set; } = new();
    }
}
