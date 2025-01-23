using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Skyrim;
using Mutagen.Bethesda.Synthesis;

namespace SynthEBD;

public class FilePathReplacement
{
    public string Source { get; set; } = "";
    public string Destination { get; set; } = "";
}
    
public class FilePathReplacementParsed
{
    private Logger _logger;
    public FilePathReplacementParsed(FilePathReplacement pathTemplate, NPCInfo npcInfo, FlattenedAssetPack sourceAssetPack, ILinkCache<ISkyrimMod, ISkyrimModGetter> recordTemplateLinkCache, string parentCombinationSignature, Logger logger)
    {
        _logger = logger;

        Source = pathTemplate.Source;
        Destination = RecordPathParser.SplitPath(pathTemplate.Destination);
        DestinationStr = pathTemplate.Destination;
        TemplateNPC = GetTemplateNPC(npcInfo, sourceAssetPack, recordTemplateLinkCache);
        ParentCombinationSignature = parentCombinationSignature;
        AssetPackName = sourceAssetPack.GroupName;
    }

    public string Source { get; set; }
    public string[] Destination { get; set; }
    public string DestinationStr { get; set; }
    public INpcGetter TemplateNPC { get; set; }
    public string ParentCombinationSignature { get; set; } // for logging only
    public string AssetPackName { get; set; } // also for logging
    public HashSet<GeneratedRecordInfo> TraversedRecords { get; set; } = new(); // for logging only

    private INpcGetter GetTemplateNPC(NPCInfo npcInfo, FlattenedAssetPack chosenAssetPack, ILinkCache<ISkyrimMod, ISkyrimModGetter> recordTemplateLinkCache)
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
            
        if (!recordTemplateLinkCache.TryResolve<INpcGetter>(templateFK, out var templateNPC) && chosenAssetPack.Type != FlattenedAssetPack.AssetPackType.ReplacerVirtual)
        {
            _logger.LogError("Error: Cannot resolve template NPC with FormKey " + templateFK.ToString());
            return null;
        }
        else
        {
            return templateNPC;
        }
    }

    public static (List<FilePathReplacementParsed>, int) CombinationToPaths(SubgroupCombination combination, NPCInfo npcInfo, PatcherState patcherState, Logger logger)
    {
        List<FilePathReplacementParsed> paths = new List<FilePathReplacementParsed>();

        int longestPathLength = 0;
        foreach (var subgroup in combination.ContainedSubgroups)
        {
            foreach (var path in subgroup.Paths)
            {
                var parsed = new FilePathReplacementParsed(path, npcInfo, combination.AssetPack, patcherState.RecordTemplateLinkCache, combination.Signature, logger);

                if (!patcherState.TexMeshSettings.bChangeNPCTextures && path.Source.EndsWith(".dds", StringComparison.OrdinalIgnoreCase)) { continue; }
                if (!patcherState.TexMeshSettings.bChangeNPCMeshes && path.Source.EndsWith(".nif", StringComparison.OrdinalIgnoreCase)) { continue; }

                paths.Add(parsed);
                if (parsed.Destination.Length > longestPathLength)
                {
                    longestPathLength = parsed.Destination.Length;
                }
            }
        }

        return (paths, longestPathLength);
    }
}