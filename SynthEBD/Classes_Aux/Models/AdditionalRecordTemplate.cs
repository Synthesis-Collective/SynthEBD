using Mutagen.Bethesda.Plugins;

namespace SynthEBD
{
    public class AdditionalRecordTemplate
    {
        public AdditionalRecordTemplate()
        {
            this.Races = new HashSet<FormKey>();
            this.TemplateNPC = new FormKey();
            this.AdditionalRacesPaths = new HashSet<string>();
        }

        public HashSet<FormKey> Races { get; set; }
        public FormKey TemplateNPC { get; set; }
        public HashSet<string> AdditionalRacesPaths { get; set; }
    }
}
