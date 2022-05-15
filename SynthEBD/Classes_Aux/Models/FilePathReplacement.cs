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
    public FilePathReplacementParsed(FilePathReplacement pathTemplate, NPCInfo npcInfo, FlattenedAssetPack sourceAssetPack, SubgroupCombination parentCombination)
    {
        this.Source = pathTemplate.Source;
        this.Destination = RecordPathParser.SplitPath(pathTemplate.Destination);
        this.DestinationStr = pathTemplate.Destination;
        this.TemplateNPC = GetTemplateNPC(npcInfo, sourceAssetPack);
        this.ParentCombination = parentCombination;
    }

    public string Source { get; set; }
    public string[] Destination { get; set; }
    public string DestinationStr { get; set; }
    public INpcGetter TemplateNPC { get; set; }
    public SubgroupCombination ParentCombination { get; set; } // for logging only
    public HashSet<GeneratedRecordInfo> TraversedRecords { get; set; } = new(); // for logging only

    private static INpcGetter GetTemplateNPC(NPCInfo npcInfo, FlattenedAssetPack chosenAssetPack)
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
            
        if (!Patcher.MainLinkCache.TryResolve<INpcGetter>(templateFK, out var templateNPC) && chosenAssetPack.Type != FlattenedAssetPack.AssetPackType.ReplacerVirtual)
        {
            Logger.LogError("Error: Cannot resolve template NPC with FormKey " + templateFK.ToString());
            Logger.SwitchViewToLogDisplay();
            return null;
        }
        else
        {
            return templateNPC;
        }
    }
}