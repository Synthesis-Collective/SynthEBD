using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SynthEBD.Settings_AssetPack
{
    class Settings_AssetPack
    {
        public Settings_AssetPack()
        {
            this.bM_T_IncludeNPCsWithFaceParts = true;
            this.bChangeMeshes = true;
            this.bChangeTextures = true;
            this.bUpdateHeadParts = true;
            this.bExcludePC = true;
            this.bDisplayAssetPackAlerts = true;
            this.bAbortIfPathWarnings = true;
        }

        bool bM_T_IncludeNPCsWithFaceParts { get; set; } 
        bool bChangeMeshes { get; set; } 
        bool bChangeTextures { get; set; } 
        bool bUpdateHeadParts { get; set; }
        bool bExcludePC { get; set; } 
        bool bDisplayAssetPackAlerts { get; set; }
        bool bAbortIfPathWarnings { get; set; }
    }
}
