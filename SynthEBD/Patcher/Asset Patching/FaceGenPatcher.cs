using System.IO;
using Mutagen.Bethesda.Plugins;
using nifly;

namespace SynthEBD;

/// <summary>
/// Patches NPC FaceGen .nif files with face texture paths derived from asset assignments,
/// when Face Patching Mode is set to Mesh. Instead of applying face textures at runtime via
/// Papyrus scripts, this bakes them directly into the NIF's BSShaderTextureSet.
///
/// Rather than blindly iterating every shape in the NIF, this class structurally locates
/// the head skin shape by verifying:
///
///   1. The shape's parent node is "BSFaceGenNiNodeSkinned"
///   2. The shape's BSDismemberSkinInstance contains a partition with body part
///      SBP_230_HEAD (preferred) or SBP_30_HEAD (fallback)
///
/// This ensures only the actual head mesh gets patched, leaving mouth, hair, brows, etc.
/// untouched.
/// </summary>
public class FaceGenPatcher
{
    // BSDismemberBodyPartType values for head identification.
    private const int SBP_30_HEAD  = 30;
    private const int SBP_230_HEAD = 230;

    // Maps FilePathReplacement.Destination strings to BSShaderTextureSet slot indices.
    private static readonly Dictionary<string, int> HeadTextureDestinationToSlot =
        new(StringComparer.OrdinalIgnoreCase)
        {
            { FilePathDestinationMap.Dest_HeadDiffuse,    0 },  // Diffuse
            { FilePathDestinationMap.Dest_HeadNormal,     1 },  // Normal / Gloss
            { FilePathDestinationMap.Dest_HeadSubsurface, 2 },  // Glow / Skin Tint
            { FilePathDestinationMap.Dest_HeadDetail,     3 },  // Height / Detail
            { FilePathDestinationMap.Dest_HeadSpecular,   7 },  // Backlight Mask / Specular
        };

    private readonly IOutputEnvironmentStateProvider _environmentProvider;
    private readonly PatcherState _patcherState;
    private readonly SynthEBDPaths _paths;
    private readonly BSAHandler _bsaHandler;
    private readonly Logger _logger;

    public FaceGenPatcher(
        IOutputEnvironmentStateProvider environmentProvider,
        PatcherState patcherState,
        SynthEBDPaths paths,
        BSAHandler bsaHandler,
        Logger logger)
    {
        _environmentProvider = environmentProvider;
        _patcherState = patcherState;
        _paths = paths;
        _bsaHandler = bsaHandler;
        _logger = logger;
    }

    // ─── Public entry point ─────────────────────────────────────────────────

    /// <summary>
    /// Entry point called from <see cref="Patcher.RunPatcher"/> after ApplySelectedAssets.
    /// Iterates all asset assignments for a given NPC and bakes any face texture paths
    /// into the NPC's FaceGen .nif file.
    /// </summary>
    public void PatchFaceGenNifs(NPCInfo npcInfo, List<Patcher.SelectedAssetContainer> assetContainers)
    {
        var slotAssignments = CollectFaceTextureAssignments(assetContainers);
        if (slotAssignments.Count == 0)
        {
            return;
        }

        string sourcePath = ResolveFaceGenNifPath(npcInfo, _environmentProvider.DataFolderPath);
        bool foundInBsa = false;
        if (!File.Exists(sourcePath))
        {
            string bsaSourcePath = ResolveFaceGenNifBsaSubPath(npcInfo);
            FormKey formKey = npcInfo.NPC.FormKey;
            string pluginName = formKey.ModKey.FileName;
            string formIdHex = formKey.ID.ToString("X8");
            string extractedDestPath = Path.Combine(_patcherState.ModManagerSettings.TempExtractionFolder, pluginName + "_" + formIdHex + ".nif");
            var contexts = _environmentProvider.LinkCache.ResolveAllContexts(npcInfo.NPC);
            foreach (var context in contexts)
            {
                if (_bsaHandler.TryOpenCorrespondingArchiveReaders(context.ModKey, out var bsaReaders) &&
                    _bsaHandler.ReadersHaveFile(bsaSourcePath, bsaReaders, out var file) &&
                    _bsaHandler.TryExtractFileFromBSA(file, extractedDestPath))
                {
                    sourcePath = extractedDestPath;
                    foundInBsa = true;
                    break;
                }
            }

            if (!foundInBsa)
            {
                _logger.LogReport(
                    "FaceGenPatcher: FaceGen NIF not found at expected path: " + sourcePath,
                    false, npcInfo);
                return;
            }
        }

        string outputPath = ResolveFaceGenNifPath(npcInfo, _paths.OutputDataFolder);
        ApplyTextureSlots(sourcePath, outputPath, slotAssignments, npcInfo);

        if (foundInBsa && File.Exists(sourcePath))
        {
            try
            {
                File.Delete(sourcePath);
            }
            catch (Exception ex)
            {
                _logger.LogMessage($"Warning: Could not clean up temporary file at {sourcePath}. It may be in use. Error: {ex.Message}");
            }
        }
    }

    // ─── Step 1 – Collect face-texture assignments ──────────────────────────

    private static Dictionary<int, string> CollectFaceTextureAssignments(
        List<Patcher.SelectedAssetContainer> assetContainers)
    {
        var result = new Dictionary<int, string>();

        foreach (var container in assetContainers)
        {
            foreach (var path in container.Paths)
            {
                if (HeadTextureDestinationToSlot.TryGetValue(path.Destination, out int slot))
                {
                    if (!string.IsNullOrWhiteSpace(path.Source))
                    {
                        result[slot] = path.Source;
                    }
                }
            }
        }

        return result;
    }

    // ─── Step 2 – Resolve the FaceGen NIF path on disk ──────────────────────

    private static string ResolveFaceGenNifPath(NPCInfo npcInfo, string rootFolder)
    {
        FormKey formKey = npcInfo.NPC.FormKey;
        string pluginName = formKey.ModKey.FileName;
        string formIdHex = formKey.ID.ToString("X8");

        return Path.Combine(
            rootFolder,
            "meshes",
            "actors",
            "character",
            "facegendata",
            "facegeom",
            pluginName,
            formIdHex + ".nif");
    }

    private static string ResolveFaceGenNifBsaSubPath(NPCInfo npcInfo)
    {
        FormKey formKey = npcInfo.NPC.FormKey;
        string pluginName = formKey.ModKey.FileName;
        string formIdHex = formKey.ID.ToString("X8");

        return string.Join("\\", 
            "meshes",
            "actors",
            "character",
            "facegendata",
            "facegeom",
            pluginName,
            formIdHex + ".nif");
    }

    // ─── Step 3 – Structural traversal helpers ──────────────────────────────

    /// <summary>
    /// Checks whether a shape's parent node is "BSFaceGenNiNodeSkinned",
    /// which is the NIF subtree that contains all face geometry.
    /// </summary>
    private static bool IsUnderFaceGenSkinNode(NifFile nif, NiShape shape)
    {
        // C++ nifly: NiNode* NifFile::GetParentNode(NiObject*)
        var parentNode = nif.GetParentNode(shape);
        if (parentNode == null)
        {
            return false;
        }

        // Check the parent node's name.
        // SWIG wraps NiObjectNET.name as a NiStringRef.
        // Try: parentNode.name.get()  OR  parentNode.name.GetString()
        // If neither compiles, check IntelliSense for the NiStringRef accessor.
        string parentName = parentNode.name.get();
        if (parentName == "BSFaceGenNiNodeSkinned")
        {
            return true;
        }

        // Fallback: check the block type via the header in case the NIF stores
        // BSFaceGenNiNodeSkinned as a distinct type rather than a named NiNode.
        uint blockId = nif.GetBlockID(parentNode);
        if (blockId >= 0)
        {
            string typeName = nif.GetHeader().GetBlockTypeStringById(blockId);
            if (typeName == "BSFaceGenNiNodeSkinned")
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Checks whether a shape has a BSDismemberSkinInstance whose partition
    /// list contains SBP_230_HEAD or SBP_30_HEAD.
    ///
    /// Uses the niflycpp.BlockCache pattern (same as Jampi0n/Skyrim-NifPatcher)
    /// to access blocks by reference index.
    /// </summary>
    private static bool HasHeadDismemberPartition(
        NifFile nif,
        NiShape shape,
        out bool isSBP230)
    {
        isSBP230 = false;

        // Get the skin instance reference from the shape.
        // C++ nifly: Ref<NiObject>& NiShape::SkinInstanceRef()
        var skinRef = shape.SkinInstanceRef();
        if (skinRef == null || skinRef.IsEmpty())
        {
            return false;
        }

        // Use BlockCache to retrieve the BSDismemberSkinInstance.
        // This pattern is confirmed working from the Jampi0n/Skyrim-NifPatcher codebase.
        var header = nif.GetHeader();
        var blockCache = new niflycpp.BlockCache(header);

        var rawBlock = blockCache.EditableBlockById<BSDismemberSkinInstance>(skinRef.index);
        if (rawBlock == null)
        {
            return false;
        }

        var dismember = niflycpp.BlockCache.SafeClone<BSDismemberSkinInstance>(rawBlock);
        if (dismember == null)
        {
            return false;
        }

        // Iterate the partition list.
        // C++ nifly: std::vector<PartitionInfo> BSDismemberSkinInstance::partitions
        //   PartitionInfo has: BSDismemberBodyPartType partID
        // SWIG exposes the vector as an iterable property.
        // If .partitions doesn't compile, try .GetPartitions() or check IntelliSense.
        var partitionItems = dismember.partitions.items();
        for (int i = 0; i < (int)dismember.partitions.size(); i++)
        {
            int bodyPart = (int)partitionItems[i].partID;
            if (bodyPart == SBP_230_HEAD)
            {
                isSBP230 = true;
                return true;
            }
            if (bodyPart == SBP_30_HEAD)
            {
                return true;
            }
        }

        return false;
    }

    // ─── Step 4 – Open the NIF, locate head, update texture slots, save ─────

    /// <summary>
    /// Loads the source NIF, locates the head skin shape via structural traversal
    /// (BSFaceGenNiNodeSkinned parent + SBP_230_HEAD dismember partition), applies
    /// texture overrides, and saves to the output path.
    ///
    /// Only the head shape is modified; all other shapes (mouth, hair, brows, etc.)
    /// are left untouched.
    /// </summary>
    private void ApplyTextureSlots(
        string sourcePath,
        string outputPath,
        Dictionary<int, string> slotAssignments,
        NPCInfo npcInfo)
    {
        using var nif = new NifFile();

        int loadResult = nif.Load(sourcePath);
        if (loadResult != 0)
        {
            _logger.LogError(
                "FaceGenPatcher: nifly failed to load NIF (error " + loadResult + "): " + sourcePath);
            return;
        }

        // ── Find the head shape via structural traversal ──

        NiShape headShape = null;
        NiShape sbp30Fallback = null;

        using var shapes = nif.GetShapes();
        foreach (var shape in shapes)
        {
            // Gate 1: shape must be a child of BSFaceGenNiNodeSkinned.
            if (!IsUnderFaceGenSkinNode(nif, shape))
            {
                continue;
            }

            // Gate 2: shape must have a head dismember partition.
            if (!HasHeadDismemberPartition(nif, shape, out bool is230))
            {
                continue;
            }

            if (is230)
            {
                headShape = shape;
                break; // SBP_230_HEAD — exact match, stop searching.
            }

            sbp30Fallback ??= shape; // SBP_30_HEAD — keep as fallback.
        }

        headShape ??= sbp30Fallback;

        if (headShape == null)
        {
            _logger.LogReport(
                "FaceGenPatcher: No head shape (SBP_230/30_HEAD under BSFaceGenNiNodeSkinned) found in: "
                + sourcePath,
                false, npcInfo);
            return;
        }

        // Gate 3: head shape must have a shader for texture assignment.
        var shader = nif.GetShader(headShape);
        if (shader == null)
        {
            _logger.LogReport(
                "FaceGenPatcher: Head shape has no shader in: " + sourcePath,
                false, npcInfo);
            return;
        }

        // ── Apply texture slot overrides ──

        bool anyChange = false;

        foreach (var (slot, newTexturePath) in slotAssignments)
        {
            nif.SetTextureSlot(headShape, newTexturePath, (uint)slot);

            _logger.LogReport(
                "FaceGenPatcher: Slot " + slot + " -> \"" + newTexturePath + "\"",
                false, npcInfo);

            anyChange = true;
        }

        if (!anyChange)
        {
            return;
        }

        // Ensure the output directory exists.
        string? outputDir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(outputDir))
        {
            Directory.CreateDirectory(outputDir);
        }

        int saveResult = nif.Save(outputPath);
        if (saveResult != 0)
        {
            _logger.LogError(
                "FaceGenPatcher: nifly failed to save NIF (error " + saveResult + "): " + outputPath);
        }
    }
}