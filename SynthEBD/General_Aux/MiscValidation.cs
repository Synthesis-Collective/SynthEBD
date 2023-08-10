using System.IO;
using AssemblyVersionGenerator;
using Noggog;

namespace SynthEBD;

public class MiscValidation
{
    private readonly IEnvironmentStateProvider _environmentProvider;
    private readonly Logger _logger;
    private readonly SynthEBDPaths _paths;
    private readonly RaceMenuIniHandler _raceMenuHandler;
    private readonly PatcherState _patcherState;
    public MiscValidation(IEnvironmentStateProvider environmentProvider, PatcherState patcherState, Logger logger, SynthEBDPaths paths, RaceMenuIniHandler raceMenuHandler)
    {
        _environmentProvider = environmentProvider;
        _patcherState = patcherState;
        _logger = logger;
        _paths = paths;
        _raceMenuHandler = raceMenuHandler;
    }
    public bool VerifyEBDInstalled()
    {
        bool verified = true;

        string helperScriptPath = Path.Combine(_environmentProvider.DataFolderPath, "Scripts", "EBDHelperScript.pex");
        if (!File.Exists(helperScriptPath))
        {
            _logger.LogMessage("Could not find EBDHelperScript.pex from EveryBody's Different Redone SSE at " + helperScriptPath);
            verified = false;
        }

        string globalScriptPath = Path.Combine(_environmentProvider.DataFolderPath, "Scripts", "EBDGlobalFuncs.pex");
        if (!File.Exists(globalScriptPath))
        {
            _logger.LogMessage("Could not find EBDGlobalFuncs.pex from EveryBody's Different Redone SSE at " + globalScriptPath);
            verified = false;
        }

        if (!verified)
        {
            _logger.LogMessage("Please make sure that EveryBody's Different Redone SSE is installed.");
        }

        return verified;
    }

    public bool VerifyRaceMenuInstalled(DirectoryPath dataFolderPath)
    {
        bool verified = true;

        string dllPath64 = Path.Combine(dataFolderPath, "SKSE", "Plugins", "skee64.dll");
        string dllPathVR = Path.Combine(dataFolderPath, "SKSE", "Plugins", "skeevr.dll");
        if (!File.Exists(dllPath64) && !File.Exists(dllPathVR))
        {
            _logger.LogMessage("Could not find skee64.dll from RaceMenu at " + Path.Combine(dataFolderPath, "SKSE", "Plugins"));
            verified = false;
        }

        string iniPath64 = Path.Combine(dataFolderPath, "SKSE", "Plugins", "skee64.ini");
        string iniPathVR = Path.Combine(dataFolderPath, "SKSE", "Plugins", "skeevr.ini");
        if (!File.Exists(iniPath64) && !File.Exists(iniPathVR))
        {
            _logger.LogMessage("Could not find skee64.ini from RaceMenu at " + Path.Combine(dataFolderPath, "SKSE", "Plugins"));
            verified = false;
        }

        if (!verified)
        {
            _logger.LogMessage("Please make sure that RaceMenu SE is installed.");
        }

        return verified;
    }

    public bool VerifyOBodyInstalled(DirectoryPath dataFolderPath)
    {
        bool verified = true;

        string scriptPath = Path.Combine(dataFolderPath, "Scripts", "OBodyNative.pex");
        if (!File.Exists(scriptPath))
        {
            _logger.LogMessage("Could not find OBodyNative.pex from OBody at " + scriptPath);
            verified = false;
        }

        string dllPath = Path.Combine(dataFolderPath, "SKSE", "Plugins", "OBody.dll");
        if (!File.Exists(dllPath))
        {
            _logger.LogMessage("Could not find OBody.dll from OBody at " + dllPath);
            verified = false;
        }

        if (!verified)
        {
            _logger.LogMessage("Please make sure that OBody is installed.");
        }

        return verified;
    }

    public bool VerifyAutoBodyInstalled(DirectoryPath dataFolderPath)
    {
        bool verified = true;

        string scriptPath = Path.Combine(dataFolderPath, "Scripts", "autoBodyUtils.pex");
        if (!File.Exists(scriptPath))
        {
            _logger.LogMessage("Could not find autoBodyUtils.pex from AutoBody at " + scriptPath);
            verified = false;
        }

        string dllPath = Path.Combine(dataFolderPath, "SKSE", "Plugins", "autoBodyAE.dll");
        if (!File.Exists(dllPath))
        {
            _logger.LogMessage("Could not find autoBodyAE.dll from AutoBody at " + dllPath);
            verified = false;
        }

        if (!verified)
        {
            _logger.LogMessage("Please make sure that AutoBody is installed.");
        }

        return verified;
    }

    public bool VerifyBodySlideUniqueLabels()
    {
        List<string> existingLabels = new();
        bool foundDuplicate = false;
        foreach (var bodySlide in _patcherState.OBodySettings.BodySlidesMale.And(_patcherState.OBodySettings.BodySlidesFemale))
        {
            if (existingLabels.Contains(bodySlide.Label))
            {
                _logger.LogMessage("Found duplicate BodySlide name: " + bodySlide.Label + ". Names must be unique even if the linked BodySlide is the same.");
                foundDuplicate = true;
            }
            else
            {
                existingLabels.Add(bodySlide.Label);
            }
        }
        return !foundDuplicate;
    }

    public bool VerifyReferencedBodySlides()
    {
        bool foundEmpty = false;
        foreach (var bodySlide in _patcherState.OBodySettings.BodySlidesMale.And(_patcherState.OBodySettings.BodySlidesFemale))
        {
            if (bodySlide.ReferencedBodySlide.IsNullOrWhitespace())
            {
                _logger.LogMessage("Found empty BodySlide for Setting named: " + bodySlide.Label + ".");
                foundEmpty = true;
            }
        }
        return !foundEmpty;
    }

    public bool VerifySPIDInstalled(DirectoryPath dataFolderPath, bool bSilent)
    {
        string dllPath = Path.Combine(dataFolderPath, "SKSE", "Plugins", "po3_SpellPerkItemDistributor.dll");
        if (!File.Exists(dllPath))
        {
            if (!bSilent)
            {
                _logger.LogMessage("Could not find po3_SpellPerkItemDistributor.dll from Spell Perk Item Distributor at " + dllPath);
                _logger.LogMessage("Please make sure Spell Perk Item Distributor is enabled.");
            }
            
            return false;
        }
        return true;
    }

    public bool VerifyJContainersInstalled(DirectoryPath dataFolderPath, bool bSilent)
    {
        string dllPathSE_AE = Path.Combine(dataFolderPath, "SKSE", "Plugins", "JContainers64.dll");
        string dllPathVR = Path.Combine(dataFolderPath, "SKSE", "Plugins", "JContainersVR.dll");

        var currentSkyrimVersion = _environmentProvider.SkyrimVersion;
        bool checkSE = currentSkyrimVersion == Mutagen.Bethesda.Skyrim.SkyrimRelease.SkyrimSE || currentSkyrimVersion == Mutagen.Bethesda.Skyrim.SkyrimRelease.EnderalSE; // not sure if JContainers actually works with Enderal
        bool checkVR = currentSkyrimVersion == Mutagen.Bethesda.Skyrim.SkyrimRelease.SkyrimVR;

        if ((checkSE && !File.Exists(dllPathSE_AE)) || (checkVR && !File.Exists(dllPathVR)))
        {
            if (!bSilent)
            {
                string dllName = "";
                string dllPath = "";
                if (checkSE) { dllName = "JContainers64.dll"; dllPath = dllPathSE_AE; }
                else if (checkVR) { dllName = "JContainersVR.dll"; dllPath = dllPathVR; }
                _logger.LogMessage("Could not find " + dllName + " from JContainers at " + dllPath);
                _logger.LogMessage("Please make sure JContainers is enabled.");
            }

            return false;
        }
        return true;
    }

    public bool VerifyBodyGenAnnotations(List<AssetPack> assetPacks, BodyGenConfigs bodyGenConfigs)
    {
        bool valid = true;
        List<string> missingBodyGenMessage = new List<string>();

        List<string> message = new List<string>();
        List<string> messages = new List<string>();
        HashSet<string> examinedConfigs = new HashSet<string>();

        foreach (var assetPack in assetPacks)
        {
            if (!string.IsNullOrWhiteSpace(assetPack.AssociatedBodyGenConfigName))
            {
                List<string> subMessage = new List<string>();
                BodyGenConfig bodyGenConfig = null;
                switch (assetPack.Gender)
                {
                    case Gender.Male: bodyGenConfig = bodyGenConfigs.Male.Where(x => x.Label == assetPack.AssociatedBodyGenConfigName).FirstOrDefault(); break;
                    case Gender.Female: bodyGenConfig = bodyGenConfigs.Female.Where(x => x.Label == assetPack.AssociatedBodyGenConfigName).FirstOrDefault(); break;
                }

                if (examinedConfigs.Contains(assetPack.AssociatedBodyGenConfigName)) { continue; }
                else
                {
                    examinedConfigs.Add(assetPack.AssociatedBodyGenConfigName);
                }

                if (bodyGenConfig == null)
                {
                    _logger.LogMessage("BodyGen Config " + assetPack.AssociatedBodyGenConfigName + " expected by " + assetPack.GroupName + " is not currently loaded.");
                    valid = false;
                }
                else
                {
                    foreach (var template in bodyGenConfig.Templates)
                    {
                        if (template.AllowRandom && !template.BodyShapeDescriptors.Any())
                        {
                            subMessage.Add(template.Label);
                        }
                    }
                    if (subMessage.Any())
                    {
                        message.Add("The following active BodyGen morphs in " + assetPack.AssociatedBodyGenConfigName + " have not been annotated with any body shape descriptors:");
                        message.AddRange(subMessage);
                    }
                }
            }
        }

        if (!valid)
        {
            return false;
        }
        else if (message.Any())
        {
            message.Add("Morphs that lack descriptors can be misassigned by the texture/body shape assigner. Do you want to continue patching?");
            return CustomMessageBox.DisplayNotificationYesNo("Missing Descriptors", String.Join(Environment.NewLine, message));
        }
        else
        {
            return true;
        }
    }

    public bool VerifyBlankAttributes(List<string> itemsWithBlankAttributes)
    {
        if (_patcherState.GeneralSettings.bChangeMeshesOrTextures)
        {
            foreach (var assetPack in _patcherState.AssetPacks.Where(x => _patcherState.TexMeshSettings.SelectedAssetPacks.Contains(x.GroupName)).ToArray())
            {
                if (HasBlankAttribute(assetPack.DistributionRules.AllowedAttributes) || HasBlankAttribute(assetPack.DistributionRules.DisallowedAttributes))
                {
                    itemsWithBlankAttributes.Add(assetPack.GroupName + " (Distribution Rules)");
                }

                List<string> subGroupIDs = new();
                foreach (var subgroup in assetPack.Subgroups)
                {
                    CheckSubgroupHasBlankAttribute(subgroup, subGroupIDs);
                }

                foreach (var replacer in assetPack.ReplacerGroups)
                {
                    foreach (var subgroup in replacer.Subgroups)
                    {
                        CheckSubgroupHasBlankAttribute(subgroup, subGroupIDs);
                    }
                }

                if (subGroupIDs.Any())
                {
                    itemsWithBlankAttributes.Add(assetPack.GroupName + ": Subgroups [" + String.Join(", ", subGroupIDs));
                }
            }
        }

        if(_patcherState.GeneralSettings.BodySelectionMode == BodyShapeSelectionMode.BodyGen)
        {
            foreach (var bodyGenConfig in _patcherState.BodyGenConfigs.Male.And(_patcherState.BodyGenConfigs.Female))
            {
                foreach (var template in bodyGenConfig.Templates)
                {
                    if (HasBlankAttribute(template.AllowedAttributes) || (HasBlankAttribute(template.DisallowedAttributes)))
                    {
                        itemsWithBlankAttributes.Add("BodyGen Template: " + template.Label);
                    }
                }
                foreach (var descriptor in bodyGenConfig.TemplateDescriptors)
                {
                    if (HasBlankAttribute(descriptor.AssociatedRules.AllowedAttributes) || HasBlankAttribute(descriptor.AssociatedRules.DisallowedAttributes))
                    {
                        itemsWithBlankAttributes.Add("BodyGen Descriptor: " + descriptor.ID.Category + ": " + descriptor.ID.Value);
                    }
                }
            }
        }
        else if (_patcherState.GeneralSettings.BodySelectionMode == BodyShapeSelectionMode.BodySlide)
        {
            foreach (var template in _patcherState.OBodySettings.BodySlidesMale.And(_patcherState.OBodySettings.BodySlidesFemale))
            {
                if (HasBlankAttribute(template.AllowedAttributes) || (HasBlankAttribute(template.DisallowedAttributes)))
                {
                    itemsWithBlankAttributes.Add("BodySlide: " + template.Label);
                }
            }
            foreach (var descriptor in _patcherState.OBodySettings.TemplateDescriptors)
            {
                if (HasBlankAttribute(descriptor.AssociatedRules.AllowedAttributes) || HasBlankAttribute(descriptor.AssociatedRules.DisallowedAttributes))
                {
                    itemsWithBlankAttributes.Add("BodySlide Descriptor: " + descriptor.ID.Category + ": " + descriptor.ID.Value);
                }
            }
        }

        if (_patcherState.GeneralSettings.bChangeHeadParts)
        {
            foreach (var headPartType in _patcherState.HeadPartSettings.Types.Keys)
            {
                var headPartList = _patcherState.HeadPartSettings.Types[headPartType];
                if (HasBlankAttribute(headPartList.AllowedAttributes) || HasBlankAttribute(headPartList.DisallowedAttributes))
                {
                    itemsWithBlankAttributes.Add("Head Part Rules: " + headPartType.ToString());
                }
                foreach (var headPart in headPartList.HeadParts)
                {
                    if (HasBlankAttribute(headPart.AllowedAttributes) || HasBlankAttribute(headPart.DisallowedAttributes))
                    {
                        itemsWithBlankAttributes.Add(headPartType.ToString() + ": " + headPart.EditorID);
                    }
                }
            }
        }

        return !itemsWithBlankAttributes.Any();
    }

    private bool CheckSubgroupHasBlankAttribute(AssetPack.Subgroup sg, List<string> subgroupIDs)
    {
        bool hasBlank = false;
        if (HasBlankAttribute(sg.AllowedAttributes) || HasBlankAttribute(sg.DisallowedAttributes))
        {
            subgroupIDs.Add(sg.ID);
            hasBlank = true;
        }
        foreach(var subgroup in sg.Subgroups)
        {
            if (CheckSubgroupHasBlankAttribute(subgroup, subgroupIDs))
            {
                hasBlank = true;
            }
        }
        return hasBlank;
    }

    private bool HasBlankAttribute(IEnumerable<NPCAttribute> attributes)
    {
        foreach (var attribute in attributes)
        {
            foreach (var subAttribute in attribute.SubAttributes)
            {
                if (subAttribute.IsBlank())
                {
                    return true;
                }
            }
        }
        return false;
    }

    public bool VerifyBodySlideAnnotations(Settings_OBody obodySettings)
    {
        List<string> bsMissingDescriptors = new List<string>();
        GetMissingBodySlideAnnotations(obodySettings.BodySlidesMale, obodySettings.CurrentlyExistingBodySlides, bsMissingDescriptors);
        GetMissingBodySlideAnnotations(obodySettings.BodySlidesFemale, obodySettings.CurrentlyExistingBodySlides, bsMissingDescriptors);

        if (bsMissingDescriptors.Any())
        {
            bsMissingDescriptors.Insert(0, "The following active BodySlides have not been annotated with any body shape descriptors:");
            bsMissingDescriptors.Add("Bodyslides that lack descriptors can be misassigned by the texture/body shape assigner. Do you want to continue patching?");
            return CustomMessageBox.DisplayNotificationYesNo("Missing Descriptors", String.Join(Environment.NewLine, bsMissingDescriptors));
        }
        else
        {
            return true;
        }
    }

    public void GetMissingBodySlideAnnotations(List<BodySlideSetting> bodySlidesInSettings, HashSet<string> bodySlideNamesInDataFolder, List<string> bsMissingDescriptors)
    {
        foreach (var bs in bodySlidesInSettings)
        {
            if (!bodySlideNamesInDataFolder.Contains(bs.Label)) { continue; } // don't validate bodyslides that aren't currently loaded because they won't be distributed anyway
            if (bs.AllowRandom && !bs.BodyShapeDescriptors.Any())
            {
                bsMissingDescriptors.Add(bs.Label);
            }
        }
    }
    public bool VerifyGeneratedTriFilesForOBody(Settings_OBody oBodySettings)
    {
        bool valid = true;
        if (oBodySettings.BodySlidesMale.Where(x => x.AllowRandom && oBodySettings.CurrentlyExistingBodySlides.Contains(x.Label)).Any())
        {
            string triPath = Path.Combine(_environmentProvider.DataFolderPath, "meshes", "actors", "character", "character assets", "malebody.tri");
            if (!File.Exists(triPath))
            {
                valid = false;
                _logger.LogMessage("Male BodySlides were detected but no malebody.tri was found at " + triPath);
            }
        }

        if (oBodySettings.BodySlidesFemale.Where(x => x.AllowRandom && oBodySettings.CurrentlyExistingBodySlides.Contains(x.Label)).Any())
        {
            string triPath = Path.Combine(_environmentProvider.DataFolderPath, "meshes", "actors", "character", "character assets", "femalebody.tri");
            if (!File.Exists(triPath))
            {
                valid = false;
                _logger.LogMessage("Female BodySlides were detected but no femalebody.tri was found at " + triPath);
            }
        }

        if (!valid)
        {
            _logger.LogMessage("Please make sure to check the `Build Morphs` box in BodySlide when generating your BodySlide output");
        }

        return valid;
    }

    public bool VerifyGeneratedTriFilesForBodyGen(List<AssetPack> assetPacks, BodyGenConfigs bodyGenConfigs)
    {
        bool valid = true;
        BodyGenHasActiveGenderedConfigs(assetPacks, bodyGenConfigs, out bool hasMaleConfigs, out bool hasFemaleConfigs);

        if (hasMaleConfigs)
        {
            string triPath = Path.Combine(_environmentProvider.DataFolderPath, "meshes", "actors", "character", "character assets", "malebody.tri");
            if (!File.Exists(triPath))
            {
                valid = false;
                _logger.LogMessage("Male BodyGen configs were detected but no malebody.tri was found at " + triPath);
            }
        }
        if (hasFemaleConfigs)
        {
            string triPath = Path.Combine(_environmentProvider.DataFolderPath, "meshes", "actors", "character", "character assets", "femalebody.tri");
            if (!File.Exists(triPath))
            {
                valid = false;
                _logger.LogMessage("Female BodyGen configs were detected but no femalebody.tri was found at " + triPath);
            }
        }

        if (!valid)
        {
            _logger.LogMessage("Please make sure to check the `Build Morphs` box in BodySlide when generating your zeroed BodySlide output");
        }

        return valid;
    }

    private void BodyGenHasActiveGenderedConfigs(List<AssetPack> assetPacks, BodyGenConfigs bodyGenConfigs, out bool hasMaleConfigs, out bool hasFemaleConfigs)
    {
        hasMaleConfigs = false;
        hasFemaleConfigs = false;

        foreach (var assetPack in assetPacks)
        {
            if (!string.IsNullOrWhiteSpace(assetPack.AssociatedBodyGenConfigName))
            {
                List<string> subMessage = new List<string>();
                BodyGenConfig bodyGenConfig = null;
                switch (assetPack.Gender)
                {
                    case Gender.Male: bodyGenConfig = bodyGenConfigs.Male.Where(x => x.Label == assetPack.AssociatedBodyGenConfigName).FirstOrDefault(); break;
                    case Gender.Female: bodyGenConfig = bodyGenConfigs.Female.Where(x => x.Label == assetPack.AssociatedBodyGenConfigName).FirstOrDefault(); break;
                }
                if (bodyGenConfig != null)
                {
                    switch (bodyGenConfig.Gender)
                    {
                        case Gender.Male: hasMaleConfigs = true; break;
                        case Gender.Female : hasFemaleConfigs = true; break;
                    }
                }
            }
        }
    }

    public bool VerifyRaceMenuIniForBodyGen()
    {
        bool valid = true;

        var iniContents = _raceMenuHandler.GetRaceMenuIniContents(out valid, out string iniFileName);

        string message = "";

        bool morphEnabled = _raceMenuHandler.GetBodyMorphEnabled(iniContents, out bool morphParsed, out string morphLine);
        if (!morphParsed)
        {
            valid = false;
            message = "Could not parse bEnableBodyMorph in " + iniFileName;
            if (morphLine.Any())
            {
                message += "( in line " + morphLine + ")";
            }
            _logger.LogMessage(message);
        }
        else if (!morphEnabled)
        {
            valid = false;
            _logger.LogMessage("bEnableBodyMorph must be enabled in " + iniFileName + " for BodyGen to work. Please fix this from the BodyGen menu.");
        }

        bool bodygenEnabled = _raceMenuHandler.GetBodyGenEnabled(iniContents, out bool bodyGenParsed, out string genLine);
        if (!bodyGenParsed)
        {
            valid = false;
            message = "Could not parse bEnableBodyGen in " + iniFileName;
            if (morphLine.Any())
            {
                message += "( in line " + genLine + ")";
            }
            _logger.LogMessage(message);
        }
        else if (!bodygenEnabled)
        {
            valid = false;
            _logger.LogMessage("bEnableBodyGen must be enabled in " + iniFileName + " for BodyGen to work. Please fix this from the BodyGen menu.");
        }

        int scaleMode = RaceMenuIniHandler.GetScaleMode(iniContents, out bool scaleModeParsed, out string scaleLine);
        if (!scaleModeParsed)
        {
            valid = false;
            message = "Could not parse iScaleMode in " + iniFileName;
            if (morphLine.Any())
            {
                message += "( in line " + scaleLine + ")";
            }
            _logger.LogMessage(message);
        }
        else if (scaleMode == 0)
        {
            valid = false;
            _logger.LogMessage("iScaleMode must not be 0 in " + iniFileName + " for BodyGen to work. Please fix this from the BodyGen menu.");
        }
        else if (scaleMode == 2)
        {
            _logger.LogMessage("Warning: iScaleMode is set to 2 in " + iniFileName + ". This can cause NPC weapons to become supersized when the game is loaded. It is recommended to set this to 1 or 3.");
        }

        return valid;
    }

    public bool VerifyRaceMenuIniForBodySlide()
    {
        bool valid = true;

        var iniContents = _raceMenuHandler.GetRaceMenuIniContents(out valid, out string iniFileName);

        string message = "";

        bool morphEnabled = _raceMenuHandler.GetBodyMorphEnabled(iniContents, out bool morphParsed, out string morphLine);
        if (!morphParsed)
        {
            valid = false;
            message = "Could not parse bEnableBodyMorph in " + iniFileName;
            if (morphLine.Any())
            {
                message += "( in line " + morphLine + ")";
            }
            _logger.LogMessage(message);
        }
        else if (!morphEnabled)
        {
            valid = false;
            _logger.LogMessage("bEnableBodyMorph must be enabled in " + iniFileName + " for " + _patcherState.GeneralSettings.BSSelectionMode + " to work. Please fix this from the OBody Settings menu.");
        }

        bool bodygenEnabled = _raceMenuHandler.GetBodyGenEnabled(iniContents, out bool bodyGenParsed, out string genLine);
        if (!bodyGenParsed)
        {
            valid = false;
            message = "Could not parse bEnableBodyGen in " + iniFileName;
            if (morphLine.Any())
            {
                message += "( in line " + genLine + ")";
            }
            _logger.LogMessage(message);
        }
        else if (bodygenEnabled)
        {
            valid = false;
            _logger.LogMessage("bEnableBodyGen must be disabled in " + iniFileName + " for " + _patcherState.GeneralSettings.BSSelectionMode + " to work. Please fix this from the OBody Settings menu.");
        }

        return valid;
    }

    public static IEnumerable<RaceGrouping> CheckRaceGroupingDuplicates(IEnumerable<RaceGrouping> raceGroupings, string parentDispName)
    {
        var filteredRaceGroupings = raceGroupings.ToList();

        List<string> names = new();
        List<string> duplicates = new();

        foreach (var g in raceGroupings)
        {
            if (names.Contains(g.Label))
            {
                duplicates.Add(g.Label);
            }
            names.Add(g.Label);
        }

        if (duplicates.Any())
        {
            string message = "Duplicate Race Groupings detected in " + parentDispName + ". Remove duplicates? [Only the first occurrence will be kept; make sure this is the one you want to save.]" + Environment.NewLine;

            foreach (var g in duplicates.Distinct())
            {
                message += g + " (" + (duplicates.Where(x => x == g).Count() + 1) + ")" + Environment.NewLine;
            }

            if (CustomMessageBox.DisplayNotificationYesNo("Duplicate Race Groupings", message))
            {
                foreach (var name in duplicates)
                {
                    bool triggered = false;
                    int duplicateCount = duplicates.Where(x => x == name).ToArray().Count();
                    for (int i = 0; i < filteredRaceGroupings.Count; i++)
                    {
                        if (filteredRaceGroupings[i].Label == name)
                        {
                            if (triggered)
                            {
                                filteredRaceGroupings.RemoveAt(i);
                                i--;
                            }
                            else
                            {
                                triggered = true;
                            }
                        }
                    }
                }

                return filteredRaceGroupings;
            }
        }
        return raceGroupings;
    }
}
