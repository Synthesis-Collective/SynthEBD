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
        public bool Equals(NPCAttribute other)
        {
            var thisArray = this.GroupedSubAttributes.ToArray();
            var otherArray = other.GroupedSubAttributes.ToArray();
            if (thisArray.Length != otherArray.Length) { return false; }
            else
            {
                for (int i = 0; i < thisArray.Length; i++)
                {
                    if (thisArray[i].Type != otherArray[i].Type) { return false; }
                    if (!thisArray[i].Equals(otherArray[i])) { return false; }
                }
            }
            return true;
        }
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
            this.ForceIf = false;
        }
        public HashSet<FormKey> FormKeys { get; set; }
        public NPCAttributeType Type { get; set; }
        public bool ForceIf { get; set; }
        public bool Equals(ITypedNPCAttribute other)
        {
            var otherTyped = (NPCAttributeVoiceType)other;
            if (this.Type == other.Type && FormKeyHashSetComparer.Equals(this.FormKeys, otherTyped.FormKeys)) { return true; }
            return false;
        }
    }

    public class NPCAttributeClass : ITypedNPCAttribute
    {
        public NPCAttributeClass()
        {
            this.FormKeys = new HashSet<FormKey>();
            this.Type = NPCAttributeType.Class;
            this.ForceIf = false;
        }
        public HashSet<FormKey> FormKeys { get; set; }
        public NPCAttributeType Type { get; set; }
        public bool ForceIf { get; set; }
        public bool Equals(ITypedNPCAttribute other)
        {
            var otherTyped = (NPCAttributeClass)other;
            if (this.Type == other.Type && FormKeyHashSetComparer.Equals(this.FormKeys, otherTyped.FormKeys)) { return true; }
            return false;
        }
    }

    public class NPCAttributeFactions : ITypedNPCAttribute
    {
        public NPCAttributeFactions()
        {
            this.FormKeys = new HashSet<FormKey>();
            this.RankMin = -1;
            this.RankMax = 100;
            this.Type = NPCAttributeType.Faction;
            this.ForceIf = false;
        }
        public HashSet<FormKey> FormKeys { get; set; }
        public int RankMin { get; set; }
        public int RankMax { get; set; }
        public NPCAttributeType Type { get; set; }
        public bool ForceIf { get; set; }
        public bool Equals(ITypedNPCAttribute other)
        {
            var otherTyped = (NPCAttributeFactions)other;
            if (this.Type == other.Type && FormKeyHashSetComparer.Equals(this.FormKeys, otherTyped.FormKeys) && this.RankMin == otherTyped.RankMin && this.RankMax == otherTyped.RankMax) { return true; }

            return false;
        }
    }

    public class NPCAttributeFaceTexture : ITypedNPCAttribute
    {
        public NPCAttributeFaceTexture()
        {
            this.FormKeys = new HashSet<FormKey>();
            this.Type = NPCAttributeType.FaceTexture;
            this.ForceIf = false;
        }
        public HashSet<FormKey> FormKeys { get; set; }
        public NPCAttributeType Type { get; set; }
        public bool ForceIf { get; set; }
        public bool Equals(ITypedNPCAttribute other)
        {
            var otherTyped = (NPCAttributeFaceTexture)other;
            if (this.Type == other.Type && FormKeyHashSetComparer.Equals(this.FormKeys, otherTyped.FormKeys)) { return true; }
            return false;
        }
    }

    public class NPCAttributeRace : ITypedNPCAttribute
    {
        public NPCAttributeRace()
        {
            this.FormKeys = new HashSet<FormKey>();
            this.Type = NPCAttributeType.Race;
            this.ForceIf = false;
        }
        public HashSet<FormKey> FormKeys { get; set; }
        public NPCAttributeType Type { get; set; }
        public bool ForceIf { get; set; }
        public bool Equals(ITypedNPCAttribute other)
        {
            var otherTyped = (NPCAttributeRace)other;
            if (this.Type == other.Type && FormKeyHashSetComparer.Equals(this.FormKeys, otherTyped.FormKeys)) { return true; }
            return false;
        }
    }

    public class NPCAttributeNPC : ITypedNPCAttribute
    {
        public NPCAttributeNPC()
        {
            this.FormKeys = new HashSet<FormKey>();
            this.Type = NPCAttributeType.NPC;
            this.ForceIf = false;
        }
        public HashSet<FormKey> FormKeys { get; set; }
        public NPCAttributeType Type { get; set; }
        public bool ForceIf { get; set; }
        public bool Equals(ITypedNPCAttribute other)
        {
            var otherTyped = (NPCAttributeNPC)other;
            if (this.Type == other.Type && FormKeyHashSetComparer.Equals(this.FormKeys, otherTyped.FormKeys)) { return true; }
            return false;
        }
    }

    public interface ITypedNPCAttribute
    {
        NPCAttributeType Type { get; set; }
        bool Equals(ITypedNPCAttribute other);
        public bool ForceIf { get; set; }
    }
}
