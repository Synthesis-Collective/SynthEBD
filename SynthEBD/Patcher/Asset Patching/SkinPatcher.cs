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
    /// <summary>
    /// Patches an NPC's worn skin (the <c>WornArmor</c> ARMO and its ARMA armatures) so that any texture
    /// sets that were swapped during asset selection are reflected on the skin records. Delegates the actual
    /// alternate-texture edits to <see cref="ArmorPatcher"/>, and fixes up biped-object flags when new
    /// armature is grafted onto an existing armor. Part of the asset-patching record output stage.
    /// </summary>
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

        /// <summary>
        /// Resolves the NPC's winning worn armor and patches alternate textures on it and each of its
        /// armatures using <paramref name="replacedRecords"/> (a map from original to replacement texture-set
        /// FormKeys). Writes overrides into <paramref name="outputMod"/> as needed.
        /// </summary>
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

        /// <summary>
        /// When SynthEBD has grafted new armature onto an existing worn armor, ensures the armor's body
        /// template biped-object flags include the flags of the newly added armature (otherwise the new
        /// armature would not render). Writes an armor override into <paramref name="outputMod"/> only if a
        /// flag mismatch is found.
        /// </summary>
        public void ValidateArmorFlags(INpcGetter npcGetter, HashSet<IMajorRecord> recordsFromTemplate, ISkyrimMod outputMod) // in rare cases, SynthEBD can add new armature to an existing armor record. That record's armor needs to patched with the new armature's body flags
        {
            var formKeysFromTemplate = recordsFromTemplate.Select(x => x.FormKey).ToArray();
            
            if (npcGetter.WornArmor != null && npcGetter.WornArmor.TryResolve(_environmentStateProvider.LinkCache, out var armorGetter) && armorGetter.BodyTemplate != null && armorGetter.Armature != null)
            {
                foreach (var armaLink in armorGetter.Armature)
                {
                    var matchedTemplateRecord = recordsFromTemplate.FirstOrDefault(x => x.FormKey == armaLink.FormKey);
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

        /// <summary>
        /// Returns true if <paramref name="toEnable"/> already has every flag set in <paramref name="enableThese"/>.
        /// </summary>
        public static bool CheckMatchingBipedObjectFlags(BipedObjectFlag toEnable, BipedObjectFlag enableThese)
        {
            return (toEnable & enableThese) == enableThese;
        }

        /// <summary>
        /// Returns <paramref name="toEnable"/> with all flags from <paramref name="enableThese"/> OR'd in.
        /// </summary>
        private static BipedObjectFlag EnableMatchingBipedObjectFlags(BipedObjectFlag toEnable, BipedObjectFlag enableThese)
        {
            return toEnable | enableThese;
        }
    }
}
