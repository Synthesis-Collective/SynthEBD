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

            Dictionary<Object, List<FilePathReplacementParsed>> pathsAtRecord = new Dictionary<object, List<FilePathReplacementParsed>>();

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

                    // if this is the last part of the path, attempt to assign the Source asset to the Destination
                    if (group.First().Source.Length == i - 1)
                    {
                        foreach (var assetAssignment in group)
                        {
                            rootObj = assetAssignment.Source;
                        }
                        continue;
                    }

                    // otherwise step through the path
                    bool currentObjectIsARecord = RecordPathParser.PropertyIsRecord(rootObj, commonPath, out FormKey? recordFK);
                    currentObj = RecordPathParser.GetObjectAtPath(rootObj, commonPath, objectsAtPath_NPC);
                    // if the NPC doesn't have the given object (e.g. the NPC doesn't have a WNAM), assign in from template
                    if (currentObj == null || currentObjectIsARecord && recordFK == null)
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
                                var templateRecord = (IMajorRecordGetter)currentObj;
                                currentObj = AddTemplateToPatch(templateRecord, outputMod);
                                // increment editor ID number
                                var newRecord = (IMajorRecord)currentObj;
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
                            }
                            RecordPathParser.SetSubObject(rootObj, templateRelPath, currentObj);
                        }
                    }

                    // store paths associated with this record for future lookup
                    if (currentObj != null)
                    {
                        objectsAtPath_Template.Add(prePath + commonPath, currentObj);

                        if (RecordPathParser.PropertyIsRecord(currentObj))
                        {
                            pathsAtRecord.Add(currentObj, group.ToList());
                        }
                    }
                }
            }


            int dbg = 0;
        }

        public static object AddTemplateToPatch(IMajorRecordGetter templateRecord, SkyrimMod outputMod)
        {
            var getterType = LoquiRegistration.GetRegister(templateRecord.GetType()).GetterType;
            dynamic group = outputMod.GetTopLevelGroup(getterType);
            return OverrideMixIns.GetOrAddAsOverride(group, templateRecord);
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
