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
        private readonly IEnvironmentStateProvider _environmentProvider;
        private readonly Logger _logger;
        public PatchableRaceResolver(IEnvironmentStateProvider environmentProvider, Logger logger)
        {
            _environmentProvider = environmentProvider;
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
            foreach (var raceFK in PatcherSettings.General.PatchableRaces)
            {
                if (_environmentProvider.LinkCache.TryResolve<IRaceGetter>(raceFK, out var raceGetter))
                {
                    PatchableRaces.Add(raceGetter.ToLinkGetter());
                }
            }
            PatchableRaces.Add(Skyrim.Race.DefaultRace.Resolve(_environmentProvider.LinkCache).ToLinkGetter());
        }
    }
}
