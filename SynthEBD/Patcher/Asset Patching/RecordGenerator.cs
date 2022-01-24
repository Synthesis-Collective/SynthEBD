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
        public static void CombinationToRecords(List<SubgroupCombination> combinations, NPCInfo npcInfo, ILinkCache<ISkyrimMod, ISkyrimModGetter> recordTemplateLinkCache, Dictionary<string, dynamic> npcObjectMap, Dictionary<FormKey, Dictionary<string, dynamic>> objectCaches, SkyrimMod outputMod)
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

                        /* Original hardcoded path sorting
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

                        // new hardcoded path sorting
                        /*
                        if (path.Destination.StartsWith("WornArmor")) { wnamPaths.Add(parsed); }
                        else if (path.Destination.StartsWith("HeadTexture")) { headtexPaths.Add(parsed); }
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

            var currentNPC = outputMod.Npcs.GetOrAddAsOverride(npcInfo.NPC);
            objectCaches.Add(npcInfo.NPC.FormKey, new Dictionary<string, dynamic>(StringComparer.OrdinalIgnoreCase) { { "", currentNPC } });

            if (headtexPaths.Any())
            {
                AssignHeadTexture(npcInfo, outputMod, Patcher.MainLinkCache, recordTemplateLinkCache, headtexPaths, npcObjectMap, objectCaches);
            }
            if (wnamPaths.Any())
            {
                AssignBodyTextures(npcInfo, outputMod, Patcher.MainLinkCache, recordTemplateLinkCache, wnamPaths, npcObjectMap, objectCaches);
            }
            if (nonHardcodedPaths.Any())
            {
                AssignGenericAssetPaths(npcInfo, nonHardcodedPaths, currentNPC, recordTemplateLinkCache, outputMod, longestPath, true, false, npcObjectMap, objectCaches);
            }
        }

        public static void ReplacerCombinationToRecords(SubgroupCombination combination, NPCInfo npcInfo, SkyrimMod outputMod, ILinkCache<ISkyrimMod, ISkyrimModGetter> recordTemplateLinkCache, Dictionary<string, dynamic> npcObjectMap, Dictionary<FormKey, Dictionary<string, dynamic>> objectCaches)
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
                    var currentNPC = outputMod.Npcs.GetOrAddAsOverride(npcInfo.NPC);
                    AssignGenericAssetPaths(npcInfo, nonHardcodedPaths, currentNPC, null, outputMod, longestPath, false, true, npcObjectMap, objectCaches);
                }
            }
            else if (combination.DestinationType != SubgroupCombination.DestinationSpecifier.Main)
            {
                AssignSpecialCaseAssetReplacer(combination, npcInfo.NPC, outputMod);
            }
        }

        public static int GetLongestPath(IEnumerable<FilePathReplacementParsed> paths)
        {
            int longestPath = 0;
            foreach (var path in paths)
            {
                if (path.Destination.Length > longestPath)
                {
                    longestPath = path.Destination.Length;
                }
            }
            return longestPath;
        }

        private class TemplateSignatureRecordPair
        {
            public HashSet<INpcGetter> TemplateSignature { get; set; }
            public IMajorRecord SubRecord { get; set; }
        }

        public static void AssignGenericAssetPaths(NPCInfo npcInfo, List<FilePathReplacementParsed> nonHardcodedPaths, Npc rootNPC, ILinkCache<ISkyrimMod, ISkyrimModGetter> recordTemplateLinkCache, SkyrimMod outputMod, int longestPath, bool assignFromTemplate, bool suppressMissingPathErrors, Dictionary<string, dynamic> npcObjectMap, Dictionary<FormKey, Dictionary<string, dynamic>> objectCaches)
        { 
            HashSet<TemplateSignatureRecordPair> templateSubRecords = new HashSet<TemplateSignatureRecordPair>();

            dynamic currentObj = null;

            for (int i = 0; i < longestPath; i++)
            {
                #region Remove paths that were already assigned
                for (int j = 0; j < nonHardcodedPaths.Count; j++)
                {
                    if (i == nonHardcodedPaths[j].Destination.Length) 
                    {
                        nonHardcodedPaths.RemoveAt(j);
                        j--;
                    }
                }
                #endregion

                var groupedPathsAtI = nonHardcodedPaths.GroupBy(x => BuildPath(x.Destination.ToList().GetRange(0, i + 1))); // group paths by the current path segment

                foreach (var group in groupedPathsAtI)
                {
                    string parentPath = BuildPath(group.First().Destination.ToList().GetRange(0, i));
                    string currentSubPath = group.First().Destination[i];
                    var rootObj = npcObjectMap[parentPath];
                    var pathSignature = group.Select(x => x.Source).ToHashSet();

                    // step through the path
                    bool skipObjectMapAssignment = false;
                    bool npcSetterHasObject = npcObjectMap.ContainsKey(group.Key);
                    ObjectInfo currentObjInfo = null;

                    if (group.First().Destination.Length == i + 1) // if this is the last part of the path, attempt to assign the Source asset to the Destination
                    {
                        foreach (var assetAssignment in group)
                        {
                            RecordPathParser.SetSubObject(rootObj, currentSubPath, assetAssignment.Source);
                            currentObj = assetAssignment.Source;
                        }
                        skipObjectMapAssignment = true;
                    }
                    else if (npcSetterHasObject) // if the current subpath has already been added to the given NPC record, 
                    {
                        currentObj = npcObjectMap[group.Key];
                    }
                    else if (RecordPathParser.GetObjectAtPath(rootNPC, group.Key, npcObjectMap, Patcher.MainLinkCache, suppressMissingPathErrors, Logger.GetNPCLogNameString(npcInfo.NPC) + "(Generated Override)", out currentObj, out currentObjInfo) && !currentObjInfo.IsNullFormLink) // if the current object is a sub-object of a template-derived record, it will not yet have been added to npcObjectMap in a previous iteration (note that it is added during this GetObjectAtPath() call so no need to add it again)
                    {
                        npcSetterHasObject = true;
                        if (currentObjInfo.HasFormKey) 
                        {
                            if (currentObjInfo.RecordFormKey.ModKey.Equals(outputMod.ModKey)) // This is a subrecord of a template-derived deep copied record. Now that the path signature of the given template-derived subrecord is known, cache it
                            {
                                var generatedSubRecord = templateSubRecords.Where(x => x.SubRecord == currentObj).FirstOrDefault();
                                if (generatedSubRecord != null)
                                {
                                    AddGeneratedObjectToDictionary(pathSignature, group.Key, generatedSubRecord.TemplateSignature, currentObj, currentObjInfo.IndexInParentArray);
                                    templateSubRecords.Remove(generatedSubRecord);
                                }
                            }
                            else if(!TraverseRecordFromNpc(currentObj, currentObjInfo, pathSignature, group, rootObj, currentSubPath, npcInfo, nonHardcodedPaths, outputMod, out currentObj))
                            {
                                continue;
                            }
                        }
                    }
                    else if (RecordPathParser.GetObjectAtPath(npcInfo.NPC, group.Key, objectCaches[npcInfo.NPC.FormKey], Patcher.MainLinkCache, suppressMissingPathErrors, Logger.GetNPCLogNameString(npcInfo.NPC), out currentObj, out currentObjInfo) && !currentObjInfo.IsNullFormLink)
                    {
                        if (currentObjInfo.HasFormKey)  // if the current object is a record, resolve it
                        {
                            if (!TraverseRecordFromNpc(currentObj, currentObjInfo, pathSignature, group, rootObj, currentSubPath, npcInfo, nonHardcodedPaths, outputMod, out currentObj))
                            {
                                continue;
                            }
                        }
                        else // if the current object is not a record, copy it directly
                        {
                            RecordPathParser.SetSubObject(rootObj, currentSubPath, currentObj);
                        }
                    }
                    else if (assignFromTemplate) // get corresponding object from template NPC
                    {
                        var templateSignature = group.Select(x => x.TemplateNPC).ToHashSet();
                        if (TryGetGeneratedObject(pathSignature, group.Key, templateSignature, out currentObj, out int? indexIfInArray))
                        {
                            if (RecordPathParser.ObjectHasFormKey(currentObj, out FormKey? _))
                            {
                                SetViaFormKeyReplacement(currentObj, rootObj, currentSubPath, indexIfInArray, outputMod);
                            }
                            else
                            {
                                RecordPathParser.SetSubObject(rootObj, currentSubPath, currentObj);
                            }

                            RemovePathsFromList(nonHardcodedPaths, group); // remove because everything downstream has already been assigned
                        }
                        else if (GetObjectFromAvailableTemplates(group.Key, group.ToArray(), objectCaches, recordTemplateLinkCache, suppressMissingPathErrors, out currentObj, out currentObjInfo))
                        {
                            if (currentObjInfo.HasFormKey)
                            {
                                if (!TraverseRecordFromTemplate(rootObj, currentSubPath, currentObj, currentObjInfo, recordTemplateLinkCache, nonHardcodedPaths, group, templateSignature, templateSubRecords, outputMod, out currentObj))
                                {
                                    continue;
                                }
                            }
                            else if (!currentObjInfo.IsNullFormLink)
                            {
                                RecordPathParser.SetSubObject(rootObj, currentSubPath, currentObj);
                            }

                            AddGeneratedObjectToDictionary(pathSignature, group.Key, templateSignature, currentObj, currentObjInfo.IndexInParentArray);
                        }
                    }
                    else
                    {
                        Logger.LogError("Error: neither NPC " + npcInfo.LogIDstring + " nor the record templates " + string.Join(", ", group.Select(x => x.TemplateNPC).Select(x => x.EditorID)) + " contained a record at " + group.Key + ". Cannot assign this record.");
                        RemovePathsFromList(nonHardcodedPaths, group);
                    }

                    if (!skipObjectMapAssignment)
                    {
                        switch(npcSetterHasObject)
                        {
                            case false: npcObjectMap.Add(group.Key, currentObj); break;
                            case true: npcObjectMap[group.Key] = currentObj; break;
                        }
                    }
                }
            }
        }

        private static bool TraverseRecordFromNpc(dynamic currentObj, ObjectInfo currentObjInfo, HashSet<string> pathSignature, IGrouping<string, FilePathReplacementParsed> group, dynamic rootObj, string currentSubPath, NPCInfo npcInfo, List<FilePathReplacementParsed> allPaths, SkyrimMod outputMod, out dynamic outputObj)
        {
            outputObj = currentObj;
            IMajorRecord copiedRecord = null;
            if (!TryGetGeneratedRecord(pathSignature, currentObjInfo.RecordFormKey, out copiedRecord) && !currentObjInfo.RecordFormKey.IsNull)
            {
                if (currentObjInfo.LoquiRegistration == null)
                {
                    Logger.LogError("Could not determine record type for object of type " + currentObj.GetType().Name + ": " + Logger.GetNPCLogNameString(npcInfo.NPC) + " at path: " + group.Key + ". This subrecord will not be assigned.");
                    RemovePathsFromList(allPaths, group);
                    return false;
                }

                dynamic recordGroup = GetPatchRecordGroup(currentObjInfo.RecordType, outputMod);
                copiedRecord = (IMajorRecord)IGroupMixIns.DuplicateInAsNewRecord(recordGroup, (IMajorRecordGetter)currentObj);
                AssignEditorID(copiedRecord, currentObjInfo.RecordFormKey.ToString(), false);
            }
            if (copiedRecord == null)
            {
                Logger.LogError("Could not deep copy a record for NPC " + Logger.GetNPCLogNameString(npcInfo.NPC) + " at path: " + group.Key + ". This subrecord will not be assigned.");
                RemovePathsFromList(allPaths, group);
                return false;
            }

            SetViaFormKeyReplacement(copiedRecord, rootObj, currentSubPath, currentObjInfo.IndexInParentArray, outputMod);
            AddModifiedRecordToDictionary(pathSignature, currentObjInfo.RecordFormKey, copiedRecord);
            outputObj = copiedRecord;
            return true;
        }

        private static bool TraverseRecordFromTemplate(dynamic rootObj, string currentSubPath, dynamic recordToCopy, ObjectInfo recordObjectInfo, ILinkCache<ISkyrimMod, ISkyrimModGetter> recordTemplateLinkCache, List<FilePathReplacementParsed> allPaths, IGrouping<string, FilePathReplacementParsed> group, HashSet<INpcGetter> templateSignature, HashSet<TemplateSignatureRecordPair> templateDerivedRecords, SkyrimMod outputMod, out dynamic currentObj)
        {
            IMajorRecord newRecord = null;
            HashSet<IMajorRecord> copiedRecords = new HashSet<IMajorRecord>(); // includes current record and its subrecords

            newRecord = DeepCopyRecordToPatch((IMajorRecordGetter)recordToCopy, recordObjectInfo.RecordFormKey.ModKey, recordTemplateLinkCache, outputMod, copiedRecords);

            if (newRecord == null)
            {
                Logger.LogError("Record template error: Could not obtain a subrecord from any template NPCs " + string.Join(", ", group.Select(x => x.TemplateNPC).Select(x => x.EditorID)) + " at path: " + group.Key + ". This subrecord will not be assigned.");
                RemovePathsFromList(allPaths, group);
                currentObj = recordToCopy;
                return false;
            }

            foreach (var record in copiedRecords.Where(x => x != newRecord))
            {
                templateDerivedRecords.Add(new TemplateSignatureRecordPair() { SubRecord = record, TemplateSignature = templateSignature });
            }

            IncrementEditorID(copiedRecords);

            SetViaFormKeyReplacement(newRecord, rootObj, currentSubPath, recordObjectInfo.IndexInParentArray, outputMod);

            currentObj = newRecord;

            return true;
        }

        public static dynamic GetObjectFromAvailableTemplates(string currentSubPath, FilePathReplacementParsed[] allPaths, Dictionary<FormKey, Dictionary<string, dynamic>> objectCaches, ILinkCache<ISkyrimMod, ISkyrimModGetter> recordTemplateLinkCache, bool suppressMissingPathErrors, out dynamic outputObj, out ObjectInfo outputObjInfo)
        {
            foreach (var templateNPC in allPaths.Select(x => x.TemplateNPC).ToHashSet())
            {
                if (!objectCaches.ContainsKey(templateNPC.FormKey))
                {
                    objectCaches.Add(templateNPC.FormKey, new Dictionary<string, dynamic>(StringComparer.OrdinalIgnoreCase));
                }

                if (RecordPathParser.GetObjectAtPath(templateNPC, currentSubPath, objectCaches[templateNPC.FormKey], recordTemplateLinkCache, suppressMissingPathErrors, Logger.GetNPCLogNameString(templateNPC), out outputObj, out outputObjInfo))
                {
                    return true;
                }
            }

            outputObj = null;
            outputObjInfo = null;
            return false;
        }
        
        public static void RemovePathsFromList(List<FilePathReplacementParsed> allPaths, IGrouping<string, FilePathReplacementParsed> toRemove)
        {
            foreach (var path in toRemove)
            {
                allPaths.Remove(path);
            }
        }

        private static void SetViaFormKeyReplacement(IMajorRecord record, dynamic root, string currentSubPath, int? arrayIndex, SkyrimMod outputMod)
        {
            if (RecordPathParser.PathIsArray(currentSubPath))
            {
                SetRecordInArray(root, arrayIndex.Value, record);
            }
            else if (RecordPathParser.GetSubObject(root, currentSubPath, out dynamic formLinkToSet))
            {
                formLinkToSet.SetTo(record.FormKey);
            }
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

        public static IMajorRecord DeepCopyRecordToPatch(dynamic sourceRecordObj, ModKey sourceModKey, ILinkCache<ISkyrimMod, ISkyrimModGetter> sourceLinkCache, SkyrimMod destinationMod, HashSet<IMajorRecord> copiedSubRecords)
        {
            dynamic group = GetPatchRecordGroup(sourceRecordObj, destinationMod);
            IMajorRecord copiedRecord = (IMajorRecord)IGroupMixIns.DuplicateInAsNewRecord(group, sourceRecordObj);
            copiedSubRecords.Add(copiedRecord);

            Dictionary<FormKey, FormKey> mapping = new Dictionary<FormKey, FormKey>();
            foreach (var fl in copiedRecord.ContainedFormLinks)
            {
                if (fl.FormKey.ModKey == sourceModKey && !fl.FormKey.IsNull && sourceLinkCache.TryResolve(fl.FormKey, fl.Type, out var subRecord))
                {
                    var copiedSubRecord = DeepCopyRecordToPatch(subRecord, sourceModKey, sourceLinkCache, destinationMod, copiedSubRecords);
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

        public static IGroup GetPatchRecordGroup(IMajorRecordGetter recordGetter, SkyrimMod outputMod)
        {
            var getterType = LoquiRegistration.GetRegister(recordGetter.GetType()).GetterType;
            return outputMod.GetTopLevelGroup(getterType);
        }

        public static dynamic GetPatchRecordGroup(Type loquiType, SkyrimMod outputMod) // must return dynamic so that the type IGroup<T> is determined at runtime. Returning IGroup causes IGroupMixIns.DuplicateInAsNewRecord() to complain.
        {
            return outputMod.GetTopLevelGroup(loquiType);
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

        public static INpcGetter GetTemplateNPC(NPCInfo npcInfo, FlattenedAssetPack chosenAssetPack, ILinkCache<ISkyrimMod, ISkyrimModGetter> recordTemplateLinkCache)
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

        public static void CacheResolvedObject(string path, dynamic toCache, Dictionary<FormKey, Dictionary<string, dynamic>> objectCaches, INpcGetter npcGetter)
        {
            if (!objectCaches.ContainsKey(npcGetter.FormKey))
            {
                objectCaches.Add(npcGetter.FormKey, new Dictionary<string, dynamic>(StringComparer.OrdinalIgnoreCase));
                objectCaches[npcGetter.FormKey].Add("", npcGetter);
            }

            if(!objectCaches[npcGetter.FormKey].ContainsKey(path))
            {
                objectCaches[npcGetter.FormKey].Add(path, toCache);
            }
        }

        public static IMajorRecord AssignHeadTexture(NPCInfo npcInfo, SkyrimMod outputMod, ILinkCache<ISkyrimMod, ISkyrimModGetter> mainLinkCache, ILinkCache<ISkyrimMod, ISkyrimModGetter> templateLinkCache, HashSet<FilePathReplacementParsed> paths, Dictionary<string, dynamic> npcObjectMap, Dictionary<FormKey, Dictionary<string, dynamic>> objectCaches)
        {
            var patchedNPC = outputMod.Npcs.GetOrAddAsOverride(npcInfo.NPC);

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
                AssignEditorID(headTex, existingHeadTexture.FormKey.ToString(), false);
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
                AssignEditorID(headTex, null, true);
                AddGeneratedRecordToDictionary(pathSignature, templateNPC, headTex);
                CacheResolvedObject("HeadTexture", templateHeadTexture, objectCaches, templateNPC);
            }
            else
            {
                Logger.LogReport("Could not resolve a head texture from NPC " + Logger.GetNPCLogNameString(npcInfo.NPC) + " or its corresponding record template.", true, npcInfo);
                return null;
            }

            if (!assignedFromDictionary)
            {
                var additionalGenericPaths = new List<FilePathReplacementParsed>();

                foreach (var path in paths)
                {
                    switch (path.DestinationStr)
                    {
                        case "HeadTexture.Height": headTex.Height = path.Source; break;
                        case "HeadTexture.Diffuse": headTex.Diffuse = path.Source; break;
                        case "HeadTexture.NormalOrGloss": headTex.NormalOrGloss = path.Source; break;
                        case "HeadTexture.GlowOrDetailMap": headTex.GlowOrDetailMap = path.Source; break;
                        case "HeadTexture.BacklightMaskOrSpecular": headTex.BacklightMaskOrSpecular = path.Source; break;
                        default: additionalGenericPaths.Add(path); break;
                    }
                }

                if (additionalGenericPaths.Any())
                {
                    AssignGenericAssetPaths(npcInfo, additionalGenericPaths, patchedNPC, templateLinkCache, outputMod, GetLongestPath(additionalGenericPaths), true, false, npcObjectMap, objectCaches);
                }
            }

            npcObjectMap.Add("HeadTexture", headTex);

            patchedNPC.HeadTexture.SetTo(headTex);
            return headTex;
        }

        private static Armor AssignBodyTextures(NPCInfo npcInfo, SkyrimMod outputMod, ILinkCache<ISkyrimMod, ISkyrimModGetter> mainLinkCache, ILinkCache<ISkyrimMod, ISkyrimModGetter> templateLinkCache, HashSet<FilePathReplacementParsed> paths, Dictionary<string, dynamic> npcObjectMap, Dictionary<FormKey, Dictionary<string, dynamic>> objectCaches)
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
                AssignEditorID(newSkin, existingWNAM.FormKey.ToString(), false);
                AddModifiedRecordToDictionary(pathSignature, npcInfo.NPC.WornArmor.FormKey, newSkin);
            }
            else if (TryGetGeneratedRecord(pathSignature, templateNPC, out newSkin))
            {
                assignedFromDictionary = true;
            }
            else if (!templateNPC.WornArmor.IsNull && templateLinkCache.TryResolve<IArmorGetter>(templateNPC.WornArmor.FormKey, out var templateWNAM))
            {
                CacheResolvedObject("WornArmor", templateWNAM, objectCaches, templateNPC);
                CacheResolvedObject("WornArmor.Armature", templateWNAM.Armature, objectCaches, templateNPC);
                newSkin = outputMod.Armors.AddNew();
                newSkin.DeepCopyIn(templateWNAM);
                assignedFromTemplate = true;
                AssignEditorID(newSkin, null, true);
                AddGeneratedRecordToDictionary(pathSignature, templateNPC, newSkin);
            }
            else
            {
                Logger.LogReport("Could not resolve a body texture from NPC " + npcInfo.LogIDstring + " or its corresponding record template.", true, npcInfo);
                outputMod.Armors.Remove(newSkin);
                return null;
            }

            var patchedNPC = outputMod.Npcs.GetOrAddAsOverride(npcInfo.NPC);

            npcObjectMap.Add("WornArmor", newSkin);
            npcObjectMap.Add("WornArmor.Armature", newSkin.Armature);

            if (!assignedFromDictionary)
            {
                #region sort paths
                var hardcodedTorsoArmorAddonPaths = new HashSet<FilePathReplacementParsed>();
                var hardcodedHandsArmorAddonPaths = new HashSet<FilePathReplacementParsed>();
                var hardcodedFeetArmorAddonPaths = new HashSet<FilePathReplacementParsed>();
                var hardcodedTailArmorAddonPaths = new HashSet<FilePathReplacementParsed>();
                var genericArmorAddonPaths = new List<FilePathReplacementParsed>();
                var genericTorsoArmorAddonSubpaths = new List<FilePathReplacementParsed>();
                var genericHandsArmorAddonSubpaths = new List<FilePathReplacementParsed>();
                var genericFeetArmorAddonSubpaths = new List<FilePathReplacementParsed>();
                var genericTailArmorAddonSubpaths = new List<FilePathReplacementParsed>();

                foreach (var path in paths)
                {
                    if (path.DestinationStr.StartsWith("WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Body)", StringComparison.OrdinalIgnoreCase))
                    {
                        if (TorsoArmorAddonPaths.Contains(path.DestinationStr)) { hardcodedTorsoArmorAddonPaths.Add(path); }
                        else { genericTorsoArmorAddonSubpaths.Add(path); }
                    }
                    else if (path.DestinationStr.StartsWith("WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Hands)", StringComparison.OrdinalIgnoreCase))
                    {
                        if (HandsArmorAddonPaths.Contains(path.DestinationStr)) { hardcodedHandsArmorAddonPaths.Add(path); }
                        else { genericHandsArmorAddonSubpaths.Add(path); }
                    }
                    else if (path.DestinationStr.StartsWith("WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Feet)", StringComparison.OrdinalIgnoreCase))
                    {
                        if (FeetArmorAddonPaths.Contains(path.DestinationStr)) { hardcodedFeetArmorAddonPaths.Add(path); }
                        else { genericFeetArmorAddonSubpaths.Add(path); }
                    }
                    else if (path.DestinationStr.StartsWith("WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Tail)", StringComparison.OrdinalIgnoreCase))
                    {
                        if (TailArmorAddonPaths.Contains(path.DestinationStr)) { hardcodedTailArmorAddonPaths.Add(path); }
                        else { genericTailArmorAddonSubpaths.Add(path); }
                    }
                    else
                    {
                        genericArmorAddonPaths.Add(path);
                    }
                }
                #endregion

                var allowedRaces = new HashSet<string>();
                allowedRaces.Add(npcInfo.NPC.Race.FormKey.ToString());
                var assetsRaceString = npcInfo.AssetsRace.ToString();
                if (!allowedRaces.Contains(assetsRaceString))
                {
                    allowedRaces.Add(assetsRaceString);
                }
                allowedRaces.Add(Skyrim.Race.DefaultRace.FormKey.ToString());

                string subPath;
                if (hardcodedTorsoArmorAddonPaths.Any() || genericTorsoArmorAddonSubpaths.Any())
                {
                    subPath = "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Body) && PatchableRaces.Contains(Race)]";
                    var assignedTorso = AssignArmorAddon(patchedNPC, newSkin, npcInfo, outputMod, mainLinkCache, templateLinkCache, hardcodedTorsoArmorAddonPaths, genericTorsoArmorAddonSubpaths, ArmorAddonType.Torso, subPath, allowedRaces, assignedFromTemplate, npcObjectMap, objectCaches);
                }
                if (hardcodedHandsArmorAddonPaths.Any() || genericHandsArmorAddonSubpaths.Any())
                {
                    subPath = "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Hands) && PatchableRaces.Contains(Race)]";
                    var assignedHands = AssignArmorAddon(patchedNPC, newSkin, npcInfo, outputMod, mainLinkCache, templateLinkCache, hardcodedHandsArmorAddonPaths, genericHandsArmorAddonSubpaths, ArmorAddonType.Hands, subPath, allowedRaces, assignedFromTemplate, npcObjectMap, objectCaches);
                }
                if (hardcodedFeetArmorAddonPaths.Any() || genericFeetArmorAddonSubpaths.Any())
                {
                    subPath = "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Feet) && PatchableRaces.Contains(Race)]";
                    var assignedFeet = AssignArmorAddon(patchedNPC, newSkin, npcInfo, outputMod, mainLinkCache, templateLinkCache, hardcodedFeetArmorAddonPaths, genericFeetArmorAddonSubpaths, ArmorAddonType.Feet, subPath, allowedRaces, assignedFromTemplate, npcObjectMap, objectCaches);
                }
                if (hardcodedTailArmorAddonPaths.Any() || genericTailArmorAddonSubpaths.Any())
                {
                    subPath = "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Tail) && PatchableRaces.Contains(Race)]";
                    var assignedTail = AssignArmorAddon(patchedNPC, newSkin, npcInfo, outputMod, mainLinkCache, templateLinkCache, hardcodedTailArmorAddonPaths, genericTailArmorAddonSubpaths, ArmorAddonType.Tail, subPath, allowedRaces, assignedFromTemplate, npcObjectMap, objectCaches);
                }
                if (genericArmorAddonPaths.Any())
                {
                    AssignGenericAssetPaths(npcInfo, genericArmorAddonPaths, patchedNPC, templateLinkCache, outputMod, GetLongestPath(genericArmorAddonPaths), true, false, npcObjectMap, objectCaches);
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

        private static ArmorAddon AssignArmorAddon(Npc targetNPC, Armor parentArmorRecord, NPCInfo npcInfo, SkyrimMod outputMod, ILinkCache<ISkyrimMod, ISkyrimModGetter> mainLinkCache, ILinkCache<ISkyrimMod, ISkyrimModGetter> templateLinkCache, HashSet<FilePathReplacementParsed> hardcodedPaths, List<FilePathReplacementParsed> additionalGenericPaths, ArmorAddonType type, string subPath, HashSet<string> currentRaceIDstrs, bool parentAssignedFromTemplate, Dictionary<string, dynamic> npcObjectMap, Dictionary<FormKey, Dictionary<string, dynamic>> objectCaches)
        {
            ArmorAddon newArmorAddon = null;
            IArmorAddonGetter sourceArmorAddon;
            HashSet<IArmorAddonGetter> candidateAAs = new HashSet<IArmorAddonGetter>();
            bool replaceExistingArmature = false;

            var pathSignature = hardcodedPaths.Select(x => x.Source).ToHashSet();

            INpcGetter templateNPC = hardcodedPaths.Where(x => WornArmorPaths.Contains(x.DestinationStr)).First().TemplateNPC ?? null;

            // try to get the needed armor addon template record from the existing parent armor record
            candidateAAs = GetAvailableArmature(parentArmorRecord, mainLinkCache, templateLinkCache, !parentAssignedFromTemplate, parentAssignedFromTemplate);

            sourceArmorAddon = ChooseArmature(candidateAAs, type, currentRaceIDstrs);
            replaceExistingArmature = sourceArmorAddon is not null;

            if (TryGetGeneratedRecord(pathSignature, sourceArmorAddon.FormKey, out newArmorAddon))
            {
                // do nothing
            }
            else if (sourceArmorAddon != null)
            {
                newArmorAddon = outputMod.ArmorAddons.AddNew();
                newArmorAddon.DeepCopyIn(sourceArmorAddon);
                var assignedSkinTexture = AssignSkinTexture(newArmorAddon, parentAssignedFromTemplate, npcInfo, outputMod, mainLinkCache, templateLinkCache, hardcodedPaths, npcObjectMap, objectCaches);
                replaceExistingArmature = true;

                AssignEditorID(newArmorAddon, sourceArmorAddon.FormKey.ToString(), parentAssignedFromTemplate);
                AddModifiedRecordToDictionary(pathSignature, sourceArmorAddon.FormKey, newArmorAddon);
            }

            // try to get the needed armor record from the corresponding record template
            else if (!templateNPC.WornArmor.IsNull && templateNPC.WornArmor.TryResolve(templateLinkCache, out var templateArmorGetter))
            {
                candidateAAs = GetAvailableArmature(templateArmorGetter, mainLinkCache, templateLinkCache, false, true);
                sourceArmorAddon = ChooseArmature(candidateAAs, type, currentRaceIDstrs);
                replaceExistingArmature = sourceArmorAddon is not null;

                if (TryGetGeneratedRecord(pathSignature, templateNPC, out newArmorAddon))
                {
                    // do nothing
                }
                else if (sourceArmorAddon != null)
                {
                    //newArmorAddon = outputMod.ArmorAddons.AddNew(templateAA.FormKey); // don't deep copy to avoid misnaming downstream editorIDs
                    newArmorAddon = outputMod.ArmorAddons.AddNew();
                    newArmorAddon.DeepCopyIn(sourceArmorAddon);
                    AssignEditorID(newArmorAddon, null, true);
                    AddGeneratedRecordToDictionary(pathSignature, templateNPC, newArmorAddon);

                    var assignedSkinTexture = AssignSkinTexture(newArmorAddon, true, npcInfo, outputMod, mainLinkCache, templateLinkCache, hardcodedPaths, npcObjectMap, objectCaches);
                }
            }

            if (sourceArmorAddon == null)
            {
                Logger.LogReport("Could not resolve " + type.ToString() + " armature for NPC " + npcInfo.LogIDstring + " or its template.", true, npcInfo);
            }
            else if (replaceExistingArmature == false)
            {
                parentArmorRecord.Armature.Add(newArmorAddon);
            }
            else
            {
                var templateFK = sourceArmorAddon.FormKey.ToString();
                for (int i = 0; i < parentArmorRecord.Armature.Count; i++)
                {
                    if (parentArmorRecord.Armature[i].FormKey.ToString() == templateFK)
                    {
                        parentArmorRecord.Armature[i] = newArmorAddon.AsLinkGetter();
                    }
                }
            }

            npcObjectMap.Add(subPath, newArmorAddon);

            if (additionalGenericPaths.Any())
            {
                AssignGenericAssetPaths(npcInfo, additionalGenericPaths, targetNPC, templateLinkCache, outputMod, GetLongestPath(additionalGenericPaths), true, false, npcObjectMap, objectCaches);
            }

            return newArmorAddon;
        }

        private static TextureSet AssignSkinTexture(ArmorAddon parentArmorAddonRecord, bool parentAssignedFromTemplate, NPCInfo npcInfo, SkyrimMod outputMod, ILinkCache<ISkyrimMod, ISkyrimModGetter> mainLinkCache, ILinkCache<ISkyrimMod, ISkyrimModGetter> templateLinkCache, HashSet<FilePathReplacementParsed> paths, Dictionary<string, dynamic> npcObjectMap, Dictionary<FormKey, Dictionary<string, dynamic>> objectCaches)
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
                AssignEditorID(newSkinTexture, existingSkinTexture.FormKey.ToString(), parentAssignedFromTemplate);
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
                AssignEditorID(newSkinTexture, null, true);
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

            var recordPathSplit = paths.Where(x => WornArmorPaths.Contains(x.DestinationStr)).First().DestinationStr.Split('.').ToList();
            string recordPath = BuildPath(recordPathSplit.GetRange(0, recordPathSplit.Count - 2));
            objectCaches[npcInfo.NPC.FormKey][recordPath] = newSkinTexture;

            return newSkinTexture;
        }

        public static void AssignEditorID(IMajorRecord record, string templateFKstr, bool copiedFromTemplate)
        {
            if (copiedFromTemplate)
            {
                IncrementEditorID(new HashSet<IMajorRecord>() { record });
            }
            else
            {
                record.EditorID += "_Patched";
                if (ModifiedRecordCounts.ContainsKey(templateFKstr))
                {
                    ModifiedRecordCounts[templateFKstr]++;
                }
                else
                {
                    ModifiedRecordCounts.Add(templateFKstr, 1);
                }

                record.EditorID += ModifiedRecordCounts[templateFKstr].ToString("D4");
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
                GeneratedRecordsByTempateNPC[pathSignature].Add(templateFKstring, record);
            }

            //GeneratedRecordsByTempateNPC[pathSignature][templateFKstring] = record;
        }

        //Dictionary[SourcePaths.ToHashSet()][SubPathStr][RecordTemplate.FormKey.ToString()] = Object Generated

        private static Dictionary<HashSet<string>, Dictionary<string, Dictionary<HashSet<string>, ObjectAtIndex>>> GeneratedObjectsByPathAndTemplate = new Dictionary<HashSet<string>, Dictionary<string, Dictionary<HashSet<string>, ObjectAtIndex>>>(HashSet<string>.CreateSetComparer());

        private class ObjectAtIndex
        {
            public ObjectAtIndex()
            {
                generatedObj = null;
                indexInTemplate = null; // index in template NPC remains invariant throughout patcher execution so is safe to store rather than having to call RecordPathParser to find the array index of the generatedObj for the given NPC
            }
            public dynamic generatedObj { get; set; }
            public int? indexInTemplate { get; set; }
        }

        private static bool TryGetGeneratedObject(HashSet<string> pathSignature, string pathRelativeToNPC, HashSet<INpcGetter> templateSignature, out dynamic storedObj, out int? indexIfInArray)
        {
            var templateSignatureStr = templateSignature.Select(x => x.FormKey.ToString()).ToHashSet();
            if (GeneratedObjectsByPathAndTemplate.ContainsKey(pathSignature) && GeneratedObjectsByPathAndTemplate[pathSignature].ContainsKey(pathRelativeToNPC) && GeneratedObjectsByPathAndTemplate[pathSignature][pathRelativeToNPC].ContainsKey(templateSignatureStr))
            {
                storedObj = GeneratedObjectsByPathAndTemplate[pathSignature][pathRelativeToNPC][templateSignatureStr].generatedObj;
                indexIfInArray = GeneratedObjectsByPathAndTemplate[pathSignature][pathRelativeToNPC][templateSignatureStr].indexInTemplate;
                return storedObj != null;
            }
            storedObj = null;
            indexIfInArray = null;
            return false;
        }

        private static void AddGeneratedObjectToDictionary(HashSet<string> pathSignature, string pathRelativeToNPC, HashSet<INpcGetter> templateSignature, dynamic storedObj, int? storedIndex)
        {
            var storedObjectAndIndex = new ObjectAtIndex() { generatedObj = storedObj, indexInTemplate = storedIndex };

            var templateSignatureStr = templateSignature.Select(x => x.FormKey.ToString()).ToHashSet();
            if (!GeneratedObjectsByPathAndTemplate.ContainsKey(pathSignature))
            {
                GeneratedObjectsByPathAndTemplate.Add(pathSignature, new Dictionary<string, Dictionary<HashSet<string>, ObjectAtIndex>>());
            }

            if (!GeneratedObjectsByPathAndTemplate[pathSignature].ContainsKey(pathRelativeToNPC))
            {
                GeneratedObjectsByPathAndTemplate[pathSignature].Add(pathRelativeToNPC, new Dictionary<HashSet<string>, ObjectAtIndex>(HashSet<string>.CreateSetComparer()));
            }

            if (!GeneratedObjectsByPathAndTemplate[pathSignature][pathRelativeToNPC].ContainsKey(templateSignatureStr))
            {
                GeneratedObjectsByPathAndTemplate[pathSignature][pathRelativeToNPC].Add(templateSignatureStr, storedObjectAndIndex);
            }
        }
    }
}
