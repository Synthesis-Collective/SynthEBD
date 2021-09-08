using Mutagen.Bethesda;
using Mutagen.Bethesda.Environments;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Skyrim;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SynthEBD.GUI_Aux
{
    public class GameEnvironmentProvider
    {
        private Lazy<IGameEnvironmentState<ISkyrimMod, ISkyrimModGetter>> _env = new(
            () =>
        {
            var gameRelease = SkyrimRelease.SkyrimSE;
            return GameEnvironment.Typical.Skyrim(gameRelease, LinkCachePreferences.OnlyIdentifiers());
        });

        public IGameEnvironmentState<ISkyrimMod, ISkyrimModGetter> MyEnvironment => _env.Value;
    }
}
