using System.Reflection;
using FluentAssertions;
using Xunit;

namespace SynthEBD.Tests;

public class DefaultRaceAliasesTests
{
    // B20: the default RaceAliases (Settings_General, and the 1.0.4.8 backfill set in UpdateHandler) listed
    // DefaultRaceAliases.RaceAliasCotR_Imperial twice and omitted RaceAliasCotR_ImperialVampire, so CotR Imperial
    // Vampire NPCs were never aliased to vanilla ImperialRaceVampire and got skipped by the patcher. These tests
    // assert every defined CotR alias is present in the fresh-install defaults and that no source race is aliased
    // twice — catching both the omission and the duplicate, and any future CotR alias that gets left out.
    private static List<RaceAlias> AllCotRDefaultAliases()
    {
        return typeof(DefaultRaceAliases)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.FieldType == typeof(RaceAlias) && f.Name.StartsWith("RaceAliasCotR_"))
            .Select(f => (RaceAlias)f.GetValue(null)!)
            .ToList();
    }

    [Fact]
    public void DefaultRaceAliases_IncludeEveryDefinedCotRAlias()
    {
        var defaults = new Settings_General().RaceAliases;

        defaults.Should().Contain(AllCotRDefaultAliases());
    }

    [Fact]
    public void DefaultRaceAliases_DoNotAliasTheSameSourceRaceTwice()
    {
        var defaults = new Settings_General().RaceAliases;

        defaults.Select(a => a.Race).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void DefaultRaceAliases_IncludeImperialVampireSpecifically()
    {
        var defaults = new Settings_General().RaceAliases;

        defaults.Should().Contain(DefaultRaceAliases.RaceAliasCotR_ImperialVampire);
    }
}
