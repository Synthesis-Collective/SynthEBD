using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;
using Mutagen.Bethesda;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SynthEBD
{
    public class ApplyRacialSpell
    {
        public static void ApplySpell(SkyrimMod outputMod, Spell spell)
        {
            foreach (var raceGetter in CompilePatchableRaces())
            {
                if (PatcherSettings.General.PatchableRaces.Contains(raceGetter.FormKey))
                {
                    var patchableRace = outputMod.Races.GetOrAddAsOverride(raceGetter);
                    if (patchableRace.ActorEffect == null)
                    {
                        patchableRace.ActorEffect = new Noggog.ExtendedList<IFormLinkGetter<ISpellRecordGetter>>();
                    }
                    patchableRace.ActorEffect.Add(spell);
                }
            }
        }

        public static HashSet<IRaceGetter> CompilePatchableRaces() // combines explicit patchable races, race groupings, and aliases
        {
            HashSet<FormKey> raceFKs = new();
            foreach (var pr in PatcherSettings.General.PatchableRaces)
            {
                if (!raceFKs.Contains(pr))
                {
                    raceFKs.Add(pr);
                }
            }

            foreach (var grouping in PatcherSettings.General.RaceGroupings)
            {
                foreach (var member in grouping.Races)
                {
                    if (!raceFKs.Contains(member))
                    {
                        raceFKs.Add(member);
                    }
                }
            }

            foreach (var alias in PatcherSettings.General.RaceAliases)
            {
                if (!raceFKs.Contains(alias.Race))
                {
                    raceFKs.Add(alias.Race);
                }
            }

            HashSet<IRaceGetter> races = new();
            foreach (var formKey in raceFKs)
            {
                if (PatcherEnvironmentProvider.Instance.Environment.LinkCache.TryResolve<IRaceGetter>(formKey, out var raceGetter) && raceGetter is not null)
                {
                    races.Add(raceGetter);
                }
            }

            return races;
        }
    }
}