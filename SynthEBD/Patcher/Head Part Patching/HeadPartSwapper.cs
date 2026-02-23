using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Skyrim;
using nifly;

namespace SynthEBD;

/// <summary>
/// Applies head part assignments directly into FaceGen NIF files by cloning NiTriShapes
/// from head part model NIFs into the NPC's FaceGen mesh under BSFaceGenNiNodeSkinned.
///
/// This replaces the script + JSON approach (HeadPartWriter) with a baked-mesh approach,
/// producing modified FaceGen .nifs as output — analogous to how FaceGenPatcher bakes
/// face textures into the NIF.
///
/// ─── Approach ──────────────────────────────────────────────────────────────
///
///   For each assigned head part:
///     1. Resolve the head part's model NIF path from IHeadPartGetter.Model.File
///     2. Open the model NIF; collect all NiTriShapes and their partition body-part IDs
///     3. In the FaceGen NIF, find BSFaceGenNiNodeSkinned and:
///        a. For SINGULAR types (Hair, Eyes, Eyebrows, Face*, FacialHair):
///           remove existing shapes whose partitions overlap with the new head part's
///        b. For ADDITIVE types (Scars, Misc):
///           remove only exact matches (same head part being re-applied), not all of that type
///     4. Clone new shapes into BSFaceGenNiNodeSkinned via nifly's CloneShape
///     5. Recurse into IHeadPartGetter.ExtraParts (e.g., hairline parts)
///
///   * Face type is EXCLUDED by default — swapping the face mesh destroys NPC-specific
///     FaceGen morphs. Face textures should be handled by FaceGenPatcher instead.
///
/// ─── Known Limitations ─────────────────────────────────────────────────────
///
///   - FaceGen morph loss: Cloned shapes use default head part geometry without
///     NPC-specific vertex morphs. Acceptable for hair/eyes/brows/scars; the runtime
///     script swap has the same limitation.
///
///   - Bone remapping: nifly's CloneShape should handle bone index translation between
///     source and destination NIFs, but edge cases may exist with modded skeletons.
/// </summary>
public class HeadPartSwapper
{
    // ─── Body Part ID Constants ─────────────────────────────────────────────
    // Used to identify which shapes belong to which head part type in the
    // BSDismemberSkinInstance partition list.

    private const int SBP_30_HEAD       = 30;
    private const int SBP_31_HAIR       = 31;
    private const int SBP_32_BODY       = 32;
    private const int SBP_41_LONGHAIR   = 41;
    private const int SBP_42_CIRCLET    = 42;
    private const int SBP_43_EARS       = 43;
    private const int SBP_44_MOUTH      = 44;  // DRAGON_BLOODHEAD_OR_MOD_MOUTH
    private const int SBP_130_HEAD      = 130;
    private const int SBP_131_HAIR      = 131;
    private const int SBP_141_LONGHAIR  = 141;
    private const int SBP_142_CIRCLET   = 142;
    private const int SBP_143_EARS      = 143;
    private const int SBP_230_HEAD      = 230;

    /// <summary>
    /// Maps each HeadPart.TypeEnum to the set of body-part partition IDs that type
    /// is expected to occupy. Used to identify which existing FaceGen shapes should
    /// be removed when doing a SINGULAR replacement.
    ///
    /// NOTE: These mappings reflect vanilla Skyrim conventions. Modded head parts
    /// may use non-standard partition IDs — the code falls back to reading the
    /// actual partition IDs from the source head part NIF to handle this.
    /// </summary>
    private static readonly Dictionary<HeadPart.TypeEnum, HashSet<int>> TypeToExpectedPartitions = new()
    {
        { HeadPart.TypeEnum.Hair,       new HashSet<int> { SBP_31_HAIR, SBP_131_HAIR, SBP_41_LONGHAIR, SBP_141_LONGHAIR } },
        { HeadPart.TypeEnum.Eyes,       new HashSet<int> { SBP_43_EARS, SBP_143_EARS } }, // Eyes often share partition with ears
        { HeadPart.TypeEnum.Eyebrows,   new HashSet<int> { SBP_42_CIRCLET, SBP_142_CIRCLET } },
        { HeadPart.TypeEnum.Face,       new HashSet<int> { SBP_30_HEAD, SBP_130_HEAD, SBP_230_HEAD } },
        { HeadPart.TypeEnum.FacialHair, new HashSet<int> { SBP_44_MOUTH } },
        { HeadPart.TypeEnum.Scars,      new HashSet<int> { } },  // Additive — no automatic removal by partition
        { HeadPart.TypeEnum.Misc,       new HashSet<int> { } },  // Additive — no automatic removal by partition
    };

    /// <summary>
    /// Types where only one head part should exist at a time. Existing shapes with
    /// overlapping partitions are removed before the new head part is applied.
    /// </summary>
    private static readonly HashSet<HeadPart.TypeEnum> SingularTypes = new()
    {
        HeadPart.TypeEnum.Hair,
        HeadPart.TypeEnum.Eyes,
        HeadPart.TypeEnum.Eyebrows,
        HeadPart.TypeEnum.Face,
        HeadPart.TypeEnum.FacialHair,
    };

    /// <summary>
    /// Types excluded from NIF swapping because the geometry is NPC-specific
    /// (FaceGen morphs). These should be handled via texture-only patching
    /// (FaceGenPatcher) or left untouched.
    /// </summary>
    private static readonly HashSet<HeadPart.TypeEnum> ExcludedTypes = new()
    {
        HeadPart.TypeEnum.Face,
    };

    private readonly IOutputEnvironmentStateProvider _environmentProvider;
    private readonly PatcherState _patcherState;
    private readonly SynthEBDPaths _paths;
    private readonly BSAHandler _bsaHandler;
    private readonly Logger _logger;

    public HeadPartSwapper(
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

    // ═══════════════════════════════════════════════════════════════════════════
    //  PUBLIC ENTRY POINT
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Applies head part assignments to an NPC's FaceGen NIF. Called after
    /// HeadPartSelector.AssignHeadParts has determined which head parts to use.
    ///
    /// Opens the FaceGen NIF once, applies all head part swaps, and saves.
    /// </summary>
    /// <param name="npcInfo">The NPC being patched.</param>
    /// <param name="headPartAssignments">
    ///   Map of type → FormKey from HeadPartSelector.AssignHeadParts.
    /// </param>
    public void ApplyHeadPartsToFaceGen(
        NPCInfo npcInfo,
        Dictionary<HeadPart.TypeEnum, FormKey> headPartAssignments)
    {
        if (headPartAssignments.Count == 0) return;

        // Filter out excluded types (Face) and unresolvable assignments.
        var validAssignments = ResolveHeadPartAssignments(headPartAssignments);
        if (validAssignments.Count == 0) return;

        // ── Resolve and load the FaceGen NIF ──

        string faceGenSourcePath = ResolveFaceGenNifPath(npcInfo, _environmentProvider.DataFolderPath);
        bool extractedFromBsa = false;

        if (!File.Exists(faceGenSourcePath))
        {
            faceGenSourcePath = TryExtractFaceGenFromBsa(npcInfo, out extractedFromBsa);
            if (faceGenSourcePath == null)
            {
                _logger.LogReport(
                    "HeadPartSwapper: FaceGen NIF not found for NPC. Head parts cannot be baked.",
                    false, npcInfo);
                return;
            }
        }

        string faceGenOutputPath = ResolveFaceGenNifPath(npcInfo, _paths.OutputDataFolder);

        // ── Open the FaceGen NIF and apply all head part swaps ──

        bool anyChanges = false;

        using (var faceGenNif = new NifFile())
        {
            int loadResult = faceGenNif.Load(faceGenSourcePath);
            if (loadResult != 0)
            {
                _logger.LogError(
                    "HeadPartSwapper: Failed to load FaceGen NIF (error " + loadResult + "): " + faceGenSourcePath);
                return;
            }

            // Find the BSFaceGenNiNodeSkinned parent node.
            NiNode faceGenSkinNode = FindFaceGenSkinNode(faceGenNif);
            if (faceGenSkinNode == null)
            {
                _logger.LogReport(
                    "HeadPartSwapper: No BSFaceGenNiNodeSkinned node found in: " + faceGenSourcePath,
                    false, npcInfo);
                return;
            }

            foreach (var (type, headPartGetter) in validAssignments)
            {
                bool changed = SwapHeadPartType(faceGenNif, faceGenSkinNode, headPartGetter, type, npcInfo);
                anyChanges |= changed;
            }

            if (anyChanges)
            {
                string outputDir = Path.GetDirectoryName(faceGenOutputPath);
                if (!string.IsNullOrEmpty(outputDir))
                {
                    Directory.CreateDirectory(outputDir);
                }

                int saveResult = faceGenNif.Save(faceGenOutputPath);
                if (saveResult != 0)
                {
                    _logger.LogError(
                        "HeadPartSwapper: Failed to save FaceGen NIF (error " + saveResult + "): " + faceGenOutputPath);
                }
                else
                {
                    _logger.LogReport(
                        "HeadPartSwapper: Saved modified FaceGen NIF to: " + faceGenOutputPath,
                        false, npcInfo);
                }
            }
        }

        // Clean up temporary BSA-extracted file.
        if (extractedFromBsa && File.Exists(faceGenSourcePath))
        {
            try { File.Delete(faceGenSourcePath); }
            catch (Exception ex)
            {
                _logger.LogMessage("Warning: Could not clean up temp file: " + faceGenSourcePath + " — " + ex.Message);
            }
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  STEP 1 — Resolve head part assignments to IHeadPartGetter records
    // ═══════════════════════════════════════════════════════════════════════════

    private List<(HeadPart.TypeEnum Type, IHeadPartGetter HeadPart)> ResolveHeadPartAssignments(
        Dictionary<HeadPart.TypeEnum, FormKey> assignments)
    {
        var result = new List<(HeadPart.TypeEnum, IHeadPartGetter)>();

        foreach (var (type, formKey) in assignments)
        {
            if (ExcludedTypes.Contains(type))
            {
                _logger.LogReport(
                    "HeadPartSwapper: Skipping " + type + " — excluded from NIF swapping (use FaceGenPatcher for face textures).",
                    false, null);
                continue;
            }

            if (formKey.IsNull) continue;

            if (!_environmentProvider.LinkCache.TryResolve<IHeadPartGetter>(formKey, out var headPartGetter))
            {
                _logger.LogMessage(
                    "HeadPartSwapper: Could not resolve head part " + formKey + " for type " + type);
                continue;
            }

            if (headPartGetter.Model?.File == null || string.IsNullOrWhiteSpace(headPartGetter.Model.File))
            {
                _logger.LogMessage(
                    "HeadPartSwapper: Head part " + formKey + " (" + type + ") has no model NIF path.");
                continue;
            }

            result.Add((type, headPartGetter));
        }

        return result;
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  STEP 2 — Swap a single head part type in the FaceGen NIF
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Processes a single head part assignment: opens the source model NIF, removes
    /// conflicting shapes from the FaceGen NIF, and clones new shapes in.
    /// Also recurses into ExtraParts.
    /// </summary>
    private bool SwapHeadPartType(
        NifFile faceGenNif,
        NiNode faceGenSkinNode,
        IHeadPartGetter headPartGetter,
        HeadPart.TypeEnum type,
        NPCInfo npcInfo)
    {
        bool anyChanges = false;

        // Process the main head part model.
        anyChanges |= SwapSingleHeadPartModel(faceGenNif, faceGenSkinNode, headPartGetter, type, npcInfo);

        // Recurse into ExtraParts (e.g., hairline parts referenced by a hair head part).
        if (headPartGetter.ExtraParts != null)
        {
            foreach (var extraPartLink in headPartGetter.ExtraParts)
            {
                if (_environmentProvider.LinkCache.TryResolve(extraPartLink, out var extraPartGetter))
                {
                    if (extraPartGetter.Model?.File == null || string.IsNullOrWhiteSpace(extraPartGetter.Model.File))
                    {
                        continue;
                    }

                    _logger.LogReport(
                        "HeadPartSwapper: Processing ExtraPart " +
                        (extraPartGetter.EditorID ?? extraPartGetter.FormKey.ToString()) +
                        " for " + type,
                        false, npcInfo);

                    // ExtraParts share the parent type's singular/additive behavior.
                    anyChanges |= SwapSingleHeadPartModel(faceGenNif, faceGenSkinNode, extraPartGetter, type, npcInfo);
                }
            }
        }

        return anyChanges;
    }

    /// <summary>
    /// Opens a single head part model NIF, removes conflicting shapes from FaceGen,
    /// and clones the model's shapes into BSFaceGenNiNodeSkinned.
    /// </summary>
    private bool SwapSingleHeadPartModel(
        NifFile faceGenNif,
        NiNode faceGenSkinNode,
        IHeadPartGetter headPartGetter,
        HeadPart.TypeEnum type,
        NPCInfo npcInfo)
    {
        // ── Locate the head part model NIF on disk ──

        string modelRelPath = headPartGetter.Model.File.DataRelativePath.Path;
        string modelAbsPath = ResolveModelNifPath(modelRelPath);
        bool extractedModel = false;

        if (!File.Exists(modelAbsPath))
        {
            modelAbsPath = TryExtractModelFromBsa(modelRelPath, headPartGetter, out extractedModel);
            if (modelAbsPath == null)
            {
                _logger.LogReport(
                    "HeadPartSwapper: Model NIF not found: " + modelRelPath +
                    " for head part " + (headPartGetter.EditorID ?? headPartGetter.FormKey.ToString()),
                    true, npcInfo);
                return false;
            }
        }

        bool anyChanges = false;

        try
        {
            using var modelNif = new NifFile();
            int loadResult = modelNif.Load(modelAbsPath);
            if (loadResult != 0)
            {
                _logger.LogError(
                    "HeadPartSwapper: Failed to load model NIF (error " + loadResult + "): " + modelAbsPath);
                return false;
            }

            // ── Collect shapes from the model NIF and their partition IDs ──

            var modelShapeInfos = CollectModelShapes(modelNif);
            if (modelShapeInfos.Count == 0)
            {
                _logger.LogReport(
                    "HeadPartSwapper: No NiTriShapes found in model: " + modelRelPath,
                    false, npcInfo);
                return false;
            }

            // Gather all partition body-part IDs used by the new head part's shapes.
            var incomingPartitions = new HashSet<int>();
            foreach (var info in modelShapeInfos)
            {
                incomingPartitions.UnionWith(info.PartitionBodyParts);
            }

            // ── Remove conflicting shapes from FaceGen (for singular types) ──

            if (SingularTypes.Contains(type))
            {
                // Build the set of partition IDs to match against: use both the
                // expected partitions for this type AND the actual partitions from
                // the incoming head part (to handle non-standard modded partitions).
                var removePartitions = new HashSet<int>(incomingPartitions);
                if (TypeToExpectedPartitions.TryGetValue(type, out var expected))
                {
                    removePartitions.UnionWith(expected);
                }

                RemoveShapesByPartition(faceGenNif, faceGenSkinNode, removePartitions, npcInfo);
            }

            // ── Clone shapes from model NIF into FaceGen NIF ──

            foreach (var shapeInfo in modelShapeInfos)
            {
                // Clone the shape from the model NIF into the FaceGen NIF.
                // CloneShape handles copying geometry, shader, skin weights, and
                // remaps bone indices between source and destination NIFs.
                NiShape clonedShape = faceGenNif.CloneShape(shapeInfo.Shape, shapeInfo.Name, modelNif);

                if (clonedShape == null)
                {
                    _logger.LogReport(
                        "HeadPartSwapper: CloneShape returned null for shape \"" + shapeInfo.Name +
                        "\" from model: " + modelRelPath,
                        true, npcInfo);
                    continue;
                }

                // CloneShape adds the new shape to the root node's children by default.
                // Reparent it under BSFaceGenNiNodeSkinned so the game treats it as
                // part of the FaceGen subtree.
                faceGenNif.SetParentNode(clonedShape, faceGenSkinNode);

                _logger.LogReport(
                    "HeadPartSwapper: Cloned shape \"" + shapeInfo.Name + "\" (" + type + ") into FaceGen NIF.",
                    false, npcInfo);

                anyChanges = true;
            }
        }
        finally
        {
            // Clean up BSA-extracted model file.
            if (extractedModel && File.Exists(modelAbsPath))
            {
                try { File.Delete(modelAbsPath); }
                catch { /* best effort */ }
            }
        }

        return anyChanges;
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  STEP 3 — Shape collection and partition reading
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Collects all NiTriShapes from a head part model NIF, along with their
    /// BSDismemberSkinInstance partition body-part IDs.
    /// </summary>
    private List<ModelShapeInfo> CollectModelShapes(NifFile modelNif)
    {
        var results = new List<ModelShapeInfo>();

        using var shapes = modelNif.GetShapes();
        foreach (var shape in shapes)
        {
            // Read the shape's name.
            string shapeName = shape.name.get();

            // Collect partition body-part IDs from BSDismemberSkinInstance.
            var bodyParts = GetDismemberBodyParts(modelNif, shape);

            results.Add(new ModelShapeInfo
            {
                Shape = shape,
                Name = shapeName ?? "UnnamedShape",
                PartitionBodyParts = bodyParts,
            });
        }

        return results;
    }

    /// <summary>
    /// Reads all body-part IDs from a shape's BSDismemberSkinInstance partitions.
    /// Returns an empty set if the shape has no dismember skin instance.
    ///
    /// Uses the higher-level NifFile.GetShapePartitions API rather than the
    /// BlockCache pattern (both work, but this avoids manual block traversal).
    /// </summary>
    private static HashSet<int> GetDismemberBodyParts(NifFile nif, NiShape shape)
    {
        var bodyParts = new HashSet<int>();

        var partitionInfo = new NiVectorBSDismemberSkinInstancePartitionInfo();
        var triParts = new vectorint();

        if (!nif.GetShapePartitions(shape, partitionInfo, triParts))
        {
            return bodyParts;
        }

        using var partitions = partitionInfo.items();
        for (int i = 0; i < partitions.Count; i++)
        {
            bodyParts.Add(partitions[i].partID);
        }

        return bodyParts;
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  STEP 4 — Remove conflicting shapes from FaceGen NIF
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Removes all shapes under BSFaceGenNiNodeSkinned whose BSDismemberSkinInstance
    /// partitions overlap with the given set of body-part IDs.
    /// </summary>
    private void RemoveShapesByPartition(
        NifFile faceGenNif,
        NiNode faceGenSkinNode,
        HashSet<int> targetPartitions,
        NPCInfo npcInfo)
    {
        if (targetPartitions.Count == 0) return;

        // Collect shapes to remove first (avoid modifying collection during iteration).
        var shapesToRemove = new List<(NiShape Shape, string Name)>();

        using var shapes = faceGenNif.GetShapes();
        foreach (var shape in shapes)
        {
            // Only consider shapes parented to BSFaceGenNiNodeSkinned.
            var parent = faceGenNif.GetParentNode(shape);
            if (parent == null) continue;

            // Compare parent by reference to the known faceGenSkinNode, or by name.
            string parentName = parent.name.get();
            if (parentName != "BSFaceGenNiNodeSkinned")
            {
                // Also check by block type (same fallback as FaceGenPatcher).
                uint blockId = faceGenNif.GetBlockID(parent);
                string typeName = faceGenNif.GetHeader().GetBlockTypeStringById(blockId);
                if (typeName != "BSFaceGenNiNodeSkinned") continue;
            }

            // Check if this shape's partitions overlap with the target set.
            var shapeBodyParts = GetDismemberBodyParts(faceGenNif, shape);
            if (shapeBodyParts.Overlaps(targetPartitions))
            {
                shapesToRemove.Add((shape, shape.name.get() ?? "unnamed"));
            }
        }

        // Delete the shapes.
        foreach (var (shape, name) in shapesToRemove)
        {
            faceGenNif.DeleteShape(shape);

            _logger.LogReport(
                "HeadPartSwapper: Removed existing shape \"" + name + "\" (partition conflict).",
                false, npcInfo);
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  NIF STRUCTURAL HELPERS
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Locates the BSFaceGenNiNodeSkinned node in a FaceGen NIF.
    ///
    /// Uses the same traversal pattern confirmed working in FaceGenPatcher:
    /// iterate shapes via GetShapes(), call GetParentNode() on each, and check
    /// the parent's name (via NiStringRef.get()) and block type (via header).
    ///
    /// Returns the first matching NiNode, or null if the NIF has no
    /// BSFaceGenNiNodeSkinned subtree.
    /// </summary>
    private static NiNode FindFaceGenSkinNode(NifFile nif)
    {
        using var shapes = nif.GetShapes();
        foreach (var shape in shapes)
        {
            var parentNode = nif.GetParentNode(shape);
            if (parentNode == null) continue;

            // Check by node name (most FaceGen NIFs use this).
            string parentName = parentNode.name.get();
            if (parentName == "BSFaceGenNiNodeSkinned")
            {
                return parentNode;
            }

            // Fallback: check the block type string via the header, in case the
            // NIF stores BSFaceGenNiNodeSkinned as a distinct block type rather
            // than a named NiNode. Same pattern as FaceGenPatcher.IsUnderFaceGenSkinNode.
            uint blockId = nif.GetBlockID(parentNode);
            if (blockId >= 0)
            {
                string typeName = nif.GetHeader().GetBlockTypeStringById(blockId);
                if (typeName == "BSFaceGenNiNodeSkinned")
                {
                    return parentNode;
                }
            }
        }

        return null;
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  PATH RESOLUTION
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Builds the absolute path to an NPC's FaceGen NIF under a given root folder.
    /// Same logic as FaceGenPatcher.ResolveFaceGenNifPath.
    /// </summary>
    private static string ResolveFaceGenNifPath(NPCInfo npcInfo, string rootFolder)
    {
        FormKey formKey = npcInfo.NPC.FormKey;
        string pluginName = formKey.ModKey.FileName;
        string formIdHex = formKey.ID.ToString("X8");

        return Path.Combine(
            rootFolder,
            "meshes", "actors", "character",
            "facegendata", "facegeom",
            pluginName,
            formIdHex + ".nif");
    }

    /// <summary>
    /// Builds the BSA-relative subpath for an NPC's FaceGen NIF.
    /// Same logic as FaceGenPatcher.ResolveFaceGenNifBsaSubPath.
    /// </summary>
    private static string ResolveFaceGenNifBsaSubPath(NPCInfo npcInfo)
    {
        FormKey formKey = npcInfo.NPC.FormKey;
        string pluginName = formKey.ModKey.FileName;
        string formIdHex = formKey.ID.ToString("X8");

        return string.Join("\\",
            "meshes", "actors", "character",
            "facegendata", "facegeom",
            pluginName,
            formIdHex + ".nif");
    }

    /// <summary>
    /// Resolves a head part model's relative NIF path to an absolute path under
    /// the game's Data folder.
    /// </summary>
    private string ResolveModelNifPath(string modelRelativePath)
    {
        return Path.Combine(_environmentProvider.DataFolderPath, modelRelativePath);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  BSA EXTRACTION
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Attempts to extract the NPC's FaceGen NIF from BSAs. Returns the extracted
    /// file path, or null if not found.
    /// </summary>
    private string TryExtractFaceGenFromBsa(NPCInfo npcInfo, out bool extracted)
    {
        extracted = false;
        string bsaSubPath = ResolveFaceGenNifBsaSubPath(npcInfo);
        FormKey formKey = npcInfo.NPC.FormKey;
        string extractedPath = Path.Combine(
            _patcherState.ModManagerSettings.TempExtractionFolder,
            formKey.ModKey.FileName + "_" + formKey.ID.ToString("X8") + "_facegen.nif");

        var contexts = _environmentProvider.LinkCache.ResolveAllContexts<INpc, INpcGetter>(npcInfo.NPC.FormKey);
        foreach (var context in contexts)
        {
            if (_bsaHandler.TryOpenCorrespondingArchiveReaders(context.ModKey, out var bsaReaders) &&
                _bsaHandler.ReadersHaveFile(bsaSubPath, bsaReaders, out var file) &&
                _bsaHandler.TryExtractFileFromBSA(file, extractedPath))
            {
                extracted = true;
                return extractedPath;
            }
        }

        return null;
    }

    /// <summary>
    /// Attempts to extract a head part model NIF from BSAs. Searches archives
    /// corresponding to the head part's source mod.
    /// </summary>
    private string TryExtractModelFromBsa(
        string modelRelativePath,
        IHeadPartGetter headPartGetter,
        out bool extracted)
    {
        extracted = false;

        // Normalize the path for BSA lookup (backslash-separated).
        string bsaSubPath = modelRelativePath.Replace('/', '\\');

        string safeFileName = Path.GetFileNameWithoutExtension(modelRelativePath)
            .Replace(Path.DirectorySeparatorChar, '_')
            .Replace(Path.AltDirectorySeparatorChar, '_');
        string extractedPath = Path.Combine(
            _patcherState.ModManagerSettings.TempExtractionFolder,
            "HP_" + safeFileName + ".nif");

        // Try the head part's own mod first, then all mods in the load order
        // that override this head part.
        var contexts = _environmentProvider.LinkCache.ResolveAllContexts<IHeadPart, IHeadPartGetter>(headPartGetter.FormKey);
        foreach (var context in contexts)
        {
            if (_bsaHandler.TryOpenCorrespondingArchiveReaders(context.ModKey, out var bsaReaders) &&
                _bsaHandler.ReadersHaveFile(bsaSubPath, bsaReaders, out var file) &&
                _bsaHandler.TryExtractFileFromBSA(file, extractedPath))
            {
                extracted = true;
                return extractedPath;
            }
        }

        return null;
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  INTERNAL TYPES
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Holds information about a NiTriShape from a head part model NIF.
    /// </summary>
    private class ModelShapeInfo
    {
        public NiShape Shape { get; set; }
        public string Name { get; set; }
        public HashSet<int> PartitionBodyParts { get; set; } = new();
    }
}