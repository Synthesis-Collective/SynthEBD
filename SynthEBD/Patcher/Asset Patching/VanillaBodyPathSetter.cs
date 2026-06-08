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
    /// <summary>Injects environment, patcher state, logging, status-bar UI, race resolution, surrogate NPCs, and the SkyPatcher interface.</summary>
    public VanillaBodyPathSetter(IEnvironmentStateProvider environmentStateProvider, PatcherState patcherState, Logger logger, VM_StatusBar statusBar, PatchableRaceResolver raceResolver, SurrogateNPCProvider surrogateNpcProvider, SkyPatcherInterface skyPatcherInterface)
    {
        _environmentStateProvider = environmentStateProvider;
        _patcherState = patcherState;
        _logger = logger;
        _statusBar = statusBar;
        _raceResolver = raceResolver;
        _surrogateNpcProvider = surrogateNpcProvider;
        _skyPatcherInterface = skyPatcherInterface;
    }

    /// <summary>
    /// Top-level driver: iterates all NPCs, skips those of non-patchable race, the player, presets, and
    /// block-listed NPCs, and applies <see cref="SetVanillaBodyPath"/> to the rest while advancing the status bar.
    /// </summary>
    /// <remarks>Updates the status-bar UI and writes records to <paramref name="outputMod"/>.</remarks>
    public void SetVanillaBodyMeshPaths(ISkyrimMod outputMod, IEnumerable<INpcGetter> allNPCs)
    {
        _statusBar.ProgressBarCurrent = 0;
        _statusBar.DispString = "Setting Vanilla Body Mesh Paths";
        var npcArray = allNPCs.ToArray();
        for (int i = 0; i < npcArray.Length; i++)
        {
            _statusBar.ProgressBarCurrent++;
            var npc = npcArray[i];

            if (!_raceResolver.PatchableRaceFormKeys.Contains(npc.Race.FormKey))
            {
                continue;
            }

            if (_patcherState.GeneralSettings.ExcludePlayerCharacter && npc.FormKey.ToString() == Skyrim.Npc.Player.FormKey.ToString())
            {
                continue;
            }

            if (_patcherState.GeneralSettings.ExcludePresets && npc.EditorID != null && npc.EditorID.Contains("Preset"))
            {
                continue;
            }
            
            if (BlockedNPCs.Contains(npc.FormKey))
            {
                continue;
            }
            
            SetVanillaBodyPath(npc, outputMod);
        }
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
    private void SetVanillaBodyPath(INpcGetter npcGetter, ISkyrimMod outputMod)
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
        }
        else if (outputMod.Npcs.Any(x => x.FormKey.Equals(npcGetter.FormKey)))
        {
            currentArmor = outputMod.Npcs.First(x => x.FormKey.Equals(npcGetter.FormKey)).WornArmor;
        }
        
        if (!currentArmor.IsNull && _environmentStateProvider.LinkCache.TryResolve<IArmorGetter>(currentArmor.FormKey, out var armorGetter))
        {
            if (ArmorDuplicatedwithVanillaPaths.ContainsKey(currentArmor.FormKey))
            {
                var duplicatedArmor = ArmorDuplicatedwithVanillaPaths[currentArmor.FormKey];
                if (duplicatedArmor == null)
                {
                    _logger.LogMessage($"Vanilla body path setter: duplicated armor is null. Npc {currentNpc.FormKey.ToString()} Template armor: {currentArmor.FormKey.ToString()}");
                    return;
                }
                
                if (_patcherState.TexMeshSettings.bSkyPatcherModeAssets)
                {
                    _skyPatcherInterface.ApplySkin(npcGetter.FormKey, duplicatedArmor.FormKey);
                }
                else
                {
                    var npc = outputMod.Npcs.GetOrAddAsOverride(npcGetter);
                    npc.WornArmor.SetTo(duplicatedArmor.FormKey);
                }
                return;
            }

            if (armorGetter.Armature == null) { return; }

            var currentGender = NPCInfo.GetGender(npcGetter);

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
            }

            bool hasBlockedArmature = BlockedArmatures.Keys.Intersect(armorGetter.Armature.Select(x => x.FormKey).ToArray()).Any();

            if (hasBlockedArmature || _patcherState.TexMeshSettings.bSkyPatcherModeAssets && !_surrogateNpcProvider.TryGetImportedFormKey(armorGetter.FormKey, out _))
            {
                SetViaNewArmor(outputMod, armorGetter, npcGetter, currentGender);
            }
            else
            {
                SetInExistingArmor(outputMod, armorGetter, npcGetter, currentGender);
            }   
        }
    }

    /// <summary>
    /// Creates a vanilla-path worn armor for the NPC by cloning the template armor (or overriding the surrogate's
    /// armor in SkyPatcher mode), suffixing EditorIDs with "_VanillaBodyPath", caching the result, and rewriting
    /// each body armature to a cloned/overridden armature carrying the race-default world-model path. Routes the
    /// NPC to the new armor via ApplySkin (SkyPatcher mode) or a WornArmor override.
    /// </summary>
    /// <remarks>Writes armor/armature records, mutates the duplicate caches, may emit SkyPatcher directives, and logs.</remarks>
    private void SetViaNewArmor(ISkyrimMod outputMod, IArmorGetter templateArmorGetter, INpcGetter npcGetter, Gender currentGender)
    {
        Armor wornArmor;
        var implicits = Implicits.Get(outputMod.GameRelease);

        if (_patcherState.TexMeshSettings.bSkyPatcherModeAssets)
        {
            if (!_surrogateNpcProvider.TryGetSurrogateNpc(npcGetter, out var surrogateNpc))
            {
                _logger.LogMessage($"Cannot set vanilla body paths for NPC {npcGetter.FormKey} because surrogate creation failed.");
                return;
            }
            
            if (surrogateNpc.WornArmor == null || surrogateNpc.WornArmor.IsNull ||
                implicits.BaseMasters.Contains(surrogateNpc.WornArmor.FormKey.ModKey))
            {
                // This NPC already has vanilla armor paths. No need to warn user.
                return;
            }
            
            if (!_surrogateNpcProvider.TryGetImportedFormKey(npcGetter.WornArmor.FormKey, out _))
            {
                _logger.LogMessage($"Cannot set vanilla body paths in armor {npcGetter.WornArmor.FormKey} of NPC {npcGetter.FormKey} because the armor's source mod is blocked from import in Avoid Override Mode");
                return;
            }
            _skyPatcherInterface.ApplySkin(npcGetter.FormKey, surrogateNpc.WornArmor.FormKey);
            wornArmor = outputMod.Armors.GetOrAddAsOverride(surrogateNpc.WornArmor, _environmentStateProvider.LinkCache);
        }
        else
        {
            if (npcGetter.WornArmor == null || npcGetter.WornArmor.IsNull ||
                implicits.BaseMasters.Contains(npcGetter.WornArmor.FormKey.ModKey))
            {
                // This NPC already has vanilla armor paths. No need to warn user.
                return;
            }
            
            wornArmor = outputMod.Armors.AddNew();
            wornArmor.DeepCopyIn(templateArmorGetter);
            var npc = outputMod.Npcs.GetOrAddAsOverride(npcGetter);
            npc.WornArmor.SetTo(wornArmor);
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

        for (int i = 0; i < wornArmor.Armature.Count; i++)
        {
            var armaLinkGetter = wornArmor.Armature[i];
            if (!_environmentStateProvider.LinkCache.TryResolve<IArmorAddonGetter>(armaLinkGetter.FormKey, out var armaGetter))
            {
                _logger.LogMessage("Warning: Could not evaluate armature " + armaLinkGetter.FormKey.ToString() + " for vanilla body mesh path - armature could not be resolved.");
            }
            else if (ArmatureDuplicatedWithVanillaPath.ContainsKey(armaLinkGetter.FormKey)) // set the previously duplicated armature from cache
            {
                var newSetter = armaLinkGetter.AsSetter();
                newSetter.SetTo(ArmatureDuplicatedWithVanillaPath[armaLinkGetter.FormKey]);
                wornArmor.Armature[i] = newSetter;
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
            }
            else if (IsValidBodyArmature(armaGetter, wornArmor, npcGetter, out BipedObjectFlag primaryBodyPart) &&
                ArmatureHasWorldModel(armaGetter, currentGender) &&
                !ArmatureHasVanillaPath(armaGetter, primaryBodyPart, currentGender, npcGetter, out string vanillaPathB))
            {
                var armature = outputMod.ArmorAddons.GetOrAddAsOverride(armaGetter);
                SetArmatureVanillaPath(armature, currentGender, vanillaPathB);
            }
        }
    }

    /// <summary>
    /// Sets the race-default world-model path directly on the NPC's existing body armatures (overriding each in
    /// the output mod) for those that are valid body armatures with a world model and a non-vanilla path.
    /// </summary>
    /// <remarks>Writes armature overrides to <paramref name="outputMod"/> and logs unresolved armatures.</remarks>
    private void SetInExistingArmor(ISkyrimMod outputMod, IArmorGetter currentArmorGetter, INpcGetter npcGetter, Gender currentGender)
    {
        for (int i = 0; i < currentArmorGetter.Armature.Count; i++)
        {
            var armaLinkGetter = currentArmorGetter.Armature[i];
            if (!_environmentStateProvider.LinkCache.TryResolve<IArmorAddonGetter>(armaLinkGetter.FormKey, out var armaGetter))
            {
                _logger.LogMessage("Warning: Could not evaluate armature " + armaLinkGetter.FormKey.ToString() + " for vanilla body mesh path - armature could not be resolved.");
                continue;
            }
            if (IsValidBodyArmature(armaGetter, currentArmorGetter, npcGetter, out BipedObjectFlag primaryBodyPart) &&
                ArmatureHasWorldModel(armaGetter, currentGender))
            {
                if (!ArmatureHasVanillaPath(armaGetter, primaryBodyPart, currentGender, npcGetter, out string vanillaPath))
                {
                    var armature = outputMod.ArmorAddons.GetOrAddAsOverride(armaGetter);
                    SetArmatureVanillaPath(armature, currentGender, vanillaPath);
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
}
