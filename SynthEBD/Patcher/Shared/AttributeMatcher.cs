using Mutagen.Bethesda.Skyrim;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SynthEBD
{
    public class AttributeMatcher
    {
        public static bool HasMatchedAttributes(HashSet<NPCAttribute> attributeList, INpcGetter npc)
        {
            return GetNPCMatchedAttributes(attributeList, npc, false) > 0;
        }
        public static int GetMatchedAttributeCount(HashSet<NPCAttribute> attributeList, INpcGetter npc)
        {
            return GetNPCMatchedAttributes(attributeList, npc, true);
        }

        private static int GetNPCMatchedAttributes(HashSet<NPCAttribute> attributeList, INpcGetter npc, bool getMatchCount)
        {
            int matchCount = 0;
            foreach (var attribute in attributeList)
            {
                bool allSubAttributesMatched = true;
                foreach (var subAttribute in attribute.GroupedSubAttributes)
                {
                    switch(subAttribute.Type)
                    {
                        case NPCAttributeType.Class:
                            var classAttribute = (NPCAttributeClass)subAttribute;
                            if (!classAttribute.FormKeys.Contains(npc.Class.FormKey))
                            {
                                allSubAttributesMatched = false;
                            }
                            else
                            {
                                bool debug = true;
                            }
                            break;
                        case NPCAttributeType.Faction:
                            var factionAttribute = (NPCAttributeFactions)subAttribute;
                            foreach (var factionFK in factionAttribute.FormKeys)
                            {
                                bool factionMatched = false;
                                foreach (var npcFaction in npc.Factions)
                                {
                                    if (npcFaction.Faction.FormKey.Equals(factionFK) && npcFaction.Rank > factionAttribute.RankMin && npcFaction.Rank < factionAttribute.RankMax)
                                    {
                                        factionMatched = true;
                                        break;
                                    }
                                }
                                if (!factionMatched)
                                {
                                    allSubAttributesMatched = false;
                                }
                            }
                            break;
                        case NPCAttributeType.FaceTexture:
                            var faceTextureAttribute = (NPCAttributeFaceTexture)subAttribute;
                            if (!faceTextureAttribute.FormKeys.Contains(npc.HeadTexture.FormKey))
                            {
                                allSubAttributesMatched = false;
                            }
                            break;
                        case NPCAttributeType.NPC:
                            var npcAttribute = (NPCAttributeNPC)subAttribute;
                            if (!npcAttribute.FormKeys.Contains(npc.FormKey))
                            {
                                allSubAttributesMatched = false;
                            }
                            break;
                        case NPCAttributeType.Race:
                            var npcAttributeRace = (NPCAttributeRace)subAttribute;
                            if (!npcAttributeRace.FormKeys.Contains(npc.Race.FormKey))
                            {
                                allSubAttributesMatched = false;
                            }
                            break;
                        case NPCAttributeType.VoiceType:
                            var npcAttributeVT = (NPCAttributeVoiceType)subAttribute;
                            if (!npcAttributeVT.FormKeys.Contains(npc.Voice.FormKey))
                            {
                                allSubAttributesMatched = false;
                            }
                            break;
                    }
                    if (allSubAttributesMatched == false) { break; }
                }
                if (allSubAttributesMatched)
                {
                    switch (getMatchCount)
                    {
                        case false: return 1;
                        case true: matchCount++; break;
                    }
                }
            }

            return matchCount;
        }
    }
}
