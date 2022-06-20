using Mutagen.Bethesda.Skyrim;

namespace SynthEBD;

public class HeadPartFunctions
{
    public static void ApplyNeededFaceTextures(HashSet<Npc> npcsWithHeadParts) // The EBD Papyrus scripts require a head texture to be assigned in order to process headparts. If none was assigned by SynthEBD, assign the default head texture for the NPC's race
    {
        foreach (var npc in npcsWithHeadParts)
        {
            if (npc.HeadTexture == null || npc.HeadTexture.IsNull)
            {
                if (npc.Race != null && PatcherEnvironmentProvider.Instance.Environment.LinkCache.TryResolve<IRaceGetter>(npc.Race.FormKey, out var raceGetter))
                {
                    var gender = NPCInfo.GetGender(npc);
                    switch (gender)
                    {
                        case Gender.Male:
                            if (raceGetter.HeadData != null && raceGetter.HeadData.Male != null && raceGetter.HeadData.Male.DefaultFaceTexture != null && raceGetter.HeadData.Male.DefaultFaceTexture.IsNull == false)
                            {
                                npc.HeadTexture.SetTo(raceGetter.HeadData.Male.DefaultFaceTexture.FormKey);
                            }
                            else
                            {
                                RevertUnpatchedHeadParts(npc);
                            }
                            break;
                        case Gender.Female:
                            if (raceGetter.HeadData != null && raceGetter.HeadData.Female != null && raceGetter.HeadData.Female.DefaultFaceTexture != null && raceGetter.HeadData.Female.DefaultFaceTexture.IsNull == false)
                            {
                                npc.HeadTexture.SetTo(raceGetter.HeadData.Female.DefaultFaceTexture.FormKey);
                            }
                            else
                            {
                                RevertUnpatchedHeadParts(npc);
                            }
                            break;
                    }
                }
                else
                {
                    RevertUnpatchedHeadParts(npc);
                }
            }
        }
    }

    public static void RevertUnpatchedHeadParts(Npc npc)
    {
        var npcString = Logger.GetNPCLogReportingString(npc);

        Logger.LogMessage("Reverting headparts of NPC " + npcString + "because no face texture was assigned by SynthEBD and no default face texture exists in its RACE record.");

        var allContexts = PatcherEnvironmentProvider.Instance.Environment.LinkCache.ResolveAllContexts<INpc, INpcGetter>(npc.FormKey); // [0] is winning override. [Last] is source plugin

        // at this point the current override is [0], so the previous winner should be [1]
        if (allContexts != null && allContexts.Count() > 1)
        {
            var allContextsIndexable = allContexts.ToArray();
            var previousWinner = allContextsIndexable[1];
            npc.HeadParts.Clear();
            if (previousWinner.Record.HeadParts != null)
            {
                foreach (var headpart in previousWinner.Record.HeadParts)
                {
                    npc.HeadParts.Add(headpart);
                }
            }
        }
        else
        {
            Logger.LogMessage("Could not revert headparts for NPC" + npcString);
        }

    }
}