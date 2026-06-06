using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Skyrim;

namespace SynthEBD;

/// <summary>A source→destination file-path replacement template (raw strings, as configured by the user).</summary>
public class FilePathReplacement
{
    public string Source { get; set; } = "";
    public string Destination { get; set; } = "";
}
    
/// <summary>
/// A <see cref="FilePathReplacement"/> resolved for a specific NPC and asset pack: the destination path
/// is pre-split into segments, the template NPC is resolved, and traversal info is captured for logging.
/// </summary>
public class FilePathReplacementParsed
{
    private Logger _logger;
    /// <summary>Builds a parsed replacement by splitting the destination path and resolving the template NPC.</summary>
    /// <param name="pathTemplate">The raw source/destination template.</param>
    /// <param name="npcInfo">The NPC being patched.</param>
    /// <param name="sourceAssetPack">The asset pack supplying the replacement and its record templates.</param>
    /// <param name="recordTemplateLinkCache">Link cache over the record-template plugins.</param>
    /// <param name="logger">Logger for resolution errors.</param>
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

    /// <summary>Resolves the template NPC for this NPC: a race-specific additional template if one matches, otherwise the asset pack's default template.</summary>
    /// <param name="npcInfo">The NPC being patched.</param>
    /// <param name="chosenAssetPack">The asset pack supplying the templates.</param>
    /// <param name="recordTemplateLinkCache">Link cache over the record-template plugins.</param>
    /// <returns>The resolved template NPC, or null if it cannot be resolved (logged as an error unless the pack is a virtual replacer).</returns>
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