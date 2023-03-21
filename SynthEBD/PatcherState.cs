using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Skyrim;

namespace SynthEBD;

public class PatcherState
{
    // Version
    public static string Version = "0.9.9.2";

    // Settings
    public Settings_General GeneralSettings { get; set; }
    public Settings_TexMesh TexMeshSettings { get; set; }
    public Settings_BodyGen BodyGenSettings { get; set; }
    public Settings_OBody OBodySettings { get; set; }
    public Settings_Height HeightSettings { get; set; }
    public Settings_Headparts HeadPartSettings { get; set; }
    public Settings_ModManager ModManagerSettings { get; set; }

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
        }
        if (TexMeshSettings == null)
        {
            sb.AppendLine("Asset Settings: Null");
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
                        sb.AppendLine("MO2 Path exists");
                    }
                    else
                    {
                        sb.AppendLine("MO2 Path does not exist");
                    }
                    if (System.IO.Directory.Exists(ModManagerSettings.MO2Settings.ModFolderPath))
                    {
                        sb.AppendLine("Mod Folder exists");
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
                sb.AppendLine("\t" + primary.GroupName);
            }
            sb.AppendLine(("MixIn Config Files"));
            foreach (var mixin in AssetPacks.Where(x => x.ConfigType == AssetPackType.MixIn))
            {
                sb.AppendLine("\t" + mixin.GroupName);
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
            sb.AppendLine("Male BodySlides: " + OBodySettings.BodySlidesMale.Count + " (" + OBodySettings.BodySlidesMale.Where(x => x.BodyShapeDescriptors.Any()).Count() + ") annotated");
        }

        if (OBodySettings != null && OBodySettings.BodySlidesFemale == null)
        {
            sb.AppendLine("Female BodySlides: Null");
        }
        else if (OBodySettings != null)
        {
            sb.AppendLine("Female BodySlides: " + OBodySettings.BodySlidesFemale.Count + " (" + OBodySettings.BodySlidesFemale.Where(x => x.BodyShapeDescriptors.Any()).Count() + ") annotated");
        }

        if (HeightConfigs == null)
        {
            sb.AppendLine("Height Configs: Null");
        }
        else
        {
            sb.AppendLine("Height Configs: " + HeightConfigs.Count);
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