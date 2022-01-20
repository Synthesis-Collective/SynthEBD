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
        public static void CombinationToRecords(List<SubgroupCombination> combinations, NPCInfo npcInfo, ImmutableLoadOrderLinkCache<ISkyrimMod, ISkyrimModGetter> recordTemplateLinkCache, SkyrimMod outputMod)
        {
            HashSet<FilePathReplacementParsed> wnamPaths = new HashSet<FilePathReplacementParsed>();
            HashSet<FilePathReplacementParsed> headtexPaths = new HashSet<FilePathReplacementParsed>();
            List<FilePathReplacementParsed> nonHardcodedPaths = new List<FilePathReplacementParsed>();

            int longestPath = 0;

            foreach (var combination in combinations)
            {
                foreach (var subgroup in combination.ContainedSubgroups)
                {
                    foreach (var path in subgroup.Paths)
                    {
                        var parsed = new FilePathReplacementParsed(path, npcInfo, combination.AssetPack, recordTemplateLinkCache);

                        /*
                        if (WornArmorPaths.Contains(path.Destination)) { wnamPaths.Add(parsed); }
                        else if (HeadTexturePaths.Contains(path.Destination)) { headtexPaths.Add(parsed); }
                        else
                        {
                            nonHardcodedPaths.Add(parsed);
                            if (parsed.Destination.Length > longestPath)
                            {
                                longestPath = parsed.Destination.Length;
                            }
                        }
                        */

                        // temp debugging for profiling generic record assignment function
                        
                        nonHardcodedPaths.Add(parsed);
                        if (parsed.Destination.Length > longestPath)
                        {
                            longestPath = parsed.Destination.Length;
                        }
                        
                        // end temp debugging
                    }
                }
            }

            if (headtexPaths.Any())
            {
                AssignHeadTexture(npcInfo, outputMod, Patcher.MainLinkCache, recordTemplateLinkCache, headtexPaths);
            }
            if (wnamPaths.Any())
            {
                AssignBodyTextures(npcInfo, outputMod, Patcher.MainLinkCache, recordTemplateLinkCache, wnamPaths);
            }
            if (nonHardcodedPaths.Any())
            {
                AssignNonHardCodedTextures(npcInfo, nonHardcodedPaths, recordTemplateLinkCache, outputMod, longestPath, true, false);
            }
        }

        public static void ReplacerCombinationToRecords(SubgroupCombination combination, NPCInfo npcInfo, SkyrimMod outputMod, ImmutableLoadOrderLinkCache<ISkyrimMod, ISkyrimModGetter> recordTemplateLinkCache)
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
                        var parsed = new FilePathReplacementParsed(path, npcInfo, combination.AssetPack, recordTemplateLinkCache);

                        nonHardcodedPaths.Add(parsed);
                        if (parsed.Destination.Length > longestPath)
                        {
                            longestPath = parsed.Destination.Length;
                        }
                    }
                }
                if (nonHardcodedPaths.Any())
                {
                    AssignNonHardCodedTextures(npcInfo, nonHardcodedPaths, null, outputMod, longestPath, false, true);
                }
            }
            else if (combination.DestinationType != SubgroupCombination.DestinationSpecifier.Main)
            {
                AssignSpecialCaseAssetReplacer(combination, npcInfo.NPC, outputMod);
            }
        }

        public static void AssignNonHardCodedTextures(NPCInfo npcInfo, List<FilePathReplacementParsed> nonHardcodedPaths, ImmutableLoadOrderLinkCache<ISkyrimMod, ISkyrimModGetter> recordTemplateLinkCache, SkyrimMod outputMod, int longestPath, bool assignFromTemplate, bool suppressMissingPathErrors)
        { 
            HashSet<IMajorRecord> assignedRecords = new HashSet<IMajorRecord>();

            Dictionary<string, dynamic> recordsAtPaths = new Dictionary<string, dynamic>(); // quickly look up record templates rather than redoing reflection work

            Dictionary<dynamic, Dictionary<string, dynamic>> objectLinkMap = new Dictionary<dynamic, Dictionary<string, dynamic>>();

            Dictionary<string, dynamic> objectsAtPath_NPC = new Dictionary<string, dynamic>();

            var currentNPC = outputMod.Npcs.GetOrAddAsOverride(npcInfo.NPC);

            objectsAtPath_NPC.Add("", currentNPC);
            objectLinkMap.Add(currentNPC, objectsAtPath_NPC);

            foreach (var template in nonHardcodedPaths.Select(x => x.TemplateNPC).ToHashSet())
            {
                Dictionary<string, dynamic> objectsAtPath_Template = new Dictionary<string, dynamic>();
                objectsAtPath_Template.Add("", template);

                if (template != null)
                {
                    objectLinkMap.Add(template, objectsAtPath_Template);
                }
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
                        { 
                            if (!recordFormKey.Value.IsNull)
                            {
                                ILoquiRegistration register = null;
                                Type recordType = null;

                                if (TryGetRegister(currentObj, out Type loquiType))
                                {
                                    register = LoquiRegistration.GetRegister(loquiType);
                                    recordType = register.GetterType;
                                }
                                else
                                {
                                    Logger.LogError("Could not determine record type for object of type " + currentObj.GetType().Name + ": " + Logger.GetNPCLogNameString(currentNPC) + " at path: " + group.Key + ". This subrecord will not be assigned.");
                                    RemoveInvalidPaths(nonHardcodedPaths, group);
                                    continue;
                                }

                                //if the current object is an existing record, resolve it so that it can be traversed
                                if (Patcher.MainLinkCache.TryResolve(recordFormKey.Value, recordType, out var currentMajorRecordGetter)) 
                                {
                                    if (!templateDerivedRecords.Contains(currentMajorRecordGetter)) // make a copy of the record that the NPC currently has at this position, unless this record was set from a record template during a previous iteration, in which case it does not need to be copied.
                                    {
                                        dynamic recordGroup = GetPatchRecordGroup(currentMajorRecordGetter, outputMod);

                                        IMajorRecord copiedRecord = (IMajorRecord)IGroupMixIns.DuplicateInAsNewRecord(recordGroup, currentMajorRecordGetter);
                                        if (copiedRecord == null)
                                        {
                                            Logger.LogError("Could not deep copy a record for NPC " + Logger.GetNPCLogNameString(currentNPC) + " at path: " + group.Key + ". This subrecord will not be assigned.");
                                            RemoveInvalidPaths(nonHardcodedPaths, group);
                                            continue;
                                        }

                                        copiedRecord.EditorID += "_" + npcInfo.NPC.EditorID;

                                        SetViaFormKeyReplacement(currentObj, copiedRecord, rootObj, currentSubPath, indexIfInArray, outputMod);
                                        currentObj = copiedRecord;
                                    } 
                                }
                                else if (recordTemplateLinkCache.TryResolve(recordFormKey.Value, recordType, out currentMajorRecordGetter)) // note: current object can belong to a record template if it is an attribute of a class that was copied from a record template (since copying the struct doesn't deep copy the contained formlnks)
                                {
                                    string pathSignature = string.Concat(group.Select(x => x.Source));
                                    if (!TraverseRecordFromTemplate(rootObj, currentSubPath, indexIfInArray, currentObj, recordTemplateLinkCache, nonHardcodedPaths, group, assignedRecords, recordsAtPaths, pathSignature, outputMod, out currentObj))
                                    {
                                        continue;
                                    };
                                }
                            }
                            else
                            {
                                npcHasNullFormLink = true;
                            }
                        }
                    }

                    // if the NPC doesn't have the given object (e.g. the NPC doesn't have a WNAM), assign in from template
                    if (assignFromTemplate && (!npcHasObject || npcHasNullFormLink) && GetObjectFromAvailableTemplates(group.Key, group.ToArray(), objectLinkMap, recordTemplateLinkCache, suppressMissingPathErrors, out currentObj, out indexIfInArray)) // get corresponding object from template NPC
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
                                if (!TraverseRecordFromTemplate(rootObj, currentSubPath, indexIfInArray, currentObj, recordTemplateLinkCache, nonHardcodedPaths, group, assignedRecords, recordsAtPaths, pathSignature, outputMod, out currentObj))
                                {
                                    continue;
                                }
                            }
                            else
                            {
                                Logger.LogError("Record template error: Could not obtain a non-null FormKey for template NPCs " + string.Join(", ", group.Select(x => x.TemplateNPC).Select(x => x.EditorID)) + " at path: " + group.Key + ". This subrecord will not be assigned.");
                                RemoveInvalidPaths(nonHardcodedPaths, group);
                            }
                        }
                        else
                        {
                            RecordPathParser.SetSubObject(rootObj, currentSubPath, currentObj);
                        }
                    }

                    if (!npcHasObject && !templateHasObject && assignFromTemplate)
                    {
                        Logger.LogError("Error: neither NPC " + npcInfo.LogIDstring + " nor the record templates " + string.Join(", ", group.Select(x => x.TemplateNPC).Select(x => x.EditorID)) + " contained a record at " + group.Key + ". Cannot assign this record.");
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
                    else if (objectsAtPath_NPC.ContainsKey(group.Key))
                    {
                        objectsAtPath_NPC[group.Key] = currentObj; // RecordPathParser.GetObjectAtPath() populates this with the Getter. Update it here with the newly generated Setter
                    }
                    else if (!objectsAtPath_NPC.ContainsKey(group.Key)) // this condition evaluates true only when the current subpath is a top-level subpath (e.g. npc.x rather than npc.x.y) because GetObjectAtPath() will populate the first subpath of the root object, which in this case is the NPC
                    {
                        objectsAtPath_NPC.Add(group.Key, currentObj); // for next iteration of top for loop
                    }
                }
            }
        }

        public static bool TraverseRecordFromTemplate(dynamic rootObj, string currentSubPath, int? indexIfInArray, dynamic recordToCopy, ImmutableLoadOrderLinkCache<ISkyrimMod, ISkyrimModGetter> recordTemplateLinkCache, List<FilePathReplacementParsed> allPaths, IGrouping<string, FilePathReplacementParsed> group, HashSet<IMajorRecord> templateDerivedRecords, Dictionary<string, dynamic> recordsAtPaths, string pathSignature, SkyrimMod outputMod, out dynamic currentObj)
        {
            IMajorRecord newRecord = null;
            HashSet<IMajorRecord> copiedRecords = new HashSet<IMajorRecord>(); // includes current record and its subrecords

            if (ObjectIsFormLink(recordToCopy, out bool _))
            {
                IMajorRecordGetter linkedMajorRecordGetter = null;
                if (TryGetRegister(recordToCopy, out Type recordType) && recordTemplateLinkCache.TryResolve(recordToCopy.FormKey, recordType, out linkedMajorRecordGetter))
                {
                    newRecord = DeepCopyRecordToPatch(linkedMajorRecordGetter, recordToCopy.FormKey.ModKey, recordTemplateLinkCache, outputMod, copiedRecords);
                }
            }
            else
            {
                newRecord = DeepCopyRecordToPatch((IMajorRecordGetter)recordToCopy, recordToCopy.FormKey.ModKey, recordTemplateLinkCache, outputMod, copiedRecords);
            }

            if (newRecord == null)
            {
                Logger.LogError("Record template error: Could not obtain a subrecord from any template NPCs " + string.Join(", ", group.Select(x => x.TemplateNPC).Select(x => x.EditorID)) + " at path: " + group.Key + ". This subrecord will not be assigned.");
                RemoveInvalidPaths(allPaths, group);
                currentObj = recordToCopy;
                return false;
            }

            templateDerivedRecords.UnionWith(copiedRecords);
            IncrementEditorID(copiedRecords);

            SetViaFormKeyReplacement(recordToCopy, newRecord, rootObj, currentSubPath, indexIfInArray, outputMod);

            currentObj = newRecord;

            recordsAtPaths.Add(pathSignature, newRecord); // store paths associated with this record for future lookup to avoid having to repeat the reflection for other NPCs who get the same combination and need to be assigned the same record
            return true;
        }

        public static dynamic GetObjectFromAvailableTemplates(string currentSubPath, FilePathReplacementParsed[] allPaths, Dictionary<dynamic, Dictionary<string, dynamic>> objectLinkMap, ImmutableLoadOrderLinkCache<ISkyrimMod, ISkyrimModGetter> recordTemplateLinkCache, bool suppressMissingPathErrors, out dynamic outputObj, out int? indexIfInArray)
        {
            foreach (var templateNPC in allPaths.Select(x => x.TemplateNPC).ToHashSet())
            {
                if (RecordPathParser.GetObjectAtPath(templateNPC, currentSubPath, objectLinkMap, recordTemplateLinkCache, suppressMissingPathErrors, out outputObj, out indexIfInArray))
                {
                    return true;
                }
            }

            outputObj = null;
            indexIfInArray = null;
            return false;
        }
        
        public static void RemoveInvalidPaths(List<FilePathReplacementParsed> allPaths, IGrouping<string, FilePathReplacementParsed> toRemove)
        {
            foreach (var path in toRemove)
            {
                allPaths.Remove(path);
            }
        }

        private static void SetViaFormKeyReplacement(dynamic toReplace, IMajorRecord replaceWith, dynamic root, string currentSubPath, int? arrayIndex, SkyrimMod outputMod)
        {
            if (RecordPathParser.PathIsArray(currentSubPath))
            {
                SetRecordInArray(root, arrayIndex.Value, replaceWith);
            }
            else if (RecordPathParser.GetSubObject(root, currentSubPath, out dynamic formLinkToSet))
            {
                formLinkToSet.SetTo(replaceWith.FormKey);
            }
        }

        private static bool TryGetRegister(dynamic currentObject, out Type registerType)
        {
            Type objType = currentObject.GetType();
            if (LoquiRegistration.IsLoquiType(objType))
            {
                registerType = objType;
                return true;
            }
            else if (objType.Name == "FormLink`1")
            {
                var formLink = currentObject as IFormLinkGetter;
                if (LoquiRegistration.IsLoquiType(formLink.Type))
                {
                    registerType = formLink.Type;
                    return true;
                }
            }
            registerType = null;
            return false;
        }

        private static bool ObjectIsFormLink(dynamic currentObject, out bool nullable)
        {
            nullable = false;
            Type objType = currentObject.GetType();
            if (objType.Name == "FormLink`1")
            {
                return true;
            }
            else if (objType.Name == "FormLinkNullable`1")
            {
                nullable = true;
                return true;
            }
            return false;
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
                //Logger.LogReport("Could not set record " + settableRecord.EditorID + " at " + propertyName, true, npcInfo);
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
                //Logger.LogReport("Could not set record at " + propertyName, true, npcInfo);
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

        public static IMajorRecord AssignHeadTexture(NPCInfo npcInfo, SkyrimMod outputMod, ILinkCache<ISkyrimMod, ISkyrimModGetter> mainLinkCache, ILinkCache<ISkyrimMod, ISkyrimModGetter> templateLinkCache, HashSet<FilePathReplacementParsed> paths)
        {
            TextureSet headTex = null;
            bool assignedFromDictionary = false;
            var pathSignature = paths.Select(x => x.Source).ToHashSet();

            INpcGetter templateNPC = paths.Where(x => HeadTexturePaths.Contains(x.DestinationStr)).First().TemplateNPC ?? null;

            if (npcInfo.NPC.HeadTexture != null && !npcInfo.NPC.HeadTexture.IsNull && TryGetGeneratedRecord(pathSignature, npcInfo.NPC.HeadTexture.FormKey, out headTex))
            {
                assignedFromDictionary = true;
            }
            else if (npcInfo.NPC.HeadTexture != null && !npcInfo.NPC.HeadTexture.IsNull && mainLinkCache.TryResolve<ITextureSetGetter>(npcInfo.NPC.HeadTexture.FormKey, out var existingHeadTexture))
            {
                headTex = outputMod.TextureSets.AddNew();
                headTex.DeepCopyIn(existingHeadTexture);
                AssignEditorID(headTex, npcInfo.NPC, false);
                AddModifiedRecordToDictionary(pathSignature, npcInfo.NPC.HeadTexture.FormKey, headTex);

            }
            else if (TryGetGeneratedRecord(pathSignature, templateNPC, out headTex))
            {
                assignedFromDictionary = true;
            }
            else if (!templateNPC.HeadTexture.IsNull && templateLinkCache.TryResolve<ITextureSetGetter>(templateNPC.HeadTexture.FormKey, out var templateHeadTexture))
            {
                headTex = outputMod.TextureSets.AddNew();
                headTex.DeepCopyIn(templateHeadTexture);
                AssignEditorID(headTex, npcInfo.NPC, true);
                AddGeneratedRecordToDictionary(pathSignature, templateNPC, headTex);
            }
            else
            {
                Logger.LogReport("Could not resolve a head texture from NPC " + Logger.GetNPCLogNameString(npcInfo.NPC) + " or its corresponding record template.", true, npcInfo);
                return null;
            }

            if (!assignedFromDictionary)
            {
                foreach (var path in paths)
                {
                    switch (path.DestinationStr)
                    {
                        case "HeadTexture.Height": headTex.Height = path.Source; break;
                        case "HeadTexture.Diffuse": headTex.Diffuse = path.Source; break;
                        case "HeadTexture.NormalOrGloss": headTex.NormalOrGloss = path.Source; break;
                        case "HeadTexture.GlowOrDetailMap": headTex.GlowOrDetailMap = path.Source; break;
                        case "HeadTexture.BacklightMaskOrSpecular": headTex.BacklightMaskOrSpecular = path.Source; break;
                    }
                }
            }

            var patchedNPC = outputMod.Npcs.GetOrAddAsOverride(npcInfo.NPC);
            patchedNPC.HeadTexture.SetTo(headTex);
            return headTex;
        }

        private static Armor AssignBodyTextures(NPCInfo npcInfo, SkyrimMod outputMod, ILinkCache<ISkyrimMod, ISkyrimModGetter> mainLinkCache, ILinkCache<ISkyrimMod, ISkyrimModGetter> templateLinkCache, HashSet<FilePathReplacementParsed> paths)
        {
            Armor newSkin = null;
            bool assignedFromTemplate = false;

            bool assignedFromDictionary = false;
            var pathSignature = paths.Select(x => x.Source).ToHashSet();

            INpcGetter templateNPC = paths.Where(x => WornArmorPaths.Contains(x.DestinationStr)).First().TemplateNPC ?? null;

            if (npcInfo.NPC.WornArmor != null && !npcInfo.NPC.WornArmor.IsNull && TryGetGeneratedRecord(pathSignature, npcInfo.NPC.WornArmor.FormKey, out newSkin))
            {
                assignedFromDictionary = true;
            }
            else if (!npcInfo.NPC.WornArmor.IsNull && mainLinkCache.TryResolve<IArmorGetter>(npcInfo.NPC.WornArmor.FormKey, out var existingWNAM))
            {
                newSkin = outputMod.Armors.AddNew();
                newSkin.DeepCopyIn(existingWNAM);
                AssignEditorID(newSkin, npcInfo.NPC, false);
                AddModifiedRecordToDictionary(pathSignature, npcInfo.NPC.WornArmor.FormKey, newSkin);
            }
            else if (TryGetGeneratedRecord(pathSignature, templateNPC, out newSkin))
            {
                assignedFromDictionary = true;
            }
            else if (!templateNPC.WornArmor.IsNull && templateLinkCache.TryResolve<IArmorGetter>(templateNPC.WornArmor.FormKey, out var templateWNAM))
            {
                //newSkin = outputMod.Armors.AddNew(templateWNAM.FormKey); // don't deep copy to avoid mis-naming editorIDs
                newSkin = outputMod.Armors.AddNew();
                newSkin.DeepCopyIn(templateWNAM);
                assignedFromTemplate = true;
                AssignEditorID(newSkin, npcInfo.NPC, true);
                AddGeneratedRecordToDictionary(pathSignature, templateNPC, newSkin);
            }
            else
            {
                Logger.LogReport("Could not resolve a body texture from NPC " + npcInfo.LogIDstring + " or its corresponding record template.", true, npcInfo);
                outputMod.Armors.Remove(newSkin);
                return null;
            }

            if (!assignedFromDictionary)
            {
                var torsoArmorAddonPaths = paths.Where(x => TorsoArmorAddonPaths.Contains(x.DestinationStr)).ToHashSet();
                var handsArmorAddonPaths = paths.Where(x => HandsArmorAddonPaths.Contains(x.DestinationStr)).ToHashSet();
                var feetArmorAddonPaths = paths.Where(x => FeetArmorAddonPaths.Contains(x.DestinationStr)).ToHashSet();
                var tailArmorAddonPaths = paths.Where(x => TailArmorAddonPaths.Contains(x.DestinationStr)).ToHashSet();
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
                    var assignedTorso = AssignArmorAddon(newSkin, npcInfo, outputMod, mainLinkCache, templateLinkCache, torsoArmorAddonPaths, ArmorAddonType.Torso, allowedRaces, assignedFromTemplate);
                }
                if (handsArmorAddonPaths.Any())
                {
                    var assignedHands = AssignArmorAddon(newSkin, npcInfo, outputMod, mainLinkCache, templateLinkCache, handsArmorAddonPaths, ArmorAddonType.Hands, allowedRaces, assignedFromTemplate);
                }
                if (feetArmorAddonPaths.Any())
                {
                    var assignedFeet = AssignArmorAddon(newSkin, npcInfo, outputMod, mainLinkCache, templateLinkCache, feetArmorAddonPaths, ArmorAddonType.Feet, allowedRaces, assignedFromTemplate);
                }
                if (tailArmorAddonPaths.Any())
                {
                    var assignedTail = AssignArmorAddon(newSkin, npcInfo, outputMod, mainLinkCache, templateLinkCache, tailArmorAddonPaths, ArmorAddonType.Tail, allowedRaces, assignedFromTemplate);
                }
            }
            else // if record is one that has previously been generated, update any SynthEBD-generated armature to ensure that the current NPC's race is present within the Additional Races collection.
            {
                foreach (var armatureLink in newSkin.Armature)
                {
                    if (Patcher.MainLinkCache.TryResolve<IArmorAddonGetter>(armatureLink.FormKey, out var armaGetter) && outputMod.ArmorAddons.ContainsKey(armatureLink.FormKey) && !armaGetter.AdditionalRaces.Select(x => x.FormKey.ToString()).Contains(npcInfo.NPC.Race.FormKey.ToString())) // 
                    {
                        var armature = outputMod.ArmorAddons.GetOrAddAsOverride(armaGetter);
                        armature.AdditionalRaces.Add(npcInfo.NPC.Race);
                    }
                }
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

        private static ArmorAddon AssignArmorAddon(Armor parentArmorRecord, NPCInfo npcInfo, SkyrimMod outputMod, ILinkCache<ISkyrimMod, ISkyrimModGetter> mainLinkCache, ILinkCache<ISkyrimMod, ISkyrimModGetter> templateLinkCache, HashSet<FilePathReplacementParsed> paths, ArmorAddonType type, HashSet<string> currentRaceIDstrs, bool parentAssignedFromTemplate)
        {
            ArmorAddon newArmorAddon = null;
            IArmorAddonGetter templateAA;
            HashSet<IArmorAddonGetter> candidateAAs = new HashSet<IArmorAddonGetter>();
            bool replaceExistingArmature = false;

            var pathSignature = paths.Select(x => x.Source).ToHashSet();

            INpcGetter templateNPC = paths.Where(x => WornArmorPaths.Contains(x.DestinationStr)).First().TemplateNPC ?? null;

            // try to get the needed armor addon template record from the existing parent armor record
            candidateAAs = GetAvailableArmature(parentArmorRecord, mainLinkCache, templateLinkCache, !parentAssignedFromTemplate, parentAssignedFromTemplate);
            templateAA = ChooseArmature(candidateAAs, type, currentRaceIDstrs);

            if (TryGetGeneratedRecord(pathSignature, templateAA.FormKey, out newArmorAddon))
            {
                if (templateAA != null)
                { 
                    replaceExistingArmature = true;
                }
            }
            else if (templateAA != null)
            {
                newArmorAddon = outputMod.ArmorAddons.AddNew();
                newArmorAddon.DeepCopyIn(templateAA);
                var assignedSkinTexture = AssignSkinTexture(newArmorAddon, parentAssignedFromTemplate, npcInfo, outputMod, mainLinkCache, templateLinkCache, paths);
                replaceExistingArmature = true;

                AssignEditorID(newArmorAddon, npcInfo.NPC, parentAssignedFromTemplate);
                AddModifiedRecordToDictionary(pathSignature, templateAA.FormKey, newArmorAddon);
            }

            // try to get the needed armor record from the corresponding record template
            else if (!templateNPC.WornArmor.IsNull && templateNPC.WornArmor.TryResolve(templateLinkCache, out var templateArmorGetter))
            {
                candidateAAs = GetAvailableArmature(templateArmorGetter, mainLinkCache, templateLinkCache, false, true);
                templateAA = ChooseArmature(candidateAAs, type, currentRaceIDstrs);

                if (TryGetGeneratedRecord(pathSignature, templateNPC, out newArmorAddon))
                {
                    if (templateAA != null)
                    {
                        replaceExistingArmature = true;
                    }
                }
                else if (templateAA != null)
                {
                    //newArmorAddon = outputMod.ArmorAddons.AddNew(templateAA.FormKey); // don't deep copy to avoid misnaming downstream editorIDs
                    newArmorAddon = outputMod.ArmorAddons.AddNew();
                    newArmorAddon.DeepCopyIn(templateAA);
                    AssignEditorID(newArmorAddon, npcInfo.NPC, true);
                    AddGeneratedRecordToDictionary(pathSignature, templateNPC, newArmorAddon);

                    var assignedSkinTexture = AssignSkinTexture(newArmorAddon, true, npcInfo, outputMod, mainLinkCache, templateLinkCache, paths);
                }
            }

            if (templateAA == null)
            {
                Logger.LogReport("Could not resolve " + type.ToString() + " armature for NPC " + npcInfo.LogIDstring + " or its template.", true, npcInfo);
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

        private static TextureSet AssignSkinTexture(ArmorAddon parentArmorAddonRecord, bool parentAssignedFromTemplate, NPCInfo npcInfo, SkyrimMod outputMod, ILinkCache<ISkyrimMod, ISkyrimModGetter> mainLinkCache, ILinkCache<ISkyrimMod, ISkyrimModGetter> templateLinkCache, HashSet<FilePathReplacementParsed> paths)
        {
            INpcGetter templateNPC = paths.Where(x => WornArmorPaths.Contains(x.DestinationStr)).First().TemplateNPC ?? null;

            IFormLinkNullableGetter<ITextureSetGetter> parentSkinTexture = null;
            switch (npcInfo.Gender) 
            {
                case Gender.Male: parentSkinTexture = parentArmorAddonRecord.SkinTexture.Male; break;
                case Gender.Female: parentSkinTexture = parentArmorAddonRecord.SkinTexture.Female; break;
            }


            TextureSet newSkinTexture = null;
            bool assignedFromDictionary = false;
            var pathSignature = paths.Select(x => x.Source).ToHashSet();

            if (parentSkinTexture != null && !parentSkinTexture.IsNull && TryGetGeneratedRecord(pathSignature, npcInfo.NPC.HeadTexture.FormKey, out newSkinTexture))
            {
                assignedFromDictionary = true;
            }
            else if (parentSkinTexture != null && !parentSkinTexture.IsNull && mainLinkCache.TryResolve<ITextureSetGetter>(parentSkinTexture.FormKey, out var existingSkinTexture))
            {
                newSkinTexture = outputMod.TextureSets.AddNew();
                newSkinTexture.DeepCopyIn(existingSkinTexture);
                AssignEditorID(newSkinTexture, npcInfo.NPC, parentAssignedFromTemplate);
                AddModifiedRecordToDictionary(pathSignature, npcInfo.NPC.HeadTexture.FormKey, newSkinTexture);

            }
            else if (TryGetGeneratedRecord(pathSignature, templateNPC, out newSkinTexture))
            {
                assignedFromDictionary = true;
            }
            else if (!templateNPC.HeadTexture.IsNull && templateLinkCache.TryResolve<ITextureSetGetter>(templateNPC.HeadTexture.FormKey, out var templateHeadTexture))
            {
                newSkinTexture = outputMod.TextureSets.AddNew();
                newSkinTexture.DeepCopyIn(templateHeadTexture);
                AssignEditorID(newSkinTexture, npcInfo.NPC, true);
                AddGeneratedRecordToDictionary(pathSignature, templateNPC, newSkinTexture);
            }
            else
            {
                Logger.LogReport("Could not resolve a skin texture from NPC " + Logger.GetNPCLogNameString(npcInfo.NPC) + " or its corresponding record template.", true, npcInfo);
                return null;
            }

            if (!assignedFromDictionary)
            {
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
            }

            switch (npcInfo.Gender)
            {
                case Gender.Male: parentArmorAddonRecord.SkinTexture.Male = newSkinTexture.AsNullableLinkGetter(); break;
                case Gender.Female: parentArmorAddonRecord.SkinTexture.Female = newSkinTexture.AsNullableLinkGetter(); break;
            }

            return newSkinTexture;
        }

        public static void AssignEditorID(IMajorRecord record, INpcGetter npc, bool copiedFromTemplate)
        {
            if (copiedFromTemplate)
            {
                IncrementEditorID(new HashSet<IMajorRecord>() { record });
            }
            else
            {
                record.EditorID += "_Patched";

                string fkStr = record.FormKey.ToString();
                if (ModifiedRecordCounts.ContainsKey(fkStr))
                {
                    ModifiedRecordCounts[fkStr]++;
                }
                else
                {
                    ModifiedRecordCounts.Add(fkStr, 1);
                }

                record.EditorID += ModifiedRecordCounts[fkStr].ToString("D4");
            }
        }

        public static void IncrementEditorID(HashSet<IMajorRecord> records)
        {
            foreach (var newRecord in records)
            {
                if (Patcher.EdidCounts.ContainsKey(newRecord.EditorID))
                {
                    Patcher.EdidCounts[newRecord.EditorID]++;
                    newRecord.EditorID += Patcher.EdidCounts[newRecord.EditorID].ToString("D4"); // pad with leading zeroes https://stackoverflow.com/questions/4325267/c-sharp-convert-int-to-string-with-padding-zeros
                }
                else
                {
                    Patcher.EdidCounts.Add(newRecord.EditorID, 1);
                    newRecord.EditorID += 1.ToString("D4");
                }
            }
        }

        private static Dictionary<string, int> ModifiedRecordCounts = new Dictionary<string, int>(); // for modified Editor IDs only

        private static HashSet<IArmorAddonGetter> GetAvailableArmature(IArmorGetter parentArmor, ILinkCache mainLinkCache, ILinkCache templateLinkCache, bool checkMainLinkCache, bool checkTemplateLinkCache)
        {
            HashSet<IArmorAddonGetter> candidateAAs = new HashSet<IArmorAddonGetter>();
            //foreach (var aa in parentArmor.Armature.Where(x => Patcher.IgnoredArmorAddons.Contains(x.FormKey.AsLinkGetter<IArmorAddonGetter>()) == false))
            foreach (var aa in parentArmor.Armature)
            {
                if (checkMainLinkCache && aa.TryResolve(mainLinkCache, out var candidateAA))
                {
                    candidateAAs.Add(candidateAA);
                }
                else if (checkTemplateLinkCache && aa.TryResolve(templateLinkCache, out var candidateAAfromTemplate))
                {
                    candidateAAs.Add(candidateAAfromTemplate);
                }
            }
            return candidateAAs;
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
            if (filteredFlags == null || !filteredFlags.Any()) { return null; }
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

        //Dictionary[SourcePaths.ToHashSet()][OriginalRecordGetter.FormKey.ToString()] = IMajorRecord Generated
        private static Dictionary<HashSet<string>, Dictionary<string, IMajorRecord>> ModifiedRecords = new Dictionary<HashSet<string>, Dictionary<string, IMajorRecord>>(HashSet<string>.CreateSetComparer()); // https://stackoverflow.com/questions/5910137/how-do-i-use-hashsett-as-a-dictionary-key

        private static bool TryGetGeneratedRecord<T>(HashSet<string> pathSignature, FormKey originalFormKey, out T record) where T : class
        {
            string fkStr = originalFormKey.ToString();

            if (ModifiedRecords.ContainsKey(pathSignature) && ModifiedRecords[pathSignature].ContainsKey(fkStr))
            {
                record = ModifiedRecords[pathSignature][fkStr] as T;
                return record != null;
            }
            else
            {
                record = null;
                return false;
            }
        }

        private static void AddModifiedRecordToDictionary(HashSet<string> pathSignature, FormKey originalFormKey, IMajorRecord record)
        {
            string fkStr = originalFormKey.ToString();

            if (!ModifiedRecords.ContainsKey(pathSignature))
            {
                ModifiedRecords.Add(pathSignature, new Dictionary<string, IMajorRecord>());
            }

            if (!ModifiedRecords[pathSignature].ContainsKey(fkStr))
            {
                ModifiedRecords[pathSignature].Add(fkStr, null);
            }

            ModifiedRecords[pathSignature][fkStr] = record;
        }

        //Dictionary[SourcePaths.ToHashSet()][RecordTemplate.FormKey.ToString()] = IMajorRecord Generated
        private static Dictionary<HashSet<string>, Dictionary<string, IMajorRecord>> GeneratedRecordsByTempateNPC = new Dictionary<HashSet<string>, Dictionary<string, IMajorRecord>>(HashSet<string>.CreateSetComparer()); // https://stackoverflow.com/questions/5910137/how-do-i-use-hashsett-as-a-dictionary-key

        private static bool TryGetGeneratedRecord<T>(HashSet<string> pathSignature, INpcGetter template, out T record) where T : class
        {
            if (GeneratedRecordsByTempateNPC.ContainsKey(pathSignature) && GeneratedRecordsByTempateNPC[pathSignature].ContainsKey(template.FormKey.ToString()))
            {
                record = GeneratedRecordsByTempateNPC[pathSignature][template.FormKey.ToString()] as T;
                return record != null;
            }
            else
            {
                record = null;
                return false;
            }
        }

        private static void AddGeneratedRecordToDictionary(HashSet<string> pathSignature, INpcGetter template, IMajorRecord record)
        {
            var templateFKstring = template.FormKey.ToString();
            if (!GeneratedRecordsByTempateNPC.ContainsKey(pathSignature))
            {
                GeneratedRecordsByTempateNPC.Add(pathSignature, new Dictionary<string, IMajorRecord>());
            }

            if (!GeneratedRecordsByTempateNPC[pathSignature].ContainsKey(templateFKstring))
            {
                GeneratedRecordsByTempateNPC[pathSignature].Add(templateFKstring, null);
            }

            GeneratedRecordsByTempateNPC[pathSignature][templateFKstring] = record;
        }
    }
}
