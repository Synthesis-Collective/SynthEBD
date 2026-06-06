using Mutagen.Bethesda.Plugins;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SynthEBD;

/// <summary>
/// Parses an EasyNPC profile file into a map of NPC FormKey to the chosen appearance-winning plugin, used to
/// restrict patching to the appearance source EasyNPC forwarded for each NPC.
/// </summary>
public class EasyNPCProfileParser
{
    /// <summary>NPC FormKey to the EasyNPC-selected appearance plugin, populated by <see cref="Reinitialize"/>.</summary>
    public Dictionary<FormKey, ModKey> AppearanceDictionary { get; set; } = new();

    /// <summary>
    /// Clears and repopulates <see cref="AppearanceDictionary"/> from the EasyNPC profile at <paramref name="filepath"/>.
    /// Each profile line is expected to end with '|' and be of the form "FormID#Plugin.esp=...|AppearancePlugin.esp";
    /// malformed lines are skipped. No-ops if the file cannot be read.
    /// </summary>
    public void Reinitialize(string filepath)
    {
        AppearanceDictionary.Clear();
        var contents = IO_Aux.ReadFileToList(filepath, out var success);
        if (success)
        {
            foreach (var entry in contents)
            {
                if (!entry.EndsWith('|'))
                {
                    continue;
                }
                var trimmed = entry.TrimEnd('|');
                var split1 = trimmed.Split('|');
                if (split1.Length != 2) { continue; }
                var appearanceMod = ModKey.FromNameAndExtension(split1[1]);

                var split2 = split1[0].Split('=');
                if (split2.Length != 2) { continue; }

                var split3 = split2[0].Split('#');
                if (split3.Length != 2) { continue; }

                string fkStr = split3[1] + ":" + split3[0];
                var formKey = FormKey.TryFactory(fkStr);
                if (formKey != null)
                {
                    AppearanceDictionary.Add(formKey.Value, appearanceMod);
                }
            }
        }
    }

    /// <summary>
    /// Looks up the EasyNPC appearance plugin for <paramref name="npcFormKey"/>. Returns true and sets
    /// <paramref name="appearanceModKey"/> when present; otherwise returns false with a null out value.
    /// </summary>
    public bool GetNPCMod(FormKey npcFormKey, out ModKey? appearanceModKey)
    {
        if (AppearanceDictionary.ContainsKey(npcFormKey))
        {
            appearanceModKey = AppearanceDictionary[npcFormKey]; 
            return true;
        }
        else
        {
            appearanceModKey = null;
            return false;
        }
    }
}
