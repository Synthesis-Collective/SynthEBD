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

            Dictionary<string, Object> recordsAtPaths = new Dictionary<string, Object>(); // quickly look up record templates rather than redoing reflection work

            Dictionary<string, Object> objectsAtPath_NPC = new Dictionary<string, Object>();
            objectsAtPath_NPC.Add("", npcInfo.NPC);

            Dictionary<string, Object> objectsAtPath_Template = new Dictionary<string, Object>();
            objectsAtPath_Template.Add("", template);

            Object currentObj;

            for (int i = 0; i < longestPath; i++)
            {
                for (int j = 0; j < paths.Count; j++)
                {
                    if (paths[j].Destination.Length == i - 1) // last segment of path
                    {
                        paths.RemoveAt(j);
                        j--;
                    }
                }

                var groupedPathsAtI = paths.GroupBy(x => x.Destination[i]);

                foreach (var group in groupedPathsAtI)
                {
                    string prePath = String.Concat(group.First().Destination.ToList().GetRange(0, i));
                    string commonPath = group.First().Destination[i];
                    var rootObj = objectsAtPath_NPC[prePath];

                    // if the current set of paths has already been assigned to another record, get that record
                    string pathSignature = string.Concat(group.Select(x => x.Source));
                    if (recordsAtPaths.ContainsKey(pathSignature))
                    {
                        currentObj = recordsAtPaths[pathSignature];
                    }
                    else
                    {
                        // step through the path
                        bool currentObjectIsARecord = RecordPathParser.PropertyIsRecord(rootObj, commonPath, out FormKey? recordFK);
                        currentObj = RecordPathParser.GetObjectAtPath(rootObj, commonPath, objectsAtPath_NPC); //  this function needs to be updated to resolve from LinkCache if currentObjectIsARecord == true

                        // if the NPC doesn't have the given object (e.g. the NPC doesn't have a WNAM), assign in from template
                        if (currentObj == null || currentObjectIsARecord && recordFK.Value.IsNull)
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

                            currentObj = RecordPathParser.GetObjectAtPath(template, templateRelPath, objectsAtPath_Template); // get corresponding object from template NPC

                            if (currentObj == null)
                            {
                                Logger.LogError("Error: neither NPC " + npcInfo.LogIDstring + " nor the record template " + template.EditorID + " contained a record at " + templateRelPath + ". Cannot assign this record.");
                            }

                            else
                            {
                                // if the template objecet is a record, add it to the generated patch and then copy it to the NPC
                                // if the template object is just a struct (not a record), simply copy it to the NPC
                                if (currentObjectIsARecord)
                                {
                                    var templateFormKeyObj = RecordPathParser.GetObjectAtPath(currentObj, "FormKey", objectsAtPath_Template);
                                    recordTemplateLinkCache.TryResolveContext((FormKey)templateFormKeyObj, out var templateContext);
                                    var newRecord = (IMajorRecord)templateContext.DuplicateIntoAsNewRecord(outputMod); // without cast, would be an IMajorRecordCommon

                                    // increment editor ID number
                                    //var newRecord = (IMajorRecord)currentObj;
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
                            objectsAtPath_NPC.Add(prePath + commonPath, currentObj); // for next iteration of top for loop
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
                    int dbg = 0;
                }
            }



        }

        public static object GetOrAddGenericRecordAsOverride(IMajorRecordGetter recordGetter, SkyrimMod outputMod)
        {
            var getterType = LoquiRegistration.GetRegister(recordGetter.GetType()).GetterType;
            dynamic group = outputMod.GetTopLevelGroup(getterType);
            return OverrideMixIns.GetOrAddAsOverride(group, recordGetter);
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
