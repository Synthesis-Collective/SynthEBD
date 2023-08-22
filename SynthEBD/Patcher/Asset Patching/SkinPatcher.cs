using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Records;
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

        public void ValidateArmorFlags(INpcGetter npcGetter, HashSet<IMajorRecord> recordsFromTemplate, ISkyrimMod outputMod) // in rare cases, SynthEBD can add new armature to an existing armor record. That record's armor needs to patched with the new armature's body flags
        {
            var formKeysFromTemplate = recordsFromTemplate.Select(x => x.FormKey).ToArray();
            
            if (npcGetter.WornArmor != null && npcGetter.WornArmor.TryResolve(_environmentStateProvider.LinkCache, out var armorGetter) && armorGetter.BodyTemplate != null && armorGetter.Armature != null)
            {
                foreach (var armaLink in armorGetter.Armature)
                {
                    var matchedTemplateRecord = recordsFromTemplate.Where(x => x.FormKey == armaLink.FormKey).FirstOrDefault();
                    if (matchedTemplateRecord != null)
                    {
                        var matchedArmatureRecord = matchedTemplateRecord as ArmorAddon;
                        if (matchedArmatureRecord != null && matchedArmatureRecord.BodyTemplate != null)
                        {
                            if (!CheckMatchingBipedObjectFlags(armorGetter.BodyTemplate.FirstPersonFlags, matchedArmatureRecord.BodyTemplate.FirstPersonFlags))
                            {
                                var armor = outputMod.Armors.GetOrAddAsOverride(armorGetter);
                                armor.BodyTemplate.FirstPersonFlags = EnableMatchingBipedObjectFlags(armor.BodyTemplate.FirstPersonFlags, matchedArmatureRecord.BodyTemplate.FirstPersonFlags);
                            }
                        }
                    }
                }
            }
        }

        public static bool CheckMatchingBipedObjectFlags(BipedObjectFlag toEnable, BipedObjectFlag enableThese)
        {
            return (toEnable & enableThese) == enableThese;
        }

        private static BipedObjectFlag EnableMatchingBipedObjectFlags(BipedObjectFlag toEnable, BipedObjectFlag enableThese)
        {
            return toEnable | enableThese;
        }
    }
}
