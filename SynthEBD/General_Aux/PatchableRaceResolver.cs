using Mutagen.Bethesda;
using Mutagen.Bethesda.FormKeys.SkyrimSE;
using Mutagen.Bethesda.Plugins;
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
        private readonly IEnvironmentStateProvider _stateProvider;
        private readonly Logger _logger;
        public PatchableRaceResolver(IEnvironmentStateProvider stateProvider, Logger logger)
        {
            _stateProvider = stateProvider;
            _logger = logger;
        }
        
        public HashSet<IFormLinkGetter<IRaceGetter>> PatchableRaces { get; set; } = new();
        public void ResolvePatchableRaces()
        {
            if (_stateProvider.LinkCache is null)
            {
                _logger.LogError("Error: Link cache is null.");
                return;
            }

            PatchableRaces = new();
            foreach (var raceFK in PatcherSettings.General.PatchableRaces)
            {
                if (_stateProvider.LinkCache.TryResolve<IRaceGetter>(raceFK, out var raceGetter))
                {
                    PatchableRaces.Add(raceGetter.ToLinkGetter());
                }
            }
            PatchableRaces.Add(Skyrim.Race.DefaultRace.Resolve(_stateProvider.LinkCache).ToLinkGetter());
        }
    }
}
