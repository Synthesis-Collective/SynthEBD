using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Skyrim;

namespace SynthEBD;

public class HeadPartFunctions
{
    private readonly PatcherState _patcherState;
    private readonly IOutputEnvironmentStateProvider _environmentStateProvider;
    private readonly Logger _logger;

    public HeadPartFunctions(PatcherState patcherState, IOutputEnvironmentStateProvider environmentStateProvider, Logger logger)
    {
        _patcherState = patcherState;
        _environmentStateProvider = environmentStateProvider;
        _logger = logger;

    }

    public void ApplyNeededFaceTextures(Dictionary<FormKey, HeadPartSelection> headPartAssignemnts) // The EBD Papyrus scripts require a head texture to be assigned in order to process headparts. If none was assigned by SynthEBD, assign the default head texture for the NPC's race
    {
        HashSet<FormKey> npcsToRemove = new();
        foreach (var npcFormKey in headPartAssignemnts.Keys)
        {
            if (headPartAssignemnts[npcFormKey] == null || !headPartAssignemnts[npcFormKey].HasAssignment())
            {
                continue;
            }

            if(!_environmentStateProvider.LinkCache.TryResolve<INpcGetter>(npcFormKey, out var npcGetter))
            {
                continue; // this pretty much can't happen
            }

            if (npcGetter.HeadTexture == null || npcGetter.HeadTexture.IsNull)
            {
                if (npcGetter.WornArmor != null && !npcGetter.WornArmor.IsNull)
                {
                    AddNPCtoRemovalList_WNAM(npcGetter, npcsToRemove);
                }
                if (npcGetter.Race != null && _environmentStateProvider.LinkCache.TryResolve<IRaceGetter>(npcGetter.Race.FormKey, out var raceGetter))
                {
                    var gender = NPCInfo.GetGender(npcGetter);
                    switch (gender)
                    {
                        case Gender.Male:
                            if (raceGetter.HeadData != null && raceGetter.HeadData.Male != null && raceGetter.HeadData.Male.DefaultFaceTexture != null && raceGetter.HeadData.Male.DefaultFaceTexture.IsNull == false)
                            {
                                var npc = _environmentStateProvider.OutputMod.Npcs.GetOrAddAsOverride(npcGetter);
                                npc.HeadTexture.SetTo(raceGetter.HeadData.Male.DefaultFaceTexture.FormKey);
                            }
                            else
                            {
                                AddNPCtoRemovalList(npcGetter, npcsToRemove);
                            }
                            break;
                        case Gender.Female:
                            if (raceGetter.HeadData != null && raceGetter.HeadData.Female != null && raceGetter.HeadData.Female.DefaultFaceTexture != null && raceGetter.HeadData.Female.DefaultFaceTexture.IsNull == false)
                            {
                                var npc = _environmentStateProvider.OutputMod.Npcs.GetOrAddAsOverride(npcGetter);
                                npc.HeadTexture.SetTo(raceGetter.HeadData.Female.DefaultFaceTexture.FormKey);
                            }
                            else
                            {
                                AddNPCtoRemovalList(npcGetter, npcsToRemove);
                            }
                            break;
                    }
                }
                else
                {
                    AddNPCtoRemovalList(npcGetter, npcsToRemove);
                }
            }
        }

        foreach (var npcFK in npcsToRemove)
        {
            headPartAssignemnts.Remove(npcFK);
        }
    }

    public void AddNPCtoRemovalList(INpcGetter npcGetter, HashSet<FormKey> npcsToRemove)
    {
        var npcString = Logger.GetNPCLogReportingString(npcGetter);
        if (IsForced(npcGetter))
        {
            _logger.LogMessage("Warning: headparts of NPC " + npcString + " should be reverted because no face texture was assigned by SynthEBD and no default face texture exists in its RACE record. HOWEVER, this NPC has headparts assigned via Specific NPC Assignment which will be respected.");
        }
        else
        {
            _logger.LogMessage("Reverting headparts of NPC " + npcString + " because no face texture was assigned by SynthEBD and no default face texture exists in its RACE record.");
            npcsToRemove.Add(npcGetter.FormKey);
        }
    }

    public void AddNPCtoRemovalList_WNAM(INpcGetter npcGetter, HashSet<FormKey> npcsToRemove)
    {
        var npcString = Logger.GetNPCLogReportingString(npcGetter);

        if (IsForced(npcGetter))
        {
            _logger.LogMessage("Warning: headparts of NPC " + npcString + " should be reverted because no face texture was assigned by SynthEBD or its original plugin, but the NPC has a WNAM so SynthEBD HeadPart assignment would cause a neck seam. HOWEVER, this NPC has headparts assigned via Specific NPC Assignment which will be respected.");
        }
        else
        {
            _logger.LogMessage("Reverting headparts of NPC " + npcString + " because no face texture was assigned by SynthEBD or its original plugin, but the NPC has a WNAM so SynthEBD HeadPart assignment would cause a neck seam.");
            npcsToRemove.Add(npcGetter.FormKey);
        }
    }

    private bool IsForced(INpcGetter npcGetter)
    {
        var specificAssignment = _patcherState.SpecificNPCAssignments.Where(x => x.NPCFormKey == npcGetter.FormKey).FirstOrDefault();
        return specificAssignment != null && specificAssignment.HeadParts != null && specificAssignment.HeadParts.Where(x => x.Value != null && x.Value.FormKey != null && !x.Value.FormKey.IsNull).Any();
    }
}