using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Skyrim;

namespace SynthEBD;

public class AttributeMatcher
{
    private readonly IEnvironmentStateProvider _environmentProvider;
    private readonly PatcherState _patcherState;
    private readonly Logger _logger;
    private readonly RecordPathParser _recordPathParser;
    public AttributeMatcher(IEnvironmentStateProvider environmentProvider, PatcherState patcherState, Logger logger, RecordPathParser recordPathParser)
    {
        _environmentProvider = environmentProvider;
        _patcherState = patcherState;
        _logger = logger;
        _recordPathParser = recordPathParser;
    }

    /// <summary>
    ///  Evaluates a list of NPCAttributes to determine if the given NPC matches any. Note that attributes of ForceType "Restrict" or "ForceIfAndRestrict" must be matched, while ForceType "ForceIf" does not need to be matched
    /// </summary>
    /// <param name="attributeList">List to attributes against which the NPC is to be compared</param>
    /// <param name="npc">NPC to be compared</param>
    /// <param name="attributeGroups">Collection of group-type attribute definitions that will be recursively evaluated if a Group-type attribute is supplied</param>
    /// <param name="hasAttributeRestrictions">Output: Does the attribute list contain attributes of type "Restrict" or "ForceIfAndRestrict" </param>
    /// <param name="matchesAttributeRestrictions">Output: Does the NPC match any attributes of type "Restrict" or "ForceIfAndRestrict"</param>
    /// <param name="matchedForceIfAttributeWeightedCount">"Output: Cumulative weighting of matched ForceIf attributes"</param>
    /// <param name="matchLog">"Output: Log of matched Attributes</param>
    /// <param name="unmatchedLog">"Output: Log of unmatched Attributes</param>
    /// <param name="unmatchedLog">"Output: Log of matched ForceIf Attributes and their respective weights</param>
    /// <param name="overrideForceIf">"overrideForceIf: if not null, overrides the AttributeForcing on all sub-attributes (allows Group type attributes to forward its own forcing to recursive calls)</param>
    public void MatchNPCtoAttributeList(HashSet<NPCAttribute> attributeList, INpcGetter npc, HashSet<AttributeGroup> attributeGroups, out bool hasAttributeRestrictions, out bool matchesAttributeRestrictions, out int matchedForceIfAttributeWeightedCount, out string matchLog, out string unmatchedLog, out string forceIfLog, AttributeForcing? overrideForceIf)
    {
        hasAttributeRestrictions = false;
        matchesAttributeRestrictions = false;
        matchedForceIfAttributeWeightedCount = 0;
        matchLog = string.Empty;
        unmatchedLog = string.Empty;
        forceIfLog = string.Empty;
        if (attributeList.Count == 0) { return; }
        int groupWeightingMultiplier = 1;

        bool forceIfFromOverride = overrideForceIf != null && (overrideForceIf.Value == AttributeForcing.ForceIf || overrideForceIf.Value == AttributeForcing.ForceIfAndRestrict);

        foreach (var attribute in attributeList)
        {
            bool subAttributeMatched = true;
            int currentAttributeForceIfWeight = 0;
            foreach (var subAttribute in attribute.SubAttributes)
            {
                if (subAttribute.ForceMode != AttributeForcing.ForceIf) { hasAttributeRestrictions = true; }
                groupWeightingMultiplier = 1;

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
                        if (!EvaluateCustomAttribute(npc, customAttribute, _environmentProvider.LinkCache, out _))
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
                    case NPCAttributeType.Group:
                        var groupAttribute = (NPCAttributeGroup)subAttribute;
                        bool hasMatchedGroup = false;
                        foreach (string selectedGroup in groupAttribute.SelectedLabels)
                        {
                            var attributeGroup = NPCAttribute.GetAttributeGroupByLabel(selectedGroup, attributeGroups, _patcherState, _logger);
                            if (attributeGroup == null)
                            {
                                subAttributeMatched = false;
                            }
                            else
                            {
                                var recursiveForceMode = overrideForceIf ?? subAttribute.ForceMode;
                                MatchNPCtoAttributeList(attributeGroup.Attributes, npc, attributeGroups, out _, out bool groupMatched, out int groupForceIfCount, out _, out _, out _, recursiveForceMode);
                                if (groupMatched)
                                {
                                    groupWeightingMultiplier = groupForceIfCount;
                                    hasMatchedGroup = true;
                                }
                            }
                        }
                        if (!hasMatchedGroup)
                        {
                            subAttributeMatched = false;
                        }
                        break;
                    case NPCAttributeType.Misc:
                        var miscAttribute = (NPCAttributeMisc)subAttribute;
                        if (miscAttribute.EvalAggression && npc.AIData.Aggression != miscAttribute.Aggression) { subAttributeMatched = false; }
                        if (miscAttribute.EvalMood && npc.AIData.Mood != miscAttribute.Mood) { subAttributeMatched = false; }
                        if (miscAttribute.EvalGender && miscAttribute.NPCGender == Gender.Male && npc.Configuration.Flags.HasFlag(NpcConfiguration.Flag.Female)) { subAttributeMatched = false; }
                        else if (miscAttribute.EvalGender && miscAttribute.NPCGender == Gender.Female && !npc.Configuration.Flags.HasFlag(NpcConfiguration.Flag.Female)) { subAttributeMatched = false; }
                        if (miscAttribute.Essential != ThreeWayState.Ignore)
                        {
                            if (miscAttribute.Essential == ThreeWayState.Is && !npc.Configuration.Flags.HasFlag(NpcConfiguration.Flag.Essential)) { subAttributeMatched = false; }
                            else if (miscAttribute.Essential == ThreeWayState.IsNot && npc.Configuration.Flags.HasFlag(NpcConfiguration.Flag.Essential)) { subAttributeMatched = false; }
                        }
                        if (miscAttribute.Ghost != ThreeWayState.Ignore)
                        {
                            if (miscAttribute.Ghost == ThreeWayState.Is && !npc.Configuration.Flags.HasFlag(NpcConfiguration.Flag.IsGhost)) { subAttributeMatched = false; }
                            else if (miscAttribute.Ghost == ThreeWayState.IsNot && npc.Configuration.Flags.HasFlag(NpcConfiguration.Flag.IsGhost)) { subAttributeMatched = false; }
                        }
                        if (miscAttribute.Invulnerable != ThreeWayState.Ignore)
                        {
                            if (miscAttribute.Invulnerable == ThreeWayState.Is && !npc.Configuration.Flags.HasFlag(NpcConfiguration.Flag.Invulnerable)) { subAttributeMatched = false; }
                            else if (miscAttribute.Invulnerable == ThreeWayState.IsNot && npc.Configuration.Flags.HasFlag(NpcConfiguration.Flag.Invulnerable)) { subAttributeMatched = false; }
                        }
                        if (miscAttribute.Protected != ThreeWayState.Ignore)
                        {
                            if (miscAttribute.Protected == ThreeWayState.Is && !npc.Configuration.Flags.HasFlag(NpcConfiguration.Flag.Protected)) { subAttributeMatched = false; }
                            else if (miscAttribute.Protected == ThreeWayState.IsNot && npc.Configuration.Flags.HasFlag(NpcConfiguration.Flag.Protected)) { subAttributeMatched = false; }
                        }
                        if (miscAttribute.Summonable != ThreeWayState.Ignore)
                        {
                            if (miscAttribute.Summonable == ThreeWayState.Is && !npc.Configuration.Flags.HasFlag(NpcConfiguration.Flag.Summonable)) { subAttributeMatched = false; }
                            else if (miscAttribute.Summonable == ThreeWayState.IsNot && npc.Configuration.Flags.HasFlag(NpcConfiguration.Flag.Summonable)) { subAttributeMatched = false; }
                        }
                        if (miscAttribute.Unique != ThreeWayState.Ignore)
                        {
                            if (miscAttribute.Unique == ThreeWayState.Is && !npc.Configuration.Flags.HasFlag(NpcConfiguration.Flag.Unique)) { subAttributeMatched = false; }
                            else if (miscAttribute.Unique == ThreeWayState.IsNot && npc.Configuration.Flags.HasFlag(NpcConfiguration.Flag.Unique)) { subAttributeMatched = false; }
                        }
                        break;
                    case NPCAttributeType.Mod:
                        var modAttribute = (NPCAttributeMod)subAttribute;
                        switch(modAttribute.ModActionType)
                        {
                            case ModAttributeEnum.From: 
                                if (!ModKeyHashSetComparer.Contains(modAttribute.ModKeys, npc.FormKey.ModKey)) { subAttributeMatched = false; }
                                break;
                            case ModAttributeEnum.PatchedBy:
                                var contexts = _environmentProvider.LinkCache.ResolveAllContexts<INpc, INpcGetter>(npc.FormKey).Where(x => !x.ModKey.Equals(npc.FormKey.ModKey)); // contexts[0] is winning override. [Last] is source plugin. Omit the source plugin
                                bool foundContext = false;
                                foreach (var context in contexts)
                                {
                                    if (ModKeyHashSetComparer.Contains(modAttribute.ModKeys, context.ModKey)) {  foundContext = true; break; }
                                }
                                if (!foundContext)
                                {
                                    subAttributeMatched = false;
                                }
                                break;
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

                if (!subAttributeMatched && subAttribute.ForceMode != AttributeForcing.ForceIf && (overrideForceIf == null || overrideForceIf.Value != AttributeForcing.ForceIf)) //  "ForceIf" mode does not cause attribute to fail matching because it implies the user does not want this sub-attribute to restrict distribute (otherwise it would be ForceIfAndRestrict) 
                {
                    if (unmatchedLog.Any()) { unmatchedLog += " | "; }
                    unmatchedLog += subAttribute.ToLogString(_patcherState.GeneralSettings.VerboseModeDetailedAttributes, _environmentProvider.LinkCache);
                    break; // stop evaluating sub-attributes if one sub-attribute isn't matched
                }
                else if (subAttributeMatched && (subAttribute.ForceMode == AttributeForcing.ForceIf || subAttribute.ForceMode == AttributeForcing.ForceIfAndRestrict || forceIfFromOverride)) 
                { 
                    currentAttributeForceIfWeight += subAttribute.Weighting * groupWeightingMultiplier; 
                }
            }

            //finished evaluating all sub-attributes

            if (hasAttributeRestrictions && subAttributeMatched) // if the last subAttribute was matched, then all subAttributes were matched. (!hasAttributeRestrictions && subAttributeMatched) means that the only matched attribute was a ForceIf, in which case the restricted attributes were not matched
            {
                matchesAttributeRestrictions = true;
            }

            if (subAttributeMatched) // compute the total forceIf weight for this attribute if all sub-attributes were matched
            {
                matchedForceIfAttributeWeightedCount += currentAttributeForceIfWeight;
            }

            if (matchesAttributeRestrictions)
            {
                matchLog += "\n" + attribute.ToLogString(_patcherState.GeneralSettings.VerboseModeDetailedAttributes, _environmentProvider.LinkCache);
            }

            if (currentAttributeForceIfWeight > 0)
            {
                forceIfLog += "\n" + attribute.ToLogString(_patcherState.GeneralSettings.VerboseModeDetailedAttributes, _environmentProvider.LinkCache) + " (Weighting: " + currentAttributeForceIfWeight + ")";
            }
        }

        return;
    }

    public bool EvaluateCustomAttribute(INpcGetter npc, NPCAttributeCustom attribute, ILinkCache linkCache, out string dispMessage)
    {
        var resolvedObjects = new List<dynamic>();
        bool success = _recordPathParser.GetObjectCollectionAtPath(npc, npc, attribute.Path, new Dictionary<string, dynamic>(), linkCache, true, Logger.GetNPCLogNameString(npc), resolvedObjects);
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
                        else if (attribute.Comparator == "Contains" && currentTypeMatched && (resolvedObject as string).Contains(attribute.ValueStr)) { valueMatched = true; break; }
                        else if (attribute.Comparator == "Starts With" && currentTypeMatched && (resolvedObject as string).StartsWith(attribute.ValueStr)) { valueMatched = true; break; }
                        else if (attribute.Comparator == "Ends With" && currentTypeMatched && (resolvedObject as string).EndsWith(attribute.ValueStr)) { valueMatched = true; break; }
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
                            case "Contains":
                                if (valueMatched) { return true; }
                                else { dispMessage = "The text at the specified path(s) does not contain the attribute value."; return false; }
                            case "Starts With":
                                if (valueMatched) { return true; }
                                else { dispMessage = "The text at the specified path(s) does not start with the attribute value."; return false; }
                            case "Ends With":
                                if (valueMatched) { return true; }
                                else { dispMessage = "The text at the specified path(s) does not end with the attribute value."; return false; }
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
                        var formKeyToMatch = new FormKey();
                        if (RecordPathParser.ObjectHasFormKey(resolvedObject, out FormKey? fk ) && fk.Value != null && !fk.Value.IsNull) { typeMatched = true; currentTypeMatched = true; formKeyToMatch = fk.Value; }
                        if(currentTypeMatched && FormKeyHashSetComparer.Contains(attribute.ValueFKs, formKeyToMatch)) { valueMatched = true; break; }
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