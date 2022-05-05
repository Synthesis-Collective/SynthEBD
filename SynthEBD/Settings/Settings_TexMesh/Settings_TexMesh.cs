using Newtonsoft.Json;

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
            this.bForceVanillaBodyMeshPath = false;
            this.bDisplayPopupAlerts = true;
            this.bGenerateAssignmentLog = true;
            this.bShowPreviewImages = true;
            this.SelectedAssetPacks = new HashSet<string>();
            this.TrimPaths = new HashSet<TrimPath>();
        }

        public bool bChangeNPCTextures { get; set; }
        public bool bChangeNPCMeshes { get; set; }
        public bool bApplyToNPCsWithCustomSkins { get; set; }
        public bool bApplyToNPCsWithCustomFaces { get; set; }
        public bool bForceVanillaBodyMeshPath { get; set; }
        public bool bDisplayPopupAlerts { get; set; }
        public bool bGenerateAssignmentLog { get; set; }
        public bool bShowPreviewImages { get; set; }
        public HashSet<string> SelectedAssetPacks { get; set; }
        [JsonIgnore]
        public HashSet<TrimPath> TrimPaths { get; set; }

    }
}
