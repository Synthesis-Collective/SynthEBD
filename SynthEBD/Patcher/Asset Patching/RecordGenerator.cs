using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins.Cache.Internals.Implementations;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;
using Mutagen.Bethesda.FormKeys.SkyrimSE;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Reflection;
using Mutagen.Bethesda.Plugins.Records;
using Loqui;
using Mutagen.Bethesda.Plugins.Cache;
using FastMember;

namespace SynthEBD
{
    public class RecordGenerator
    {
        public static void CombinationToRecords(SubgroupCombination combination, NPCInfo npcInfo, ImmutableLoadOrderLinkCache<ISkyrimMod, ISkyrimModGetter> recordTemplateLinkCache, SkyrimMod outputMod)
        {
            var template = GetTemplateNPC(npcInfo, combination.AssetPack, recordTemplateLinkCache);

            HashSet<FilePathReplacement> wnamPaths = new HashSet<FilePathReplacement>();
            HashSet<FilePathReplacement> headtexPaths = new HashSet<FilePathReplacement>();
            List<FilePathReplacementParsed> nonHardcodedPaths = new List<FilePathReplacementParsed>();

            int longestPath = 0;

            foreach (var subgroup in combination.ContainedSubgroups)
            {
                foreach (var path in subgroup.Paths)
                {
                    var parsed = new FilePathReplacementParsed(path);

                    if (WornArmorPaths.Contains(path.Destination)) { wnamPaths.Add(path); }
                    else if (HeadTexturePaths.Contains(path.Destination)) { headtexPaths.Add(path); }
                    else
                    {
                        nonHardcodedPaths.Add(parsed);
                        if (parsed.Destination.Length > longestPath)
                        {
                            longestPath = parsed.Destination.Length;
                        }
                    }
                }
            }
            
            if (headtexPaths.Any())
            {
                AssignHeadTexture(npcInfo.NPC, outputMod, template, Patcher.MainLinkCache, recordTemplateLinkCache, headtexPaths);
            }
            if (wnamPaths.Any())
            {
                AssignBodyTextures(npcInfo, outputMod, template, Patcher.MainLinkCache, recordTemplateLinkCache, wnamPaths);
            }
            if (nonHardcodedPaths.Any())
            {
                AssignNonHardCodedTextures(npcInfo, template, nonHardcodedPaths, recordTemplateLinkCache, outputMod, longestPath);
            }
            int dbg = 0;
        }

        public static void AssignNonHardCodedTextures(NPCInfo npcInfo, INpcGetter template, List<FilePathReplacementParsed> nonHardcodedPaths, ImmutableLoadOrderLinkCache<ISkyrimMod, ISkyrimModGetter> recordTemplateLinkCache, SkyrimMod outputMod, int longestPath)
        {
            HashSet<IMajorRecord> assignedRecords = new HashSet<IMajorRecord>();

            Dictionary<string, dynamic> recordsAtPaths = new Dictionary<string, dynamic>(); // quickly look up record templates rather than redoing reflection work

            Dictionary<dynamic, Dictionary<string, dynamic>> objectLinkMap = new Dictionary<dynamic, Dictionary<string, dynamic>>();

            Dictionary<string, dynamic> objectsAtPath_NPC = new Dictionary<string, dynamic>();
            objectsAtPath_NPC.Add("", npcInfo.NPC);
            objectLinkMap.Add(npcInfo.NPC, objectsAtPath_NPC);


            Dictionary<string, dynamic> objectsAtPath_Template = new Dictionary<string, dynamic>();
            objectsAtPath_Template.Add("", template);
            objectLinkMap.Add(template, objectsAtPath_Template);

            dynamic currentObj;

            for (int i = 0; i < longestPath; i++)
            {
                for (int j = 0; j < nonHardcodedPaths.Count; j++)
                {
                    if (i == nonHardcodedPaths[j].Destination.Length) // Remove paths that were already assigned
                    {
                        nonHardcodedPaths.RemoveAt(j);
                        j--;
                    }
                }

                var groupedPathsAtI = nonHardcodedPaths.GroupBy(x => String.Join(".", x.Destination.ToList().GetRange(0, i + 1))); // group paths by the current path segment

                foreach (var group in groupedPathsAtI)
                {
                    string parentPath = String.Join(".", group.First().Destination.ToList().GetRange(0, i));
                    string currentSubPath = group.First().Destination[i];
                    var rootObj = objectsAtPath_NPC[parentPath];

                    // if the current set of paths has already been assigned to another record, get that record
                    string pathSignature = string.Concat(group.Select(x => x.Source));
                    if (recordsAtPaths.ContainsKey(pathSignature))
                    {
                        currentObj = recordsAtPaths[pathSignature];
                    }
                    else
                    {
                        // step through the path
                        currentObj = RecordPathParser.GetObjectAtPath(rootObj, currentSubPath, objectLinkMap, Patcher.MainLinkCache);
                        bool currentObjectIsARecord = RecordPathParser.ObjectIsRecord(currentObj, out FormKey? recordFormKey);
                        if (currentObjectIsARecord && !recordFormKey.Value.IsNull)
                        {
                            Type recordType = RecordPathParser.GetSubObject(currentObj, "Type");
                            if (recordType == null)
                            {
                                Type objType = currentObj.GetType();
                                var register = LoquiRegistration.GetRegister(objType);
                                recordType = register.GetterType;
                            }
                            if (Patcher.MainLinkCache.TryResolve(recordFormKey.Value, recordType, out var currentMajorRecordCommonGetter)) //if the current object is an existing record, resolve it so that it can be traversed
                            {
                                currentObj = GetOrAddGenericRecordAsOverride((IMajorRecordGetter)currentMajorRecordCommonGetter, outputMod);
                            }
                        }

                        // if the NPC doesn't have the given object (e.g. the NPC doesn't have a WNAM), assign in from template
                        if (currentObj == null || currentObjectIsARecord && recordFormKey.Value.IsNull)
                        {
                            currentObj = RecordPathParser.GetObjectAtPath(template, group.Key, objectLinkMap, recordTemplateLinkCache); // get corresponding object from template NPC

                            if (currentObj == null)
                            {
                                Logger.LogError("Error: neither NPC " + npcInfo.LogIDstring + " nor the record template " + template.EditorID + " contained a record at " + group.Key + ". Cannot assign this record.");
                            }

                            else
                            {
                                // if the template object is a record, add it to the generated patch and then copy it to the NPC
                                // if the template object is just a struct (not a record), simply copy it to the NPC
                                if (currentObjectIsARecord)
                                {
                                    FormKey templateFormKey = RecordPathParser.GetSubObject(currentObj, "FormKey");
                                    var newRecord = DeepCopyRecordToPatch(currentObj, templateFormKey.ModKey, recordTemplateLinkCache, outputMod);
                                    if (newRecord == null)
                                    {
                                        Logger.LogError("Record template error: Could not obtain a non-null FormKey for template NPC " + Logger.GetNPCLogNameString(template) + " at path: " + group.Key + ". This subrecord will not be assigned.");
                                        continue;
                                    }

                                    // increment editor ID number
                                    if (Patcher.EdidCounts.ContainsKey(newRecord.EditorID))
                                    {
                                        Patcher.EdidCounts[newRecord.EditorID]++;
                                        newRecord.EditorID += Patcher.EdidCounts[newRecord.EditorID];
                                    }
                                    else
                                    {
                                        Patcher.EdidCounts.Add(newRecord.EditorID, 1);
                                        newRecord.EditorID += 1;
                                    }
                                    SetRecord((IMajorRecordGetter)rootObj, currentSubPath, newRecord, outputMod);
                                    currentObj = newRecord;
                                }
                                else
                                {
                                    RecordPathParser.SetSubObject(rootObj, currentSubPath, currentObj);
                                }
                            }

                            // store paths associated with this record for future lookup to avoid having to repeat the reflection
                            if (currentObjectIsARecord)
                            {
                                recordsAtPaths.Add(pathSignature, currentObj); // for other NPCs who get the same combination and need to be assigned the same record
                            }
                        }
                    }

                    // if this is the last part of the path, attempt to assign the Source asset to the Destination
                    if (group.First().Destination.Length == i + 1)
                    {
                        foreach (var assetAssignment in group)
                        {
                            RecordPathParser.SetSubObject(rootObj, currentSubPath, assetAssignment.Source);
                            currentObj = assetAssignment.Source;
                        }
                    }
                    else
                    {
                        objectsAtPath_NPC.Add(group.Key, currentObj); // for next iteration of top for loop
                    }
                }
            }
        }

        public static IMajorRecord DeepCopyRecordToPatch(dynamic sourceRecordObj, ModKey sourceModKey, ILinkCache<ISkyrimMod, ISkyrimModGetter> sourceLinkCache, SkyrimMod destinationMod)
        {
            dynamic group = GetPatchRecordGroup(sourceRecordObj, destinationMod);
            IMajorRecord copiedRecord = (IMajorRecord)IGroupMixIns.DuplicateInAsNewRecord(group, sourceRecordObj);

            Dictionary<FormKey, FormKey> mapping = new Dictionary<FormKey, FormKey>();
            foreach (var fl in copiedRecord.ContainedFormLinks)
            {
                if (fl.FormKey.ModKey == sourceModKey && !fl.FormKey.IsNull && sourceLinkCache.TryResolve(fl.FormKey, fl.Type, out var subRecord))
                {
                    var copiedSubRecord = DeepCopyRecordToPatch(subRecord, sourceModKey, sourceLinkCache, destinationMod);
                    mapping.Add(fl.FormKey, copiedSubRecord.FormKey);
                }
            }
            if (mapping.Any())
            {
                copiedRecord.RemapLinks(mapping);
            }

            return copiedRecord;
        }

        public static dynamic GetOrAddGenericRecordAsOverride(IMajorRecordGetter recordGetter, SkyrimMod outputMod)
        {
            dynamic group = GetPatchRecordGroup(recordGetter, outputMod);
            return OverrideMixIns.GetOrAddAsOverride(group, recordGetter);
        }

        public static dynamic GetPatchRecordGroup(IMajorRecordGetter recordGetter, SkyrimMod outputMod)
        {
            var getterType = LoquiRegistration.GetRegister(recordGetter.GetType()).GetterType;
            return outputMod.GetTopLevelGroup(getterType);
        }

        public static void SetRecord(IMajorRecordGetter root, string propertyName, IMajorRecord value, SkyrimMod outputMod)
        {
            IMajorRecord settableRecord = GetOrAddGenericRecordAsOverride(root, outputMod);
            var settableRecordType = settableRecord.GetType();
            var property = settableRecordType.GetProperty(propertyName);
            var currentValue = property.GetValue(settableRecord);
            var valueType = currentValue.GetType();
            var valueMethods = valueType.GetMethods();

            var formKeySetter = valueMethods.Where(x => x.Name == "set_FormKey").FirstOrDefault();
            formKeySetter.Invoke(currentValue, new object[] { value.FormKey });
        }

        public static INpcGetter GetTemplateNPC(NPCInfo npcInfo, FlattenedAssetPack chosenAssetPack, ImmutableLoadOrderLinkCache<ISkyrimMod, ISkyrimModGetter> recordTemplateLinkCache)
        {
            FormKey templateFK = new FormKey();
            foreach (var additionalTemplate in chosenAssetPack.AdditionalRecordTemplateAssignments)
            {
                if (additionalTemplate.Races.Contains(npcInfo.AssetsRace))
                {
                    templateFK = additionalTemplate.TemplateNPC;
                    break;
                }
            }
            if (templateFK.IsNull)
            {
                templateFK = chosenAssetPack.DefaultRecordTemplate;
            }

            var templateFormLink = new FormLink<INpcGetter>(templateFK);

            if (!templateFormLink.TryResolve(recordTemplateLinkCache, out var templateNPC))
            {
                // Warn User
                return null;
            }
            else
            {
                return templateNPC;
            }
        }

        public static IMajorRecord AssignHeadTexture(INpcGetter currentNPC, SkyrimMod outputMod, INpcGetter templateNPC, ILinkCache<ISkyrimMod, ISkyrimModGetter> mainLinkCache, ILinkCache<ISkyrimMod, ISkyrimModGetter> templateLinkCache, HashSet<FilePathReplacement> paths)
        {
            TextureSet headTex = outputMod.TextureSets.AddNew();
            if (!currentNPC.HeadTexture.IsNull && mainLinkCache.TryResolve<ITextureSetGetter>(currentNPC.HeadTexture.FormKey, out var existingHeadTexture))
            {
                headTex.DeepCopyIn(existingHeadTexture);
            }
            else if (!templateNPC.HeadTexture.IsNull && templateLinkCache.TryResolve<ITextureSetGetter>(templateNPC.HeadTexture.FormKey, out var templateHeadTexture))
            {
                headTex.DeepCopyIn(templateHeadTexture);
            }
            else
            {
                Logger.LogReport("Could not resolve a head texture from NPC " + Logger.GetNPCLogNameString(currentNPC) + " or its corresponding record template.");
                return null;
            }

            foreach (var path in paths)
            {
                switch (path.Destination)
                {
                    case "HeadTexture.Height": headTex.Height = path.Source; break;
                    case "HeadTexture.Diffuse": headTex.Diffuse = path.Source; break;
                    case "HeadTexture.NormalOrGloss": headTex.NormalOrGloss = path.Source; break;
                    case "HeadTexture.GlowOrDetailMap": headTex.GlowOrDetailMap = path.Source; break;
                    case "HeadTexture.BacklightMaskOrSpecular": headTex.BacklightMaskOrSpecular = path.Source; break;
                }
            }

            var patchedNPC = outputMod.Npcs.GetOrAddAsOverride(currentNPC);
            patchedNPC.HeadTexture.SetTo(headTex);
            return headTex;
        }

        private static Armor AssignBodyTextures(NPCInfo npcInfo, SkyrimMod outputMod, INpcGetter templateNPC, ILinkCache<ISkyrimMod, ISkyrimModGetter> mainLinkCache, ILinkCache<ISkyrimMod, ISkyrimModGetter> templateLinkCache, HashSet<FilePathReplacement> paths)
        {
            Armor newSkin = outputMod.Armors.AddNew();
            bool assignedFromTemplate = false;
            if (!npcInfo.NPC.WornArmor.IsNull && mainLinkCache.TryResolve<IArmorGetter>(npcInfo.NPC.WornArmor.FormKey, out var existingWNAM))
            {
                newSkin.DeepCopyIn(existingWNAM);
            }
            else if (!templateNPC.WornArmor.IsNull && templateLinkCache.TryResolve<IArmorGetter>(templateNPC.WornArmor.FormKey, out var templateWNAM))
            {
                newSkin.DeepCopyIn(templateWNAM);
                assignedFromTemplate = true;
            }
            else
            {
                Logger.LogReport("Could not resolve a head texture from NPC " + npcInfo.LogIDstring + " or its corresponding record template.");
                outputMod.Armors.Remove(newSkin);
                return null;
            }

            var torsoArmorAddonPaths = paths.Where(x => TorsoArmorAddonPaths.Contains(x.Destination)).ToHashSet();
            var handsArmorAddonPaths = paths.Where(x => HandsArmorAddonPaths.Contains(x.Destination)).ToHashSet();
            var feetArmorAddonPaths = paths.Where(x => FeetArmorAddonPaths.Contains(x.Destination)).ToHashSet();
            var tailArmorAddonPaths = paths.Where(x => TailArmorAddonPaths.Contains(x.Destination)).ToHashSet();
            var allowedRaces = new HashSet<string>();
            allowedRaces.Add(npcInfo.NPC.Race.FormKey.ToString());
            var assetsRaceString = npcInfo.AssetsRace.ToString();
            if (!allowedRaces.Contains(assetsRaceString))
            {
                allowedRaces.Add(assetsRaceString);
            }
            allowedRaces.Add(Skyrim.Race.DefaultRace.FormKey.ToString());

            if (torsoArmorAddonPaths.Any())
            {
                var assignedTorso = AssignArmorAddon(newSkin, npcInfo, outputMod, templateNPC, mainLinkCache, templateLinkCache, torsoArmorAddonPaths, ArmorAddonType.Torso, allowedRaces, assignedFromTemplate);
            }
            if (handsArmorAddonPaths.Any())
            {
                var assignedHands = AssignArmorAddon(newSkin, npcInfo, outputMod, templateNPC, mainLinkCache, templateLinkCache, handsArmorAddonPaths, ArmorAddonType.Hands, allowedRaces, assignedFromTemplate);
            }
            if (feetArmorAddonPaths.Any())
            {
                var assignedFeet = AssignArmorAddon(newSkin, npcInfo, outputMod, templateNPC, mainLinkCache, templateLinkCache, feetArmorAddonPaths, ArmorAddonType.Feet, allowedRaces, assignedFromTemplate);
            }
            if (tailArmorAddonPaths.Any())
            {
                var assignedHands = AssignArmorAddon(newSkin, npcInfo, outputMod, templateNPC, mainLinkCache, templateLinkCache, tailArmorAddonPaths, ArmorAddonType.Tail, allowedRaces, assignedFromTemplate);
            }
            var patchedNPC = outputMod.Npcs.GetOrAddAsOverride(npcInfo.NPC);
            patchedNPC.WornArmor.SetTo(newSkin);
            return newSkin;
        }

        private enum ArmorAddonType
        {
            Torso,
            Hands,
            Feet,
            Tail
        }

        private static ArmorAddon AssignArmorAddon(Armor parentArmorRecord, NPCInfo npcInfo, SkyrimMod outputMod, INpcGetter templateNPC, ILinkCache<ISkyrimMod, ISkyrimModGetter> mainLinkCache, ILinkCache<ISkyrimMod, ISkyrimModGetter> templateLinkCache, HashSet<FilePathReplacement> paths, ArmorAddonType type, HashSet<string> currentRaceIDstrs, bool isFromTemplateLinkCache)
        {
            ArmorAddon newArmorAddon = outputMod.ArmorAddons.AddNew();
            IArmorAddonGetter templateAA;
            HashSet<IArmorAddonGetter> candidateAAs = new HashSet<IArmorAddonGetter>();
            bool replaceExistingArmature = false;

            // try to get the needed armor addon template record from the existing parent armor record
            foreach (var aa in parentArmorRecord.Armature.Where(x => Patcher.IgnoredArmorAddons.Contains(x.FormKey.AsLinkGetter<IArmorAddonGetter>()) == false))
            {
                if (!isFromTemplateLinkCache && aa.TryResolve(mainLinkCache, out var candidateAA))
                {
                    candidateAAs.Add(candidateAA);
                }
                else if (aa.TryResolve(templateLinkCache, out var candidateAAfromTemplate))
                {
                    candidateAAs.Add(candidateAAfromTemplate);
                }
            }

            templateAA = ChooseArmature(candidateAAs, type, currentRaceIDstrs);
            if (templateAA != null)
            {
                newArmorAddon.DeepCopyIn(templateAA);
                var assignedSkinTexture = AssignSkinTexture(newArmorAddon, isFromTemplateLinkCache, npcInfo, outputMod, mainLinkCache, templateLinkCache, paths);
                replaceExistingArmature = true;
            }

            // try to get the needed armor record from the corresponding record template
            else if (!templateNPC.WornArmor.IsNull && templateNPC.WornArmor.TryResolve(templateLinkCache, out var templateArmorGetter))
            {
                candidateAAs = new HashSet<IArmorAddonGetter>();
                foreach (var aa in templateArmorGetter.Armature.Where(x => Patcher.IgnoredArmorAddons.Contains(x.FormKey.AsLinkGetter<IArmorAddonGetter>()) == false))
                {
                    if (aa.TryResolve(templateLinkCache, out var candidateAA))
                    {
                        candidateAAs.Add(candidateAA);
                    }
                }
                templateAA = ChooseArmature(candidateAAs, type, currentRaceIDstrs);
                if (templateAA != null)
                {
                    newArmorAddon.DeepCopyIn(templateAA);
                    var assignedSkinTexture = AssignSkinTexture(newArmorAddon, true, npcInfo, outputMod, mainLinkCache, templateLinkCache, paths);
                }
            }

            if (templateAA == null)
            {
                Logger.LogReport("Could not resolve " + type.ToString() + " armature for NPC " + npcInfo.LogIDstring + " or its template.");
                outputMod.ArmorAddons.Remove(newArmorAddon);
            }
            else if (replaceExistingArmature == false)
            {
                parentArmorRecord.Armature.Add(newArmorAddon);
            }
            else
            {
                var templateFK = templateAA.FormKey.ToString();
                for (int i = 0; i < parentArmorRecord.Armature.Count; i++)
                {
                    if (parentArmorRecord.Armature[i].FormKey.ToString() == templateFK)
                    {
                        parentArmorRecord.Armature[i] = newArmorAddon.AsLinkGetter();
                    }
                }
            }

            return newArmorAddon;
        }

        private static TextureSet AssignSkinTexture(ArmorAddon parentArmorAddonRecord, bool isFromTemplateLinkCache, NPCInfo npcInfo, SkyrimMod outputMod, ILinkCache<ISkyrimMod, ISkyrimModGetter> mainLinkCache, ILinkCache<ISkyrimMod, ISkyrimModGetter> templateLinkCache, HashSet<FilePathReplacement> paths)
        { 
            ITextureSetGetter templateTextures = null;
            bool templateResolved = false;

            switch(isFromTemplateLinkCache)
            {
                case false: // parent record is from main link cache
                    switch (npcInfo.Gender)
                    {
                        case Gender.male:
                           templateResolved = parentArmorAddonRecord.SkinTexture.Male.TryResolve<ITextureSetGetter>(mainLinkCache, out templateTextures); break;
                        case Gender.female:
                            templateResolved = parentArmorAddonRecord.SkinTexture.Female.TryResolve<ITextureSetGetter>(mainLinkCache, out templateTextures); break;
                    } break;

                case true: // parent record is from record template link cache
                    switch (npcInfo.Gender)
                    {
                        case Gender.male:
                            templateResolved = parentArmorAddonRecord.SkinTexture.Male.TryResolve<ITextureSetGetter>(templateLinkCache, out templateTextures); break;
                        case Gender.female:
                            templateResolved = parentArmorAddonRecord.SkinTexture.Female.TryResolve<ITextureSetGetter>(templateLinkCache, out templateTextures); break;
                    }
                    break;
            }
            
            if (templateResolved)
            {
                TextureSet newSkinTexture = outputMod.TextureSets.AddNew();
                newSkinTexture.DeepCopyIn(templateTextures);
                foreach (var path in paths)
                {
                    if (path.Destination.Contains("GlowOrDetailMap"))
                    {
                        newSkinTexture.GlowOrDetailMap = path.Source;
                    }
                    else if (path.Destination.Contains("Diffuse"))
                    {
                        newSkinTexture.Diffuse = path.Source;
                    }
                    else if (path.Destination.Contains("NormalOrGloss"))
                    {
                        newSkinTexture.NormalOrGloss = path.Source;
                    }
                    else if (path.Destination.Contains("BacklightMaskOrSpecular"))
                    {
                        newSkinTexture.BacklightMaskOrSpecular = path.Source;
                    }
                }

                switch(npcInfo.Gender)
                {
                    case Gender.male: parentArmorAddonRecord.SkinTexture.Male = newSkinTexture.AsNullableLinkGetter(); break;
                    case Gender.female: parentArmorAddonRecord.SkinTexture.Female = newSkinTexture.AsNullableLinkGetter(); break;
                }

                return newSkinTexture;
            }
            else
            {
                Logger.LogReport("Could not resolve Skin Texture for NPC " + npcInfo.LogIDstring + " or its template.");
                return null;
            }
        }

        private static IArmorAddonGetter ChooseArmature(HashSet<IArmorAddonGetter> candidates, ArmorAddonType type, HashSet<string> requiredRaceFKstrs)
        {
            IEnumerable<IArmorAddonGetter> filteredFlags = null;
            switch(type)
            {
                case ArmorAddonType.Torso: filteredFlags = candidates.Where(x => x.BodyTemplate.FirstPersonFlags.HasFlag(BipedObjectFlag.Body)); break;
                case ArmorAddonType.Hands: filteredFlags = candidates.Where(x => x.BodyTemplate.FirstPersonFlags.HasFlag(BipedObjectFlag.Hands)); break;
                case ArmorAddonType.Feet: filteredFlags = candidates.Where(x => x.BodyTemplate.FirstPersonFlags.HasFlag(BipedObjectFlag.Feet)); break;
                case ArmorAddonType.Tail: filteredFlags = candidates.Where(x => x.BodyTemplate.FirstPersonFlags.HasFlag(BipedObjectFlag.Tail)); break;
            }
            if (!filteredFlags.Any()) { return null; }
            return filteredFlags.Where(x => requiredRaceFKstrs.Contains(x.Race.FormKey.ToString())).FirstOrDefault();
        }

        private static HashSet<string> TorsoArmorAddonPaths = new HashSet<string>()
        {
            "WornArmor.Armature[BodyTemplate.FirstPersonFlags,.HasFlag(BipedObjectFlag.Body) && PatchableRaces.Contains(Race)].SkinTexture.Male.GlowOrDetailMap",
            "WornArmor.Armature[BodyTemplate.FirstPersonFlags,.HasFlag(BipedObjectFlag.Body) && PatchableRaces.Contains(Race)].SkinTexture.Female.GlowOrDetailMap",

            "WornArmor.Armature[BodyTemplate.FirstPersonFlags,.HasFlag(BipedObjectFlag.Body) && PatchableRaces.Contains(Race)].SkinTexture.Male.Diffuse",
            "WornArmor.Armature[BodyTemplate.FirstPersonFlags,.HasFlag(BipedObjectFlag.Body) && PatchableRaces.Contains(Race)].SkinTexture.Female.Diffuse",

            "WornArmor.Armature[BodyTemplate.FirstPersonFlags,.HasFlag(BipedObjectFlag.Body) && PatchableRaces.Contains(Race)].SkinTexture.Male.NormalOrGloss",
            "WornArmor.Armature[BodyTemplate.FirstPersonFlags,.HasFlag(BipedObjectFlag.Body) && PatchableRaces.Contains(Race)].SkinTexture.Female.NormalOrGloss",

            "WornArmor.Armature[BodyTemplate.FirstPersonFlags,.HasFlag(BipedObjectFlag.Body) && PatchableRaces.Contains(Race)].SkinTexture.Male.BacklightMaskOrSpecular",
            "WornArmor.Armature[BodyTemplate.FirstPersonFlags,.HasFlag(BipedObjectFlag.Body) && PatchableRaces.Contains(Race)].SkinTexture.Female.BacklightMaskOrSpecular"
        };

        private static HashSet<string> HandsArmorAddonPaths = new HashSet<string>()
        {
            "WornArmor.Armature[BodyTemplate.FirstPersonFlags,.HasFlag(BipedObjectFlag.Hands) && PatchableRaces.Contains(Race)].SkinTexture.Male.GlowOrDetailMap",
            "WornArmor.Armature[BodyTemplate.FirstPersonFlags,.HasFlag(BipedObjectFlag.Hands) && PatchableRaces.Contains(Race)].SkinTexture.Female.GlowOrDetailMap",

            "WornArmor.Armature[BodyTemplate.FirstPersonFlags,.HasFlag(BipedObjectFlag.Hands) && PatchableRaces.Contains(Race)].SkinTexture.Male.Diffuse",
            "WornArmor.Armature[BodyTemplate.FirstPersonFlags,.HasFlag(BipedObjectFlag.Hands) && PatchableRaces.Contains(Race)].SkinTexture.Female.Diffuse",

            "WornArmor.Armature[BodyTemplate.FirstPersonFlags,.HasFlag(BipedObjectFlag.Hands) && PatchableRaces.Contains(Race)].SkinTexture.Male.NormalOrGloss",
            "WornArmor.Armature[BodyTemplate.FirstPersonFlags,.HasFlag(BipedObjectFlag.Hands) && PatchableRaces.Contains(Race)].SkinTexture.Female.NormalOrGloss",

            "WornArmor.Armature[BodyTemplate.FirstPersonFlags,.HasFlag(BipedObjectFlag.Hands) && PatchableRaces.Contains(Race)].SkinTexture.Male.BacklightMaskOrSpecular",
            "WornArmor.Armature[BodyTemplate.FirstPersonFlags,.HasFlag(BipedObjectFlag.Hands) && PatchableRaces.Contains(Race)].SkinTexture.Female.BacklightMaskOrSpecular"
        };

        private static HashSet<string> FeetArmorAddonPaths = new HashSet<string>()
        {
            "WornArmor.Armature[BodyTemplate.FirstPersonFlags,.HasFlag(BipedObjectFlag.Feet) && PatchableRaces.Contains(Race)].SkinTexture.Male.GlowOrDetailMap",
            "WornArmor.Armature[BodyTemplate.FirstPersonFlags,.HasFlag(BipedObjectFlag.Feet) && PatchableRaces.Contains(Race)].SkinTexture.Female.GlowOrDetailMap",

            "WornArmor.Armature[BodyTemplate.FirstPersonFlags,.HasFlag(BipedObjectFlag.Feet) && PatchableRaces.Contains(Race)].SkinTexture.Male.Diffuse",
            "WornArmor.Armature[BodyTemplate.FirstPersonFlags,.HasFlag(BipedObjectFlag.Feet) && PatchableRaces.Contains(Race)].SkinTexture.Female.Diffuse",

            "WornArmor.Armature[BodyTemplate.FirstPersonFlags,.HasFlag(BipedObjectFlag.Feet) && PatchableRaces.Contains(Race)].SkinTexture.Male.NormalOrGloss",
            "WornArmor.Armature[BodyTemplate.FirstPersonFlags,.HasFlag(BipedObjectFlag.Feet) && PatchableRaces.Contains(Race)].SkinTexture.Female.NormalOrGloss",

            "WornArmor.Armature[BodyTemplate.FirstPersonFlags,.HasFlag(BipedObjectFlag.Feet) && PatchableRaces.Contains(Race)].SkinTexture.Male.BacklightMaskOrSpecular",
            "WornArmor.Armature[BodyTemplate.FirstPersonFlags,.HasFlag(BipedObjectFlag.Feet) && PatchableRaces.Contains(Race)].SkinTexture.Female.BacklightMaskOrSpecular"
        };

        private static HashSet<string> TailArmorAddonPaths = new HashSet<string>()
        {
            "WornArmor.Armature[BodyTemplate.FirstPersonFlags,.HasFlag(BipedObjectFlag.Tail) && PatchableRaces.Contains(Race)].SkinTexture.Male.GlowOrDetailMap",
            "WornArmor.Armature[BodyTemplate.FirstPersonFlags,.HasFlag(BipedObjectFlag.Feet) && PatchableRaces.Contains(Race)].SkinTexture.Female.GlowOrDetailMap",

            "WornArmor.Armature[BodyTemplate.FirstPersonFlags,.HasFlag(BipedObjectFlag.Tail) && PatchableRaces.Contains(Race)].SkinTexture.Male.Diffuse",
            "WornArmor.Armature[BodyTemplate.FirstPersonFlags,.HasFlag(BipedObjectFlag.Tail) && PatchableRaces.Contains(Race)].SkinTexture.Female.Diffuse",

            "WornArmor.Armature[BodyTemplate.FirstPersonFlags,.HasFlag(BipedObjectFlag.Tail) && PatchableRaces.Contains(Race)].SkinTexture.Male.NormalOrGloss",
            "WornArmor.Armature[BodyTemplate.FirstPersonFlags,.HasFlag(BipedObjectFlag.Tail) && PatchableRaces.Contains(Race)].SkinTexture.Female.NormalOrGloss",

            "WornArmor.Armature[BodyTemplate.FirstPersonFlags,.HasFlag(BipedObjectFlag.Tail) && PatchableRaces.Contains(Race)].SkinTexture.Male.BacklightMaskOrSpecular",
            "WornArmor.Armature[BodyTemplate.FirstPersonFlags,.HasFlag(BipedObjectFlag.Tail) && PatchableRaces.Contains(Race)].SkinTexture.Female.BacklightMaskOrSpecular"
        };

        private static HashSet<string> WornArmorPaths = new HashSet<string>().Combine(TorsoArmorAddonPaths).Combine(HandsArmorAddonPaths).Combine(FeetArmorAddonPaths).Combine(TailArmorAddonPaths).ToHashSet();

        private static HashSet<string> HeadTexturePaths = new HashSet<string>()
        {
            "HeadTexture.Height",
            "HeadTexture.Diffuse" ,
            "HeadTexture.NormalOrGloss",
            "HeadTexture.GlowOrDetailMap",
            "HeadTexture.BacklightMaskOrSpecular",
        };
    }
}
