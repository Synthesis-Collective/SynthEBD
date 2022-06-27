using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Skyrim;

namespace SynthEBD;

public enum LogMatchType
{
    None,
    Matched,
    Unmatched,
    ForceIf
}
public class AttributeMatcher
{
    public static bool HasMatchedAttributes(HashSet<NPCAttribute> attributeList, INpcGetter npc, LogMatchType logType, out string matchLog)
    {
        return MatchNPCtoAttributeList(attributeList, npc, false, logType, out int unused, out matchLog);
    }
    public static int GetForceIfAttributeCount(HashSet<NPCAttribute> attributeList, INpcGetter npc, out string matchLog)
    {
        MatchNPCtoAttributeList(attributeList, npc, true, LogMatchType.ForceIf, out int count, out matchLog);
        return count;
    }

    /// <summary>
    /// Evaluates a list of NPCAttributes to determine if the given NPC 
    /// </summary>
    /// <param name="attributeList"></param>
    /// <param name="npc"></param>
    /// <param name="getForceIfCount"></param>
    /// <param name="matchedForceIfAttributeWeightedCount"></param>
    /// <returns></returns>
    private static bool MatchNPCtoAttributeList(HashSet<NPCAttribute> attributeList, INpcGetter npc, bool getForceIfCount, LogMatchType logType, out int matchedForceIfAttributeWeightedCount, out string matchLog)
    {
        bool matched = false;
        matchedForceIfAttributeWeightedCount = 0;
        matchLog = string.Empty;
        if (attributeList.Count == 0) { return false; }

        foreach (var attribute in attributeList)
        {
            bool subAttributeMatched = true;
            int currentAttributeForceIfWeight = 0; // for logging only
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
                        if (!EvaluateCustomAttribute(npc, customAttribute, PatcherEnvironmentProvider.Instance.Environment.LinkCache, out _))
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

                if (!subAttributeMatched) 
                {
                    if (logType == LogMatchType.Unmatched)
                    {
                        matchLog += subAttribute.ToLogString();
                    }
                    break; 
                }
                else if (subAttribute.ForceIf) { matchedForceIfAttributeWeightedCount += subAttribute.Weighting; currentAttributeForceIfWeight += subAttribute.Weighting; }
            }
            if (!subAttributeMatched) // sub attributes are treated as AND, so as soon as one isn't matched return false
            {
                continue; // evaluate the next attribute - the current attribute is not matched because one of the sub-attributes is not matched
            }
            else if (!getForceIfCount) // if the calling function only wants to know if any attributes are matched, and does not care how many of the matched attributes are ForceIf, then return true as soon as the first attribute is matched
            {
                if (logType == LogMatchType.Matched)
                {
                    matchLog = attribute.ToLogString();
                }
                return true;
            }
            else
            {
                if (logType == LogMatchType.ForceIf)
                {
                    matchLog += "\n" + attribute.ToLogString() + " (Weighting: " + currentAttributeForceIfWeight + ")";
                }
                matched = true;
            }
        }

        return matched;
    }

    public static bool EvaluateCustomAttribute(INpcGetter npc, NPCAttributeCustom attribute, ILinkCache linkCache, out string dispMessage)
    {
        var resolvedObjects = new List<dynamic>();
        bool success = RecordPathParser.GetObjectCollectionAtPath(npc, npc, attribute.Path, new Dictionary<string, dynamic>(), linkCache, true, Logger.GetNPCLogNameString(npc), resolvedObjects);
        dispMessage = "";

        bool currentTypeMatched = false;
        bool typeMatched = false;
        bool valueMatched = false;

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
                    foreach (var resolvedObject in resolvedObjects)
                    {
                        currentTypeMatched = false;
                        if (resolvedObject.GetType() == typeof(string)) { typeMatched = true; currentTypeMatched = true; }
                        if (currentTypeMatched && resolvedObject == attribute.ValueStr) { valueMatched = true; break; }
                    }

                    if (typeMatched == false)
                    {
                        dispMessage = "The value at the specified path is not a text string";
                        return false;
                    }
                    else
                    {
                        switch(attribute.Comparator)
                        {
                            case "=":
                                if (valueMatched) { return true; }
                                else { dispMessage = "The text at the specified path(s) does not match the attribute value.";  return false; }
                            case "!=":
                                if (!valueMatched) { return true; }
                                else { dispMessage = "The text at the specified path(s) matches the attribute value."; return false; }
                            default: return false;
                        }  
                    }

                case CustomAttributeType.Integer:
                    int.TryParse(attribute.ValueStr, out var iValue);
                    int iResult;
                    foreach (var resolvedObject in resolvedObjects)
                    {
                        currentTypeMatched = false;
                        if (int.TryParse(resolvedObject.ToString(), out iResult)) { typeMatched = true; currentTypeMatched = true; }
                        if (currentTypeMatched && CompareResult(iResult, iValue, attribute.Comparator, out dispMessage)) { valueMatched = true; break; }
                    }

                    if (typeMatched == false)
                    {
                        dispMessage = "The value at the specified path is not an integer.";
                        return false;
                    }
                    else
                    {
                        return valueMatched;
                    }

                case CustomAttributeType.Decimal:
                    float.TryParse(attribute.ValueStr, out var fValue);
                    float fResult;
                    foreach (var resolvedObject in resolvedObjects)
                    {
                        currentTypeMatched = false;
                        if (float.TryParse(resolvedObject.ToString(), out fResult)) { typeMatched = true; currentTypeMatched = true; }
                        if (currentTypeMatched && CompareResult(fResult, fValue, attribute.Comparator, out dispMessage)) { valueMatched = true; break; }
                    }

                    if (typeMatched == false)
                    {
                        dispMessage = "The value at the specified path is not an decimal number.";
                        return false;
                    }
                    else
                    {
                        return valueMatched;
                    }

                case CustomAttributeType.Boolean:
                    bool.TryParse(attribute.ValueStr, out var bValue);
                    bool bResult;
                    foreach (var resolvedObject in resolvedObjects)
                    {
                        currentTypeMatched = false;
                        if (bool.TryParse(resolvedObject.ToString(), out bResult)) { typeMatched = true; currentTypeMatched = true; }
                        if (currentTypeMatched && bValue == bResult) { valueMatched = true; break; }
                    }

                    if (typeMatched == false)
                    {
                        dispMessage = "The value at the specified path is not a Boolean value.";
                        return false;
                    }
                    else
                    {
                        switch (attribute.Comparator)
                        {
                            case "=":
                                if (valueMatched) { return true; }
                                else { dispMessage = "The value at the specified path is not " + attribute.ValueStr; return false; }
                            case "!=":
                                if (!valueMatched) { return true; }
                                else { dispMessage = "The value at the specified path is not " + attribute.ValueStr; return false; }
                            default: return false;
                        }
                    }

                case CustomAttributeType.Record:
                    foreach (var resolvedObject in resolvedObjects)
                    {
                        currentTypeMatched = false;
                        if (RecordPathParser.ObjectHasFormKey(resolvedObject)) { typeMatched = true; currentTypeMatched = true; }
                        if(currentTypeMatched && FormKeyHashSetComparer.Contains(attribute.ValueFKs, resolvedObject.FormKey)) { valueMatched = true; break; }
                    }

                    if (typeMatched == false)
                    {
                        dispMessage = "The value(s) at the specified paths are not records.";
                        return false;
                    }
                    else
                    {
                        switch (attribute.Comparator)
                        {
                            case "=":
                                if (valueMatched) { return true; }
                                else { dispMessage = "The record(s) at the specified path do not match the selected value(s)."; return false; }
                            case "!=":
                                if (!valueMatched) { return true; }
                                else { dispMessage = "The record(s) at the specified path match the selected value(s)."; return false; }
                            default: return false;
                        }
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