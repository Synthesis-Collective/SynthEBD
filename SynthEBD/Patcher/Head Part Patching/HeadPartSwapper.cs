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

    /// <summary>
    /// Tag written to the NIF header's ExportInfo field to identify SynthEBD outputs.
    /// The game engine completely ignores this field — it's pure metadata used by
    /// modding tools (e.g., Outfit Studio writes "Exported using Outfit Studio" here).
    /// </summary>
    private const string SynthEBDNifTag = "SynthEBD HeadPartSwapper Output";

    // ─── Dark Face Bug Fix Toggles ───────────────────────────────────────────
    //
    // Both address the same root cause: the removal pass incorrectly deletes
    // shapes (eyes, mouth, brows) that use plain NiSkinInstance instead of
    // BSDismemberSkinInstance. nifly reports a synthetic partition ID of 32
    // (SBP_32_BODY) for these shapes, which can collide with incoming head
    // part partitions and cause false-positive deletion.
    //
    // FIX_A: Skip non-dismember shapes during removal.
    //   Shapes with a plain NiSkinInstance have no real partition data, so
    //   partition-based matching is invalid for them. When true, the removal
    //   pass only considers shapes that have a genuine BSDismemberSkinInstance.
    //
    // FIX_B: Only use expected partitions for removal (not incoming).
    //   Don't include the incoming model's actual partition IDs in the removal
    //   set. Only use the static TypeToExpectedPartitions map, which contains
    //   well-known partition IDs for each head part type. This avoids pulling
    //   in collision-prone IDs (like 32) from non-standard modded NIFs.
    //
    // Recommended: enable both. Either one alone fixes the Hod-style bug,
    // but together they cover a wider range of edge cases.

    private const bool FIX_A_SKIP_NON_DISMEMBER_IN_REMOVAL = true;
    private const bool FIX_B_ONLY_EXPECTED_PARTITIONS_FOR_REMOVAL = true;

    private readonly IOutputEnvironmentStateProvider _environmentProvider;
    private readonly PatcherState _patcherState;
    private readonly SynthEBDPaths _paths;
    private readonly BSAHandler _bsaHandler;
    private readonly Logger _logger;

    // ── Debug tracing for specific NPCs ──
    // Set of NPC FormKeys that get verbose diagnostic logging at every step.
    // Remove or clear this set once debugging is complete.
    private static readonly HashSet<FormKey> DebugFormKeys = new()
    {
        Mutagen.Bethesda.FormKeys.SkyrimSE.Skyrim.Npc.Hod.FormKey,
        Mutagen.Bethesda.FormKeys.SkyrimSE.Skyrim.Npc.Alvor.FormKey,
    };

    private bool IsDebugNpc(NPCInfo npcInfo)
    {
        return npcInfo?.NPC?.FormKey != null &&
               DebugFormKeys.Contains(npcInfo.NPC.FormKey);
    }

    private void DebugLog(NPCInfo npcInfo, string message)
    {
        if (IsDebugNpc(npcInfo))
        {
            _logger.LogMessage("[DEBUG-HPSWAP " + npcInfo.NPC.FormKey + "] " + message);
        }
    }

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
    /// <returns>
    ///   true if the FaceGen NIF was processed normally (even if no changes were needed);
    ///   false if the NPC was SKIPPED because the source FaceGen is a stale SynthEBD output.
    ///   When false, the caller should also skip record modification (HeadPartWriter) for
    ///   this NPC to avoid mismatches between the NPC record and the FaceGen NIF.
    /// </returns>
    public bool ApplyHeadPartsToFaceGen(
        NPCInfo npcInfo,
        Dictionary<HeadPart.TypeEnum, FormKey> headPartAssignments)
    {
        DebugLog(npcInfo, "=== ApplyHeadPartsToFaceGen ENTER === assignments.Count=" + headPartAssignments.Count);
        foreach (var kvp in headPartAssignments)
        {
            DebugLog(npcInfo, "  Assignment: " + kvp.Key + " -> " + kvp.Value);
        }

        if (headPartAssignments.Count == 0) return true;

        // Filter out excluded types (Face) and unresolvable assignments.
        var validAssignments = ResolveHeadPartAssignments(headPartAssignments);
        DebugLog(npcInfo, "After ResolveHeadPartAssignments: validAssignments.Count=" + validAssignments.Count);
        foreach (var (t, hp) in validAssignments)
        {
            DebugLog(npcInfo, "  Valid: " + t + " -> " + (hp.EditorID ?? hp.FormKey.ToString()) + " model=" + hp.Model?.File?.DataRelativePath.Path);
        }
        if (validAssignments.Count == 0) return true;

        // ── Resolve and load the FaceGen NIF ──

        string faceGenSourcePath = ResolveFaceGenNifPath(npcInfo, _environmentProvider.DataFolderPath);
        bool extractedFromBsa = false;

        DebugLog(npcInfo, "FaceGen source path (loose): " + faceGenSourcePath + " exists=" + File.Exists(faceGenSourcePath));

        if (!File.Exists(faceGenSourcePath))
        {
            DebugLog(npcInfo, "FaceGen not loose, trying BSA extraction...");
            faceGenSourcePath = TryExtractFaceGenFromBsa(npcInfo, out extractedFromBsa);
            if (faceGenSourcePath == null)
            {
                DebugLog(npcInfo, "FaceGen NIF NOT FOUND anywhere — returning.");
                LogAndPrint(
                    "HeadPartSwapper: FaceGen NIF not found for NPC. Head parts cannot be baked.",
                    false, npcInfo);
                return true;
            }
            DebugLog(npcInfo, "FaceGen extracted from BSA: " + faceGenSourcePath);
        }

        string faceGenOutputPath = ResolveFaceGenNifPath(npcInfo, _paths.OutputDataFolder);
        DebugLog(npcInfo, "FaceGen output path: " + faceGenOutputPath);

        // ── Tier 1: Detect stale output by path ──
        //
        // If the source FaceGen NIF lives inside the output folder, it's a
        // previous SynthEBD output being fed back via MO2's VFS. The face
        // shape textures may reference mods that are no longer active, causing
        // dark face bug. Skip this NPC entirely.
        if (IsPreviousOutputByPath(faceGenSourcePath))
        {
            LogAndPrint(
                "HeadPartSwapper: WARNING — FaceGen source is inside the output folder (stale previous output). " +
                "Skipping NIF editing and record modification for this NPC. " +
                "Please clear your SynthEBD output folder and re-run. Source: " + faceGenSourcePath,
                true, npcInfo);
            return false;
        }

        // ── Open the FaceGen NIF and apply all head part swaps ──

        bool anyChanges = false;

        using (var faceGenNif = new NifFile())
        {
            int loadResult = faceGenNif.Load(faceGenSourcePath);
            DebugLog(npcInfo, "FaceGen NIF load result: " + loadResult);
            if (loadResult != 0)
            {
                LogAndPrint(
                    "HeadPartSwapper: Failed to load FaceGen NIF (error " + loadResult + "): " + faceGenSourcePath, true, npcInfo);
                return true;
            }

            // ── Tier 2: Detect stale output by NIF metadata ──
            //
            // Even if the source path doesn't match the current output folder
            // (e.g., the user changed OutputDataFolder between runs), a SynthEBD
            // tag in the NIF header's ExportInfo field identifies it as a previous
            // output. This catches the edge case of orphaned outputs.
            string existingExportInfo = faceGenNif.GetHeader().GetExportInfo() ?? "";
            if (existingExportInfo.Contains(SynthEBDNifTag))
            {
                LogAndPrint(
                    "HeadPartSwapper: WARNING — FaceGen NIF is tagged as a previous SynthEBD output. " +
                    "Skipping NIF editing and record modification for this NPC. " +
                    "Please clear your previous SynthEBD output folder and re-run. Source: " + faceGenSourcePath,
                    true, npcInfo);
                return false;
            }

            // Dump FaceGen NIF block structure for debug NPCs.
            if (IsDebugNpc(npcInfo))
            {
                var dbgHeader = faceGenNif.GetHeader();
                uint numBlocks = dbgHeader.GetNumBlocks();
                DebugLog(npcInfo, "FaceGen NIF block count: " + numBlocks);
                for (uint bi = 0; bi < numBlocks && bi < 30; bi++)
                {
                    string btype = dbgHeader.GetBlockTypeStringById(bi);
                    NiObject bobj = dbgHeader.GetBlockById(bi);
                    string bname = "";
                    try { if (bobj is NiObjectNET named) bname = named.name?.get() ?? ""; } catch { }
                    DebugLog(npcInfo, "  Block[" + bi + "] " + btype + " \"" + bname + "\"");
                }
                using var dbgNodes = faceGenNif.GetNodes();
                DebugLog(npcInfo, "FaceGen NIF node count: " + dbgNodes.Count);
                foreach (var dn in dbgNodes)
                {
                    string nn = dn.name?.get() ?? "(null)";
                    uint nid = dbgHeader.GetBlockID(dn);
                    DebugLog(npcInfo, "  Node: \"" + nn + "\" blockId=" + nid);
                }
            }

            // Find the BSFaceGenNiNodeSkinned parent node.
            NiNode faceGenSkinNode = FindFaceGenSkinNode(faceGenNif);
            DebugLog(npcInfo, "FindFaceGenSkinNode result: " + (faceGenSkinNode == null ? "NULL" : "found, name=\"" + (faceGenSkinNode.name?.get() ?? "") + "\""));
            if (faceGenSkinNode == null)
            {
                _logger.LogReport(
                    "HeadPartSwapper: No BSFaceGenNiNodeSkinned node found in: " + faceGenSourcePath,
                    false, npcInfo);
                return true;
            }

            foreach (var (type, headPartGetter) in validAssignments)
            {
                DebugLog(npcInfo, "--- SwapHeadPartType: type=" + type + " editorId=" + (headPartGetter.EditorID ?? headPartGetter.FormKey.ToString()));
                bool changed = SwapHeadPartType(faceGenNif, faceGenSkinNode, headPartGetter, type, npcInfo);
                DebugLog(npcInfo, "--- SwapHeadPartType result: changed=" + changed);
                anyChanges |= changed;
            }

            DebugLog(npcInfo, "All swaps done. anyChanges=" + anyChanges);

            if (anyChanges)
            {
                string outputDir = Path.GetDirectoryName(faceGenOutputPath);
                if (!string.IsNullOrEmpty(outputDir))
                {
                    Directory.CreateDirectory(outputDir);
                }

                // Dump final block structure for debug NPCs before saving.
                if (IsDebugNpc(npcInfo))
                {
                    var dbgHeader = faceGenNif.GetHeader();
                    uint numBlocks = dbgHeader.GetNumBlocks();
                    DebugLog(npcInfo, "=== FINAL FaceGen NIF block structure (before save) === blocks=" + numBlocks);
                    for (uint bi = 0; bi < numBlocks && bi < 80; bi++)
                    {
                        string btype = dbgHeader.GetBlockTypeStringById(bi);
                        NiObject bobj = dbgHeader.GetBlockById(bi);
                        string bname = "";
                        try { if (bobj is NiObjectNET named) bname = named.name?.get() ?? ""; } catch { }
                        DebugLog(npcInfo, "  Block[" + bi + "] " + btype + " \"" + bname + "\"");
                    }
                }

                // Tag the output NIF so future runs can detect it as a SynthEBD output,
                // even if the user changes their output folder path between runs.
                faceGenNif.GetHeader().SetExportInfo(SynthEBDNifTag);

                int saveResult = faceGenNif.Save(faceGenOutputPath);
                DebugLog(npcInfo, "Save result: " + saveResult + " path=" + faceGenOutputPath);
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

        return true;
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
        // Use the headpart's EditorID as the shape name in the FaceGen NIF — the game
        // matches headpart records to FaceGen geometry by name, so the shape must be
        // named after the EditorID, not whatever the source mesh calls it (e.g. "group_0").
        string mainEditorId = headPartGetter.EditorID ?? headPartGetter.FormKey.ToString();

        // performRemoval=true: the main headpart's removal pass uses the union of the
        // type's expected partitions AND the incoming model's partitions, which covers
        // both the main shape and any extra parts. Extra parts must NOT re-trigger
        // removal, or they'll delete the main shape we just cloned.
        anyChanges |= SwapSingleHeadPartModel(faceGenNif, faceGenSkinNode, headPartGetter, type, npcInfo, mainEditorId, performRemoval: true);

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

                    string extraEditorId = extraPartGetter.EditorID ?? extraPartGetter.FormKey.ToString();

                    _logger.LogReport(
                        "HeadPartSwapper: Processing ExtraPart " + extraEditorId + " for " + type,
                        false, npcInfo);

                    // performRemoval=false: extra parts must not trigger removal, as the
                    // main headpart's removal pass already cleared conflicting shapes.
                    anyChanges |= SwapSingleHeadPartModel(faceGenNif, faceGenSkinNode, extraPartGetter, type, npcInfo, extraEditorId, performRemoval: false);
                }
            }
        }

        return anyChanges;
    }

    /// <summary>
    /// Opens a single head part model NIF, optionally removes conflicting shapes from
    /// FaceGen, and clones the model's shapes into BSFaceGenNiNodeSkinned.
    ///
    /// Cloned shapes are renamed to the headpart's EditorID (so the game can match
    /// headpart records to FaceGen geometry) and converted from NiTriShape to
    /// BSDynamicTriShape if needed (FaceGen NIFs require BSDynamicTriShape).
    ///
    /// Before cloning, the source model's root node is renamed to match
    /// BSFaceGenNiNodeSkinned so that CloneShape correctly remaps the skeleton root
    /// reference in the BSDismemberSkinInstance.
    /// </summary>
    private bool SwapSingleHeadPartModel(
        NifFile faceGenNif,
        NiNode faceGenSkinNode,
        IHeadPartGetter headPartGetter,
        HeadPart.TypeEnum type,
        NPCInfo npcInfo,
        string headPartEditorId,
        bool performRemoval)
    {
        // ── Locate the head part model NIF on disk ──

        string modelRelPath = headPartGetter.Model.File.DataRelativePath.Path;
        string modelAbsPath = ResolveModelNifPath(modelRelPath);
        bool extractedModel = false;

        DebugLog(npcInfo, "SwapSingleHeadPartModel: editorId=" + headPartEditorId +
            " modelRelPath=" + modelRelPath + " performRemoval=" + performRemoval);
        DebugLog(npcInfo, "  modelAbsPath=" + modelAbsPath + " exists=" + File.Exists(modelAbsPath));

        if (!File.Exists(modelAbsPath))
        {
            modelAbsPath = TryExtractModelFromBsa(modelRelPath, headPartGetter, out extractedModel);
            if (modelAbsPath == null)
            {
                LogAndPrint(
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
            DebugLog(npcInfo, "  Model NIF load result: " + loadResult);
            if (loadResult != 0)
            {
                _logger.LogError(
                    "HeadPartSwapper: Failed to load model NIF (error " + loadResult + "): " + modelAbsPath);
                return false;
            }

            // ── Ensure the model NIF uses SSE-native BSDynamicTriShape blocks ──
            //
            // Many modded head part NIFs (e.g., KS Hairdos) use the old Gamebryo NiTriShape
            // format with separate NiTriShapeData blocks. FaceGen NIFs require BSDynamicTriShape,
            // which stores geometry inline. CloneShape preserves the source block type, so we
            // need the source to already be BSDynamicTriShape before cloning.
            //
            // OptimizeFor with headParts=true converts NiTriShape → BSDynamicTriShape (this is
            // the same conversion Outfit Studio uses). Since modelNif is a temporary in-memory
            // copy that we discard after cloning, optimizing it in-place is safe.

            bool isSSE = modelNif.GetHeader().GetVersion().IsSSE();
            DebugLog(npcInfo, "  Model NIF IsSSE=" + isSSE);

            // Dump source model shape types before optimize
            if (IsDebugNpc(npcInfo))
            {
                using var dbgShapes = modelNif.GetShapes();
                DebugLog(npcInfo, "  Source model shape count (pre-optimize): " + dbgShapes.Count);
                foreach (var ds in dbgShapes)
                {
                    string sn = ds.name?.get() ?? "(null)";
                    string st = GetBlockTypeName(modelNif, ds);
                    DebugLog(npcInfo, "    Shape: \"" + sn + "\" type=" + st);
                }
            }

            if (!isSSE)
            {
                using var optOptions = new OptOptions();
                optOptions.targetVersion = NiVersion.getSSE();
                optOptions.headParts = true;
                modelNif.OptimizeFor(optOptions);

                _logger.LogReport(
                    "HeadPartSwapper: Optimized model NIF to SSE format (NiTriShape → BSDynamicTriShape): " + modelRelPath,
                    false, npcInfo);
            }
            else
            {
                // Even SSE NIFs may use BSTriShape instead of BSDynamicTriShape for head parts.
                // OptimizeFor with headParts=true promotes BSTriShape → BSDynamicTriShape.
                using var shapes = modelNif.GetShapes();
                bool needsOptimize = false;
                foreach (var shape in shapes)
                {
                    string blockType = GetBlockTypeName(modelNif, shape);
                    if (blockType != "BSDynamicTriShape")
                    {
                        needsOptimize = true;
                        break;
                    }
                }

                if (needsOptimize)
                {
                    using var optOptions = new OptOptions();
                    optOptions.targetVersion = NiVersion.getSSE();
                    optOptions.headParts = true;
                    modelNif.OptimizeFor(optOptions);

                    _logger.LogReport(
                        "HeadPartSwapper: Promoted model NIF shapes to BSDynamicTriShape: " + modelRelPath,
                        false, npcInfo);
                }
            }

            // NOTE: The skeleton root reference fix happens AFTER cloning in
            // RemapClonedShapeBones() below, not before, because CloneShape copies
            // block indices rather than doing name-based remapping for the skeleton root.

            // Dump source model shape types after optimize
            if (IsDebugNpc(npcInfo))
            {
                using var dbgShapes2 = modelNif.GetShapes();
                DebugLog(npcInfo, "  Source model shape count (post-optimize): " + dbgShapes2.Count);
                foreach (var ds in dbgShapes2)
                {
                    string sn = ds.name?.get() ?? "(null)";
                    string st = GetBlockTypeName(modelNif, ds);
                    DebugLog(npcInfo, "    Shape: \"" + sn + "\" type=" + st);

                    // Also dump bone list
                    using var boneNames = new vectorstring();
                    uint bc = modelNif.GetShapeBoneList(ds, boneNames);
                    DebugLog(npcInfo, "      Bones (" + bc + "): " + string.Join(", ", Enumerable.Range(0, boneNames.Count).Select(j => boneNames[j])));
                }
            }

            // ── Collect shapes from the (now-optimized) model NIF and their partition IDs ──

            var modelShapeInfos = CollectModelShapes(modelNif);
            DebugLog(npcInfo, "  CollectModelShapes returned " + modelShapeInfos.Count + " shapes.");
            foreach (var info in modelShapeInfos)
            {
                DebugLog(npcInfo, "    Shape: \"" + info.Name + "\" partitions=[" + string.Join(",", info.PartitionBodyParts) + "]");
            }
            if (modelShapeInfos.Count == 0)
            {
                _logger.LogReport(
                    "HeadPartSwapper: No shapes found in model: " + modelRelPath,
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
            // Only performed for the main headpart, not for extra parts — the main
            // headpart's removal pass already clears all conflicting shapes using
            // the type's expected partition set.

            if (performRemoval && SingularTypes.Contains(type))
            {
                // Build the set of partition IDs to match against.
                HashSet<int> removePartitions;

                if (FIX_B_ONLY_EXPECTED_PARTITIONS_FOR_REMOVAL)
                {
                    // FIX_B: Only use the static expected partitions for this type.
                    // Do NOT include the incoming model's actual partitions, because
                    // modded NIFs may use non-standard IDs (e.g. SBP_32_BODY) that
                    // collide with unrelated shapes in the FaceGen NIF.
                    removePartitions = new HashSet<int>();
                    if (TypeToExpectedPartitions.TryGetValue(type, out var expectedOnly))
                    {
                        removePartitions.UnionWith(expectedOnly);
                    }
                }
                else
                {
                    // Original behavior: use both the expected partitions for this type
                    // AND the actual partitions from the incoming head part.
                    removePartitions = new HashSet<int>(incomingPartitions);
                    if (TypeToExpectedPartitions.TryGetValue(type, out var expected))
                    {
                        removePartitions.UnionWith(expected);
                    }
                }

                RemoveShapesByPartition(faceGenNif, faceGenSkinNode, removePartitions, npcInfo);
            }

            // ── Clone shapes from model NIF into FaceGen NIF ──

            for (int i = 0; i < modelShapeInfos.Count; i++)
            {
                var shapeInfo = modelShapeInfos[i];

                // Use the headpart's EditorID as the destination shape name. The game
                // matches headpart records to FaceGen geometry by shape name, so it must
                // be the EditorID — not whatever the source mesh calls it (e.g. "group_0").
                // If a single NIF has multiple shapes, make subsequent names unique.
                string destShapeName = modelShapeInfos.Count == 1
                    ? headPartEditorId
                    : headPartEditorId + "_" + i;

                DebugLog(npcInfo, "  Cloning shape[" + i + "]: src=\"" + shapeInfo.Name +
                    "\" -> dest=\"" + destShapeName + "\" srcType=" + GetBlockTypeName(modelNif, shapeInfo.Shape));

                // Clone the shape from the model NIF into the FaceGen NIF.
                // CloneShape handles copying geometry, shader, skin weights, and
                // remaps bone indices between source and destination NIFs.
                NiShape clonedShape = faceGenNif.CloneShape(shapeInfo.Shape, destShapeName, modelNif);

                if (clonedShape == null)
                {
                    DebugLog(npcInfo, "  CloneShape returned NULL!");
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

                string clonedBlockType = GetBlockTypeName(faceGenNif, clonedShape);
                DebugLog(npcInfo, "  Cloned successfully. Cloned block type in FaceGen: " + clonedBlockType);
                DebugLog(npcInfo, "  HasSkinInstance=" + clonedShape.HasSkinInstance());

                // Dump cloned shape's bone IDs BEFORE remap
                if (IsDebugNpc(npcInfo))
                {
                    using var preRemapIds = new vectorint();
                    faceGenNif.GetShapeBoneIDList(clonedShape, preRemapIds);
                    DebugLog(npcInfo, "  Bone IDs BEFORE remap (" + preRemapIds.Count + "): [" +
                        string.Join(", ", Enumerable.Range(0, preRemapIds.Count).Select(j => preRemapIds[j].ToString())) + "]");
                }

                // ── Remap bone references in the cloned skin instance ──
                DebugLog(npcInfo, "  Calling RemapClonedShapeBones...");
                RemapClonedShapeBones(faceGenNif, clonedShape, modelNif, shapeInfo.Shape, faceGenSkinNode, npcInfo);

                // Dump cloned shape's bone IDs AFTER remap
                if (IsDebugNpc(npcInfo))
                {
                    using var postRemapIds = new vectorint();
                    faceGenNif.GetShapeBoneIDList(clonedShape, postRemapIds);
                    DebugLog(npcInfo, "  Bone IDs AFTER remap (" + postRemapIds.Count + "): [" +
                        string.Join(", ", Enumerable.Range(0, postRemapIds.Count).Select(j => postRemapIds[j].ToString())) + "]");
                }

                _logger.LogReport(
                    "HeadPartSwapper: Cloned shape \"" + shapeInfo.Name + "\" as \"" + destShapeName +
                    "\" (" + type + ") into FaceGen NIF.",
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
    /// Collects all shapes from a head part model NIF (typically BSDynamicTriShape after
    /// optimization), along with their BSDismemberSkinInstance partition body-part IDs.
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

    /// <summary>
    /// Checks whether a shape's skin instance is a genuine BSDismemberSkinInstance
    /// (as opposed to a plain NiSkinInstance). Only shapes with BSDismemberSkinInstance
    /// have meaningful partition body-part IDs; nifly synthesizes a default partition
    /// ID of 32 (SBP_32_BODY) for plain NiSkinInstance shapes.
    /// </summary>
    private static bool HasTrueDismemberSkinInstance(NifFile nif, NiShape shape)
    {
        if (!shape.HasSkinInstance()) return false;

        NiBlockRefNiBoneContainer skinInstRef = shape.SkinInstanceRef();
        if (skinInstRef == null || skinInstRef.IsEmpty()) return false;

        uint skinInstBlockId = skinInstRef.index;
        string blockType = nif.GetHeader().GetBlockTypeStringById(skinInstBlockId);
        return blockType == "BSDismemberSkinInstance";
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
                if (!IsBlockType(faceGenNif, parent, "BSFaceGenNiNodeSkinned")) continue;
            }

            // FIX_A: Skip shapes that use a plain NiSkinInstance rather than
            // BSDismemberSkinInstance. Plain NiSkinInstance shapes have no real
            // partition data — nifly synthesizes a default partition ID of 32
            // (SBP_32_BODY), which causes false-positive matches against
            // incoming head parts that happen to use partition 32.
            if (FIX_A_SKIP_NON_DISMEMBER_IN_REMOVAL && !HasTrueDismemberSkinInstance(faceGenNif, shape))
            {
                DebugLog(npcInfo, "  RemoveShapesByPartition: Skipping \"" +
                    (shape.name?.get() ?? "unnamed") +
                    "\" — no BSDismemberSkinInstance (FIX_A)");
                continue;
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
    /// Remaps all bone references in a cloned shape's skin instance so they
    /// point to the correct NiNode blocks in the FaceGen NIF.
    ///
    /// CloneShape copies raw bone block indices from the source NIF. In the
    /// source model NIF, "NPC Head [Head]" might be block 5; in the FaceGen
    /// NIF, it's block 1. Without remapping, the game follows garbage block
    /// indices into unrelated data, causing crashes (typically divide-by-zero
    /// in NiSkinPartition processing).
    ///
    /// This method:
    ///   1. Reads bone names from the source shape in the source model NIF
    ///   2. Builds a name→blockId map of all NiNodes in the FaceGen NIF
    ///   3. Maps the source root node name to BSFaceGenNiNodeSkinned
    ///   4. Overwrites bone block IDs on the cloned shape via SetShapeBoneIDList
    ///   5. Fixes the skeleton root pointer (targetRef) in the skin instance
    /// </summary>
    private void RemapClonedShapeBones(
        NifFile faceGenNif,
        NiShape clonedShape,
        NifFile sourceNif,
        NiShape sourceShape,
        NiNode faceGenSkinNode,
        NPCInfo npcInfo)
    {
        NiHeader faceGenHeader = faceGenNif.GetHeader();
        uint skinNodeBlockId = faceGenHeader.GetBlockID(faceGenSkinNode);
        string skinNodeName = faceGenSkinNode.name?.get() ?? "BSFaceGenNiNodeSkinned";

        // ── Step 1: Get bone names from the source shape ──

        using var sourceBoneNames = new vectorstring();
        uint boneCount = sourceNif.GetShapeBoneList(sourceShape, sourceBoneNames);
        if (boneCount == 0)
        {
            _logger.LogReport(
                "HeadPartSwapper: Source shape has no bones, skipping bone remap.",
                false, npcInfo);
            return;
        }

        // ── Step 2: Build name→blockId map for all NiNodes in FaceGen NIF ──

        var nodeNameToBlockId = new Dictionary<string, uint>();
        using var faceGenNodes = faceGenNif.GetNodes();
        foreach (var node in faceGenNodes)
        {
            string nodeName = node.name?.get();
            if (!string.IsNullOrEmpty(nodeName) && !nodeNameToBlockId.ContainsKey(nodeName))
            {
                uint nodeBlockId = faceGenHeader.GetBlockID(node);
                nodeNameToBlockId[nodeName] = nodeBlockId;
            }
        }

        // Also ensure BSFaceGenNiNodeSkinned is in the map.
        if (!string.IsNullOrEmpty(skinNodeName))
        {
            nodeNameToBlockId[skinNodeName] = skinNodeBlockId;
        }

        // ── Step 3: Map source root node name → BSFaceGenNiNodeSkinned ──
        //
        // The source model's root (e.g., "Scene Root") has no name match in
        // the FaceGen NIF. It corresponds to BSFaceGenNiNodeSkinned, which
        // serves as the skeleton root for FaceGen shapes.

        NiNode sourceRoot = sourceNif.GetRootNode();
        if (sourceRoot != null)
        {
            string sourceRootName = sourceRoot.name?.get();
            if (!string.IsNullOrEmpty(sourceRootName) && !nodeNameToBlockId.ContainsKey(sourceRootName))
            {
                nodeNameToBlockId[sourceRootName] = skinNodeBlockId;

                _logger.LogReport(
                    "HeadPartSwapper: Mapping source root \"" + sourceRootName +
                    "\" -> BSFaceGenNiNodeSkinned (block " + skinNodeBlockId + ").",
                    false, npcInfo);
            }
        }

        // ── Step 4: Build remapped bone ID list ──

        using var remappedBoneIds = new vectorint();
        bool allBonesFound = true;

        for (int i = 0; i < sourceBoneNames.Count; i++)
        {
            string boneName = sourceBoneNames[i];

            if (nodeNameToBlockId.TryGetValue(boneName, out uint targetBlockId))
            {
                remappedBoneIds.Add((int)targetBlockId);
            }
            else
            {
                // Bone not found in FaceGen NIF — map to BSFaceGenNiNodeSkinned
                // as a fallback. This handles physics bones or custom bones that
                // aren't part of the FaceGen skeleton. The weights for this bone
                // will effectively be ignored.
                remappedBoneIds.Add((int)skinNodeBlockId);
                allBonesFound = false;

                _logger.LogReport(
                    "HeadPartSwapper: Bone \"" + boneName +
                    "\" not found in FaceGen NIF, mapping to " + skinNodeName + ".",
                    true, npcInfo);
            }
        }

        // Apply the remapped bone IDs to the cloned shape.
        faceGenNif.SetShapeBoneIDList(clonedShape, remappedBoneIds);

        _logger.LogReport(
            "HeadPartSwapper: Remapped " + sourceBoneNames.Count + " bone(s)" +
            (allBonesFound ? " (all matched)." : " (some fallbacks used)."),
            false, npcInfo);

        // ── Step 5: Fix skeleton root pointer (targetRef) ──
        //
        // The skin instance's targetRef is the skeleton root — it must point
        // to BSFaceGenNiNodeSkinned, not BSFadeNode or whatever CloneShape set.

        if (!clonedShape.HasSkinInstance())
            return;

        NiBlockRefNiBoneContainer skinInstRef = clonedShape.SkinInstanceRef();
        if (skinInstRef == null || skinInstRef.IsEmpty())
            return;

        uint skinInstBlockId = skinInstRef.index;
        NiObject skinInstObj = faceGenHeader.GetBlockById(skinInstBlockId);
        if (skinInstObj == null)
            return;

        NiSkinInstance skinInst = skinInstObj as NiSkinInstance;
        if (skinInst == null)
            return;

        NiBlockRefNiNode targetRef = skinInst.targetRef;
        if (targetRef != null && targetRef.index != skinNodeBlockId)
        {
            uint oldTargetId = targetRef.index;
            targetRef.index = skinNodeBlockId;
            _logger.LogReport(
                "HeadPartSwapper: Fixed skeleton root: block " + oldTargetId +
                " -> block " + skinNodeBlockId + " (" + skinNodeName + ").",
                false, npcInfo);
        }
    }

    /// <summary>
    /// Checks whether a NIF block's type string matches the expected type name.
    /// </summary>
    private static bool IsBlockType(NifFile nif, NiObject block, string expectedType)
    {
        return GetBlockTypeName(nif, block) == expectedType;
    }

    /// <summary>
    /// Returns the block type name string for a NIF block (e.g. "NiTriShape",
    /// "BSDynamicTriShape", "BSFaceGenNiNodeSkinned").
    /// </summary>
    private static string GetBlockTypeName(NifFile nif, NiObject block)
    {
        uint blockId = nif.GetBlockID(block);
        return nif.GetHeader().GetBlockTypeStringById(blockId);
    }

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
            if (IsBlockType(nif, parentNode, "BSFaceGenNiNodeSkinned"))
            {
                return parentNode;
            }
        }

        return null;
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  PATH RESOLUTION
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Tier 1 stale output detection: checks if the source FaceGen NIF path
    /// is inside the current SynthEBD output folder. If so, it's a previous
    /// run's output being fed back through MO2's VFS.
    /// </summary>
    private bool IsPreviousOutputByPath(string sourcePath)
    {
        if (string.IsNullOrEmpty(sourcePath) || string.IsNullOrEmpty(_paths.OutputDataFolder))
            return false;

        try
        {
            string sourceFullPath = Path.GetFullPath(sourcePath);
            string outputFullPath = Path.GetFullPath(_paths.OutputDataFolder);

            // Ensure trailing separator for prefix comparison.
            if (!outputFullPath.EndsWith(Path.DirectorySeparatorChar.ToString()) &&
                !outputFullPath.EndsWith(Path.AltDirectorySeparatorChar.ToString()))
            {
                outputFullPath += Path.DirectorySeparatorChar;
            }

            return sourceFullPath.StartsWith(outputFullPath, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

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

    private void LogAndPrint(string message, bool triggerSave, NPCInfo npcInfo)
    {
        _logger.LogReport(message, triggerSave, npcInfo);
        _logger.LogMessage(message);
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