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
            foreach (var raceGetter in PatcherEnvironmentProvider.Instance.Environment.LoadOrder.PriorityOrder.OnlyEnabledAndExisting().WinningOverrides<IRaceGetter>())
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
    }
}
