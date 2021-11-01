using Mutagen.Bethesda.Plugins;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SynthEBD
{
    // Each NPCAttribute within a HashSet<NPC> Attribute is treated with OR logic; i.e. if an NPC matches ANY of the NPCAttributes, the NPCAttribute's parent object can be assigned to the NPC
    public class NPCAttribute
    {
        public NPCAttribute()
        {
            this.GroupedSubAttributes = new HashSet<ITypedNPCAttribute>(); // Each NPCAttributeShell is treated with AND logic; i.e. the NPC must match ALL of the GroupedSubAttributes for the parent object to be assigned to the NPC.
        }

        public HashSet<ITypedNPCAttribute> GroupedSubAttributes { get; set; }
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

    public class NPCAttributeVoiceType : ITypedNPCAttribute
    {
        public NPCAttributeVoiceType()
        {
            this.FormKeys = new HashSet<FormKey>();
            this.Type = NPCAttributeType.VoiceType;
        }
        public HashSet<FormKey> FormKeys { get; set; }
        public NPCAttributeType Type { get; set; }
    }

    public class NPCAttributeClass : ITypedNPCAttribute
    {
        public NPCAttributeClass()
        {
            this.FormKeys = new HashSet<FormKey>();
            this.Type = NPCAttributeType.Class;
        }
        public HashSet<FormKey> FormKeys { get; set; }
        public NPCAttributeType Type { get; set; }
    }

    public class NPCAttributeFactions : ITypedNPCAttribute
    {
        public NPCAttributeFactions()
        {
            this.FormKeys = new HashSet<FormKey>();
            this.RankMin = -1;
            this.RankMax = 100;
            this.Type = NPCAttributeType.Faction;
        }
        public HashSet<FormKey> FormKeys { get; set; }
        public int RankMin { get; set; }
        public int RankMax { get; set; }
        public NPCAttributeType Type { get; set; }
    }

    public class NPCAttributeFaceTexture : ITypedNPCAttribute
    {
        public NPCAttributeFaceTexture()
        {
            this.FormKeys = new HashSet<FormKey>();
            this.Type = NPCAttributeType.FaceTexture;
        }
        public HashSet<FormKey> FormKeys { get; set; }
        public NPCAttributeType Type { get; set; }
    }

    public class NPCAttributeRace : ITypedNPCAttribute
    {
        public NPCAttributeRace()
        {
            this.FormKeys = new HashSet<FormKey>();
            this.Type = NPCAttributeType.Race;
        }
        public HashSet<FormKey> FormKeys { get; set; }
        public NPCAttributeType Type { get; set; }
    }

    public class NPCAttributeNPC : ITypedNPCAttribute
    {
        public NPCAttributeNPC()
        {
            this.FormKeys = new HashSet<FormKey>();
            this.Type = NPCAttributeType.NPC;
        }
        public HashSet<FormKey> FormKeys { get; set; }
        public NPCAttributeType Type { get; set; }
    }

    public interface ITypedNPCAttribute
    {
        NPCAttributeType Type { get; set; }
    }
}
