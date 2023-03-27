using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Skyrim;
using ReactiveUI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SynthEBD
{
    public class ArmorPatcher
    {
        private readonly IEnvironmentStateProvider _environmentStateProvider;
        private readonly Logger _logger;
        public ArmorPatcher(IEnvironmentStateProvider environmentStateProvider, Logger logger)
        {
            _environmentStateProvider = environmentStateProvider;
            _logger = logger;
        }

        public void PatchArmorTextures(NPCInfo npcInfo, Dictionary<FormKey, FormKey> replacedRecords, ISkyrimMod outputMod)
        {
            if (npcInfo.NPC.DefaultOutfit != null && !npcInfo.NPC.DefaultOutfit.IsNull && _environmentStateProvider.LinkCache.TryResolve<IOutfitGetter>(npcInfo.NPC.DefaultOutfit.FormKey, out var outfitGetter) && outfitGetter.Items != null)
            {
                foreach (var armorLink in outfitGetter.Items)
                {
                    if (BaseGamePlugins.Plugins.Contains(armorLink.FormKey.ModKey.FileName.String)) { continue; }

                    if (_environmentStateProvider.LinkCache.TryResolve<IArmorGetter>(armorLink.FormKey, out var armorGetter) && armorGetter != null)
                    {
                        // check armor record alternate textures
                        PatchArmorAltTextures(npcInfo, replacedRecords, outputMod, armorGetter);

                        // check armature
                        if (armorGetter.Armature != null) 
                        {
                            foreach (var armaLink in armorGetter.Armature.Where(x => x.FormKey != null).ToArray())
                            {
                                if (_environmentStateProvider.LinkCache.TryResolve<IArmorAddonGetter>(armaLink.FormKey, out var armaGetter))
                                {
                                    PatchArmatureSkinTextures(npcInfo, replacedRecords, outputMod, armaGetter);
                                    PatchArmatureAltTextures(npcInfo, replacedRecords, outputMod, armaGetter);
                                }
                            } 
                        }
                    }
                }
            }
        }     
        
        public void PatchArmorAltTextures(NPCInfo npcInfo, Dictionary<FormKey, FormKey> replacedRecords, ISkyrimMod outputMod, IArmorGetter armorGetter)
        {
            Armor currentArmor = null; // don't initialize until necessary to avoid ITM

            if (armorGetter.WorldModel != null)
            {
                switch (npcInfo.Gender)
                {
                    case Gender.Male:
                        if (armorGetter.WorldModel.Male != null && armorGetter.WorldModel.Male.Model != null && armorGetter.WorldModel.Male.Model.AlternateTextures != null)
                        {
                            for (int i = 0; i < armorGetter.WorldModel.Male.Model.AlternateTextures.Count; i++)
                            {
                                var texture = armorGetter.WorldModel.Male.Model.AlternateTextures[i];
                                if (texture.NewTexture != null && replacedRecords.ContainsKey(texture.NewTexture.FormKey))
                                {
                                    if (currentArmor == null)
                                    {
                                        currentArmor = outputMod.Armors.GetOrAddAsOverride(armorGetter);
                                    }
                                    AlternateTexture replacementTexture = new();
                                    replacementTexture.Name = texture.Name;
                                    replacementTexture.Index = texture.Index;
                                    replacementTexture.NewTexture.SetTo(replacedRecords[texture.NewTexture.FormKey]);
                                    currentArmor.WorldModel.Male.Model.AlternateTextures[i] = replacementTexture;
                                }
                            }
                        }
                        break;
                    case Gender.Female:
                        if (armorGetter.WorldModel.Female != null && armorGetter.WorldModel.Female.Model != null && armorGetter.WorldModel.Female.Model.AlternateTextures != null)
                        {
                            for (int i = 0; i < armorGetter.WorldModel.Female.Model.AlternateTextures.Count; i++)
                            {
                                var texture = armorGetter.WorldModel.Female.Model.AlternateTextures[i];
                                if (texture.NewTexture != null && replacedRecords.ContainsKey(texture.NewTexture.FormKey))
                                {
                                    if (currentArmor == null)
                                    {
                                        currentArmor = outputMod.Armors.GetOrAddAsOverride(armorGetter);
                                    }
                                    AlternateTexture replacementTexture = new();
                                    replacementTexture.Name = texture.Name;
                                    replacementTexture.Index = texture.Index;
                                    replacementTexture.NewTexture.SetTo(replacedRecords[texture.NewTexture.FormKey]);
                                    currentArmor.WorldModel.Female.Model.AlternateTextures[i] = replacementTexture;
                                }
                            }
                        }
                        break;
                }
            }
        }

        public void PatchArmatureSkinTextures(NPCInfo npcInfo, Dictionary<FormKey, FormKey> replacedRecords, ISkyrimMod outputMod, IArmorAddonGetter armaGetter)
        {
            switch (npcInfo.Gender)
            {
                case Gender.Male:
                    if (armaGetter.SkinTexture != null && armaGetter.SkinTexture.Male != null && replacedRecords.ContainsKey(armaGetter.SkinTexture.Male.FormKey))
                    {
                        var currentArmature = outputMod.ArmorAddons.GetOrAddAsOverride(armaGetter);
                        var maleFormLink = currentArmature.SkinTexture.Male.AsNullable();
                        var femaleFormLink = currentArmature.SkinTexture.Female.AsNullable();
                        maleFormLink.SetTo(replacedRecords[armaGetter.SkinTexture.Male.FormKey]);
                        var newSkinTexture = new GenderedItem<IFormLinkNullableGetter<ITextureSetGetter>>(maleFormLink, femaleFormLink);
                        currentArmature.SkinTexture = newSkinTexture;
                    }
                    break;

                case Gender.Female:
                    if (armaGetter.SkinTexture != null && armaGetter.SkinTexture.Female != null && replacedRecords.ContainsKey(armaGetter.SkinTexture.Female.FormKey))
                    {
                        var currentArmature = outputMod.ArmorAddons.GetOrAddAsOverride(armaGetter);
                        var maleFormLink = currentArmature.SkinTexture.Male.AsNullable();
                        var femaleFormLink = currentArmature.SkinTexture.Female.AsNullable();
                        femaleFormLink.SetTo(replacedRecords[armaGetter.SkinTexture.Female.FormKey]);
                        var newSkinTexture = new GenderedItem<IFormLinkNullableGetter<ITextureSetGetter>>(maleFormLink, femaleFormLink);
                        currentArmature.SkinTexture = newSkinTexture;
                    }
                    break;
            }
        }

        public void PatchArmatureAltTextures(NPCInfo npcInfo, Dictionary<FormKey, FormKey> replacedRecords, ISkyrimMod outputMod, IArmorAddonGetter armaGetter)
        {
            ArmorAddon currentArmature = null; // don't initialize until necessary to avoid ITM
            switch (npcInfo.Gender)
            {
                case Gender.Male:
                    if (armaGetter.WorldModel != null && armaGetter.WorldModel.Male != null && armaGetter.WorldModel.Male.AlternateTextures != null)
                    {
                        for (int i = 0; i < armaGetter.WorldModel.Male.AlternateTextures.Count; i++)
                        {
                            var texture = armaGetter.WorldModel.Male.AlternateTextures[i];
                            if (texture.NewTexture != null && replacedRecords.ContainsKey(texture.NewTexture.FormKey))
                            {
                                if (currentArmature == null)
                                {
                                    currentArmature = outputMod.ArmorAddons.GetOrAddAsOverride(armaGetter);
                                }
                                AlternateTexture replacementTexture = new();
                                replacementTexture.Name = texture.Name;
                                replacementTexture.Index = texture.Index;
                                replacementTexture.NewTexture.SetTo(replacedRecords[texture.NewTexture.FormKey]);
                                currentArmature.WorldModel.Male.AlternateTextures[i] = replacementTexture;
                            }
                        }
                    }
                    break;

                case Gender.Female:
                    if (armaGetter.WorldModel != null && armaGetter.WorldModel.Female != null && armaGetter.WorldModel.Female.AlternateTextures != null)
                    {
                        for (int i = 0; i < armaGetter.WorldModel.Female.AlternateTextures.Count; i++)
                        {
                            var texture = armaGetter.WorldModel.Female.AlternateTextures[i];
                            if (texture.NewTexture != null && replacedRecords.ContainsKey(texture.NewTexture.FormKey))
                            {
                                if (currentArmature == null)
                                {
                                    currentArmature = outputMod.ArmorAddons.GetOrAddAsOverride(armaGetter);
                                }
                                AlternateTexture replacementTexture = new();
                                replacementTexture.Name = texture.Name;
                                replacementTexture.Index = texture.Index;
                                replacementTexture.NewTexture.SetTo(replacedRecords[texture.NewTexture.FormKey]);
                                currentArmature.WorldModel.Female.AlternateTextures[i] = replacementTexture;
                            }
                        }
                    }
                    break;
            }
        }
    }
}
