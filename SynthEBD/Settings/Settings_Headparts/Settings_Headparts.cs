using Mutagen.Bethesda.Skyrim;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SynthEBD
{
    /// <summary>How head-part assignments are applied: via the EBD Papyrus script at runtime, or
    /// by baking the head-part references directly into the NPC/FaceGen NIF.</summary>
    public enum HeadPartPatchingMode
    {
        /// <summary>Apply head parts at runtime via the EBD script.</summary>
        Script,
        /// <summary>Bake head-part edits directly into the NIF.</summary>
        NifEdit
    }
    
    /// <summary>Settings POCO for the Head Parts axis, persisted as JSON. Holds the patching mode,
    /// the per-head-part-type rule sets, source-conflict resolution, and BodyGen-config links.</summary>
    public class Settings_Headparts
    {
        /// <summary>Whether head parts are applied via script or baked into the NIF.</summary>
        public HeadPartPatchingMode PatchingMode { get; set; } = HeadPartPatchingMode.NifEdit;
        /// <summary>Per-head-part-category rule sets (eyebrows, eyes, face, hair, scars, etc.).</summary>
        public Dictionary<HeadPart.TypeEnum, Settings_HeadPartType> Types { get; set; } = new()
        {
            { HeadPart.TypeEnum.Eyebrows, new() },
            { HeadPart.TypeEnum.Eyes, new() },
            { HeadPart.TypeEnum.Face, new() },
            { HeadPart.TypeEnum.FacialHair, new() },
            { HeadPart.TypeEnum.Hair, new() },
            { HeadPart.TypeEnum.Misc, new() },
            { HeadPart.TypeEnum.Scars, new() }
        };

        /// <summary>Per-category tiebreaker: which source (asset pack vs. plugin) wins when two
        /// supply a head part of the same type for one NPC.</summary>
        public Dictionary<HeadPart.TypeEnum, HeadPartSourceCandidate> SourceConflictWinners { get; set; } = new()
        {
            { HeadPart.TypeEnum.Eyebrows, HeadPartSourceCandidate.AssetPack },
            { HeadPart.TypeEnum.Eyes, HeadPartSourceCandidate.AssetPack },
            { HeadPart.TypeEnum.Face, HeadPartSourceCandidate.AssetPack },
            { HeadPart.TypeEnum.FacialHair, HeadPartSourceCandidate.AssetPack },
            { HeadPart.TypeEnum.Hair, HeadPartSourceCandidate.AssetPack },
            { HeadPart.TypeEnum.Misc, HeadPartSourceCandidate.AssetPack },
            { HeadPart.TypeEnum.Scars, HeadPartSourceCandidate.AssetPack }
        };

        /// <summary>Emit verbose head-part Papyrus scripts for debugging.</summary>
        public bool bUseVerboseScripts { get; set; } = false;
        /// <summary>Emit head-part assignments as SkyPatcher ini directives instead of plugin records.</summary>
        public bool bSkyPatcherModeHeadparts { get; set; } = false;
        /// <summary>Male BodyGen config whose descriptors gate body-shape-conditioned head parts.</summary>
        public string AssociatedBodyGenConfigNameMale { get; set; } = "";
        /// <summary>Female BodyGen config whose descriptors gate body-shape-conditioned head parts.</summary>
        public string AssociatedBodyGenConfigNameFemale { get; set; } = "";
    }
}
