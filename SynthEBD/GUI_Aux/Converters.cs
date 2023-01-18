using Mutagen.Bethesda;
using Mutagen.Bethesda.Environments;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Order;
using Mutagen.Bethesda.Skyrim;
using System.Text.RegularExpressions;

namespace SynthEBD;

public class Converters
{
    private readonly IEnvironmentStateProvider _environmentProvider;
    private readonly Logger _logger;
    private readonly PatcherState _patcherState;
    public Converters(IEnvironmentStateProvider environmentProvider, Logger logger, PatcherState patcherState)
    {
        _environmentProvider = environmentProvider;
        _logger = logger;
        _patcherState = patcherState;
    }
    public static FormKey RaceEDID2FormKey(string EDID, IEnvironmentStateProvider environmentProvider)
    {
        foreach (var plugin in environmentProvider.LoadOrder.ListedOrder)
        {
            if (plugin.Mod != null && plugin.Mod.Races != null)
            {
                foreach (var race in plugin.Mod.Races)
                {
                    if (race.EditorID != null && race.EditorID.ToLower() == EDID.ToLower())
                    {
                        return race.FormKey;
                    }
                }
            }
        }

        return new FormKey();
    }

    public string CreateNPCDispNameFromFormKey(FormKey NPCFormKey)
    {
        var npcFormLink = new FormLink<INpcGetter>(NPCFormKey);

        if (npcFormLink.TryResolve(_environmentProvider.LinkCache, out var npcRecord))
        {
            string subName = "";
            if (npcRecord.Name != null && !string.IsNullOrEmpty(npcRecord.Name.ToString()))
            {
                subName = npcRecord.Name.ToString();
            }
            else
            {
                subName = EditorIDHandler.GetEditorIDSafely(npcRecord);
            }
            return subName + " (" + NPCFormKey.ToString() + ")";
        }

        _logger.LogError("Could not resolve NPC with FormKey " + NPCFormKey.ToString() + " in the current load order");
        return "";
    }

    public HashSet<NPCAttribute> StringArraysToAttributes(List<string[]> arrList)
    {
        HashSet<NPCAttribute> h = new HashSet<NPCAttribute>();

        //temporary storage lists for grouping attributes of same type
        HashSet<FormKey> classAttributes = new HashSet<FormKey>();
        HashSet<FormKey> faceTextureAttributes = new HashSet<FormKey>();
        HashSet<FormKey> factionAttributes = new HashSet<FormKey>();
        HashSet<FormKey> npcAttributes = new HashSet<FormKey>();
        HashSet<FormKey> raceAttributes = new HashSet<FormKey>();
        HashSet<FormKey> voiceTypeAttributes = new HashSet<FormKey>();

        FormKey tmpFK;

        foreach (string[] arr in arrList)
        {
            string type = arr[0].ToUpper();
            string value = arr[1];
                
            switch(type)
            {
                case "CNAM":
                    tmpFK = GetFormKeyFromxEditFormIDString(value);
                    if (tmpFK.IsNull == false)
                    {
                        classAttributes.Add(tmpFK);
                    }
                    break;

                case "CNAM\\FULL":
                    foreach (var classGetter in _environmentProvider.LoadOrder.PriorityOrder.Class().WinningContextOverrides().ToList())
                    {
                        if (classGetter.Record.Name.ToString() == value)
                        {
                            classAttributes.Add(classGetter.Record.FormKey);
                        }
                    }
                    break;

                case "FTST":
                    tmpFK = GetFormKeyFromxEditFormIDString(value);
                    if (tmpFK.IsNull == false)
                    {
                        faceTextureAttributes.Add(tmpFK);
                    }
                    break;

                case "Factions\\*\\Faction":
                    tmpFK = GetFormKeyFromxEditFormIDString(value);
                    if (tmpFK.IsNull == false)
                    {
                        factionAttributes.Add(tmpFK);
                    }
                    break;

                case "FULL - Name":
                    foreach (var npcGetter in _environmentProvider.LoadOrder.PriorityOrder.Npc().WinningContextOverrides().ToList())
                    {
                        if (npcGetter.Record.Name.ToString() == value)
                        {
                            npcAttributes.Add(npcGetter.Record.FormKey);
                        }
                    }
                    break;

                case "EDID":
                    foreach (var npcGetter in _environmentProvider.LoadOrder.PriorityOrder.Npc().WinningContextOverrides().ToList())
                    {
                        if (npcGetter.Record.EditorID != null && npcGetter.Record.EditorID.ToString() == value)
                        {
                            npcAttributes.Add(npcGetter.Record.FormKey);
                        }
                    }
                    break;

                case "RNAM":
                    tmpFK = GetFormKeyFromxEditFormIDString(value);
                    if (tmpFK.IsNull == false)
                    {
                        raceAttributes.Add(tmpFK);
                    }
                    break;

                case "VTCK":
                    tmpFK = GetFormKeyFromxEditFormIDString(value);
                    if (tmpFK.IsNull == false)
                    {
                        voiceTypeAttributes.Add(tmpFK);
                    }
                    break;
            }
        }

        if (classAttributes.Count > 0)
        {
            NPCAttributeClass tmpAttr = new NPCAttributeClass();
            tmpAttr.FormKeys = classAttributes;
            NPCAttribute newAttr = new NPCAttribute();
            newAttr.SubAttributes.Add(tmpAttr);
            h.Add(newAttr);
        }

        if (faceTextureAttributes.Count > 0)
        {
            NPCAttributeFaceTexture tmpAttr = new NPCAttributeFaceTexture();
            tmpAttr.FormKeys = faceTextureAttributes;
            NPCAttribute newAttr = new NPCAttribute();
            newAttr.SubAttributes.Add(tmpAttr);
            h.Add(newAttr);
        }

        if (factionAttributes.Count > 0)
        {
            NPCAttributeFactions tmpAttr = new NPCAttributeFactions();
            tmpAttr.FormKeys = factionAttributes;
            NPCAttribute newAttr = new NPCAttribute();
            newAttr.SubAttributes.Add(tmpAttr);
            h.Add(newAttr);
        }

        if (npcAttributes.Count > 0)
        {
            NPCAttributeNPC tmpAttr = new NPCAttributeNPC();
            tmpAttr.FormKeys = npcAttributes;
            NPCAttribute newAttr = new NPCAttribute();
            newAttr.SubAttributes.Add(tmpAttr);
            h.Add(newAttr);
        }

        if (raceAttributes.Count > 0)
        {
            NPCAttributeRace tmpAttr = new NPCAttributeRace();
            tmpAttr.FormKeys = raceAttributes;
            NPCAttribute newAttr = new NPCAttribute();
            newAttr.SubAttributes.Add(tmpAttr);
            h.Add(newAttr);
        }

        if (voiceTypeAttributes.Count > 0)
        {
            NPCAttributeVoiceType tmpAttr = new NPCAttributeVoiceType();
            tmpAttr.FormKeys = voiceTypeAttributes;
            NPCAttribute newAttr = new NPCAttribute();
            newAttr.SubAttributes.Add(tmpAttr);
            h.Add(newAttr);
        }

        return h;
    }

    public FormKey GetFormKeyFromxEditFormIDString(string str)
    {
        FormKey output = new FormKey();
        var pattern = @"\[(.*?)\]"; // get text between square brackets - str will look like "Beggar \"Beggar\" [CLAS:0001327B]"
        var matches = Regex.Matches(str, pattern);

        if (matches.Count == 0)
        {
            _logger.LogError("Could not parse " + str + " as a FormID");
            return output;
        }

        string subStr = matches[matches.Count - 1].ToString(); // typically should only be one match.

        string[] split =  subStr.Split(':');
        if (split.Length != 2)
        {
            _logger.LogError("Could not parse " + str + " as a FormID");
            return output;
        }

        string formID = split[1].Trim(']'); // should look like "0001327B"

        if (formID.Length != 8)
        {
            _logger.LogError("Could not parse " + str + " as a FormID");
            return output;
        }

        string modIndex = formID.Substring(0, 2);
        int iModIndex = -1;
        string pluginNameAndExtension = "";
        try
        {
            iModIndex = int.Parse(modIndex, System.Globalization.NumberStyles.HexNumber); // https://theburningmonk.com/2010/02/converting-hex-to-int-in-csharp/
            pluginNameAndExtension = _environmentProvider.LoadOrder[iModIndex].ModKey.FileName;
        }
        catch
        {
            _logger.LogError("Could not parse " + str + " as a FormID");
            return output;
        }

        string signature = formID.Substring(2, 6);

        string fkString = signature + ":" + pluginNameAndExtension;

        try
        {
            output = FormKey.Factory(fkString);
        }
        catch
        {
            _logger.LogError("Could not create FormKey " + fkString + " from FormID " + str);
            return output;
        }

        return output;
    }

    public FormKey zEBDSignatureToFormKey(string rootPlugin, string formID, IEnvironmentStateProvider environmentProvider)
    {
        string fkString = "";
        FormKey output = new FormKey();

        foreach (var plugin in environmentProvider.LoadOrder.ListedOrder)
        {
            if (plugin.ModKey.FileName.String.ToLower() == rootPlugin.ToLower())
            {
                switch(formID.Length)
                {
                    case 6: fkString = formID + ":" + plugin.ModKey.FileName; break;
                    case 8: fkString = formID.Substring(2, 6) + ":" + plugin.ModKey.FileName; break;
                    default:
                        _logger.LogError("Could not convert zEBD FormKey Signature " + fkString + " to FormKey");
                        break;
                }
            }
        }

        try
        {
            output = FormKey.Factory(fkString);
        }
        catch
        {
            _logger.LogError("Could not convert zEBD FormKey Signature " + fkString + " to FormKey");
        }

        return output;
    }

    public static NPCWeightRange StringArrayToWeightRange(string[] arr)
    {
        var weightRange = new NPCWeightRange();
        int tmpLower = 0;
        int tmpUpper = 100;
        int.TryParse(arr[0], out tmpLower); // (default zEBD value of null gets parsed as 0).

        if (arr[1] == null || arr[1] == "") // (default zEBD value of null gets parsed as 0, which is incorrect for .Upper).
        {
            tmpUpper = 100;
        }
        else
        {
            int.TryParse(arr[1], out tmpUpper);
        }
        weightRange.Lower = tmpLower;
        weightRange.Upper = tmpUpper;

        return weightRange;
    }

    /// <summary>
    /// Reads in ForceIf attributes from a zEBD config file, matches them to their corresponding AllowedAttribute, and sets that AllowedAttribute's ForceIf property to tru
    /// If is no AllowedAttribute that matches the ForceIfAttribute (zEBD permitted this), make a new AllowedAttribute
    /// Note: This function is only called when upgrading zEBD config files, which didn't have AND-grouped subattributes, so the bool is set on attribute.GroupedSubAttributes.FirstOrDefault because there must only be one subattribute
    /// </summary>
    /// <param name="allowedAttributes"></param>
    /// <param name="forceIfAttributes"></param>
    public static void ImportzEBDForceIfAttributes(HashSet<NPCAttribute> allowedAttributes, HashSet<NPCAttribute> forceIfAttributes)
    {
        foreach (var ofa in forceIfAttributes)
        {
            var matches = new HashSet<NPCAttribute>();
            foreach (var aa in allowedAttributes)
            {
                if (ofa.Equals(aa))
                {
                    matches.Add(aa);
                }
            }

            if (matches.Any())
            {
                foreach (var match in matches)
                {
                    match.SubAttributes.FirstOrDefault().ForceMode = AttributeForcing.ForceIf;
                }
            }
            else
            {
                ofa.SubAttributes.FirstOrDefault().ForceMode = AttributeForcing.ForceIf;
                allowedAttributes.Add(ofa);
            }
        }
    }

    public bool FormKeyStringToFormIDString(string formKeyString, out string formIDstr)
    {
        formIDstr = string.Empty;
        var split = formKeyString.Split(':');
        if (split.Length != 2) { return false; }

        if (split[1] == _patcherState.GeneralSettings.PatchFileName + ".esp")
        {
            formIDstr = _environmentProvider.LoadOrder.ListedOrder.Count().ToString("X"); // format FormID assuming the generated patch will be last in the load order
        }
        else
        {
            for (int i = 0; i < _environmentProvider.LoadOrder.ListedOrder.Count(); i++)
            {
                var currentListing = _environmentProvider.LoadOrder.ListedOrder.ElementAt(i);
                if (currentListing.ModKey.FileName == split[1])
                {
                    formIDstr = i.ToString("X"); // https://www.delftstack.com/howto/csharp/integer-to-hexadecimal-in-csharp/
                    break;
                }
            }
        }
        if (!formIDstr.Any())
        {
            return false;
        }

        formIDstr += split[0];
        return true;
    }
}