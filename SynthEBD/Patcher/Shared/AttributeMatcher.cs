using Mutagen.Bethesda.Plugins.Cache;
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
            return MatchNPCtoAttributeList(attributeList, npc, false, out int unused);
        }
        public static int GetForceIfAttributeCount(HashSet<NPCAttribute> attributeList, INpcGetter npc)
        {
            MatchNPCtoAttributeList(attributeList, npc, true, out int count);
            return count;
        }

        /// <summary>
        /// Evaluates a list of NPCAttributes to determine if the given NPC 
        /// </summary>
        /// <param name="attributeList"></param>
        /// <param name="npc"></param>
        /// <param name="getForceIfCount"></param>
        /// <param name="matchedForceIfAttributeCount"></param>
        /// <returns></returns>
        private static bool MatchNPCtoAttributeList(HashSet<NPCAttribute> attributeList, INpcGetter npc, bool getForceIfCount, out int matchedForceIfAttributeCount)
        {
            bool matched = false;
            matchedForceIfAttributeCount = 0;
            if (attributeList.Count == 0) { return false; }

            foreach (var attribute in attributeList)
            {
                bool subAttributeMatched = true;
                foreach (var subAttribute in attribute.SubAttributes)
                {
                    switch(subAttribute.Type)
                    {
                        case NPCAttributeType.Class:
                            var classAttribute = (NPCAttributeClass)subAttribute;
                            if (!classAttribute.FormKeys.Contains(npc.Class.FormKey))
                            {
                                subAttributeMatched = false;
                            }
                            break;

                        case NPCAttributeType.Custom:
                            var customAttribute = (NPCAttributeCustom)subAttribute;
                            if (!EvaluateCustomAttribute(npc, customAttribute, Patcher.MainLinkCache, out _))
                            {
                                subAttributeMatched = false;
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
                                    subAttributeMatched = false;
                                }
                            }
                            break;
                        case NPCAttributeType.FaceTexture:
                            var faceTextureAttribute = (NPCAttributeFaceTexture)subAttribute;
                            if (!faceTextureAttribute.FormKeys.Contains(npc.HeadTexture.FormKey))
                            {
                                subAttributeMatched = false;
                            }
                            break;
                        case NPCAttributeType.NPC:
                            var npcAttribute = (NPCAttributeNPC)subAttribute;
                            if (!npcAttribute.FormKeys.Contains(npc.FormKey))
                            {
                                subAttributeMatched = false;
                            }
                            break;
                        case NPCAttributeType.Race:
                            var npcAttributeRace = (NPCAttributeRace)subAttribute;
                            if (!npcAttributeRace.FormKeys.Contains(npc.Race.FormKey))
                            {
                                subAttributeMatched = false;
                            }
                            break;
                        case NPCAttributeType.VoiceType:
                            var npcAttributeVT = (NPCAttributeVoiceType)subAttribute;
                            if (!npcAttributeVT.FormKeys.Contains(npc.Voice.FormKey))
                            {
                                subAttributeMatched = false;
                            }
                            break;
                    }
                    if (subAttributeMatched == false) { break; }
                    else if (subAttribute.ForceIf) { matchedForceIfAttributeCount++; }
                }
                if (!subAttributeMatched) // sub attributes are treated as AND, so as soon as one isn't matched return false
                {
                    continue; // evaluate the next attribute - the current attribute is not matched because one of the sub-attributes is not matched
                }
                else if (!getForceIfCount) // if the calling function only wants to know if any attributes are matched, and does not care how many of the matched attributes are ForceIf, then return true as soon as the first attribute is matched
                {
                    return true;
                }
                else
                {
                    matched = true;
                }
            }

            return matched;
        }

        public static bool EvaluateCustomAttribute(INpcGetter npc, NPCAttributeCustom attribute, ILinkCache<ISkyrimMod, ISkyrimModGetter> linkCache, out string dispMessage)
        {
            bool success = RecordPathParser.GetObjectAtPath(npc, attribute.Path, new Dictionary<dynamic, Dictionary<string, dynamic>>(), linkCache, out dynamic resolvedObject);
            dispMessage = "";

            if (!success) 
            {
                dispMessage = "NPC does not have an object at this path";
                return false; 
            }
            else
            {
                switch(attribute.CustomType)
                {
                    case CustomAttributeType.Text: 
                        if (resolvedObject.GetType() != typeof(string))
                        {
                            dispMessage = "The value at the specified path is not a text string";
                            return false;
                        }
                        else if (resolvedObject != attribute.ValueStr)
                        {
                            dispMessage = "The value at the specified path does not match the attribute value.";
                            return false;
                        }
                        else
                        {
                            return true;
                        }

                    case CustomAttributeType.Integer:
                        int.TryParse(attribute.ValueStr, out var iValue);
                        if (!int.TryParse(resolvedObject.ToString(), out int iResult))
                        {
                            dispMessage = "The value at the specified path is not an integer";
                            return false;
                        }
                        else if (!CompareResult(iResult, iValue, attribute.Comparator, out dispMessage))
                        {
                            return false;
                        }
                        else
                        {
                            return true;
                        }

                    case CustomAttributeType.Decimal:
                        float.TryParse(attribute.ValueStr, out var fValue);
                        if (!float.TryParse(resolvedObject.ToString(), out float fResult))
                        {
                            dispMessage = "The value at the specified path is not an decimal";
                            return false;
                        }
                        else if (!CompareResult(fResult, fValue, attribute.Comparator, out dispMessage))
                        {
                            dispMessage = "The decimal at the specified path does not match the attribute value.";
                            return false;
                        }
                        else
                        {
                            return true;
                        }

                    case CustomAttributeType.FormKey:
                        if (!RecordPathParser.ObjectHasFormKey(resolvedObject))
                        {
                            dispMessage = "The value at the specified path is not a record.";
                            return false;
                        }
                        else if (!FormKeyHashSetComparer.Contains(attribute.ValueFKs, resolvedObject.FormKey))
                        {
                            dispMessage = "The value at the specified path is not one of the specified FormKey(s)";
                            return false;
                        }
                        else
                        {
                            return true;
                        }
                    default: return false;
                }
            }
        }

        private static bool CompareResult(int toEval, int comparison, string comparator, out string dispMessage)
        {
            dispMessage = "";
            switch(comparator)
            {
                case "=":
                    if (toEval != comparison) { dispMessage = "The integer at the specified path does not equal the attribute value."; return false; }
                    else { return true; }
                case "!=":
                    if (toEval == comparison) { dispMessage = "The integer at the specified path does not NOT equal the attribute value."; return false; }
                    else { return true; }
                case "<":
                    if (!(toEval < comparison)) { dispMessage = "The integer at the specified path is not less than the attribute value."; return false; }
                    else { return true; }
                case ">":
                    if (!(toEval > comparison)) { dispMessage = "The integer at the specified path is not greater than the attribute value."; return false; }
                    else { return true; }
                case "<=":
                    if (!(toEval <= comparison)) { dispMessage = "The integer at the specified path is not less than or equal to the attribute value."; return false; }
                    else { return true; }
                case ">=":
                    if (!(toEval >= comparison)) { dispMessage = "The integer at the specified path is not greater than or equal to the attribute value."; return false; }
                    else { return true; }
                default:
                    dispMessage = "Comparator not recognized"; return false;
            }
        }

        private static bool CompareResult(float toEval, float comparison, string comparator, out string dispMessage)
        {
            dispMessage = "";
            switch (comparator)
            {
                case "=":
                    if (toEval != comparison) { dispMessage = "The decimal at the specified path does not equal the attribute value."; return false; }
                    else { return true; }
                case "!=":
                    if (toEval == comparison) { dispMessage = "The decimal at the specified path does not NOT equal the attribute value."; return false; }
                    else { return true; }
                case "<":
                    if (!(toEval < comparison)) { dispMessage = "The decimal at the specified path is not less than the attribute value."; return false; }
                    else { return true; }
                case ">":
                    if (!(toEval > comparison)) { dispMessage = "The decimal at the specified path is not greater than the attribute value."; return false; }
                    else { return true; }
                case "<=":
                    if (!(toEval <= comparison)) { dispMessage = "The decimal at the specified path is not less than or equal to the attribute value."; return false; }
                    else { return true; }
                case ">=":
                    if (!(toEval >= comparison)) { dispMessage = "The decimal at the specified path is not greater than or equal to the attribute value."; return false; }
                    else { return true; }
                default:
                    dispMessage = "Comparator not recognized"; return false;
            }
        }
    }
}
