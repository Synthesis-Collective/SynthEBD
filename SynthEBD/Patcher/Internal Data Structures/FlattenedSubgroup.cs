using DynamicData.Aggregation;
using Mutagen.Bethesda.Plugins;
using Noggog;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using static SynthEBD.AssetPack;

namespace SynthEBD;

/// <summary>
/// Runtime (flattened) counterpart of an <see cref="AssetPack.Subgroup"/>. Represents a single bottom-level
/// subgroup with its rules already resolved: race groupings merged into allowed/disallowed form-key sets,
/// required/excluded subgroup references mapped to id dictionaries, body-shape descriptors mapped to dictionaries,
/// and (after <see cref="FlattenSubgroups"/>) all ancestor rules inherited. Implements <see cref="IProbabilityWeighted"/>
/// so it can participate in weighted random selection.
/// </summary>
[DebuggerDisplay("{Id}: {Name}")]
public class FlattenedSubgroup : IProbabilityWeighted
{
    private readonly DictionaryMapper _dictionaryMapper;
    /// <summary>
    /// Builds a flattened subgroup from a settings-model <see cref="Subgroup"/> template: merges race groupings
    /// into allowed/disallowed sets (and trims disallowed from allowed), clones attribute and weight rules, and
    /// maps required/excluded subgroups and body-shape descriptors into dictionaries. Inheritance from parents is
    /// applied separately by <see cref="FlattenSubgroups"/>.
    /// </summary>
    public FlattenedSubgroup(Subgroup template, List<RaceGrouping> raceGroupingList, List<Subgroup> subgroupHierarchy, FlattenedAssetPack parent, DictionaryMapper dictionaryMapper)
    {
        _dictionaryMapper = dictionaryMapper;

        Id = template.ID;
        Name = template.Name;
        DistributionEnabled = template.DistributionEnabled;
        AllowedRaces = RaceGrouping.MergeRaceAndGroupingList(template.AllowedRaceGroupings, raceGroupingList, template.AllowedRaces);
        if (AllowedRaces.Count == 0) { AllowedRacesIsEmpty = true; }
        else { AllowedRacesIsEmpty = false; }
        DisallowedRaces = RaceGrouping.MergeRaceAndGroupingList(template.DisallowedRaceGroupings, raceGroupingList, template.DisallowedRaces);
        AllowedRaces = AllowedDisallowedCombiners.TrimDisallowedRacesFromAllowed(AllowedRaces, DisallowedRaces);
        AllowedAttributes = new HashSet<NPCAttribute>(template.AllowedAttributes);
        DisallowedAttributes = new HashSet<NPCAttribute>(template.DisallowedAttributes);
        ProbabilityWeightModifiers = template.ProbabilityWeightModifiers.Select(AttributeWeightModifier.CloneAsNew).ToList();
        AllowUnique = template.AllowUnique;
        AllowNonUnique = template.AllowNonUnique;
        RequiredSubgroupIDs = _dictionaryMapper.RequiredOrExcludedSubgroupsToDictionary(template.RequiredSubgroups, subgroupHierarchy);
        ExcludedSubgroupIDs = _dictionaryMapper.RequiredOrExcludedSubgroupsToDictionary(template.ExcludedSubgroups, subgroupHierarchy);
        AddKeywords = new HashSet<string>(template.AddKeywords);
        ProbabilityWeighting = template.ProbabilityWeighting;
        Paths = new HashSet<FilePathReplacement>(template.Paths);
        AllowedBodyGenDescriptors = DictionaryMapper.BodyShapeDescriptorsToDictionary(template.AllowedBodyGenDescriptors);
        AllowedBodyGenMatchMode = template.AllowedBodyGenMatchMode;
        DisallowedBodyGenDescriptors = DictionaryMapper.BodyShapeDescriptorsToDictionary(template.DisallowedBodyGenDescriptors);
        DisallowedBodyGenMatchMode = template.DisallowedBodyGenMatchMode;
        AllowedBodySlideDescriptors = DictionaryMapper.BodyShapeDescriptorsToDictionary(template.AllowedBodySlideDescriptors);
        AllowedBodySlideMatchMode = template.AllowedBodySlideMatchMode;
        DisallowedBodySlideDescriptors = DictionaryMapper.BodyShapeDescriptorsToDictionary(template.DisallowedBodySlideDescriptors);
        DisallowedBodySlideMatchMode = template.DisallowedBodySlideMatchMode;
        PrioritizedBodySlideDescriptors = template.PrioritizedBodySlideDescriptors.GroupBy(x => x.Category).ToDictionary(g => g.Key, g => g.ToList());
        WeightRange = template.WeightRange.Clone();
        ContainedSubgroupIDs = new List<string> { Id };
        ContainedSubgroupNames = new List<string> { Name };
        ParentAssetPack = parent;
    }
    /// <summary>The subgroup's ID.</summary>
    public string Id { get; set; }
    /// <summary>The subgroup's display name.</summary>
    public string Name { get; set; }
    /// <summary>Whether this subgroup participates in random distribution (can be forced off by an ancestor).</summary>
    public bool DistributionEnabled { get; set; }
    /// <summary>Allowed race form keys (race groupings merged in, disallowed races trimmed out).</summary>
    public HashSet<FormKey> AllowedRaces { get; set; }
    /// <summary>Disallowed race form keys (race groupings merged in).</summary>
    public HashSet<FormKey> DisallowedRaces { get; set; }
    /// <summary>Distinguishes an initially empty allowed-races set (all races valid) from one emptied by trimming disallowed races (subgroup invalid).</summary>
    public bool AllowedRacesIsEmpty { get; set; } // distinguishes between initially empty (All races valid) vs. empty after pruning of Disallowed Races (subgroup is invalid)
    /// <summary>Attributes an NPC must match for this subgroup to be allowed.</summary>
    public HashSet<NPCAttribute> AllowedAttributes { get; set; }
    /// <summary>Attributes that, if matched, exclude this subgroup.</summary>
    public HashSet<NPCAttribute> DisallowedAttributes { get; set; }
    /// <summary>Attribute-based multiplicative modifiers applied to this subgroup's selection weight.</summary>
    public List<AttributeWeightModifier> ProbabilityWeightModifiers { get; set; } = new();
    /// <summary>Whether this subgroup may be assigned to unique NPCs.</summary>
    public bool AllowUnique { get; set; }
    /// <summary>Whether this subgroup may be assigned to non-unique NPCs.</summary>
    public bool AllowNonUnique { get; set; }
    /// <summary>Required co-subgroups keyed by top-level position; an assignment must include one of the listed IDs at each position.</summary>
    public Dictionary<int, HashSet<string>> RequiredSubgroupIDs { get; set; }
    /// <summary>Excluded co-subgroups keyed by top-level position; an assignment must not include any listed ID.</summary>
    public Dictionary<int, HashSet<string>> ExcludedSubgroupIDs { get; set; }
    /// <summary>Keywords to add to the NPC/record when this subgroup is assigned.</summary>
    public HashSet<string> AddKeywords { get; set; }
    /// <summary>Base selection weight for this subgroup (multiplied by ancestors' weights during flattening).</summary>
    public double ProbabilityWeighting { get; set; }
    /// <summary>File path replacements (source -> destination) contributed by this subgroup.</summary>
    public HashSet<FilePathReplacement> Paths { get; set; }
    public Dictionary<string, HashSet<string>> AllowedBodyGenDescriptors { get; set; }
    public DescriptorMatchMode AllowedBodyGenMatchMode { get; set; } = DescriptorMatchMode.All;
    public Dictionary<string, HashSet<string>> DisallowedBodyGenDescriptors { get; set; }
    public DescriptorMatchMode DisallowedBodyGenMatchMode { get; set; } = DescriptorMatchMode.Any;
    public Dictionary<string, HashSet<string>> AllowedBodySlideDescriptors { get; set; }
    public DescriptorMatchMode AllowedBodySlideMatchMode { get; set; } = DescriptorMatchMode.All;
    public Dictionary<string, HashSet<string>> DisallowedBodySlideDescriptors { get; set; }
    public DescriptorMatchMode DisallowedBodySlideMatchMode { get; set; } = DescriptorMatchMode.Any;
    /// <summary>BodySlide descriptors with priority weights, grouped by category, used to bias preset selection.</summary>
    public Dictionary<string, List<BodyShapeDescriptor.PrioritizedLabelSignature>> PrioritizedBodySlideDescriptors { get; set; } = new();
    /// <summary>NPC weight (0-100) range this subgroup applies to (narrowed to the intersection with ancestors during flattening).</summary>
    public NPCWeightRange WeightRange { get; set; }
    /// <summary>The top-level subgroup position this flattened subgroup descends from.</summary>
    public int TopLevelSubgroupIndex { get; set; }
    /// <summary>IDs of every subgroup in this flattened chain, from top-level ancestor down to this subgroup.</summary>
    public List<string> ContainedSubgroupIDs { get; set; }
    /// <summary>Names of every subgroup in this flattened chain, parallel to <see cref="ContainedSubgroupIDs"/>.</summary>
    public List<string> ContainedSubgroupNames { get; set; }

    // used during combination generation
    /// <summary>The flattened asset pack this subgroup belongs to.</summary>
    public FlattenedAssetPack ParentAssetPack { get; set; }
    // Per-NPC ForceIf match counts live in NPCInfo.ForceIfMatches (R19), NOT here: subgroup objects are
    // shared across NPCs (ShallowCopy copies lists, not subgroups), so per-NPC scratch on them would
    // block parallel selection.
    /// <summary>IDs of the direct ancestor subgroups inherited during flattening.</summary>
    public List<string> ParentSubgroupIDs { get; set; } = new();
    // used for logging
    /// <summary>Running count of how many times this subgroup has been assigned (for logging).</summary>
    public int AssignmentCount { get; set; } = 0;
    /// <summary>The contained subgroup names joined with " -> " for log display.</summary>
    public string DeepNamesString => String.Join(" -> ", ContainedSubgroupNames);

    /// <summary>
    /// Joins the contained subgroup names with "/". When <paramref name="ignoreTopLevel"/> is true, the top-level
    /// name is omitted (replaced by "Top Level" only if it is the sole entry).
    /// </summary>
    public string GetNestedNameString(bool ignoreTopLevel)
    {
        List<string> names = new();
        for (int i = 0; i < ContainedSubgroupNames.Count; i++)
        {
            if (ignoreTopLevel && i == 0)
            {
                if (ContainedSubgroupNames.Count == 1)
                {
                    names.Add("Top Level");
                }
                continue;
            }
            names.Add(ContainedSubgroupNames[i]);
        }
        return string.Join("/", names);
    }

    /// <summary>Returns "Id (nested-name-string)" for log display.</summary>
    public string GetDetailedID_NameString(bool ignoreTopLevel)
    {
        return Id + " (" + GetNestedNameString(ignoreTopLevel) + ")";
    }

    /// <summary>
    /// Recursively flattens a subgroup tree, appending one <see cref="FlattenedSubgroup"/> per leaf to
    /// <paramref name="bottomLevelSubgroups"/>. Disabled subgroups are skipped. When a parent is supplied, the
    /// child inherits and merges the parent's rules (distribution/unique flags, multiplicative probability weight,
    /// allowed/disallowed races by intersection, required/excluded subgroups, attributes, weight range, keywords,
    /// paths, and body-shape descriptors). A subgroup that becomes incompatible with its parent (e.g. empty allowed
    /// races, invalid required/excluded set, or invalid descriptors) is pruned along with its entire subtree.
    /// </summary>
    /// <param name="toFlatten">The current subgroup to flatten.</param>
    /// <param name="parent">The already-flattened parent whose rules are inherited, or null at the top level.</param>
    /// <param name="bottomLevelSubgroups">Accumulator that receives the flattened leaf subgroups.</param>
    /// <param name="raceGroupingList">Race groupings used to resolve allowed/disallowed race lists.</param>
    /// <param name="parentAssetPackName">Name of the owning asset pack (for context).</param>
    /// <param name="topLevelIndex">The top-level subgroup position being flattened.</param>
    /// <param name="subgroupHierarchy">The full top-level subgroup list, used to resolve required-subgroup parent chains.</param>
    /// <param name="parentAssetPack">The flattened asset pack each produced subgroup is attached to.</param>
    /// <param name="dictionaryMapper">Mapper for subgroup/descriptor id resolution.</param>
    /// <param name="patcherState">Current patcher state (determines BodyGen vs BodySlide descriptor merging).</param>
    public static void FlattenSubgroups(Subgroup toFlatten, FlattenedSubgroup parent, List<FlattenedSubgroup> bottomLevelSubgroups, List<RaceGrouping> raceGroupingList, string parentAssetPackName, int topLevelIndex, List<Subgroup> subgroupHierarchy, FlattenedAssetPack parentAssetPack, DictionaryMapper dictionaryMapper, PatcherState patcherState)
    {
        if (toFlatten.Enabled == false) { return; }

        FlattenedSubgroup flattened = new FlattenedSubgroup(toFlatten, raceGroupingList, subgroupHierarchy, parentAssetPack, dictionaryMapper);
        flattened.TopLevelSubgroupIndex = topLevelIndex;

        if (parent != null)
        {
            // merge properties between current subgroup and parent
            if (parent.DistributionEnabled == false) { flattened.DistributionEnabled = false; }
            if (parent.AllowUnique == false) { flattened.AllowUnique = false; }
            if (parent.AllowNonUnique == false) { flattened.AllowNonUnique = false; }

            if (parent.Id != AssetPack.ConfigDistributionRules.SubgroupIDString)
            {
                flattened.ProbabilityWeighting *= parent.ProbabilityWeighting;
                flattened.ContainedSubgroupIDs.InsertRange(0, parent.ContainedSubgroupIDs);
                flattened.ContainedSubgroupNames.InsertRange(0, parent.ContainedSubgroupNames);
                flattened.ParentSubgroupIDs.Add(parent.Id);
            }

            //handle DisallowedRaces first
            flattened.DisallowedRaces.UnionWith(parent.DisallowedRaces);

            // if both flattened and parent AllowedRaces are empty, do nothing
            // else if parent AllowedRaces is empty and flatted AllowedRaces is not empty, do nothing
            // else if parent AllowedRaces is not empty and flattened AllowedRaces is empty:
            if (parent.AllowedRaces.Count > 0 && flattened.AllowedRacesIsEmpty)
            {
                flattened.AllowedRaces = parent.AllowedRaces;
                flattened.AllowedRacesIsEmpty = false;
            }
            // else if both parent AllowedRaces and flattened AllowedRaces are not empty, get their intersection
            else if (parent.AllowedRaces.Count > 0 && flattened.AllowedRaces.Count > 0)
            {
                flattened.AllowedRaces.IntersectWith(parent.AllowedRaces);
            }
            // now trim disallowedRaces from allowed
            flattened.AllowedRaces = AllowedDisallowedCombiners.TrimDisallowedRacesFromAllowed(flattened.AllowedRaces, flattened.DisallowedRaces);
            // if there are now no more AllowedRaces, the current subgroup is incompatible with parent and should be ignored along with all children
            if (flattened.AllowedRaces.Count == 0 && flattened.AllowedRacesIsEmpty == false) { return; }

            //Required / Excluded Subgroups
            //flattened.RequiredSubgroupIDs = DictionaryMapper.MergeDictionaries(new List<Dictionary<int, HashSet<string>>> { flattened.RequiredSubgroupIDs, parent.RequiredSubgroupIDs});
            flattened.RequiredSubgroupIDs = MergeRequiredSubgroupIDs(flattened, parent, subgroupHierarchy);
            flattened.ExcludedSubgroupIDs = DictionaryMapper.MergeDictionaries(new List<Dictionary<int, HashSet<string>>> { flattened.ExcludedSubgroupIDs, parent.ExcludedSubgroupIDs });
            flattened.RequiredSubgroupIDs = AllowedDisallowedCombiners.TrimExcludedSubgroupsFromRequired(flattened.RequiredSubgroupIDs, flattened.ExcludedSubgroupIDs, out bool requiredSubgroupsValid);
            if (!requiredSubgroupsValid) { return; }

            // Attribute Merging
            flattened.AllowedAttributes = NPCAttribute.InheritAttributes(parent.AllowedAttributes, flattened.AllowedAttributes);
            flattened.DisallowedAttributes = NPCAttribute.InheritAttributes(parent.DisallowedAttributes, flattened.DisallowedAttributes);

            // Probability modifiers concatenate (union) parent -> child; they are independent multiplicative
            // terms, NOT cross-producted like AllowedAttributes. Unlike the base ProbabilityWeighting multiply
            // above (which the ConfigDistributionRules guard skips), whole-config modifiers SHOULD apply, so this
            // runs for every parent including the ConfigDistributionRules pseudo-parent.
            flattened.ProbabilityWeightModifiers.InsertRange(0, parent.ProbabilityWeightModifiers.Select(AttributeWeightModifier.CloneAsNew));

            // Weight Range
            if (parent.WeightRange.Lower > flattened.WeightRange.Lower) { flattened.WeightRange.Lower = parent.WeightRange.Lower; }
            if (parent.WeightRange.Upper < flattened.WeightRange.Upper) { flattened.WeightRange.Upper = parent.WeightRange.Upper; }

            // Keywords
            flattened.AddKeywords.UnionWith(parent.AddKeywords);

            // Paths
            flattened.Paths.UnionWith(parent.Paths);

            if (patcherState.GeneralSettings.BodySelectionMode == BodyShapeSelectionMode.BodyGen)
            {
                flattened.AllowedBodyGenDescriptors = DictionaryMapper.GetMorphDictionaryIntersection(flattened.AllowedBodyGenDescriptors, parent.AllowedBodyGenDescriptors);
                flattened.DisallowedBodyGenDescriptors = DictionaryMapper.MergeDictionaries(new List<Dictionary<string, HashSet<string>>> { flattened.DisallowedBodyGenDescriptors, parent.DisallowedBodyGenDescriptors });
                flattened.AllowedBodyGenDescriptors = AllowedDisallowedCombiners.TrimDisallowedDescriptorsFromAllowed(flattened.AllowedBodyGenDescriptors, flattened.DisallowedBodyGenDescriptors, out bool BodyShapeDescriptorsValid);
                if (!BodyShapeDescriptorsValid) { return; }
            }
            else if (patcherState.GeneralSettings.BodySelectionMode == BodyShapeSelectionMode.BodySlide)
            {
                flattened.AllowedBodySlideDescriptors = DictionaryMapper.GetMorphDictionaryIntersection(flattened.AllowedBodySlideDescriptors, parent.AllowedBodySlideDescriptors);
                flattened.DisallowedBodySlideDescriptors = DictionaryMapper.MergeDictionaries(new List<Dictionary<string, HashSet<string>>> { flattened.DisallowedBodySlideDescriptors, parent.DisallowedBodySlideDescriptors });
                flattened.AllowedBodySlideDescriptors = AllowedDisallowedCombiners.TrimDisallowedDescriptorsFromAllowed(flattened.AllowedBodySlideDescriptors, flattened.DisallowedBodySlideDescriptors, out bool BodyShapeDescriptorsValid);
                if (!BodyShapeDescriptorsValid) { return; }
                flattened.PrioritizedBodySlideDescriptors = MergePrioritizedDescriptors(parent, toFlatten);
            }
        }

        if (toFlatten.Subgroups.Count == 0)
        {
            bottomLevelSubgroups.Add(flattened);
        }
        else
        {
            foreach (var subgroup in toFlatten.Subgroups)
            {
                FlattenSubgroups(subgroup, flattened, bottomLevelSubgroups, raceGroupingList, parentAssetPackName, topLevelIndex, subgroupHierarchy, parentAssetPack, dictionaryMapper, patcherState);
            }
        }
    }

    /// <summary>Returns "Subgroup Id (Name) " for log/report output.</summary>
    public string GetReportString()
    {
        return "Subgroup " + Id + " (" + Name + ") ";
    }

    /// <summary>
    /// Merges the required-subgroup constraints of two subgroups, keyed by top-level position. Where both constrain
    /// the same position, the more specific child requirements are kept and any of the other side's requirements that
    /// are merely parents (umbrella) of those children are discarded; otherwise both sides' requirements are retained.
    /// </summary>
    /// <param name="subgroupA">First subgroup whose required IDs are merged.</param>
    /// <param name="subgroupB">Second subgroup (typically the parent) whose required IDs are merged.</param>
    /// <param name="subgroupHierarchy">The top-level subgroup list used to resolve parent chains.</param>
    // if the required subgroups being inherited are children of an existing required subgroup, keep the children (more specific restriction) and get rid of the parent
    // otherwise keep both
    public static Dictionary<int, HashSet<string>> MergeRequiredSubgroupIDs(FlattenedSubgroup subgroupA, FlattenedSubgroup subgroupB, List<Subgroup> subgroupHierarchy)
    {
        Dictionary<int, HashSet<string>> mergedRequiredSubgroupIDs = new();
        var topLevelIndices = subgroupA.RequiredSubgroupIDs.Keys.And(subgroupB.RequiredSubgroupIDs.Keys).Distinct(x => x).ToArray();

        foreach (int index in topLevelIndices)
        {
            if (subgroupA.RequiredSubgroupIDs.ContainsKey(index) && !subgroupB.RequiredSubgroupIDs.ContainsKey(index))
            {
                mergedRequiredSubgroupIDs.Add(index, new(subgroupA.RequiredSubgroupIDs[index]));
                continue;
            }

            if (subgroupB.RequiredSubgroupIDs.ContainsKey(index) && !subgroupA.RequiredSubgroupIDs.ContainsKey(index))
            {
                mergedRequiredSubgroupIDs.Add(index, new(subgroupB.RequiredSubgroupIDs[index]));
                continue;
            }

            // both subgroups have required subgroups at this index
            var aRequiredSubgroups = TryGetSubgroupCollectionByID(subgroupHierarchy, subgroupA.RequiredSubgroupIDs[index], index);
            var bRequiredSubgroups = TryGetSubgroupCollectionByID(subgroupHierarchy, subgroupB.RequiredSubgroupIDs[index], index);

            var parentSubgroupIDsToDiscard = new HashSet<string>();

            var compatibilizedRequiredSubgroups = new HashSet<Subgroup>();

            foreach (var aSubgroup in aRequiredSubgroups)
            {
                // get rid of B's required subgroups if where they contain a parent of one of A's required subgroups (A's is more specific)
                var parentUmbrellaSubgroups = bRequiredSubgroups.Where(x => GetParentChain(subgroupHierarchy, aSubgroup, index).Contains(x)).ToArray();
                parentSubgroupIDsToDiscard.Add(parentUmbrellaSubgroups.Select(x => x.ID));
            }

            foreach (var bSubgroup in bRequiredSubgroups)
            {
                // get rid of A's required subgroups if where they contain a parent of one of B's required subgroups (B's is more specific)
                var parentUmbrellaSubgroups = aRequiredSubgroups.Where(x => GetParentChain(subgroupHierarchy, bSubgroup, index).Contains(x)).ToArray();
                parentSubgroupIDsToDiscard.Add(parentUmbrellaSubgroups.Select(x => x.ID));
            }

            compatibilizedRequiredSubgroups.Add(aRequiredSubgroups.And(bRequiredSubgroups).Where(x => !parentSubgroupIDsToDiscard.Contains(x.ID)));
            mergedRequiredSubgroupIDs.Add(index, compatibilizedRequiredSubgroups.Select(x => x.ID).ToHashSet());
        }
        return mergedRequiredSubgroupIDs;
    }

    /// <summary>
    /// Returns the chain of ancestor subgroups (from immediate parent up to the top-level subgroup) of
    /// <paramref name="toMatch"/> within the given top-level position, or an empty list if not found or the index is out of range.
    /// </summary>
    private static List<Subgroup> GetParentChain(List<Subgroup> subgroupHierarchy, Subgroup toMatch, int topLevelIndex)
    {
        List<Subgroup> parentChain = new();

        if(subgroupHierarchy.Count < topLevelIndex + 1 || topLevelIndex < 0)
        {
            return parentChain;
        }

        if (AddToParentChain(subgroupHierarchy[topLevelIndex], toMatch, parentChain))
        {
            parentChain.Add(subgroupHierarchy[topLevelIndex]);
        }

        return parentChain;
    }

    /// <summary>
    /// Recursive helper for <see cref="GetParentChain"/>: walks the subtree of <paramref name="subgroup"/> looking for
    /// <paramref name="toMatch"/>, appending each ancestor to <paramref name="chain"/> on the way back up.
    /// </summary>
    /// <returns>True if <paramref name="toMatch"/> was found in (a descendant of) <paramref name="subgroup"/>.</returns>
    private static bool AddToParentChain(Subgroup subgroup, Subgroup toMatch, List<Subgroup> chain)
    {
        if (subgroup.Subgroups.Contains(toMatch))
        {
            return true;
        }

        foreach (var sg in subgroup.Subgroups)
        {
            if (AddToParentChain(sg, toMatch, chain))
            {
                chain.Add(sg);
                return true;
            }
        }
        return false;
    }

    /// <summary>Searches the entire subgroup hierarchy (all positions, recursively) for a subgroup with the given ID.</summary>
    /// <returns>True and sets <paramref name="match"/> if found; otherwise false.</returns>
    public static bool TryGetSubgroupByID(List<Subgroup> subgroupHierarchy, string id, out Subgroup? match)
    {
        match = null;
        foreach (var subgroup in subgroupHierarchy)
        {
            if (TryGetSubgroupRecursive(subgroup, id, out match))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>Searches only the subtree at the given top-level position for a subgroup with the given ID.</summary>
    /// <returns>True and sets <paramref name="match"/> if found (and the index is in range); otherwise false.</returns>
    public static bool TryGetSubgroupByID(List<Subgroup> subgroupHierarchy, string id, int topLevelIndex, out Subgroup? match)
    {
        match = null;
        if (subgroupHierarchy.Count() < topLevelIndex + 1 || topLevelIndex < 0)
        {
            return false;
        }

        return TryGetSubgroupByID(subgroupHierarchy[topLevelIndex].Subgroups, id, out match);
    }

    /// <summary>Recursively searches a subgroup and its descendants for a matching ID.</summary>
    /// <returns>True and sets <paramref name="match"/> if found; otherwise false.</returns>
    private static bool TryGetSubgroupRecursive(Subgroup subgroup, string id, out Subgroup? match)
    {
        match = null;
        if (subgroup.ID == id)
        {
            match = subgroup;
            return true;
        }

        foreach (var sg in subgroup.Subgroups)
        {
            if (TryGetSubgroupRecursive(sg, id, out match))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>Resolves a collection of subgroup IDs to their <see cref="Subgroup"/> objects (searching the whole hierarchy); IDs not found are skipped.</summary>
    public static List<Subgroup> TryGetSubgroupCollectionByID(List<Subgroup> subgroupHierarchy, IEnumerable<string> ids)
    {
        List<Subgroup> subgroups = new();

        foreach (string id in ids)
        {
            if (TryGetSubgroupByID(subgroupHierarchy, id, out var subgroup))
            {
                subgroups.Add(subgroup);
            }
        }
        return subgroups;
    }

    /// <summary>Resolves a collection of subgroup IDs to their <see cref="Subgroup"/> objects within a single top-level position; IDs not found are skipped.</summary>
    public static List<Subgroup> TryGetSubgroupCollectionByID(List<Subgroup> subgroupHierarchy, IEnumerable<string> ids, int topLevelIndex)
    {
        List<Subgroup> subgroups = new();

        foreach (string id in ids)
        {
            if (TryGetSubgroupByID(subgroupHierarchy, id, topLevelIndex, out var subgroup))
            {
                subgroups.Add(subgroup);
            }
        }
        return subgroups;
    }

    /// <summary>
    /// Merges the prioritized BodySlide descriptors of a parent (already flattened) and an incoming child subgroup,
    /// grouped by category. Categories present on only one side are carried over; for categories on both sides,
    /// descriptors with the same value are combined and their priorities summed.
    /// </summary>
    /// <param name="parent">The flattened parent whose prioritized descriptors are inherited.</param>
    /// <param name="subgroup">The child subgroup contributing its own prioritized descriptors.</param>
    private static Dictionary<string, List<BodyShapeDescriptor.PrioritizedLabelSignature>> MergePrioritizedDescriptors(FlattenedSubgroup parent, Subgroup subgroup)
    {
        var mergedDescriptors = new Dictionary<string, List<BodyShapeDescriptor.PrioritizedLabelSignature>>();

        var incomingDescriptors = subgroup.PrioritizedBodySlideDescriptors.GroupBy(x => x.Category).ToDictionary(g => g.Key, g => g.ToList());

        var descriptorCategories = parent.PrioritizedBodySlideDescriptors.Keys.And(incomingDescriptors.Keys).Distinct(x => x).ToList();

        foreach (var category in descriptorCategories)
        {
            if (parent.PrioritizedBodySlideDescriptors.ContainsKey(category) && !incomingDescriptors.ContainsKey(category))
            {
                mergedDescriptors.Add(category, new(parent.PrioritizedBodySlideDescriptors[category]));
            }
            else if (incomingDescriptors.ContainsKey(category) && !parent.PrioritizedBodySlideDescriptors.ContainsKey(category))
            {
                mergedDescriptors.Add(category, new(incomingDescriptors[category]));
            }
            else // both have descriptors for the same category
            {
                var descriptorsByValue = incomingDescriptors[category].And(parent.PrioritizedBodySlideDescriptors[category]).GroupBy(x => x.Value).ToArray();
                List<BodyShapeDescriptor.PrioritizedLabelSignature> descriptorsHere = new();
                foreach (var descriptorgrouping in descriptorsByValue)
                {
                    var templateDescriptor = descriptorgrouping.First();
                    BodyShapeDescriptor.PrioritizedLabelSignature inheritedDescriptor = new() { Category = templateDescriptor.Category, Value = templateDescriptor.Value };
                    inheritedDescriptor.Priority = descriptorgrouping.Select(x => x.Priority).ToArray().Sum();
                    descriptorsHere.Add(inheritedDescriptor);
                }
                mergedDescriptors.Add(category, descriptorsHere);
            }
        }

        return mergedDescriptors;
    }
}