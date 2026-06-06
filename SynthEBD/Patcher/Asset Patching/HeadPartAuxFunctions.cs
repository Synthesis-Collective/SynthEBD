using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Skyrim;

namespace SynthEBD;

/// <summary>
/// Post-pass helpers for head-part assignment. The EBD Papyrus scripts require an NPC to have a head texture
/// before head parts can be applied; this class ensures one exists (falling back to the race's default face
/// texture, or hardcoded Khajiit/Argonian skins) and reverts head-part assignments for NPCs where no valid
/// face texture is available or where applying head parts would create a neck seam (WNAM present).
/// </summary>
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

    /// <summary>
    /// For every NPC in <paramref name="assignedHeadPartTransfers"/> lacking a head texture, assigns the
    /// race's default face texture (or hardcoded Khajiit/Argonian skin) by writing an NPC override into the
    /// output mod. If no suitable texture exists, or the NPC has a WNAM (worn armor) that would cause a neck
    /// seam, removes the entry from the dictionary so its head parts are not applied. Mutates both the output
    /// mod and the passed dictionary; logs a message per reversion.
    /// </summary>
    public void ApplyNeededFaceTextures(Dictionary<FormKey, (NPCInfo NpcInfo, Dictionary<HeadPart.TypeEnum, FormKey> HeadParts)> assignedHeadPartTransfers) // The EBD Papyrus scripts require a head texture to be assigned in order to process headparts. If none was assigned by SynthEBD, assign the default head texture for the NPC's race
    {
        HashSet<FormKey> toRemove = new();
        
        foreach (var kvp in assignedHeadPartTransfers)
        {
            var npcInfo = kvp.Value.NpcInfo;
            var npcGetter = npcInfo.NPC;
            if (npcGetter.HeadTexture == null || npcGetter.HeadTexture.IsNull)
            {
                if (npcGetter.WornArmor != null && !npcGetter.WornArmor.IsNull)
                {
                    ShowRemovalMessage_WNAM(npcGetter);
                    toRemove.Add(kvp.Key);
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
                                toRemove.Add(kvp.Key);
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
                                toRemove.Add(kvp.Key);
                            }

                            break;
                    }
                }
                else
                {
                    ShowRemovalMessage(npcGetter);
                    toRemove.Add(kvp.Key);
                }
            }
        }

        foreach (var fk in toRemove)
        {
            assignedHeadPartTransfers.Remove(fk);
        }
    }

    /// <summary>
    /// Logs that an NPC's head parts are being reverted because no face texture was assigned and the race has
    /// no default; if the NPC has forced (Specific NPC Assignment) head parts, logs a warning that they are
    /// respected instead.
    /// </summary>
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

    /// <summary>
    /// Logs that an NPC's head parts are being reverted because no face texture was assigned and the NPC has a
    /// WNAM (worn armor) which would cause a neck seam; warns instead if forced head parts exist.
    /// </summary>
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

    /// <summary>
    /// Returns true if the NPC has a Specific NPC Assignment that forces at least one non-null head part.
    /// </summary>
    private bool IsForced(INpcGetter npcGetter)
    {
        var specificAssignment = _patcherState.SpecificNPCAssignments.Where(x => x.NPCFormKey == npcGetter.FormKey).FirstOrDefault();
        return specificAssignment != null && specificAssignment.HeadParts != null && specificAssignment.HeadParts.Where(x => x.Value != null && x.Value.FormKey != null && !x.Value.FormKey.IsNull).Any();
    }
}