using System.Collections.Generic;
using System.Linq;

namespace SynthEBD;

/// <summary>
/// Backward-compatibility helper for renamed default attribute-group labels. Attribute groups are
/// referenced by label string (a rule's <see cref="NPCAttributeGroup.SelectedLabels"/> resolved against
/// <see cref="AttributeGroup.Label"/> at match time), so renaming a shipped default desyncs every reference
/// unless the old label is rewritten to the new one everywhere it appears.
///
/// <para><see cref="RenamedLabels"/> is the single source of truth: add one entry per renamed default and
/// both consumers pick it up automatically. The version migration (<c>UpdateHandler.UpdateV1070AttributeGroupRename</c>)
/// renames the loaded attribute-group menu view models (group references are live pointers to those definition
/// VMs, so renaming the definition propagates to every reference on serialization); this class handles the
/// pure model side used at config-install time (<see cref="ConfigInstaller"/> works on <see cref="AssetPack"/>
/// models, not VMs) and is the unit-testable embodiment of the rewrite logic.</para>
/// </summary>
public static class AttributeGroupLabelMigrator
{
    /// <summary>
    /// Map of an obsolete default attribute-group label to its current label. Comparison is ordinal to match
    /// the <c>==</c> label resolution used elsewhere. Add an entry here whenever a shipped default
    /// <see cref="AttributeGroup.Label"/> is renamed.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> RenamedLabels = new Dictionary<string, string>
    {
        // M1 (v1.0.7.0): "Mildy" typo fix on DefaultAttributeGroups.MatureFace.
        { "Can Get Mildy Older Face", "Can Get Mildly Older Face" },
    };

    /// <summary>Returns the current label for <paramref name="label"/> if it was renamed, otherwise the input unchanged.</summary>
    public static string Rename(string label) =>
        label != null && RenamedLabels.TryGetValue(label, out var renamed) ? renamed : label;

    /// <summary>
    /// Rewrites every renamed label in an asset-pack config: its local group definitions, its config-wide
    /// distribution rules, and every subgroup in both the main and replacer trees (recursively). Mutates in
    /// place; returns whether anything changed.
    /// </summary>
    public static bool RewriteAssetPack(AssetPack assetPack)
    {
        if (assetPack == null) { return false; }

        bool changed = false;
        changed |= RewriteAttributeGroupDefinitions(assetPack.AttributeGroups);
        changed |= RewriteDistributionRules(assetPack.DistributionRules);
        changed |= RewriteSubgroups(assetPack.Subgroups);
        foreach (var replacer in assetPack.ReplacerGroups)
        {
            changed |= RewriteSubgroups(replacer.Subgroups);
        }
        return changed;
    }

    /// <summary>
    /// Rewrites renamed labels across a set of attribute-group definitions: each group's own
    /// <see cref="AttributeGroup.Label"/> and any renamed group references nested in its attributes. Mutates
    /// in place; returns whether anything changed.
    /// </summary>
    public static bool RewriteAttributeGroupDefinitions(IEnumerable<AttributeGroup> groups)
    {
        if (groups == null) { return false; }

        bool changed = false;
        foreach (var group in groups)
        {
            var renamedLabel = Rename(group.Label);
            if (renamedLabel != group.Label)
            {
                group.Label = renamedLabel;
                changed = true;
            }
            if (RewriteAttributeSet(group.Attributes, out var rebuilt))
            {
                group.Attributes = rebuilt;
                changed = true;
            }
        }
        return changed;
    }

    private static bool RewriteDistributionRules(AssetPack.ConfigDistributionRules rules)
    {
        if (rules == null) { return false; }

        bool changed = false;
        if (RewriteAttributeSet(rules.AllowedAttributes, out var allowed)) { rules.AllowedAttributes = allowed; changed = true; }
        if (RewriteAttributeSet(rules.DisallowedAttributes, out var disallowed)) { rules.DisallowedAttributes = disallowed; changed = true; }
        changed |= RewriteWeightModifiers(rules.ProbabilityWeightModifiers);
        return changed;
    }

    private static bool RewriteSubgroups(IEnumerable<AssetPack.Subgroup> subgroups)
    {
        if (subgroups == null) { return false; }

        bool changed = false;
        foreach (var subgroup in subgroups)
        {
            if (RewriteAttributeSet(subgroup.AllowedAttributes, out var allowed)) { subgroup.AllowedAttributes = allowed; changed = true; }
            if (RewriteAttributeSet(subgroup.DisallowedAttributes, out var disallowed)) { subgroup.DisallowedAttributes = disallowed; changed = true; }
            changed |= RewriteWeightModifiers(subgroup.ProbabilityWeightModifiers);
            changed |= RewriteSubgroups(subgroup.Subgroups);
        }
        return changed;
    }

    private static bool RewriteWeightModifiers(IEnumerable<AttributeWeightModifier> modifiers)
    {
        if (modifiers == null) { return false; }

        bool changed = false;
        foreach (var modifier in modifiers)
        {
            changed |= RewriteAttribute(modifier.Attribute);
        }
        return changed;
    }

    /// <summary>
    /// Rewrites renamed group labels referenced by any Group sub-attribute within a set of NPC attributes,
    /// returning a freshly rebuilt set via <paramref name="rebuilt"/> when anything changed (a rebuild keeps
    /// the <see cref="HashSet{T}"/> correctly bucketed after the contained group references, which feed
    /// <see cref="NPCAttribute"/> equality, change). Returns whether anything changed; when false,
    /// <paramref name="rebuilt"/> is the unchanged input.
    /// </summary>
    public static bool RewriteAttributeSet(HashSet<NPCAttribute> attributes, out HashSet<NPCAttribute> rebuilt)
    {
        rebuilt = attributes;
        if (attributes == null) { return false; }

        bool changed = false;
        foreach (var attribute in attributes)
        {
            if (RewriteAttribute(attribute)) { changed = true; }
        }
        if (changed)
        {
            // Element hashes changed, so re-bucket the outer set. Materialize to a List first: the
            // HashSet(IEnumerable) copy-constructor fast-path would otherwise clone the now-stale buckets
            // verbatim when the source is itself a HashSet.
            rebuilt = new HashSet<NPCAttribute>(attributes.ToList());
        }
        return changed;
    }

    private static bool RewriteAttribute(NPCAttribute attribute)
    {
        if (attribute?.SubAttributes == null) { return false; }

        bool changed = false;
        foreach (var groupSub in attribute.SubAttributes.OfType<NPCAttributeGroup>())
        {
            if (groupSub.SelectedLabels == null || groupSub.SelectedLabels.Count == 0) { continue; }

            var renamed = groupSub.SelectedLabels.Select(Rename).ToHashSet();
            if (!renamed.SetEquals(groupSub.SelectedLabels))
            {
                groupSub.SelectedLabels = renamed;
                changed = true;
            }
        }
        if (changed)
        {
            // The group's hash changed, so re-bucket; materialize to a List to defeat the HashSet copy-constructor
            // fast-path (see RewriteAttributeSet) which would otherwise preserve the stale bucketing.
            attribute.SubAttributes = new HashSet<ITypedNPCAttribute>(attribute.SubAttributes.ToList());
        }
        return changed;
    }
}
