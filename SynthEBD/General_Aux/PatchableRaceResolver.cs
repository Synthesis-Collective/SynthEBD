using Mutagen.Bethesda;
using Mutagen.Bethesda.FormKeys.SkyrimSE;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Skyrim;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SynthEBD
{
    public class PatchableRaceResolver
    {
        private readonly IEnvironmentStateProvider _environmentProvider;
        private readonly PatcherState _patcherState;
        private readonly Logger _logger;
        public PatchableRaceResolver(IEnvironmentStateProvider environmentProvider, PatcherState patcherState, Logger logger)
        {
            _environmentProvider = environmentProvider;
            _patcherState = patcherState;
            _logger = logger;
        }
        
        public HashSet<IFormLinkGetter<IRaceGetter>> PatchableRaces { get; set; } = new();
        public void ResolvePatchableRaces()
        {
            if (_environmentProvider.LinkCache is null)
            {
                _logger.LogError("Error: Link cache is null.");
                return;
            }

            PatchableRaces = new();
            foreach (var race in CompilePatchableRaces(_environmentProvider.LinkCache, _patcherState, true, true, true))
            {
                PatchableRaces.Add(race.ToLinkGetter());
            }
        }

        public static HashSet<IRaceGetter> CompilePatchableRaces(ILinkCache linkCache, PatcherState patcherState, bool includeGroupings, bool includeAliases, bool includeDefault) // combines explicit patchable races, race groupings, and aliases
        {
            HashSet<FormKey> raceFKs = new();
            foreach (var pr in patcherState.GeneralSettings.PatchableRaces)
            {
                if (!raceFKs.Contains(pr))
                {
                    raceFKs.Add(pr);
                }
            }

            if (includeGroupings)
            {
                foreach (var grouping in patcherState.GeneralSettings.RaceGroupings)
                {
                    foreach (var member in grouping.Races)
                    {
                        if (!raceFKs.Contains(member))
                        {
                            raceFKs.Add(member);
                        }
                    }
                }
            }

            if (includeAliases)
            {
                foreach (var alias in patcherState.GeneralSettings.RaceAliases)
                {
                    if (!raceFKs.Contains(alias.Race))
                    {
                        raceFKs.Add(alias.Race);
                    }
                }
            }

            HashSet<IRaceGetter> races = new();
            foreach (var formKey in raceFKs)
            {
                if (linkCache.TryResolve<IRaceGetter>(formKey, out var raceGetter) && raceGetter is not null)
                {
                    races.Add(raceGetter);
                }
            }

            if (includeDefault && linkCache.TryResolve<IRaceGetter>(Skyrim.Race.DefaultRace.FormKey, out var defaultRace))
            {
                races.Add(defaultRace);
            }

            return races;
        }
    }
}
