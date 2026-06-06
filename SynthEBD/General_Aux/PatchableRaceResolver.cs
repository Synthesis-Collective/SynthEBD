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
    /// <summary>
    /// Computes and caches the set of races SynthEBD is allowed to patch, combining the user's explicit
    /// patchable-race list with race-grouping members, race aliases, and (optionally) the default race.
    /// </summary>
    public class PatchableRaceResolver
    {
        private readonly IEnvironmentStateProvider _environmentProvider;
        private readonly PatcherState _patcherState;
        private readonly Logger _logger;
        /// <summary>Creates the resolver.</summary>
        /// <param name="environmentProvider">Supplies the link cache used to resolve race records.</param>
        /// <param name="patcherState">Holds the general settings (patchable races, groupings, aliases).</param>
        /// <param name="logger">Logger for startup timing and errors.</param>
        public PatchableRaceResolver(IEnvironmentStateProvider environmentProvider, PatcherState patcherState, Logger logger)
        {
            _environmentProvider = environmentProvider;
            _patcherState = patcherState;
            _logger = logger;
        }
        
        /// <summary>The resolved set of patchable races as form-link getters (populated by <see cref="ResolvePatchableRaces"/>).</summary>
        public HashSet<IFormLinkGetter<IRaceGetter>> PatchableRaces { get; set; } = new();
        /// <summary>The FormKeys of the resolved patchable races (populated by <see cref="ResolvePatchableRaces"/>).</summary>
        public HashSet<FormKey> PatchableRaceFormKeys { get; set; } = new();
        /// <summary>Resolves the full set of patchable races (explicit + groupings + aliases + default) into <see cref="PatchableRaces"/> / <see cref="PatchableRaceFormKeys"/>.</summary>
        /// <remarks>Logs an error and leaves the results unchanged when the link cache is unavailable.</remarks>
        public void ResolvePatchableRaces()
        {
            _logger.LogStartupEventStart("Compiling patchable races");
            if (_environmentProvider.LinkCache is null)
            {
                _logger.LogError("Error: Link cache is null.");
            }
            else
            {
                PatchableRaces = new();
                foreach (var race in CompilePatchableRaces(_environmentProvider.LinkCache, _patcherState, true, true, true))
                {
                    PatchableRaces.Add(race.ToLinkGetter());
                    PatchableRaceFormKeys.Add(race.FormKey);
                }
            }
            _logger.LogStartupEventEnd("Compiling patchable races");
        }

        /// <summary>Builds the set of race records to treat as patchable by unioning the configured patchable races with, optionally, race-grouping members, race-alias source races, and the default race.</summary>
        /// <param name="linkCache">Link cache used to resolve FormKeys to race records.</param>
        /// <param name="patcherState">Source of the configured races, groupings, and aliases.</param>
        /// <param name="includeGroupings">When <c>true</c>, also includes every member of every race grouping.</param>
        /// <param name="includeAliases">When <c>true</c>, also includes each race-alias's source race.</param>
        /// <param name="includeDefault">When <c>true</c>, also includes Skyrim's DefaultRace.</param>
        /// <returns>The resolved race records (unresolvable FormKeys are skipped).</returns>
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
