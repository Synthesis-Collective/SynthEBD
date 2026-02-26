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
/// Unified FaceGen NIF patcher that handles BOTH face texture baking AND head part
/// shape swapping in a single NIF editing session. This eliminates the previous issue
/// where FaceGenPatcher and HeadPartSwapper independently loaded, modified, and saved
/// the same FaceGen NIF — causing the second writer to overwrite the first's changes.
///
/// ─── Face Texture Patching (formerly FaceGenPatcher) ────────────────────────
///
///   Patches NPC FaceGen .nif files with face texture paths derived from asset
///   assignments, when Face Patching Mode is set to Mesh. Instead of applying face
///   textures at runtime via Papyrus scripts, this bakes them directly into the NIF's
///   BSShaderTextureSet.
///
///   Rather than blindly iterating every shape in the NIF, this class structurally
///   locates the head skin shape by verifying:
///
///     1. The shape's parent node is "BSFaceGenNiNodeSkinned"
///     2. The shape's BSDismemberSkinInstance contains a partition with body part
///        SBP_230_HEAD (preferred) or SBP_30_HEAD (fallback)
///
///   This ensures only the actual head mesh gets patched, leaving mouth, hair, brows,
///   etc. untouched.
///
/// ─── Head Part Swapping (formerly HeadPartSwapper) ──────────────────────────
///
///   Applies head part assignments directly into FaceGen NIF files by cloning
///   NiTriShapes from head part model NIFs into the NPC's FaceGen mesh under
///   BSFaceGenNiNodeSkinned.
///
///   This replaces the script + JSON approach (HeadPartWriter) with a baked-mesh
///   approach, producing modified FaceGen .nifs as output.
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
///     FaceGen morphs. Face textures should be handled by face texture patching instead.
///
/// ─── Known Limitations ──────────────────────────────────────────────────────
///
///   - FaceGen morph loss: Cloned shapes use default head part geometry without
///     NPC-specific vertex morphs. Acceptable for hair/eyes/brows/scars; the runtime
///     script swap has the same limitation.
///
///   - Bone remapping: nifly's CloneShape should handle bone index translation between
///     source and destination NIFs, but edge cases may exist with modded skeletons.
/// </summary>
public class FaceGenPatcher
{
    // ═══════════════════════════════════════════════════════════════════════════
    //  CONSTANTS
    // ═══════════════════════════════════════════════════════════════════════════

    // ─── BSDismemberBodyPartType values ─────────────────────────────────────

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

    // ─── Face texture slot mapping ──────────────────────────────────────────

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

    // ─── Head part type → expected partition mappings ────────────────────────

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
    /// or left untouched.
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
    private const string SynthEBDNifTag = "SynthEBD FaceGenPatcher Output";

    // ─── Dark Face Bug Fix Toggles ──────────────────────────────────────────
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

    // ═══════════════════════════════════════════════════════════════════════════
    //  DEPENDENCIES
    // ═══════════════════════════════════════════════════════════════════════════

    private readonly IOutputEnvironmentStateProvider _environmentProvider;
    private readonly PatcherState _patcherState;
    private readonly SynthEBDPaths _paths;
    private readonly BSAHandler _bsaHandler;
    private readonly Logger _logger;

    // ─── Debug tracing for specific NPCs ────────────────────────────────────
    // Set of NPC FormKeys that get verbose diagnostic logging at every step.
    // Remove or clear this set once debugging is complete.
    private static readonly HashSet<FormKey> DebugFormKeys = new()
    {
        //Mutagen.Bethesda.FormKeys.SkyrimSE.Skyrim.Npc.Uthgerd.FormKey
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
            _logger.LogMessage("[DEBUG-FGPATCH " + npcInfo.NPC.FormKey + "] " + message);
        }
    }

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

    // ═══════════════════════════════════════════════════════════════════════════
    //  PUBLIC ENTRY POINT
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Unified entry point called from <see cref="Patcher.RunPatcher"/>. Performs
    /// BOTH face texture baking AND head part shape swapping in a single NIF editing
    /// session. Either set of inputs may be null/empty if the corresponding feature
    /// is not active.
    ///
    /// Opens the FaceGen NIF once, applies all modifications, and saves once to the
    /// output folder — avoiding the previous issue where two separate classes would
    /// independently load/save the same file, causing the second to overwrite the first.
    /// </summary>
    /// <param name="npcInfo">The NPC being patched.</param>
    /// <param name="assetContainers">
    ///   Asset assignments for face texture baking (from asset selection). May be null
    ///   or empty if face texture patching is not active.
    /// </param>
    /// <param name="headPartAssignments">
    ///   Head part assignments for shape swapping (from head part selection). May be
    ///   null or empty if head part NIF patching is not active.
    /// </param>
    /// <returns>
    ///   true if the FaceGen NIF was processed normally (even if no changes were needed);
    ///   false if the NPC was SKIPPED because the source FaceGen is a stale SynthEBD output.
    ///   When false, the caller should also skip record modification (HeadPartWriter) for
    ///   this NPC to avoid mismatches between the NPC record and the FaceGen NIF.
    /// </returns>
    public bool PatchFaceGenNif(
        NPCInfo npcInfo,
        List<Patcher.SelectedAssetContainer> assetContainers,
        Dictionary<HeadPart.TypeEnum, FormKey> headPartAssignments)
    {
        // ── Step 1: Determine what work needs to be done ──

        DebugLog(npcInfo, "=== PatchFaceGenNif ENTER === assetContainers=" +
            (assetContainers == null ? "null" : assetContainers.Count.ToString()) +
            " headPartAssignments=" +
            (headPartAssignments == null ? "null" : headPartAssignments.Count.ToString()));

        // Collect face texture slot overrides from asset assignments.
        var textureSlotAssignments = (assetContainers != null && assetContainers.Count > 0)
            ? CollectFaceTextureAssignments(assetContainers, npcInfo)
            : new Dictionary<int, string>();

        bool hasTextureWork = textureSlotAssignments.Count > 0;

        // Resolve and validate head part assignments.
        var validHeadPartAssignments = (headPartAssignments != null && headPartAssignments.Count > 0)
            ? ResolveHeadPartAssignments(headPartAssignments)
            : new List<(HeadPart.TypeEnum Type, IHeadPartGetter HeadPart)>();

        bool hasHeadPartWork = validHeadPartAssignments.Count > 0;

        DebugLog(npcInfo, "After collection: hasTextureWork=" + hasTextureWork +
            " (" + textureSlotAssignments.Count + " slots) hasHeadPartWork=" + hasHeadPartWork +
            " (" + validHeadPartAssignments.Count + " parts)");

        // Nothing to do — return success without touching the NIF.
        if (!hasTextureWork && !hasHeadPartWork)
        {
            DebugLog(npcInfo, "No texture or headpart work needed — returning true without opening NIF");
            return true;
        }

        // ── Step 2: Resolve and load the source FaceGen NIF ──

        string sourcePath = ResolveFaceGenNifPath(npcInfo, _environmentProvider.DataFolderPath);
        bool extractedFromBsa = false;

        DebugLog(npcInfo, "FaceGen source path (loose): " + sourcePath + " exists=" + File.Exists(sourcePath));

        if (!File.Exists(sourcePath))
        {
            DebugLog(npcInfo, "FaceGen not loose, trying BSA extraction...");
            sourcePath = TryExtractFaceGenFromBsa(npcInfo, out extractedFromBsa);
            if (sourcePath == null)
            {
                DebugLog(npcInfo, "FaceGen NIF NOT FOUND anywhere — returning.");
                string message = "FaceGenPatcher: FaceGen NIF not found for NPC.";
                if (hasTextureWork) message += " Face textures cannot be baked.";
                if (hasHeadPartWork) message += " Head parts cannot be baked.";
                LogAndPrint(message, false, npcInfo);
                return true;
            }
            DebugLog(npcInfo, "FaceGen extracted from BSA: " + sourcePath);
        }

        string outputPath = ResolveFaceGenNifPath(npcInfo, _paths.OutputDataFolder);
        DebugLog(npcInfo, "FaceGen output path: " + outputPath);

        // ── Step 3: Stale output detection ──
        //
        // If the source FaceGen NIF lives inside the output folder, it's a
        // previous SynthEBD output being fed back via MO2's VFS. The face
        // shape textures may reference mods that are no longer active, causing
        // dark face bug. Skip this NPC entirely.

        if (hasHeadPartWork)
        {
            // Tier 1: Detect stale output by path.
            if (IsPreviousOutputByPath(sourcePath))
            {
                LogAndPrint(
                    "FaceGenPatcher: WARNING — FaceGen source is inside the output folder (stale previous output). " +
                    "Skipping NIF editing and record modification for this NPC. " +
                    "Please clear your SynthEBD output folder and re-run. Source: " + sourcePath,
                    true, npcInfo);
                CleanupTempFile(sourcePath, extractedFromBsa);
                return false;
            }
        }

        // ── Step 4: Open the NIF and apply all modifications ──

        bool anyChanges = false;

        using (var nif = new NifFile())
        {
            int loadResult = nif.Load(sourcePath);
            DebugLog(npcInfo, "FaceGen NIF load result: " + loadResult);
            if (loadResult != 0)
            {
                _logger.LogError(
                    "FaceGenPatcher: nifly failed to load NIF (error " + loadResult + "): " + sourcePath);
                CleanupTempFile(sourcePath, extractedFromBsa);
                return true;
            }

            // Tier 2: Detect stale output by NIF metadata.
            //
            // Even if the source path doesn't match the current output folder
            // (e.g., the user changed OutputDataFolder between runs), a SynthEBD
            // tag in the NIF header's ExportInfo field identifies it as a previous
            // output. This catches the edge case of orphaned outputs.
            if (hasHeadPartWork)
            {
                string existingExportInfo = nif.GetHeader().GetExportInfo() ?? "";
                if (existingExportInfo.Contains(SynthEBDNifTag))
                {
                    LogAndPrint(
                        "FaceGenPatcher: WARNING — FaceGen NIF is tagged as a previous SynthEBD output. " +
                        "Skipping NIF editing and record modification for this NPC. " +
                        "Please clear your previous SynthEBD output folder and re-run. Source: " + sourcePath,
                        true, npcInfo);
                    CleanupTempFile(sourcePath, extractedFromBsa);
                    return false;
                }
            }

            // ── Ensure FaceGen NIF uses SSE-native block types ──
            //
            // Modded FaceGen NIFs (e.g., Bijin) may ship in Oldrim format with
            // NiTriShape + NiTriShapeData blocks. Cloned shapes from optimized
            // model NIFs use BSDynamicTriShape. Mixing both formats in one NIF
            // causes CTDs in SSE. Convert the entire FaceGen NIF to SSE format
            // before any swap operations so all shapes are BSDynamicTriShape.
            //
            // This is primarily needed for head part swapping (which clones shapes),
            // but we do it unconditionally to keep the output consistent.

            if (!nif.GetHeader().GetVersion().IsSSE())
            {
                using var optOptions = new OptOptions();
                optOptions.targetVersion = NiVersion.getSSE();
                optOptions.headParts = true;
                nif.OptimizeFor(optOptions);

                _logger.LogReport(
                    "FaceGenPatcher: Optimized FaceGen NIF to SSE format (Oldrim → SSE): " + sourcePath,
                    false, npcInfo);
                DebugLog(npcInfo, "FaceGen NIF was Oldrim format — optimized to SSE.");
            }

            // Dump FaceGen NIF block structure for debug NPCs.
            if (IsDebugNpc(npcInfo))
            {
                DumpNifBlockStructure(nif, npcInfo, "INITIAL");
            }

            // ── Locate structural nodes ──

            NiNode faceGenSkinNode = FindFaceGenSkinNode(nif);
            DebugLog(npcInfo, "FindFaceGenSkinNode result: " + (faceGenSkinNode == null ? "NULL" : "found, name=\"" + (faceGenSkinNode.name?.get() ?? "") + "\""));

            if (faceGenSkinNode == null)
            {
                _logger.LogReport(
                    "FaceGenPatcher: No BSFaceGenNiNodeSkinned node found in: " + sourcePath,
                    false, npcInfo);
                CleanupTempFile(sourcePath, extractedFromBsa);
                return true;
            }

            // ──────────────────────────────────────────────────────────────────
            //  Phase A: Head Part Shape Swapping  (structural changes first)
            // ──────────────────────────────────────────────────────────────────
            //
            // Structural NIF modifications (shape deletion, CloneShape, bone
            // remapping) MUST happen before any texture slot writes.
            //
            // nifly re-indexes blocks on every DeleteShape / CloneShape call.
            // If we wrote texture data first and then deleted or cloned shapes,
            // the block index chain (NiShape → BSLightingShaderProperty →
            // BSShaderTextureSet) could shift or break, silently discarding the
            // texture changes. Running all structural changes first ensures the
            // block layout is stable before we touch texture data.
            //
            // The Face type is excluded from swapping (ExcludedTypes), so the
            // head skin shape that carries the face textures is never removed
            // or replaced here — it remains present for Phase B to modify.

            if (hasHeadPartWork)
            {
                DebugLog(npcInfo, "--- Phase A: Head Part Shape Swapping ---");
                foreach (var (type, headPartGetter) in validHeadPartAssignments)
                {
                    DebugLog(npcInfo, "--- SwapHeadPartType: type=" + type +
                        " editorId=" + (headPartGetter.EditorID ?? headPartGetter.FormKey.ToString()));
                    bool changed = SwapHeadPartType(nif, faceGenSkinNode, headPartGetter, type, npcInfo);
                    DebugLog(npcInfo, "--- SwapHeadPartType result: changed=" + changed);
                    anyChanges |= changed;
                }
            }

            // ──────────────────────────────────────────────────────────────────
            //  Phase B: Face Texture Baking  (data writes on stable layout)
            // ──────────────────────────────────────────────────────────────────
            //
            // Now that the NIF's block layout is final (all shapes deleted /
            // cloned / reindexed), it is safe to locate the head skin shape
            // and write texture slot overrides into its BSShaderTextureSet.
            //
            // IMPORTANT: The shape search, SetTextureSlot calls, and Save must
            // all happen within the same GetShapes() scope. The SWIG wrapper
            // for vectorNiShape may invalidate shape references on Dispose,
            // which would discard texture modifications if Save runs after
            // the shapes vector is disposed.  This matches the structure of
            // the original standalone FaceGenPatcher which kept everything in
            // one method / one `using var shapes` scope.

            // Open a single GetShapes() scope that covers both Phase B and Save.
            using var phaseBShapes = hasTextureWork ? nif.GetShapes() : null;

            if (hasTextureWork && phaseBShapes != null)
            {
                DebugLog(npcInfo, "--- Phase B: Face Texture Baking ---");

                // ── Find the head shape via structural traversal ──

                NiShape headShape = null;
                NiShape sbp30Fallback = null;
                int shapeIndex = 0;

                foreach (var shape in phaseBShapes)
                {
                    string shapeName = shape.name?.get() ?? "(null)";
                    string blockType = GetBlockTypeName(nif, shape);

                    // Gate 1: shape must be a child of BSFaceGenNiNodeSkinned.
                    if (!IsUnderFaceGenSkinNode(nif, shape))
                    {
                        DebugLog(npcInfo, "  Shape[" + shapeIndex + "] \"" + shapeName +
                            "\" (" + blockType + ") — skipped: not under BSFaceGenNiNodeSkinned");
                        shapeIndex++;
                        continue;
                    }

                    // Gate 2: shape must have a head dismember partition.
                    if (!HasHeadDismemberPartition(nif, shape, out bool is230))
                    {
                        var actualParts = GetDismemberBodyParts(nif, shape);
                        DebugLog(npcInfo, "  Shape[" + shapeIndex + "] \"" + shapeName +
                            "\" (" + blockType + ") — skipped: no head partition. Actual partitions=[" +
                            string.Join(",", actualParts) + "]");
                        shapeIndex++;
                        continue;
                    }

                    DebugLog(npcInfo, "  Shape[" + shapeIndex + "] \"" + shapeName +
                        "\" (" + blockType + ") — " + (is230 ? "SBP_230_HEAD (exact match)" : "SBP_30_HEAD (fallback candidate)"));

                    if (is230)
                    {
                        headShape = shape;
                        break;
                    }

                    sbp30Fallback ??= shape;
                    shapeIndex++;
                }

                headShape ??= sbp30Fallback;

                if (headShape == null)
                {
                    _logger.LogReport(
                        "FaceGenPatcher: No head shape (SBP_230/30_HEAD under BSFaceGenNiNodeSkinned) found in: "
                        + sourcePath,
                        false, npcInfo);
                    DebugLog(npcInfo, "Phase B: FAILED — no head shape found");
                }
                else
                {
                    string headShapeName = headShape.name?.get() ?? "(null)";
                    DebugLog(npcInfo, "Phase B: Using head shape \"" + headShapeName + "\"");

                    var shader = nif.GetShader(headShape);
                    if (shader == null)
                    {
                        _logger.LogReport(
                            "FaceGenPatcher: Head shape has no shader in: " + sourcePath,
                            false, npcInfo);
                        DebugLog(npcInfo, "Phase B: FAILED — head shape \"" +
                            headShapeName + "\" has no shader");
                    }
                    else
                    {
                        // NOTE: nifly's GetTextureSlot uses a std::string& output parameter
                        // that SWIG wraps by-value. Reads always return "". We cannot verify
                        // writes; use NifSkope to inspect outputs.

                        foreach (var (slot, newTexturePath) in textureSlotAssignments)
                        {
                            DebugLog(npcInfo, "  SetTextureSlot: slot " + slot +
                                " -> \"" + newTexturePath + "\"");

                            nif.SetTextureSlot(headShape, newTexturePath, (uint)slot);

                            _logger.LogReport(
                                "FaceGenPatcher: Slot " + slot + " -> \"" + newTexturePath + "\"",
                                false, npcInfo);

                            anyChanges = true;
                        }

                        DebugLog(npcInfo, "Phase B: done. anyChanges=" + anyChanges);
                    }
                }
            }

            // ── Save the modified NIF ──
            //
            // headShape (from phaseBShapes) is still alive here — the using
            // scope for phaseBShapes doesn't end until after Save completes.

            DebugLog(npcInfo, "All modifications done. anyChanges=" + anyChanges);

            if (anyChanges)
            {
                // Dump final block structure for debug NPCs before saving.
                if (IsDebugNpc(npcInfo))
                {
                    DumpNifBlockStructure(nif, npcInfo, "FINAL (before save)");
                }

                string outputDir = Path.GetDirectoryName(outputPath);
                if (!string.IsNullOrEmpty(outputDir))
                {
                    Directory.CreateDirectory(outputDir);
                }

                // Tag the output NIF so future runs can detect it as a SynthEBD output,
                // even if the user changes their output folder path between runs.
                nif.GetHeader().SetExportInfo(SynthEBDNifTag);

                int saveResult = nif.Save(outputPath);
                DebugLog(npcInfo, "Save result: " + saveResult + " path=" + outputPath);
                if (saveResult != 0)
                {
                    _logger.LogError(
                        "FaceGenPatcher: nifly failed to save NIF (error " + saveResult + "): " + outputPath);
                }
                else
                {
                    _logger.LogReport(
                        "FaceGenPatcher: Saved modified FaceGen NIF to: " + outputPath,
                        false, npcInfo);
                }
            }
        }

        // Clean up temporary BSA-extracted file.
        CleanupTempFile(sourcePath, extractedFromBsa);

        return true;
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  FACE TEXTURE BAKING
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Collects face texture slot assignments from asset containers.
    /// Maps FilePathReplacement.Destination strings to BSShaderTextureSet slot
    /// indices and texture paths.
    /// </summary>
    private Dictionary<int, string> CollectFaceTextureAssignments(
        List<Patcher.SelectedAssetContainer> assetContainers,
        NPCInfo npcInfo)
    {
        var result = new Dictionary<int, string>();

        DebugLog(npcInfo, "CollectFaceTextureAssignments: " + assetContainers.Count + " container(s)");

        foreach (var container in assetContainers)
        {
            DebugLog(npcInfo, "  Container: \"" + container.AssetPackName +
                "\" (" + container.LoggingLabel + ") — " + container.Paths.Count + " path(s)");

            foreach (var path in container.Paths)
            {
                bool isHeadSlot = HeadTextureDestinationToSlot.TryGetValue(path.Destination, out int slot);

                DebugLog(npcInfo, "    Path: Destination=\"" + path.Destination +
                    "\" Source=\"" + (path.Source ?? "(null)") +
                    "\" → " + (isHeadSlot ? "MATCH slot " + slot : "no match (not a head texture destination)"));

                if (isHeadSlot)
                {
                    if (!string.IsNullOrWhiteSpace(path.Source))
                    {
                        if (result.ContainsKey(slot))
                        {
                            DebugLog(npcInfo, "      Slot " + slot + " already assigned \"" +
                                result[slot] + "\" — overwriting with \"" + path.Source + "\"");
                        }
                        result[slot] = path.Source;
                    }
                    else
                    {
                        DebugLog(npcInfo, "      Slot " + slot + " matched but Source is null/empty — skipping");
                    }
                }
            }
        }

        DebugLog(npcInfo, "CollectFaceTextureAssignments result: " + result.Count + " slot(s)");
        foreach (var (slot, texPath) in result)
        {
            DebugLog(npcInfo, "  Slot " + slot + " = \"" + texPath + "\"");
        }

        return result;
    }

    /// <summary>
    /// Checks whether a shape's parent node is "BSFaceGenNiNodeSkinned",
    /// which is the NIF subtree that contains all face geometry.
    /// Used by face texture baking to locate the head skin shape.
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
        string parentName = parentNode.name.get();
        if (parentName == "BSFaceGenNiNodeSkinned")
        {
            return true;
        }

        // Fallback: check the block type via the header in case the NIF stores
        // BSFaceGenNiNodeSkinned as a distinct type rather than a named NiNode.
        return IsBlockType(nif, parentNode, "BSFaceGenNiNodeSkinned");
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

    // ═══════════════════════════════════════════════════════════════════════════
    //  HEAD PART SWAPPING — Assignment Resolution
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Filters and resolves head part FormKey assignments into validated
    /// IHeadPartGetter records, excluding types that shouldn't be NIF-swapped.
    /// </summary>
    private List<(HeadPart.TypeEnum Type, IHeadPartGetter HeadPart)> ResolveHeadPartAssignments(
        Dictionary<HeadPart.TypeEnum, FormKey> assignments)
    {
        var result = new List<(HeadPart.TypeEnum, IHeadPartGetter)>();

        foreach (var (type, formKey) in assignments)
        {
            if (ExcludedTypes.Contains(type))
            {
                _logger.LogReport(
                    "FaceGenPatcher: Skipping " + type + " — excluded from NIF swapping (use face texture patching for face textures).",
                    false, null);
                continue;
            }

            if (formKey.IsNull) continue;

            if (!_environmentProvider.LinkCache.TryResolve<IHeadPartGetter>(formKey, out var headPartGetter))
            {
                _logger.LogMessage(
                    "FaceGenPatcher: Could not resolve head part " + formKey + " for type " + type);
                continue;
            }

            if (headPartGetter.Model?.File == null || string.IsNullOrWhiteSpace(headPartGetter.Model.File))
            {
                _logger.LogMessage(
                    "FaceGenPatcher: Head part " + formKey + " (" + type + ") has no model NIF path.");
                continue;
            }

            result.Add((type, headPartGetter));
        }

        return result;
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  HEAD PART SWAPPING — Per-Type Processing
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

        // ── Capture the NPC-specific hair tint color before removing anything ──
        //
        // Hair shapes in the FaceGen NIF carry an NPC-specific hair tint in their
        // BSLightingShaderProperty (shader type HAIRTINT). The cloned replacement
        // shapes bring the model's default tint (typically very dark). Capturing
        // the original tint here lets us forward it to the cloned shapes so the
        // NPC keeps their intended hair color.

        (float R, float G, float B)? capturedHairTint = null;
        if (type == HeadPart.TypeEnum.Hair || type == HeadPart.TypeEnum.Eyebrows)
        {
            capturedHairTint = CaptureHairTintColor(faceGenNif, faceGenSkinNode, npcInfo);
        }

        // Process the main head part model.
        // Use the headpart's EditorID as the shape name in the FaceGen NIF — the game
        // matches headpart records to FaceGen geometry by name, so the shape must be
        // named after the EditorID, not whatever the source mesh calls it (e.g. "group_0").
        string mainEditorId = headPartGetter.EditorID ?? headPartGetter.FormKey.ToString();

        // performRemoval=true: the main headpart's removal pass uses the union of the
        // type's expected partitions AND the incoming model's partitions, which covers
        // both the main shape and any extra parts. Extra parts must NOT re-trigger
        // removal, or they'll delete the main shape we just cloned.
        anyChanges |= SwapSingleHeadPartModel(faceGenNif, faceGenSkinNode, headPartGetter, type, npcInfo, mainEditorId, performRemoval: true, capturedHairTint: capturedHairTint);

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
                        "FaceGenPatcher: Processing ExtraPart " + extraEditorId + " for " + type,
                        false, npcInfo);

                    // performRemoval=false: extra parts must not trigger removal, as the
                    // main headpart's removal pass already cleared conflicting shapes.
                    anyChanges |= SwapSingleHeadPartModel(faceGenNif, faceGenSkinNode, extraPartGetter, type, npcInfo, extraEditorId, performRemoval: false, capturedHairTint: capturedHairTint);
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
        bool performRemoval,
        (float R, float G, float B)? capturedHairTint = null)
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
                    "FaceGenPatcher: Model NIF not found: " + modelRelPath +
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
                    "FaceGenPatcher: Failed to load model NIF (error " + loadResult + "): " + modelAbsPath);
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
                    "FaceGenPatcher: Optimized model NIF to SSE format (NiTriShape → BSDynamicTriShape): " + modelRelPath,
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
                        "FaceGenPatcher: Promoted model NIF shapes to BSDynamicTriShape: " + modelRelPath,
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
                    "FaceGenPatcher: No shapes found in model: " + modelRelPath,
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
                        "FaceGenPatcher: CloneShape returned null for shape \"" + shapeInfo.Name +
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

                // ── Forward captured hair tint color to the cloned shape ──
                if (capturedHairTint.HasValue)
                {
                    ApplyHairTintToClonedShape(faceGenNif, clonedShape, capturedHairTint.Value, npcInfo);
                }

                _logger.LogReport(
                    "FaceGenPatcher: Cloned shape \"" + shapeInfo.Name + "\" as \"" + destShapeName +
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
    //  HEAD PART SWAPPING — Shape Collection and Partition Reading
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
    //  HEAD PART SWAPPING — Hair Tint Color Forwarding
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Scans shapes under BSFaceGenNiNodeSkinned for a BSLightingShaderProperty
    /// with shader type BSLSP_HAIRTINT and captures the hairTintColor vector.
    ///
    /// Returns the captured color as (r, g, b) floats, or null if no hair tint
    /// shader was found. Uses a tuple to avoid lifetime issues with nifly's
    /// Vector3 SWIG wrapper (which may be invalidated when shapes are deleted).
    /// </summary>
    private (float R, float G, float B)? CaptureHairTintColor(
        NifFile faceGenNif,
        NiNode faceGenSkinNode,
        NPCInfo npcInfo)
    {
        NiHeader header = faceGenNif.GetHeader();

        using var shapes = faceGenNif.GetShapes();
        foreach (var shape in shapes)
        {
            // Only consider shapes parented to BSFaceGenNiNodeSkinned.
            var parent = faceGenNif.GetParentNode(shape);
            if (parent == null) continue;

            string parentName = parent.name.get();
            if (parentName != "BSFaceGenNiNodeSkinned" &&
                !IsBlockType(faceGenNif, parent, "BSFaceGenNiNodeSkinned"))
                continue;

            // Get the shape's shader property.
            NiBlockRefNiShader shaderRef = shape.ShaderPropertyRef();
            if (shaderRef == null || shaderRef.IsEmpty()) continue;

            NiObject shaderObj = header.GetBlockById(shaderRef.index);
            if (shaderObj is not BSLightingShaderProperty bslsp) continue;

            if (bslsp.bslspShaderType != (uint)BSLightingShaderPropertyShaderType.BSLSP_HAIRTINT) continue;

            Vector3 tint = bslsp.hairTintColor;
            if (tint == null) continue;

            DebugLog(npcInfo, "  CaptureHairTintColor: Found on \"" +
                (shape.name?.get() ?? "unnamed") + "\" = (" +
                tint.x + ", " + tint.y + ", " + tint.z + ")");

            return (tint.x, tint.y, tint.z);
        }

        DebugLog(npcInfo, "  CaptureHairTintColor: No HAIRTINT shader found.");
        return null;
    }

    /// <summary>
    /// Applies a previously captured hair tint color to a cloned shape's
    /// BSLightingShaderProperty, if it uses the BSLSP_HAIRTINT shader type.
    /// </summary>
    private void ApplyHairTintToClonedShape(
        NifFile faceGenNif,
        NiShape clonedShape,
        (float R, float G, float B) tint,
        NPCInfo npcInfo)
    {
        NiHeader header = faceGenNif.GetHeader();

        NiBlockRefNiShader shaderRef = clonedShape.ShaderPropertyRef();
        if (shaderRef == null || shaderRef.IsEmpty()) return;

        NiObject shaderObj = header.GetBlockById(shaderRef.index);
        if (shaderObj is not BSLightingShaderProperty bslsp) return;

        if (bslsp.bslspShaderType != (uint)BSLightingShaderPropertyShaderType.BSLSP_HAIRTINT) return;

        bslsp.hairTintColor = new Vector3(tint.R, tint.G, tint.B);

        DebugLog(npcInfo, "  ApplyHairTintToClonedShape: Set \"" +
            (clonedShape.name?.get() ?? "unnamed") + "\" tint to (" +
            tint.R + ", " + tint.G + ", " + tint.B + ")");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  HEAD PART SWAPPING — Remove Conflicting Shapes
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
                // Also check by block type (same fallback as face texture patching).
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
                "FaceGenPatcher: Removed existing shape \"" + name + "\" (partition conflict).",
                false, npcInfo);
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  HEAD PART SWAPPING — Bone Remapping
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
                "FaceGenPatcher: Source shape has no bones, skipping bone remap.",
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
                    "FaceGenPatcher: Mapping source root \"" + sourceRootName +
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
                    "FaceGenPatcher: Bone \"" + boneName +
                    "\" not found in FaceGen NIF, mapping to " + skinNodeName + ".",
                    true, npcInfo);
            }
        }

        // Apply the remapped bone IDs to the cloned shape.
        faceGenNif.SetShapeBoneIDList(clonedShape, remappedBoneIds);

        _logger.LogReport(
            "FaceGenPatcher: Remapped " + sourceBoneNames.Count + " bone(s)" +
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
                "FaceGenPatcher: Fixed skeleton root: block " + oldTargetId +
                " -> block " + skinNodeBlockId + " (" + skinNodeName + ").",
                false, npcInfo);
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  NIF STRUCTURAL HELPERS
    // ═══════════════════════════════════════════════════════════════════════════

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
    /// Uses the confirmed traversal pattern: iterate shapes via GetShapes(),
    /// call GetParentNode() on each, and check the parent's name (via
    /// NiStringRef.get()) and block type (via header).
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
            // than a named NiNode.
            if (IsBlockType(nif, parentNode, "BSFaceGenNiNodeSkinned"))
            {
                return parentNode;
            }
        }

        return null;
    }

    /// <summary>
    /// Dumps the NIF's block structure to the debug log for diagnostic purposes.
    /// </summary>
    private void DumpNifBlockStructure(NifFile nif, NPCInfo npcInfo, string label)
    {
        var dbgHeader = nif.GetHeader();
        uint numBlocks = dbgHeader.GetNumBlocks();
        DebugLog(npcInfo, "=== " + label + " FaceGen NIF block structure === blocks=" + numBlocks);
        for (uint bi = 0; bi < numBlocks && bi < 80; bi++)
        {
            string btype = dbgHeader.GetBlockTypeStringById(bi);
            NiObject bobj = dbgHeader.GetBlockById(bi);
            string bname = "";
            try { if (bobj is NiObjectNET named) bname = named.name?.get() ?? ""; } catch { }
            DebugLog(npcInfo, "  Block[" + bi + "] " + btype + " \"" + bname + "\"");
        }
        using var dbgNodes = nif.GetNodes();
        DebugLog(npcInfo, "Node count: " + dbgNodes.Count);
        foreach (var dn in dbgNodes)
        {
            string nn = dn.name?.get() ?? "(null)";
            uint nid = dbgHeader.GetBlockID(dn);
            DebugLog(npcInfo, "  Node: \"" + nn + "\" blockId=" + nid);
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  PATH RESOLUTION
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Builds the absolute path to an NPC's FaceGen NIF under a given root folder.
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
    //  UTILITY
    // ═══════════════════════════════════════════════════════════════════════════

    private void LogAndPrint(string message, bool triggerSave, NPCInfo npcInfo)
    {
        _logger.LogReport(message, triggerSave, npcInfo);
        _logger.LogMessage(message);
    }

    /// <summary>
    /// Cleans up a temporary BSA-extracted file if applicable.
    /// </summary>
    private void CleanupTempFile(string path, bool wasExtracted)
    {
        if (wasExtracted && !string.IsNullOrEmpty(path) && File.Exists(path))
        {
            try { File.Delete(path); }
            catch (Exception ex)
            {
                _logger.LogMessage("Warning: Could not clean up temp file: " + path + " — " + ex.Message);
            }
        }
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