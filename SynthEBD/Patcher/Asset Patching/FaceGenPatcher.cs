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
///           remove existing shapes whose names match head part EditorIDs of the
///           same type on the NPC record
///        b. For ADDITIVE types (Scars, Misc):
///           skip removal — multiple head parts of the same type may coexist
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

    // ═══════════════════════════════════════════════════════════════════════════
    //  DEPENDENCIES
    // ═══════════════════════════════════════════════════════════════════════════

    private readonly IOutputEnvironmentStateProvider _environmentProvider;
    private readonly PatcherState _patcherState;
    private readonly SynthEBDPaths _paths;
    private readonly BSAHandler _bsaHandler;
    private readonly Logger _logger;
    private readonly SurrogateNPCProvider _surrogateNpcProvider;

    // ─── Head part model validation cache ────────────────────────────────────
    //
    // Caches the result of PreValidateHeadPartModels by the top-level head part's
    // FormKey. Since many NPCs share the same head part assignment, this avoids
    // redundant file-existence checks and BSA searches for every NPC.
    //
    // Values:
    //   non-null list  → validation passed; contains pre-resolved model paths
    //   null           → validation failed; all models for this head part are known missing
    //
    // BSA-extracted temp files referenced by cached entries are NOT cleaned up
    // per-NPC — they persist in the temp folder for the duration of the run.
    // This is safe because the temp folder is ephemeral between runs.
    private readonly Dictionary<FormKey, List<ResolvedHeadPartModel>> _headPartValidationCache = new();

    // ─── Model NIF cache ─────────────────────────────────────────────────────
    //
    // Caches loaded and SSE-optimized NifFile instances by absolute model path.
    // SwapSingleHeadPartModel is called ~12K times across 3500 NPCs, but the
    // number of unique model NIFs is much smaller (a few hundred at most).
    // CloneShape reads from the source NIF without mutating it, so sharing a
    // single loaded NifFile across all NPCs that reference the same head part
    // model is safe.
    //
    // IMPORTANT: NifFile wraps native SWIG memory. All cached entries must be
    // disposed at the end of the patching run via DisposeCaches().
    private readonly Dictionary<string, NifFile> _modelNifCache
        = new(StringComparer.OrdinalIgnoreCase);

    // ─── Parsed .tri file cache ──────────────────────────────────────────────
    //
    // Caches parsed TriFileData by absolute .tri path. The same CharGen .tri
    // (e.g. EyesFemaleChargen.tri) is parsed for every NPC that gets an eye
    // swap — thousands of redundant binary file reads. TriFileData is a pure
    // managed object with no native handles, so sharing is entirely safe.
    private readonly Dictionary<string, TriFileParser.TriFileData> _triFileCache
        = new(StringComparer.OrdinalIgnoreCase);

    // ─── CharGen .tri path resolution cache ──────────────────────────────────
    //
    // Caches the result of ResolveCharGenTriPath by the model's relative NIF
    // path. The .tri path depends only on the model path and head part type,
    // not on the NPC, so the result is shared across all NPCs that reference
    // the same head part model. null values are cached intentionally to avoid
    // re-searching for .tri files that are known to not exist (common for
    // modded hair like KS Hairdos).
    private readonly Dictionary<string, string> _triPathCache
        = new(StringComparer.OrdinalIgnoreCase);

    // ─── Debug tracing for specific NPCs ────────────────────────────────────
    // Set of NPC FormKeys that get verbose diagnostic logging at every step.
    // Remove or clear this set once debugging is complete.
    private static readonly HashSet<FormKey> DebugFormKeys = new()
    {
        //Mutagen.Bethesda.FormKeys.SkyrimSE.Skyrim.Npc.Uthgerd.FormKey,
        //Mutagen.Bethesda.FormKeys.SkyrimSE.Skyrim.Npc.Saadia.FormKey,
        //Mutagen.Bethesda.FormKeys.SkyrimSE.Skyrim.Npc.Hulda.FormKey,
        //Mutagen.Bethesda.FormKeys.SkyrimSE.Skyrim.Npc.Ysolda.FormKey,
        //Mutagen.Bethesda.FormKeys.SkyrimSE.Skyrim.Npc.IdolafBattleBorn.FormKey
        Mutagen.Bethesda.FormKeys.SkyrimSE.Skyrim.Npc.OlfridBattleBorn.FormKey
    };

    private bool IsDebugNpc(NPCInfo npcInfo)
    {
        return npcInfo?.NPC?.FormKey != null &&
               DebugFormKeys.Contains(npcInfo.OriginalNPC.FormKey);
    }

    private void DebugLog(NPCInfo npcInfo, string message)
    {
        if (LOGFACEGENDEBUG && IsDebugNpc(npcInfo))
        {
            _logger.LogMessage("[DEBUG-FGPATCH " + npcInfo.NPC.FormKey + "] " + message);
        }
    }

    private void WarnLog(NPCInfo npcInfo, string message)
    {
        _logger.LogMessage("[WARN-FGPATCH " + npcInfo.NPC.FormKey + "] " + message);
    }
    
    // ═══════════════════════════════════════════════════════════════════════════
    //  PROFILING FRAMEWORK
    // ═══════════════════════════════════════════════════════════════════════════

    private const bool LOGFACEGENDEBUG = false; // Toggle this to turn verbose FaceGen debug logging on/off

    private const bool EnableProfiling = true; // Toggle this to turn performance logging on/off

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, (long TotalTicks, int CallCount)> _profileStats = new();

    /// <summary>
    /// Starts a profiling session for a specific block or method.
    /// Dispose the returned object to end the session and record the execution time.
    /// </summary>
    /// <param name="name">The name of the method or block being profiled.</param>
    /// <returns>An IDisposable scope that stops the stopwatch upon disposal, or null if profiling is disabled.</returns>
    private IDisposable ProfileBlock(string name)
    {
        if (!EnableProfiling) return null;
        return new ProfilerScope(name);
    }

    /// <summary>
    /// A lightweight disposable wrapper that utilizes a Stopwatch to track execution time.
    /// </summary>
    private class ProfilerScope : IDisposable
    {
        private readonly string _name;
        private readonly System.Diagnostics.Stopwatch _sw;

        public ProfilerScope(string name)
        {
            _name = name;
            _sw = System.Diagnostics.Stopwatch.StartNew();
        }

        public void Dispose()
        {
            _sw.Stop();
            _profileStats.AddOrUpdate(
                _name,
                _ => (_sw.ElapsedTicks, 1),
                (_, current) => (current.TotalTicks + _sw.ElapsedTicks, current.CallCount + 1));
        }
    }

    /// <summary>
    /// Writes the accumulated profiling statistics to the logger, calculating average execution times.
    /// </summary>
    public void DumpProfilingStats()
    {
        if (!EnableProfiling || _profileStats.IsEmpty) return;

        _logger.LogMessage("=== FaceGenPatcher Profiling Stats ===");
        _logger.LogMessage(string.Format("{0,-35} | {1,-10} | {2,-12} | {3,-12}", "Method", "Calls", "Total (ms)", "Avg (ms)"));
        _logger.LogMessage(new string('-', 78));

        foreach (var kvp in _profileStats.OrderByDescending(x => x.Value.TotalTicks))
        {
            double totalMs = TimeSpan.FromTicks(kvp.Value.TotalTicks).TotalMilliseconds;
            double avgMs = totalMs / kvp.Value.CallCount;
            _logger.LogMessage(string.Format("{0,-35} | {1,-10} | {2,-12:F2} | {3,-12:F4}", 
                kvp.Key, kvp.Value.CallCount, totalMs, avgMs));
        }
        _logger.LogMessage(new string('=', 78));
    }

    /// <summary>
    /// Disposes all cached NifFile instances and clears in-memory caches.
    ///
    /// MUST be called after the FaceGen patching loop completes. NifFile wraps
    /// native SWIG memory via C++/CLI pointers — failing to dispose these will
    /// leak unmanaged memory proportional to the number of unique head part
    /// models in the load order.
    ///
    /// The .tri and path caches are pure managed objects and don't strictly
    /// require disposal, but clearing them releases GC pressure from the large
    /// string sets.
    /// </summary>
    public void DisposeCaches()
    {
        int modelCount = _modelNifCache.Count;
        foreach (var kvp in _modelNifCache)
        {
            try { kvp.Value?.Dispose(); }
            catch { /* best effort — native destructor may throw on corrupted state */ }
        }
        _modelNifCache.Clear();

        int triCount = _triFileCache.Count;
        _triFileCache.Clear();

        int triPathCount = _triPathCache.Count;
        _triPathCache.Clear();

        // Don't clear _headPartValidationCache — it doesn't hold native resources
        // and may be useful for diagnostics after the run.

        _logger.LogMessage("FaceGenPatcher caches disposed: " +
            modelCount + " model NIF(s), " +
            triCount + " parsed .tri file(s), " +
            triPathCount + " .tri path resolution(s).");
    }

    public FaceGenPatcher(
        IOutputEnvironmentStateProvider environmentProvider,
        PatcherState patcherState,
        SynthEBDPaths paths,
        BSAHandler bsaHandler,
        Logger logger,
        SurrogateNPCProvider surrogateNpcProvider)
    {
        _environmentProvider = environmentProvider;
        _patcherState = patcherState;
        _paths = paths;
        _bsaHandler = bsaHandler;
        _logger = logger;
        _surrogateNpcProvider = surrogateNpcProvider;
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  CACHE LIFECYCLE
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Loads the BSA file index cache from disk. Call this once BEFORE
    /// the FaceGen patching loop to enable deferred BSA reader opening.
    /// The cache is stored in the temp extraction folder.
    /// </summary>
    public void LoadBsaIndexCache()
    {
        string cachePath = GetBsaCacheFilePath();
        if (cachePath != null)
        {
            _bsaHandler.LoadBsaIndexCache(cachePath);
        }
    }

    /// <summary>
    /// Saves the BSA file index cache to disk. Call this once AFTER
    /// the FaceGen patching loop to persist any newly-indexed BSAs.
    /// </summary>
    public void SaveBsaIndexCache()
    {
        string cachePath = GetBsaCacheFilePath();
        if (cachePath != null)
        {
            _bsaHandler.SaveBsaIndexCache(cachePath);
        }
    }

    private string GetBsaCacheFilePath()
    {
        string tempFolder = _patcherState.ModManagerSettings.TempExtractionFolder;
        if (string.IsNullOrEmpty(tempFolder))
            return null;

        // Store the cache file one level up from the temp extraction folder,
        // in a predictable location that persists between runs.
        string parentDir = Path.GetDirectoryName(tempFolder);
        if (string.IsNullOrEmpty(parentDir))
            parentDir = tempFolder;

        return Path.Combine(parentDir, "SynthEBD_BsaIndexCache.json");
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
    /// <param name="outputFormKey">
    ///   When non-null, the output FaceGen NIF is saved to the path corresponding to 
    ///   this FormKey (the surrogate NPC) instead of the original NPC's FormKey.
    ///   The source NIF is still read from the original NPC's path via OriginalNPC.
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
        Dictionary<HeadPart.TypeEnum, FormKey> headPartAssignments,
        FormKey? outputFormKey = null)
    {
        using (ProfileBlock(nameof(PatchFaceGenNif)))
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

            // Remove head parts that failed validation from the caller's dictionary
            // so ApplyHeadPartRecords won't write them to the NPC record (which would
            // cause a record/NIF mismatch → dark face bug). Must run unconditionally —
            // if ALL assignments fail, hasHeadPartWork will be false and the Phase A
            // failedTypes cleanup (which is gated on hasHeadPartWork) would be skipped.
            if (headPartAssignments != null && validHeadPartAssignments.Count < headPartAssignments.Count)
            {
                var validTypes = new HashSet<HeadPart.TypeEnum>(
                    validHeadPartAssignments.Select(v => v.Type));
                var invalidTypes = headPartAssignments.Keys
                    .Where(k => !validTypes.Contains(k) && !ExcludedTypes.Contains(k))
                    .ToList();
                foreach (var t in invalidTypes)
                {
                    headPartAssignments.Remove(t);
                    DebugLog(npcInfo, "Removed invalid type " + t +
                        " from headPartAssignments — failed validation.");
                }
            }

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

            // Source path: always uses OriginalNPC (handled by the fixed ResolveFaceGenNifPath)
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
                    string message = $"FaceGenPatcher: FaceGen NIF not found for {npcInfo.LogIDstring}.";
                    if (hasTextureWork) message += " Face textures cannot be baked.";
                    if (hasHeadPartWork) message += " Head parts cannot be baked.";
                    LogAndPrint(message, false, npcInfo);
                    return true;
                }
                DebugLog(npcInfo, "FaceGen extracted from BSA: " + sourcePath);
            }

            string outputPath;
            if (outputFormKey.HasValue && !outputFormKey.Value.IsNull)
            {
                // SkyPatcher mode: output nif goes to the surrogate NPC's path
                outputPath = ResolveFaceGenNifPathForFormKey(outputFormKey.Value, _paths.OutputDataFolder);
            }
            else
            {
                // Direct mode: output nif goes to the original NPC's path
                outputPath = ResolveFaceGenNifPath(npcInfo, _paths.OutputDataFolder);
            }

            DebugLog(npcInfo, "FaceGen output path: " + outputPath +
                              (outputFormKey.HasValue ? " (surrogate: " + outputFormKey.Value + ")" : " (original NPC)"));

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

                    // Build a map of shape name (EditorID) → type for all head parts
                    // currently on the NPC record. This lets the removal pass identify
                    // existing FaceGen shapes by their authoritative record type instead
                    // of relying on NIF partition data (which is absent on shapes using
                    // plain NiSkinInstance rather than BSDismemberSkinInstance).
                    var existingShapeTypes = BuildExistingHeadPartTypeMap(npcInfo);

                    // Track types that failed pre-validation so we can remove them from
                    // headPartAssignments. This prevents the caller from applying NPC
                    // record changes for head part types whose NIF swap was aborted.
                    var failedTypes = new List<HeadPart.TypeEnum>();

                    foreach (var (type, headPartGetter) in validHeadPartAssignments)
                    {
                        DebugLog(npcInfo, "--- SwapHeadPartType: type=" + type +
                            " editorId=" + (headPartGetter.EditorID ?? headPartGetter.FormKey.ToString()));
                        bool changed = SwapHeadPartType(nif, faceGenSkinNode, headPartGetter, type, npcInfo, existingShapeTypes);
                        DebugLog(npcInfo, "--- SwapHeadPartType result: changed=" + changed);
                        anyChanges |= changed;

                        if (!changed)
                        {
                            failedTypes.Add(type);
                        }
                    }

                    // Remove failed types from headPartAssignments so the caller
                    // (Patcher.ApplyHeadPartRecords) doesn't set NPC record entries
                    // for types whose NIF swap was aborted. This prevents the
                    // record/NIF mismatch that causes the dark face bug.
                    if (headPartAssignments != null)
                    {
                        foreach (var failedType in failedTypes)
                        {
                            headPartAssignments.Remove(failedType);
                            DebugLog(npcInfo, "Removed failed type " + failedType +
                                " from headPartAssignments to prevent record mismatch.");
                        }
                    }
                }

                // ──────────────────────────────────────────────────────────────────
                //  Phase C: Surrogate Shape Name Reconciliation
                // ──────────────────────────────────────────────────────────────────
                //
                // When outputting to a surrogate, head part sub-records may have been
                // duplicated with new EditorIDs. Rename NIF shapes to match so the
                // engine can pair head part records with their FaceGen geometry.
                //
                // Must run after Phase A (all shapes are cloned) but before Phase B
                // opens its GetShapes() scope, to avoid overlapping SWIG scopes.

                ReconcileSurrogateShapeNames(nif, faceGenSkinNode, npcInfo, headPartAssignments, outputFormKey);

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
                bool needsTintRemap = outputFormKey.HasValue && !outputFormKey.Value.IsNull;
                using var phaseBShapes = (hasTextureWork || needsTintRemap) ? nif.GetShapes() : null;

                NiShape headShape = null;

                if (phaseBShapes != null)
                {
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
                }

                // ── Phase B: Face Texture Baking ──

                if (hasTextureWork)
                {
                    DebugLog(npcInfo, "--- Phase B: Face Texture Baking ---");

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
                
                // ──────────────────────────────────────────────────────────────────
                //  Phase D: Surrogate Face Tint Remapping
                // ──────────────────────────────────────────────────────────────────
                //
                // The engine generates the FaceTint DDS lookup path at runtime from
                // the NPC's FormKey: FaceGenData\FaceTint\<plugin>\<formId>.dds.
                // For a surrogate, this resolves to a path that doesn't exist because
                // the tint was generated for the original NPC, not the surrogate.
                //
                // Fix: remap the tint texture slot (slot 6) in the NIF to use the
                // surrogate's expected tint path, and copy the original tint DDS to
                // that path so the engine can find it.

                if (needsTintRemap && headShape != null)
                {
                    var originalFk = npcInfo.OriginalNPC.FormKey;
                    var surrogateFk = outputFormKey.Value;

                    // Slot 6 = Subsurface Tint in BSLightingShaderMaterialFacegen
                    const uint FaceTintSlot = 6;

                    string surrogateTintRelPath = string.Join("\\",
                        "textures", "actors", "character",
                        "facegendata", "facetint",
                        surrogateFk.ModKey.FileName,
                        surrogateFk.ID.ToString("X8") + ".dds");

                    DebugLog(npcInfo, "Phase D: Remapping tint slot " + FaceTintSlot +
                                      " → \"" + surrogateTintRelPath + "\"");

                    nif.SetTextureSlot(headShape, surrogateTintRelPath, FaceTintSlot);
                    anyChanges = true;

                    // Copy (or extract from BSA) the original NPC's face tint DDS
                    // to the surrogate's expected path in the output folder.
                    CopySurrogateFaceTint(npcInfo, originalFk, surrogateFk);
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
                    
                    /*_logger.LogMessage("FaceGenPatcher SAVE: " + outputPath + 
                                       " | outputFormKey=" + (outputFormKey.HasValue ? outputFormKey.Value.ToString() : "null") +
                                       " | originalNPC=" + npcInfo.OriginalNPC.FormKey); */

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

            bool hasOwnModel = headPartGetter.Model?.File != null
                && !string.IsNullOrWhiteSpace(headPartGetter.Model.File);
            bool hasExtraParts = headPartGetter.ExtraParts != null && headPartGetter.ExtraParts.Count > 0;

            if (!hasOwnModel && !hasExtraParts)
            {
                _logger.LogMessage(
                    "FaceGenPatcher: Head part " + formKey + " (" + type + ") has no model NIF path and no ExtraParts.");
                continue;
            }

            result.Add((type, headPartGetter));
        }

        return result;
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  HEAD PART SWAPPING — Pre-Validation
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Resolves the absolute path for a head part's model NIF, checking:
    ///   1. Loose files in the Data folder
    ///   2. BSAs associated with the head part's defining/overriding plugins
    ///   3. ALL loaded BSAs (broad fallback for cross-mod mesh references)
    ///
    /// Returns the absolute path and whether it was extracted from a BSA,
    /// or (null, false) if the model cannot be found anywhere.
    /// </summary>
    private (string AbsPath, bool ExtractedFromBsa) ResolveModelNifToAbsPath(
        IHeadPartGetter headPartGetter, NPCInfo npcInfo)
    {
        string modelRelPath = headPartGetter.Model.File.DataRelativePath.Path;
        string modelAbsPath = ResolveModelNifPath(modelRelPath);

        if (File.Exists(modelAbsPath))
        {
            return (modelAbsPath, false);
        }

        // Not loose — try BSA extraction (plugin-scoped, then broad fallback).
        string extracted = TryExtractModelFromBsa(modelRelPath, headPartGetter, out bool wasExtracted);
        if (extracted != null)
        {
            return (extracted, wasExtracted);
        }

        return (null, false);
    }

    /// <summary>
    /// Pre-validates that ALL model NIFs required by a head part assignment
    /// (main model + all ExtraParts, recursively) are available on disk or
    /// in BSAs before any NIF editing begins.
    ///
    /// Results are cached by head part FormKey so that repeated assignments of
    /// the same head part across multiple NPCs only perform file/BSA lookups once.
    ///
    /// Returns a list of <see cref="ResolvedHeadPartModel"/> entries with
    /// pre-resolved absolute paths, or null if ANY model is missing. When
    /// null is returned, no NIF editing should take place — the entire swap
    /// for this head part type must be aborted to avoid the partial-swap
    /// state that causes the dark face bug.
    ///
    /// Any BSA-extracted temp files from a failed validation are cleaned up
    /// before returning null.
    /// </summary>
    private List<ResolvedHeadPartModel> PreValidateHeadPartModels(
        IHeadPartGetter headPartGetter,
        HeadPart.TypeEnum type,
        NPCInfo npcInfo)
    {
        FormKey cacheKey = headPartGetter.FormKey;

        // ── Cache hit: return previously computed result ──

        if (_headPartValidationCache.TryGetValue(cacheKey, out var cached))
        {
            if (cached == null)
            {
                // Known invalid — skip without re-logging the missing model message
                // (it was already logged on the first NPC that triggered validation).
                DebugLog(npcInfo, "PreValidate: CACHE HIT (invalid) for " +
                    (headPartGetter.EditorID ?? cacheKey.ToString()));
                return null;
            }

            DebugLog(npcInfo, "PreValidate: CACHE HIT (valid, " + cached.Count +
                " models) for " + (headPartGetter.EditorID ?? cacheKey.ToString()));

            // Return the cached entries but with ExtractedFromBsa = false so
            // the downstream SwapSingleHeadPartModel won't try to delete the
            // temp files — they're shared across all NPCs using this head part.
            return cached;
        }

        // ── Cache miss: perform full validation ──

        DebugLog(npcInfo, "PreValidate: CACHE MISS for " +
            (headPartGetter.EditorID ?? cacheKey.ToString()) + " — resolving models...");

        var results = PreValidateHeadPartModelsUncached(headPartGetter, type, npcInfo);

        // Store in cache. null = known invalid.
        _headPartValidationCache[cacheKey] = results;

        if (results != null)
        {
            // Mark all entries as non-extracted so future consumers (including
            // the current one) won't attempt cleanup. The temp files persist
            // for the duration of the patching run.
            foreach (var model in results)
            {
                model.ExtractedFromBsa = false;
            }
        }

        return results;
    }

    /// <summary>
    /// Uncached implementation of head part model validation.
    /// Called by <see cref="PreValidateHeadPartModels"/> on cache miss.
    /// </summary>
    private List<ResolvedHeadPartModel> PreValidateHeadPartModelsUncached(
        IHeadPartGetter headPartGetter,
        HeadPart.TypeEnum type,
        NPCInfo npcInfo)
    {
        var results = new List<ResolvedHeadPartModel>();

        string mainEditorId = headPartGetter.EditorID ?? headPartGetter.FormKey.ToString();

        // ── Validate main model (optional — head parts may be ExtraParts-only containers) ──

        bool hasOwnModel = headPartGetter.Model?.File != null
            && !string.IsNullOrWhiteSpace(headPartGetter.Model.File);

        if (hasOwnModel)
        {
            var (mainPath, mainExtracted) = ResolveModelNifToAbsPath(headPartGetter, npcInfo);
            if (mainPath == null)
            {
                LogAndPrint(
                    "FaceGenPatcher: Model NIF not found: " +
                    headPartGetter.Model.File.DataRelativePath.Path +
                    " for head part " + mainEditorId +
                    ". Aborting entire " + type + " swap for " + npcInfo.LogIDstring + ".",
                    true, npcInfo);
                return null;
            }

            results.Add(new ResolvedHeadPartModel
            {
                HeadPart = headPartGetter,
                EditorId = mainEditorId,
                ModelAbsPath = mainPath,
                ExtractedFromBsa = mainExtracted,
                IsMainPart = true,
            });

            DebugLog(npcInfo, "PreValidate: main model OK — " + mainEditorId + " -> " + mainPath);
        }
        else
        {
            DebugLog(npcInfo, "PreValidate: head part " + mainEditorId +
                " has no direct model — will process ExtraParts only.");
        }

        // ── Validate ExtraParts (recursively) ──

        if (headPartGetter.ExtraParts != null)
        {
            foreach (var extraPartLink in headPartGetter.ExtraParts)
            {
                if (!_environmentProvider.LinkCache.TryResolve(extraPartLink, out var extraPartGetter))
                    continue;

                if (extraPartGetter.Model?.File == null ||
                    string.IsNullOrWhiteSpace(extraPartGetter.Model.File))
                    continue;

                string extraEditorId = extraPartGetter.EditorID ?? extraPartGetter.FormKey.ToString();

                var (extraPath, extraExtracted) = ResolveModelNifToAbsPath(extraPartGetter, npcInfo);
                if (extraPath == null)
                {
                    LogAndPrint(
                        "FaceGenPatcher: Model NIF not found: " +
                        extraPartGetter.Model.File.DataRelativePath.Path +
                        " for extra part " + extraEditorId +
                        " (parent: " + mainEditorId + ")" +
                        ". Aborting entire " + type + " swap for " + npcInfo.LogIDstring + ".",
                        true, npcInfo);

                    // Clean up any BSA-extracted temp files from earlier in this validation.
                    CleanupResolvedModels(results);
                    return null;
                }

                results.Add(new ResolvedHeadPartModel
                {
                    HeadPart = extraPartGetter,
                    EditorId = extraEditorId,
                    ModelAbsPath = extraPath,
                    ExtractedFromBsa = extraExtracted,
                    IsMainPart = false,
                });

                DebugLog(npcInfo, "PreValidate: extra part OK — " + extraEditorId + " -> " + extraPath);
            }
        }

        // ── Ensure at least one model was resolved ──
        // A head part with no direct model AND no valid ExtraParts has nothing to swap.
        if (results.Count == 0)
        {
            LogAndPrint(
                "FaceGenPatcher: Head part " + mainEditorId + " has no model and no valid ExtraParts." +
                " Aborting " + type + " swap for " + npcInfo.LogIDstring + ".",
                true, npcInfo);
            return null;
        }

        DebugLog(npcInfo, "PreValidate: all " + results.Count + " model(s) validated for " + mainEditorId);
        return results;
    }

    /// <summary>
    /// Cleans up BSA-extracted temp files from a list of resolved models.
    /// Used when pre-validation fails partway through to avoid leaking temp files.
    /// </summary>
    private void CleanupResolvedModels(List<ResolvedHeadPartModel> models)
    {
        foreach (var model in models)
        {
            CleanupTempFile(model.ModelAbsPath, model.ExtractedFromBsa);
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  HEAD PART SWAPPING — Per-Type Processing
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Processes a single head part assignment: validates all required models exist,
    /// then removes conflicting shapes from the FaceGen NIF and clones new shapes in.
    /// Also recurses into ExtraParts.
    ///
    /// This is an all-or-nothing operation: if ANY model (main or extra) cannot be
    /// found, the entire swap for this head part type is aborted. No shapes are
    /// removed and no shapes are cloned, avoiding the partial-swap state where old
    /// shapes coexist with new ones and cause the dark face bug.
    /// </summary>
    private bool SwapHeadPartType(
        NifFile faceGenNif,
        NiNode faceGenSkinNode,
        IHeadPartGetter headPartGetter,
        HeadPart.TypeEnum type,
        NPCInfo npcInfo,
        Dictionary<string, HeadPart.TypeEnum> existingShapeTypes)
    {
        using (ProfileBlock(nameof(SwapHeadPartType)))
        {
            bool anyChanges = false;

            // ── Step 0: Pre-validate all models before touching the NIF ──
            //
            // Resolve paths for the main head part model AND all ExtraParts.
            // If ANY model is missing, abort the entire swap cleanly — no removal,
            // no cloning. This prevents the partial-swap state that causes dark face.

            var resolvedModels = PreValidateHeadPartModels(headPartGetter, type, npcInfo);
            if (resolvedModels == null)
            {
                WarnLog(npcInfo, "SwapHeadPartType: Pre-validation FAILED for " + type +
                    " — aborting entire swap (no NIF edits made).");
                return false;
            }

            // ── Capture the NPC-specific hair tint color before removing anything ──
            //
            // Hair, eyebrow, and facial hair shapes in the FaceGen NIF carry an
            // NPC-specific hair tint in their BSLightingShaderProperty (shader type
            // HAIRTINT). The cloned replacement shapes bring the model's default tint
            // (typically very dark). Capturing the original tint here lets us forward
            // it to the cloned shapes so the NPC keeps their intended color.

            (float R, float G, float B)? capturedHairTint = null;
            if (type == HeadPart.TypeEnum.Hair ||
                type == HeadPart.TypeEnum.Eyebrows ||
                type == HeadPart.TypeEnum.FacialHair)
            {
                capturedHairTint = CaptureHairTintColor(faceGenNif, faceGenSkinNode, npcInfo);
            }

            // ── Capture the NPC-specific bone transforms before removing anything ──
            //
            // The Creation Kit bakes NPC-specific vertex positions into every FaceGen
            // shape — not just eyes, but also eyebrows, facial hair, and scars. These
            // morphed vertices position each shape to fit the NPC's unique facial
            // geometry. The cloned replacement shapes bring the model's generic default
            // vertex positions, causing them to appear offset or misaligned. Capturing
            // the original transforms here lets us forward them to the cloned shapes.
            CapturedBoneTransforms capturedEyeTransforms = null;
            if (type == HeadPart.TypeEnum.Eyes ||
                type == HeadPart.TypeEnum.Eyebrows ||
                type == HeadPart.TypeEnum.FacialHair ||
                type == HeadPart.TypeEnum.Scars)
            {
                capturedEyeTransforms = CaptureEyeBoneTransforms(faceGenNif, faceGenSkinNode, existingShapeTypes, type, npcInfo);
            }

            // ── Capture the NPC-specific eye shader properties before removing anything ──
            //
            // Eye shapes in the FaceGen NIF carry NPC-specific shader properties
            // (specular color, glossiness, emissive, cubemap scale, reflection
            // centers, etc.) on their BSLSP_EYE BSLightingShaderProperty. The cloned
            // replacement shapes bring the model's default values, causing all NPCs
            // to end up with the same eye color. Capturing here lets us forward
            // the original values to the cloned shapes.
            CapturedEyeShaderProperties capturedEyeShader = null;
            if (type == HeadPart.TypeEnum.Eyes)
            {
                capturedEyeShader = CaptureEyeShaderProperties(faceGenNif, faceGenSkinNode, existingShapeTypes, npcInfo);
            }

            // ── Resolve the head part's TNAM texture set for eye texture baking ──
            //
            // Eye shapes cloned from the model NIF carry the model's default textures
            // (e.g., EyeBrown.dds) in their BSShaderTextureSet. The actual eye textures
            // are defined in the head part's TNAM record (a TXST texture set). At runtime,
            // the engine would normally resolve TNAM and apply those textures, but since
            // we're writing a pre-baked FaceGen NIF, the engine uses whatever textures
            // are already in the NIF. We must therefore bake the TNAM textures into the
            // cloned shape's BSShaderTextureSet ourselves.
            Dictionary<uint, string> resolvedEyeTextures = null;
            if (type == HeadPart.TypeEnum.Eyes)
            {
                resolvedEyeTextures = ResolveHeadPartTextureSet(headPartGetter, npcInfo);
            }

            // ── Remove conflicting shapes BEFORE any cloning ──
            //
            // Removal is now the caller's responsibility (moved out of SwapSingleHeadPartModel)
            // so it always executes regardless of individual model load outcomes. Since we've
            // already validated all models above, this is safe.

            if (SingularTypes.Contains(type))
            {
                RemoveShapesByHeadPartType(faceGenNif, faceGenSkinNode, type, existingShapeTypes, npcInfo);
            }

            // ── Clone all pre-validated models into the FaceGen NIF ──
            //
            // Iterate the resolved models (main first, then extras) and clone each one.
            // All models were validated above so failures here are unexpected — but if one
            // does fail (e.g., corrupt NIF), we log it and continue with the rest rather
            // than leaving a half-removed NIF.

            foreach (var resolved in resolvedModels)
            {
                if (!resolved.IsMainPart)
                {
                    _logger.LogReport(
                        "FaceGenPatcher: Processing ExtraPart " + resolved.EditorId + " for " + type,
                        false, npcInfo);
                }

                anyChanges |= SwapSingleHeadPartModel(
                    faceGenNif, faceGenSkinNode, resolved.HeadPart, type, npcInfo,
                    resolved.EditorId,
                    resolvedModelAbsPath: resolved.ModelAbsPath,
                    resolvedModelExtracted: resolved.ExtractedFromBsa,
                    isMainPart: resolved.IsMainPart,
                    existingShapeTypes: existingShapeTypes,
                    capturedHairTint: capturedHairTint,
                    capturedEyeTransforms: capturedEyeTransforms,
                    capturedEyeShader: capturedEyeShader,
                    resolvedEyeTextures: resolvedEyeTextures);
            }

            return anyChanges;
        }
    }
    
    /// <summary>
    /// Opens a single head part model NIF and clones its shapes into
    /// BSFaceGenNiNodeSkinned.
    ///
    /// Shape removal is NOT performed here — it is the caller's responsibility
    /// (SwapHeadPartType handles removal before any cloning begins, ensuring
    /// the all-or-nothing invariant).
    ///
    /// The model path is pre-resolved by PreValidateHeadPartModels, so this
    /// method does not perform any path resolution or BSA extraction.
    ///
    /// Cloned shapes are renamed to the headpart's EditorID (so the game can match
    /// headpart records to FaceGen geometry) and converted from NiTriShape to
    /// BSDynamicTriShape if needed (FaceGen NIFs require BSDynamicTriShape).
    /// </summary>
    private bool SwapSingleHeadPartModel(
        NifFile faceGenNif,
        NiNode faceGenSkinNode,
        IHeadPartGetter headPartGetter,
        HeadPart.TypeEnum type,
        NPCInfo npcInfo,
        string headPartEditorId,
        string resolvedModelAbsPath,
        bool resolvedModelExtracted,
        bool isMainPart,
        Dictionary<string, HeadPart.TypeEnum> existingShapeTypes,
        (float R, float G, float B)? capturedHairTint = null,
        CapturedBoneTransforms capturedEyeTransforms = null,
        CapturedEyeShaderProperties capturedEyeShader = null,
        Dictionary<uint, string> resolvedEyeTextures = null)
    {
        using (ProfileBlock(nameof(SwapSingleHeadPartModel)))
        {
            // ── Model path was pre-resolved by PreValidateHeadPartModels ──

            string modelRelPath = headPartGetter.Model.File.DataRelativePath.Path;
            string modelAbsPath = resolvedModelAbsPath;
            bool extractedModel = resolvedModelExtracted;

            DebugLog(npcInfo, "SwapSingleHeadPartModel: editorId=" + headPartEditorId +
                " modelRelPath=" + modelRelPath + " (pre-resolved)");
            DebugLog(npcInfo, "  modelAbsPath=" + modelAbsPath);

            bool anyChanges = false;

            try
            {
                // ── Cache lookup: reuse already-loaded and optimized model NIFs ──
                //
                // The same model NIF (e.g. EyesMale.nif, Zombrex.nif) is referenced by
                // hundreds or thousands of NPCs. Loading from disk and running OptimizeFor
                // on every call was the single largest per-NPC cost (~6ms × 12K calls).
                //
                // Since CloneShape reads from the source NIF without mutating it, a single
                // loaded NifFile can be safely shared across all NPCs. The NifFile is NOT
                // wrapped in `using` — it's owned by _modelNifCache and disposed in
                // DisposeCaches() after the patching run completes.

                if (!_modelNifCache.TryGetValue(modelAbsPath, out var modelNif))
                {
                    modelNif = new NifFile();
                    int loadResult = modelNif.Load(modelAbsPath);
                    DebugLog(npcInfo, "  Model NIF load result: " + loadResult);
                    if (loadResult != 0)
                    {
                        _logger.LogError(
                            "FaceGenPatcher: Failed to load model NIF (error " + loadResult + "): " + modelAbsPath);
                        modelNif.Dispose();
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
                    // the same conversion Outfit Studio uses). Since modelNif is cached and reused, the
                    // optimization only runs once per unique model path.

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

                    _modelNifCache[modelAbsPath] = modelNif;
                }
                else
                {
                    DebugLog(npcInfo, "  Model NIF CACHE HIT: " + modelAbsPath);
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

                // ── Clone shapes from model NIF into FaceGen NIF ──
                //
                // NOTE: Shape removal for singular types is handled by the caller
                // (SwapHeadPartType) BEFORE any cloning begins, ensuring the
                // all-or-nothing invariant.

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

                    // ── Forward captured eye shader properties to the cloned shape ──
                    if (capturedEyeShader != null)
                    {
                        ApplyEyeShaderToClonedShape(faceGenNif, clonedShape, capturedEyeShader, npcInfo);
                    }

                    // ── Bake TNAM texture set into the cloned eye shape ──
                    //
                    // The cloned shape's BSShaderTextureSet carries the model NIF's default
                    // textures (e.g., EyeBrown.dds). The actual eye textures are defined by
                    // the head part's TNAM record. At runtime the engine would resolve TNAM
                    // and apply those textures, but since this is a pre-baked FaceGen NIF,
                    // the engine uses whatever is already in the NIF. We must bake the TNAM
                    // textures here so the correct eye color appears in-game.
                    if (resolvedEyeTextures != null && resolvedEyeTextures.Count > 0)
                    {
                        ApplyEyeTexturesToClonedShape(faceGenNif, clonedShape, resolvedEyeTextures, npcInfo);
                    }

                    // ── Apply NPC-specific eye positioning ──
                    //
                    // Three strategies in priority order:
                    //
                    //   1. DIRECT VERTEX COPY: If the original and replacement shapes
                    //      have the same vertex count (same base mesh topology — common
                    //      for texture-only eye swaps), copy the CK-morphed vertices
                    //      directly. Pixel-perfect, no approximation.
                    //
                    //   2. TRI MORPH: If vertex counts differ but the NPC has chargen
                    //      slider data (NAM9), apply the CharGen .tri morphs to deform
                    //      the replacement mesh. CK-equivalent per-vertex morphing.
                    //
                    //   3. CENTROID OFFSET: Last resort — rigid translation based on
                    //      centroid delta. Handles custom-sculpted NPCs (Bijin etc.)
                    //      where NAM9 is null and topologies differ.

                    bool positioned = false;
                    bool usedDirectVertexCopy = false;

                    // Strategy 1: Direct vertex copy (same topology)
                    // Only for the main part — extra parts have no relationship to the
                    // original shape's morphed vertices.
                    if (!positioned && isMainPart && capturedEyeTransforms?.MorphedVertices != null)
                    {
                        using var clonedVerts = faceGenNif.GetVertsForShape(clonedShape);
                        if (clonedVerts != null && clonedVerts.Count == capturedEyeTransforms.MorphedVertices.Length)
                        {
                            // Same vertex count = same base mesh. Copy directly.
                            for (int vi = 0; vi < clonedVerts.Count; vi++)
                            {
                                var src = capturedEyeTransforms.MorphedVertices[vi];
                                var dst = clonedVerts[vi];
                                dst.x = src.X;
                                dst.y = src.Y;
                                dst.z = src.Z;
                            }
                            faceGenNif.SetVertsForShape(clonedShape, clonedVerts);
                            positioned = true;
                            usedDirectVertexCopy = true;

                            DebugLog(npcInfo, "  EyePositioning: DIRECT VERTEX COPY — copied " +
                                clonedVerts.Count + " CK-morphed vertices (pixel-perfect)");
                        }
                        else
                        {
                            DebugLog(npcInfo, "  EyePositioning: Vertex count mismatch — original=" +
                                capturedEyeTransforms.MorphedVertices.Length + " cloned=" +
                                (clonedVerts?.Count ?? 0) + " (trying other strategies)");
                        }
                    }

                    // Strategy 2: TRI morph (different topology, has chargen data)
                    //
                    // When TRI morph succeeds, the mesh's own bone transforms are correct
                    // for the deformed geometry. Captured bone transforms from the OLD shape
                    // must NOT be applied — they're from a different mesh with a different
                    // coordinate space, and overwriting them corrupts the positioning.
                    if (!positioned)
                    {
                        positioned = TryApplyTriMorphToClonedShape(
                            faceGenNif, clonedShape, headPartGetter, type, npcInfo);
                        if (positioned)
                        {
                            DebugLog(npcInfo, "  EyePositioning: TRI MORPH applied (bone transforms kept from model)");
                        }
                    }

                    // Strategy 3: Centroid offset (fallback — also applies bone transforms)
                    //
                    // IMPORTANT: This is ONLY applied to the main head part shape, NOT to
                    // extra parts (accessories, ornaments, braids, beads, etc.). Extra parts
                    // are authored by the mod creator to sit correctly relative to their bone
                    // references at their model-default positions. The centroid offset compares
                    // against the OLD shape that was replaced, which has no spatial relationship
                    // to ornamental sub-parts. Applying it to extras produces wildly wrong
                    // offsets (e.g., a chest-dangling statuette shifted -111 Z units because
                    // the old chin beard was at Z=-2).
                    if (!positioned && capturedEyeTransforms != null && isMainPart)
                    {
                        DebugLog(npcInfo, "  EyePositioning: CENTROID OFFSET fallback");
                        // ApplyEyeBoneTransformsToClonedShape handles both vertex
                        // offset AND GlobalToSkin/SkinToBone transforms.
                        ApplyEyeBoneTransformsToClonedShape(faceGenNif, clonedShape, capturedEyeTransforms, npcInfo);
                        positioned = true;
                    }
                    else if (!positioned && capturedEyeTransforms != null && !isMainPart)
                    {
                        DebugLog(npcInfo, "  EyePositioning: SKIPPED centroid offset for extra part \"" +
                            destShapeName + "\" — keeping model-default positions");
                    }

                    // Apply captured bone transforms ONLY for Strategy 1 (direct vertex copy).
                    //
                    // Strategy 1 copies the old shape's CK-morphed vertices wholesale, so the
                    // old shape's GlobalToSkin and SkinToBone transforms must also be forwarded
                    // to maintain the correct vertex↔bone relationship.
                    //
                    // Strategy 2 (TRI morph) deforms the REPLACEMENT mesh's own vertices using
                    // the NPC's chargen sliders. The replacement mesh's bone transforms are
                    // already correct for its own geometry — overwriting them with the old
                    // shape's transforms corrupts positioning (different mesh, different
                    // coordinate space).
                    //
                    // Strategy 3 (centroid offset) already applies bone transforms internally
                    // inside ApplyEyeBoneTransformsToClonedShape.
                    if (capturedEyeTransforms != null && usedDirectVertexCopy)
                    {
                        if (capturedEyeTransforms.GlobalToSkin != null)
                        {
                            faceGenNif.SetShapeTransformGlobalToSkin(clonedShape, capturedEyeTransforms.GlobalToSkin);
                        }
                        using var bonesToApply = new vectorstring();
                        faceGenNif.GetShapeBoneList(clonedShape, bonesToApply);
                        for (uint bi = 0; bi < (uint)bonesToApply.Count; bi++)
                        {
                            string boneName = bonesToApply[(int)bi];
                            if (capturedEyeTransforms.SkinToBone.TryGetValue(boneName, out var bt))
                            {
                                faceGenNif.SetShapeTransformSkinToBone(clonedShape, bi, bt);
                            }
                        }
                    }

                    _logger.LogReport(
                        "FaceGenPatcher: Cloned shape \"" + shapeInfo.Name + "\" as \"" + destShapeName +
                        "\" (" + type + ") into FaceGen NIF.",
                        false, npcInfo);

                    anyChanges = true;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    "FaceGenPatcher: Exception in SwapSingleHeadPartModel for " +
                    headPartEditorId + ": " + ex.Message);
            }

            return anyChanges;
        }
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
            (clonedShape.name?.get() ?? "unnamed") + " tint to (" +
            tint.R + ", " + tint.G + ", " + tint.B + ")");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  HEAD PART SWAPPING — Eye Shader Property Forwarding
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Scans shapes under BSFaceGenNiNodeSkinned for the existing eye shape's
    /// BSLightingShaderProperty (shader type BSLSP_EYE) and captures the
    /// NPC-specific shader properties that determine eye color and appearance.
    ///
    /// Eye color in Skyrim is primarily driven by the specular color, glossiness,
    /// emissive color/multiple, cubemap scale, and reflection centers stored on
    /// the BSLSP_EYE shader. These values are baked per-NPC in FaceGen NIFs.
    /// Without capturing and re-applying them, cloned replacement eyes use the
    /// head part model's default values — causing all NPCs to share the same
    /// eye color.
    ///
    /// Identifies eye shapes by matching shape names against the NPC's head part
    /// records of type Eyes (via existingShapeTypes), consistent with the
    /// name-based identification used throughout the head part swapping system.
    ///
    /// Returns null if no BSLSP_EYE shader was found. Uses plain value types
    /// to avoid lifetime issues with nifly's SWIG wrappers (which may be
    /// invalidated when shapes are deleted during removal).
    /// </summary>
    private CapturedEyeShaderProperties CaptureEyeShaderProperties(
        NifFile faceGenNif,
        NiNode faceGenSkinNode,
        Dictionary<string, HeadPart.TypeEnum> existingShapeTypes,
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

            // Identify eye shapes by name match against the NPC's head part records.
            string shapeName = shape.name?.get();
            if (shapeName == null) continue;
            if (!existingShapeTypes.TryGetValue(shapeName, out var shapeType)) continue;
            if (shapeType != HeadPart.TypeEnum.Eyes) continue;

            // Get the shape's shader property.
            NiBlockRefNiShader shaderRef = shape.ShaderPropertyRef();
            if (shaderRef == null || shaderRef.IsEmpty()) continue;

            NiObject shaderObj = header.GetBlockById(shaderRef.index);
            if (shaderObj is not BSLightingShaderProperty bslsp) continue;

            if (bslsp.bslspShaderType != (uint)BSLightingShaderPropertyShaderType.BSLSP_EYE) continue;

            // Capture all eye-relevant shader properties into plain value types.
            Vector3 ec = bslsp.emissiveColor;
            Vector3 sc = bslsp.specularColor;
            Vector3 elrc = bslsp.eyeLeftReflectionCenter;
            Vector3 errc = bslsp.eyeRightReflectionCenter;

            var captured = new CapturedEyeShaderProperties
            {
                EmissiveColor = (ec?.x ?? 0f, ec?.y ?? 0f, ec?.z ?? 0f),
                EmissiveMultiple = bslsp.emissiveMultiple,
                SpecularColor = (sc?.x ?? 0f, sc?.y ?? 0f, sc?.z ?? 0f),
                SpecularStrength = bslsp.specularStrength,
                Glossiness = bslsp.glossiness,
                Alpha = bslsp.alpha,
                Softlighting = bslsp.softlighting,
                RimlightPower = bslsp.rimlightPower,
                EyeCubemapScale = bslsp.eyeCubemapScale,
                EyeLeftReflectionCenter = (elrc?.x ?? 0f, elrc?.y ?? 0f, elrc?.z ?? 0f),
                EyeRightReflectionCenter = (errc?.x ?? 0f, errc?.y ?? 0f, errc?.z ?? 0f),
            };

            DebugLog(npcInfo, "  CaptureEyeShaderProperties: Found on \"" + shapeName +
                "\" — specularColor=(" + captured.SpecularColor.R + ", " +
                captured.SpecularColor.G + ", " + captured.SpecularColor.B +
                ") glossiness=" + captured.Glossiness +
                " emissiveMultiple=" + captured.EmissiveMultiple +
                " eyeCubemapScale=" + captured.EyeCubemapScale);

            return captured;
        }

        DebugLog(npcInfo, "  CaptureEyeShaderProperties: No BSLSP_EYE shader found.");
        return null;
    }

    /// <summary>
    /// Applies previously captured eye shader properties to a cloned shape's
    /// BSLightingShaderProperty, if it uses the BSLSP_EYE shader type.
    ///
    /// This preserves the NPC's original eye color, glossiness, emissive glow,
    /// cubemap scale, and reflection center positioning when the eye mesh is
    /// swapped for a different head part model.
    /// </summary>
    private void ApplyEyeShaderToClonedShape(
        NifFile faceGenNif,
        NiShape clonedShape,
        CapturedEyeShaderProperties props,
        NPCInfo npcInfo)
    {
        NiHeader header = faceGenNif.GetHeader();

        NiBlockRefNiShader shaderRef = clonedShape.ShaderPropertyRef();
        if (shaderRef == null || shaderRef.IsEmpty()) return;

        NiObject shaderObj = header.GetBlockById(shaderRef.index);
        if (shaderObj is not BSLightingShaderProperty bslsp) return;

        if (bslsp.bslspShaderType != (uint)BSLightingShaderPropertyShaderType.BSLSP_EYE) return;

        bslsp.emissiveColor = new Vector3(props.EmissiveColor.R, props.EmissiveColor.G, props.EmissiveColor.B);
        bslsp.emissiveMultiple = props.EmissiveMultiple;
        bslsp.specularColor = new Vector3(props.SpecularColor.R, props.SpecularColor.G, props.SpecularColor.B);
        bslsp.specularStrength = props.SpecularStrength;
        bslsp.glossiness = props.Glossiness;
        bslsp.alpha = props.Alpha;
        bslsp.softlighting = props.Softlighting;
        bslsp.rimlightPower = props.RimlightPower;
        bslsp.eyeCubemapScale = props.EyeCubemapScale;
        bslsp.eyeLeftReflectionCenter = new Vector3(
            props.EyeLeftReflectionCenter.X,
            props.EyeLeftReflectionCenter.Y,
            props.EyeLeftReflectionCenter.Z);
        bslsp.eyeRightReflectionCenter = new Vector3(
            props.EyeRightReflectionCenter.X,
            props.EyeRightReflectionCenter.Y,
            props.EyeRightReflectionCenter.Z);

        DebugLog(npcInfo, "  ApplyEyeShaderToClonedShape: Set \"" +
            (clonedShape.name?.get() ?? "unnamed") +
            "\" — specularColor=(" + props.SpecularColor.R + ", " +
            props.SpecularColor.G + ", " + props.SpecularColor.B +
            ") glossiness=" + props.Glossiness +
            " emissiveMultiple=" + props.EmissiveMultiple +
            " eyeCubemapScale=" + props.EyeCubemapScale);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  HEAD PART SWAPPING — Eye Texture Set Baking (TNAM Resolution)
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Resolves the texture paths from a head part's TNAM (Texture Set) record.
    ///
    /// In Skyrim's data model, each HDPT (Head Part) record has a TNAM field
    /// that references a TXST (Texture Set) record. The TXST record contains
    /// up to 9 texture slots (TX00–TX08) that define the textures the engine
    /// should use for that head part at runtime.
    ///
    /// For eye head parts, the TXST typically contains:
    ///   TX00 = Diffuse (the eye color texture, e.g., eyenordic.dds)
    ///   TX01 = Normal/Gloss
    ///   TX02 = Environment Mask / Subsurface Tint
    ///   TX05 = Environment (cubemap)
    ///
    /// When writing pre-baked FaceGen NIFs, the engine does NOT re-resolve TNAM
    /// at runtime — it uses whatever textures are already in the NIF's
    /// BSShaderTextureSet. So we must bake TNAM's textures into the NIF ourselves.
    ///
    /// Returns a dictionary of slot index → texture path for all non-empty slots,
    /// or an empty dictionary if the texture set could not be resolved.
    /// </summary>
    private Dictionary<uint, string> ResolveHeadPartTextureSet(
        IHeadPartGetter headPartGetter,
        NPCInfo npcInfo)
    {
        var result = new Dictionary<uint, string>();

        // TNAM is the texture set link on the head part record.
        var textureSetLink = headPartGetter.TextureSet;
        if (textureSetLink == null || textureSetLink.IsNull)
        {
            DebugLog(npcInfo, "  ResolveHeadPartTextureSet: No TNAM on head part \"" +
                (headPartGetter.EditorID ?? headPartGetter.FormKey.ToString()) + "\"");
            return result;
        }

        if (!_environmentProvider.LinkCache.TryResolve<ITextureSetGetter>(textureSetLink.FormKey, out var textureSet))
        {
            WarnLog(npcInfo, "  ResolveHeadPartTextureSet: Could not resolve TNAM " +
                textureSetLink.FormKey + " for head part \"" +
                (headPartGetter.EditorID ?? headPartGetter.FormKey.ToString()) + "\"");
            return result;
        }

        string txstEditorId = textureSet.EditorID ?? textureSet.FormKey.ToString();
        DebugLog(npcInfo, "  ResolveHeadPartTextureSet: Resolved TNAM \"" + txstEditorId +
            "\" [" + textureSet.FormKey + "]");

        // Read each texture slot from the TXST record.
        // Mutagen exposes these as Diffuse, NormalOrGloss, GlowOrDetailMap, etc.
        // Map them to BSShaderTextureSet slot indices (TX00=0, TX01=1, ..., TX07=7).
        //
        // NOTE: The accessor `.DataRelativePath.Path` assumes Mutagen asset link
        // properties. If your Mutagen version exposes these as plain strings,
        // replace with e.g. `textureSet.Diffuse` directly. If it uses `.RawPath`
        // or `.GivenPath`, adjust accordingly.
        var texturePaths = new (uint Slot, string Path)[]
        {
            (0, textureSet.Diffuse?.DataRelativePath.Path),
            (1, textureSet.NormalOrGloss?.DataRelativePath.Path),
            (2, textureSet.EnvironmentMaskOrSubsurfaceTint?.DataRelativePath.Path),
            (3, textureSet.GlowOrDetailMap?.DataRelativePath.Path),
            (4, textureSet.Height?.DataRelativePath.Path),
            (5, textureSet.Environment?.DataRelativePath.Path),
            (6, textureSet.Multilayer?.DataRelativePath.Path),
            (7, textureSet.BacklightMaskOrSpecular?.DataRelativePath.Path),
        };

        foreach (var (slot, path) in texturePaths)
        {
            if (!string.IsNullOrWhiteSpace(path))
            {
                // Ensure the path includes the "textures\" prefix expected by BSShaderTextureSet.
                // TXST records in plugins store paths relative to Data\textures (e.g.,
                // "Actors\Character\eyes\eyenordic.dds"), but BSShaderTextureSet paths in
                // NIFs are stored relative to Data with the prefix (e.g.,
                // "textures\actors\character\eyes\eyenordic.dds"). The engine handles both
                // forms, but we match the existing NIF convention for consistency.
                string normalizedPath = path;
                if (!normalizedPath.StartsWith("textures\\", StringComparison.OrdinalIgnoreCase) &&
                    !normalizedPath.StartsWith("textures/", StringComparison.OrdinalIgnoreCase))
                {
                    normalizedPath = "textures\\" + normalizedPath;
                }

                result[slot] = normalizedPath;
                DebugLog(npcInfo, "    TX0" + slot + " (slot " + slot + ") = \"" + normalizedPath + "\"" +
                    (normalizedPath != path ? " (prefixed from \"" + path + "\")" : ""));
            }
        }

        DebugLog(npcInfo, "  ResolveHeadPartTextureSet: " + result.Count + " non-empty slot(s)");
        return result;
    }

    /// <summary>
    /// Bakes resolved TNAM texture paths into a cloned eye shape's BSShaderTextureSet.
    ///
    /// This overwrites the model NIF's default textures (e.g., EyeBrown.dds) with
    /// the textures defined by the head part's TNAM record (e.g., eyenordic.dds),
    /// ensuring the correct eye color appears in the pre-baked FaceGen NIF.
    ///
    /// Only writes to slots that have non-empty paths in the resolved texture set.
    /// Slots not present in the TNAM are left unchanged (preserving the model's
    /// defaults for any slots the TXST record doesn't override).
    /// </summary>
    private void ApplyEyeTexturesToClonedShape(
        NifFile faceGenNif,
        NiShape clonedShape,
        Dictionary<uint, string> eyeTextures,
        NPCInfo npcInfo)
    {
        string shapeName = clonedShape.name?.get() ?? "unnamed";

        foreach (var (slot, texturePath) in eyeTextures)
        {
            faceGenNif.SetTextureSlot(clonedShape, texturePath, slot);

            DebugLog(npcInfo, "  ApplyEyeTexturesToClonedShape: \"" + shapeName +
                "\" slot " + slot + " -> \"" + texturePath + "\"");
        }

        _logger.LogReport(
            "FaceGenPatcher: Baked " + eyeTextures.Count + " TNAM texture slot(s) into eye shape \"" +
            shapeName + "\".",
            false, npcInfo);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  HEAD PART SWAPPING — Eye Bone Transform Forwarding
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Scans shapes under BSFaceGenNiNodeSkinned for the existing shape of the
    /// specified type and captures its global-to-skin and per-bone skin-to-bone
    /// transforms, as well as the morphed vertex positions.
    ///
    /// These NPC-specific transforms are baked by the Creation Kit to position
    /// shapes correctly on the NPC's unique face. Without capturing and
    /// re-applying them, cloned replacement shapes land at the generic head
    /// part model's default position.
    ///
    /// Identifies shapes by matching shape names against the NPC's head part
    /// records of the specified type (via existingShapeTypes), consistent with
    /// the name-based identification used throughout the head part swapping system.
    ///
    /// Used for eyes, eyebrows, facial hair, and scars — all of which have
    /// CK-morphed vertices in FaceGen NIFs.
    /// </summary>
    private CapturedBoneTransforms CaptureEyeBoneTransforms(
        NifFile faceGenNif,
        NiNode faceGenSkinNode,
        Dictionary<string, HeadPart.TypeEnum> existingShapeTypes,
        HeadPart.TypeEnum targetType,
        NPCInfo npcInfo)
    {
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

            // Identify shapes by matching name against the NPC's head part
            // records of the target type, same approach as RemoveShapesByHeadPartType.
            string shapeName = shape.name?.get();
            if (string.IsNullOrEmpty(shapeName)) continue;

            if (!existingShapeTypes.TryGetValue(shapeName, out var shapeType) ||
                shapeType != targetType)
                continue;

            // Found the existing eye shape — capture its transforms.
            var captured = new CapturedBoneTransforms();

            // ── Capture vertex centroid (PRIMARY eye positioning data) ──
            //
            // The CK bakes NPC-specific eye positioning into the actual vertex
            // positions, NOT into any transform. All transforms (TransformToParent,
            // GlobalToSkin, SkinToBone) are identity for vanilla eye shapes.
            //
            // By computing the centroid (average position) of the morphed vertices
            // here and comparing to the cloned shape's centroid later, we can
            // derive a translation offset that repositions the new eye mesh into
            // the correct NPC-specific location.
            using (var verts = faceGenNif.GetVertsForShape(shape))
            {
                if (verts != null && verts.Count > 0)
                {
                    // Store ALL morphed vertices for direct copy when topology matches.
                    captured.MorphedVertices = new (float, float, float)[verts.Count];
                    double cx = 0, cy = 0, cz = 0;
                    for (int vi = 0; vi < verts.Count; vi++)
                    {
                        var v = verts[vi];
                        captured.MorphedVertices[vi] = (v.x, v.y, v.z);
                        cx += v.x;
                        cy += v.y;
                        cz += v.z;
                    }
                    float n = verts.Count;
                    captured.VertexCentroid = ((float)(cx / n), (float)(cy / n), (float)(cz / n));

                    DebugLog(npcInfo, "  CaptureEyeBoneTransforms: Captured " + verts.Count +
                        " morphed vertices from \"" + shapeName + "\" (centroid: " +
                        captured.VertexCentroid.Value.X + ", " +
                        captured.VertexCentroid.Value.Y + ", " +
                        captured.VertexCentroid.Value.Z + ")");
                }
            }

            // Capture global-to-skin transform.
            var globalToSkin = new MatTransform();
            if (faceGenNif.GetShapeTransformGlobalToSkin(shape, globalToSkin))
            {
                captured.GlobalToSkin = globalToSkin;
                DebugLog(npcInfo, "  CaptureEyeBoneTransforms: GlobalToSkin captured from \"" +
                    shapeName + "\"");
            }

            // Capture per-bone skin-to-bone transforms.
            using var boneNames = new vectorstring();
            uint boneCount = faceGenNif.GetShapeBoneList(shape, boneNames);

            for (int i = 0; i < boneNames.Count; i++)
            {
                string boneName = boneNames[i];
                var skinToBone = new MatTransform();

                if (faceGenNif.GetShapeTransformSkinToBone(shape, boneName, skinToBone))
                {
                    captured.SkinToBone[boneName] = skinToBone;
                    DebugLog(npcInfo, "  CaptureEyeBoneTransforms: Bone \"" + boneName +
                        "\" transform captured (translation: " +
                        skinToBone.translation.x + ", " +
                        skinToBone.translation.y + ", " +
                        skinToBone.translation.z + ")");
                }
            }

            if (captured.VertexCentroid.HasValue || captured.SkinToBone.Count > 0)
            {
                DebugLog(npcInfo, "  CaptureEyeBoneTransforms: Captured from \"" + shapeName +
                    "\": Centroid=" + (captured.VertexCentroid.HasValue ? "yes" : "no") +
                    ", " + captured.SkinToBone.Count + " bone transform(s)");
                return captured;
            }
        }

        DebugLog(npcInfo, "  CaptureEyeBoneTransforms: No existing " + targetType + " shape found to capture from.");
        return null;
    }

    /// <summary>
    /// Applies previously captured NPC-specific eye positioning to a cloned eye shape.
    ///
    /// The CK bakes NPC-specific eye positioning by morphing actual vertex positions
    /// in the FaceGen NIF — NOT via transforms (which are all identity for eye shapes).
    /// Since we can't transfer per-vertex morphs between meshes with different topology,
    /// we compute the centroid (average vertex position) of both the original morphed
    /// mesh and the cloned generic mesh, then shift all cloned vertices by the delta.
    ///
    /// This effectively translates the new eye mesh into the NPC's eye sockets without
    /// requiring vertex-for-vertex correspondence between the old and new meshes.
    ///
    /// Also applies captured GlobalToSkin and SkinToBone transforms (typically identity
    /// for vanilla eyes, but may carry meaningful data for modded eye meshes).
    /// </summary>
    private void ApplyEyeBoneTransformsToClonedShape(
        NifFile faceGenNif,
        NiShape clonedShape,
        CapturedBoneTransforms captured,
        NPCInfo npcInfo)
    {
        // ── Apply vertex centroid offset (PRIMARY eye positioning fix) ──
        //
        // The original FaceGen eye shape had CK-morphed vertices positioned in the
        // NPC's eye sockets. The cloned shape has generic model vertices at the
        // "default head" position. Shift all cloned vertices by the centroid delta
        // to reposition the new eyes into the NPC's actual eye sockets.
        if (captured.VertexCentroid.HasValue)
        {
            using var clonedVerts = faceGenNif.GetVertsForShape(clonedShape);
            if (clonedVerts != null && clonedVerts.Count > 0)
            {
                // Compute centroid of the cloned (generic) shape.
                double cx = 0, cy = 0, cz = 0;
                for (int vi = 0; vi < clonedVerts.Count; vi++)
                {
                    var v = clonedVerts[vi];
                    cx += v.x;
                    cy += v.y;
                    cz += v.z;
                }
                float n = clonedVerts.Count;
                float clonedCX = (float)(cx / n);
                float clonedCY = (float)(cy / n);
                float clonedCZ = (float)(cz / n);

                // Delta = original morphed centroid - cloned generic centroid
                float dx = captured.VertexCentroid.Value.X - clonedCX;
                float dy = captured.VertexCentroid.Value.Y - clonedCY;
                float dz = captured.VertexCentroid.Value.Z - clonedCZ;

                DebugLog(npcInfo, "  ApplyEyeBoneTransforms: Centroid offset for \"" +
                    (clonedShape.name?.get() ?? "unnamed") + "\": original=(" +
                    captured.VertexCentroid.Value.X + ", " +
                    captured.VertexCentroid.Value.Y + ", " +
                    captured.VertexCentroid.Value.Z + ") cloned=(" +
                    clonedCX + ", " + clonedCY + ", " + clonedCZ + ") delta=(" +
                    dx + ", " + dy + ", " + dz + ")");

                // Apply the offset to every vertex in-place.
                // clonedVerts[vi] returns a non-owning reference into the native
                // vector's storage, so setting .x/.y/.z directly modifies the
                // underlying data without allocating temporary Vector3 objects.
                for (int vi = 0; vi < clonedVerts.Count; vi++)
                {
                    var v = clonedVerts[vi];
                    v.x += dx;
                    v.y += dy;
                    v.z += dz;
                }

                // Write the shifted vertices back to the shape.
                faceGenNif.SetVertsForShape(clonedShape, clonedVerts);

                DebugLog(npcInfo, "  ApplyEyeBoneTransforms: Shifted " + clonedVerts.Count +
                    " vertices by delta (" + dx + ", " + dy + ", " + dz + ")");
            }
        }

        // Apply global-to-skin transform.
        if (captured.GlobalToSkin != null)
        {
            faceGenNif.SetShapeTransformGlobalToSkin(clonedShape, captured.GlobalToSkin);
            DebugLog(npcInfo, "  ApplyEyeBoneTransforms: Set GlobalToSkin on \"" +
                (clonedShape.name?.get() ?? "unnamed") + "\"");
        }

        // Apply per-bone skin-to-bone transforms.
        using var clonedBoneNames = new vectorstring();
        uint boneCount = faceGenNif.GetShapeBoneList(clonedShape, clonedBoneNames);

        int applied = 0;
        for (uint i = 0; i < (uint)clonedBoneNames.Count; i++)
        {
            string boneName = clonedBoneNames[(int)i];

            if (captured.SkinToBone.TryGetValue(boneName, out var capturedTransform))
            {
                faceGenNif.SetShapeTransformSkinToBone(clonedShape, i, capturedTransform);
                applied++;

                DebugLog(npcInfo, "  ApplyEyeBoneTransforms: Bone[" + i + "] \"" + boneName +
                    "\" -> captured transform (translation: " +
                    capturedTransform.translation.x + ", " +
                    capturedTransform.translation.y + ", " +
                    capturedTransform.translation.z + ")");
            }
        }

        DebugLog(npcInfo, "  ApplyEyeBoneTransforms: Applied " + applied + "/" +
            clonedBoneNames.Count + " bone transforms to \"" +
            (clonedShape.name?.get() ?? "unnamed") + "\"");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  HEAD PART SWAPPING — Remove Conflicting Shapes
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Builds a map of shape name (EditorID) → HeadPart.TypeEnum for all head parts
    /// currently on the NPC record, including ExtraParts (e.g., hairlines).
    ///
    /// Used by the removal pass to identify which existing FaceGen shapes belong to
    /// which head part type by consulting the authoritative record data, rather than
    /// inspecting NIF partition data. This correctly handles shapes that use plain
    /// NiSkinInstance (no BSDismemberSkinInstance) — notably eyes, brows, and mouth
    /// shapes in vanilla FaceGen NIFs — which the previous partition-based approach
    /// could not identify.
    /// </summary>
    private Dictionary<string, HeadPart.TypeEnum> BuildExistingHeadPartTypeMap(NPCInfo npcInfo)
    {
        var map = new Dictionary<string, HeadPart.TypeEnum>(StringComparer.OrdinalIgnoreCase);

        foreach (var hpLink in npcInfo.OriginalNPC.HeadParts)
        {
            if (!_environmentProvider.LinkCache.TryResolve<IHeadPartGetter>(hpLink.FormKey, out var hpGetter))
                continue;
            if (hpGetter.Type == null)
                continue;

            var type = hpGetter.Type.Value;
            string editorId = hpGetter.EditorID;
            if (!string.IsNullOrEmpty(editorId))
                map.TryAdd(editorId, type);

            // Recurse into ExtraParts (hairlines, highlights, etc.).
            // ExtraParts are subordinate to the parent and are always removed/replaced
            // as a group, so they are mapped under the PARENT's type regardless of
            // their own PNAM type. Some ExtraParts (e.g., hairlines) may have a
            // different PNAM type than their parent, which would cause
            // RemoveShapesByHeadPartType to miss them if we used the extra part's own type.
            if (hpGetter.ExtraParts != null)
            {
                foreach (var extraLink in hpGetter.ExtraParts)
                {
                    if (_environmentProvider.LinkCache.TryResolve<IHeadPartGetter>(extraLink.FormKey, out var extraGetter)
                        && !string.IsNullOrEmpty(extraGetter.EditorID))
                    {
                        map.TryAdd(extraGetter.EditorID, type);
                    }
                }
            }
        }

        return map;
    }

    /// <summary>
    /// Removes existing FaceGen shapes that belong to the specified head part type,
    /// identified by matching shape names against the NPC's head part EditorIDs
    /// (provided via <paramref name="existingShapeTypes"/>).
    ///
    /// This replaces the previous partition-based RemoveShapesByPartition approach,
    /// which failed for shapes using plain NiSkinInstance (no BSDismemberSkinInstance).
    /// nifly synthesizes a default partition ID of 32 (SBP_32_BODY) for such shapes,
    /// causing either false-positive deletion of unrelated shapes or — when guarded
    /// against — failure to remove the shapes that actually needed replacing (e.g.,
    /// eyes, brows). By identifying shapes through their record type instead of
    /// partition data, this approach works uniformly regardless of skin instance type.
    /// </summary>
    private void RemoveShapesByHeadPartType(
        NifFile faceGenNif,
        NiNode faceGenSkinNode,
        HeadPart.TypeEnum typeToRemove,
        Dictionary<string, HeadPart.TypeEnum> existingShapeTypes,
        NPCInfo npcInfo)
    {
        var shapesToRemove = new List<(NiShape Shape, string Name)>();

        using var shapes = faceGenNif.GetShapes();
        foreach (var shape in shapes)
        {
            // Only consider shapes parented to BSFaceGenNiNodeSkinned.
            var parent = faceGenNif.GetParentNode(shape);
            if (parent == null) continue;

            string parentName = parent.name.get();
            if (parentName != "BSFaceGenNiNodeSkinned"
                && !IsBlockType(faceGenNif, parent, "BSFaceGenNiNodeSkinned"))
                continue;

            string shapeName = shape.name?.get();
            if (string.IsNullOrEmpty(shapeName)) continue;

            if (existingShapeTypes.TryGetValue(shapeName, out var shapeType)
                && shapeType == typeToRemove)
            {
                shapesToRemove.Add((shape, shapeName));
            }
        }

        foreach (var (shape, name) in shapesToRemove)
        {
            faceGenNif.DeleteShape(shape);

            DebugLog(npcInfo, "  RemoveShapesByHeadPartType: Removed \"" + name +
                "\" (type=" + typeToRemove + ")");

            _logger.LogReport(
                "FaceGenPatcher: Removed existing shape \"" + name + "\" (type " + typeToRemove + " conflict).",
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
    //  HEAD PART SWAPPING — TRI File Morph Application
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Attempts to apply NPC-specific FaceGen morphs to a cloned head part
    /// shape by reading the head part's CharGen .tri file and applying the
    /// NPC's slider values as morph weights.
    ///
    /// This produces CK-equivalent per-vertex morphing — the exact same
    /// deformation the Creation Kit would bake when generating FaceGen data.
    /// Unlike the centroid approximation (which only translates the mesh
    /// rigidly), this correctly handles rotation, asymmetric deformation,
    /// and per-vertex conforming to the NPC's specific face shape.
    ///
    /// Returns true if morphing was successfully applied, false if the .tri
    /// file could not be found or parsed (caller should fall back to the
    /// centroid approximation in that case).
    /// </summary>
    private bool TryApplyTriMorphToClonedShape(
        NifFile faceGenNif,
        NiShape clonedShape,
        IHeadPartGetter headPartGetter,
        HeadPart.TypeEnum type,
        NPCInfo npcInfo)
    {
        using (ProfileBlock(nameof(TryApplyTriMorphToClonedShape)))
        {
            // ── Step 1: Locate the CharGen .tri file for this head part ──
            //
            // The CharGen .tri file sits alongside the head part's NIF with a
            // naming convention: if the NIF is "EyesFemaleHumanPassion.nif",
            // the chargen TRI is "EyesFemaleChargen.tri" (shared across eye
            // variants within a gender/race), or sometimes "<basename>Chargen.tri".
            //
            // We try multiple resolution strategies:
            //   1. Direct .tri path: replace .nif extension with .tri
            //      (some head parts have their own per-model .tri)
            //   2. CharGen variant: replace the model filename with the
            //      corresponding CharGen TRI filename from the game's
            //      standard naming pattern

            string modelRelPath = null;
            if (headPartGetter.Model != null)
            {
                modelRelPath = headPartGetter.Model.File.DataRelativePath.Path;
            }

            if (string.IsNullOrEmpty(modelRelPath))
            {
                DebugLog(npcInfo, "  TryApplyTriMorph: No model path on head part");
                return false;
            }

            // ── Cached .tri path resolution ──
            //
            // The .tri path depends only on the model's relative NIF path (and
            // indirectly the head part type via AddStandardCharGenTriCandidates),
            // not on the NPC. null values are cached intentionally so that
            // known-missing .tri files (e.g., all KS Hairdos models) skip the
            // expensive File.Exists + BSA scan on every subsequent NPC.

            string triAbsPath;
            if (_triPathCache.TryGetValue(modelRelPath, out triAbsPath))
            {
                // Cache hit — triAbsPath may be null (known-missing)
                if (triAbsPath == null)
                {
                    DebugLog(npcInfo, "  TryApplyTriMorph: CharGen .tri CACHE HIT (known-missing) for " + modelRelPath);
                    return false;
                }
                DebugLog(npcInfo, "  TryApplyTriMorph: CharGen .tri CACHE HIT: " + triAbsPath);
            }
            else
            {
                // Cache miss — perform full resolution and store result
                triAbsPath = ResolveCharGenTriPath(modelRelPath, headPartGetter, npcInfo);
                _triPathCache[modelRelPath] = triAbsPath; // null = known-missing

                if (triAbsPath == null)
                {
                    DebugLog(npcInfo, "  TryApplyTriMorph: CharGen .tri not found for " + modelRelPath);
                    return false;
                }
            }

            // ── Cached .tri file parsing ──
            //
            // The same CharGen .tri (e.g. EyesFemaleChargen.tri) is used by
            // every NPC that gets an eye/brow/etc swap for that gender+race.
            // TriFileData is a pure managed object — safe to share.

            if (!_triFileCache.TryGetValue(triAbsPath, out var triData))
            {
                triData = TriFileParser.Load(triAbsPath, out string parseError);
                if (triData == null)
                {
                    WarnLog(npcInfo, "  TryApplyTriMorph: Failed to parse .tri: " + parseError);
                    return false;
                }

                _triFileCache[triAbsPath] = triData;
            }
            else
            {
                DebugLog(npcInfo, "  TryApplyTriMorph: Parsed .tri CACHE HIT: " + triAbsPath);
            }

            DebugLog(npcInfo, "  TryApplyTriMorph: Using \"" + triAbsPath +
                "\": " + triData.VertexCount + " verts, " +
                triData.Morphs.Count + " morphs [" +
                string.Join(", ", triData.MorphNames) + "]");

            // ── Step 3: Verify vertex count matches the cloned shape ──
            //
            // The .tri file must have the same vertex count as the NIF mesh
            // it was built for. If they don't match, the morph deltas would
            // be applied to the wrong vertices.

            using var clonedVerts = faceGenNif.GetVertsForShape(clonedShape);
            if (clonedVerts == null || clonedVerts.Count == 0)
            {
                DebugLog(npcInfo, "  TryApplyTriMorph: Cloned shape has no vertices");
                return false;
            }

            if (clonedVerts.Count != triData.VertexCount)
            {
                WarnLog(npcInfo, "  TryApplyTriMorph: Vertex count mismatch — " +
                    "shape has " + clonedVerts.Count + " verts but .tri has " +
                    triData.VertexCount + ". Cannot apply morphs.");
                return false;
            }

            // ── Step 4: Read the NPC's chargen slider values from Mutagen ──

            var morphWeights = BuildNpcMorphWeights(npcInfo);
            if (morphWeights.Count == 0)
            {
                DebugLog(npcInfo, "  TryApplyTriMorph: NPC has no face morph data");
                return false;
            }

            DebugLog(npcInfo, "  TryApplyTriMorph: NPC has " + morphWeights.Count + " morph weights");

            // ── Step 5: Compute per-vertex morph offsets ──

            var offsets = TriFileParser.ComputeMorphOffsets(triData, morphWeights);

            // Check if any offsets are non-zero (worth applying).
            bool hasNonZeroOffset = false;
            float maxOffset = 0;
            for (int i = 0; i < offsets.Length; i++)
            {
                float mag = Math.Abs(offsets[i].X) + Math.Abs(offsets[i].Y) + Math.Abs(offsets[i].Z);
                if (mag > maxOffset) maxOffset = mag;
                if (mag > 1e-6f) hasNonZeroOffset = true;
            }

            if (!hasNonZeroOffset)
            {
                DebugLog(npcInfo, "  TryApplyTriMorph: All morph offsets are zero (sliders may not match .tri morph names)");
                return false;
            }

            DebugLog(npcInfo, "  TryApplyTriMorph: Max offset magnitude: " + maxOffset);

            // ── Step 6: Apply offsets to the cloned vertices ──

            for (int i = 0; i < clonedVerts.Count; i++)
            {
                var v = clonedVerts[i];
                v.x += offsets[i].X;
                v.y += offsets[i].Y;
                v.z += offsets[i].Z;
            }

            faceGenNif.SetVertsForShape(clonedShape, clonedVerts);

            DebugLog(npcInfo, "  TryApplyTriMorph: Applied per-vertex morphs to " +
                clonedVerts.Count + " vertices on \"" +
                (clonedShape.name?.get() ?? "unnamed") + "\"");

            _logger.LogReport(
                "FaceGenPatcher: Applied CharGen .tri morphs (" + triData.Morphs.Count +
                " morphs, " + morphWeights.Count + " active weights) to \"" +
                (clonedShape.name?.get() ?? "unnamed") + "\" (" + type + ").",
                false, npcInfo);

            return true;
        }
    }
    
    /// <summary>
    /// Reads the NPC's chargen slider values (FaceMorph / NAM9) and preset
    /// indices (FaceParts / NAMA) from the winning override.
    ///
    /// If FaceMorph is null, returns EMPTY weights. Both the continuous
    /// sliders AND presets are meaningless for a face that wasn't built
    /// with the chargen system (Bijin etc.), so the centroid fallback
    /// should handle positioning instead.
    /// </summary>
    private Dictionary<string, float> BuildNpcMorphWeights(NPCInfo npcInfo)
    {
        var npcGetter = npcInfo.OriginalNPC;

        if (npcGetter.FaceMorph == null)
        {
            DebugLog(npcInfo, "    BuildNpcMorphWeights: FaceMorph is NULL — " +
                "custom-sculpted face, returning empty weights (centroid fallback).");
            return new Dictionary<string, float>();
        }

        var sliderValues = ExtractFaceMorphSliders(npcGetter.FaceMorph);

        DebugLog(npcInfo, "    BuildNpcMorphWeights: FaceMorph present — " +
            sliderValues.Count + " sliders");

        if (IsDebugNpc(npcInfo))
        {
            for (int i = 0; i < sliderValues.Count; i++)
            {
                string morphName = ChargenSliderMap.GetSliderMorphName(i) ?? ("index" + i);
                DebugLog(npcInfo, "      Slider[" + i + "] " + morphName + " = " + sliderValues[i]);
            }
        }

        uint? nosePreset = null, browPreset = null, eyePreset = null, mouthPreset = null;
        if (npcGetter.FaceParts != null)
        {
            nosePreset  = npcGetter.FaceParts.Nose;
            browPreset  = npcGetter.FaceParts.Unknown;
            eyePreset   = npcGetter.FaceParts.Eyes;
            mouthPreset = npcGetter.FaceParts.Mouth;

            DebugLog(npcInfo, "    BuildNpcMorphWeights: FaceParts: Nose=" + nosePreset +
                " Brow=" + browPreset + " Eyes=" + eyePreset + " Mouth=" + mouthPreset);
        }

        var weights = ChargenSliderMap.BuildMorphWeights(
            sliderValues, nosePreset, browPreset, eyePreset, mouthPreset);

        if (IsDebugNpc(npcInfo))
        {
            DebugLog(npcInfo, "    BuildNpcMorphWeights: Final weight count=" + weights.Count);
            foreach (var (name, weight) in weights)
            {
                DebugLog(npcInfo, "    MorphWeight: \"" + name + "\" = " + weight);
            }
        }

        return weights;
    }

    /// <summary>
    /// Extracts the 19 chargen slider float values from Mutagen's NpcFaceMorph.
    /// Filters sentinel values: Mutagen returns float.MaxValue for missing fields.
    /// </summary>
    private static List<float> ExtractFaceMorphSliders(Mutagen.Bethesda.Skyrim.INpcFaceMorphGetter morphGetter)
    {
        var result = new List<float>(19);

        result.Add(morphGetter.NoseLongVsShort);       // 0
        result.Add(morphGetter.NoseUpVsDown);          // 1
        result.Add(morphGetter.JawUpVsDown);           // 2
        result.Add(morphGetter.JawNarrowVsWide);       // 3
        result.Add(morphGetter.JawForwardVsBack);      // 4
        result.Add(morphGetter.CheeksUpVsDown);        // 5
        result.Add(morphGetter.CheeksForwardVsBack);   // 6
        result.Add(morphGetter.EyesUpVsDown);          // 7
        result.Add(morphGetter.EyesInVsOut);           // 8
        result.Add(morphGetter.BrowsUpVsDown);         // 9
        result.Add(morphGetter.BrowsInVsOut);          // 10
        result.Add(morphGetter.BrowsForwardVsBack);    // 11
        result.Add(morphGetter.LipsUpVsDown);          // 12
        result.Add(morphGetter.LipsInVsOut);           // 13
        result.Add(morphGetter.ChinNarrowVsWide);      // 14
        result.Add(morphGetter.ChinUpVsDown);          // 15
        result.Add(morphGetter.ChinUnderbiteVsOverbite); // 16
        result.Add(morphGetter.EyesForwardVsBack);     // 17
        result.Add(morphGetter.Unknown);               // 18

        // Sanitize: Mutagen returns float.MaxValue (3.4E+38) for missing fields.
        // Chargen sliders are always in [-1, 1]. Anything beyond ±10 is a sentinel.
        for (int i = 0; i < result.Count; i++)
        {
            float v = result[i];
            if (float.IsNaN(v) || float.IsInfinity(v) || Math.Abs(v) > 10f)
            {
                result[i] = 0f;
            }
        }

        return result;
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  TRI FILE PATH RESOLUTION
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Resolves the CharGen .tri file path for a head part model.
    ///
    /// Skyrim's CharGen .tri naming conventions:
    ///
    ///   Model NIF path                          → CharGen TRI path
    ///   ──────────────────────────────────────   ──────────────────────────────
    ///   EyesFemaleHumanPassion.nif              → EyesFemaleChargen.tri
    ///   FemaleHead.nif                          → FemaleHeadCharGen.tri
    ///   Mouth\MouthHumanF.nif                   → Mouth\MouthHumanFChargen.tri
    ///   FaceParts\FemaleHeadBrows.nif           → FaceParts\FemaleHeadBrowsCharGen.tri
    ///   <anything>.nif                          → <anything>.tri   (direct match)
    ///   <anything>.nif                          → <anything>Chargen.tri
    ///   <anything>.nif                          → <anything>CharGen.tri
    ///
    /// We try in priority order:
    ///   1. Direct .tri replacement (model.nif → model.tri)
    ///   2. CharGen suffix variants (model.nif → modelChargen.tri, modelCharGen.tri)
    ///   3. Known game-standard CharGen TRI paths for the head part type
    ///
    /// Returns the absolute path of the first match found, or null.
    /// </summary>
    private string ResolveCharGenTriPath(
        string modelRelPath,
        IHeadPartGetter headPartGetter,
        NPCInfo npcInfo)
    {
        string dataFolder = _environmentProvider.DataFolderPath;

        // Strip .nif extension to get the base name
        string basePath = modelRelPath;
        if (basePath.EndsWith(".nif", StringComparison.OrdinalIgnoreCase))
        {
            basePath = basePath.Substring(0, basePath.Length - 4);
        }

        var candidates = new List<string>();

        // Priority 1: Standard game CharGen TRI paths (most reliable)
        AddStandardCharGenTriCandidates(candidates, modelRelPath, headPartGetter);

        // Priority 2: CharGen suffix on the specific model name
        candidates.Add(basePath + "Chargen.tri");
        candidates.Add(basePath + "CharGen.tri");
        candidates.Add(basePath + "chargen.tri");

        // DO NOT add basePath + ".tri" — that's the expression TRI
        // (BlinkLeft, DialogueAnger), not the CharGen TRI.

        // De-duplicate
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var uniqueCandidates = new List<string>();
        foreach (var c in candidates)
        {
            if (seen.Add(c)) uniqueCandidates.Add(c);
        }

        DebugLog(npcInfo, "  ResolveCharGenTriPath: " + uniqueCandidates.Count +
            " candidates for " + modelRelPath);

        foreach (string candidate in uniqueCandidates)
        {
            string absPath = Path.Combine(dataFolder, candidate);
            bool exists = File.Exists(absPath);
            DebugLog(npcInfo, "    Loose: " + candidate + " → " + (exists ? "FOUND" : "miss"));
            if (exists) return absPath;
        }

        foreach (string candidate in uniqueCandidates)
        {
            string bsaSubPath = candidate.Replace('/', '\\');
            string extractedPath = TryExtractTriFromBsa(bsaSubPath, headPartGetter);
            DebugLog(npcInfo, "    BSA: " + candidate + " → " + (extractedPath != null ? "EXTRACTED" : "miss"));
            if (extractedPath != null) return extractedPath;
        }

        return null;
    }

    /// <summary>
    /// Adds standard game CharGen TRI paths based on the head part type and
    /// the NPC's gender/race. These are the shared CharGen TRI files that
    /// Skyrim uses for all head parts of a given type within a gender/race.
    ///
    /// For example, ALL female human eye meshes share "EyesFemaleChargen.tri"
    /// regardless of which specific eye model (Passion, Default, etc.) is used.
    /// </summary>
    private void AddStandardCharGenTriCandidates(
        List<string> candidates,
        string modelRelPath,
        IHeadPartGetter headPartGetter)
    {
        // Detect gender from model path (crude but effective for vanilla)
        string pathLower = modelRelPath.ToLowerInvariant();
        bool isFemale = pathLower.Contains("female");
        string genderPrefix = isFemale ? "Female" : "Male";

        string charAssetsBase = Path.Combine("meshes", "actors", "character", "character assets");

        var type = headPartGetter.Type;

        // Standard Skyrim CharGen TRI files:
        if (type == HeadPart.TypeEnum.Eyes)
        {
            candidates.Add(Path.Combine(charAssetsBase, "Eyes" + genderPrefix + "Chargen.tri"));
            candidates.Add(Path.Combine(charAssetsBase, "Eyes" + genderPrefix + "CharGen.tri"));
        }
        else if (type == HeadPart.TypeEnum.Eyebrows)
        {
            candidates.Add(Path.Combine(charAssetsBase, "FaceParts",
                genderPrefix + "HeadBrowsCharGen.tri"));
        }
        else if (type == HeadPart.TypeEnum.Face)
        {
            candidates.Add(Path.Combine(charAssetsBase, genderPrefix + "HeadCharGen.tri"));
        }
        else if (type == HeadPart.TypeEnum.Hair)
        {
            // Hair typically doesn't have CharGen morphs (it's positioned
            // by bones, not vertex morphs), but some modded hair does.
            // No standard candidates to add.
        }

        // Mouth parts
        if (pathLower.Contains("mouth"))
        {
            candidates.Add(Path.Combine(charAssetsBase, "Mouth",
                "MouthHuman" + (isFemale ? "F" : "M") + "Chargen.tri"));
            candidates.Add(Path.Combine(charAssetsBase, "Mouth",
                "MouthHuman" + (isFemale ? "F" : "M") + "CharGen.tri"));
        }
    }

    /// <summary>
    /// Attempts to extract a .tri file from BSAs. Similar to TryExtractModelFromBsa
    /// but for .tri files.
    /// </summary>
    private string TryExtractTriFromBsa(string bsaSubPath, IHeadPartGetter headPartGetter)
    {
        string safeFileName = Path.GetFileNameWithoutExtension(bsaSubPath)
            .Replace(Path.DirectorySeparatorChar, '_')
            .Replace(Path.AltDirectorySeparatorChar, '_');
        string extractedPath = Path.Combine(
            _patcherState.ModManagerSettings.TempExtractionFolder,
            "TRI_" + safeFileName + ".tri");

        // Pass 1: Search BSAs associated with the head part's defining/overriding plugins.
        var contexts = _environmentProvider.LinkCache
            .ResolveAllContexts<IHeadPart, IHeadPartGetter>(headPartGetter.FormKey);

        foreach (var context in contexts)
        {
            if (_bsaHandler.TryOpenCorrespondingArchiveReaders(context.ModKey, out var bsaReaders) &&
                _bsaHandler.ReadersOrDeferredHaveFile(bsaSubPath, context.ModKey, bsaReaders, out var file) &&
                _bsaHandler.TryExtractFileFromBSA(file, extractedPath))
            {
                return extractedPath;
            }
        }

        // Pass 2: Broad fallback — search ALL loaded BSA archives.
        _bsaHandler.EnsureAllArchivesOpened();
        if (_bsaHandler.TryFindFileInAnyArchive(bsaSubPath, out var broadFile) &&
            _bsaHandler.TryExtractFileFromBSA(broadFile, extractedPath))
        {
            return extractedPath;
        }

        return null;
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
    //  SURROGATE SHAPE NAME RECONCILIATION
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// When outputting a FaceGen NIF for a surrogate NPC, the shapes are named
    /// after the ORIGINAL head part EditorIDs. But the surrogate's head part list
    /// may reference DUPLICATED head parts with modified EditorIDs (e.g. with
    /// "_SynthEBD_Imported" suffix). The engine matches head part records to
    /// FaceGen shapes by name, so mismatched names cause rendering failures.
    ///
    /// This method builds a mapping from original → imported EditorIDs for all
    /// head parts that were duplicated during surrogate creation, then renames
    /// the corresponding shapes in the FaceGen NIF to match.
    ///
    /// Head parts from blocked mods (which were NOT duplicated) keep their
    /// original names — no rename needed since the surrogate references the
    /// original FormKeys for those.
    /// </summary>
    private void ReconcileSurrogateShapeNames(
        NifFile nif,
        NiNode faceGenSkinNode,
        NPCInfo npcInfo,
        Dictionary<HeadPart.TypeEnum, FormKey> headPartAssignments,
        FormKey? outputFormKey)
    {
        // Only applies when outputting to a surrogate
        if (!outputFormKey.HasValue || outputFormKey.Value.IsNull)
        {
            return;
        }

        // ── Build the EditorID rename mapping ──
        //
        // Collect all head part FormKeys that could have shapes in the NIF:
        //   a) Assigned head parts (cloned by Phase A from headPartAssignments)
        //   b) Original NPC's existing head parts (already in the source FaceGen NIF)
        //
        // For each, check if the FormKey was remapped during surrogate creation.
        // If so, the shape needs renaming from the original EditorID to the
        // imported EditorID.

        var editorIdRenameMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var linkCache = _environmentProvider.LinkCache;

        // Collect candidate FormKeys from both sources
        var candidateFormKeys = new HashSet<FormKey>();

        if (headPartAssignments != null)
        {
            foreach (var kvp in headPartAssignments)
            {
                if (!kvp.Value.IsNull) candidateFormKeys.Add(kvp.Value);
            }
        }

        if (npcInfo.OriginalNPC?.HeadParts != null)
        {
            foreach (var hp in npcInfo.OriginalNPC.HeadParts)
            {
                if (!hp.IsNull) candidateFormKeys.Add(hp.FormKey);
            }
        }

        // For each candidate, check if it was remapped and build the rename entry
        foreach (var originalFk in candidateFormKeys)
        {
            if (!_surrogateNpcProvider.TryGetImportedFormKey(originalFk, out var importedFk))
            {
                continue; // Not remapped (blocked mod, different source mod, etc.) — no rename needed
            }

            if (!linkCache.TryResolve<IHeadPartGetter>(originalFk, out var originalHp))
            {
                continue;
            }

            if (!linkCache.TryResolve<IHeadPartGetter>(importedFk, out var importedHp))
            {
                continue;
            }

            string originalEditorId = originalHp.EditorID ?? originalHp.FormKey.ToString();
            string importedEditorId = importedHp.EditorID ?? importedHp.FormKey.ToString();

            if (!originalEditorId.Equals(importedEditorId, StringComparison.OrdinalIgnoreCase))
            {
                editorIdRenameMap.TryAdd(originalEditorId, importedEditorId);
            }

            // Also handle ExtraParts — each extra part gets its own shape in the NIF
            BuildExtraPartRenames(originalHp, editorIdRenameMap);
        }

        if (editorIdRenameMap.Count == 0)
        {
            DebugLog(npcInfo, "ReconcileSurrogateShapeNames: no renames needed");
            return;
        }

        DebugLog(npcInfo, "ReconcileSurrogateShapeNames: " + editorIdRenameMap.Count + " rename(s) to apply");

        // ── Apply renames to shapes under BSFaceGenNiNodeSkinned ──

        using var shapes = nif.GetShapes();
        foreach (var shape in shapes)
        {
            if (!IsUnderFaceGenSkinNode(nif, shape))
            {
                continue;
            }

            string currentName = shape.name?.get();
            if (string.IsNullOrEmpty(currentName))
            {
                continue;
            }

            // Check for exact match first
            if (editorIdRenameMap.TryGetValue(currentName, out var newName))
            {
                shape.name = new NiStringRef(newName);

                _logger.LogReport(
                    "FaceGenPatcher: Renamed surrogate shape \"" + currentName +
                    "\" → \"" + newName + "\"",
                    false, npcInfo);
                continue;
            }

            // Check for indexed names (multi-shape head parts: "EditorId_0", "EditorId_1", etc.)
            // SwapSingleHeadPartModel appends "_N" when a head part model has multiple shapes.
            int lastUnderscore = currentName.LastIndexOf('_');
            if (lastUnderscore > 0 && int.TryParse(currentName.Substring(lastUnderscore + 1), out _))
            {
                string baseName = currentName.Substring(0, lastUnderscore);
                string suffix = currentName.Substring(lastUnderscore); // includes the underscore

                if (editorIdRenameMap.TryGetValue(baseName, out var newBaseName))
                {
                    string newIndexedName = newBaseName + suffix;
                    shape.name = new NiStringRef(newIndexedName);

                    _logger.LogReport(
                        "FaceGenPatcher: Renamed surrogate shape \"" + currentName +
                        "\" → \"" + newIndexedName + "\"",
                        false, npcInfo);
                }
            }
        }
    }

    /// <summary>
    /// Recursively adds ExtraPart renames to the mapping. Each extra part
    /// referenced by a head part can also have its sub-records duplicated,
    /// resulting in a renamed EditorID that needs reconciliation.
    /// </summary>
    private void BuildExtraPartRenames(
        IHeadPartGetter headPartGetter,
        Dictionary<string, string> editorIdRenameMap)
    {
        if (headPartGetter.ExtraParts == null) return;

        var linkCache = _environmentProvider.LinkCache;

        foreach (var extraPartLink in headPartGetter.ExtraParts)
        {
            if (extraPartLink.IsNull) continue;

            if (!_surrogateNpcProvider.TryGetImportedFormKey(extraPartLink.FormKey, out var importedExtraFk))
            {
                continue;
            }

            if (!linkCache.TryResolve<IHeadPartGetter>(extraPartLink.FormKey, out var originalExtra))
            {
                continue;
            }

            if (!linkCache.TryResolve<IHeadPartGetter>(importedExtraFk, out var importedExtra))
            {
                continue;
            }

            string originalExtraEditorId = originalExtra.EditorID ?? originalExtra.FormKey.ToString();
            string importedExtraEditorId = importedExtra.EditorID ?? importedExtra.FormKey.ToString();

            if (!originalExtraEditorId.Equals(importedExtraEditorId, StringComparison.OrdinalIgnoreCase))
            {
                editorIdRenameMap.TryAdd(originalExtraEditorId, importedExtraEditorId);
            }

            // Recurse — extra parts can reference their own extra parts
            BuildExtraPartRenames(originalExtra, editorIdRenameMap);
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
        FormKey formKey = npcInfo.OriginalNPC.FormKey; // Changed: use OriginalNPC for source resolution
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
    /// Resolves the FaceGen NIF path for a given FormKey and root folder.
    /// Used when the output NIF must be written to a surrogate NPC's path
    /// rather than the original NPC's path (SkyPatcher mode).
    /// </summary>
    public static string ResolveFaceGenNifPathForFormKey(FormKey formKey, string rootFolder)
    {
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
        FormKey formKey = npcInfo.OriginalNPC.FormKey; // Changed: use OriginalNPC for BSA lookup
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
        FormKey formKey = npcInfo.OriginalNPC.FormKey; // Changed: use OriginalNPC
        string extractedPath = Path.Combine(
            _patcherState.ModManagerSettings.TempExtractionFolder,
            formKey.ModKey.FileName + "_" + formKey.ID.ToString("X8") + "_facegen.nif");

        var contexts = _environmentProvider.LinkCache.ResolveAllContexts<INpc, INpcGetter>(npcInfo.OriginalNPC.FormKey); // Changed: use OriginalNPC
        foreach (var context in contexts)
        {
            if (_bsaHandler.TryOpenCorrespondingArchiveReaders(context.ModKey, out var bsaReaders) &&
                _bsaHandler.ReadersOrDeferredHaveFile(bsaSubPath, context.ModKey, bsaReaders, out var file) &&
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

        // ── Pass 1: Search BSAs associated with the head part's defining/overriding plugins ──
        //
        // This is the fast, targeted search — most models ship in the same mod's BSA.
        var contexts = _environmentProvider.LinkCache.ResolveAllContexts<IHeadPart, IHeadPartGetter>(headPartGetter.FormKey);
        foreach (var context in contexts)
        {
            if (_bsaHandler.TryOpenCorrespondingArchiveReaders(context.ModKey, out var bsaReaders) &&
                _bsaHandler.ReadersOrDeferredHaveFile(bsaSubPath, context.ModKey, bsaReaders, out var file) &&
                _bsaHandler.TryExtractFileFromBSA(file, extractedPath))
            {
                extracted = true;
                return extractedPath;
            }
        }

        // ── Pass 2: Broad fallback — search ALL loaded BSA archives ──
        //
        // Handles the common case where a head part record in plugin A (e.g.,
        // "Beards of Power.esp") references a mesh that ships in plugin B's BSA
        // (e.g., "KS Hairdos.bsa" or "High Poly Head.bsa"). The plugin-scoped
        // search above misses these cross-mod references.
        //
        // EnsureAllArchivesOpened() is idempotent and fast after the first call
        // (just dictionary lookups for already-opened readers).
        _bsaHandler.EnsureAllArchivesOpened();
        if (_bsaHandler.TryFindFileInAnyArchive(bsaSubPath, out var broadFile) &&
            _bsaHandler.TryExtractFileFromBSA(broadFile, extractedPath))
        {
            extracted = true;
            if (LOGFACEGENDEBUG)
                _logger.LogMessage(
                    "FaceGenPatcher: Model NIF found via broad BSA search: " + modelRelativePath +
                    " (not in head part plugin's own BSA)");
            return extractedPath;
        }

        return null;
    }
    
    // ═══════════════════════════════════════════════════════════════════════════
    //  SURROGATE FACE TINT
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Copies the original NPC's face tint DDS to the surrogate NPC's expected
    /// tint path in the output folder. Tries loose files first, then BSA extraction.
    ///
    /// The engine looks up FaceTint textures at runtime using the NPC's own FormKey:
    ///   textures\actors\character\FaceGenData\FaceTint\{plugin}\{formId}.dds
    /// Since the surrogate has a different FormKey, it needs its own copy of the
    /// tint texture at the surrogate's expected path.
    /// </summary>
    private void CopySurrogateFaceTint(NPCInfo npcInfo, FormKey originalFk, FormKey surrogateFk)
    {
        string originalTintRelPath = ResolveFaceTintRelPath(originalFk);
        string surrogateTintRelPath = ResolveFaceTintRelPath(surrogateFk);

        string surrogateTintAbsPath = Path.Combine(_paths.OutputDataFolder, surrogateTintRelPath);

        // Already copied (e.g. from a previous call for the same surrogate)
        if (File.Exists(surrogateTintAbsPath))
        {
            return;
        }

        // Try loose file first
        string originalTintAbsPath = Path.Combine(_environmentProvider.DataFolderPath, originalTintRelPath);

        if (File.Exists(originalTintAbsPath))
        {
            string dir = Path.GetDirectoryName(surrogateTintAbsPath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            File.Copy(originalTintAbsPath, surrogateTintAbsPath, true);
            _logger.LogReport(
                "FaceGenPatcher: Copied face tint DDS to surrogate path: " + surrogateTintAbsPath,
                false, npcInfo);
            return;
        }

        // Try BSA extraction
        string bsaSubPath = originalTintRelPath.Replace('/', '\\');

        var contexts = _environmentProvider.LinkCache
            .ResolveAllContexts<INpc, INpcGetter>(originalFk);

        foreach (var context in contexts)
        {
            if (_bsaHandler.TryOpenCorrespondingArchiveReaders(context.ModKey, out var bsaReaders) &&
                _bsaHandler.ReadersOrDeferredHaveFile(bsaSubPath, context.ModKey, bsaReaders, out var file) &&
                _bsaHandler.TryExtractFileFromBSA(file, surrogateTintAbsPath))
            {
                _logger.LogReport(
                    "FaceGenPatcher: Extracted face tint DDS from BSA to surrogate path: " +
                    surrogateTintAbsPath,
                    false, npcInfo);
                return;
            }
        }

        _logger.LogReport(
            "FaceGenPatcher: WARNING — Face tint DDS not found for " + originalFk +
            " (loose or BSA). Surrogate " + surrogateFk + " may have missing tint.",
            true, npcInfo);
    }

    /// <summary>
    /// Builds the Data-relative path for an NPC's face tint DDS.
    /// </summary>
    private static string ResolveFaceTintRelPath(FormKey formKey)
    {
        return string.Join("\\",
            "textures", "actors", "character",
            "facegendata", "facetint",
            formKey.ModKey.FileName,
            formKey.ID.ToString("X8") + ".dds");
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

    /// <summary>
    /// Holds the pre-resolved absolute path for a head part model NIF (main or extra),
    /// used by the pre-validation pass to ensure all required models are available
    /// before any NIF editing begins.
    /// </summary>
    private class ResolvedHeadPartModel
    {
        public IHeadPartGetter HeadPart { get; set; }
        public string EditorId { get; set; }
        public string ModelAbsPath { get; set; }
        public bool ExtractedFromBsa { get; set; }

        /// <summary>
        /// True for the primary head part, false for entries from ExtraParts.
        /// The main part drives shape removal; extras are additive only.
        /// </summary>
        public bool IsMainPart { get; set; }
    }

    /// <summary>
    /// Holds captured NPC-specific bone transforms from the original FaceGen eye
    /// shape, so they can be transferred to the cloned replacement shape.
    ///
    /// This preserves the CK-baked eye positioning that accounts for each NPC's
    /// unique facial morphs (eye socket depth, spacing, height, etc.).
    /// </summary>
    private class CapturedBoneTransforms
    {
        /// <summary>
        /// The overall global-to-skin transform from NiSkinData.skinTransform.
        /// </summary>
        public MatTransform GlobalToSkin { get; set; }

        /// <summary>
        /// Per-bone skin-to-bone transforms, keyed by bone name.
        /// </summary>
        public Dictionary<string, MatTransform> SkinToBone { get; set; } = new();

        /// <summary>
        /// The centroid (average vertex position) of the original FaceGen eye shape.
        /// Used as a fallback for rigid translation when vertex copy isn't possible.
        /// </summary>
        public (float X, float Y, float Z)? VertexCentroid { get; set; }

        /// <summary>
        /// ALL morphed vertex positions from the original FaceGen eye shape,
        /// stored as plain float tuples to avoid SWIG lifetime issues.
        ///
        /// When the replacement head part uses the same base mesh NIF as the
        /// original (common for texture-only eye swaps), the cloned shape has
        /// identical vertex topology. In that case we can copy these vertices
        /// directly onto the clone for pixel-perfect positioning — no centroid
        /// approximation or .tri morph math needed.
        ///
        /// The vertex count serves as the topology match check: if
        /// MorphedVertices.Length == clonedShape vertex count, it's safe to copy.
        /// </summary>
        public (float X, float Y, float Z)[] MorphedVertices { get; set; }
    }

    /// <summary>
    /// Holds captured BSLightingShaderProperty values from the original FaceGen
    /// eye shape so they can be transferred to the cloned replacement shape.
    ///
    /// Eye color in Skyrim is primarily driven by the specular color and related
    /// properties on the BSLSP_EYE shader. Without forwarding these, cloned eye
    /// meshes use the default shader values from the head part model NIF, causing
    /// all NPCs to end up with the same eye color.
    /// </summary>
    private class CapturedEyeShaderProperties
    {
        public (float R, float G, float B) EmissiveColor { get; set; }
        public float EmissiveMultiple { get; set; }
        public (float R, float G, float B) SpecularColor { get; set; }
        public float SpecularStrength { get; set; }
        public float Glossiness { get; set; }
        public float Alpha { get; set; }
        public float Softlighting { get; set; }       // "Lighting Effect 1" in NifSkope
        public float RimlightPower { get; set; }       // "Lighting Effect 2" in NifSkope
        public float EyeCubemapScale { get; set; }
        public (float X, float Y, float Z) EyeLeftReflectionCenter { get; set; }
        public (float X, float Y, float Z) EyeRightReflectionCenter { get; set; }
    }
}