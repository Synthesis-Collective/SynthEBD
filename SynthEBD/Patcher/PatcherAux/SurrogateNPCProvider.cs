using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Skyrim;
using Noggog;

namespace SynthEBD;

/// <summary>
/// Creates and caches surrogate NPC records for SkyPatcher mode.
///
/// A surrogate is a new NPC record in SynthEBD.esp that holds modified skin,
/// head texture, head parts, and/or FaceGen data for an original NPC. At runtime,
/// SkyPatcher transfers the surrogate's appearance to the original NPC via
/// SetSkin / CopyVisualStyle ini commands.
///
/// Each original NPC gets at most one surrogate, shared across all features
/// (asset patching, headpart patching, FaceGen baking). The surrogate is created
/// by duplicating the original NPC's skin-related records (WornArmor, HeadTexture)
/// into the output mod with new FormKeys.
///
/// Key behavioral difference from the old NPCProvider: this class ALWAYS creates
/// a new surrogate record with a new FormKey. It never returns the original NPC
/// record or an output mod override of it. If surrogate creation fails, it returns
/// false rather than silently falling back to the original.
/// </summary>
public class SurrogateNPCProvider
{
    private readonly IOutputEnvironmentStateProvider _environmentStateProvider;
    private readonly PatcherState _patcherState;
    private readonly Logger _logger;

    /// <summary>
    /// Cumulative FormKey mapping (original → new) for all sub-records duplicated
    /// across all surrogates. Passed by ref to DuplicateFromOnlyReferencedNpcs
    /// and used by RemapLinks to fix up references in the output mod.
    /// Persists across calls within a single patcher run; cleared on Reinitialize.
    /// </summary>
    private Dictionary<FormKey, FormKey> _formKeyMap = new();

    /// <summary>
    /// Cache of created surrogates, keyed by the ORIGINAL NPC's FormKey.
    /// Ensures one surrogate per original NPC across all callers.
    /// </summary>
    private Dictionary<FormKey, Npc> _surrogateCache = new();

    /// <summary>
    /// Suffix appended to editor IDs of surrogate NPCs and their duplicated sub-records.
    /// Used by IsImportedForSkyPatcher() in RecordGenerator to identify surrogate-owned records.
    /// </summary>
    public const string SurrogateSuffix = "_SynthEBD_Imported";

    public SurrogateNPCProvider(IOutputEnvironmentStateProvider environmentStateProvider, PatcherState patcherState, Logger logger)
    {
        _environmentStateProvider = environmentStateProvider;
        _patcherState = patcherState;
        _logger = logger;
    }

    public void Reinitialize()
    {
        _formKeyMap.Clear();
        _surrogateCache.Clear();
    }

    /// <summary>
    /// Gets or creates a surrogate NPC for the given original NPC.
    /// 
    /// If a surrogate was already created for this original NPC (by any caller),
    /// returns the cached instance. Otherwise, creates a new surrogate by duplicating
    /// the original NPC's skin-related records into the output mod.
    /// 
    /// Returns false if surrogate creation fails (source mod blocked, NPC not found
    /// in any non-output mod, etc.). Callers should handle failure gracefully — 
    /// typically by logging a warning and falling back to direct NPC editing or
    /// skipping the NPC.
    /// </summary>
    /// <param name="originalNpc">The original NPC to create a surrogate for.
    /// Should be the unmodified original (e.g., npcInfo.OriginalNPC), not a 
    /// record that may have already been swapped to a surrogate.</param>
    /// <param name="surrogate">The surrogate NPC record with a new FormKey in SynthEBD.esp,
    /// or null if creation failed.</param>
    /// <returns>True if a surrogate was obtained (created or cached), false otherwise.</returns>
    public bool TryGetSurrogateNpc(INpcGetter originalNpc, out Npc surrogate)
    {
        // ── Check cache first ──
        // One surrogate per original NPC, shared across asset patching,
        // headpart patching, and FaceGen baking.

        if (_surrogateCache.TryGetValue(originalNpc.FormKey, out surrogate))
        {
            return true;
        }

        // ── Find the pre-patching source record ──
        // We need the NPC record from a non-output mod to duplicate from.
        // If other patcher operations (height, BodySlide spells, etc.) have
        // already added the NPC to the output mod, the winning override will
        // be in the output mod — we must look past it.

        var outputMod = _environmentStateProvider.OutputMod;

        if (!_environmentStateProvider.LinkCache.TryResolveContext(
                originalNpc.FormKey, typeof(INpcGetter), out var context))
        {
            _logger.LogMessage("WARNING: SurrogateNPCProvider — Could not resolve NPC " +
                originalNpc.FormKey + " in link cache. Surrogate creation failed.");
            surrogate = null;
            return false;
        }

        // If the winning override is in the output mod, look for the next one
        if (context.ModKey.Equals(outputMod.ModKey))
        {
            var allContexts = originalNpc.ToLink()
                .ResolveAllContexts<ISkyrimMod, ISkyrimModGetter, INpc, INpcGetter>(
                    _environmentStateProvider.LinkCache)
                .ToArray();

            var prePatchingOverride = allContexts
                .FirstOrDefault(x => !x.ModKey.Equals(outputMod.ModKey));

            if (prePatchingOverride != null)
            {
                context = prePatchingOverride;
            }
            else
            {
                _logger.LogMessage("WARNING: SurrogateNPCProvider — NPC " +
                    originalNpc.FormKey + " exists only in the output mod. " +
                    "Cannot create surrogate without a non-output source record.");
                surrogate = null;
                return false;
            }
        }

        // ── Create the surrogate ──
        // DuplicateFromOnlyReferencedNpcs (with onlySkin=true) creates a new
        // NPC record in the output mod containing only WornArmor and HeadTexture
        // references, plus duplicates of all sub-records those reference from
        // the source mod. The new NPC gets a new FormKey in SynthEBD.esp.

        Dictionary<FormKey, FormKey> remappedNpcs = new();

        try
        {
            outputMod.DuplicateFromOnlyReferencedNpcs(
                new List<INpcGetter>() { context.Record as INpcGetter },
                _environmentStateProvider.LinkCache,
                context.ModKey,
                ref _formKeyMap,
                true,
                ref remappedNpcs,
                _patcherState.GeneralSettings.BlockedModsFromImport.ToHashSet());
        }
        catch (Exception ex)
        {
            _logger.LogMessage("WARNING: SurrogateNPCProvider — Exception creating surrogate for NPC " +
                originalNpc.FormKey + ": " + ex.Message);
            surrogate = null;
            return false;
        }

        if (!remappedNpcs.TryGetValue(originalNpc.FormKey, out var surrogateFormKey))
        {
            _logger.LogMessage("WARNING: SurrogateNPCProvider — DuplicateFromOnlyReferencedNpcs " +
                "did not produce a remapped FormKey for NPC " + originalNpc.FormKey + ".");
            surrogate = null;
            return false;
        }

        surrogate = outputMod.Npcs.FirstOrDefault(x => x.FormKey.Equals(surrogateFormKey));

        if (surrogate == null)
        {
            _logger.LogMessage("WARNING: SurrogateNPCProvider — Could not find surrogate NPC " +
                surrogateFormKey + " in output mod after duplication.");
            return false;
        }

        if (!surrogate.EditorID.IsNullOrWhitespace())
        {
            surrogate.EditorID += "_SynthEBD";
        }

        _surrogateCache.Add(originalNpc.FormKey, surrogate);
        return true;
    }
    
    /// <summary>
    /// Checks whether a record with the given FormKey was duplicated into the
    /// output mod as part of surrogate creation. Used by VanillaBodyPathSetter
    /// to determine if an armor record belongs to a surrogate.
    /// </summary>
    public bool TryGetImportedFormKey(FormKey originalFormKey, out FormKey importedFormKey)
    {
        if (_formKeyMap.TryGetValue(originalFormKey, out importedFormKey))
        {
            return true;
        }
        else
        {
            importedFormKey = default;
            return false;
        }
    }
    
    /// <summary>
    /// Checks whether a surrogate was already created for the given original NPC.
    /// Does NOT create a new surrogate — this is a cache lookup only.
    /// Used by VanillaBodyPathSetter to check if the NPC's armor has already been
    /// redirected to a surrogate.
    /// </summary>
    public bool TryGetCachedSurrogate(FormKey originalFormKey, out Npc surrogate)
    {
        return _surrogateCache.TryGetValue(originalFormKey, out surrogate);
    }
}