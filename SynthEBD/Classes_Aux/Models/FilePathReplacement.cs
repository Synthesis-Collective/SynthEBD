using Mutagen.Bethesda.Plugins.Cache.Internals.Implementations;
using Mutagen.Bethesda.Skyrim;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace SynthEBD
{
    public class FilePathReplacement
    {
        public FilePathReplacement()
        {
            this.Source = "";
            this.Destination = "";
        }

        public string Source { get; set; }
        public string Destination { get; set; }
    }
    
    public class FilePathReplacementParsed
    {
        public FilePathReplacementParsed(FilePathReplacement pathTemplate, NPCInfo npcInfo, FlattenedAssetPack sourceAssetPack, ImmutableLoadOrderLinkCache<ISkyrimMod, ISkyrimModGetter> recordTemplateLinkCache)
        {
            this.Source = pathTemplate.Source;
            this.Destination = RecordPathParser.SplitPath(pathTemplate.Destination);
            this.DestinationStr = pathTemplate.Destination;
            this.TemplateNPC = RecordGenerator.GetTemplateNPC(npcInfo, sourceAssetPack, recordTemplateLinkCache);
        }

        public string Source { get; set; }
        public string[] Destination { get; set; }
        public string DestinationStr { get; set; }
        public INpcGetter TemplateNPC { get; set; }
    }
}
