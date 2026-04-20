using Mutagen.Bethesda.FormKeys.SkyrimSE;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;

namespace SynthEBD;

/// <summary>
/// Read-only helper for picking preview NPCs for the 3D viewer outside of
/// Specific NPC Assignment contexts. Not bound to patching state.
/// </summary>
public class PreviewNpcResolver
{
    private readonly IEnvironmentStateProvider _env;
    private readonly Logger _logger;

    public PreviewNpcResolver(IEnvironmentStateProvider env, Logger logger)
    {
        _env = env;
        _logger = logger;
    }

    /// <summary>
    /// Returns the FormKey of the first NPC in load-order priority whose
    /// Race matches <paramref name="race"/> and whose gender matches
    /// <paramref name="gender"/>. Returns FormKey.Null if none found.
    /// </summary>
    public FormKey FindFirstNpcForRace(FormKey race, Gender gender)
    {
        if (_env.LoadOrder == null) return FormKey.Null;

        foreach (var ctx in _env.LoadOrder.PriorityOrder.Npc().WinningContextOverrides())
        {
            var npc = ctx.Record;
            if (npc.Race == null || npc.Race.IsNull) continue;
            if (!npc.Race.FormKey.Equals(race)) continue;
            if (GetGender(npc) != gender) continue;
            return npc.FormKey;
        }
        return FormKey.Null;
    }

    /// <summary>
    /// Returns the first NordRace NPC of the given gender. Convenience
    /// default for preview mappings where no per-race NPC is set.
    /// </summary>
    public FormKey FindFirstNordRaceNpc(Gender gender)
    {
        return FindFirstNpcForRace(Skyrim.Race.NordRace.FormKey, gender);
    }

    /// <summary>
    /// Returns the first NPC of the given gender whose NPC.Weight is within
    /// <paramref name="tolerance"/> of <paramref name="targetWeight"/>.
    /// </summary>
    public FormKey FindFirstNpcAtWeight(Gender gender, int targetWeight, int tolerance = 5)
    {
        if (_env.LoadOrder == null) return FormKey.Null;

        foreach (var ctx in _env.LoadOrder.PriorityOrder.Npc().WinningContextOverrides())
        {
            var npc = ctx.Record;
            if (GetGender(npc) != gender) continue;
            if (Math.Abs(npc.Weight - targetWeight) > tolerance) continue;
            return npc.FormKey;
        }

        _logger.LogMessage($"PreviewNpcResolver: No NPC found at weight {targetWeight} " +
                           $"(±{tolerance}) for gender {gender}.");
        return FormKey.Null;
    }

    /// <summary>
    /// Returns the weight of the NPC (0-100) if resolvable, else null.
    /// </summary>
    public float? GetNpcWeight(FormKey npc)
    {
        if (npc.IsNull || _env.LinkCache == null) return null;
        if (_env.LinkCache.TryResolve<INpcGetter>(npc, out var rec))
        {
            return rec.Weight;
        }
        return null;
    }

    private static Gender GetGender(INpcGetter npc)
    {
        return npc.Configuration.Flags.HasFlag(NpcConfiguration.Flag.Female)
            ? Gender.Female
            : Gender.Male;
    }
}
