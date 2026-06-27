using Loqui;
using Mutagen.Bethesda;
using Mutagen.Bethesda.FormKeys.SkyrimSE;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Skyrim;
using Mutagen.Bethesda.Synthesis;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Serilog;

namespace SynthEBD;

/// <summary>
/// Output-stage helper that forces the vanilla (race default) body mesh path onto NPCs whose worn-armor
/// armatures point at non-vanilla body meshes (and were not assigned one from a config). Caches each
/// race/gender's default body/hands/feet/tail mesh paths, then for each eligible NPC either edits the
/// existing armor/armature or clones surrogate armor and armatures, applying the change directly or via
/// SkyPatcher's ApplySkin in SkyPatcher asset mode. Honors patchable-race, player, preset, and block-list
/// exclusions. Runs after asset assignment in the patcher pipeline.
/// </summary>
public class VanillaBodyPathSetter
{
    private readonly IEnvironmentStateProvider _environmentStateProvider;
    private readonly PatcherState _patcherState;
    private readonly Logger _logger;
    private readonly VM_StatusBar _statusBar;
    private readonly PatchableRaceResolver _raceResolver;
    private readonly SurrogateNPCProvider _surrogateNpcProvider;
    private readonly SkyPatcherInterface _skyPatcherInterface;
    private readonly GameAssetResolver _assetResolver;
    /// <summary>Injects environment, patcher state, logging, status-bar UI, race resolution, surrogate NPCs, the SkyPatcher interface, and the (loose+BSA) asset resolver used for UBE morph-tri existence checks.</summary>
    public VanillaBodyPathSetter(IEnvironmentStateProvider environmentStateProvider, PatcherState patcherState, Logger logger, VM_StatusBar statusBar, PatchableRaceResolver raceResolver, SurrogateNPCProvider surrogateNpcProvider, SkyPatcherInterface skyPatcherInterface, GameAssetResolver assetResolver)
    {
        _environmentStateProvider = environmentStateProvider;
        _patcherState = patcherState;
        _logger = logger;
        _statusBar = statusBar;
        _raceResolver = raceResolver;
        _surrogateNpcProvider = surrogateNpcProvider;
        _skyPatcherInterface = skyPatcherInterface;
        _assetResolver = assetResolver;
    }

    /// <summary>
    /// Top-level driver: iterates all NPCs, skips those of non-patchable race, the player, presets, and
    /// block-listed NPCs, and applies <see cref="SetVanillaBodyPath"/> to the rest while advancing the status bar.
    /// For each NPC it resolves the per-NPC <see cref="NPCInfo"/> built during selection from
    /// <paramref name="npcInfoLookup"/> so the whole forced-vanilla pass can write into that NPC's verbose report.
    /// Because the report was already saved at the end of selection, it is re-saved here (via
    /// <see cref="FinalizeVanillaReport"/>) so the section appended during this pass is persisted.
    /// </summary>
    /// <param name="npcInfoLookup">FormKey -&gt; NPCInfo map retained from the selection loop, used purely for verbose
    /// logging — the record logic still operates on the original getter. An NPC missing from the map (e.g. one filtered
    /// out before its NPCInfo was used) is processed exactly as before, just without verbose output.</param>
    /// <remarks>Updates the status-bar UI and writes records to <paramref name="outputMod"/>.</remarks>
    public void SetVanillaBodyMeshPaths(ISkyrimMod outputMod, IEnumerable<INpcGetter> allNPCs, IReadOnlyDictionary<FormKey, NPCInfo> npcInfoLookup)
    {
        _statusBar.ProgressBarCurrent = 0;
        _statusBar.DispString = "Setting Vanilla Body Mesh Paths";
        var npcArray = allNPCs.ToArray();
        for (int i = 0; i < npcArray.Length; i++)
        {
            _statusBar.ProgressBarCurrent++;
            var npc = npcArray[i];

            npcInfoLookup.TryGetValue(npc.FormKey, out var npcInfo); // may be null for an NPC that never reached selection's NPCInfo use; logging is then skipped, processing is unchanged
            if (npcInfo != null)
            {
                _logger.CloseReportSubsectionsTo("Report", npcInfo); // return to the report root in case selection left a subsection open
                _logger.OpenReportSubsection("ForcedVanillaBodyPaths", npcInfo);
                Report(npcInfo, "=== Forced Vanilla Body Mesh Paths ===");
                Report(npcInfo, "Evaluating " + _logger.GetNPCLogNameString(npc) + " (race " + EditorIDHandler.GetEditorIDSafely<IRaceGetter>(npc.Race.FormKey, _environmentStateProvider.LinkCache) + ").");
            }

            if (!_raceResolver.PatchableRaceFormKeys.Contains(npc.Race.FormKey))
            {
                Report(npcInfo, "Skipped: the NPC's race is not in the patchable races list.");
                FinalizeVanillaReport(npcInfo);
                continue;
            }

            if (_patcherState.GeneralSettings.ExcludePlayerCharacter && npc.FormKey.ToString() == Skyrim.Npc.Player.FormKey.ToString())
            {
                Report(npcInfo, "Skipped: this is the player character and 'Exclude Player Character' is enabled.");
                FinalizeVanillaReport(npcInfo);
                continue;
            }

            if (_patcherState.GeneralSettings.ExcludePresets && npc.EditorID != null && npc.EditorID.Contains("Preset"))
            {
                Report(npcInfo, "Skipped: the NPC looks like a preset (EditorID contains 'Preset') and 'Exclude Presets' is enabled.");
                FinalizeVanillaReport(npcInfo);
                continue;
            }

            if (BlockedNPCs.Contains(npc.FormKey))
            {
                Report(npcInfo, "Skipped: the NPC is blocked from forced vanilla body paths (via the NPC or plugin block list).");
                FinalizeVanillaReport(npcInfo);
                continue;
            }

            SetVanillaBodyPath(npc, outputMod, npcInfo);
            FinalizeVanillaReport(npcInfo);
        }
    }

    /// <summary>Null-safe per-NPC verbose-report append: writes to the NPC's report only when an <see cref="NPCInfo"/>
    /// is available (post-selection passes may not have one) and the NPC is being verbose-logged. Leaves the general
    /// on-screen log untouched.</summary>
    private void Report(NPCInfo npcInfo, string message)
    {
        if (npcInfo != null)
        {
            _logger.LogReport(message, false, npcInfo);
        }
    }

    /// <summary>Closes the forced-vanilla report subsection and re-saves the NPC's verbose report so the entries added
    /// during this post-selection pass are persisted (selection already wrote the report once). No-op for NPCs that are
    /// not being verbose-logged.</summary>
    private void FinalizeVanillaReport(NPCInfo npcInfo)
    {
        if (npcInfo == null) { return; }
        _logger.CloseReportSubsection(npcInfo);
        _logger.SaveReport(npcInfo);
    }

    /// <summary>Clears all per-run caches (paths, blocked armatures/NPCs, duplicated armor/armatures, asset NIFs) and rebuilds the default race/gender mesh-path table.</summary>
    public void Reinitialize()
    {
        PathsByRaceGender.Clear();
        BlockedArmatures.Clear();
        BlockedNPCs.Clear();
        ArmatureDuplicatedWithVanillaPath.Clear();
        ArmorDuplicatedwithVanillaPaths.Clear();
        ArmatureNifsFromAssets.Clear();

        InitializeDefaultMeshPaths();
    }

    /// <summary>Armatures (with their primary body part) that already had a vanilla path and must be left untouched / cloned.</summary>
    private Dictionary<FormKey, BipedObjectFlag> BlockedArmatures = new();
    /// <summary>NPCs explicitly blocked from forced vanilla body paths.</summary>
    private HashSet<FormKey> BlockedNPCs = new();
    /// <summary>Cache mapping a source armature FormKey to its already-cloned vanilla-path duplicate.</summary>
    private Dictionary<FormKey, IArmorAddonGetter> ArmatureDuplicatedWithVanillaPath = new();
    /// <summary>Cache mapping a source armor FormKey to its already-cloned vanilla-path duplicate.</summary>
    private Dictionary<FormKey, IArmorGetter> ArmorDuplicatedwithVanillaPaths = new();
    /// <summary>Body NIF source paths assigned to NPCs from config files, so armatures pointing at them are not overwritten.</summary>
    private HashSet<string> ArmatureNifsFromAssets = new();

    /// <summary>
    /// Marks an NPC as blocked from vanilla body paths and records its body armatures (that currently carry a
    /// non-vanilla world-model path) in <see cref="BlockedArmatures"/>, so later cloning preserves their custom paths.
    /// </summary>
    /// <remarks>Mutates <see cref="BlockedNPCs"/> and <see cref="BlockedArmatures"/>.</remarks>
    public void RegisterBlockedFromVanillaBodyPaths(NPCInfo currentNPCinfo)
    {
        BlockedNPCs.Add(currentNPCinfo.NPC.FormKey);
        if (_environmentStateProvider.LinkCache.TryResolve<INpcGetter>(currentNPCinfo.NPC.FormKey, out var npcWinningRecord) // get the winning override - the getter being passed in is from the pre-patching winning context
            && npcWinningRecord != null && npcWinningRecord.WornArmor != null && !npcWinningRecord.WornArmor.IsNull && _environmentStateProvider.LinkCache.TryResolve<IArmorGetter>(npcWinningRecord.WornArmor.FormKey, out var armorGetter))
        {
            foreach (var armaLink in armorGetter.Armature)
            {
                if (!BlockedArmatures.ContainsKey(armaLink.FormKey) && 
                    armaLink.TryResolve(_environmentStateProvider.LinkCache, out var armaGetter) && 
                    IsValidBodyArmature(armaGetter, armorGetter, npcWinningRecord, out BipedObjectFlag primaryBodyPart) &&
                    ArmatureHasWorldModel(armaGetter, currentNPCinfo.Gender) &&
                    !ArmatureHasVanillaPath(armaGetter, primaryBodyPart, currentNPCinfo.Gender, npcWinningRecord, out _))
                {
                    BlockedArmatures.Add(armaGetter.FormKey, primaryBodyPart);
                }
            }
        }
    }
    

    /// <summary>
    /// Core per-NPC logic: resolves the NPC's effective worn armor (surrogate or output override), reuses a
    /// cached duplicated armor if present, determines whether any body armature still has a non-vanilla path,
    /// warns when an NPC with no overriding appearance mods is being modified, then routes to
    /// <see cref="SetViaNewArmor"/> (clone) or <see cref="SetInExistingArmor"/> (edit in place).
    /// </summary>
    /// <remarks>Writes records to <paramref name="outputMod"/> and/or emits SkyPatcher directives; logs.</remarks>
    private void SetVanillaBodyPath(INpcGetter npcGetter, ISkyrimMod outputMod, NPCInfo npcInfo)
    {
        if (npcGetter == null)
        {
            _logger.LogMessage("npc is null. Can't process vanilla body path.");
            return;
        }

        var currentNpc = npcGetter;
        var currentArmor = currentNpc.WornArmor;

        if (_surrogateNpcProvider.TryGetCachedSurrogate(npcGetter.FormKey, out var surrogateNpc))
        {
            currentNpc = surrogateNpc;
            currentArmor = surrogateNpc.WornArmor;
            Report(npcInfo, "Basis for evaluation: the cached surrogate NPC's worn armor.");
        }
        else if (outputMod.Npcs.Any(x => x.FormKey.Equals(npcGetter.FormKey)))
        {
            currentArmor = outputMod.Npcs.First(x => x.FormKey.Equals(npcGetter.FormKey)).WornArmor;
            Report(npcInfo, "Basis for evaluation: the patched output NPC override's worn armor.");
        }
        else
        {
            Report(npcInfo, "Basis for evaluation: the NPC's existing (unpatched) worn armor.");
        }

        if (!currentArmor.IsNull && _environmentStateProvider.LinkCache.TryResolve<IArmorGetter>(currentArmor.FormKey, out var armorGetter))
        {
            Report(npcInfo, "Worn armor: " + EditorIDHandler.GetEditorIDSafely(armorGetter) + " (" + armorGetter.FormKey.ToString() + ").");
            if (ArmorDuplicatedwithVanillaPaths.ContainsKey(currentArmor.FormKey))
            {
                var duplicatedArmor = ArmorDuplicatedwithVanillaPaths[currentArmor.FormKey];
                if (duplicatedArmor == null)
                {
                    _logger.LogMessage($"Vanilla body path setter: duplicated armor is null. Npc {currentNpc.FormKey.ToString()} Template armor: {currentArmor.FormKey.ToString()}");
                    Report(npcInfo, "Skipped: a previously-created vanilla-path armor for this worn armor was unexpectedly null.");
                    return;
                }

                Report(npcInfo, "Reusing the vanilla-path armor already created for this worn armor (" + EditorIDHandler.GetEditorIDSafely(duplicatedArmor) + " | " + duplicatedArmor.FormKey.ToString() + ").");
                if (_patcherState.TexMeshSettings.bSkyPatcherModeAssets)
                {
                    _skyPatcherInterface.ApplySkin(npcGetter.FormKey, duplicatedArmor.FormKey);
                    Report(npcInfo, "Applied via a SkyPatcher ApplySkin directive.");
                }
                else
                {
                    var npc = outputMod.Npcs.GetOrAddAsOverride(npcGetter);
                    npc.WornArmor.SetTo(duplicatedArmor.FormKey);
                    Report(npcInfo, "Applied by overriding the NPC's WornArmor to that duplicated armor.");
                }
                return;
            }

            if (armorGetter.Armature == null) { Report(npcInfo, "Skipped: the worn armor has no armature (ARMA) entries."); return; }

            var currentGender = NPCInfo.GetGender(npcGetter);

            // Read-only diagnostic pass (verbose-logged NPCs only): explain how each armature evaluates against the
            // eligibility rules. It re-runs the same predicates the decision logic uses, mutates nothing, and so cannot
            // change patch behavior; it exists purely to make "why was this body left un-destandaloned" visible.
            if (npcInfo != null && npcInfo.Report.LogCurrentNPC)
            {
                _logger.LogReport("Evaluating " + armorGetter.Armature.Count + " armature(s) for the NPC's " + currentGender + " body meshes:", false, npcInfo);
                foreach (var armaLinkDiag in armorGetter.Armature)
                {
                    if (armaLinkDiag.TryResolve(_environmentStateProvider.LinkCache, out var armaGetterDiag))
                    {
                        LogArmatureEvaluation(npcInfo, armaGetterDiag, armorGetter, currentNpc, currentGender);
                    }
                    else
                    {
                        _logger.LogReport("  Armature " + armaLinkDiag.FormKey.ToString() + ": could not be resolved from the link cache.", false, npcInfo);
                    }
                }
            }

            bool hasNonVanillaBodyPaths = false;
            foreach (var armaLink in armorGetter.Armature)
            {
                if (armaLink.TryResolve(_environmentStateProvider.LinkCache, out var armaGetter) && 
                    IsValidBodyArmature(armaGetter, armorGetter, currentNpc, out BipedObjectFlag primaryBodyPart) &&
                    ArmatureHasWorldModel(armaGetter, NPCInfo.GetGender(currentNpc)) &&
                    !ArmatureHasVanillaPath(armaGetter, primaryBodyPart, currentGender, currentNpc, out _) &&
                    !ArmaturePathAssignedFromConfig(armaGetter, currentGender))
                {
                    hasNonVanillaBodyPaths = true;
                    break;
                }
            }

            if (!hasNonVanillaBodyPaths)
            {
                Report(npcInfo, "Result: no armature requires a forced vanilla path (each is already vanilla/UBE, was config-assigned, or is not an applicable body armature). NPC left unmodified.");
                return;
            }


            var registration = LoquiRegistration.StaticRegister.GetRegister(npcGetter.GetType());
            var contexts = _environmentStateProvider.LinkCache?.ResolveAllContexts(npcGetter.FormKey, registration.GetterType).ToList() ?? new(); // note: ResolveAllContexts directly off npcGetter returns only the context from SynthEBD.esp

            // Diagnostic warning only (no behavior change): fire when this NPC has NO appearance-override mods
            // yet is getting the vanilla/race body, so the user can check whether it should be blocked.
            // "No override mods" == the context chain is just the base master, plus SynthEBD's own output
            // override which is added ONLY in non-SkyPatcher mode (via GetOrAddAsOverride above; SkyPatcher mode
            // uses ApplySkin/ini and adds NO override record). Hence no-override == Count 2 in non-SkyPatcher mode
            // and Count 1 in SkyPatcher mode. The operator precedence below is INTENTIONAL, not a bug:
            // (!SkyPatcher && Count==2) || Count==1 warns for exactly those no-override cases in BOTH modes and
            // correctly excludes SkyPatcher Count==2 (= base + a real override mod). Do NOT rewrite this as
            // !SkyPatcher && (Count==2 || Count==1): that suppresses the warning for every no-override NPC in
            // SkyPatcher mode, where Count==1 is the normal case.
            if (!_patcherState.TexMeshSettings.bSkyPatcherModeAssets && contexts.Count == 2 || contexts.Count == 1)
            {
                string raceName = "No Race";
                if (npcGetter.Race != null && _environmentStateProvider.LinkCache.TryResolve(npcGetter.Race, out var raceGetter))
                {
                    raceName = EditorIDHandler.GetEditorIDSafely(raceGetter);
                }
                _logger.LogMessage(_logger.GetNPCLogNameString(npcGetter) + " is getting its body mesh path set to that of its race (" + raceName + ") despite having no overriding appearance mods. Make sure this NPC does not need to be blocked from vanilla body paths.");
                Report(npcInfo, "WARNING: this NPC has no overriding appearance mods yet is being given its race's vanilla body (race " + raceName + "). If that is incorrect, block it from forced vanilla body paths.");
            }

            bool hasBlockedArmature = BlockedArmatures.Keys.Intersect(armorGetter.Armature.Select(x => x.FormKey).ToArray()).Any();

            if (hasBlockedArmature || _patcherState.TexMeshSettings.bSkyPatcherModeAssets && !_surrogateNpcProvider.TryGetImportedFormKey(armorGetter.FormKey, out _))
            {
                Report(npcInfo, "Applying via a cloned surrogate armor" + (hasBlockedArmature ? " (one or more armatures are block-listed and must be preserved on a clone)." : " (SkyPatcher assets mode)."));
                SetViaNewArmor(outputMod, armorGetter, npcGetter, currentGender, npcInfo);
            }
            else
            {
                Report(npcInfo, "Applying by editing the NPC's existing armatures in place.");
                SetInExistingArmor(outputMod, armorGetter, npcGetter, currentGender, npcInfo);
            }
        }
        else
        {
            Report(npcInfo, "Skipped: the NPC has no resolvable worn armor.");
        }
    }

    /// <summary>
    /// Creates a vanilla-path worn armor for the NPC by cloning the template armor (or overriding the surrogate's
    /// armor in SkyPatcher mode), suffixing EditorIDs with "_VanillaBodyPath", caching the result, and rewriting
    /// each body armature to a cloned/overridden armature carrying the race-default world-model path. Routes the
    /// NPC to the new armor via ApplySkin (SkyPatcher mode) or a WornArmor override.
    /// </summary>
    /// <remarks>Writes armor/armature records, mutates the duplicate caches, may emit SkyPatcher directives, and logs.</remarks>
    private void SetViaNewArmor(ISkyrimMod outputMod, IArmorGetter templateArmorGetter, INpcGetter npcGetter, Gender currentGender, NPCInfo npcInfo)
    {
        Armor wornArmor;
        var implicits = Implicits.Get(outputMod.GameRelease);

        if (_patcherState.TexMeshSettings.bSkyPatcherModeAssets)
        {
            if (!_surrogateNpcProvider.TryGetSurrogateNpc(npcGetter, out var surrogateNpc))
            {
                _logger.LogMessage($"Cannot set vanilla body paths for NPC {npcGetter.FormKey} because surrogate creation failed.");
                Report(npcInfo, "Skipped: could not create a surrogate NPC to carry the vanilla-path armor.");
                return;
            }

            if (surrogateNpc.WornArmor == null || surrogateNpc.WornArmor.IsNull ||
                implicits.BaseMasters.Contains(surrogateNpc.WornArmor.FormKey.ModKey))
            {
                // This NPC already has vanilla armor paths. No need to warn user.
                Report(npcInfo, "No change needed: the surrogate's worn armor is null or already a base-game (vanilla) armor.");
                return;
            }

            if (!_surrogateNpcProvider.TryGetImportedFormKey(npcGetter.WornArmor.FormKey, out _))
            {
                _logger.LogMessage($"Cannot set vanilla body paths in armor {npcGetter.WornArmor.FormKey} of NPC {npcGetter.FormKey} because the armor's source mod is blocked from import in Avoid Override Mode");
                Report(npcInfo, "Skipped: the worn armor's source mod is blocked from import in Avoid Override Mode.");
                return;
            }
            _skyPatcherInterface.ApplySkin(npcGetter.FormKey, surrogateNpc.WornArmor.FormKey);
            wornArmor = outputMod.Armors.GetOrAddAsOverride(surrogateNpc.WornArmor, _environmentStateProvider.LinkCache);
            Report(npcInfo, "SkyPatcher mode: emitted ApplySkin to the surrogate's worn armor and overrode it for path rewriting.");
        }
        else
        {
            if (npcGetter.WornArmor == null || npcGetter.WornArmor.IsNull ||
                implicits.BaseMasters.Contains(npcGetter.WornArmor.FormKey.ModKey))
            {
                // This NPC already has vanilla armor paths. No need to warn user.
                Report(npcInfo, "No change needed: the NPC's worn armor is null or already a base-game (vanilla) armor.");
                return;
            }

            wornArmor = outputMod.Armors.AddNew();
            wornArmor.DeepCopyIn(templateArmorGetter);
            var npc = outputMod.Npcs.GetOrAddAsOverride(npcGetter);
            npc.WornArmor.SetTo(wornArmor);
            Report(npcInfo, "Cloned the worn armor to a new record and pointed the NPC's WornArmor at the clone.");
        }

        if (wornArmor.EditorID == null)
        {
            wornArmor.EditorID = "_VanillaBodyPath";
        }
        else
        {
            wornArmor.EditorID += "_VanillaBodyPath";
        }

        ArmorDuplicatedwithVanillaPaths.Add(templateArmorGetter.FormKey, wornArmor);
        Report(npcInfo, "Vanilla-path armor: " + wornArmor.EditorID + " (" + wornArmor.FormKey.ToString() + "). Rewriting its body armatures:");

        for (int i = 0; i < wornArmor.Armature.Count; i++)
        {
            var armaLinkGetter = wornArmor.Armature[i];
            if (!_environmentStateProvider.LinkCache.TryResolve<IArmorAddonGetter>(armaLinkGetter.FormKey, out var armaGetter))
            {
                _logger.LogMessage("Warning: Could not evaluate armature " + armaLinkGetter.FormKey.ToString() + " for vanilla body mesh path - armature could not be resolved.");
                Report(npcInfo, "  Armature " + armaLinkGetter.FormKey.ToString() + ": could not be resolved -> left unchanged.");
            }
            else if (ArmatureDuplicatedWithVanillaPath.ContainsKey(armaLinkGetter.FormKey)) // set the previously duplicated armature from cache
            {
                var newSetter = armaLinkGetter.AsSetter();
                newSetter.SetTo(ArmatureDuplicatedWithVanillaPath[armaLinkGetter.FormKey]);
                wornArmor.Armature[i] = newSetter;
                Report(npcInfo, "  Armature " + EditorIDHandler.GetEditorIDSafely(armaGetter) + ": pointed at the previously cloned vanilla-path armature.");
            }
            else if (BlockedArmatures.ContainsKey(armaLinkGetter.FormKey) && GetArmatureVanillaPath(BlockedArmatures[armaLinkGetter.FormKey], currentGender, npcGetter, out string vanillaPath))
            {
                ArmorAddon clonedArmature = outputMod.ArmorAddons.AddNew();
                clonedArmature.DeepCopyIn(armaGetter);
                if (clonedArmature.EditorID == null)
                {
                    clonedArmature.EditorID = "_VanillaBodyPath";
                }
                else
                {
                    clonedArmature.EditorID += "_VanillaBodyPath";
                }
                var newSetter = armaLinkGetter.AsSetter();
                newSetter.SetTo(clonedArmature);
                wornArmor.Armature[i] = newSetter;
                SetArmatureVanillaPath(clonedArmature, currentGender, vanillaPath);
                ArmatureDuplicatedWithVanillaPath.Add(armaGetter.FormKey, clonedArmature);
                Report(npcInfo, "  Armature " + EditorIDHandler.GetEditorIDSafely(armaGetter) + ": block-listed, so cloned to " + clonedArmature.EditorID + " and set its " + currentGender + " mesh to '" + vanillaPath + "'.");
            }
            else if (IsValidBodyArmature(armaGetter, wornArmor, npcGetter, out BipedObjectFlag primaryBodyPart) &&
                ArmatureHasWorldModel(armaGetter, currentGender) &&
                !ArmatureHasVanillaPath(armaGetter, primaryBodyPart, currentGender, npcGetter, out string vanillaPathB))
            {
                var armature = outputMod.ArmorAddons.GetOrAddAsOverride(armaGetter);
                SetArmatureVanillaPath(armature, currentGender, vanillaPathB);
                Report(npcInfo, "  Armature " + EditorIDHandler.GetEditorIDSafely(armaGetter) + " [" + primaryBodyPart + "]: set its " + currentGender + " mesh to '" + vanillaPathB + "'.");
            }
        }
    }

    /// <summary>
    /// Sets the race-default world-model path directly on the NPC's existing body armatures (overriding each in
    /// the output mod) for those that are valid body armatures with a world model and a non-vanilla path.
    /// </summary>
    /// <remarks>Writes armature overrides to <paramref name="outputMod"/> and logs unresolved armatures.</remarks>
    private void SetInExistingArmor(ISkyrimMod outputMod, IArmorGetter currentArmorGetter, INpcGetter npcGetter, Gender currentGender, NPCInfo npcInfo)
    {
        for (int i = 0; i < currentArmorGetter.Armature.Count; i++)
        {
            var armaLinkGetter = currentArmorGetter.Armature[i];
            if (!_environmentStateProvider.LinkCache.TryResolve<IArmorAddonGetter>(armaLinkGetter.FormKey, out var armaGetter))
            {
                _logger.LogMessage("Warning: Could not evaluate armature " + armaLinkGetter.FormKey.ToString() + " for vanilla body mesh path - armature could not be resolved.");
                Report(npcInfo, "  Armature " + armaLinkGetter.FormKey.ToString() + ": could not be resolved -> left unchanged.");
                continue;
            }
            if (IsValidBodyArmature(armaGetter, currentArmorGetter, npcGetter, out BipedObjectFlag primaryBodyPart) &&
                ArmatureHasWorldModel(armaGetter, currentGender))
            {
                if (!ArmatureHasVanillaPath(armaGetter, primaryBodyPart, currentGender, npcGetter, out string vanillaPath))
                {
                    var armature = outputMod.ArmorAddons.GetOrAddAsOverride(armaGetter);
                    SetArmatureVanillaPath(armature, currentGender, vanillaPath);
                    Report(npcInfo, "  Armature " + EditorIDHandler.GetEditorIDSafely(armaGetter) + " [" + primaryBodyPart + "]: set its " + currentGender + " mesh to '" + vanillaPath + "'.");
                }
                else
                {
                    Report(npcInfo, "  Armature " + EditorIDHandler.GetEditorIDSafely(armaGetter) + " [" + primaryBodyPart + "]: already at the vanilla/UBE path (or unresolvable) -> left unchanged.");
                }
            }
        }
    }

    /// <summary>Writes <paramref name="updatedPath"/> to the gender-appropriate world-model File of the armature.</summary>
    private void SetArmatureVanillaPath(ArmorAddon armature, Gender currentGender, string updatedPath)
    {
        switch (currentGender)
        {
            case Gender.Female: armature.WorldModel.Female.File = updatedPath; break;
            case Gender.Male: armature.WorldModel.Male.File = updatedPath; break;
        }
    }

    /// <summary>
    /// Looks up the cached race/gender/body-part default mesh path for the NPC's race, logging an error and
    /// returning false if the race or the requested entry is missing from <see cref="PathsByRaceGender"/>.
    /// </summary>
    /// <param name="vanillaPath">The resolved vanilla mesh path, or empty on failure.</param>
    /// <returns>True if a path was found.</returns>
    private bool GetArmatureVanillaPath(BipedObjectFlag currentBodyPart, Gender currentGender, INpcGetter npcGetter, out string vanillaPath)
    {
        vanillaPath = "";
        if (npcGetter.Race == null || npcGetter.Race.IsNull)
        {
            _logger.LogError("Vanilla path setter: NPC " + EditorIDHandler.GetEditorIDSafely(npcGetter) + " has no Race record.");
            return false;
        }
        else if (!PathsByRaceGender.ContainsKey(npcGetter.Race.FormKey))
        {
            _logger.LogError("Vanilla path setter cannot find a race (" + EditorIDHandler.GetEditorIDSafely<IRaceGetter>(npcGetter.Race.FormKey, _environmentStateProvider.LinkCache) + ") for NPC " + EditorIDHandler.GetEditorIDSafely(npcGetter));
            return false;
        }
        else if (!PathsByRaceGender[npcGetter.Race.FormKey].ContainsKey(currentGender))
        {
            _logger.LogError("Vanilla path setter cannot find data for " + currentGender + " NPCs of race " + EditorIDHandler.GetEditorIDSafely<IRaceGetter>(npcGetter.Race.FormKey, _environmentStateProvider.LinkCache) + " (NPC is " + EditorIDHandler.GetEditorIDSafely(npcGetter) + ")");
            return false;
        }
        else if (!PathsByRaceGender[npcGetter.Race.FormKey][currentGender].ContainsKey(currentBodyPart))
        {
            _logger.LogError("Vanilla path setter cannot find " + currentBodyPart + " data for " + currentGender + " NPCs of race " + EditorIDHandler.GetEditorIDSafely<IRaceGetter>(npcGetter.Race.FormKey, _environmentStateProvider.LinkCache) + " (NPC is " + EditorIDHandler.GetEditorIDSafely(npcGetter) + ")");
            return false;
        }
        else
        {
            vanillaPath = PathsByRaceGender[npcGetter.Race.FormKey][currentGender][currentBodyPart];
            return true;
        }
    }
    /// <summary>
    /// Returns whether the armature's gender-appropriate world-model path already equals the race-default path
    /// (case-insensitive). When the default path cannot be resolved, returns true so the armature is skipped.
    /// </summary>
    /// <param name="vanillaPath">The race-default path used for the comparison.</param>
    private bool ArmatureHasVanillaPath(IArmorAddonGetter armaGetter, BipedObjectFlag currentBodyPart, Gender currentGender, INpcGetter npcGetter, out string vanillaPath) // function assumes that IsBodyArmature() has been called so potential null refs have been checked.
    {
        // UBE-aware handling: when enabled, a UBE body (recognized by its "!UBE" mesh path) is either preserved
        // (its morph .tri is present, so it morphs correctly in place) or treated as needing the canonical UBE base
        // body (its .tri is missing). Returning true here = "already correct, leave alone"; returning false hands
        // the caller the UBE-canonical path to write. Non-UBE armatures fall through to the race-default logic below.
        if (_patcherState.TexMeshSettings.bForceVanillaBodyMeshPath
            && _patcherState.TexMeshSettings.bAllowUBEBodyPaths
            && TryEvaluateUbeBody(armaGetter, currentBodyPart, currentGender, out bool preserveUbe, out string ubeCanonicalPath))
        {
            if (preserveUbe)
            {
                vanillaPath = GetWorldModelPath(armaGetter, currentGender) ?? string.Empty;
                return true;
            }
            vanillaPath = ubeCanonicalPath;
            var currentUbePath = GetWorldModelPath(armaGetter, currentGender);
            return currentUbePath != null && currentUbePath.Equals(ubeCanonicalPath, StringComparison.OrdinalIgnoreCase);
        }

        if (!GetArmatureVanillaPath(currentBodyPart, currentGender, npcGetter, out vanillaPath))
        {
            return true; // can't evaluate, so can't operate on this armature - assume it already has its vanilla path
        }

        switch (currentGender)
        {
            case Gender.Female: return armaGetter.WorldModel?.Female?.File.GivenPath.ToString().Equals(vanillaPath, StringComparison.OrdinalIgnoreCase) ?? true;
            case Gender.Male: return armaGetter.WorldModel?.Male?.File.GivenPath.ToString().Equals(vanillaPath, StringComparison.OrdinalIgnoreCase) ?? true;
            default: return true;
        }
    }

    /// <summary>
    /// Records the .nif source paths of all meshes assigned from the given asset combinations into
    /// <see cref="ArmatureNifsFromAssets"/>, so armatures pointing at them are not reset to vanilla.
    /// </summary>
    public void RegisterAssetAssignedMeshes(List<SubgroupCombination> assignedCombinations)
    {
        ArmatureNifsFromAssets.UnionWith(assignedCombinations.SelectMany(x => x.ContainedSubgroups).SelectMany(x => x.Paths).Select(x => x.Source).Where(x => x.EndsWith(".nif", StringComparison.OrdinalIgnoreCase)));
    }

    /// <summary>Returns true if the armature's gender world-model path is one of the NIF paths assigned from a config (so it should be left alone).</summary>
    private bool ArmaturePathAssignedFromConfig(IArmorAddonGetter armaGetter, Gender currentGender)
    {
        if (currentGender == Gender.Female && armaGetter.WorldModel?.Female?.File.GivenPath != null && ArmatureNifsFromAssets.Contains(armaGetter.WorldModel.Female.File.GivenPath) ||
            (currentGender == Gender.Male && armaGetter.WorldModel?.Male?.File.GivenPath != null && ArmatureNifsFromAssets.Contains(armaGetter.WorldModel.Male.File.GivenPath)))
        {
            return true;
        }
        return false;
    }

    /// <summary>Returns true if the armature has a world model for the given gender.</summary>
    private bool ArmatureHasWorldModel(IArmorAddonGetter armaGetter, Gender currentGender)
    {
        if (armaGetter.WorldModel == null)
        {
            return false;
        }
        switch (currentGender)
        {
            case Gender.Female: return armaGetter.WorldModel.Female != null;
            case Gender.Male: return armaGetter.WorldModel.Male != null;
            default: return false;
        }
    }

    /// <summary>
    /// Determines whether an armature is a body armature eligible for vanilla-path handling: it is a body part,
    /// its armor is not flagged ArmorClothing, and the armature applies to the NPC's race (directly or via
    /// additional races).
    /// </summary>
    /// <param name="primaryBodyPart">The matched body-part biped flag.</param>
    private bool IsValidBodyArmature(IArmorAddonGetter armaGetter, IArmorGetter armorGetter, INpcGetter currentNPC, out BipedObjectFlag primaryBodyPart)
    {
        return IsBodyPart(armaGetter, out primaryBodyPart, currentNPC) &&
            (armorGetter.Keywords == null || !armorGetter.Keywords.Contains(Skyrim.Keyword.ArmorClothing)) &&
            ((armaGetter.Race != null && armaGetter.Race.Equals(currentNPC.Race)) ||
            (armaGetter.AdditionalRaces != null && armaGetter.AdditionalRaces.Contains(currentNPC.Race)));
    }
    
    /// <summary>Returns true if the armature's body template covers one of the tracked body parts (see <see cref="BodyFlags"/>), outputting the first match.</summary>
    /// <param name="primaryBodyPart">The first matching body-part flag, or 0 if none.</param>
    private bool IsBodyPart(IArmorAddonGetter armaGetter, out BipedObjectFlag primaryBodyPart, INpcGetter currentNpcGetter)
    {
        primaryBodyPart = 0;
        if (armaGetter.BodyTemplate != null)
        {
            foreach (var flag in BodyFlags)
            {
                if (armaGetter.BodyTemplate.FirstPersonFlags.HasFlag(flag))
                {
                    primaryBodyPart = flag;
                    return true;
                }
            }
        }
        return false;
    }

    /// <summary>Returns true (and logs) if the NPC is blocked from forced vanilla body paths via the NPC or plugin block lists.</summary>
    public bool IsBlockedForVanillaBodyPaths(NPCInfo npcInfo)
    {
        if (npcInfo.BlockedNPCEntry.VanillaBodyPath)
        {
            _logger.LogReport("Current NPC is blocked from Forced Vanilla Body Mesh Path via the NPC block list", false, npcInfo);
            return true;
        }
        else if (npcInfo.BlockedPluginEntry.VanillaBodyPath)
        {
            _logger.LogReport("Current NPC is blocked from Forced Vanilla Body Mesh Path via the Plugin block list", false, npcInfo);
            return true;
        }
        return false;
    }
    /// <summary>
    /// Read-only diagnostic: writes a per-armature explanation of every eligibility sub-check to the NPC's verbose
    /// report (no-op unless the NPC is being verbose-logged). It re-evaluates the same predicates the decision logic
    /// uses — <see cref="IsBodyPart"/>, the ArmorClothing keyword, the ARMA race match, world-model presence,
    /// config-assignment, and vanilla-path resolution — without mutating anything, so it cannot change patch behavior.
    /// Its purpose is to make "why was this body left un-de-standaloned?" visible; the most common answer is an ARMA
    /// whose Race / AdditionalRaces do not include the NPC's race.
    /// </summary>
    private void LogArmatureEvaluation(NPCInfo npcInfo, IArmorAddonGetter armaGetter, IArmorGetter armorGetter, INpcGetter npcGetter, Gender currentGender)
    {
        if (npcInfo == null || !npcInfo.Report.LogCurrentNPC) { return; }

        string armaName = EditorIDHandler.GetEditorIDSafely(armaGetter) + " (" + armaGetter.FormKey.ToString() + ")";

        if (!IsBodyPart(armaGetter, out BipedObjectFlag primaryBodyPart, npcGetter))
        {
            _logger.LogReport("  Armature " + armaName + ": not a tracked body armature (its BodyTemplate flags no Body/Hands/Feet/Tail) -> skipped.", false, npcInfo);
            return;
        }

        bool isClothing = armorGetter.Keywords != null && armorGetter.Keywords.Contains(Skyrim.Keyword.ArmorClothing);
        bool raceMatch = (armaGetter.Race != null && armaGetter.Race.Equals(npcGetter.Race)) ||
                         (armaGetter.AdditionalRaces != null && armaGetter.AdditionalRaces.Contains(npcGetter.Race));
        bool hasWorldModel = ArmatureHasWorldModel(armaGetter, currentGender);
        bool assignedFromConfig = ArmaturePathAssignedFromConfig(armaGetter, currentGender);
        string currentPath = GetWorldModelPath(armaGetter, currentGender) ?? "(no " + currentGender + " world model)";

        var sb = new StringBuilder();
        sb.AppendLine("  Armature " + armaName + " [" + primaryBodyPart + "]:");
        sb.AppendLine("    Current " + currentGender + " mesh: " + currentPath);
        if (isClothing) { sb.AppendLine("    Worn armor is flagged ArmorClothing -> EXCLUDED."); }
        sb.AppendLine("    Race applies to this NPC: " + raceMatch + (raceMatch ? "" : " -> EXCLUDED (most common cause of a body not being de-standaloned)"));
        if (!raceMatch)
        {
            string armaRace = armaGetter.Race != null ? EditorIDHandler.GetEditorIDSafely<IRaceGetter>(armaGetter.Race.FormKey, _environmentStateProvider.LinkCache) : "(null)";
            int addlCount = armaGetter.AdditionalRaces != null ? armaGetter.AdditionalRaces.Count : 0;
            string npcRace = EditorIDHandler.GetEditorIDSafely<IRaceGetter>(npcGetter.Race.FormKey, _environmentStateProvider.LinkCache);
            sb.AppendLine("      ARMA Race = " + armaRace + "; ARMA AdditionalRaces count = " + addlCount + "; NPC Race = " + npcRace + ".");
        }
        if (!hasWorldModel) { sb.AppendLine("    No " + currentGender + " world model -> nothing to change."); }
        if (assignedFromConfig) { sb.AppendLine("    Mesh path was assigned by an asset config -> PRESERVED."); }

        if (!isClothing && raceMatch && hasWorldModel)
        {
            if (IsUbePath(currentPath))
            {
                sb.AppendLine("    Current mesh is a UBE path; UBE-aware handling applies (Allow UBE = " + _patcherState.TexMeshSettings.bAllowUBEBodyPaths + ").");
            }
            else if (!GetArmatureVanillaPath(primaryBodyPart, currentGender, npcGetter, out string racePath))
            {
                sb.AppendLine("    Could not resolve a race-default mesh for this race/gender/body part -> left unchanged (see the main log for the specific reason).");
            }
            else
            {
                bool already = ArmatureHasVanillaPath(armaGetter, primaryBodyPart, currentGender, npcGetter, out _);
                sb.AppendLine("    Race-default mesh: " + racePath);
                if (already) { sb.AppendLine("    Already at the race-default path -> left unchanged."); }
                else if (assignedFromConfig) { sb.AppendLine("    Differs from race default, but PRESERVED because it was config-assigned."); }
                else { sb.AppendLine("    Differs from race default -> WILL be forced to the race-default path."); }
            }
        }

        _logger.LogReport(sb.ToString().TrimEnd(), false, npcInfo);
    }

    /// <summary>
    /// Builds <see cref="PathsByRaceGender"/> by reading each patchable race's skin armor armatures and recording
    /// the male/female world-model body/hands/feet/tail mesh paths as that race's vanilla defaults.
    /// </summary>
    private void InitializeDefaultMeshPaths()
    {
        foreach (var raceFK in _raceResolver.PatchableRaceFormKeys)
        {
            PathsByRaceGender.Add(raceFK, new Dictionary<Gender, Dictionary<BipedObjectFlag, string>>());
            PathsByRaceGender[raceFK].Add(Gender.Male, new Dictionary<BipedObjectFlag, string>());
            PathsByRaceGender[raceFK].Add(Gender.Female, new Dictionary<BipedObjectFlag, string>());

            if (_environmentStateProvider.LinkCache.TryResolve<IRaceGetter>(raceFK, out var raceGetter) && raceGetter.Skin != null && !raceGetter.Skin.IsNull && _environmentStateProvider.LinkCache.TryResolve<IArmorGetter>(raceGetter.Skin.FormKey, out var skinGetter) && skinGetter.Armature != null)
            {
                foreach (var armaLink in skinGetter.Armature)
                {
                    if (armaLink.TryResolve(_environmentStateProvider.LinkCache, out var armaGetter)
                        && armaGetter.BodyTemplate != null
                        && (armaGetter.Race != null && armaGetter.Race.FormKey.Equals(raceFK) || armaGetter.AdditionalRaces != null && armaGetter.AdditionalRaces.Contains(raceGetter)))
                    {
                        foreach (var bodyFlag in BodyFlags)
                        {
                            if (armaGetter.BodyTemplate.FirstPersonFlags.HasFlag(bodyFlag))
                            {
                                if (!PathsByRaceGender[raceFK][Gender.Male].ContainsKey(bodyFlag) && armaGetter.WorldModel != null && armaGetter.WorldModel.Male != null && armaGetter.WorldModel.Male.File != null)
                                {
                                    PathsByRaceGender[raceFK][Gender.Male].Add(bodyFlag, armaGetter.WorldModel.Male.File);
                                }
                                if (!PathsByRaceGender[raceFK][Gender.Female].ContainsKey(bodyFlag) && armaGetter.WorldModel != null && armaGetter.WorldModel.Female != null && armaGetter.WorldModel.Female.File != null)
                                {
                                    PathsByRaceGender[raceFK][Gender.Female].Add(bodyFlag, armaGetter.WorldModel.Female.File);
                                }
                            }
                        }
                    }
                }
            }
        }
    }

    /// <summary>Cached race-default mesh paths, keyed by race FormKey then gender then body-part flag.</summary>
    private Dictionary<FormKey, Dictionary<Gender, Dictionary<BipedObjectFlag, string>>> PathsByRaceGender = new();

    /// <summary>The biped body-part flags treated as "body" meshes for vanilla-path handling (body, hands, feet, tail).</summary>
    public static HashSet<BipedObjectFlag> BodyFlags = new()
    {
        BipedObjectFlag.Body,
        BipedObjectFlag.Hands,
        BipedObjectFlag.Feet,
        BipedObjectFlag.Tail
    };

    /// <summary>
    /// Canonical UBE base-body mesh paths (the "Zeroed Sliders - UBE" install) per body slot, used as the remap
    /// target when a UBE body's morph .tri is missing. Female-only; UBE has no male body, so a UBE-pathed male
    /// armature (which should not occur) is preserved rather than remapped. Paths are meshes-relative (no "meshes\").
    /// </summary>
    private static readonly Dictionary<BipedObjectFlag, string> UbeCanonicalFemalePaths = new()
    {
        { BipedObjectFlag.Body, @"!UBE\Body\femalebody_tangent.nif" },
        { BipedObjectFlag.Feet, @"!UBE\Feet\femalefeet_tangent.nif" },
        { BipedObjectFlag.Hands, @"!UBE\Hands\femalehands_tangent.nif" },
    };

    /// <summary>Returns true if a mesh path contains the UBE marker folder token "!UBE" (case-insensitive).</summary>
    private static bool IsUbePath(string? path) =>
        !string.IsNullOrEmpty(path) && path.Contains("!UBE", StringComparison.OrdinalIgnoreCase);

    /// <summary>Returns the gender-appropriate world-model mesh path of an armature (meshes-relative), or null.</summary>
    private static string? GetWorldModelPath(IArmorAddonGetter armaGetter, Gender currentGender)
    {
        var model = currentGender switch
        {
            Gender.Female => armaGetter.WorldModel?.Female,
            Gender.Male => armaGetter.WorldModel?.Male,
            _ => null
        };
        return model?.File.GivenPath.ToString();
    }

    /// <summary>
    /// Evaluates whether an armature's gender world-model path is a UBE body ("!UBE" in the path) and, if so, whether
    /// it should be preserved (its sibling morph .tri exists on disk) or remapped to the canonical UBE base body for
    /// its slot (the .tri is missing). Returns false for non-UBE armatures so they fall through to race-default handling.
    /// </summary>
    /// <param name="preserve">True = leave the UBE body's path untouched; false = rewrite it to <paramref name="canonicalUbePath"/>.</param>
    /// <param name="canonicalUbePath">The canonical UBE base mesh path to remap to (meaningful only when <paramref name="preserve"/> is false).</param>
    /// <returns>True if the armature is a UBE body handled here.</returns>
    private bool TryEvaluateUbeBody(IArmorAddonGetter armaGetter, BipedObjectFlag currentBodyPart, Gender currentGender, out bool preserve, out string canonicalUbePath)
    {
        preserve = true;
        canonicalUbePath = string.Empty;

        var currentPath = GetWorldModelPath(armaGetter, currentGender);
        if (!IsUbePath(currentPath))
        {
            return false; // not a UBE body - normal race-default handling applies
        }

        if (UbeSiblingTriExists(currentPath!))
        {
            return true; // morph .tri present - preserve the UBE body in place
        }

        // .tri missing: the body cannot be morphed where it is, so remap to the canonical UBE base body for its slot.
        // If there is no canonical target (e.g. a male UBE path, or an unmapped slot), preserve rather than risk a
        // wrong remap.
        if (UbeCanonicalFemalePaths.TryGetValue(currentBodyPart, out var canonical))
        {
            preserve = false;
            canonicalUbePath = canonical;
        }
        return true;
    }

    /// <summary>
    /// Returns whether the sibling morph .tri for a UBE body mesh exists (loose file or any BSA, via the asset
    /// resolver). On any failure to derive or resolve the path, returns true (assume present) so a working UBE body
    /// is never wrongly remapped.
    /// </summary>
    private bool UbeSiblingTriExists(string worldModelMeshPath)
    {
        try
        {
            var triSubPath = DeriveSiblingTriPath(worldModelMeshPath);
            if (string.IsNullOrEmpty(triSubPath))
            {
                return true;
            }
            // GameAssetResolver expects a game-relative path including the "meshes\" prefix.
            var resolved = _assetResolver.ResolveAssetPath(Path.Combine("meshes", triSubPath));
            return !string.IsNullOrWhiteSpace(resolved);
        }
        catch (Exception ex)
        {
            _logger.LogMessage("Vanilla body path setter: could not check the UBE morph .tri for '" + worldModelMeshPath + "': " + ex.Message + ". Preserving the body.");
            return true;
        }
    }

    /// <summary>
    /// Derives the canonical sibling .tri path for a body mesh path: strips a trailing _0/_1 weight suffix and swaps
    /// the extension to .tri (femalebody_tangent_1.nif -> femalebody_tangent.tri), preserving the directory.
    /// </summary>
    private static string DeriveSiblingTriPath(string meshPath)
    {
        if (string.IsNullOrEmpty(meshPath)) return string.Empty;
        var dir = Path.GetDirectoryName(meshPath) ?? string.Empty;
        var fileName = Path.GetFileNameWithoutExtension(meshPath);
        if (fileName.EndsWith("_0", StringComparison.OrdinalIgnoreCase) || fileName.EndsWith("_1", StringComparison.OrdinalIgnoreCase))
        {
            fileName = fileName.Substring(0, fileName.Length - 2);
        }
        return Path.Combine(dir, fileName + ".tri");
    }
}
