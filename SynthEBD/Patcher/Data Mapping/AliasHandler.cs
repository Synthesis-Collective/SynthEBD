using Mutagen.Bethesda.Plugins;

namespace SynthEBD;

/// <summary>
/// Resolves a race alias for an NPC's race on a per-axis basis (assets, BodyGen, height, head parts).
/// Race aliases let one race borrow another's configured data; each lookup honors the alias's
/// per-axis opt-in flags and falls back to the NPC's own race when no applicable alias is set.
/// </summary>
public class AliasHandler
{
    private readonly PatcherState _patcherState;
    /// <summary>Initializes a new <see cref="AliasHandler"/> reading race aliases from <see cref="PatcherState"/>.</summary>
    public AliasHandler(PatcherState patcherState)
    {
        _patcherState = patcherState;
    }
    /// <summary>
    /// Returns the asset (texture/mesh) alias race for <paramref name="npcRaceFormKey"/> if an asset-applicable
    /// alias is configured; otherwise returns the race unchanged.
    /// </summary>
    public FormKey GetAliasTexMesh(FormKey npcRaceFormKey)
    {
        var alias = _patcherState.GeneralSettings.RaceAliases.Where(x => x.bApplyToAssets && x.Race == npcRaceFormKey).Select(x => x.AliasRace).FirstOrDefault();

        if (!alias.IsNull)
        {
            return alias;
        }
        else
        {
            return npcRaceFormKey;
        }
    }

    /// <summary>
    /// Returns the BodyGen alias race for <paramref name="npcRaceFormKey"/> if a BodyGen-applicable alias is
    /// configured; otherwise returns the race unchanged.
    /// </summary>
    public FormKey GetAliasBodyGen(FormKey npcRaceFormKey)
    {
        var alias = _patcherState.GeneralSettings.RaceAliases.Where(x => x.bApplyToBodyGen && x.Race == npcRaceFormKey).Select(x => x.AliasRace).FirstOrDefault();

        if (!alias.IsNull)
        {
            return alias;
        }
        else
        {
            return npcRaceFormKey;
        }
    }

    /// <summary>
    /// Returns the height alias race for <paramref name="npcRaceFormKey"/> if a height-applicable alias is
    /// configured; otherwise returns the race unchanged.
    /// </summary>
    public FormKey GetAliasHeight(FormKey npcRaceFormKey)
    {
        var alias = _patcherState.GeneralSettings.RaceAliases.Where(x => x.bApplyToHeight && x.Race == npcRaceFormKey).Select(x => x.AliasRace).FirstOrDefault();

        if (!alias.IsNull)
        {
            return alias;
        }
        else
        {
            return npcRaceFormKey;
        }
    }

    /// <summary>
    /// Returns the head-parts alias race for <paramref name="npcRaceFormKey"/> if a head-parts-applicable alias
    /// is configured; otherwise returns the race unchanged.
    /// </summary>
    public FormKey GetAliasHeadParts(FormKey npcRaceFormKey)
    {
        var alias = _patcherState.GeneralSettings.RaceAliases.Where(x => x.bApplyToHeadParts && x.Race == npcRaceFormKey).Select(x => x.AliasRace).FirstOrDefault();

        if (!alias.IsNull)
        {
            return alias;
        }
        else
        {
            return npcRaceFormKey;
        }
    }
}