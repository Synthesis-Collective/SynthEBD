using FluentAssertions;
using Xunit;

namespace SynthEBD.Tests;

public class SettingsIO_MiscTests
{
    // B19: LoadUpdateLog's fallback branch checked File.Exists(GetFallBackPath(ConsistencyPath)) but loaded
    // GetFallBackPath(UpdateLogPath) — it checked one file and loaded another, so the update-log fallback was
    // loaded based on whether the consistency backup existed. The primary/fallback selection was extracted into
    // SelectExistingPath, which ties the existence check and the returned path to the same arguments. The
    // predicate is injectable, so these tests need no disk.
    [Fact]
    public void SelectExistingPath_PrimaryExists_ReturnsPrimary()
    {
        SettingsIO_Misc.SelectExistingPath("primary.json", "fallback.json", p => p == "primary.json")
            .Should().Be("primary.json");
    }

    [Fact]
    public void SelectExistingPath_PrimaryMissingFallbackExists_ReturnsFallback()
    {
        SettingsIO_Misc.SelectExistingPath("primary.json", "fallback.json", p => p == "fallback.json")
            .Should().Be("fallback.json");
    }

    [Fact]
    public void SelectExistingPath_NeitherExists_ReturnsNull()
    {
        SettingsIO_Misc.SelectExistingPath("primary.json", "fallback.json", _ => false)
            .Should().BeNull();
    }

    [Fact]
    public void SelectExistingPath_BothExist_PrefersPrimary()
    {
        SettingsIO_Misc.SelectExistingPath("primary.json", "fallback.json", _ => true)
            .Should().Be("primary.json");
    }
}
