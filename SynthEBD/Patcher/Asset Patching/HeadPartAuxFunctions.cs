using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Skyrim;

namespace SynthEBD;

public class HeadPartAuxFunctions
{
    private readonly PatcherState _patcherState;
    private readonly IOutputEnvironmentStateProvider _environmentStateProvider;
    private readonly Logger _logger;

    public HeadPartAuxFunctions(PatcherState patcherState, IOutputEnvironmentStateProvider environmentStateProvider, Logger logger)
    {
        _patcherState = patcherState;
        _environmentStateProvider = environmentStateProvider;
        _logger = logger;
    }

    public void ApplyNeededFaceTextures(Dictionary<NPCInfo, Dictionary<HeadPart.TypeEnum, FormKey>> assignedHeadPartTransfers) // The EBD Papyrus scripts require a head texture to be assigned in order to process headparts. If none was assigned by SynthEBD, assign the default head texture for the NPC's race
    {
        HashSet<NPCInfo> toRemove = new();
        
        foreach (var npcInfo in assignedHeadPartTransfers.Keys)
        {
            var npcGetter = npcInfo.NPC;
            if (npcGetter.HeadTexture == null || npcGetter.HeadTexture.IsNull)
            {
                if (npcGetter.WornArmor != null && !npcGetter.WornArmor.IsNull)
                {
                    ShowRemovalMessage_WNAM(npcGetter);
                    toRemove.Add(npcInfo);
                    continue;
                }

                if (npcGetter.Race != null &&
                    _environmentStateProvider.LinkCache.TryResolve<IRaceGetter>(npcGetter.Race.FormKey,
                        out var raceGetter))
                {
                    var gender = NPCInfo.GetGender(npcGetter);
                    switch (gender)
                    {
                        case Gender.Male:
                            if (raceGetter.HeadData != null && raceGetter.HeadData.Male != null &&
                                raceGetter.HeadData.Male.DefaultFaceTexture != null &&
                                raceGetter.HeadData.Male.DefaultFaceTexture.IsNull == false)
                            {
                                var npc = _environmentStateProvider.OutputMod.Npcs.GetOrAddAsOverride(npcGetter);
                                npc.HeadTexture.SetTo(raceGetter.HeadData.Male.DefaultFaceTexture.FormKey);
                            }
                            else if (raceGetter.HeadData != null &&
                                     raceGetter.Equals(Mutagen.Bethesda.FormKeys.SkyrimSE.Skyrim.Race.KhajiitRace))
                            {
                                var npc = _environmentStateProvider.OutputMod.Npcs.GetOrAddAsOverride(npcGetter);
                                npc.HeadTexture.SetTo(Mutagen.Bethesda.FormKeys.SkyrimSE.Skyrim.TextureSet
                                    .SkinHeadMaleKhajiit);
                            }
                            else if (raceGetter.HeadData != null &&
                                     raceGetter.Equals(Mutagen.Bethesda.FormKeys.SkyrimSE.Skyrim.Race.ArgonianRace))
                            {
                                var npc = _environmentStateProvider.OutputMod.Npcs.GetOrAddAsOverride(npcGetter);
                                npc.HeadTexture.SetTo(Mutagen.Bethesda.FormKeys.SkyrimSE.Skyrim.TextureSet
                                    .SkinHeadMaleArgonian);
                            }
                            else
                            {
                                ShowRemovalMessage(npcGetter);
                                toRemove.Add(npcInfo);
                            }

                            break;
                        case Gender.Female:
                            if (raceGetter.HeadData != null && raceGetter.HeadData.Female != null &&
                                raceGetter.HeadData.Female.DefaultFaceTexture != null &&
                                raceGetter.HeadData.Female.DefaultFaceTexture.IsNull == false)
                            {
                                var npc = _environmentStateProvider.OutputMod.Npcs.GetOrAddAsOverride(npcGetter);
                                npc.HeadTexture.SetTo(raceGetter.HeadData.Female.DefaultFaceTexture.FormKey);
                            }
                            else if (raceGetter.HeadData != null &&
                                     raceGetter.Equals(Mutagen.Bethesda.FormKeys.SkyrimSE.Skyrim.Race.KhajiitRace))
                            {
                                var npc = _environmentStateProvider.OutputMod.Npcs.GetOrAddAsOverride(npcGetter);
                                npc.HeadTexture.SetTo(Mutagen.Bethesda.FormKeys.SkyrimSE.Skyrim.TextureSet
                                    .SkinHeadFemaleKhajiit);
                            }
                            else if (raceGetter.HeadData != null &&
                                     raceGetter.Equals(Mutagen.Bethesda.FormKeys.SkyrimSE.Skyrim.Race.ArgonianRace))
                            {
                                var npc = _environmentStateProvider.OutputMod.Npcs.GetOrAddAsOverride(npcGetter);
                                npc.HeadTexture.SetTo(Mutagen.Bethesda.FormKeys.SkyrimSE.Skyrim.TextureSet
                                    .SkinHeadFemaleArgonian);
                            }
                            else
                            {
                                ShowRemovalMessage(npcGetter);
                                toRemove.Add(npcInfo);
                            }

                            break;
                    }
                }
                else
                {
                    ShowRemovalMessage(npcGetter);
                    toRemove.Add(npcInfo);
                }
            }
        }

        foreach (var npcInfo in toRemove)
        {
            assignedHeadPartTransfers.Remove(npcInfo);
        }
    }

    public void ShowRemovalMessage(INpcGetter npcGetter)
    {
        var npcString = Logger.GetNPCLogReportingString(npcGetter);
        if (IsForced(npcGetter))
        {
            _logger.LogMessage("Warning: headparts of NPC " + npcString + " should be reverted because no face texture was assigned by SynthEBD and no default face texture exists in its RACE record. HOWEVER, this NPC has headparts assigned via Specific NPC Assignment which will be respected.");
        }
        else
        {
            _logger.LogMessage("Reverting headparts of NPC " + npcString + " because no face texture was assigned by SynthEBD and no default face texture exists in its RACE record.");
        }
    }

    public void ShowRemovalMessage_WNAM(INpcGetter npcGetter)
    {
        var npcString = Logger.GetNPCLogReportingString(npcGetter);

        if (IsForced(npcGetter))
        {
            _logger.LogMessage("Warning: headparts of NPC " + npcString + " should be reverted because no face texture was assigned by SynthEBD or its original plugin, but the NPC has a WNAM so SynthEBD HeadPart assignment would cause a neck seam. HOWEVER, this NPC has headparts assigned via Specific NPC Assignment which will be respected.");
        }
        else
        {
            _logger.LogMessage("Reverting headparts of NPC " + npcString + " because no face texture was assigned by SynthEBD or its original plugin, but the NPC has a WNAM so SynthEBD HeadPart assignment would cause a neck seam.");
        }
    }

    private bool IsForced(INpcGetter npcGetter)
    {
        var specificAssignment = _patcherState.SpecificNPCAssignments.Where(x => x.NPCFormKey == npcGetter.FormKey).FirstOrDefault();
        return specificAssignment != null && specificAssignment.HeadParts != null && specificAssignment.HeadParts.Where(x => x.Value != null && x.Value.FormKey != null && !x.Value.FormKey.IsNull).Any();
    }
}