using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Skyrim;
using System.Windows.Forms;

namespace SynthEBD;

public class PatcherState
{
    // Version
    public static string Version = "1.0.5.2";
    // Settings
    public Settings_General GeneralSettings { get; set; }
    public Settings_TexMesh TexMeshSettings { get; set; }
    public Settings_BodyGen BodyGenSettings { get; set; }
    public Settings_OBody OBodySettings { get; set; }
    public Settings_Height HeightSettings { get; set; }
    public Settings_Headparts HeadPartSettings { get; set; }
    public Settings_ModManager ModManagerSettings { get; set; }

    //Misc
    public UpdateLog UpdateLog { get; set; }

    // Plugins

    public List<AssetPack> AssetPacks { get; set; }
    public List<HeightConfig> HeightConfigs { get; set; }
    public BodyGenConfigs BodyGenConfigs { get; set; }
    public Dictionary<string, NPCAssignment> Consistency { get; set; }
    public ILinkCache<ISkyrimMod, ISkyrimModGetter> RecordTemplateLinkCache { get; set; }
    public BlockList BlockList { get; set; }
    public List<SkyrimMod> RecordTemplatePlugins { get; set; }
    public HashSet<NPCAssignment> SpecificNPCAssignments { get; set; }

    public string GetStateLogStr()
    {
        System.Text.StringBuilder sb = new();

        if (GeneralSettings == null)
        {
            sb.AppendLine("General Settings: Null");
        }
        else
        {
            sb.AppendLine("Main Settings:");
            sb.AppendLine("Validation Disabled: " + GeneralSettings.bDisableValidation);
            sb.AppendLine("Apply Assets: " + GeneralSettings.bChangeMeshesOrTextures);
            sb.AppendLine("Apply BodyShapes via: " + GeneralSettings.BodySelectionMode);
            if (GeneralSettings.BodySelectionMode == BodyShapeSelectionMode.BodySlide)
            {
                sb.AppendLine("Apply BodySlides via: " + GeneralSettings.BSSelectionMode);
            }
            sb.AppendLine("Apply Height: " + GeneralSettings.bChangeHeight);
            sb.AppendLine("Apply Head Parts: " + GeneralSettings.bChangeHeadParts);
            sb.AppendLine("Use Consistency: " + GeneralSettings.bEnableConsistency);
            sb.AppendLine("Output Folder: " + GeneralSettings.OutputDataFolder);
            sb.AppendLine("Output File Name: " + GeneralSettings.PatchFileName);
        }
        if (TexMeshSettings == null)
        {
            sb.AppendLine("Asset Settings: Null");
        }
        else
        {
            if (GeneralSettings != null && TexMeshSettings != null &&
                (TexMeshSettings.bForceVanillaBodyMeshPath || GeneralSettings.bChangeMeshesOrTextures))
            {
                sb.AppendLine("SkyPatcher Mode for Skins: " + TexMeshSettings.bPureScriptMode);
            }
            
            if (GeneralSettings != null && GeneralSettings.bChangeMeshesOrTextures)
            {
                sb.AppendLine("Fix EBD's Global Script: " + TexMeshSettings.bApplyFixedScripts);
                
                if (TexMeshSettings.bApplyFixedScripts)
                {
                    string fixedScriptVer = string.Empty;
                    switch(TexMeshSettings.bFixedScriptsOldSKSEversion)
                    {
                        case false: fixedScriptVer = "1.5.9.7 or Higher"; break;
                        case true: fixedScriptVer = "<1.5.9.7"; break;
                    }
                    sb.AppendLine("Using Fixed Global Script For " + fixedScriptVer);
                }

                switch (TexMeshSettings.bLegacyEBDMode)
                {
                    case true: sb.AppendLine("Using original EBD face texture script"); break;
                    case false: sb.AppendLine("Using SynthEBD's updated face texture script"); break;
                }

                sb.AppendLine("VR Only: Use PO3 script version: " + TexMeshSettings.bPO3ModeForVR);
            }
        }
        if (BodyGenSettings == null)
        {
            sb.AppendLine("BodyGen Settings: Null");
        }
        if (OBodySettings == null)
        {
            sb.AppendLine("BodySlide Settings: Null");
        }
        if (HeightSettings == null)
        {
            sb.AppendLine("Height Settings: Null");
        }
        if (ModManagerSettings == null)
        {
            sb.AppendLine("Mod Manager Settings: Null");
        }
        else
        {
            sb.AppendLine("Mod Manager Type: " + ModManagerSettings.ModManagerType);
            if (ModManagerSettings.ModManagerType == ModManager.ModOrganizer2)
            {
                if (ModManagerSettings.MO2Settings == null)
                {
                    sb.AppendLine("MO2 Settings: Null");
                }
                else
                {
                    if (System.IO.File.Exists(ModManagerSettings.MO2Settings.ExecutablePath))
                    {
                        sb.AppendLine("MO2 Path exists: " + ModManagerSettings.MO2Settings.ExecutablePath);
                    }
                    else
                    {
                        sb.AppendLine("MO2 Path does not exist");
                    }
                    if (System.IO.Directory.Exists(ModManagerSettings.MO2Settings.ModFolderPath))
                    {
                        sb.AppendLine("Mod Folder exists: " + ModManagerSettings.MO2Settings.ModFolderPath);
                    }
                    else
                    {
                        sb.AppendLine("Mod Folder does not exist");
                    }
                }
            }
            else if (ModManagerSettings.ModManagerType == ModManager.Vortex)
            {
                if (ModManagerSettings.VortexSettings == null)
                {
                    sb.AppendLine("Vortex Settings: Null");
                }
                else
                {
                    if (System.IO.Directory.Exists(ModManagerSettings.VortexSettings.StagingFolderPath))
                    {
                        sb.AppendLine("Staging Folder exists");
                    }
                    else
                    {
                        sb.AppendLine("Staging folder does not exist");
                    }
                }
            }
        }

        if (AssetPacks != null)
        {
            sb.AppendLine("Primary Config Files");
            foreach (var primary in AssetPacks.Where(x => x.ConfigType == AssetPackType.Primary))
            {
                string symbol = string.Empty;
                if (TexMeshSettings.SelectedAssetPacks.Contains(primary.GroupName))
                {
                    symbol = "(+) ";
                }
                else
                {
                    symbol = "(-) ";
                }
                sb.AppendLine("\t" + symbol + primary.GroupName);
            }
            sb.AppendLine(("MixIn Config Files"));
            foreach (var mixin in AssetPacks.Where(x => x.ConfigType == AssetPackType.MixIn))
            {
                string symbol = string.Empty;
                if (TexMeshSettings.SelectedAssetPacks.Contains(mixin.GroupName))
                {
                    symbol = "(+) ";
                }
                else
                {
                    symbol = "(-) ";
                }
                sb.AppendLine("\t" + symbol + mixin.GroupName);
            }
        }
        else
        {
            sb.AppendLine("AssetPacks: Null");
        }

        if (BodyGenConfigs != null)
        {
            if (BodyGenConfigs.Male != null)
            {
                sb.AppendLine("Male BodyGen Configs");
                foreach (var bgc in BodyGenConfigs.Male)
                {
                    sb.AppendLine("\t" + bgc.Label);
                }
            }
            else
            {
                sb.AppendLine("Male BodyGen Configs: Null");
            }

            if (BodyGenConfigs.Female != null)
            {
                sb.AppendLine("Female BodyGen Configs");
                foreach (var bgc in BodyGenConfigs.Female)
                {
                    sb.AppendLine("\t" + bgc.Label);
                }
            }
            else
            {
                sb.AppendLine("Female BodyGen Configs: Null");
            }
        }
        else
        {
            sb.AppendLine("BodyGen Configs: Null");
        }

        if (OBodySettings != null && OBodySettings.BodySlidesMale == null)
        {
            sb.AppendLine("Male BodySlides: Null");
        }
        else if (OBodySettings != null)
        {
            sb.AppendLine("Male BodySlides: " + OBodySettings.BodySlidesMale.Count + " (" + OBodySettings.BodySlidesMale.Where(x => x.BodyShapeDescriptors.Any()).Count() + " annotated)");
        }

        if (OBodySettings != null && OBodySettings.BodySlidesFemale == null)
        {
            sb.AppendLine("Female BodySlides: Null");
        }
        else if (OBodySettings != null)
        {
            sb.AppendLine("Female BodySlides: " + OBodySettings.BodySlidesFemale.Count + " (" + OBodySettings.BodySlidesFemale.Where(x => x.BodyShapeDescriptors.Any()).Count() + " annotated)");
        }

        if (HeightConfigs == null)
        {
            sb.AppendLine("Height Configs: Null");
        }
        else
        {
            sb.AppendLine("Height Configs: " + HeightConfigs.Count);
        }

        if (GeneralSettings != null && GeneralSettings.bChangeHeight && HeightSettings != null)
        {
            sb.AppendLine("SkyPatcher Mode for Height: " + HeightSettings.bApplyWithoutOverride);
        }

        if (HeadPartSettings == null)
        {
            sb.AppendLine("HeadPart Settings: Null");
        }
        else
        {
            foreach (var entry in HeadPartSettings.Types)
            {
                if (entry.Value == null)
                {
                    sb.AppendLine(entry.Key + " HeadParts: Null");
                }
                else if (entry.Value.HeadParts == null)
                {
                    sb.AppendLine(entry.Key + " HeadParts List: Null");
                }
                else
                {
                    sb.AppendLine(entry.Key + ": " + entry.Value.HeadParts.Count + " HeadParts");
                }
            }
        }

        return sb.ToString();
    }
}