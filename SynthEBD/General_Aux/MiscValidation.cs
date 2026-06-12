using System.IO;
using Noggog;

namespace SynthEBD;

/// <summary>
/// A collection of pre-run "is dependency X installed / configured correctly" checks: required SKSE
/// plugins and Papyrus scripts (EBD, RaceMenu, OBody/AutoBody, JContainers, SPID, SkyPatcher, PO3),
/// RaceMenu ini settings, generated body <c>.tri</c> morphs, BodySlide label/reference integrity,
/// blank-attribute detection, and race-grouping de-duplication. Each check logs actionable guidance.
/// </summary>
public class MiscValidation
{
    private readonly IEnvironmentStateProvider _environmentProvider;
    private readonly Logger _logger;
    private readonly SynthEBDPaths _paths;
    private readonly RaceMenuIniHandler _raceMenuHandler;
    private readonly PatcherState _patcherState;
    /// <summary>Creates the validation helper.</summary>
    /// <param name="environmentProvider">Supplies the data-folder path and Skyrim version.</param>
    /// <param name="patcherState">Settings/state inspected by the verifications.</param>
    /// <param name="logger">Logger for surfacing problems.</param>
    /// <param name="paths">Resolved SynthEBD paths.</param>
    /// <param name="raceMenuHandler">Reads/parses the RaceMenu ini.</param>
    public MiscValidation(IEnvironmentStateProvider environmentProvider, PatcherState patcherState, Logger logger, SynthEBDPaths paths, RaceMenuIniHandler raceMenuHandler)
    {
        _environmentProvider = environmentProvider;
        _patcherState = patcherState;
        _logger = logger;
        _paths = paths;
        _raceMenuHandler = raceMenuHandler;
    }

    /// <summary>
    /// Shared "are these Data-folder files present" check behind the simple <c>Verify*Installed</c> helpers.
    /// For each descriptor it composes the full path under <paramref name="dataFolderPath"/> and, when the file
    /// is absent (per the injected <paramref name="fileExists"/> probe), appends a
    /// "Could not find {file} from {source} at {path}" line to <paramref name="messages"/>. If anything was
    /// missing and <paramref name="installHint"/> is non-null, the hint is appended last. The probe is injected
    /// (and the messages are returned rather than logged) so the assembly logic is pure and unit-testable
    /// without a Data folder; callers pass <see cref="File.Exists(string)"/> and decide whether to emit.
    /// </summary>
    /// <returns><c>true</c> if every descriptor's file exists.</returns>
    public static bool CheckDataFiles(string dataFolderPath, IReadOnlyList<(string RelativePath, string SourceName)> files, string? installHint, Func<string, bool> fileExists, out List<string> messages)
    {
        messages = new();
        bool verified = true;
        foreach (var (relativePath, sourceName) in files)
        {
            string fullPath = Path.Combine(dataFolderPath, relativePath);
            if (!fileExists(fullPath))
            {
                messages.Add("Could not find " + Path.GetFileName(relativePath) + " from " + sourceName + " at " + fullPath);
                verified = false;
            }
        }
        if (!verified && installHint != null)
        {
            messages.Add(installHint);
        }
        return verified;
    }

    /// <summary>Verifies that EveryBody's Different Redone's Papyrus scripts are present in the Data folder.</summary>
    /// <returns><c>true</c> if both required <c>.pex</c> scripts exist.</returns>
    public bool VerifyEBDInstalled()
    {
        bool verified = CheckDataFiles(_environmentProvider.DataFolderPath, new[]
        {
            (@"Scripts\EBDHelperScript.pex", "EveryBody's Different Redone SSE"),
            (@"Scripts\EBDGlobalFuncs.pex", "EveryBody's Different Redone SSE"),
        }, "Please make sure that EveryBody's Different Redone SSE is installed.", File.Exists, out var messages);
        foreach (var message in messages) { _logger.LogMessage(message); }
        return verified;
    }

    /// <summary>Verifies that RaceMenu (SE or VR) is installed by checking for its SKSE plugin DLL and ini.</summary>
    /// <returns><c>true</c> if a RaceMenu DLL and ini are present.</returns>
    public bool VerifyRaceMenuInstalled()
    {
        // Not folded into CheckDataFiles (R1): this is an either/or check (skee64 OR skeevr) and it logs the
        // containing directory rather than the full file path, so it does not fit the uniform descriptor shape.
        bool verified = true;

        string dllPath64 = Path.Combine(_environmentProvider.DataFolderPath, "SKSE", "Plugins", "skee64.dll");
        string dllPathVR = Path.Combine(_environmentProvider.DataFolderPath, "SKSE", "Plugins", "skeevr.dll");
        if (!File.Exists(dllPath64) && !File.Exists(dllPathVR))
        {
            _logger.LogMessage("Could not find skee64.dll from RaceMenu at " + Path.Combine(_environmentProvider.DataFolderPath, "SKSE", "Plugins"));
            verified = false;
        }

        string iniPath64 = Path.Combine(_environmentProvider.DataFolderPath, "SKSE", "Plugins", "skee64.ini");
        string iniPathVR = Path.Combine(_environmentProvider.DataFolderPath, "SKSE", "Plugins", "skeevr.ini");
        if (!File.Exists(iniPath64) && !File.Exists(iniPathVR))
        {
            _logger.LogMessage("Could not find skee64.ini from RaceMenu at " + Path.Combine(_environmentProvider.DataFolderPath, "SKSE", "Plugins"));
            verified = false;
        }

        if (!verified)
        {
            _logger.LogMessage("Please make sure that RaceMenu SE is installed.");
        }

        return verified;
    }

    /// <summary>Verifies that OBody is installed (its Papyrus script and SKSE plugin DLL).</summary>
    /// <returns><c>true</c> if both files exist.</returns>
    public bool VerifyOBodyInstalled()
    {
        bool verified = CheckDataFiles(_environmentProvider.DataFolderPath, new[]
        {
            (@"Scripts\OBodyNative.pex", "OBody"),
            (@"SKSE\Plugins\OBody.dll", "OBody"),
        }, "Please make sure that OBody is installed.", File.Exists, out var messages);
        foreach (var message in messages) { _logger.LogMessage(message); }
        return verified;
    }

    /// <summary>Verifies that OBody's preset-distribution config JSON exists in the Data folder.</summary>
    /// <returns><c>true</c> if the config JSON is present.</returns>
    public bool VerifyOBodyTemplateJsonExists()
    {
        bool verified = CheckDataFiles(_environmentProvider.DataFolderPath, new[]
        {
            (@"SKSE\Plugins\OBody_presetDistributionConfig.json", "OBody"),
        }, null, File.Exists, out var messages);
        foreach (var message in messages) { _logger.LogMessage(message); }
        return verified;
    }

    /// <summary>Verifies that AutoBody is installed (its Papyrus script and SKSE plugin DLL).</summary>
    /// <returns><c>true</c> if both files exist.</returns>
    public bool VerifyAutoBodyInstalled()
    {
        bool verified = CheckDataFiles(_environmentProvider.DataFolderPath, new[]
        {
            (@"Scripts\autoBodyUtils.pex", "AutoBody"),
            (@"SKSE\Plugins\autoBodyAE.dll", "AutoBody"),
        }, "Please make sure that AutoBody is installed.", File.Exists, out var messages);
        foreach (var message in messages) { _logger.LogMessage(message); }
        return verified;
    }

    /// <summary>Verifies that every distributable BodySlide setting has a unique label within its gender's
    /// list. Settings referencing uninstalled BodySlides or lacking a detected Registry Body Type are
    /// skipped: RunPatcher filters both out of distribution, so their labels can never collide at
    /// assignment time. The male and female lists are validated independently because NPCs only ever
    /// select from the gender-matching list, so a label shared across genders is unambiguous.</summary>
    /// <returns><c>true</c> if no duplicate labels were found.</returns>
    public bool VerifyBodySlideUniqueLabels()
    {
        // & rather than && so both lists get validated and logged in one pass
        return VerifyBodySlideUniqueLabels(_patcherState.OBodySettings.BodySlidesMale, "male")
            & VerifyBodySlideUniqueLabels(_patcherState.OBodySettings.BodySlidesFemale, "female");
    }

    /// <summary>Checks one gender's BodySlide list for duplicate labels among distributable entries.</summary>
    /// <param name="bodySlides">The gender-specific BodySlide list to validate.</param>
    /// <param name="genderLabel">Gender name used in the duplicate log message.</param>
    /// <returns><c>true</c> if no duplicate labels were found.</returns>
    private bool VerifyBodySlideUniqueLabels(List<BodySlideSetting> bodySlides, string genderLabel)
    {
        HashSet<string> existingLabels = new();
        bool foundDuplicate = false;
        foreach (var bodySlide in bodySlides)
        {
            if (!_patcherState.OBodySettings.CurrentlyExistingBodySlides.Contains(bodySlide.ReferencedBodySlide) || !bodySlide.HasDetectedBodyType)
            {
                continue; // not distributable (mirrors the RunPatcher BodySlide filters)
            }
            if (!existingLabels.Add(bodySlide.Label))
            {
                _logger.LogMessage("Found duplicate " + genderLabel + " BodySlide name: " + bodySlide.Label + ". Names must be unique within each gender's list even if the linked BodySlide is the same.");
                foundDuplicate = true;
            }
        }
        return !foundDuplicate;
    }

    /// <summary>Verifies that every configured BodySlide setting references a non-empty BodySlide.</summary>
    /// <returns><c>true</c> if none are empty.</returns>
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

    /// <summary>Verifies that Spell Perk Item Distributor (SPID) is installed.</summary>
    /// <param name="bSilent">When true, suppresses the not-found log messages.</param>
    /// <returns><c>true</c> if the SPID DLL is present.</returns>
    public bool VerifySPIDInstalled(bool bSilent)
    {
        bool verified = CheckDataFiles(_environmentProvider.DataFolderPath, new[]
        {
            (@"SKSE\Plugins\po3_SpellPerkItemDistributor.dll", "Spell Perk Item Distributor"),
        }, "Please make sure Spell Perk Item Distributor is enabled.", File.Exists, out var messages);
        if (!bSilent) { foreach (var message in messages) { _logger.LogMessage(message); } }
        return verified;
    }
    
    /// <summary>Verifies that SkyPatcher is installed.</summary>
    /// <param name="bSilent">When true, suppresses the not-found log messages.</param>
    /// <returns><c>true</c> if the SkyPatcher DLL is present.</returns>
    public bool VerifySkyPatcherInstalled(bool bSilent)
    {
        bool verified = CheckDataFiles(_environmentProvider.DataFolderPath, new[]
        {
            (@"SKSE\Plugins\SkyPatcher.dll", "SkyPatcher"),
        }, "Please make sure SkyPatcher is enabled.", File.Exists, out var messages);
        if (!bSilent) { foreach (var message in messages) { _logger.LogMessage(message); } }
        return verified;
    }

    /// <summary>Verifies that the JContainers build matching the current Skyrim edition (SE/AE vs VR) is installed.</summary>
    /// <param name="bSilent">When true, suppresses the not-found log messages.</param>
    /// <returns><c>true</c> if the appropriate JContainers DLL is present.</returns>
    public bool VerifyJContainersInstalled(bool bSilent)
    {
        // Not folded into CheckDataFiles (R1): the file checked is conditional on the Skyrim edition
        // (SE/AE -> JContainers64.dll vs VR -> JContainersVR.dll), so it does not fit the uniform shape.
        string dllPathSE_AE = Path.Combine(_environmentProvider.DataFolderPath, "SKSE", "Plugins", "JContainers64.dll");
        string dllPathVR = Path.Combine(_environmentProvider.DataFolderPath, "SKSE", "Plugins", "JContainersVR.dll");

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

    /// <summary>Checks that active BodyGen morphs referenced by the given asset packs have body-shape descriptors, prompting the user to continue if some are unannotated.</summary>
    /// <param name="assetPacks">Asset packs whose associated BodyGen configs are checked.</param>
    /// <param name="bodyGenConfigs">Available BodyGen configs.</param>
    /// <returns><c>true</c> to proceed (all annotated, or the user chose to continue); <c>false</c> if a referenced config is missing or the user declined.</returns>
    public bool VerifyBodyGenAnnotations(List<AssetPack> assetPacks, BodyGenConfigs bodyGenConfigs)
    {
        bool valid = true;

        List<string> message = new List<string>();
        HashSet<string> examinedConfigs = new HashSet<string>();

        foreach (var assetPack in assetPacks)
        {
            if (!string.IsNullOrWhiteSpace(assetPack.AssociatedBodyGenConfigName))
            {
                List<string> subMessage = new List<string>();
                BodyGenConfig bodyGenConfig = null;
                switch (assetPack.Gender)
                {
                    case Gender.Male: bodyGenConfig = bodyGenConfigs.Male.FirstOrDefault(x => x.Label == assetPack.AssociatedBodyGenConfigName); break;
                    case Gender.Female: bodyGenConfig = bodyGenConfigs.Female.FirstOrDefault(x => x.Label == assetPack.AssociatedBodyGenConfigName); break;
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
            return MessageWindow.DisplayNotificationYesNo("Missing Descriptors", String.Join(Environment.NewLine, message));
        }
        else
        {
            return true;
        }
    }

    /// <summary>Scans asset packs, body-shape configs, and head-part rules for NPC attributes that are blank (which can break distribution), collecting the offending items.</summary>
    /// <param name="itemsWithBlankAttributes">Receives a description of each item that has a blank attribute.</param>
    /// <returns><c>true</c> if no blank attributes were found.</returns>
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
                    itemsWithBlankAttributes.Add(assetPack.GroupName + ": Subgroups [" + String.Join(", ", subGroupIDs) + "]");
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
                foreach (var descriptor in bodyGenConfig.TemplateDescriptors.Flatten())
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
            foreach (var descriptor in _patcherState.OBodySettings.TemplateDescriptors.Flatten())
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

    /// <summary>Recursively records the IDs of a subgroup (and its descendants) that have any blank attribute.</summary>
    /// <param name="sg">The subgroup to check.</param>
    /// <param name="subgroupIDs">Receives the IDs of subgroups with blank attributes.</param>
    /// <returns><c>true</c> if this subgroup or any descendant had a blank attribute.</returns>
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

    /// <summary>Determines whether any sub-attribute within the given attributes is blank.</summary>
    /// <param name="attributes">Attributes to inspect.</param>
    /// <returns><c>true</c> if at least one sub-attribute is blank.</returns>
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

    /// <summary>Checks that active, currently-present BodySlides have body-shape descriptors, prompting the user to continue if some are unannotated (unless auto-annotation is enabled).</summary>
    /// <param name="obodySettings">OBody settings containing the BodySlides and the existing-BodySlide list.</param>
    /// <returns><c>true</c> to proceed; <c>false</c> if the user declined.</returns>
    public bool VerifyBodySlideAnnotations(Settings_OBody obodySettings)
    {
        if (obodySettings.AutoApplyMissingAnnotations)
        {
            return true;
        }

        List<string> bsMissingDescriptors = new List<string>();
        GetMissingBodySlideAnnotations(obodySettings.BodySlidesMale, obodySettings.CurrentlyExistingBodySlides, bsMissingDescriptors);
        GetMissingBodySlideAnnotations(obodySettings.BodySlidesFemale, obodySettings.CurrentlyExistingBodySlides, bsMissingDescriptors);

        if (bsMissingDescriptors.Any())
        {
            bsMissingDescriptors.Insert(0, "The following active BodySlides have not been annotated with any body shape descriptors:");
            bsMissingDescriptors.Add("Bodyslides that lack descriptors can be misassigned by the texture/body shape assigner. Do you want to continue patching?");
            return MessageWindow.DisplayNotificationYesNo("Missing Descriptors", String.Join(Environment.NewLine, bsMissingDescriptors));
        }
        else
        {
            return true;
        }
    }

    /// <summary>Collects the labels of active BodySlides that are present in the Data folder but lack descriptors.</summary>
    /// <param name="bodySlidesInSettings">Configured BodySlides to check.</param>
    /// <param name="bodySlideNamesInDataFolder">Labels of BodySlides actually present (others are ignored, since they won't be distributed).</param>
    /// <param name="bsMissingDescriptors">Receives the labels missing descriptors.</param>
    public void GetMissingBodySlideAnnotations(List<BodySlideSetting> bodySlidesInSettings, HashSet<string> bodySlideNamesInDataFolder, List<string> bsMissingDescriptors)
    {
        foreach (var bs in bodySlidesInSettings)
        {
            if (!bodySlideNamesInDataFolder.Contains(bs.Label)) { continue; } // don't validate bodyslides that aren't currently loaded because they won't be distributed anyway
            if (bs.AllowRandom && !bs.HasAnyDescriptors())
            {
                bsMissingDescriptors.Add(bs.Label);
            }
        }
    }
    /// <summary>Verifies that the body <c>.tri</c> morph files required by active OBody BodySlides exist for each gender in use.</summary>
    /// <param name="oBodySettings">OBody settings listing the active BodySlides.</param>
    /// <returns><c>true</c> if the needed <c>malebody.tri</c>/<c>femalebody.tri</c> files exist.</returns>
    public bool VerifyGeneratedTriFilesForOBody(Settings_OBody oBodySettings)
    {
        bool valid = true;
        if (oBodySettings.BodySlidesMale.Any(x => x.AllowRandom && oBodySettings.CurrentlyExistingBodySlides.Contains(x.Label)))
        {
            string triPath = Path.Combine(_environmentProvider.DataFolderPath, "meshes", "actors", "character", "character assets", "malebody.tri");
            if (!File.Exists(triPath))
            {
                valid = false;
                _logger.LogMessage("Male BodySlides were detected but no malebody.tri was found at " + triPath);
            }
        }

        if (oBodySettings.BodySlidesFemale.Any(x => x.AllowRandom && oBodySettings.CurrentlyExistingBodySlides.Contains(x.Label)))
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

    /// <summary>Verifies that the body <c>.tri</c> morph files required by active BodyGen configs exist for each gender in use.</summary>
    /// <param name="assetPacks">Asset packs referencing BodyGen configs.</param>
    /// <param name="bodyGenConfigs">Available BodyGen configs.</param>
    /// <returns><c>true</c> if the needed <c>.tri</c> files exist.</returns>
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

    /// <summary>Determines whether the asset packs reference any active male and/or female BodyGen configs.</summary>
    /// <param name="assetPacks">Asset packs to scan.</param>
    /// <param name="bodyGenConfigs">Available BodyGen configs.</param>
    /// <param name="hasMaleConfigs">Receives whether any male config is referenced.</param>
    /// <param name="hasFemaleConfigs">Receives whether any female config is referenced.</param>
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
                    case Gender.Male: bodyGenConfig = bodyGenConfigs.Male.FirstOrDefault(x => x.Label == assetPack.AssociatedBodyGenConfigName); break;
                    case Gender.Female: bodyGenConfig = bodyGenConfigs.Female.FirstOrDefault(x => x.Label == assetPack.AssociatedBodyGenConfigName); break;
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

    /// <summary>Appends a "( in line: &lt;text&gt;)" locator to an ini-parse error message when the offending line text is known.</summary>
    /// <param name="message">The base error message.</param>
    /// <param name="line">The raw ini line the error refers to (empty when the setting was absent).</param>
    /// <returns>The message with the line reference appended, or the message unchanged when <paramref name="line"/> is empty.</returns>
    /// <remarks>Taking the gated value as a single parameter keeps the guard and the appended text in lockstep, so they cannot reference different lines (the B16 defect).</remarks>
    public static string AppendIniLineReference(string message, string line)
    {
        return line.Any() ? message + "( in line: " + line + ")" : message;
    }

    /// <summary>Verifies the RaceMenu ini settings required for BodyGen: body morph and BodyGen enabled, and a non-zero scale mode (warning on the problematic mode 2).</summary>
    /// <returns><c>true</c> if the ini is configured correctly for BodyGen.</returns>
    public bool VerifyRaceMenuIniForBodyGen()
    {
        bool valid = true;

        var iniContents = _raceMenuHandler.GetRaceMenuIniContents(out valid, out string iniFileName);

        string message = "";

        bool morphEnabled = _raceMenuHandler.GetBodyMorphEnabled(iniContents, out bool morphParsed, out string morphLine);
        if (!morphParsed)
        {
            valid = false;
            message = AppendIniLineReference("Could not parse bEnableBodyMorph in " + iniFileName, morphLine);
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
            message = AppendIniLineReference("Could not parse bEnableBodyGen in " + iniFileName, genLine);
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
            message = AppendIniLineReference("Could not parse iScaleMode in " + iniFileName, scaleLine);
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

    /// <summary>Verifies the RaceMenu ini settings required for BodySlide/OBody: body morph enabled and BodyGen disabled.</summary>
    /// <returns><c>true</c> if the ini is configured correctly for BodySlide.</returns>
    public bool VerifyRaceMenuIniForBodySlide()
    {
        bool valid = true;

        var iniContents = _raceMenuHandler.GetRaceMenuIniContents(out valid, out string iniFileName);

        string message = "";

        bool morphEnabled = _raceMenuHandler.GetBodyMorphEnabled(iniContents, out bool morphParsed, out string morphLine);
        if (!morphParsed)
        {
            valid = false;
            message = AppendIniLineReference("Could not parse bEnableBodyMorph in " + iniFileName, morphLine);
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
            message = AppendIniLineReference("Could not parse bEnableBodyGen in " + iniFileName, genLine);
            _logger.LogMessage(message);
        }
        else if (bodygenEnabled)
        {
            valid = false;
            _logger.LogMessage("bEnableBodyGen must be disabled in " + iniFileName + " for " + _patcherState.GeneralSettings.BSSelectionMode + " to work. Please fix this from the OBody Settings menu.");
        }

        return valid;
    }

    /// <summary>Detects duplicate race-grouping labels and, with user confirmation, returns a de-duplicated list keeping the first occurrence of each.</summary>
    /// <param name="raceGroupings">The race groupings to check.</param>
    /// <param name="parentDispName">Display name of the containing settings, used in the prompt.</param>
    /// <returns>A de-duplicated list if the user opted to remove duplicates; otherwise the original list.</returns>
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
                message += g + " (" + (duplicates.Count(x => x == g) + 1) + ")" + Environment.NewLine;
            }

            if (MessageWindow.DisplayNotificationYesNo("Duplicate Race Groupings", message))
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

    /// <summary>Verifies that PowerOfThree's Papyrus Extender (script and DLL) is installed.</summary>
    /// <returns><c>true</c> if both files exist.</returns>
    public bool VerifyPO3ExtenderInstalled()
    {
        // The dll's source string reads "Papyrus Extender VR" (inconsistent with the script's
        // "PowerOfThree's Papyrus Extender"); preserved verbatim by R1's per-file SourceName.
        bool valid = CheckDataFiles(_environmentProvider.DataFolderPath, new[]
        {
            (@"Scripts\PO3_SKSEFunctions.pex", "PowerOfThree's Papyrus Extender"),
            (@"SKSE\Plugins\po3_PapyrusExtender.dll", "Papyrus Extender VR"),
        }, null, File.Exists, out var messages);
        foreach (var message in messages) { _logger.LogMessage(message); }
        return valid;
    }

    /// <summary>Verifies that powerofthree's Tweaks (script and DLL) is installed.</summary>
    /// <returns><c>true</c> if both files exist.</returns>
    public bool VerifyPO3TweaksInstalled()
    {
        // The dll's source string reads "powerofthree's Tweaks VR" (inconsistent with the script's
        // "powerofthree's Tweaks"); preserved verbatim by R1's per-file SourceName.
        bool valid = CheckDataFiles(_environmentProvider.DataFolderPath, new[]
        {
            (@"Scripts\po3_Tweaks.pex", "powerofthree's Tweaks"),
            (@"SKSE\Plugins\po3_Tweaks.dll", "powerofthree's Tweaks VR"),
        }, null, File.Exists, out var messages);
        foreach (var message in messages) { _logger.LogMessage(message); }
        return valid;
    }
}
