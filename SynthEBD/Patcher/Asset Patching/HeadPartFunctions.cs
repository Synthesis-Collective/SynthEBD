using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Skyrim;

namespace SynthEBD;

public class HeadPartFunctions
{
    public static void ApplyNeededFaceTextures(Dictionary<FormKey, HeadPartSelection> headPartAssignemnts, ISkyrimMod outputMod, Logger logger, ILinkCache linkCache) // The EBD Papyrus scripts require a head texture to be assigned in order to process headparts. If none was assigned by SynthEBD, assign the default head texture for the NPC's race
    {
        HashSet<FormKey> npcsToRemove = new();
        foreach (var npcFormKey in headPartAssignemnts.Keys)
        {
            if (headPartAssignemnts[npcFormKey] == null || !headPartAssignemnts[npcFormKey].HasAssignment())
            {
                continue;
            }

            if(!linkCache.TryResolve<INpcGetter>(npcFormKey, out var npcGetter))
            {
                continue; // this pretty much can't happen
            }

            if (npcGetter.HeadTexture == null || npcGetter.HeadTexture.IsNull)
            {
                if (npcGetter.WornArmor != null && !npcGetter.WornArmor.IsNull)
                {
                    AddNPCtoRemovalList_WNAM(npcGetter, npcsToRemove, logger);
                }
                if (npcGetter.Race != null && linkCache.TryResolve<IRaceGetter>(npcGetter.Race.FormKey, out var raceGetter))
                {
                    var gender = NPCInfo.GetGender(npcGetter);
                    switch (gender)
                    {
                        case Gender.Male:
                            if (raceGetter.HeadData != null && raceGetter.HeadData.Male != null && raceGetter.HeadData.Male.DefaultFaceTexture != null && raceGetter.HeadData.Male.DefaultFaceTexture.IsNull == false)
                            {
                                var npc = outputMod.Npcs.GetOrAddAsOverride(npcGetter);
                                npc.HeadTexture.SetTo(raceGetter.HeadData.Male.DefaultFaceTexture.FormKey);
                            }
                            else
                            {
                                AddNPCtoRemovalList(npcGetter, npcsToRemove, logger);
                            }
                            break;
                        case Gender.Female:
                            if (raceGetter.HeadData != null && raceGetter.HeadData.Female != null && raceGetter.HeadData.Female.DefaultFaceTexture != null && raceGetter.HeadData.Female.DefaultFaceTexture.IsNull == false)
                            {
                                var npc = outputMod.Npcs.GetOrAddAsOverride(npcGetter);
                                npc.HeadTexture.SetTo(raceGetter.HeadData.Female.DefaultFaceTexture.FormKey);
                            }
                            else
                            {
                                AddNPCtoRemovalList(npcGetter, npcsToRemove, logger);
                            }
                            break;
                    }
                }
                else
                {
                    AddNPCtoRemovalList(npcGetter, npcsToRemove, logger);
                }
            }
        }

        foreach (var npcFK in npcsToRemove)
        {
            headPartAssignemnts.Remove(npcFK);
        }
    }

    public static void AddNPCtoRemovalList(INpcGetter npcGetter, HashSet<FormKey> npcsToRemove, Logger logger)
    {
        var npcString = Logger.GetNPCLogReportingString(npcGetter);
        logger.LogMessage("Reverting headparts of NPC " + npcString + " because no face texture was assigned by SynthEBD and no default face texture exists in its RACE record.");
        npcsToRemove.Add(npcGetter.FormKey);
    }

    public static void AddNPCtoRemovalList_WNAM(INpcGetter npcGetter, HashSet<FormKey> npcsToRemove, Logger logger)
    {
        var npcString = Logger.GetNPCLogReportingString(npcGetter);
        logger.LogMessage("Reverting headparts of NPC " + npcString + " because no face texture was assigned by SynthEBD or its original plugin, but the NPC has a WNAM so SynthEBD HeadPart assignment would cause a neck seam.");
        npcsToRemove.Add(npcGetter.FormKey);
    }
}