using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SynthEBD
{
    public class Settings_TexMesh
    {
        public Settings_TexMesh()
        {
            this.bChangeNPCTextures = true;
            this.bChangeNPCMeshes = true;
            this.bApplyToNPCsWithCustomSkins = true;
            this.bApplyToNPCsWithCustomFaces = true;
            this.bForwardArmatureFromExistingWNAMs = true;
            this.bDisplayPopupAlerts = true;
            this.bGenerateAssignmentLog = true;
            this.SelectedAssetPacks = new HashSet<string>();
        }

        public bool bChangeNPCTextures { get; set; }
        public bool bChangeNPCMeshes { get; set; }
        public bool bApplyToNPCsWithCustomSkins { get; set; }
        public bool bApplyToNPCsWithCustomFaces { get; set; }
        public bool bForwardArmatureFromExistingWNAMs { get; set; }
        public bool bDisplayPopupAlerts { get; set; }
        public bool bGenerateAssignmentLog { get; set; }

        public HashSet<string> SelectedAssetPacks { get; set; }
    }
}
