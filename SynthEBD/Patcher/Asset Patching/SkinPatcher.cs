using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SynthEBD
{
    public class SkinPatcher
    {
        private readonly IEnvironmentStateProvider _environmentStateProvider;
        private readonly Logger _logger;
        private readonly ArmorPatcher _armorPatcher;
        public SkinPatcher(IEnvironmentStateProvider environmentStateProvider, Logger logger, ArmorPatcher armorPatcher)
        {
            _environmentStateProvider = environmentStateProvider;
            _logger = logger;
            _armorPatcher = armorPatcher;
        }

        public void PatchAltTextures(NPCInfo npcInfo, Dictionary<FormKey, FormKey> replacedRecords, ISkyrimMod outputMod)
        {
            if(_environmentStateProvider.LinkCache.TryResolve<INpcGetter>(npcInfo.NPC.FormKey, out var winningNPCGetter) && 
                winningNPCGetter.WornArmor != null && 
                !winningNPCGetter.WornArmor.IsNull
                && _environmentStateProvider.LinkCache.TryResolve<IArmorGetter>(winningNPCGetter.WornArmor.FormKey, out var armorGetter))
            {
                _armorPatcher.PatchArmorAltTextures(npcInfo, replacedRecords, outputMod, armorGetter);

                if (armorGetter.Armature != null)
                {
                    foreach (var armaLink in armorGetter.Armature)
                    {
                        if (_environmentStateProvider.LinkCache.TryResolve<IArmorAddonGetter>(armaLink.FormKey, out var armaGetter) && armaGetter != null)
                        {
                            _armorPatcher.PatchArmatureAltTextures(npcInfo, replacedRecords, outputMod, armaGetter);
                        }
                    }
                }
            }
        }
    }
}
