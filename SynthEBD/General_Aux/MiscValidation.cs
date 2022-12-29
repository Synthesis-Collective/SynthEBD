using System.IO;
using Noggog;

namespace SynthEBD;

public class MiscValidation
{
    private readonly IStateProvider _stateProvider;
    private readonly Logger _logger;
    private readonly SynthEBDPaths _paths;
    private readonly RaceMenuIniHandler _raceMenuHandler;
    public MiscValidation(IStateProvider stateProvider, Logger logger, SynthEBDPaths paths, RaceMenuIniHandler raceMenuHandler)
    {
        _stateProvider = stateProvider;
        _logger = logger;
        _paths = paths;
        _raceMenuHandler = raceMenuHandler;
    }
    public bool VerifyEBDInstalled()
    {
        bool verified = true;

        string helperScriptPath = Path.Combine(_stateProvider.DataFolderPath, "Scripts", "EBDHelperScript.pex");
        if (!File.Exists(helperScriptPath))
        {
            _logger.LogMessage("Could not find EBDHelperScript.pex from EveryBody's Different Redone SSE at " + helperScriptPath);
            verified = false;
        }

        string globalScriptPath = Path.Combine(_stateProvider.DataFolderPath, "Scripts", "EBDGlobalFuncs.pex");
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

        var currentSkyrimVersion = _stateProvider.SkyrimVersion;
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
            string triPath = Path.Combine(_stateProvider.DataFolderPath, "meshes", "actors", "character", "character assets", "malebody.tri");
            if (!File.Exists(triPath))
            {
                valid = false;
                _logger.LogMessage("Male BodySlides were detected but no malebody.tri was found at " + triPath);
            }
        }

        if (oBodySettings.BodySlidesFemale.Where(x => x.AllowRandom && oBodySettings.CurrentlyExistingBodySlides.Contains(x.Label)).Any())
        {
            string triPath = Path.Combine(_stateProvider.DataFolderPath, "meshes", "actors", "character", "character assets", "femalebody.tri");
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
            string triPath = Path.Combine(_stateProvider.DataFolderPath, "meshes", "actors", "character", "character assets", "malebody.tri");
            if (!File.Exists(triPath))
            {
                valid = false;
                _logger.LogMessage("Male BodyGen configs were detected but no malebody.tri was found at " + triPath);
            }
        }
        if (hasFemaleConfigs)
        {
            string triPath = Path.Combine(_stateProvider.DataFolderPath, "meshes", "actors", "character", "character assets", "femalebody.tri");
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
            _logger.LogMessage("bEableBodyGen must be enabled in " + iniFileName + " for BodyGen to work. Please fix this from the BodyGen menu.");
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
            _logger.LogMessage("bEableBodyGen must be enabled in " + iniFileName + " for BodyGen to work. Please fix this from the BodyGen Integration menu.");
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
            _logger.LogMessage("bEnableBodyGen must be disabled in " + iniFileName + " for OBody/AutoBody to work. Please fix this from the OBody Settings menu.");
        }

        return valid;
    }
}
