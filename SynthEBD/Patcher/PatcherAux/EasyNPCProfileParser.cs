using Mutagen.Bethesda.Plugins;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SynthEBD;

public class EasyNPCProfileParser
{
    public Dictionary<FormKey, ModKey> AppearanceDictionary { get; set; } = new();

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
