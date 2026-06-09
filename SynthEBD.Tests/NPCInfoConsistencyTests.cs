using System.Collections.Generic;
using FluentAssertions;
using Mutagen.Bethesda.Plugins;
using Xunit;

namespace SynthEBD.Tests;

/// <summary>
/// B40: HeightPatcher (and every other consistency write site) dereferenced
/// npcInfo.ConsistencyNPCAssignment under only a bEnableConsistency guard. The NPCInfo ctor always
/// created one, EXCEPT when the consistency dictionary held a key mapped to a literal null (a
/// corrupted/hand-edited consistency file): ContainsKey was true so the create-fresh branch was
/// skipped, leaving a null that NRE'd downstream. ResolveConsistencyAssignment now guarantees a
/// non-null result, treating a present-null entry like a missing one (a fresh "no consistency"
/// assignment, identical to a first-run NPC -- so it is behavior-preserving).
/// </summary>
public class NPCInfoConsistencyTests
{
    private static readonly FormKey Fk = FormKey.Factory("123456:Skyrim.esm");

    [Fact]
    public void ResolveConsistencyAssignment_PresentNonNull_ReturnsExisting()
    {
        var existing = new NPCAssignment { NPCFormKey = Fk, Height = 1.05f };
        var dict = new Dictionary<string, NPCAssignment> { [Fk.ToString()] = existing };

        var result = NPCInfo.ResolveConsistencyAssignment(dict, Fk, "Lydia");

        result.Should().BeSameAs(existing);
        result.Height.Should().Be(1.05f);
    }

    [Fact]
    public void ResolveConsistencyAssignment_MissingKey_CreatesAndStoresFresh()
    {
        var dict = new Dictionary<string, NPCAssignment>();

        var result = NPCInfo.ResolveConsistencyAssignment(dict, Fk, "Lydia");

        result.Should().NotBeNull();
        result.NPCFormKey.Should().Be(Fk);
        result.DispName.Should().Be("Lydia");
        result.Height.Should().BeNull(); // fresh = no consistency recorded yet
        dict[Fk.ToString()].Should().BeSameAs(result);
    }

    [Fact]
    public void ResolveConsistencyAssignment_PresentButNull_OverwritesWithFresh()
    {
        // The bug class: a consistency file with a null value for a present key.
        var dict = new Dictionary<string, NPCAssignment> { [Fk.ToString()] = null };

        var result = NPCInfo.ResolveConsistencyAssignment(dict, Fk, "Lydia");

        result.Should().NotBeNull();
        result.NPCFormKey.Should().Be(Fk);
        dict[Fk.ToString()].Should().BeSameAs(result); // null entry replaced via indexer, not Add-thrown
    }
}
