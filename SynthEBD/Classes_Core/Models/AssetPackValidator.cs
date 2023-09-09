using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;
using Noggog;

namespace SynthEBD;

public class AssetPackValidator
{
    private readonly BSAHandler _bsaHandler;
    private readonly IEnvironmentStateProvider _environmentProvider;
    private readonly PatcherState _patcherState;
    private readonly RecordPathParser _recordPathParser;

    public AssetPackValidator(BSAHandler bsaHandler, IEnvironmentStateProvider environmentProvider, PatcherState patcherState, RecordPathParser recordPathParser)
    {
        _bsaHandler = bsaHandler;
        _environmentProvider = environmentProvider;
        _patcherState = patcherState;
        _recordPathParser = recordPathParser;
    }
    
    public bool Validate(AssetPack assetPack, List<string> errors, BodyGenConfigs bodyGenConfigs, Settings_OBody oBodySettings)
    {
        bool isValidated = true;

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
            switch(assetPack.Gender)
            {
                case Gender.Male: matchedConfig = bodyGenConfigs.Male.Where(x => x.Label == assetPack.AssociatedBodyGenConfigName).FirstOrDefault(); break;
                case Gender.Female: matchedConfig = bodyGenConfigs.Female.Where(x => x.Label == assetPack.AssociatedBodyGenConfigName).FirstOrDefault(); break;
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

        if (!ValidateSubgroups(assetPack.Subgroups, errors, assetPack, referencedBodyGenConfig, oBodySettings, false, assetPack.AssociatedBsaModKeys))
        {
            isValidated = false;
        }
        foreach (var replacer in assetPack.ReplacerGroups)
        {
            if (!ValidateReplacer(replacer, referencedBodyGenConfig,oBodySettings, errors, assetPack.AssociatedBsaModKeys))
            {
                isValidated = false;
            }
        }

        if(!isValidated)
        {
            errors.Insert(0, "Errors detected in Config File " + assetPack.GroupName);
        }

        return isValidated;
    }

    private bool ValidateSubgroups(List<AssetPack.Subgroup> subgroups, List<string> errors, IModelHasSubgroups parent, BodyGenConfig bodyGenConfig, Settings_OBody oBodySettings, bool isReplacer, List<ModKey> associatedBSAmodKeys)
    {
        bool isValid = true;
        for (int i = 0; i < subgroups.Count; i++)
        {
            if (!ValidateSubgroup(subgroups[i], errors, parent, bodyGenConfig, oBodySettings, i, isReplacer, associatedBSAmodKeys))
            {
                isValid = false;
            }
        }
        return isValid;
    }

    private bool ValidateSubgroup(AssetPack.Subgroup subgroup, List<string> errors, IModelHasSubgroups parent, BodyGenConfig bodyGenConfig, Settings_OBody oBodySettings, int topLevelIndex, bool isReplacer, List<ModKey> associatedBSAmodKeys)
    {
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

        if (_patcherState.GeneralSettings.BodySelectionMode == BodyShapeSelectionMode.BodyGen && bodyGenConfig != null)
        {
            foreach (var descriptor in subgroup.AllowedBodyGenDescriptors)
            {
                if (!descriptor.CollectionContainsThisDescriptor(bodyGenConfig.TemplateDescriptors))
                {
                    subErrors.Add("Allowed descriptor " + descriptor.ToString() + " is invalid because it is not contained within the associated BodyGen config's descriptors");
                    isValid=false;
                }
            }
            foreach (var descriptor in subgroup.DisallowedBodyGenDescriptors)
            {
                if (!descriptor.CollectionContainsThisDescriptor(bodyGenConfig.TemplateDescriptors))
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
                if (!descriptor.CollectionContainsThisDescriptor(oBodySettings.TemplateDescriptors))
                {
                    subErrors.Add("Allowed descriptor " + descriptor.ToString() + " is invalid because it is not contained within your O/AutoBody descriptors");
                    isValid = false;
                }
            }
            foreach (var descriptor in subgroup.DisallowedBodySlideDescriptors)
            {
                if (!descriptor.CollectionContainsThisDescriptor(oBodySettings.TemplateDescriptors))
                {
                    subErrors.Add("Disallowed descriptor " + descriptor.ToString() + " is invalid because it is not contained within your O/AutoBody descriptors");
                    isValid = false;
                }
            }
        }

        foreach (var path in subgroup.Paths)
        {
            var fullPath = System.IO.Path.Combine(_environmentProvider.DataFolderPath, path.Source);
            if (!System.IO.File.Exists(fullPath) && !_bsaHandler.ReferencedPathExists(path.Source, out bool archiveExists, out string modName) && !_bsaHandler.ReferencedPathExists(path.Source, associatedBSAmodKeys, out bool specifiedArchiveExists, out string specifiedModName))
            {
                string pathError = "No file exists at " + fullPath;
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
        }

        foreach (var subSubgroup in subgroup.Subgroups)
        {
            if (!ValidateSubgroup(subSubgroup, errors, parent, bodyGenConfig, oBodySettings, topLevelIndex, isReplacer, associatedBSAmodKeys))
            {
                isValid = false;
            }
        }

        return isValid;
    }

    private bool ValidateID(string id)
    {
        return id == MiscFunctions.MakeXMLtagCompatible(id);
    }

    private bool ValidateReplacer(AssetReplacerGroup group, BodyGenConfig bodyGenConfig, Settings_OBody oBodySettings, List<string> errors, List<ModKey> associatedBSAmodKeys)
    {
        bool isValid = true;
        if (string.IsNullOrWhiteSpace(group.Label))
        {
            errors.Add("A group with an empty name was detected");
            isValid = false;
        }

        ValidateSubgroups(group.Subgroups, errors, group, bodyGenConfig, oBodySettings, true, associatedBSAmodKeys);
        return isValid;
    }

    private bool HasDuplicateSubgroupIDs(IModelHasSubgroups model, List<string> errors)
    {
        List<string> ids = new List<string>();
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

    private void GetIDDuplicates(IModelHasSubgroups model, List<string> searched, List<string> duplicates)
    {
        foreach (var subgroup in model.Subgroups)
        {
            if (!searched.Contains(subgroup.ID))
            {
                searched.Add(subgroup.ID);
            }
            else
            {
                duplicates.Add(subgroup.ID);
            }

            foreach (var subSubgroup in subgroup.Subgroups)
            {
                GetIDDuplicates(subSubgroup, searched, duplicates);
            }
        }
    }

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