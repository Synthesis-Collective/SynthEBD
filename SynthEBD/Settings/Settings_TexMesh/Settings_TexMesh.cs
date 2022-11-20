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
    public bool bDisplayPopupAlerts { get; set; } = true;
    public bool bGenerateAssignmentLog { get; set; } = true;
    public bool bShowPreviewImages { get; set; } = true;
    public int MaxPreviewImageSize { get; set; } = 1024;
    public HashSet<string> SelectedAssetPacks { get; set; } = new();
    public string LastViewedAssetPack { get; set; }
    public bool bEasyNPCCompatibilityMode { get; set; } = true;
    public bool bApplyFixedScripts { get; set; } = true;
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
}