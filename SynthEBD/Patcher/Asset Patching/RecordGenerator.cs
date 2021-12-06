using Mutagen.Bethesda;
using Mutagen.Bethesda.Cache.Implementations;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Reflection;
using Mutagen.Bethesda.Plugins.Records;
using Loqui;
using Mutagen.Bethesda.Plugins.Cache;

namespace SynthEBD
{
    public class RecordGenerator
    {
        public static void CombinationToRecords(SubgroupCombination combination, NPCInfo npcInfo, ImmutableLoadOrderLinkCache<ISkyrimMod, ISkyrimModGetter> recordTemplateLinkCache, SkyrimMod outputMod, Dictionary<string, int> edidCounts)
        {
            var template = GetTemplateNPC(npcInfo, combination.AssetPack, recordTemplateLinkCache);

            List<FilePathReplacementParsed> paths = new List<FilePathReplacementParsed>();
            int longestPath = 0;

            foreach (var subgroup in combination.ContainedSubgroups)
            {
                foreach(var path in subgroup.Paths)
                {
                    var parsed = new FilePathReplacementParsed(path);
                    paths.Add(parsed);
                    if (parsed.Destination.Length > longestPath)
                    {
                        longestPath = parsed.Destination.Length;
                    }
                }
            }

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
                for (int j = 0; j < paths.Count; j++)
                {
                    if (i == paths[j].Destination.Length) // Remove paths that were already assigned
                    {
                        paths.RemoveAt(j);
                        j--;
                    }
                }

                var groupedPathsAtI = paths.GroupBy(x => String.Join(".", x.Destination.ToList().GetRange(0, i + 1))); // group paths by the current path segment

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
                        currentObj = RecordPathParser.GetObjectAtPath(rootObj, currentSubPath, objectLinkMap, MainLoop.MainLinkCache);
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
                            if (MainLoop.MainLinkCache.TryResolve(recordFormKey.Value, recordType, out var currentMajorRecordCommonGetter)) //if the current object is an existing record, resolve it so that it can be traversed
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
                                    if (edidCounts.ContainsKey(newRecord.EditorID))
                                    {
                                        edidCounts[newRecord.EditorID]++;
                                        newRecord.EditorID += edidCounts[newRecord.EditorID];
                                    }
                                    else
                                    {
                                        edidCounts.Add(newRecord.EditorID, 1);
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
            int dbg = 0;
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

        public static object GetOrAddGenericRecordAsOverride(IMajorRecordGetter recordGetter, SkyrimMod outputMod)
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
            var settableRecord = GetOrAddGenericRecordAsOverride(root, outputMod);
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
    }
}
