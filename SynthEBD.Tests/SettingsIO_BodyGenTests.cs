using FluentAssertions;
using Xunit;

namespace SynthEBD.Tests;

public class SettingsIO_BodyGenTests
{
    // B18: the load-time General->local attribute-group merge ran two loops (one per gender bucket), but the
    // "female" loop iterated loadedPacks.Male, so male configs were merged twice (idempotent) and female configs
    // never got General groups seeded into their local set. When OverwritePluginAttGroups is off, a female config
    // that references a General-only attribute-group label then fails to resolve it. The merge body was extracted
    // into AddMissingAttributeGroups and run over Male.Concat(Female); these tests pin its semantics.

    [Fact]
    public void AddMissingAttributeGroups_MissingLabel_AddedAsIndependentCopy()
    {
        var target = new HashSet<AttributeGroup>();
        var general = new AttributeGroup { Label = "Vampires" };
        var generalGroups = new List<AttributeGroup> { general };

        SettingsIO_BodyGen.AddMissingAttributeGroups(target, generalGroups);

        target.Should().ContainSingle(g => g.Label == "Vampires");
        var added = target.Single();
        // Must be a fresh copy, not the General instance, so later local edits do not mutate General settings.
        added.Should().NotBeSameAs(general);
        added.Attributes.Should().NotBeSameAs(general.Attributes);
    }

    [Fact]
    public void AddMissingAttributeGroups_ExistingLabel_NotDuplicatedAndLocalPreserved()
    {
        var local = new AttributeGroup { Label = "Vampires" };
        var target = new HashSet<AttributeGroup> { local };
        var generalGroups = new List<AttributeGroup> { new AttributeGroup { Label = "Vampires" } };

        SettingsIO_BodyGen.AddMissingAttributeGroups(target, generalGroups);

        target.Should().ContainSingle(g => g.Label == "Vampires");
        // Local definition wins on collision: the original instance is kept, not replaced by the General one.
        target.Single().Should().BeSameAs(local);
    }

    [Fact]
    public void AddMissingAttributeGroups_EmptyGeneral_TargetUnchanged()
    {
        var local = new AttributeGroup { Label = "Vampires" };
        var target = new HashSet<AttributeGroup> { local };

        SettingsIO_BodyGen.AddMissingAttributeGroups(target, new List<AttributeGroup>());

        target.Should().ContainSingle().Which.Should().BeSameAs(local);
    }
}
