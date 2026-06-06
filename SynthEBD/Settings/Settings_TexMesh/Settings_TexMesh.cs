using Newtonsoft.Json;

namespace SynthEBD;

/// <summary>How face (head) asset assignments are applied to NPCs: via the EBD Papyrus script at
/// runtime, or by baking edits directly into FaceGen NIFs.</summary>
public enum FacePatchingMode
{
    /// <summary>Apply head assets at runtime via the EBD script.</summary>
    Script,
    /// <summary>Bake head asset edits directly into the FaceGen NIF.</summary>
    NifEdit
}

/// <summary>Settings POCO for the Assets (textures/meshes) axis, persisted as JSON. Holds the
/// face-patching mode, per-category enable flags, custom-skin/face inclusion rules, asset-pack
/// selection, NIF/texture path-trim rules, script-emission options, and SkyPatcher mode.</summary>
public class Settings_TexMesh
{
    /// <summary>Whether head assets are applied via the EBD script or baked into FaceGen NIFs.</summary>
    public FacePatchingMode FacePatchingMode { get; set; } = FacePatchingMode.NifEdit;
    public bool bChangeNPCTextures { get; set; } = true;
    public bool bChangeNPCMeshes { get; set; } = true;
    public bool bChangeNPCHeadParts { get; set; } = true;
    /// <summary>Also patch NPCs whose skin (WNAM armor) is non-vanilla/custom.</summary>
    public bool bApplyToNPCsWithCustomSkins { get; set; } = true;
    /// <summary>Also patch NPCs that use a custom (modded) face/head.</summary>
    public bool bApplyToNPCsWithCustomFaces { get; set; } = true;
    /// <summary>Force the vanilla body mesh path even when an NPC references a custom one.</summary>
    public bool bForceVanillaBodyMeshPath { get; set; } = false;
    /// <summary>Enable asset-replacer sub-records that swap specific existing assets.</summary>
    public bool bEnableAssetReplacers { get; set; } = true;
    public bool bDisplayPopupAlerts { get; set; } = true;
    /// <summary>Write a per-run log of which assets each NPC was assigned.</summary>
    public bool bGenerateAssignmentLog { get; set; } = true;
    public bool bShowPreviewImages { get; set; } = true;
    /// <summary>Whether the asset previewer shows a static image or the live 3D renderer.</summary>
    public PreviewMode PreviewMode { get; set; } = PreviewMode.Image;
    /// <summary>Max edge length (px) for displayed preview images; larger images are downscaled.</summary>
    public int MaxPreviewImageSize { get; set; } = 1024;
    /// <summary>Names of the asset packs the user has enabled for this run.</summary>
    public HashSet<string> SelectedAssetPacks { get; set; } = new();
    /// <summary>Asset pack last shown in the editor, restored on next open.</summary>
    public string LastViewedAssetPack { get; set; }
    /// <summary>Skin armor (WNAM) editor-IDs whose alternate textures are stripped before patching.</summary>
    public HashSet<string> StrippedSkinWNAMs { get; set; } = new()
    {
        "SkinNaked",
        "SkinNakedBeast"
    };
    /// <summary>Emit output in a form compatible with EasyNPC record merging.</summary>
    public bool bEasyNPCCompatibilityMode { get; set; } = true;
    /// <summary>Ship the bundled fixed/patched EBD Papyrus scripts alongside output.</summary>
    public bool bApplyFixedScripts { get; set; } = true;
    /// <summary>Use the script variant compatible with older SKSE versions.</summary>
    public bool bFixedScriptsOldSKSEversion { get; set; } = false;
    /// <summary>Cache resolved records during patching to speed up repeated lookups.</summary>
    public bool bCacheRecords { get; set; } = true;
    /// <summary>Emit output for the legacy EBD runtime rather than the current one.</summary>
    public bool bLegacyEBDMode { get; set; } = false;
    /// <summary>Verbose logging for the new (non-legacy) EBD output mode.</summary>
    public bool bNewEBDModeVerbose { get; set; } = false;
    /// <summary>Use po3's mode for VR compatibility.</summary>
    public bool bPO3ModeForVR { get; set; } = true;
    /// <summary>Path-prefix trim rules per file extension, used to normalize asset paths written to records/scripts.</summary>
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
    /// <summary>Persisted display/evaluation order of asset packs.</summary>
    public List<string> AssetOrder { get; set; } = new();
    /// <summary>Runtime event names (e.g. OStim hooks) that trigger asset re-evaluation in-game.</summary>
    public List<string> TriggerEvents { get; set; } = new() { "OStim_PreStart", "OStim_Start", "OStim_End" };
    /// <summary>Patch worn-armor records (not just skins).</summary>
    public bool bPatchArmors { get; set; } = true;
    /// <summary>Patch alternate textures on skin armor addons.</summary>
    public bool bPatchSkinAltTextures { get; set; } = true;
    /// <summary>Emit asset assignments as SkyPatcher ini directives instead of plugin records.</summary>
    public bool bSkyPatcherModeAssets { get; set; } = false;
}