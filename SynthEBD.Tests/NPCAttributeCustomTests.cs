using FluentAssertions;
using Xunit;

namespace SynthEBD.Tests;

public class NPCAttributeCustomTests
{
    // B24: NPCAttributeCustom.Comparator has no initializer (defaults to null), but GetHashCode called
    // Comparator.GetHashCode() unguarded, so hashing a freshly-constructed Custom attribute threw an NRE.
    [Fact]
    public void GetHashCode_WithNullComparator_DoesNotThrow()
    {
        var attribute = new NPCAttributeCustom(); // Comparator is null by default

        var act = () => attribute.GetHashCode();

        act.Should().NotThrow();
    }
}
