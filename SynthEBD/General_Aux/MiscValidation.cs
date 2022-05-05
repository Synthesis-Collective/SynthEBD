using System.IO;

namespace SynthEBD
{
    public class MiscValidation
    {
        public static bool VerifyEBDInstalled()
        {
            bool verified = true;

            string helperScriptPath = Path.Combine(PatcherEnvironmentProvider.Environment.DataFolderPath, "Scripts", "EBDHelperScript.pex");
            if (!File.Exists(helperScriptPath))
            {
                Logger.LogMessage("Could not find EBDHelperScript.pex from EveryBody's Different Redone SSE at " + helperScriptPath);
                verified = false;
            }

            string globalScriptPath = Path.Combine(PatcherEnvironmentProvider.Environment.DataFolderPath, "Scripts", "EBDGlobalFuncs.pex");
            if (!File.Exists(globalScriptPath))
            {
                Logger.LogMessage("Could not find EBDGlobalFuncs.pex from EveryBody's Different Redone SSE at " + globalScriptPath);
                verified = false;
            }

            if (!verified)
            {
                Logger.LogMessage("Please make sure that EveryBody's Different Redone SSE is installed.");
            }

            return verified;
        }

        public static bool VerifyRaceMenuInstalled()
        {
            bool verified = true;

            string dllPath64 = Path.Combine(PatcherEnvironmentProvider.Environment.DataFolderPath, "SKSE", "Plugins", "skee64.dll");
            string dllPathVR = Path.Combine(PatcherEnvironmentProvider.Environment.DataFolderPath, "SKSE", "Plugins", "skeevr.dll");
            if (!File.Exists(dllPath64) && !File.Exists(dllPathVR))
            {
                Logger.LogMessage("Could not find skee64.dll from RaceMenu at " + Path.Combine(PatcherEnvironmentProvider.Environment.DataFolderPath, "SKSE", "Plugins"));
                verified = false;
            }

            string iniPath64 = Path.Combine(PatcherEnvironmentProvider.Environment.DataFolderPath, "SKSE", "Plugins", "skee64.ini");
            string iniPathVR = Path.Combine(PatcherEnvironmentProvider.Environment.DataFolderPath, "SKSE", "Plugins", "skeevr.ini");
            if (!File.Exists(iniPath64) && !File.Exists(iniPathVR))
            {
                Logger.LogMessage("Could not find skee64.ini from RaceMenu at " + Path.Combine(PatcherEnvironmentProvider.Environment.DataFolderPath, "SKSE", "Plugins"));
                verified = false;
            }

            if (!verified)
            {
                Logger.LogMessage("Please make sure that RaceMenu SE is installed.");
            }

            return verified;
        }

        public static bool VerifyOBodyInstalled()
        {
            bool verified = true;

            string scriptPath = Path.Combine(PatcherEnvironmentProvider.Environment.DataFolderPath, "Scripts", "OBodyNative.pex");
            if (!File.Exists(scriptPath))
            {
                Logger.LogMessage("Could not find OBodyNative.pex from OBody at " + scriptPath);
                verified = false;
            }

            string dllPath = Path.Combine(PatcherEnvironmentProvider.Environment.DataFolderPath, "SKSE", "Plugins", "OBody.dll");
            if (!File.Exists(dllPath))
            {
                Logger.LogMessage("Could not find OBody.dll from OBody at " + dllPath);
                verified = false;
            }

            if (!verified)
            {
                Logger.LogMessage("Please make sure that OBody is installed.");
            }

            return verified;
        }

        public static bool VerifyAutoBodyInstalled()
        {
            bool verified = true;

            string scriptPath = Path.Combine(PatcherEnvironmentProvider.Environment.DataFolderPath, "Scripts", "autoBodyUtils.pex");
            if (!File.Exists(scriptPath))
            {
                Logger.LogMessage("Could not find autoBodyUtils.pex from AutoBody at " + scriptPath);
                verified = false;
            }

            string dllPath = Path.Combine(PatcherEnvironmentProvider.Environment.DataFolderPath, "SKSE", "Plugins", "autoBodyAE.dll");
            if (!File.Exists(dllPath))
            {
                Logger.LogMessage("Could not find autoBodyAE.dll from AutoBody at " + dllPath);
                verified = false;
            }

            if (!verified)
            {
                Logger.LogMessage("Please make sure that AutoBody is installed.");
            }

            return verified;
        }

        public static bool VerifySPIDInstalled()
        {
            string dllPath = Path.Combine(PatcherEnvironmentProvider.Environment.DataFolderPath, "SKSE", "Plugins", "po3_SpellPerkItemDistributor.dll");
            if (!File.Exists(dllPath))
            {
                Logger.LogMessage("Could not find po3_SpellPerkItemDistributor.dll from Spell Perk Item Distributor at " + dllPath);
                Logger.LogMessage("Please make sure Spell Perk Item Distributor is enabled.");
                return false;
            }
            return true;
        }

        public static bool VerifyBodyGenAnnotations(List<AssetPack> assetPacks, BodyGenConfigs bodyGenConfigs)
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
                        Logger.LogMessage("BodyGen Config " + assetPack.AssociatedBodyGenConfigName + " expected by " + assetPack.GroupName + " is not currently loaded.");
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

        public static bool VerifyBodySlideAnnotations(Settings_OBody obodySettings)
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

        public static void GetMissingBodySlideAnnotations(List<BodySlideSetting> bodySlidesInSettings, HashSet<string> bodySlideNamesInDataFolder, List<string> bsMissingDescriptors)
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
        public static bool VerifyGeneratedTriFilesForOBody(Settings_OBody oBodySettings)
        {
            bool valid = true;
            if (oBodySettings.BodySlidesMale.Where(x => x.AllowRandom && oBodySettings.CurrentlyExistingBodySlides.Contains(x.Label)).Any())
            {
                string triPath = Path.Combine(PatcherEnvironmentProvider.Environment.DataFolderPath, "meshes", "actors", "character", "character assets", "malebody.tri");
                if (!File.Exists(triPath))
                {
                    valid = false;
                    Logger.LogMessage("Male BodySlides were detected but no malebody.tri was found at " + triPath);
                }
            }

            if (oBodySettings.BodySlidesFemale.Where(x => x.AllowRandom && oBodySettings.CurrentlyExistingBodySlides.Contains(x.Label)).Any())
            {
                string triPath = Path.Combine(PatcherEnvironmentProvider.Environment.DataFolderPath, "meshes", "actors", "character", "character assets", "femalebody.tri");
                if (!File.Exists(triPath))
                {
                    valid = false;
                    Logger.LogMessage("Female BodySlides were detected but no femalebody.tri was found at " + triPath);
                }
            }

            if (!valid)
            {
                Logger.LogMessage("Please make sure to check the `Build Morphs` box in BodySlide when generating your BodySlide output");
            }

            return valid;
        }

        public static bool VerifyGeneratedTriFilesForBodyGen(List<AssetPack> assetPacks, BodyGenConfigs bodyGenConfigs)
        {
            bool valid = true;
            BodyGenHasActiveGenderedConfigs(assetPacks, bodyGenConfigs, out bool hasMaleConfigs, out bool hasFemaleConfigs);

            if (hasMaleConfigs)
            {
                string triPath = Path.Combine(PatcherEnvironmentProvider.Environment.DataFolderPath, "meshes", "actors", "character", "character assets", "malebody.tri");
                if (!File.Exists(triPath))
                {
                    valid = false;
                    Logger.LogMessage("Male BodyGen configs were detected but no malebody.tri was found at " + triPath);
                }
            }
            if (hasFemaleConfigs)
            {
                string triPath = Path.Combine(PatcherEnvironmentProvider.Environment.DataFolderPath, "meshes", "actors", "character", "character assets", "femalebody.tri");
                if (!File.Exists(triPath))
                {
                    valid = false;
                    Logger.LogMessage("Female BodyGen configs were detected but no femalebody.tri was found at " + triPath);
                }
            }

            if (!valid)
            {
                Logger.LogMessage("Please make sure to check the `Build Morphs` box in BodySlide when generating your zeroed BodySlide output");
            }

            return valid;
        }

        private static void BodyGenHasActiveGenderedConfigs(List<AssetPack> assetPacks, BodyGenConfigs bodyGenConfigs, out bool hasMaleConfigs, out bool hasFemaleConfigs)
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

        public static bool VerifyRaceMenuIniForBodyGen()
        {
            bool valid = true;

            var iniContents = RaceMenuIniHandler.GetRaceMenuIniContents(out valid, out string iniFileName);

            string message = "";

            bool morphEnabled = RaceMenuIniHandler.GetBodyMorphEnabled(iniContents, out bool morphParsed, out string morphLine);
            if (!morphParsed)
            {
                valid = false;
                message = "Could not parse bEnableBodyMorph in " + iniFileName;
                if (morphLine.Any())
                {
                    message += "( in line " + morphLine + ")";
                }
                Logger.LogMessage(message);
            }
            else if (!morphEnabled)
            {
                valid = false;
                Logger.LogMessage("bEableBodyGen must be enabled in " + iniFileName + " for BodyGen to work. Please fix this from the BodyGen menu.");
            }

            bool bodygenEnabled = RaceMenuIniHandler.GetBodyGenEnabled(iniContents, out bool bodyGenParsed, out string genLine);
            if (!bodyGenParsed)
            {
                valid = false;
                message = "Could not parse bEnableBodyGen in " + iniFileName;
                if (morphLine.Any())
                {
                    message += "( in line " + genLine + ")";
                }
                Logger.LogMessage(message);
            }
            else if (!bodygenEnabled)
            {
                valid = false;
                Logger.LogMessage("bEnableBodyGen must be enabled in " + iniFileName + " for BodyGen to work. Please fix this from the BodyGen menu.");
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
                Logger.LogMessage(message);
            }
            else if (scaleMode == 0)
            {
                valid = false;
                Logger.LogMessage("iScaleMode must not be 0 in " + iniFileName + " for BodyGen to work. Please fix this from the BodyGen menu.");
            }
            else if (scaleMode == 2)
            {
                Logger.LogMessage("Warning: iScaleMode is set to 2 in " + iniFileName + ". This can cause NPC weapons to become supersized when the game is loaded. It is recommended to set this to 1 or 3.");
            }

            return valid;
        }

        public static bool VerifyRaceMenuIniForBodySlide()
        {
            bool valid = true;

            var iniContents = RaceMenuIniHandler.GetRaceMenuIniContents(out valid, out string iniFileName);

            string message = "";

            bool morphEnabled = RaceMenuIniHandler.GetBodyMorphEnabled(iniContents, out bool morphParsed, out string morphLine);
            if (!morphParsed)
            {
                valid = false;
                message = "Could not parse bEnableBodyMorph in " + iniFileName;
                if (morphLine.Any())
                {
                    message += "( in line " + morphLine + ")";
                }
                Logger.LogMessage(message);
            }
            else if (!morphEnabled)
            {
                valid = false;
                Logger.LogMessage("bEableBodyGen must be enabled in " + iniFileName + " for BodyGen to work. Please fix this from the BodyGen Integration menu.");
            }

            bool bodygenEnabled = RaceMenuIniHandler.GetBodyGenEnabled(iniContents, out bool bodyGenParsed, out string genLine);
            if (!bodyGenParsed)
            {
                valid = false;
                message = "Could not parse bEnableBodyGen in " + iniFileName;
                if (morphLine.Any())
                {
                    message += "( in line " + genLine + ")";
                }
                Logger.LogMessage(message);
            }
            else if (bodygenEnabled)
            {
                valid = false;
                Logger.LogMessage("bEnableBodyGen must be disabled in " + iniFileName + " for OBody/AutoBody to work. Please fix this from the OBody Settings menu.");
            }

            return valid;
        }
    }
}
