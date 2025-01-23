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
using System.DirectoryServices.ActiveDirectory;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SynthEBD;

public class VanillaBodyPathSetter
{
    private readonly IEnvironmentStateProvider _environmentStateProvider;
    private readonly PatcherState _patcherState;
    private readonly Logger _logger;
    private readonly VM_StatusBar _statusBar;
    private readonly PatchableRaceResolver _raceResolver;
    public VanillaBodyPathSetter(IEnvironmentStateProvider environmentStateProvider, PatcherState patcherState, Logger logger, VM_StatusBar statusBar, PatchableRaceResolver raceResolver)
    {
        _environmentStateProvider = environmentStateProvider;
        _patcherState = patcherState;
        _logger = logger;
        _statusBar = statusBar;
        _raceResolver = raceResolver;
    }

    public void SetVanillaBodyMeshPaths(ISkyrimMod outputMod, IEnumerable<INpcGetter> allNPCs)
    {
        _statusBar.ProgressBarCurrent = 0;
        _statusBar.DispString = "Setting Vanilla Body Mesh Paths";
        var npcArray = allNPCs.ToArray();
        for (int i = 0; i < npcArray.Length; i++)
        {
            _statusBar.ProgressBarCurrent++;
            var npc = npcArray[i];

            if (!_raceResolver.PatchableRaces.Contains(npc.Race))
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

            var patchedNPC = outputMod.Npcs.Where(x => x.FormKey.Equals(npc.FormKey)).FirstOrDefault();
            if (patchedNPC != null)
            {
                npc = patchedNPC;
            }

            if (BlockedNPCs.Contains(npc.FormKey))
            {
                continue;
            }
            SetVanillaBodyPath(npc, outputMod);
        }
    }

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

    private Dictionary<FormKey, BipedObjectFlag> BlockedArmatures = new();
    private HashSet<FormKey> BlockedNPCs = new();
    private Dictionary<FormKey, IArmorAddonGetter> ArmatureDuplicatedWithVanillaPath = new();
    private Dictionary<FormKey, IArmorGetter> ArmorDuplicatedwithVanillaPaths = new();
    private HashSet<string> ArmatureNifsFromAssets = new();

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

    private void SetVanillaBodyPath(INpcGetter npcGetter, ISkyrimMod outputMod)
    {
        if (npcGetter == null)
        {
            _logger.LogMessage("npcGetter is null. Can't process!");
            return;
        }

        if (npcGetter.WornArmor != null && !npcGetter.WornArmor.IsNull && _environmentStateProvider.LinkCache.TryResolve<IArmorGetter>(npcGetter.WornArmor.FormKey, out var armorGetter))
        {
            if (ArmorDuplicatedwithVanillaPaths.ContainsKey(npcGetter.WornArmor.FormKey))
            {
                var npc = outputMod.Npcs.GetOrAddAsOverride(npcGetter);
                npc.WornArmor.SetTo(ArmorDuplicatedwithVanillaPaths[npcGetter.WornArmor.FormKey]);
                return;
            }

            if (armorGetter.Armature == null) { return; }

            var currentGender = NPCInfo.GetGender(npcGetter);

            bool hasNonVanillaBodyPaths = false;
            foreach (var armaLink in armorGetter.Armature)
            {
                if (armaLink.TryResolve(_environmentStateProvider.LinkCache, out var armaGetter) && 
                    IsValidBodyArmature(armaGetter, armorGetter, npcGetter, out BipedObjectFlag primaryBodyPart) &&
                    ArmatureHasWorldModel(armaGetter, NPCInfo.GetGender(npcGetter)) &&
                    !ArmatureHasVanillaPath(armaGetter, primaryBodyPart, currentGender, npcGetter, out _) &&
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

            if (contexts.Count == 2) // base mod and output mod only
            {
                string raceName = "No Race";
                if (npcGetter.Race != null && _environmentStateProvider.LinkCache.TryResolve(npcGetter.Race, out var raceGetter))
                {
                    raceName = EditorIDHandler.GetEditorIDSafely(raceGetter);
                }
                _logger.LogMessage(_logger.GetNPCLogNameString(npcGetter) + " is getting its body mesh path set to that of its race (" + raceName + ") despite having no overriding appearance mods. Make sure this NPC does not need to be blocked from vanilla body paths.");
            }

            bool hasBlockedArmature = BlockedArmatures.Keys.Intersect(armorGetter.Armature.Select(x => x.FormKey).ToArray()).Any();

            if (hasBlockedArmature)
            {
                SetViaNewArmor(outputMod, armorGetter, npcGetter, currentGender);
            }
            else
            {
                SetInExistingArmor(outputMod, armorGetter, npcGetter, currentGender);
            }   
        }
    }

    private void SetViaNewArmor(ISkyrimMod outputMod, IArmorGetter templateArmorGetter, INpcGetter currentNpcGetter, Gender currentGender)
    {
        var wornArmor = outputMod.Armors.AddNew();
        wornArmor.DeepCopyIn(templateArmorGetter);
        if (wornArmor.EditorID == null)
        {
            wornArmor.EditorID = "_VanillaBodyPath";
        }
        else
        {
            wornArmor.EditorID += "_VanillaBodyPath";
        }

        var npc = outputMod.Npcs.GetOrAddAsOverride(currentNpcGetter);
        npc.WornArmor.SetTo(wornArmor);
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
            else if (BlockedArmatures.ContainsKey(armaLinkGetter.FormKey) && GetArmatureVanillaPath(BlockedArmatures[armaLinkGetter.FormKey], currentGender, currentNpcGetter, out string vanillaPath))
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
            else if (IsValidBodyArmature(armaGetter, wornArmor, currentNpcGetter, out BipedObjectFlag primaryBodyPart) &&
                ArmatureHasWorldModel(armaGetter, currentGender) &&
                !ArmatureHasVanillaPath(armaGetter, primaryBodyPart, currentGender, currentNpcGetter, out string vanillaPathB))
            {
                var armature = outputMod.ArmorAddons.GetOrAddAsOverride(armaGetter);
                SetArmatureVanillaPath(armature, currentGender, vanillaPathB);
            }
        }
    }

    private void SetInExistingArmor(ISkyrimMod outputMod, IArmorGetter currentArmorGetter, INpcGetter currentNpcGetter, Gender currentGender)
    {
        for (int i = 0; i < currentArmorGetter.Armature.Count; i++)
        {
            var armaLinkGetter = currentArmorGetter.Armature[i];
            if (!_environmentStateProvider.LinkCache.TryResolve<IArmorAddonGetter>(armaLinkGetter.FormKey, out var armaGetter))
            {
                _logger.LogMessage("Warning: Could not evaluate armature " + armaLinkGetter.FormKey.ToString() + " for vanilla body mesh path - armature could not be resolved.");
                continue;
            }
            if (IsValidBodyArmature(armaGetter, currentArmorGetter, currentNpcGetter, out BipedObjectFlag primaryBodyPart) &&
                ArmatureHasWorldModel(armaGetter, currentGender))
            {
                if (!ArmatureHasVanillaPath(armaGetter, primaryBodyPart, currentGender, currentNpcGetter, out string vanillaPath))
                {
                    var armature = outputMod.ArmorAddons.GetOrAddAsOverride(armaGetter);
                    SetArmatureVanillaPath(armature, currentGender, vanillaPath);
                }
            }
        }
    }

    private void SetArmatureVanillaPath(ArmorAddon armature, Gender currentGender, string updatedPath)
    {
        switch (currentGender)
        {
            case Gender.Female: armature.WorldModel.Female.File = updatedPath; break;
            case Gender.Male: armature.WorldModel.Male.File = updatedPath; break;
        }
    }

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

    public void RegisterAssetAssignedMeshes(List<SimplifiedSubgroupCombination> assignedCombinations)
    {
        ArmatureNifsFromAssets.UnionWith(assignedCombinations.SelectMany(x => x.FilePathReplacements).Select(x => x.Source).Where(x => x.EndsWith(".nif", StringComparison.OrdinalIgnoreCase)));
    }

    private bool ArmaturePathAssignedFromConfig(IArmorAddonGetter armaGetter, Gender currentGender)
    {
        if (currentGender == Gender.Female && armaGetter.WorldModel?.Female?.File.GivenPath != null && ArmatureNifsFromAssets.Contains(armaGetter.WorldModel.Female.File.GivenPath) ||
            (currentGender == Gender.Male && armaGetter.WorldModel?.Male?.File.GivenPath != null && ArmatureNifsFromAssets.Contains(armaGetter.WorldModel.Male.File.GivenPath)))
        {
            return true;
        }
        return false;
    }

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

    private bool IsValidBodyArmature(IArmorAddonGetter armaGetter, IArmorGetter armorGetter, INpcGetter currentNPC, out BipedObjectFlag primaryBodyPart)
    {
        return IsBodyPart(armaGetter, out primaryBodyPart, currentNPC) &&
            (armorGetter.Keywords == null || !armorGetter.Keywords.Contains(Skyrim.Keyword.ArmorClothing)) &&
            ((armaGetter.Race != null && armaGetter.Race.Equals(currentNPC.Race)) ||
            (armaGetter.AdditionalRaces != null && armaGetter.AdditionalRaces.Contains(currentNPC.Race)));
    }
    
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
    private void InitializeDefaultMeshPaths()
    {
        foreach (var race in _raceResolver.PatchableRaces)
        {
            PathsByRaceGender.Add(race.FormKey, new Dictionary<Gender, Dictionary<BipedObjectFlag, string>>());
            PathsByRaceGender[race.FormKey].Add(Gender.Male, new Dictionary<BipedObjectFlag, string>());
            PathsByRaceGender[race.FormKey].Add(Gender.Female, new Dictionary<BipedObjectFlag, string>());
            
            if (_environmentStateProvider.LinkCache.TryResolve<IRaceGetter>(race.FormKey, out var raceGetter) && raceGetter.Skin != null && !raceGetter.Skin.IsNull && _environmentStateProvider.LinkCache.TryResolve<IArmorGetter>(raceGetter.Skin.FormKey, out var skinGetter) && skinGetter.Armature != null)
            {
                foreach (var armaLink in skinGetter.Armature)
                {
                    if (armaLink.TryResolve(_environmentStateProvider.LinkCache, out var armaGetter) 
                        && armaGetter.BodyTemplate != null 
                        && (armaGetter.Race != null && armaGetter.Race.Equals(race) || armaGetter.AdditionalRaces != null && armaGetter.AdditionalRaces.Contains(raceGetter)))
                    {
                        foreach (var bodyFlag in BodyFlags)
                        {
                            if (armaGetter.BodyTemplate.FirstPersonFlags.HasFlag(bodyFlag))
                            {
                                if (!PathsByRaceGender[race.FormKey][Gender.Male].ContainsKey(bodyFlag) && armaGetter.WorldModel != null && armaGetter.WorldModel.Male != null && armaGetter.WorldModel.Male.File != null)
                                {
                                    PathsByRaceGender[race.FormKey][Gender.Male].Add(bodyFlag, armaGetter.WorldModel.Male.File);
                                }
                                if (!PathsByRaceGender[race.FormKey][Gender.Female].ContainsKey(bodyFlag) && armaGetter.WorldModel != null && armaGetter.WorldModel.Female != null && armaGetter.WorldModel.Female.File != null)
                                {
                                    PathsByRaceGender[race.FormKey][Gender.Female].Add(bodyFlag, armaGetter.WorldModel.Female.File);
                                }
                            }
                        }
                    }
                }
            }
        }
    }

    private Dictionary<FormKey, Dictionary<Gender, Dictionary<BipedObjectFlag, string>>> PathsByRaceGender = new();

    public static HashSet<BipedObjectFlag> BodyFlags = new()
    {
        BipedObjectFlag.Body,
        BipedObjectFlag.Hands,
        BipedObjectFlag.Feet,
        BipedObjectFlag.Tail
    };
}
