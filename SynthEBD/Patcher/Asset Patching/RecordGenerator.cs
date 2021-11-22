using Mutagen.Bethesda;
using Mutagen.Bethesda.Cache.Implementations;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

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

        public static void CombinationToRecords(SubgroupCombination combination, NPCInfo npcInfo, ImmutableLoadOrderLinkCache<ISkyrimMod, ISkyrimModGetter> recordTemplateLinkCache)
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
            Dictionary<string, Object> objectsAtPath = new Dictionary<string, Object>();
            objectsAtPath.Add("", template);

            for (int i = 0; i < longestPath; i++)
            {
                for (int j = 0; j < paths.Count; j++)
                {
                    if (paths[j].Destination.Length == i)
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

                    var rootObj = objectsAtPath[prePath];

                    //if commonPath points to an array specifier, convert it to an index here
                    // ex: "Armature[BodyTemplate.FirstPersonFlags.HasFlag(BipedObjectFlag.Body)]"
                    // try https://eval-expression.net/

                    //

                    // if common path points to a specified array index (e.g. Armature[0]), convert it here

                    var currentObj = RecordPathParser.GetSubObject(rootObj, commonPath);
                    if (currentObj != null)
                    {
                        objectsAtPath.Add(prePath + commonPath, currentObj);

                        if (RecordPathParser.PropertyIsRecord(currentObj))
                        {
                            pathsAtRecord.Add(currentObj, group.ToList());
                        }
                    }
                }

                /*
                var groupedPaths = new List<List<FilePathReplacementParsed>>();
                for (int i = 0; i < longestPath; i++)
                {
                    for (int j = 0; j < paths.Count; j++)
                    {
                        if (paths[j].Destination.Length == i)
                        {
                            paths.RemoveAt(j);
                            j--;
                        }
                    }

                    var groupedPathsAtI = paths.GroupBy(x => x.Destination[i]);

                    foreach (var group in groupedPathsAtI)
                    {


                        List<FilePathReplacementParsed> terminated = new List<FilePathReplacementParsed>();
                        foreach (var path in group)
                        {
                            if (path.Destination.Length == i + 1)
                            {
                                terminated.Add(path);
                            }
                        }
                        if (terminated.Any())
                        {
                            groupedPaths.Add(terminated);
                        }

                    }

                */
            }


            int dbg = 0;
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
