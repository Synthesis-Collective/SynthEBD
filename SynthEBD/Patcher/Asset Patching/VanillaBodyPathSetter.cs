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
        BlockedArmatures.Clear();
        BlockedNPCs.Clear();
        ArmatureDuplicatedWithVanillaPath.Clear();
    }

    private Dictionary<FormKey, BipedObjectFlag> BlockedArmatures = new();
    private HashSet<FormKey> BlockedNPCs = new();
    private Dictionary<FormKey, IArmorAddonGetter> ArmatureDuplicatedWithVanillaPath = new();
    private Dictionary<FormKey, IArmorGetter> ArmorDuplicatedwithVanillaPaths = new();

    public void RegisterBlockedFromVanillaBodyPaths(NPCInfo currentNPCinfo)
    {
        BlockedNPCs.Add(currentNPCinfo.NPC.FormKey);
        if (_environmentStateProvider.LinkCache.TryResolve<INpcGetter>(currentNPCinfo.NPC.FormKey, out var npcWinningRecord) // get the winning override - the getter being passed in is from the pre-patching winning context
            && npcWinningRecord != null && npcWinningRecord.WornArmor != null && !npcWinningRecord.WornArmor.IsNull && _environmentStateProvider.LinkCache.TryResolve<IArmorGetter>(npcWinningRecord.WornArmor.FormKey, out var armorGetter))
        {
            foreach (var armaLink in armorGetter.Armature)
            {
                if (!BlockedArmatures.ContainsKey(armaLink.FormKey) && armaLink.TryResolve(_environmentStateProvider.LinkCache, out var armaGetter) && IsBodyArmature(armaGetter, npcWinningRecord, currentNPCinfo.Gender, out BipedObjectFlag primaryBodyPart) && !ArmatureHasVanillaPath(armaGetter, currentNPCinfo.Gender, DefaultBodyMeshPaths[currentNPCinfo.Gender][primaryBodyPart]))
                {
                    BlockedArmatures.Add(armaGetter.FormKey, primaryBodyPart);
                }
            }
        }
    }

    private void SetVanillaBodyPath(INpcGetter npcGetter, ISkyrimMod outputMod)
    {
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
                    IsBodyArmature(armaGetter, npcGetter, currentGender, out BipedObjectFlag primaryBodyPart) && 
                    !ArmatureHasVanillaPath(armaGetter, currentGender, DefaultBodyMeshPaths[currentGender][primaryBodyPart]))
                {
                    hasNonVanillaBodyPaths = true;
                    break;
                }
            }

            if (!hasNonVanillaBodyPaths)
            {
                return;
            }

            bool hasBlockedArmature = BlockedArmatures.Keys.Intersect(armorGetter.Armature.Select(x => x.FormKey).ToArray()).Any();

            Armor wornArmor = null;
            if (hasBlockedArmature)
            {
                wornArmor = outputMod.Armors.AddNew();
                wornArmor.DeepCopyIn(armorGetter);
                if (wornArmor.EditorID == null)
                {
                    wornArmor.EditorID = "_VanillaBodyPath";
                }
                else
                {
                    wornArmor.EditorID += "_VanillaBodyPath";
                }

                var npc = outputMod.Npcs.GetOrAddAsOverride(npcGetter);
                npc.WornArmor.SetTo(wornArmor);
                ArmorDuplicatedwithVanillaPaths.Add(armorGetter.FormKey, wornArmor);
            }
            else
            {
                wornArmor = outputMod.Armors.GetOrAddAsOverride(armorGetter);
            }

            for (int i = 0; i < wornArmor.Armature.Count; i++)
            {
                var armaLinkGetter = wornArmor.Armature[i];
                if (!_environmentStateProvider.LinkCache.TryResolve<IArmorAddonGetter>(armaLinkGetter.FormKey, out var armaGetter))
                {
                    _logger.LogMessage("Warning: Could not evaluate armature " + armaLinkGetter.FormKey.ToString() + " for vanilla body mesh path - armature could not be resolved.");
                    continue;
                }
                if (ArmatureDuplicatedWithVanillaPath.ContainsKey(armaLinkGetter.FormKey)) // set the previously duplicated armature from cache
                {
                    var newSetter = armaLinkGetter.AsSetter();
                    newSetter.SetTo(ArmatureDuplicatedWithVanillaPath[armaLinkGetter.FormKey]);
                    wornArmor.Armature[i] = newSetter;
                    continue;
                }
                else if (BlockedArmatures.ContainsKey(armaLinkGetter.FormKey))
                {
                    BipedObjectFlag primaryBodyPart = BlockedArmatures[armaLinkGetter.FormKey];
                    ArmorAddon clone = outputMod.ArmorAddons.AddNew();
                    clone.DeepCopyIn(armaGetter);
                    if (clone.EditorID == null)
                    {
                        clone.EditorID = "_VanillaBodyPath";
                    }
                    else
                    {
                        clone.EditorID += "_VanillaBodyPath";
                    }
                    var newSetter = armaLinkGetter.AsSetter();
                    newSetter.SetTo(clone);
                    wornArmor.Armature[i] = newSetter;
                    SetArmatureVanillaPath(clone, currentGender, DefaultBodyMeshPaths[currentGender][primaryBodyPart]);
                }
                else
                {
                    if (IsBodyArmature(armaGetter, npcGetter, currentGender, out BipedObjectFlag primaryBodyPart))
                    {
                        if (!DefaultBodyMeshPaths[currentGender].ContainsKey(primaryBodyPart))
                        {
                            _logger.LogMessage("Error setting vanilla mesh path: No registered path for body flag " + primaryBodyPart.ToString());
                        }
                        else if (!ArmatureHasVanillaPath(armaGetter, currentGender, DefaultBodyMeshPaths[currentGender][primaryBodyPart]))
                        {
                            var armature = outputMod.ArmorAddons.GetOrAddAsOverride(armaGetter);
                            SetArmatureVanillaPath(armature, currentGender, DefaultBodyMeshPaths[currentGender][primaryBodyPart]);
                        }
                    }
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

    private bool ArmatureHasVanillaPath(IArmorAddonGetter armaGetter, Gender currentGender, string vanillaPath)
    {
        switch (currentGender)
        {
            case Gender.Female: return armaGetter.WorldModel.Female.File.ToString().Equals(vanillaPath, StringComparison.OrdinalIgnoreCase);
            case Gender.Male: return armaGetter.WorldModel.Male.File.ToString().Equals(vanillaPath, StringComparison.OrdinalIgnoreCase);
        }
        return true;
    }

    private bool IsBodyArmature(IArmorAddonGetter armaGetter, INpcGetter currentNPCgetter, Gender currentGender, out BipedObjectFlag primaryBodyPart)
    {
        return IsBodyPart(armaGetter, out primaryBodyPart) &&
            armaGetter.WorldModel != null &&
            (_raceResolver.PatchableRaces.Contains(armaGetter.Race) || _raceResolver.PatchableRaces.Intersect(armaGetter.AdditionalRaces).Any()) &&
            DefaultBodyMeshPaths.ContainsKey(currentGender) &&
            DefaultBodyMeshPaths[currentGender] != null;
    }
    
    private bool IsBodyPart(IArmorAddonGetter armaGetter, out BipedObjectFlag primaryBodyPart)
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

    public static Dictionary<Gender, Dictionary<BipedObjectFlag, string>> DefaultBodyMeshPaths = new()
    {
        {
            Gender.Male,
            new Dictionary<BipedObjectFlag, string>()
            {
                { BipedObjectFlag.Body, "Actors\\Character\\Character Assets\\MaleBody_1.nif" },
                { BipedObjectFlag.Hands, "Actors\\Character\\Character Assets\\MaleHands_1.nif" },
                { BipedObjectFlag.Feet, "Actors\\Character\\Character Assets\\MaleFeet_1.nif" },
            }
        },
        {
            Gender.Female,
            new Dictionary<BipedObjectFlag, string>()
            {
                { BipedObjectFlag.Body, "Actors\\Character\\Character Assets\\FemaleBody_1.nif" },
                { BipedObjectFlag.Hands, "Actors\\Character\\Character Assets\\FemaleHands_1.nif" },
                { BipedObjectFlag.Feet, "Actors\\Character\\Character Assets\\FemaleFeet_1.nif" },
            }
        }
    };

    public static HashSet<BipedObjectFlag> BodyFlags = new()
    {
        BipedObjectFlag.Body,
        BipedObjectFlag.Hands,
        BipedObjectFlag.Feet
    };
}
