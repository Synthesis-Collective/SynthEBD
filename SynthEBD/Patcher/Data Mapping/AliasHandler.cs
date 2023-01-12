using Mutagen.Bethesda.Plugins;

namespace SynthEBD;

public class AliasHandler
{
    private readonly PatcherState _patcherState;
    public AliasHandler(PatcherState patcherState)
    {
        _patcherState = patcherState;
    }
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