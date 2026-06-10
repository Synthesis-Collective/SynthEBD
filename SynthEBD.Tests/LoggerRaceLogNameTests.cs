using FluentAssertions;
using Xunit;

namespace SynthEBD.Tests;

public class LoggerRaceLogNameTests
{
    // B57: GetRaceLogString's special-case race checks were fk.Equals(Mutagen...Race.X), but those Race.X
    // members are FormLink<IRaceGetter>, not FormKey, so the comparison was always false and the curated
    // names never appeared in verbose logs (the races fell through to their record name/EditorID). The lookup
    // is now a FormKey-keyed dictionary. These assert the curated names resolve (and a normal race does not),
    // which would have failed against the old FormKey-vs-FormLink compare.
    [Fact]
    public void SpecialCaseRaces_ResolveCuratedNames()
    {
        Logger.TryGetSpecialCaseRaceLogName(Mutagen.Bethesda.FormKeys.SkyrimSE.Skyrim.Race.DA13AfflictedRace.FormKey, out var afflicted).Should().BeTrue();
        afflicted.Should().Be("Afflicted");

        Logger.TryGetSpecialCaseRaceLogName(Mutagen.Bethesda.FormKeys.SkyrimSE.Skyrim.Race.NordRaceAstrid.FormKey, out var astrid).Should().BeTrue();
        astrid.Should().Be("Astrid Race");

        Logger.TryGetSpecialCaseRaceLogName(Mutagen.Bethesda.FormKeys.SkyrimSE.Dawnguard.Race.SnowElfRace.FormKey, out var snowElf).Should().BeTrue();
        snowElf.Should().Be("Snow Elf");

        Logger.TryGetSpecialCaseRaceLogName(Mutagen.Bethesda.FormKeys.SkyrimSE.Dawnguard.Race.DLC1NordRace.FormKey, out var dgNord).Should().BeTrue();
        dgNord.Should().Be("Nord (Dawnguard)");

        Logger.TryGetSpecialCaseRaceLogName(Mutagen.Bethesda.FormKeys.SkyrimSE.Dragonborn.Race.DLC2MiraakRace.FormKey, out var miraak).Should().BeTrue();
        miraak.Should().Be("Nord (Miraak)");
    }

    [Fact]
    public void NormalRace_HasNoSpecialCaseName()
    {
        Logger.TryGetSpecialCaseRaceLogName(Mutagen.Bethesda.FormKeys.SkyrimSE.Skyrim.Race.NordRace.FormKey, out _).Should().BeFalse();
    }
}
