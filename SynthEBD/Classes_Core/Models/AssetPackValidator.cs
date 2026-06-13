using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;
using Noggog;

namespace SynthEBD;

/// <summary>
/// Validates an <see cref="AssetPack"/> before patching: checks required fields, the associated BodyGen
/// config, duplicate subgroup IDs, and — recursively per subgroup — ID/XML-tag validity, required/excluded
/// subgroup references, body-shape descriptor validity, and that each source asset exists (loose or in a
/// BSA) and each destination record path resolves to a string on a record template. Accumulates
/// human-readable messages into a caller-supplied error list.
/// </summary>
public class AssetPackValidator
{
    private readonly BSAHandler _bsaHandler;
    private readonly IEnvironmentStateProvider _environmentProvider;
    private readonly PatcherState _patcherState;
    private readonly RecordPathParser _recordPathParser;

    /// <summary>Optional additional root folders probed (after the data folder, before the BSA checks) for
    /// each path's Source file. Lets headless tooling validate a config against the working folder a texture
    /// mod was extracted to, whose assets are not yet visible in the game data folder or an active
    /// mod-manager VFS. Empty by default, in which case validation behaves exactly as before.</summary>
    public List<string> ExtraAssetRoots { get; } = new();

    /// <summary>Race groupings available to the config being validated (General settings plus the config's own
    /// local groupings), used to resolve grouping-label references and race coverage. Rebuilt per Validate call.</summary>
    private List<RaceGrouping> _availableRaceGroupings = new();
    private HashSet<string> _availableRaceGroupingLabels = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Captures the BSA handler, environment, patcher state, and record-path parser used during validation.</summary>
    public AssetPackValidator(BSAHandler bsaHandler, IEnvironmentStateProvider environmentProvider, PatcherState patcherState, RecordPathParser recordPathParser)
    {
        _bsaHandler = bsaHandler;
        _environmentProvider = environmentProvider;
        _patcherState = patcherState;
        _recordPathParser = recordPathParser;
    }

    /// <summary>Validates an asset pack, appending any problems to <paramref name="errors"/>.</summary>
    /// <param name="assetPack">The pack to validate.</param>
    /// <param name="errors">Accumulates human-readable error messages (prefixed with the config name if any check fails).</param>
    /// <param name="bodyGenConfigs">Available BodyGen configs, used to resolve the pack's associated config.</param>
    /// <param name="oBodySettings">OBody/AutoBody settings, used to validate BodySlide descriptors.</param>
    /// <returns><c>true</c> if the pack is valid.</returns>
    public bool Validate(AssetPack assetPack, List<string> errors, BodyGenConfigs bodyGenConfigs, Settings_OBody oBodySettings)
    {
        bool isValidated = true;
        bool hasMisingDescriptorsError = false;

        _availableRaceGroupings = BuildAvailableRaceGroupings(assetPack);
        _availableRaceGroupingLabels = _availableRaceGroupings.Select(x => x.Label).ToHashSet(StringComparer.OrdinalIgnoreCase);

        BodyGenConfig referencedBodyGenConfig = new BodyGenConfig();

        if (string.IsNullOrWhiteSpace(assetPack.GroupName))
        {
            errors.Add("Name cannot be empty");
            isValidated = false;
        }
        if (string.IsNullOrWhiteSpace(assetPack.ShortName) && assetPack.ReplacerGroups.Any())
        {
            errors.Add("Prefix cannot be empty if replacers are included in a group");
            isValidated = false;
        }

        if (assetPack.DefaultRecordTemplate == null || assetPack.DefaultRecordTemplate.IsNull)
        {
            errors.Add("A default record template must be set.");
            isValidated = false;
        }

        if (_patcherState.GeneralSettings.BodySelectionMode == BodyShapeSelectionMode.BodyGen && !string.IsNullOrWhiteSpace(assetPack.AssociatedBodyGenConfigName))
        {
            BodyGenConfig matchedConfig = null;
            switch (assetPack.Gender)
            {
                case Gender.Male: matchedConfig = bodyGenConfigs.Male.FirstOrDefault(x => x.Label == assetPack.AssociatedBodyGenConfigName); break;
                case Gender.Female: matchedConfig = bodyGenConfigs.Female.FirstOrDefault(x => x.Label == assetPack.AssociatedBodyGenConfigName); break;
            }
            if (matchedConfig != null)
            {
                referencedBodyGenConfig = matchedConfig;
            }
            else
            {
                errors.Add("The expected associated BodyGen config " + assetPack.AssociatedBodyGenConfigName + " could not be found.");
                isValidated = false;
            }
        }

        if (HasDuplicateSubgroupIDs(assetPack, errors))
        {
            isValidated = false;
        }

        if (!ValidateSubgroups(assetPack.Subgroups, errors, assetPack, referencedBodyGenConfig, oBodySettings, false, assetPack.AssociatedBsaModKeys, out hasMisingDescriptorsError))
        {
            isValidated = false;
        }
        foreach (var replacer in assetPack.ReplacerGroups)
        {
            if (!ValidateReplacer(replacer, referencedBodyGenConfig, oBodySettings, errors, assetPack.AssociatedBsaModKeys, out bool replacerHasMissingDescriptors))
            {
                isValidated = false;
                if (replacerHasMissingDescriptors)
                {
                    hasMisingDescriptorsError = true;
                }
            }
        }

        if (!ValidateRaceCoverage(assetPack, errors))
        {
            isValidated = false;
        }

        if (!isValidated)
        {
            errors.Insert(0, "Errors detected in Config File " + assetPack.GroupName);
        }

        if (hasMisingDescriptorsError)
        {
            errors.Add("For missing Body Shape Descriptors, either import the OBody settings from which they are supposed to come, or remove them." + Environment.NewLine + "You can remove all invalid descriptors using Asset Pack -> Misc -> Delete Missing Body Shape Descriptors");
        }

        return isValidated;
    }

    /// <summary>Validates each subgroup in a list (top-level position-aware), OR-ing the missing-descriptor flag across them.</summary>
    private bool ValidateSubgroups(List<AssetPack.Subgroup> subgroups, List<string> errors, IModelHasSubgroups parent, BodyGenConfig bodyGenConfig, Settings_OBody oBodySettings, bool isReplacer, List<ModKey> associatedBSAmodKeys, out bool hasMissingDescriptorsError)
    {
        bool isValid = true;
        hasMissingDescriptorsError = false;
        for (int i = 0; i < subgroups.Count; i++)
        {
            if (!ValidateSubgroup(subgroups[i], errors, parent, bodyGenConfig, oBodySettings, i, isReplacer, associatedBSAmodKeys, out bool subgroupHasMissingDescriptors))
            {
                isValid = false;
                if (subgroupHasMissingDescriptors)
                {
                    hasMissingDescriptorsError = true;
                }
            }
        }

        return isValid;
    }

    /// <summary>
    /// Recursively validates a single subgroup: ID presence/XML-compatibility, name, required/excluded subgroup
    /// references (must exist and not be in the same branch), body-shape descriptor validity for the active
    /// selection mode, and each path's source existence + destination record path resolving to a string.
    /// </summary>
    /// <param name="topLevelIndex">Index of the owning top-level subgroup (used for branch checks and messages).</param>
    /// <param name="hasMissingDescriptorsError">Set true if a referenced BodySlide descriptor is missing from the OBody settings.</param>
    private bool ValidateSubgroup(AssetPack.Subgroup subgroup, List<string> errors, IModelHasSubgroups parent, BodyGenConfig bodyGenConfig, Settings_OBody oBodySettings, int topLevelIndex, bool isReplacer, List<ModKey> associatedBSAmodKeys, out bool hasMissingDescriptorsError)
    {
        hasMissingDescriptorsError = false;
        if (!subgroup.Enabled) { return true; }

        bool isValid = true;
        List<string> subErrors = new List<string>();

        if (string.IsNullOrWhiteSpace(subgroup.ID))
        {
            subErrors.Add("Subgroup does not have an ID");
        }
        if (!ValidateID(subgroup.ID))
        {
            subErrors.Add("ID must be XML tag-compatible");
            isValid = false;
        }
            
        if (string.IsNullOrWhiteSpace(subgroup.Name))
        {
            subErrors.Add("Subgroup must have a name");
            isValid = false;
        }

        var thisPosition = new List<int> { topLevelIndex };
        var otherPostitions = new List<int>();
        for (int i = 0; i < parent.Subgroups.Count; i++)
        {
            if (i == topLevelIndex)
            {
                continue;
            }
            else
            {
                otherPostitions.Add(i);
            }
        }

        foreach (var id in subgroup.RequiredSubgroups)
        {
            if (GetSubgroupByID(id, parent, out _, thisPosition) != null)
            {
                subErrors.Add("Cannot use " + id + " as a required subgroup because it is in the same branch as " + subgroup.ID);
                isValid = false;
            }
            else if (GetSubgroupByID(id, parent, out _, otherPostitions) == null)
            {
                subErrors.Add("Cannot use " + id + " as a required subgroup because it was not found in the subgroup tree");
                isValid = false;
            }
        }

        foreach (var id in subgroup.ExcludedSubgroups)
        {
            if (GetSubgroupByID(id, parent, out _, thisPosition) != null)
            {
                subErrors.Add("Cannot use " + id + " as an excluded subgroup because it is in the same branch as " + subgroup.ID);
                isValid = false;
            }
            else if (GetSubgroupByID(id, parent, out _, otherPostitions) == null)
            {
                subErrors.Add("Cannot use " + id + " as an excluded subgroup because it was not found in the subgroup tree");
                isValid = false;
            }
        }

        // Race-grouping labels must resolve to an actual grouping (General settings or this config's local
        // RaceGroupings); an unresolved label silently matches no races, so the subgroup never distributes.
        foreach (var label in subgroup.AllowedRaceGroupings.Concat(subgroup.DisallowedRaceGroupings))
        {
            if (!_availableRaceGroupingLabels.Contains(label))
            {
                subErrors.Add("References race grouping \"" + label + "\" which is not defined in this config's local Race Groupings or in General Settings. It will match no races.");
                isValid = false;
            }
        }

        if (_patcherState.GeneralSettings.BodySelectionMode == BodyShapeSelectionMode.BodyGen && bodyGenConfig != null)
        {
            foreach (var descriptor in subgroup.AllowedBodyGenDescriptors)
            {
                if (!descriptor.CollectionContainsThisDescriptor(bodyGenConfig.TemplateDescriptors.Flatten()))
                {
                    subErrors.Add("Allowed descriptor " + descriptor.ToString() + " is invalid because it is not contained within the associated BodyGen config's descriptors");
                    isValid=false;
                }
            }
            foreach (var descriptor in subgroup.DisallowedBodyGenDescriptors)
            {
                if (!descriptor.CollectionContainsThisDescriptor(bodyGenConfig.TemplateDescriptors.Flatten()))
                {
                    subErrors.Add("Disallowed descriptor " + descriptor.ToString() + " is invalid because it is not contained within the associated BodyGen config's descriptors");
                    isValid = false;
                }
            }
        }

        else if (_patcherState.GeneralSettings.BodySelectionMode == BodyShapeSelectionMode.BodySlide)
        {
            foreach (var descriptor in subgroup.AllowedBodySlideDescriptors)
            {
                if (!descriptor.CollectionContainsThisDescriptor(oBodySettings.TemplateDescriptors.Flatten()))
                {
                    subErrors.Add("Allowed descriptor " + descriptor.ToString() + " is invalid because it is not contained within your O/AutoBody descriptors.");
                    isValid = false;
                    hasMissingDescriptorsError = true;
                }
            }
            foreach (var descriptor in subgroup.DisallowedBodySlideDescriptors)
            {
                if (!descriptor.CollectionContainsThisDescriptor(oBodySettings.TemplateDescriptors.Flatten()))
                {
                    subErrors.Add("Disallowed descriptor " + descriptor.ToString() + " is invalid because it is not contained within your O/AutoBody descriptors");
                    isValid = false;
                    hasMissingDescriptorsError = true;
                }
            }
        }

        HashSet<string> existingDestinationPaths = new();
        foreach (var path in subgroup.Paths)
        {
            var fullPath = System.IO.Path.Combine(_environmentProvider.DataFolderPath, path.Source);
            if (!System.IO.File.Exists(fullPath) && !ExistsUnderExtraAssetRoot(path.Source) && !_bsaHandler.ReferencedPathExists(path.Source, out bool archiveExists, out string modName) && !_bsaHandler.ReferencedPathExists(path.Source, associatedBSAmodKeys, out bool specifiedArchiveExists, out string specifiedModName))
            {
                string pathError = "No file exists at " + fullPath;
                if (ExtraAssetRoots.Any())
                {
                    pathError += " or under any extra asset root";
                }
                if (archiveExists)
                {
                    pathError += " or any BSA archives corresponding to " + modName;
                }
                else if(specifiedArchiveExists)
                {
                    pathError += " or any BSA archives corresponding to " + specifiedModName;
                }
                subErrors.Add(pathError);
                isValid = false;
            }

            if (existingDestinationPaths.Contains(path.Destination))
            {
                subErrors.Add("Subgroup contains multiple Assets with the same Destination Path: " + path.Destination);
                isValid = false;
            }
            existingDestinationPaths.Add(path.Destination);

            if (!isReplacer)
            {
                var parentAssetPack = parent as AssetPack;
                if (parentAssetPack != null)
                {
                    // try to find a record template which has the given object
                    var references = parentAssetPack.AdditionalRecordTemplateAssignments.Select(x => x.TemplateNPC).And(parentAssetPack.DefaultRecordTemplate).ToHashSet();
                    bool foundReferenceNPC = false;
                    bool destinationIsString = false;
                    foreach (var referenceNPCformKey in references)
                    {
                        if (_patcherState.RecordTemplateLinkCache.TryResolve<INpcGetter>(referenceNPCformKey, out var referenceNPCgetter) && _recordPathParser.GetObjectAtPath(referenceNPCgetter, referenceNPCgetter, path.Destination, new Dictionary<string, dynamic>(), _patcherState.RecordTemplateLinkCache, true, "", out dynamic objectAtPath))
                        {
                            foundReferenceNPC = true;
                            if (objectAtPath.GetType() == typeof(string))
                            {
                                destinationIsString = true;
                                break;
                            }
                        }
                    }
                    if (!foundReferenceNPC)
                    {
                        subErrors.Add("Could not find a record template to supply objects along path: " + path.Destination);
                        isValid = false;
                    }
                    else if (!destinationIsString)
                    {
                        subErrors.Add("Asset destination path " + path.Destination + " does not point to a string.");
                        isValid = false;
                    }
                }
            }
        }

        if (!isValid)
        {
            var parentTopLevel = parent.Subgroups[topLevelIndex].ID + ": " + parent.Subgroups[topLevelIndex].Name;
            subErrors.Insert(0, "Subgroup " + subgroup.ID + ":" + subgroup.Name + " within Top Level Subgroup " + parentTopLevel + " (#" + (topLevelIndex + 1).ToString() + ")");
            errors.AddRange(subErrors);
            errors.Add(Environment.NewLine);
        }

        foreach (var subSubgroup in subgroup.Subgroups)
        {
            if (!ValidateSubgroup(subSubgroup, errors, parent, bodyGenConfig, oBodySettings, topLevelIndex, isReplacer, associatedBSAmodKeys, out bool subHasMissingDescriptors))
            {
                isValid = false;
                if (subHasMissingDescriptors)
                {
                    hasMissingDescriptorsError = true;
                }
            }
        }

        return isValid;
    }

    /// <summary>True if the source path exists under any of <see cref="ExtraAssetRoots"/>.</summary>
    private bool ExistsUnderExtraAssetRoot(string sourcePath)
    {
        return ExtraAssetRoots.Any(root => System.IO.File.Exists(System.IO.Path.Combine(root, sourcePath)));
    }

    /// <summary>The race groupings visible to a config: General settings' groupings, plus the config's own local
    /// groupings for any label General lacks (General supersedes on a label collision, mirroring the patcher).</summary>
    private List<RaceGrouping> BuildAvailableRaceGroupings(AssetPack assetPack)
    {
        var merged = new List<RaceGrouping>(_patcherState.GeneralSettings.RaceGroupings);
        var generalLabels = merged.Select(x => x.Label).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var local in assetPack.RaceGroupings)
        {
            if (!generalLabels.Contains(local.Label)) { merged.Add(local); }
        }
        return merged;
    }

    /// <summary>
    /// Flags "unsatisfiable position" gaps: since an NPC must receive one subgroup from every enabled top-level,
    /// an NPC of race R gets nothing from the whole config if any single top-level cannot be assigned to R. This
    /// reports a race only when EXACTLY ONE enabled top-level fails to cover it while every other top-level does —
    /// the strong signal of an oversight (e.g. a default Head Diffuse that forgot the Elder race, or a top-level
    /// whose subgroups are all disabled). Races a config deliberately does not support (covered by no/few
    /// top-levels, e.g. beast races in a human-only skin) are left alone. Race matching only (attributes/weights
    /// are ignored), so this never produces a false negative for the "whole position is dead" case.
    /// </summary>
    private bool ValidateRaceCoverage(AssetPack assetPack, List<string> errors)
    {
        var patchable = _patcherState.GeneralSettings.PatchableRaces?.ToHashSet() ?? new HashSet<FormKey>();
        var topLevels = assetPack.Subgroups.Where(x => x.Enabled).ToList();
        if (!patchable.Any() || topLevels.Count < 2) { return true; }

        var coveredByTop = topLevels.ToDictionary(
            t => t,
            t => patchable.Where(r => SubgroupCoversRace(t, r, new HashSet<FormKey>(), true, new HashSet<FormKey>())).ToHashSet());

        var gapsByTop = new Dictionary<AssetPack.Subgroup, List<FormKey>>();
        foreach (var race in patchable)
        {
            var missing = topLevels.Where(t => !coveredByTop[t].Contains(race)).ToList();
            if (missing.Count == 1) // every other top-level covers this race; this one is the lone hole
            {
                if (!gapsByTop.TryGetValue(missing[0], out var list)) { gapsByTop[missing[0]] = list = new(); }
                list.Add(race);
            }
        }

        bool isValid = true;
        foreach (var (top, races) in gapsByTop)
        {
            isValid = false;
            var raceNames = string.Join(", ", races.Select(RaceLabel));
            errors.Add("Top-level subgroup " + top.ID + " (" + top.Name + ") is the only top-level that cannot be " +
                "assigned to NPCs of race(s) [" + raceNames + "] - every other top-level can. Such NPCs would receive " +
                "nothing from this config (each enabled top-level must be assignable to every NPC the config patches). " +
                "Broaden this subgroup's allowed races/groupings, add a variant for those races, or add an empty " +
                "enabled placeholder subgroup if the omission is deliberate.");
        }
        return isValid;
    }

    /// <summary>Whether some enabled, distribution-enabled leaf under <paramref name="subgroup"/> can be assigned to
    /// <paramref name="race"/>, honoring the same allowed/disallowed-race inheritance the patcher applies
    /// (empty AllowedRaces = all races; child allowed intersects parent; disallowed accumulates down the tree).</summary>
    private bool SubgroupCoversRace(AssetPack.Subgroup subgroup, FormKey race, HashSet<FormKey> inheritedAllowed, bool inheritedAllowedEmpty, HashSet<FormKey> inheritedDisallowed)
    {
        if (!subgroup.Enabled) { return false; }

        var ownAllowed = RaceGrouping.MergeRaceAndGroupingList(subgroup.AllowedRaceGroupings, _availableRaceGroupings, subgroup.AllowedRaces);
        bool ownAllowedEmpty = ownAllowed.Count == 0;

        var disallowed = new HashSet<FormKey>(inheritedDisallowed);
        disallowed.UnionWith(RaceGrouping.MergeRaceAndGroupingList(subgroup.DisallowedRaceGroupings, _availableRaceGroupings, subgroup.DisallowedRaces));

        HashSet<FormKey> effAllowed;
        bool effEmpty;
        if (ownAllowedEmpty && inheritedAllowedEmpty) { effAllowed = new(); effEmpty = true; }
        else if (inheritedAllowedEmpty) { effAllowed = ownAllowed; effEmpty = false; }
        else if (ownAllowedEmpty) { effAllowed = inheritedAllowed; effEmpty = false; }
        else { effAllowed = new(ownAllowed); effAllowed.IntersectWith(inheritedAllowed); effEmpty = false; }

        bool matched = (effEmpty || effAllowed.Contains(race)) && !disallowed.Contains(race);
        if (!matched) { return false; }

        if (subgroup.Subgroups.Any())
        {
            return subgroup.Subgroups.Any(c => SubgroupCoversRace(c, race, effAllowed, effEmpty, disallowed));
        }
        return subgroup.DistributionEnabled;
    }

    /// <summary>Best-effort friendly name for a race FormKey (EditorID if resolvable, else the FormKey string).</summary>
    private string RaceLabel(FormKey raceFormKey)
    {
        if (_environmentProvider.LinkCache != null
            && _environmentProvider.LinkCache.TryResolve<Mutagen.Bethesda.Skyrim.IRaceGetter>(raceFormKey, out var race)
            && !string.IsNullOrEmpty(race.EditorID))
        {
            return race.EditorID;
        }
        return raceFormKey.ToString();
    }

    /// <summary>True if the ID is already XML-tag-compatible (unchanged by <see cref="MiscFunctions.MakeXMLtagCompatible"/>).</summary>
    private bool ValidateID(string id)
    {
        return id == MiscFunctions.MakeXMLtagCompatible(id);
    }

    /// <summary>Validates a replacer group (non-empty label) and its subgroups.</summary>
    private bool ValidateReplacer(AssetReplacerGroup group, BodyGenConfig bodyGenConfig, Settings_OBody oBodySettings, List<string> errors, List<ModKey> associatedBSAmodKeys, out bool hasMissingDescriptorError)
    {
        bool isValid = true;
        if (string.IsNullOrWhiteSpace(group.Label))
        {
            errors.Add("A group with an empty name was detected");
            isValid = false;
        }

        ValidateSubgroups(group.Subgroups, errors, group, bodyGenConfig, oBodySettings, true, associatedBSAmodKeys, out hasMissingDescriptorError);
        return isValid;
    }

    /// <summary>Reports (and lists in <paramref name="errors"/>) any subgroup IDs duplicated anywhere within the model's subgroup tree.</summary>
    private bool HasDuplicateSubgroupIDs(IModelHasSubgroups model, List<string> errors)
    {
        HashSet<string> ids = new();
        List<string> duplicates = new List<string>();
        foreach (var subgroup in model.Subgroups)
        {
            GetIDDuplicates(subgroup, ids, duplicates);
        }

        if (duplicates.Any())
        {
            errors.Add("Duplicate subgroup IDs within the same parent config are not allowed. The following IDs were found to be duplicated:");
            foreach (string id in duplicates)
            {
                errors.Add(id);
            }
            return true;
        }
        else
        {
            return false;
        }
    }

    /// <summary>Recursively walks the subgroup tree, recording each ID into <paramref name="searched"/> and any repeat into <paramref name="duplicates"/>.</summary>
    private void GetIDDuplicates(IModelHasSubgroups model, HashSet<string> searched, List<string> duplicates)
    {
        foreach (var subgroup in model.Subgroups)
        {
            if (!searched.Add(subgroup.ID))
            {
                duplicates.Add(subgroup.ID);
            }

            foreach (var subSubgroup in subgroup.Subgroups)
            {
                GetIDDuplicates(subSubgroup, searched, duplicates);
            }
        }
    }

    /// <summary>Finds the subgroup with the given ID within the specified top-level branches, reporting whether more than one matched.</summary>
    /// <returns>The first matching subgroup, or null.</returns>
    private AssetPack.Subgroup GetSubgroupByID(string id, IModelHasSubgroups model, out bool foundMultiple, List<int> topLevelSubgroupsToSearch)
    {
        List<AssetPack.Subgroup> matched = new List<AssetPack.Subgroup>();

        for (int i = 0; i < model.Subgroups.Count; i++)
        {
            if (!topLevelSubgroupsToSearch.Contains(i)) { continue; }
            GetSubgroupByID(id, model.Subgroups[i], matched);
        }

          
        if (matched.Count > 1)
        {
            foundMultiple = true;
        }
        else
        {
            foundMultiple = false;
        }

        return matched.FirstOrDefault();
    }

    /// <summary>Recursively collects every subgroup matching <paramref name="id"/> in the tree into <paramref name="matched"/>.</summary>
    private void GetSubgroupByID(string id, IModelHasSubgroups model, List<AssetPack.Subgroup> matched)
    {
        for (int i = 0; i < model.Subgroups.Count; i++)
        {
            var subgroup = model.Subgroups[i];
            if (subgroup.ID == id)
            {
                matched.Add(subgroup);
            }
            GetSubgroupByID(id, subgroup, matched);
        }
    }
}