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
            var env = new GameEnvironmentProvider().MyEnvironment;

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

        public static HashSet<NPCAttribute> StringArraysToAttributes(List<string[]> arrList)
        {
            HashSet<NPCAttribute> h = new HashSet<NPCAttribute>();

            var env = new GameEnvironmentProvider().MyEnvironment;

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
                        tmpFK = GetFormKeyFromxEditString(value, env);
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
                        tmpFK = GetFormKeyFromxEditString(value, env);
                        if (tmpFK.IsNull == false)
                        {
                            faceTextureAttributes.Add(tmpFK);
                        }
                        break;

                    case "Factions\\*\\Faction":
                        tmpFK = GetFormKeyFromxEditString(value, env);
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
                        tmpFK = GetFormKeyFromxEditString(value, env);
                        if (tmpFK.IsNull == false)
                        {
                            raceAttributes.Add(tmpFK);
                        }
                        break;

                    case "VTCK":
                        tmpFK = GetFormKeyFromxEditString(value, env);
                        if (tmpFK.IsNull == false)
                        {
                            voiceTypeAttributes.Add(tmpFK);
                        }
                        break;
                }
            }

            if (classAttributes.Count > 0)
            {
                NPCAttributeShell shell = new NPCAttributeShell();
                shell.Type = NPCAttributeType.Class;
                NPCAttributeClass tmpAttr = new NPCAttributeClass();
                tmpAttr.ClassFormKeys = classAttributes;
                shell.Attribute = tmpAttr;
                NPCAttribute newAttr = new NPCAttribute();
                newAttr.GroupedSubAttributes.Add(shell);
                h.Add(newAttr);
            }

            if (faceTextureAttributes.Count > 0)
            {
                NPCAttributeShell shell = new NPCAttributeShell();
                shell.Type = NPCAttributeType.FaceTexture;
                NPCAttributeFaceTexture tmpAttr = new NPCAttributeFaceTexture();
                tmpAttr.FaceTextureFormKeys = faceTextureAttributes;
                shell.Attribute = tmpAttr;
                NPCAttribute newAttr = new NPCAttribute();
                newAttr.GroupedSubAttributes.Add(shell);
                h.Add(newAttr);
            }

            if (factionAttributes.Count > 0)
            {
                NPCAttributeShell shell = new NPCAttributeShell();
                shell.Type = NPCAttributeType.Factions;
                NPCAttributeFactions tmpAttr = new NPCAttributeFactions();
                tmpAttr.FactionFormKeys = factionAttributes;
                shell.Attribute = tmpAttr;
                NPCAttribute newAttr = new NPCAttribute();
                newAttr.GroupedSubAttributes.Add(shell);
                h.Add(newAttr);
            }

            if (npcAttributes.Count > 0)
            {
                NPCAttributeShell shell = new NPCAttributeShell();
                shell.Type = NPCAttributeType.NPC;
                NPCAttributeNPC tmpAttr = new NPCAttributeNPC();
                tmpAttr.NPCFormKeys = npcAttributes;
                shell.Attribute = tmpAttr;
                NPCAttribute newAttr = new NPCAttribute();
                newAttr.GroupedSubAttributes.Add(shell);
                h.Add(newAttr);
            }

            if (raceAttributes.Count > 0)
            {
                NPCAttributeShell shell = new NPCAttributeShell();
                shell.Type = NPCAttributeType.Race;
                NPCAttributeRace tmpAttr = new NPCAttributeRace();
                tmpAttr.RaceFormKeys = raceAttributes;
                shell.Attribute = tmpAttr;
                NPCAttribute newAttr = new NPCAttribute();
                newAttr.GroupedSubAttributes.Add(shell);
                h.Add(newAttr);
            }

            if (voiceTypeAttributes.Count > 0)
            {
                NPCAttributeShell shell = new NPCAttributeShell();
                shell.Type = NPCAttributeType.VoiceType;
                NPCAttributeVoiceType tmpAttr = new NPCAttributeVoiceType();
                tmpAttr.VoiceTypeFormKeys = voiceTypeAttributes;
                shell.Attribute = tmpAttr;
                NPCAttribute newAttr = new NPCAttribute();
                newAttr.GroupedSubAttributes.Add(shell);
                h.Add(newAttr);
            }

            return h;
        }

        public static FormKey GetFormKeyFromxEditString(string str, IGameEnvironmentState<ISkyrimMod, ISkyrimModGetter> env)
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

        public static NPCWeightRange StringArrayToWeightRange(string[] arr)
        {
            var weightRange = new NPCWeightRange();
            int tmpLower = 0;
            int tmpUpper = 100;
            int.TryParse(arr[0], out tmpLower); // (default zEBD value of null gets parsed as 0).

            if (arr[1] == null) // (default zEBD value of null gets parsed as 0, which is incorrect for .Upper).
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

        public static BodyGenConfig.MorphDescriptor StringToMorphDescriptor(string s)
        {
            BodyGenConfig.MorphDescriptor newDescriptor = new BodyGenConfig.MorphDescriptor();
            try
            {
                string[] split = s.Split(':');
                newDescriptor.Category = split[0].Trim();
                newDescriptor.Value = split[1].Trim();
                newDescriptor.DispString = s;
            }
            catch
            {

            }
            return newDescriptor;
        }
    }
}
