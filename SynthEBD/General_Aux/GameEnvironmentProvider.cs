using Mutagen.Bethesda;
using Mutagen.Bethesda.Environments;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Skyrim;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SynthEBD
{
    public class GameEnvironmentProvider
    {
        public static readonly GameEnvironmentProvider Instance = new();

        private static Lazy<IGameEnvironmentState<ISkyrimMod, ISkyrimModGetter>> _env = new(
            () =>
            {
                var gameRelease = SkyrimRelease.SkyrimSE;
                return GameEnvironment.Typical.Skyrim(gameRelease, LinkCachePreferences.OnlyIdentifiers());
            });

        public static IGameEnvironmentState<ISkyrimMod, ISkyrimModGetter> MyEnvironment => _env.Value;
    }
}
