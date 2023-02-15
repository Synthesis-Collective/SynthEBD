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
            foreach (var raceGetter in CompilePatchableRaces(linkCache, patcherState))
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

        public static HashSet<IRaceGetter> CompilePatchableRaces(ILinkCache linkCache, PatcherState patcherState) // combines explicit patchable races, race groupings, and aliases
        {
            HashSet<FormKey> raceFKs = new();
            foreach (var pr in patcherState.GeneralSettings.PatchableRaces)
            {
                if (!raceFKs.Contains(pr))
                {
                    raceFKs.Add(pr);
                }
            }

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

            foreach (var alias in patcherState.GeneralSettings.RaceAliases)
            {
                if (!raceFKs.Contains(alias.Race))
                {
                    raceFKs.Add(alias.Race);
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

            return races;
        }
    }
}
