using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using BespokeFusion;
using System.Windows.Media;

namespace SynthEBD
{
    public class MiscValidation
    {
        public static bool VerifyEBDInstalled()
        {
            bool verified = true;

            string helperScriptPath = Path.Combine(GameEnvironmentProvider.MyEnvironment.DataFolderPath, "Scripts", "EBDHelperScript.pex");
            if (!File.Exists(helperScriptPath))
            {
                Logger.LogMessage("Could not find EBDHelperScript.pex from EveryBody's Different Redone SSE at " + helperScriptPath);
                verified = false;
            }

            string globalScriptPath = Path.Combine(GameEnvironmentProvider.MyEnvironment.DataFolderPath, "Scripts", "EBDGlobalFuncs.pex");
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

            string dllPath = Path.Combine(GameEnvironmentProvider.MyEnvironment.DataFolderPath, "SKSE", "Plugins", "skee64.dll");
            if (!File.Exists(dllPath))
            {
                Logger.LogMessage("Could not find skee64.dll from RaceMenu at " + dllPath);
                verified = false;
            }

            string iniPath = Path.Combine(GameEnvironmentProvider.MyEnvironment.DataFolderPath, "SKSE", "Plugins", "skee64.ini");
            if (!File.Exists(iniPath))
            {
                Logger.LogMessage("Could not find skee64.dll from RaceMenu at " + iniPath);
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

            string scriptPath = Path.Combine(GameEnvironmentProvider.MyEnvironment.DataFolderPath, "Scripts", "OBodyNative.pex");
            if (!File.Exists(scriptPath))
            {
                Logger.LogMessage("Could not find OBodyNative.pex from OBody at " + scriptPath);
                verified = false;
            }

            string dllPath = Path.Combine(GameEnvironmentProvider.MyEnvironment.DataFolderPath, "SKSE", "Plugins", "OBody.dll");
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

            string scriptPath = Path.Combine(GameEnvironmentProvider.MyEnvironment.DataFolderPath, "Scripts", "autoBodyUtils.pex");
            if (!File.Exists(scriptPath))
            {
                Logger.LogMessage("Could not find autoBodyUtils.pex from AutoBody at " + scriptPath);
                verified = false;
            }

            string dllPath = Path.Combine(GameEnvironmentProvider.MyEnvironment.DataFolderPath, "SKSE", "Plugins", "autoBodyAE.dll");
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
                return ShowMorphDescriptorConfirmationBox(String.Join(Environment.NewLine, message));
            }
            else
            {
                return true;
            }
        }

        public static bool VerifyBodySlideAnnotations(Settings_OBody obodySettings)
        {
            List<string> bsMissingDescriptors = new List<string>();
            GetMissingBodySlideAnnotations(obodySettings.BodySlidesMale, bsMissingDescriptors);
            GetMissingBodySlideAnnotations(obodySettings.BodySlidesFemale, bsMissingDescriptors);

            if (bsMissingDescriptors.Any())
            {
                bsMissingDescriptors.Insert(0, "The following active BodySlides have not been annotated with any body shape descriptors:");
                bsMissingDescriptors.Add("Bodyslides that lack descriptors can be misassigned by the texture/body shape assigner. Do you want to continue patching?");
                return ShowMorphDescriptorConfirmationBox(String.Join(Environment.NewLine, bsMissingDescriptors));
            }
            else
            {
                return true;
            }
        }

        public static void GetMissingBodySlideAnnotations(List<BodySlideSetting> bodySlides, List<string> bsMissingDescriptors)
        {
            foreach (var bs in bodySlides)
            {
                if (bs.AllowRandom && !bs.BodyShapeDescriptors.Any())
                {
                    bsMissingDescriptors.Add(bs.Label);
                }
            }
        }

        private static bool ShowMorphDescriptorConfirmationBox(string message)
        {
            var box = new CustomMaterialMessageBox()
            {
                TxtMessage = { Text = message, Foreground = Brushes.White },
                TxtTitle = { Text = "Missing Descriptors", Foreground = Brushes.White },
                BtnOk = { Content = "Yes" },
                BtnCancel = { Content = "No" },
                MainContentControl = { Background = Brushes.Black },
                TitleBackgroundPanel = { Background = Brushes.Black },
                BorderBrush = Brushes.Silver
            };
            box.Show();

            return box.Result == System.Windows.MessageBoxResult.OK;
        }

        public static bool VerifyGeneratedTriFilesForOBody(Settings_OBody oBodySettings)
        {
            bool valid = true;
            if (oBodySettings.BodySlidesMale.Where(x => x.AllowRandom).Any())
            {
                string triPath = Path.Combine(GameEnvironmentProvider.MyEnvironment.DataFolderPath, "meshes", "actors", "character", "character assets", "malebody.tri");
                if (!File.Exists(triPath))
                {
                    valid = false;
                    Logger.LogMessage("Male BodySlides were detected but no malebody.tri was found at " + triPath);
                }
            }

            if (oBodySettings.BodySlidesFemale.Where(x => x.AllowRandom).Any())
            {
                string triPath = Path.Combine(GameEnvironmentProvider.MyEnvironment.DataFolderPath, "meshes", "actors", "character", "character assets", "femalebody.tri");
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
                string triPath = Path.Combine(GameEnvironmentProvider.MyEnvironment.DataFolderPath, "meshes", "actors", "character", "character assets", "malebody.tri");
                if (!File.Exists(triPath))
                {
                    valid = false;
                    Logger.LogMessage("Male BodyGen configs were detected but no malebody.tri was found at " + triPath);
                }
            }
            if (hasFemaleConfigs)
            {
                string triPath = Path.Combine(GameEnvironmentProvider.MyEnvironment.DataFolderPath, "meshes", "actors", "character", "character assets", "femalebody.tri");
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
    }
}
