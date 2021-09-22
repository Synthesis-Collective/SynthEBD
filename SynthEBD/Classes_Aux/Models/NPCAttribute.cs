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
            this.GroupedSubAttributes = new HashSet<NPCAttributeShell>();
        }

        public HashSet<NPCAttributeShell> GroupedSubAttributes { get; set; }
    }

    public enum NPCAttributeType
    {
        
        Class,
        Faction,
        FaceTexture,
        NPC,
        Race,
        VoiceType
    }

    public class NPCAttributeShell
    {
       public NPCAttributeShell()
        {
            this.Type = NPCAttributeType.Class;
            this.Attribute = new NPCAttributeClass();
        }
        public object Attribute { get; set; }
        public NPCAttributeType Type { get; set; }
    }

    public class NPCAttributeVoiceType
    {
        public NPCAttributeVoiceType()
        {
            this.VoiceTypeFormKeys = new HashSet<FormKey>();
        }
        public HashSet<FormKey> VoiceTypeFormKeys { get; set; }
    }

    public class NPCAttributeClass
    {
        public NPCAttributeClass()
        {
            this.ClassFormKeys = new HashSet<FormKey>();
        }
        public HashSet<FormKey> ClassFormKeys { get; set; }
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
            this.FaceTextureFormKeys = new HashSet<FormKey>();
        }
        public HashSet<FormKey> FaceTextureFormKeys { get; set; }
    }

    public class NPCAttributeRace
    {
        public NPCAttributeRace()
        {
            this.RaceFormKeys = new HashSet<FormKey>();
        }
        public HashSet<FormKey> RaceFormKeys { get; set; }
    }

    public class NPCAttributeNPC
    {
        public NPCAttributeNPC()
        {
            this.NPCFormKeys = new HashSet<FormKey>();
        }
        public HashSet<FormKey> NPCFormKeys { get; set; }
    }
}
