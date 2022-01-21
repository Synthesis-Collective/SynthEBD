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
        public static void CombinationToRecords(List<SubgroupCombination> combinations, NPCInfo npcInfo, ILinkCache<ISkyrimMod, ISkyrimModGetter> recordTemplateLinkCache, Dictionary<FormKey, Dictionary<string, dynamic>> objectLinkMap, SkyrimMod outputMod)
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
            objectLinkMap.Add(npcInfo.NPC.FormKey, new Dictionary<string, dynamic>(StringComparer.OrdinalIgnoreCase) { { "", currentNPC } });

            if (headtexPaths.Any())
            {
                AssignHeadTexture(npcInfo, outputMod, Patcher.MainLinkCache, recordTemplateLinkCache, headtexPaths, objectLinkMap);
            }
            if (wnamPaths.Any())
            {
                AssignBodyTextures(npcInfo, outputMod, Patcher.MainLinkCache, recordTemplateLinkCache, wnamPaths, objectLinkMap);
            }
            if (nonHardcodedPaths.Any())
            {
                AssignGenericAssetPaths(npcInfo, nonHardcodedPaths, currentNPC, recordTemplateLinkCache, outputMod, longestPath, true, false, objectLinkMap);
            }
        }

        public static void ReplacerCombinationToRecords(SubgroupCombination combination, NPCInfo npcInfo, SkyrimMod outputMod, ILinkCache<ISkyrimMod, ISkyrimModGetter> recordTemplateLinkCache, Dictionary<FormKey, Dictionary<string, dynamic>> objectLinkMap)
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
                    AssignGenericAssetPaths(npcInfo, nonHardcodedPaths, currentNPC, null, outputMod, longestPath, false, true, objectLinkMap);
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

        public static void AssignGenericAssetPaths(NPCInfo npcInfo, List<FilePathReplacementParsed> nonHardcodedPaths, Npc rootNPC, ILinkCache<ISkyrimMod, ISkyrimModGetter> recordTemplateLinkCache, SkyrimMod outputMod, int longestPath, bool assignFromTemplate, bool suppressMissingPathErrors, Dictionary<FormKey, Dictionary<string, dynamic>> objectLinkMap)
        { 
            HashSet<IMajorRecord> assignedRecords = new HashSet<IMajorRecord>();

            //Dictionary<string, dynamic> recordsAtPaths = new Dictionary<string, dynamic>(); // quickly look up record templates rather than redoing reflection work
            Dictionary<string, dynamic> objectsAtPath_Root = null;
            if (objectLinkMap.ContainsKey(rootNPC.FormKey))
            {
                objectsAtPath_Root = objectLinkMap[rootNPC.FormKey];
            }
            else
            {
                objectsAtPath_Root = new Dictionary<string, dynamic>(StringComparer.OrdinalIgnoreCase);
                objectLinkMap.Add(rootNPC.FormKey, objectsAtPath_Root);
            }

            /*
            foreach (var template in nonHardcodedPaths.Select(x => x.TemplateNPC).ToHashSet())
            {
                Dictionary<string, dynamic> objectsAtPath_Template = new Dictionary<string, dynamic>(StringComparer.OrdinalIgnoreCase);
                objectsAtPath_Template.Add("", template);

                if (template != null)
                {
                    objectLinkMap.Add(template.FormKey, objectsAtPath_Template);
                }
            }*/

            dynamic currentObj;

            HashSet<object> templateDerivedRecords = new HashSet<object>();

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
                    var rootObj = objectsAtPath_Root[parentPath];
                    int? indexIfInArray = null;

                    // step through the path
                    //bool npcHasObject = RecordPathParser.GetObjectAtPath(rootObj, currentSubPath, objectLinkMap[npcInfo.NPC.FormKey], Patcher.MainLinkCache, suppressMissingPathErrors, Logger.GetNPCLogNameString(npcInfo.NPC), out currentObj, out indexIfInArray); // update this function to out the aray index
                    bool npcHasObject = RecordPathParser.GetObjectAtPath(rootNPC, group.Key, objectLinkMap[npcInfo.NPC.FormKey], Patcher.MainLinkCache, suppressMissingPathErrors, Logger.GetNPCLogNameString(npcInfo.NPC), out currentObj, out indexIfInArray); 
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
                                    Logger.LogError("Could not determine record type for object of type " + currentObj.GetType().Name + ": " + Logger.GetNPCLogNameString(npcInfo.NPC) + " at path: " + group.Key + ". This subrecord will not be assigned.");
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
                                            Logger.LogError("Could not deep copy a record for NPC " + Logger.GetNPCLogNameString(npcInfo.NPC) + " at path: " + group.Key + ". This subrecord will not be assigned.");
                                            continue;
                                        }

                                        copiedRecord.EditorID += "_" + npcInfo.NPC.EditorID;

                                        SetViaFormKeyReplacement(copiedRecord, rootObj, currentSubPath, indexIfInArray, outputMod);
                                        currentObj = copiedRecord;
                                    } 
                                }
                                else if (recordTemplateLinkCache.TryResolve(recordFormKey.Value, recordType, out currentMajorRecordGetter)) // note: current object can belong to a record template if it is an attribute of a class that was copied from a record template (since copying the struct doesn't deep copy the contained formlnks)
                                {
                                    string pathSignature = string.Concat(group.Select(x => x.Source));
                                    //if (!TraverseRecordFromTemplate(rootObj, currentSubPath, indexIfInArray, currentObj, recordTemplateLinkCache, nonHardcodedPaths, group, assignedRecords, recordsAtPaths, pathSignature, outputMod, out currentObj))
                                    if (!TraverseRecordFromTemplate(rootObj, currentSubPath, indexIfInArray, currentObj, recordTemplateLinkCache, nonHardcodedPaths, group, assignedRecords, outputMod, out currentObj))
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
                    if (assignFromTemplate && (!npcHasObject || npcHasNullFormLink)) // get corresponding object from template NPC
                    {
                        var pathSignature = group.Select(x => x.Source).ToHashSet();
                        var templateSignature = group.Select(x => x.TemplateNPC).ToHashSet();
                        if (TryGetGeneratedObject(pathSignature, group.Key, templateSignature, out currentObj, out indexIfInArray))
                        {
                            templateHasObject = true;
                            if (RecordPathParser.ObjectHasFormKey(currentObj, out FormKey? _))
                            {
                                SetViaFormKeyReplacement(currentObj, rootObj, currentSubPath, indexIfInArray, outputMod);
                            }
                            else
                            {
                                RecordPathParser.SetSubObject(rootObj, currentSubPath, currentObj);
                            }
                            
                            if (objectsAtPath_Root.ContainsKey(group.Key)) // can be the case if the record traversal returned the getter, and now we are replacing with the corresponding setter
                            {
                                objectsAtPath_Root[group.Key] = currentObj;
                            }
                            else
                            {
                                objectsAtPath_Root.Add(group.Key, currentObj);
                            }

                            RemovePathsFromList(nonHardcodedPaths, group); // remove because everything downstream has already been assigned
                        }
                        else if (GetObjectFromAvailableTemplates(group.Key, group.ToArray(), objectLinkMap, recordTemplateLinkCache, suppressMissingPathErrors, out currentObj, out indexIfInArray))
                        {
                            templateHasObject = true;
                            // if the template object is a record, add it to the generated patch and then copy it to the NPC
                            // if the template object is just a struct (not a record), simply copy it to the NPC
                            if (RecordPathParser.ObjectHasFormKey(currentObj, out FormKey? recordFormKey))
                            { ///////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////Double check that the GetSubObject(currentObj, "FormKey", out dynamic templateFormKeyDyn) is necessary. Should be able to call DeepcopyRecordTopPath using currentObj.FormKey.ModKey instead of templateFormKey.ModKey

                                // if the current set of paths has already been assigned to another record, get that record
                                /*
                                string pathSignature = string.Concat(group.Select(x => x.Source));
                                if (recordsAtPaths.ContainsKey(pathSignature))
                                {
                                    currentObj = recordsAtPaths[pathSignature];
                                }
                                else */
                                if (RecordPathParser.GetSubObject(currentObj, "FormKey", out dynamic templateFormKeyDyn))
                                {
                                    FormKey templateFormKey = (FormKey)templateFormKeyDyn;
                                    //if (!TraverseRecordFromTemplate(rootObj, currentSubPath, indexIfInArray, currentObj, recordTemplateLinkCache, nonHardcodedPaths, group, assignedRecords, recordsAtPaths, pathSignature, outputMod, out currentObj))
                                    if (!TraverseRecordFromTemplate(rootObj, currentSubPath, indexIfInArray, currentObj, recordTemplateLinkCache, nonHardcodedPaths, group, assignedRecords, outputMod, out currentObj))
                                    {
                                        continue;
                                    }
                                }
                                else
                                {
                                    Logger.LogError("Record template error: Could not obtain a non-null FormKey for template NPCs " + string.Join(", ", group.Select(x => x.TemplateNPC).Select(x => x.EditorID)) + " at path: " + group.Key + ". This subrecord will not be assigned.");
                                }
                            }
                            else
                            {
                                RecordPathParser.SetSubObject(rootObj, currentSubPath, currentObj);
                            }

                            AddGeneratedObjectToDictionary(pathSignature, group.Key, templateSignature, currentObj, indexIfInArray);
                        }
                    }

                    if (!npcHasObject && !templateHasObject && assignFromTemplate)
                    {
                        Logger.LogError("Error: neither NPC " + npcInfo.LogIDstring + " nor the record templates " + string.Join(", ", group.Select(x => x.TemplateNPC).Select(x => x.EditorID)) + " contained a record at " + group.Key + ". Cannot assign this record.");
                        RemovePathsFromList(nonHardcodedPaths, group);
                    }

                    // if this is the last part of the path, attempt to assign the Source asset to the Destination
                    else if (group.First().Destination.Length == i + 1)
                    {
                        foreach (var assetAssignment in group)
                        {
                            RecordPathParser.SetSubObject(rootObj, currentSubPath, assetAssignment.Source);
                            currentObj = assetAssignment.Source;
                        }
                    }
                    else if (objectsAtPath_Root.ContainsKey(group.Key))
                    {
                        objectsAtPath_Root[group.Key] = currentObj; // RecordPathParser.GetObjectAtPath() populates this with the Getter. Update it here with the newly generated Setter
                    }
                    else if (!objectsAtPath_Root.ContainsKey(group.Key)) // this condition evaluates true only when the current subpath is a top-level subpath (e.g. npc.x rather than npc.x.y) because GetObjectAtPath() will populate the first subpath of the root object, which in this case is the NPC
                    {
                        objectsAtPath_Root.Add(group.Key, currentObj); // for next iteration of top for loop
                    }
                }
            }
        }

        //public static bool TraverseRecordFromTemplate(dynamic rootObj, string currentSubPath, int? indexIfInArray, dynamic recordToCopy, ILinkCache<ISkyrimMod, ISkyrimModGetter> recordTemplateLinkCache, List<FilePathReplacementParsed> allPaths, IGrouping<string, FilePathReplacementParsed> group, HashSet<IMajorRecord> templateDerivedRecords, Dictionary<string, dynamic> recordsAtPaths, string pathSignature, SkyrimMod outputMod, out dynamic currentObj)
        public static bool TraverseRecordFromTemplate(dynamic rootObj, string currentSubPath, int? indexIfInArray, dynamic recordToCopy, ILinkCache<ISkyrimMod, ISkyrimModGetter> recordTemplateLinkCache, List<FilePathReplacementParsed> allPaths, IGrouping<string, FilePathReplacementParsed> group, HashSet<IMajorRecord> templateDerivedRecords, SkyrimMod outputMod, out dynamic currentObj)
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
                RemovePathsFromList(allPaths, group);
                currentObj = recordToCopy;
                return false;
            }

            templateDerivedRecords.UnionWith(copiedRecords);
            IncrementEditorID(copiedRecords);

            SetViaFormKeyReplacement(newRecord, rootObj, currentSubPath, indexIfInArray, outputMod);

            currentObj = newRecord;

            //recordsAtPaths.Add(pathSignature, newRecord); // store paths associated with this record for future lookup to avoid having to repeat the reflection for other NPCs who get the same combination and need to be assigned the same record
            return true;
        }

        public static dynamic GetObjectFromAvailableTemplates(string currentSubPath, FilePathReplacementParsed[] allPaths, Dictionary<FormKey, Dictionary<string, dynamic>> objectLinkMap, ILinkCache<ISkyrimMod, ISkyrimModGetter> recordTemplateLinkCache, bool suppressMissingPathErrors, out dynamic outputObj, out int? indexIfInArray)
        {
            foreach (var templateNPC in allPaths.Select(x => x.TemplateNPC).ToHashSet())
            {
                if (!objectLinkMap.ContainsKey(templateNPC.FormKey))
                {
                    objectLinkMap.Add(templateNPC.FormKey, new Dictionary<string, dynamic>(StringComparer.OrdinalIgnoreCase));
                }

                if (RecordPathParser.GetObjectAtPath(templateNPC, currentSubPath, objectLinkMap[templateNPC.FormKey], recordTemplateLinkCache, suppressMissingPathErrors, Logger.GetNPCLogNameString(templateNPC), out outputObj, out indexIfInArray))
                {
                    return true;
                }
            }

            outputObj = null;
            indexIfInArray = null;
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

        public static IMajorRecord AssignHeadTexture(NPCInfo npcInfo, SkyrimMod outputMod, ILinkCache<ISkyrimMod, ISkyrimModGetter> mainLinkCache, ILinkCache<ISkyrimMod, ISkyrimModGetter> templateLinkCache, HashSet<FilePathReplacementParsed> paths, Dictionary<FormKey, Dictionary<string, dynamic>> objectLinkMap)
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
                objectLinkMap[templateNPC.FormKey].Add("HeadTexture", templateHeadTexture);
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
                    AssignGenericAssetPaths(npcInfo, additionalGenericPaths, patchedNPC, templateLinkCache, outputMod, GetLongestPath(additionalGenericPaths), true, false, objectLinkMap);
                }
            }

            objectLinkMap[npcInfo.NPC.FormKey].Add("HeadTexture", headTex);

            patchedNPC.HeadTexture.SetTo(headTex);
            return headTex;
        }

        private static Armor AssignBodyTextures(NPCInfo npcInfo, SkyrimMod outputMod, ILinkCache<ISkyrimMod, ISkyrimModGetter> mainLinkCache, ILinkCache<ISkyrimMod, ISkyrimModGetter> templateLinkCache, HashSet<FilePathReplacementParsed> paths, Dictionary<FormKey, Dictionary<string, dynamic>> objectLinkMap)
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
                objectLinkMap[templateNPC.FormKey].Add("WornArmor", templateWNAM);
                objectLinkMap[templateNPC.FormKey].Add("WornArmor.Armature", templateWNAM.Armature);
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

            var patchedNPC = outputMod.Npcs.GetOrAddAsOverride(npcInfo.NPC);

            objectLinkMap[npcInfo.NPC.FormKey].Add("WornArmor", newSkin);
            objectLinkMap[npcInfo.NPC.FormKey].Add("WornArmor.Armature", newSkin.Armature);

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

                if (hardcodedTorsoArmorAddonPaths.Any() || genericTorsoArmorAddonSubpaths.Any())
                {
                    var assignedTorso = AssignArmorAddon(patchedNPC, newSkin, npcInfo, outputMod, mainLinkCache, templateLinkCache, hardcodedTorsoArmorAddonPaths, genericTorsoArmorAddonSubpaths, ArmorAddonType.Torso, allowedRaces, assignedFromTemplate, objectLinkMap);
                }
                if (hardcodedHandsArmorAddonPaths.Any() || genericHandsArmorAddonSubpaths.Any())
                {
                    var assignedHands = AssignArmorAddon(patchedNPC, newSkin, npcInfo, outputMod, mainLinkCache, templateLinkCache, hardcodedHandsArmorAddonPaths, genericHandsArmorAddonSubpaths, ArmorAddonType.Hands, allowedRaces, assignedFromTemplate, objectLinkMap);
                }
                if (hardcodedFeetArmorAddonPaths.Any() || genericFeetArmorAddonSubpaths.Any())
                {
                    var assignedFeet = AssignArmorAddon(patchedNPC, newSkin, npcInfo, outputMod, mainLinkCache, templateLinkCache, hardcodedFeetArmorAddonPaths, genericFeetArmorAddonSubpaths, ArmorAddonType.Feet, allowedRaces, assignedFromTemplate, objectLinkMap);
                }
                if (hardcodedTailArmorAddonPaths.Any() || genericTailArmorAddonSubpaths.Any())
                {
                    var assignedTail = AssignArmorAddon(patchedNPC, newSkin, npcInfo, outputMod, mainLinkCache, templateLinkCache, hardcodedTailArmorAddonPaths, genericTailArmorAddonSubpaths, ArmorAddonType.Tail, allowedRaces, assignedFromTemplate, objectLinkMap);
                }
                if (genericArmorAddonPaths.Any())
                {
                    AssignGenericAssetPaths(npcInfo, genericArmorAddonPaths, patchedNPC, templateLinkCache, outputMod, GetLongestPath(genericArmorAddonPaths), true, false, objectLinkMap);
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

        private static ArmorAddon AssignArmorAddon(Npc targetNPC, Armor parentArmorRecord, NPCInfo npcInfo, SkyrimMod outputMod, ILinkCache<ISkyrimMod, ISkyrimModGetter> mainLinkCache, ILinkCache<ISkyrimMod, ISkyrimModGetter> templateLinkCache, HashSet<FilePathReplacementParsed> hardcodedPaths, List<FilePathReplacementParsed> additionalGenericPaths, ArmorAddonType type, HashSet<string> currentRaceIDstrs, bool parentAssignedFromTemplate, Dictionary<FormKey, Dictionary<string, dynamic>> objectLinkMap)
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
                var assignedSkinTexture = AssignSkinTexture(newArmorAddon, parentAssignedFromTemplate, npcInfo, outputMod, mainLinkCache, templateLinkCache, hardcodedPaths, objectLinkMap);
                replaceExistingArmature = true;

                AssignEditorID(newArmorAddon, npcInfo.NPC, parentAssignedFromTemplate);
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
                    AssignEditorID(newArmorAddon, npcInfo.NPC, true);
                    AddGeneratedRecordToDictionary(pathSignature, templateNPC, newArmorAddon);

                    var assignedSkinTexture = AssignSkinTexture(newArmorAddon, true, npcInfo, outputMod, mainLinkCache, templateLinkCache, hardcodedPaths, objectLinkMap);
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

            if (additionalGenericPaths.Any())
            {
                AssignGenericAssetPaths(npcInfo, additionalGenericPaths, targetNPC, templateLinkCache, outputMod, GetLongestPath(additionalGenericPaths), true, false, objectLinkMap);
            }

            return newArmorAddon;
        }

        private static TextureSet AssignSkinTexture(ArmorAddon parentArmorAddonRecord, bool parentAssignedFromTemplate, NPCInfo npcInfo, SkyrimMod outputMod, ILinkCache<ISkyrimMod, ISkyrimModGetter> mainLinkCache, ILinkCache<ISkyrimMod, ISkyrimModGetter> templateLinkCache, HashSet<FilePathReplacementParsed> paths, Dictionary<FormKey, Dictionary<string, dynamic>> objectLinkMap)
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

            var recordPathSplit = paths.Where(x => WornArmorPaths.Contains(x.DestinationStr)).First().DestinationStr.Split('.').ToList();
            string recordPath = BuildPath(recordPathSplit.GetRange(0, recordPathSplit.Count - 2));
            objectLinkMap[npcInfo.NPC.FormKey][recordPath] = newSkinTexture;

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
