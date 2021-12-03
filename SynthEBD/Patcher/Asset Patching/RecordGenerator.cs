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
        private class PathToTemplateLinker
        {
            public PathToTemplateLinker()
            {

            }

            public List<FilePathReplacementParsed> ParsedPaths { get; set; }
            public ParsedPathObj PathObj { get; set; }
        }

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

            Dictionary<string, dynamic> objectsAtPath_NPC = new Dictionary<string, dynamic>();
            objectsAtPath_NPC.Add("", npcInfo.NPC);

            Dictionary<string, dynamic> objectsAtPath_Template = new Dictionary<string, dynamic>();
            objectsAtPath_Template.Add("", template);

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
                    string prePath = String.Join(".", group.First().Destination.ToList().GetRange(0, i));
                    string commonPath = group.First().Destination[i];
                    var rootObj = objectsAtPath_NPC[prePath];

                    // if the current set of paths has already been assigned to another record, get that record
                    string pathSignature = string.Concat(group.Select(x => x.Source));
                    if (recordsAtPaths.ContainsKey(pathSignature))
                    {
                        currentObj = recordsAtPaths[pathSignature];
                        objectsAtPath_NPC.Add(prePath + "." + commonPath, currentObj); // for next iteration of top for loop
                    }
                    else
                    {
                        // step through the path
                        bool currentObjectIsARecord = RecordPathParser.PropertyIsRecord(rootObj, commonPath, out FormKey? recordFormKey);

                        if (currentObjectIsARecord && !recordFormKey.Value.IsNull && MainLoop.MainLinkCache.TryResolve(recordFormKey.Value, out var currentMajorRecordCommonGetter)) //if the current object is a record, resolve it so that it can be traversed
                        {
                            currentObj = GetOrAddGenericRecordAsOverride((IMajorRecordGetter)currentMajorRecordCommonGetter, outputMod);
                        }
                        else
                        {
                            currentObj = RecordPathParser.GetObjectAtPath(rootObj, commonPath, objectsAtPath_NPC, MainLoop.MainLinkCache);
                        }
                        // if the NPC doesn't have the given object (e.g. the NPC doesn't have a WNAM), assign in from template
                        if (currentObj == null || currentObjectIsARecord && recordFormKey.Value.IsNull)
                        {
                            string templateRelPath;
                            if (prePath.Length > 0)
                            {
                                templateRelPath = String.Join('.', new string[] { prePath, commonPath });
                            }
                            else
                            {
                                templateRelPath = commonPath;
                            }

                            currentObj = RecordPathParser.GetObjectAtPath(template, templateRelPath, objectsAtPath_Template, recordTemplateLinkCache); // get corresponding object from template NPC

                            if (currentObj == null)
                            {
                                Logger.LogError("Error: neither NPC " + npcInfo.LogIDstring + " nor the record template " + template.EditorID + " contained a record at " + templateRelPath + ". Cannot assign this record.");
                            }

                            else
                            {
                                // if the template object is a record, add it to the generated patch and then copy it to the NPC
                                // if the template object is just a struct (not a record), simply copy it to the NPC
                                if (currentObjectIsARecord)
                                {
                                    // old way
                                    //var templateFormKeyObj = RecordPathParser.GetObjectAtPath(currentObj, "FormKey", objectsAtPath_Template, recordTemplateLinkCache);
                                    //recordTemplateLinkCache.TryResolveContext((FormKey)templateFormKeyObj, out var templateContext);
                                    //var newRecord = (IMajorRecord)templateContext.DuplicateIntoAsNewRecord(outputMod); // without cast, would be an IMajorRecordCommon

                                    // new way
                                    /*
                                    var templateRecordGetter = (IMajorRecordGetter)templateContext.Record;
                                    dynamic modGroup = GetPatchRecordGroup(templateRecordGetter, outputMod);
                                    var newRecord = IGroupMixIns.AddNew(modGroup);
                                    MajorRecordMixIn.DeepCopyIn(newRecord, templateRecordGetter);
                                    */

                                    // newer way
                                    var templateFormKeyObj = RecordPathParser.GetObjectAtPath(currentObj, "FormKey", objectsAtPath_Template, recordTemplateLinkCache);
                                    if (templateFormKeyObj == null)
                                    {
                                        Logger.LogError("Record template error: Could not obtain a FormKey for template NPC " + Logger.GetNPCLogNameString(template) + " at path: " + templateRelPath + ". This subrecord will not be assigned.");
                                        continue;
                                    }

                                    var templateFormKey = (FormKey)templateFormKeyObj;
                                    if (templateFormKey.IsNull)
                                    {
                                        Logger.LogError("Record template error: Template NPC " + Logger.GetNPCLogNameString(template) + " does not have a record at path: " + templateRelPath + ". This subrecord will not be assigned.");
                                        continue;
                                    }

                                    var newRecord = DeepCopyRecordToPatch(templateFormKey, recordTemplateLinkCache, outputMod);

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
                                    SetRecord((IMajorRecordGetter)rootObj, commonPath, newRecord, outputMod);
                                    currentObj = newRecord;
                                }
                                else
                                {
                                    RecordPathParser.SetSubObject(rootObj, commonPath, currentObj);
                                }
                            }

                            // store paths associated with this record for future lookup to avoid having to repeat the reflection
                            if (currentObj != null)
                            {
                                objectsAtPath_Template.Add(prePath + commonPath, currentObj); // for subsequent searches within record template
                                objectsAtPath_NPC.Add(prePath + commonPath, currentObj); // for next iteration of top for loop

                                if (currentObjectIsARecord)
                                {
                                    recordsAtPaths.Add(pathSignature, currentObj); // for other NPCs who get the same combination and need to be assigned the same record
                                }
                            }
                        }
                        else
                        {
                            objectsAtPath_NPC.Add(prePath + "." + commonPath, currentObj); // for next iteration of top for loop
                        }
                    }


                    // if this is the last part of the path, attempt to assign the Source asset to the Destination
                    if (group.First().Destination.Length == i + 1)
                    {
                        foreach (var assetAssignment in group)
                        {
                            RecordPathParser.SetSubObject(rootObj, group.Key, assetAssignment.Source);
                            currentObj = assetAssignment.Source;
                        }
                    }
                }
            }
            int dbg = 0;
        }

        public static IMajorRecord DeepCopyRecordToPatch(FormKey recordFormKey, ILinkCache<ISkyrimMod, ISkyrimModGetter> sourceLinkCache, ISkyrimMod destinationMod)
        {
            IMajorRecord copiedRecord = null;
            if (sourceLinkCache.TryResolveContext(recordFormKey, out var templateContext))
            {
                copiedRecord = (IMajorRecord)templateContext.DuplicateIntoAsNewRecord(destinationMod); // without cast, would be an IMajorRecordCommon

                Dictionary<FormKey, FormKey> mapping = new Dictionary<FormKey, FormKey>();
                foreach (var fl in copiedRecord.ContainedFormLinks)
                {
                    if (fl.FormKey.ModKey == recordFormKey.ModKey && !fl.FormKey.IsNull)
                    {
                        var copiedSubRecord = DeepCopyRecordToPatch(fl.FormKey, sourceLinkCache, destinationMod);
                        mapping.Add(fl.FormKey, copiedSubRecord.FormKey);                   
                        string pauseHere = "";
                    }
                }
                if (mapping.Any())
                {
                    copiedRecord.RemapLinks(mapping);
                }
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
