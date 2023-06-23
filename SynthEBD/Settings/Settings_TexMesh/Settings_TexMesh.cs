using Newtonsoft.Json;

namespace SynthEBD;

public class Settings_TexMesh
{
    public bool bChangeNPCTextures { get; set; } = true;
    public bool bChangeNPCMeshes { get; set; } = true;
    public bool bChangeNPCHeadParts { get; set; } = true;
    public bool bApplyToNPCsWithCustomSkins { get; set; } = true;
    public bool bApplyToNPCsWithCustomFaces { get; set; } = true;
    public bool bForceVanillaBodyMeshPath { get; set; } = false;
    public bool bEnableAssetReplacers { get; set; } = true;
    public bool bDisplayPopupAlerts { get; set; } = true;
    public bool bGenerateAssignmentLog { get; set; } = true;
    public bool bShowPreviewImages { get; set; } = true;
    public int MaxPreviewImageSize { get; set; } = 1024;
    public HashSet<string> SelectedAssetPacks { get; set; } = new();
    public string LastViewedAssetPack { get; set; }
    public HashSet<string> StrippedSkinWNAMs { get; set; } = new()
    {
        "SkinNaked",
        "SkinNakedBeast"
    };
    public bool bEasyNPCCompatibilityMode { get; set; } = true;
    public bool bApplyFixedScripts { get; set; } = true;
    public bool bFixedScriptsOldSKSEversion { get; set; } = false;
    public bool bCacheRecords { get; set; } = true;
    public bool bLegacyEBDMode { get; set; } = true;
    public bool bNewEBDModeVerbose { get; set; } = false;
    public List<TrimPath> TrimPaths { get; set; } = new()
    {
        new TrimPath()
        {
            Extension = "dds",
            PathToTrim = "textures"
        },
        new TrimPath()
        {
            Extension = "nif",
            PathToTrim = "meshes"
        },
        new TrimPath()
        {
            Extension = "tri",
            PathToTrim = "meshes"
        }
    };
    public List<string> AssetOrder { get; set; } = new();
    public List<string> TriggerEvents { get; set; } = new() { "OStim_PreStart", "OStim_Start", "OStim_End" };
    public bool bPatchArmors { get; set; } = true;
    public bool bPatchSkinAltTextures { get; set; } = true;

    public bool bFilterNPCsByArmature { get; set; } = true;
}