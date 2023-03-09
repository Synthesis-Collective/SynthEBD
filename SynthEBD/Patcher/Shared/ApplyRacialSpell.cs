using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;
using Mutagen.Bethesda;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Mutagen.Bethesda.Plugins.Cache;

namespace SynthEBD
{
    public class ApplyRacialSpell
    {
        public static void ApplySpell(ISkyrimMod outputMod, Spell spell, ILinkCache linkCache, PatcherState patcherState)
        {
            foreach (var raceGetter in PatchableRaceResolver.CompilePatchableRaces(linkCache, patcherState, true, true, false))
            {
                var patchableRace = outputMod.Races.GetOrAddAsOverride(raceGetter);
                if (patchableRace != null)
                {
                    if (patchableRace.ActorEffect == null)
                    {
                        patchableRace.ActorEffect = new Noggog.ExtendedList<IFormLinkGetter<ISpellRecordGetter>>();
                    }
                    patchableRace.ActorEffect.Add(spell);
                }
            }
        }

       
    }
}
