using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SynthEBD.Settings_General
{
    class Settings_General
    {
        public Settings_General()
        {
            this.bShowToolTips = true;
            this.bChangeMeshesOrTextures = true;
            this.bEnableBodyGenIntegration = false;
            this.bChangeHeight = false;
            this.bEnableConsistency = true;
            this.bLinkNPCsWithSameName = true;
            this.patchFileName = "SynthEBD.esp";
            this.bVerboseMode_Assets_Noncompliant = false;
            this.bVerboseMode_Assets_All = false;
            this.verboseMode_NPClist = new List<string>();
            this.bLoadSettingsFromDataFolder = false;
            this.patchableRaces = new List<string>();
            this.raceAliases = new List<Internal_Data_Classes.raceAlias>();
        }

        bool bShowToolTips { get; set; }
        bool bChangeMeshesOrTextures { get; set; }

        bool bEnableBodyGenIntegration { get; set; }

        bool bChangeHeight { get; set; }
        bool bEnableConsistency { get; set; }
        bool bLinkNPCsWithSameName { get; set; }
        string patchFileName { get; set; }

        bool bVerboseMode_Assets_Noncompliant { get; set; }
        bool bVerboseMode_Assets_All { get; set; }
        List<string> verboseMode_NPClist { get; set; } // change to mutagen NPCs
        bool bLoadSettingsFromDataFolder { get; set; }
        
        List<string> patchableRaces { get; set; } // change to mutagen races

        List<Internal_Data_Classes.raceAlias> raceAliases { get; set; }
    }
}
