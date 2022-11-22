using Mutagen.Bethesda.Skyrim;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SynthEBD
{
    public class Settings_Headparts
    {
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

        public bool bUseVerboseScripts { get; set; } = false;
    }
}
