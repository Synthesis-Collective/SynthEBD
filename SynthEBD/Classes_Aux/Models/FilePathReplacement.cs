using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Skyrim;

namespace SynthEBD;

public class FilePathReplacement
{
    public string Source { get; set; } = "";
    public string Destination { get; set; } = "";
}
    
public class FilePathReplacementParsed
{
    private Logger _logger;
    public FilePathReplacementParsed(FilePathReplacement pathTemplate, NPCInfo npcInfo, FlattenedAssetPack sourceAssetPack, ILinkCache<ISkyrimMod, ISkyrimModGetter> recordTemplateLinkCache, Logger logger)
    {
        _logger = logger;

        Source = pathTemplate.Source;
        Destination = RecordPathParser.SplitPath(pathTemplate.Destination);
        DestinationStr = pathTemplate.Destination;
        TemplateNPC = GetTemplateNPC(npcInfo, sourceAssetPack, recordTemplateLinkCache);
        AssetPackName = sourceAssetPack.GroupName;
    }

    public string Source { get; set; }
    public string[] Destination { get; set; }
    public string DestinationStr { get; set; }
    public INpcGetter TemplateNPC { get; set; }
    public HashSet<GeneratedRecordInfo> TraversedRecords { get; set; } = new(); // for logging only
    public string AssetPackName { get; set; }  // for logging only

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
}