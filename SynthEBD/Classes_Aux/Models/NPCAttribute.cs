using Mutagen.Bethesda.Plugins;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SynthEBD
{
    public class NPCAttribute
    {
        public NPCAttribute()
        {
            this.Path = "";
            this.Value = "";
        }

        public string Path { get; set; }
        public string Value { get; set; }
    }

    public class NPCAttributeVoiceType
    {
        public NPCAttributeVoiceType()
        {
            this.VoiceTypeFormKey = new FormKey();
        }
        public FormKey VoiceTypeFormKey { get; set; }
    }

    public class NPCAttributeClass
    {
        public NPCAttributeClass()
        {
            this.ClassFormKey = new FormKey();
        }
        public FormKey ClassFormKey { get; set; }
    }

    public class NPCAttributeFactions
    {
        public NPCAttributeFactions()
        {
            this.FactionFormKeys = new HashSet<FormKey>();
            this.RankMin = -1;
            this.RankMax = 100;
        }
        public HashSet<FormKey> FactionFormKeys { get; set; }
        public int RankMin { get; set; }
        public int RankMax { get; set; }
    }

    public class NPCAttributeFaceTexture
    {
        public NPCAttributeFaceTexture()
        {
            this.FaceTextureFormKey = new FormKey();
        }
        public FormKey FaceTextureFormKey { get; set; }
    }

    public class NPCAttributeRace
    {
        public NPCAttributeRace()
        {
            this.RaceFormKey = new FormKey();
        }
        public FormKey RaceFormKey { get; set; }
    }

    public class NPCAttributeNPC
    {
        public NPCAttributeNPC()
        {
            this.NPCFormKey = new FormKey();
        }
        public FormKey NPCFormKey { get; set; }
    }
}
