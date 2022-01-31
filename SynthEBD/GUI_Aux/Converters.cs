using Mutagen.Bethesda;
using Mutagen.Bethesda.Environments;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace SynthEBD
{
    class Converters
    {
        public static FormKey RaceEDID2FormKey(string EDID)
        {
            var env = PatcherEnvironmentProvider.Environment;

            foreach (var plugin in env.LoadOrder.ListedOrder)
            {
                if (plugin.Mod != null && plugin.Mod.Races != null)
                {
                    foreach (var race in plugin.Mod.Races)
                    {
                        if (race.EditorID.ToLower() == EDID.ToLower())
                        {
                            return race.FormKey;
                        }
                    }
                }
            }

            return new FormKey();
        }

        public static string CreateNPCDispNameFromFormKey(FormKey NPCFormKey)
        {
            var npcFormLink = new FormLink<INpcGetter>(NPCFormKey);

            if (npcFormLink.TryResolve(PatcherEnvironmentProvider.Environment.LinkCache, out var npcRecord))
            {
                string subName = "";
                if (npcRecord.Name != null && !string.IsNullOrEmpty(npcRecord.Name.ToString()))
                {
                    subName = npcRecord.Name.ToString();
                }
                else
                {
                    subName = npcRecord.EditorID;
                }
                return subName + " (" + NPCFormKey.ToString() + ")";
            }

            // Warn User
            return "";
        }

        public static HashSet<NPCAttribute> StringArraysToAttributes(List<string[]> arrList)
        {
            HashSet<NPCAttribute> h = new HashSet<NPCAttribute>();

            var env = PatcherEnvironmentProvider.Environment;

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
                        tmpFK = GetFormKeyFromxEditFormIDString(value, env);
                        if (tmpFK.IsNull == false)
                        {
                            classAttributes.Add(tmpFK);
                        }
                        break;

                    case "CNAM\\FULL":
                        foreach (var classGetter in env.LoadOrder.PriorityOrder.Class().WinningContextOverrides().ToList())
                        {
                            if (classGetter.Record.Name.ToString() == value)
                            {
                                classAttributes.Add(classGetter.Record.FormKey);
                            }
                        }
                        break;

                    case "FTST":
                        tmpFK = GetFormKeyFromxEditFormIDString(value, env);
                        if (tmpFK.IsNull == false)
                        {
                            faceTextureAttributes.Add(tmpFK);
                        }
                        break;

                    case "Factions\\*\\Faction":
                        tmpFK = GetFormKeyFromxEditFormIDString(value, env);
                        if (tmpFK.IsNull == false)
                        {
                            factionAttributes.Add(tmpFK);
                        }
                        break;

                    case "FULL - Name":
                        foreach (var npcGetter in env.LoadOrder.PriorityOrder.Npc().WinningContextOverrides().ToList())
                        {
                            if (npcGetter.Record.Name.ToString() == value)
                            {
                                npcAttributes.Add(npcGetter.Record.FormKey);
                            }
                        }
                        break;

                    case "EDID":
                        foreach (var npcGetter in env.LoadOrder.PriorityOrder.Npc().WinningContextOverrides().ToList())
                        {
                            if (npcGetter.Record.EditorID.ToString() == value)
                            {
                                npcAttributes.Add(npcGetter.Record.FormKey);
                            }
                        }
                        break;

                    case "RNAM":
                        tmpFK = GetFormKeyFromxEditFormIDString(value, env);
                        if (tmpFK.IsNull == false)
                        {
                            raceAttributes.Add(tmpFK);
                        }
                        break;

                    case "VTCK":
                        tmpFK = GetFormKeyFromxEditFormIDString(value, env);
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

        public static FormKey GetFormKeyFromxEditFormIDString(string str, IGameEnvironmentState<ISkyrimMod, ISkyrimModGetter> env)
        {
            FormKey output = new FormKey();
            var pattern = @"\[(.*?)\]"; // get text between square brackets - str will look like "Beggar \"Beggar\" [CLAS:0001327B]"
            var matches = Regex.Matches(str, pattern);

            if (matches.Count == 0)
            {
                // WARN USER
                return output;
            }

            string subStr = matches[matches.Count - 1].ToString(); // typically should only be one match.

            string[] split =  subStr.Split(':');
            if (split.Length != 2)
            {
                // WARN USER
                return output;
            }

            string formID = split[1].Trim(']'); // should look like "0001327B"

            if (formID.Length != 8)
            {
                // WARN USER
                return output;
            }

            string modIndex = formID.Substring(0, 2);
            int iModIndex = -1;
            string pluginNameAndExtension = "";
            try
            {
                iModIndex = int.Parse(modIndex, System.Globalization.NumberStyles.HexNumber); // https://theburningmonk.com/2010/02/converting-hex-to-int-in-csharp/
                pluginNameAndExtension = env.LoadOrder[iModIndex].ModKey.FileName;
            }
            catch
            {
                // WARN USER
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
                // WARN USER
            }

            return output;
        }

        public static FormKey zEBDSignatureToFormKey(string rootPlugin, string formID, IGameEnvironmentState<ISkyrimMod, ISkyrimModGetter> env)
        {
            string fkString = "";
            FormKey output = new FormKey();

            foreach (var plugin in env.LoadOrder.ListedOrder)
            {
                if (plugin.ModKey.FileName.String.ToLower() == rootPlugin.ToLower())
                {
                    switch(formID.Length)
                    {
                        case 6: fkString = formID + ":" + plugin.ModKey.FileName; break;
                        case 8: fkString = formID.Substring(2, 6) + ":" + plugin.ModKey.FileName; break;
                        default: break; // Warn User
                    }
                }
            }

            try
            {
                output = FormKey.Factory(fkString);
            }
            catch
            {
                // WARN USER
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

        public static BodyShapeDescriptor StringToBodyShapeDescriptor(string s)
        {
            BodyShapeDescriptor newDescriptor = new BodyShapeDescriptor();
            try
            {
                string[] split = s.Split(':');
                newDescriptor.Category = split[0].Trim();
                newDescriptor.Value = split[1].Trim();
                newDescriptor.Signature = s;
            }
            catch
            {
                // Warn User
            }
            return newDescriptor;
        }

        /// <summary>
        /// Reads in ForceIf attributes from a zEBD config file, matches them to their corresponding AllowedAttribute, and sets that AllowedAttribute's ForceIf property to tru
        /// If is no AllowedAttribute that matches the ForceIfAttribute (zEBD permitted this), make a new AllowedAttribute
        /// Note: This function is only called when upgrading zEBD config files, which didn't have AND-grouped subattributes, so the bool is set on attribute.GroupedSubAttributes.FirstOrDefault because there must only be one subattribute
        /// </summary>
        /// <param name="allowedAttributes"></param>
        /// <param name="forceIfAttributes"></param>
        public static void zEBDForceIfAttributesToAllowed(HashSet<NPCAttribute> allowedAttributes, HashSet<NPCAttribute> forceIfAttributes)
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
                        match.SubAttributes.FirstOrDefault().ForceIf = true;
                    }
                }
                else
                {
                    ofa.SubAttributes.FirstOrDefault().ForceIf = true;
                    allowedAttributes.Add(ofa);
                }
            }
        }

        public static bool FormKeyStringToFormIDString(string formKeyString, out string formIDstr)
        {
            formIDstr = string.Empty;
            var split = formKeyString.Split(':');
            if (split.Length != 2) { return false; }

            if (split[1] == PatcherSettings.General.patchFileName + ".esp")
            {
                formIDstr = PatcherEnvironmentProvider.Environment.LoadOrder.ListedOrder.Count().ToString("X"); // format FormID assuming the generated patch will be last in the load order
            }
            else
            {
                for (int i = 0; i < PatcherEnvironmentProvider.Environment.LoadOrder.ListedOrder.Count(); i++)
                {
                    var currentListing = PatcherEnvironmentProvider.Environment.LoadOrder.ListedOrder.ElementAt(i);
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
}
