using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Xunit;

namespace SynthEBD.Tests;

// M1: fixing the "Mildy"->"Mildly" typo renames the default MatureFace attribute-group LABEL. Attribute groups
// are referenced by label string (NPCAttributeGroup.SelectedLabels resolved against AttributeGroup.Label), so
// the rename must rewrite every reference too or it silently desyncs. AttributeGroupLabelMigrator is the pure,
// model-side rewrite used at config-install time and is the testable embodiment of the migration's rewrite logic
// (the UpdateHandler migration renames the equivalent VM definitions, whose references are live pointers).
public class AttributeGroupLabelMigratorTests
{
    private const string OldLabel = "Can Get Mildy Older Face";
    private const string NewLabel = "Can Get Mildly Older Face";

    private static NPCAttribute GroupRef(params string[] labels) =>
        new() { SubAttributes = new() { new NPCAttributeGroup { SelectedLabels = new(labels) } } };

    private static IEnumerable<string> SelectedLabelsIn(IEnumerable<NPCAttribute> attributes) =>
        attributes.SelectMany(a => a.SubAttributes.OfType<NPCAttributeGroup>()).SelectMany(g => g.SelectedLabels);

    private static IEnumerable<string> ReferencesIn(AssetPack.Subgroup subgroup)
    {
        foreach (var l in SelectedLabelsIn(subgroup.AllowedAttributes)) { yield return l; }
        foreach (var l in SelectedLabelsIn(subgroup.DisallowedAttributes)) { yield return l; }
        foreach (var m in subgroup.ProbabilityWeightModifiers) { foreach (var l in SelectedLabelsIn(new[] { m.Attribute })) { yield return l; } }
        foreach (var child in subgroup.Subgroups) { foreach (var l in ReferencesIn(child)) { yield return l; } }
    }

    private static IEnumerable<string> AllReferencesIn(AssetPack pack)
    {
        foreach (var l in SelectedLabelsIn(pack.DistributionRules.AllowedAttributes)) { yield return l; }
        foreach (var l in SelectedLabelsIn(pack.DistributionRules.DisallowedAttributes)) { yield return l; }
        foreach (var m in pack.DistributionRules.ProbabilityWeightModifiers) { foreach (var l in SelectedLabelsIn(new[] { m.Attribute })) { yield return l; } }
        foreach (var sg in pack.Subgroups) { foreach (var l in ReferencesIn(sg)) { yield return l; } }
        foreach (var r in pack.ReplacerGroups) { foreach (var sg in r.Subgroups) { foreach (var l in ReferencesIn(sg)) { yield return l; } } }
    }

    [Fact]
    public void Rename_MapsRenamedLabel_AndPassesOthersThrough()
    {
        AttributeGroupLabelMigrator.Rename(OldLabel).Should().Be(NewLabel);
        AttributeGroupLabelMigrator.Rename(NewLabel).Should().Be(NewLabel);          // idempotent
        AttributeGroupLabelMigrator.Rename("Some Other Group").Should().Be("Some Other Group");
    }

    [Fact]
    public void RewriteAttributeGroupDefinitions_RenamesLabel_AndNestedGroupReferences()
    {
        var definitions = new List<AttributeGroup>
        {
            new() { Label = OldLabel },                                              // the renamed group itself
            new() { Label = "Composite", Attributes = new() { GroupRef(OldLabel) } } // a group whose attribute references the renamed group
        };

        var changed = AttributeGroupLabelMigrator.RewriteAttributeGroupDefinitions(definitions);

        changed.Should().BeTrue();
        definitions.Select(d => d.Label).Should().Contain(NewLabel).And.NotContain(OldLabel);
        SelectedLabelsIn(definitions.SelectMany(d => d.Attributes)).Should().NotContain(OldLabel).And.Contain(NewLabel);
    }

    [Fact]
    public void RewriteAssetPack_RewritesDefinitions_Rules_NestedSubgroups_AndReplacers()
    {
        var pack = new AssetPack
        {
            AttributeGroups = new() { new AttributeGroup { Label = OldLabel } },     // local definition (e.g. merged from General)
            DistributionRules = new()
            {
                AllowedAttributes = new() { GroupRef(OldLabel) }                     // config-wide rule
            },
            Subgroups = new()
            {
                new AssetPack.Subgroup
                {
                    AllowedAttributes = new() { GroupRef(OldLabel) },
                    ProbabilityWeightModifiers = new() { new AttributeWeightModifier { Attribute = GroupRef(OldLabel) } },
                    Subgroups = new()
                    {
                        new AssetPack.Subgroup { DisallowedAttributes = new() { GroupRef(OldLabel) } } // nested - exercises recursion
                    }
                }
            },
            ReplacerGroups = new()
            {
                new AssetReplacerGroup { Subgroups = new() { new AssetPack.Subgroup { AllowedAttributes = new() { GroupRef(OldLabel) } } } }
            }
        };

        var changed = AttributeGroupLabelMigrator.RewriteAssetPack(pack);

        changed.Should().BeTrue();
        pack.AttributeGroups.Single().Label.Should().Be(NewLabel);

        var references = AllReferencesIn(pack).ToList();
        references.Should().HaveCount(5);                                            // rules + subgroup + weight modifier + nested subgroup + replacer
        references.Should().OnlyContain(l => l == NewLabel, "every reference must be rewritten to the new label");

        // and the rewritten references still resolve against the (renamed) local definition
        var definitions = pack.AttributeGroups.Select(g => g.Label).ToHashSet();
        references.Distinct().Should().OnlyContain(l => definitions.Contains(l));
    }

    [Fact]
    public void RewriteAssetPack_NoRenamedLabels_ReturnsFalse_AndLeavesDataUnchanged()
    {
        var pack = new AssetPack
        {
            AttributeGroups = new() { new AttributeGroup { Label = "Untouched Group" } },
            Subgroups = new() { new AssetPack.Subgroup { AllowedAttributes = new() { GroupRef("Untouched Group") } } }
        };

        var changed = AttributeGroupLabelMigrator.RewriteAssetPack(pack);

        changed.Should().BeFalse();
        pack.AttributeGroups.Single().Label.Should().Be("Untouched Group");
        SelectedLabelsIn(pack.Subgroups.Single().AllowedAttributes).Should().ContainSingle().Which.Should().Be("Untouched Group");
    }

    [Fact]
    public void RewriteAttributeSet_RebuildsSet_SoValueLookupsStaySound()
    {
        // SelectedLabels feeds NPCAttributeGroup/NPCAttribute equality, so mutating it in place would leave the
        // element mis-bucketed; the rewrite returns a re-bucketed set in which the renamed attribute is findable.
        var set = new HashSet<NPCAttribute> { GroupRef(OldLabel) };

        AttributeGroupLabelMigrator.RewriteAttributeSet(set, out var rebuilt).Should().BeTrue();

        rebuilt.Should().HaveCount(1);
        rebuilt.Contains(GroupRef(NewLabel)).Should().BeTrue("the rebuilt set must locate the renamed attribute by value");
        rebuilt.Contains(GroupRef(OldLabel)).Should().BeFalse();
    }
}
