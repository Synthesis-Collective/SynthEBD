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
    /// <summary>
    /// Adds a racial ability <see cref="Spell"/> to the actor-effect list of every patchable race,
    /// overriding each race record in the output mod. Used to attach the EBD runtime ability spell.
    /// </summary>
    public class ApplyRacialSpell
    {
        /// <summary>
        /// Overrides each patchable race (resolved via <see cref="PatchableRaceResolver.CompilePatchableRaces"/>)
        /// into <paramref name="outputMod"/> and appends <paramref name="spell"/> to its actor-effect list,
        /// creating the list first if the race has none.
        /// </summary>
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
