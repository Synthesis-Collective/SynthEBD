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
                    
                    // temp debugging for profiling generic record assignment function
                    /*
                    nonHardcodedPaths.Add(parsed);
                    if (parsed.Destination.Length > longestPath)
                    {
                        longestPath = parsed.Destination.Length;
                    }
                    */
                    // end temp debugging
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
                AssignNonHardCodedTextures(npcInfo, template, nonHardcodedPaths, recordTemplateLinkCache, outputMod, longestPath, true, false);
            }
        }

        public static void ReplacerCombinationToRecords(SubgroupCombination combination, NPCInfo npcInfo, SkyrimMod outputMod)
        {
            if (combination.DestinationType == SubgroupCombination.DestinationSpecifier.HeadPartFormKey)
            {
                AssignKnownHeadPartReplacer(combination, npcInfo.NPC, outputMod);
            }
            else if (combination.DestinationType == SubgroupCombination.DestinationSpecifier.Generic)
            {
                List<FilePathReplacementParsed> nonHardcodedPaths = new List<FilePathReplacementParsed>();
                int longestPath = 0;

                foreach (var subgroup in combination.ContainedSubgroups)
                {
                    foreach (var path in subgroup.Paths)
                    {
                        var parsed = new FilePathReplacementParsed(path);

                        nonHardcodedPaths.Add(parsed);
                        if (parsed.Destination.Length > longestPath)
                        {
                            longestPath = parsed.Destination.Length;
                        }
                    }
                }
                if (nonHardcodedPaths.Any())
                {
                    AssignNonHardCodedTextures(npcInfo, null, nonHardcodedPaths, null, outputMod, longestPath, false, true);
                }
            }
            else if (combination.DestinationType != SubgroupCombination.DestinationSpecifier.Main)
            {
                AssignSpecialCaseAssetReplacer(combination, npcInfo.NPC, outputMod);
            }
        }

        public static void AssignNonHardCodedTextures(NPCInfo npcInfo, INpcGetter template, List<FilePathReplacementParsed> nonHardcodedPaths, ImmutableLoadOrderLinkCache<ISkyrimMod, ISkyrimModGetter> recordTemplateLinkCache, SkyrimMod outputMod, int longestPath, bool assignFromTemplate, bool suppressMissingPathErrors)
        { 
            HashSet<IMajorRecord> assignedRecords = new HashSet<IMajorRecord>();

            Dictionary<string, dynamic> recordsAtPaths = new Dictionary<string, dynamic>(); // quickly look up record templates rather than redoing reflection work

            Dictionary<dynamic, Dictionary<string, dynamic>> objectLinkMap = new Dictionary<dynamic, Dictionary<string, dynamic>>();

            Dictionary<string, dynamic> objectsAtPath_NPC = new Dictionary<string, dynamic>();

            var currentNPC = outputMod.Npcs.GetOrAddAsOverride(npcInfo.NPC);

            objectsAtPath_NPC.Add("", currentNPC);
            objectLinkMap.Add(currentNPC, objectsAtPath_NPC);


            Dictionary<string, dynamic> objectsAtPath_Template = new Dictionary<string, dynamic>();
            objectsAtPath_Template.Add("", template);

            if (template != null)
            {
                objectLinkMap.Add(template, objectsAtPath_Template);
            }

            dynamic currentObj;

            HashSet<object> templateDerivedRecords = new HashSet<object>();

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

                var groupedPathsAtI = nonHardcodedPaths.GroupBy(x => BuildPath(x.Destination.ToList().GetRange(0, i + 1))); // group paths by the current path segment

                foreach (var group in groupedPathsAtI)
                {
                    string parentPath = BuildPath(group.First().Destination.ToList().GetRange(0, i));
                    string currentSubPath = group.First().Destination[i];
                    var rootObj = objectsAtPath_NPC[parentPath];
                    int? indexIfInArray = null;

                    // step through the path
                    bool npcHasObject = RecordPathParser.GetObjectAtPath(rootObj, currentSubPath, objectLinkMap, Patcher.MainLinkCache, suppressMissingPathErrors, out currentObj, out indexIfInArray); // update this function to out the aray index
                    bool npcHasNullFormLink = false;
                    bool templateHasObject = false;
                    if (npcHasObject)
                    {
                        // if the current object is a record, resolve it
                        if (RecordPathParser.ObjectHasFormKey(currentObj, out FormKey? recordFormKey))
                        { ///////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////Double check that the extra TryResolve is necessary. GetObjectAtPath already resolves Getters, so it should be possible to just get case currentObj to ImajorRecordGetter and call GetOrAddGenericRecordAsOverride()
                            if (!recordFormKey.Value.IsNull)
                            {
                                Type objType = currentObj.GetType();
                                var register = LoquiRegistration.GetRegister(objType);
                                Type recordType = register.GetterType;
                                /*
                                Type recordType = null;
                                if (RecordPathParser.GetSubObject(currentObj, "Type", out dynamic recordTypeDyn))
                                {
                                    recordType = (Type)recordTypeDyn;
                                }
                                else
                                {
                                    Type objType = currentObj.GetType();
                                    var register = LoquiRegistration.GetRegister(objType);
                                    recordType = register.GetterType;
                                }
                                */

                                if (Patcher.MainLinkCache.TryResolve(recordFormKey.Value, recordType, out var currentMajorRecordCommonGetter)) //if the current object is an existing record, resolve it so that it can be traversed
                                {
                                    if (!templateDerivedRecords.Contains(currentMajorRecordCommonGetter)) // make a copy of the record that the NPC currently has at this position, unless this record was set from a record template during a previous iteration, in which case it does not need to be copied.
                                    {
                                        dynamic recordGroup = GetPatchRecordGroup(currentObj, outputMod);

                                        IMajorRecord copiedRecord = (IMajorRecord)IGroupMixIns.DuplicateInAsNewRecord(recordGroup, (IMajorRecordGetter)currentMajorRecordCommonGetter);
                                        if (copiedRecord == null)
                                        {
                                            Logger.LogError("Could not deep copy a record for NPC " + Logger.GetNPCLogNameString(template) + " at path: " + group.Key + ". This subrecord will not be assigned.");
                                            continue;
                                        }

                                        copiedRecord.EditorID += "_" + npcInfo.NPC.EditorID;

                                        if (RecordPathParser.PathIsArray(currentSubPath))
                                        {
                                            SetRecordInArray(rootObj, indexIfInArray.Value, copiedRecord);
                                        }
                                        else if (ObjectIsRecord(rootObj, outputMod, out IMajorRecord recordToSet))
                                        {
                                            SetRecordByFormKey(recordToSet, currentSubPath, copiedRecord, outputMod);
                                        }
                                        else if (RecordPathParser.ObjectHasFormKey(rootObj))
                                        {
                                            SetFormLinkByFormKey((IFormLinkContainerGetter)rootObj, currentSubPath, copiedRecord, outputMod);
                                        }
                                        currentObj = copiedRecord;
                                    } 
                                }
                            }
                            else
                            {
                                npcHasNullFormLink = true;
                            }
                        }
                    }

                    // if the NPC doesn't have the given object (e.g. the NPC doesn't have a WNAM), assign in from template
                    if (assignFromTemplate && (!npcHasObject || npcHasNullFormLink) && RecordPathParser.GetObjectAtPath(template, group.Key, objectLinkMap, recordTemplateLinkCache, suppressMissingPathErrors, out currentObj)) // get corresponding object from template NPC
                    {
                        templateHasObject = true;
                        // if the template object is a record, add it to the generated patch and then copy it to the NPC
                        // if the template object is just a struct (not a record), simply copy it to the NPC
                        if (RecordPathParser.ObjectHasFormKey(currentObj, out FormKey? recordFormKey))
                        { ///////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////Double check that the GetSubObject(currentObj, "FormKey", out dynamic templateFormKeyDyn) is necessary. Should be able to call DeepcopyRecordTopPath using currentObj.FormKey.ModKey instead of templateFormKey.ModKey

                            // if the current set of paths has already been assigned to another record, get that record
                            string pathSignature = string.Concat(group.Select(x => x.Source));
                            if (recordsAtPaths.ContainsKey(pathSignature))
                            {
                                currentObj = recordsAtPaths[pathSignature];
                            }
                            else if (RecordPathParser.GetSubObject(currentObj, "FormKey", out dynamic templateFormKeyDyn))
                            {
                                FormKey templateFormKey = (FormKey)templateFormKeyDyn;
                                HashSet<IMajorRecord> copiedRecords = new HashSet<IMajorRecord>(); // includes current record and its subrecords
                                var newRecord = DeepCopyRecordToPatch(currentObj, templateFormKey.ModKey, recordTemplateLinkCache, outputMod, copiedRecords);
                                if (newRecord == null)
                                {
                                    Logger.LogError("Record template error: Could not obtain a subrecord for template NPC " + Logger.GetNPCLogNameString(template) + " at path: " + group.Key + ". This subrecord will not be assigned.");
                                    continue;
                                }

                                templateDerivedRecords.UnionWith(copiedRecords);
                                IncrementEditorID(copiedRecords);

                                if (RecordPathParser.PathIsArray(currentSubPath))
                                {
                                    SetRecordInArray(rootObj, indexIfInArray.Value, newRecord);
                                }
                                else if (ObjectIsRecord(rootObj, outputMod, out IMajorRecord recordToSet))
                                {
                                    SetRecordByFormKey(recordToSet, currentSubPath, newRecord, outputMod);
                                }
                                else if (RecordPathParser.ObjectHasFormKey(rootObj))
                                {
                                    SetFormLinkByFormKey((IFormLinkContainerGetter)rootObj, currentSubPath, newRecord, outputMod);
                                }

                                currentObj = newRecord;

                                recordsAtPaths.Add(pathSignature, newRecord); // store paths associated with this record for future lookup to avoid having to repeat the reflection for other NPCs who get the same combination and need to be assigned the same record
                            }
                            else
                            {
                                Logger.LogError("Record template error: Could not obtain a non-null FormKey for template NPC " + Logger.GetNPCLogNameString(template) + " at path: " + group.Key + ". This subrecord will not be assigned.");
                            }
                        }
                        else
                        {
                            RecordPathParser.SetSubObject(rootObj, currentSubPath, currentObj);
                        }
                    }

                    if (!npcHasObject && !templateHasObject && assignFromTemplate)
                    {
                        Logger.LogError("Error: neither NPC " + npcInfo.LogIDstring + " nor the record template " + template.EditorID + " contained a record at " + group.Key + ". Cannot assign this record.");
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
                    else if (!objectsAtPath_NPC.ContainsKey(group.Key)) // this condition evaluates true only when the current subpath is a top-level subpath (e.g. npc.x rather than npc.x.y) because GetObjectAtPath will populate the first subpath of the root object, which in this case is the NPC
                    {
                        objectsAtPath_NPC.Add(group.Key, currentObj); // for next iteration of top for loop
                    }
                }
            }
        }

        public static string BuildPath(List<string> splitPath)
        {
            string output = "";
            for (int i = 0; i < splitPath.Count; i++)
            {
                if (i > 0 && !RecordPathParser.PathIsArray(splitPath[i]))
                {
                    output += ".";
                }
                output += splitPath[i];
            }
            return output;
        }

        public static bool ObjectIsRecord(dynamic obj, SkyrimMod outputMod, out IMajorRecord record)
        {
            record = null;
            var resolvable = obj as IMajorRecordGetter;
            if (resolvable != null)
            {
                record = GetOrAddGenericRecordAsOverride(obj, outputMod);
                return true;
            }
            else
            {
                return false;
            }
        }

        public static IMajorRecord DeepCopyRecordToPatch(dynamic sourceRecordObj, ModKey sourceModKey, ILinkCache<ISkyrimMod, ISkyrimModGetter> sourceLinkCache, SkyrimMod destinationMod, HashSet<IMajorRecord> copiedRecords)
        {
            dynamic group = GetPatchRecordGroup(sourceRecordObj, destinationMod);
            IMajorRecord copiedRecord = (IMajorRecord)IGroupMixIns.DuplicateInAsNewRecord(group, sourceRecordObj);
            copiedRecords.Add(copiedRecord);

            Dictionary<FormKey, FormKey> mapping = new Dictionary<FormKey, FormKey>();
            foreach (var fl in copiedRecord.ContainedFormLinks)
            {
                if (fl.FormKey.ModKey == sourceModKey && !fl.FormKey.IsNull && sourceLinkCache.TryResolve(fl.FormKey, fl.Type, out var subRecord))
                {
                    var copiedSubRecord = DeepCopyRecordToPatch(subRecord, sourceModKey, sourceLinkCache, destinationMod, copiedRecords);
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

        public static void SetRecordByFormKey(IMajorRecord settableRecord, string propertyName, IMajorRecord value, SkyrimMod outputMod)
        {
            /*
            var settableRecordType = settableRecord.GetType();
            var property = settableRecordType.GetProperty(propertyName);
            var currentValue = property.GetValue(settableRecord);
            var valueType = currentValue.GetType();
            var valueMethods = valueType.GetMethods();

            var formKeySetter = valueMethods.Where(x => x.Name == "set_FormKey").FirstOrDefault();
            formKeySetter.Invoke(currentValue, new object[] { value.FormKey });*/
            if(RecordPathParser.GetSubObject(settableRecord, propertyName, out dynamic toSet))
            {
                RecordPathParser.SetSubObject(toSet, "FormKey", value.FormKey);
            }
            else
            {
                Logger.LogReport("Could not set record " + settableRecord.EditorID + " at " + propertyName);
            }
        }

        public static void SetFormLinkByFormKey(IFormLinkContainerGetter root, string propertyName, IMajorRecord value, SkyrimMod outputMod)
        {
            /*
            var settableRecordType = root.GetType();
            var property = settableRecordType.GetProperty(propertyName);
            var currentValue = property.GetValue(root);
            var valueType = currentValue.GetType();
            var valueMethods = valueType.GetMethods();

            var formKeySetter = valueMethods.Where(x => x.Name == "set_FormKey").FirstOrDefault();
            formKeySetter.Invoke(currentValue, new object[] { value.FormKey });
            */

            if (RecordPathParser.GetSubObject(root, propertyName, out dynamic toSet))
            {
                RecordPathParser.SetSubObject(toSet, "FormKey", value.FormKey);
            }
            else
            {
                Logger.LogReport("Could not set record at " + propertyName);
            }
        }

        public static void SetRecordInArray(dynamic root, int index, IMajorRecord value)
        {
            root[index].SetTo(value.FormKey);
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
                AssignEditorID(headTex, currentNPC, false);
            }
            else if (!templateNPC.HeadTexture.IsNull && templateLinkCache.TryResolve<ITextureSetGetter>(templateNPC.HeadTexture.FormKey, out var templateHeadTexture))
            {
                headTex.DeepCopyIn(templateHeadTexture);
                AssignEditorID(headTex, currentNPC, true);
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
            AssignEditorID(newSkin, npcInfo.NPC, assignedFromTemplate);

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
                var assignedTail = AssignArmorAddon(newSkin, npcInfo, outputMod, templateNPC, mainLinkCache, templateLinkCache, tailArmorAddonPaths, ArmorAddonType.Tail, allowedRaces, assignedFromTemplate);
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

        private static ArmorAddon AssignArmorAddon(Armor parentArmorRecord, NPCInfo npcInfo, SkyrimMod outputMod, INpcGetter templateNPC, ILinkCache<ISkyrimMod, ISkyrimModGetter> mainLinkCache, ILinkCache<ISkyrimMod, ISkyrimModGetter> templateLinkCache, HashSet<FilePathReplacement> paths, ArmorAddonType type, HashSet<string> currentRaceIDstrs, bool assignedFromTemplate)
        {
            ArmorAddon newArmorAddon = outputMod.ArmorAddons.AddNew();
            IArmorAddonGetter templateAA;
            HashSet<IArmorAddonGetter> candidateAAs = new HashSet<IArmorAddonGetter>();
            bool replaceExistingArmature = false;

            // try to get the needed armor addon template record from the existing parent armor record
            foreach (var aa in parentArmorRecord.Armature.Where(x => Patcher.IgnoredArmorAddons.Contains(x.FormKey.AsLinkGetter<IArmorAddonGetter>()) == false))
            {
                if (!assignedFromTemplate && aa.TryResolve(mainLinkCache, out var candidateAA))
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
                var assignedSkinTexture = AssignSkinTexture(newArmorAddon, assignedFromTemplate, npcInfo, outputMod, mainLinkCache, templateLinkCache, paths);
                replaceExistingArmature = true;

                AssignEditorID(newArmorAddon, npcInfo.NPC, assignedFromTemplate);
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
                    AssignEditorID(newArmorAddon, npcInfo.NPC, true);

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

        private static TextureSet AssignSkinTexture(ArmorAddon parentArmorAddonRecord, bool assignedFromTemplate, NPCInfo npcInfo, SkyrimMod outputMod, ILinkCache<ISkyrimMod, ISkyrimModGetter> mainLinkCache, ILinkCache<ISkyrimMod, ISkyrimModGetter> templateLinkCache, HashSet<FilePathReplacement> paths)
        { 
            ITextureSetGetter templateTextures = null;
            bool templateResolved = false;

            switch(assignedFromTemplate)
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

                AssignEditorID(newSkinTexture, npcInfo.NPC, assignedFromTemplate);

                return newSkinTexture;
            }
            else
            {
                Logger.LogReport("Could not resolve Skin Texture for NPC " + npcInfo.LogIDstring + " or its template.");
                return null;
            }
        }

        public static void AssignEditorID(IMajorRecord record, INpcGetter npc, bool copiedFromTemplate)
        {
            if (!copiedFromTemplate)
            {
                record.EditorID += "_" + npc.EditorID;
            }
            else
            {
                IncrementEditorID(new HashSet<IMajorRecord>() { record });
            }
        }

        public static void IncrementEditorID(HashSet<IMajorRecord> records)
        {
            foreach (var newRecord in records)
            {
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

        private static void AssignKnownHeadPartReplacer(SubgroupCombination subgroupCombination, INpcGetter npcGetter, SkyrimMod outputMod)
        {
            var npc = outputMod.Npcs.GetOrAddAsOverride(npcGetter);
            for (int i = 0; i < npc.HeadParts.Count; i++)
            {
                if (npc.HeadParts[i].FormKey == subgroupCombination.ReplacerDestinationFormKey)
                {
                    if(Patcher.MainLinkCache.TryResolve<IHeadPartGetter>(npc.HeadParts[i].FormKey, out var hpGetter) && Patcher.MainLinkCache.TryResolve<ITextureSetGetter>(hpGetter.TextureSet.FormKey, out var tsGetter))
                    {
                        var copiedHP = outputMod.HeadParts.AddNew();
                        copiedHP.DeepCopyIn(hpGetter);

                        var copiedTS = outputMod.TextureSets.AddNew();
                        copiedTS.DeepCopyIn(tsGetter);
                        
                        foreach (var subgroup in subgroupCombination.ContainedSubgroups)
                        {
                            foreach (var path in subgroup.Paths)
                            {
                                if (path.Destination.EndsWith("TextureSet.Diffuse", StringComparison.OrdinalIgnoreCase))
                                {
                                    copiedTS.Diffuse = path.Source;
                                }
                                else if (path.Destination.EndsWith("TextureSet.NormalOrGloss", StringComparison.OrdinalIgnoreCase))
                                {
                                    copiedTS.NormalOrGloss = path.Source;
                                }
                            }
                        }
                        copiedTS.EditorID += "_" + subgroupCombination.AssetPack.Source.ShortName + "." + subgroupCombination.Signature;
                        copiedHP.TextureSet.SetTo(copiedTS);
                        copiedHP.EditorID += "_" + subgroupCombination.AssetPack.Source.ShortName + "." + subgroupCombination.Signature;
                        npc.HeadParts[i] = copiedHP.AsLinkGetter();
                    }
                    else
                    {
                        // Warn user
                    }
                }
            }
        }

        private static void AssignSpecialCaseAssetReplacer(SubgroupCombination subgroupCombination, INpcGetter npcGetter, SkyrimMod outputMod)
        {
            var npc = outputMod.Npcs.GetOrAddAsOverride(npcGetter);
            switch (subgroupCombination.DestinationType)
            {
                case SubgroupCombination.DestinationSpecifier.MarksFemaleHumanoid04RightGashR: AssignHeadPartByDiffusePath(subgroupCombination, npc, outputMod, "actors\\character\\female\\facedetails\\facefemalerightsidegash_04.dds"); break;
                case SubgroupCombination.DestinationSpecifier.MarksFemaleHumanoid06RightGashR: AssignHeadPartByDiffusePath(subgroupCombination, npc, outputMod, "actors\\character\\female\\facedetails\\facefemalerightsidegash_06.dds"); break;
                default: break; // Warn user
            }
        }

        private static void AssignHeadPartByDiffusePath(SubgroupCombination subgroupCombination, Npc npc, SkyrimMod outputMod, string diffusePath)
        {
            for (int i = 0; i < npc.HeadParts.Count; i++)
            {
                if (Patcher.MainLinkCache.TryResolve<IHeadPartGetter>(npc.HeadParts[i].FormKey, out var hpGetter) && Patcher.MainLinkCache.TryResolve<ITextureSetGetter>(hpGetter.TextureSet.FormKey, out var tsGetter) && tsGetter.Diffuse == diffusePath)
                {
                    var copiedHP = outputMod.HeadParts.AddNew();
                    copiedHP.DeepCopyIn(hpGetter);

                    var copiedTS = outputMod.TextureSets.AddNew();
                    copiedTS.DeepCopyIn(tsGetter);

                    foreach (var subgroup in subgroupCombination.ContainedSubgroups)
                    {
                        foreach (var path in subgroup.Paths)
                        {
                            if (path.Destination.EndsWith("TextureSet.Diffuse", StringComparison.OrdinalIgnoreCase))
                            {
                                copiedTS.Diffuse = path.Source;
                            }
                            else if (path.Destination.EndsWith("TextureSet.NormalOrGloss", StringComparison.OrdinalIgnoreCase))
                            {
                                copiedTS.NormalOrGloss = path.Source;
                            }
                        }
                    }
                    copiedTS.EditorID += "_" + subgroupCombination.AssetPack.Source.ShortName + "." + subgroupCombination.Signature;
                    copiedHP.TextureSet.SetTo(copiedTS);
                    copiedHP.EditorID += "_" + subgroupCombination.AssetPack.Source.ShortName + "." + subgroupCombination.Signature;
                    npc.HeadParts[i] = copiedHP.AsLinkGetter();
                }
            }
        }

        private static HashSet<string> TorsoArmorAddonPaths = new HashSet<string>()
        {
            "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Body) && PatchableRaces.Contains(Race)].SkinTexture.Male.GlowOrDetailMap",
            "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Body) && PatchableRaces.Contains(Race)].SkinTexture.Female.GlowOrDetailMap",

            "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Body) && PatchableRaces.Contains(Race)].SkinTexture.Male.Diffuse",
            "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Body) && PatchableRaces.Contains(Race)].SkinTexture.Female.Diffuse",

            "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Body) && PatchableRaces.Contains(Race)].SkinTexture.Male.NormalOrGloss",
            "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Body) && PatchableRaces.Contains(Race)].SkinTexture.Female.NormalOrGloss",

            "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Body) && PatchableRaces.Contains(Race)].SkinTexture.Male.BacklightMaskOrSpecular",
            "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Body) && PatchableRaces.Contains(Race)].SkinTexture.Female.BacklightMaskOrSpecular"
        };

        private static HashSet<string> HandsArmorAddonPaths = new HashSet<string>()
        {
            "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Hands) && PatchableRaces.Contains(Race)].SkinTexture.Male.GlowOrDetailMap",
            "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Hands) && PatchableRaces.Contains(Race)].SkinTexture.Female.GlowOrDetailMap",

            "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Hands) && PatchableRaces.Contains(Race)].SkinTexture.Male.Diffuse",
            "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Hands) && PatchableRaces.Contains(Race)].SkinTexture.Female.Diffuse",

            "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Hands) && PatchableRaces.Contains(Race)].SkinTexture.Male.NormalOrGloss",
            "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Hands) && PatchableRaces.Contains(Race)].SkinTexture.Female.NormalOrGloss",

            "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Hands) && PatchableRaces.Contains(Race)].SkinTexture.Male.BacklightMaskOrSpecular",
            "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Hands) && PatchableRaces.Contains(Race)].SkinTexture.Female.BacklightMaskOrSpecular"
        };

        private static HashSet<string> FeetArmorAddonPaths = new HashSet<string>()
        {
            "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Feet) && PatchableRaces.Contains(Race)].SkinTexture.Male.GlowOrDetailMap",
            "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Feet) && PatchableRaces.Contains(Race)].SkinTexture.Female.GlowOrDetailMap",

            "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Feet) && PatchableRaces.Contains(Race)].SkinTexture.Male.Diffuse",
            "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Feet) && PatchableRaces.Contains(Race)].SkinTexture.Female.Diffuse",

            "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Feet) && PatchableRaces.Contains(Race)].SkinTexture.Male.NormalOrGloss",
            "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Feet) && PatchableRaces.Contains(Race)].SkinTexture.Female.NormalOrGloss",

            "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Feet) && PatchableRaces.Contains(Race)].SkinTexture.Male.BacklightMaskOrSpecular",
            "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Feet) && PatchableRaces.Contains(Race)].SkinTexture.Female.BacklightMaskOrSpecular"
        };

        private static HashSet<string> TailArmorAddonPaths = new HashSet<string>()
        {
            "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Tail) && PatchableRaces.Contains(Race)].SkinTexture.Male.GlowOrDetailMap",
            "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Tail) && PatchableRaces.Contains(Race)].SkinTexture.Female.GlowOrDetailMap",

            "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Tail) && PatchableRaces.Contains(Race)].SkinTexture.Male.Diffuse",
            "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Tail) && PatchableRaces.Contains(Race)].SkinTexture.Female.Diffuse",

            "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Tail) && PatchableRaces.Contains(Race)].SkinTexture.Male.NormalOrGloss",
            "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Tail) && PatchableRaces.Contains(Race)].SkinTexture.Female.NormalOrGloss",

            "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Tail) && PatchableRaces.Contains(Race)].SkinTexture.Male.BacklightMaskOrSpecular",
            "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Tail) && PatchableRaces.Contains(Race)].SkinTexture.Female.BacklightMaskOrSpecular"
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
