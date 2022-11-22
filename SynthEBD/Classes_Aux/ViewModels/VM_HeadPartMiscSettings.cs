using Mutagen.Bethesda.Skyrim;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SynthEBD
{
    public class VM_HeadPartMiscSettings: VM
    {
        public Dictionary<HeadPart.TypeEnum, HeadPartSource> SourceConflictWinners { get; set; } = new()
        {
            { HeadPart.TypeEnum.Eyebrows, new HeadPartSource() { Source = HeadPartSourceCandidate.AssetPack} },
            { HeadPart.TypeEnum.Eyes, new HeadPartSource() { Source = HeadPartSourceCandidate.AssetPack} },
            { HeadPart.TypeEnum.Face, new HeadPartSource() { Source = HeadPartSourceCandidate.AssetPack} },
            { HeadPart.TypeEnum.FacialHair, new HeadPartSource() { Source = HeadPartSourceCandidate.AssetPack} },
            { HeadPart.TypeEnum.Hair, new HeadPartSource() { Source = HeadPartSourceCandidate.AssetPack} },
            { HeadPart.TypeEnum.Misc, new HeadPartSource() { Source = HeadPartSourceCandidate.AssetPack} },
            { HeadPart.TypeEnum.Scars, new HeadPartSource() { Source = HeadPartSourceCandidate.AssetPack} }
        };

        public bool bUseVerboseScripts { get; set; } = false;

        public void GetViewModelFromModel(Settings_Headparts model)
        {
            foreach (var type in model.SourceConflictWinners.Keys)
            {
                SourceConflictWinners[type].Source = model.SourceConflictWinners[type];
            }

            bUseVerboseScripts = model.bUseVerboseScripts;
        }

        public void DumpViewModelToModel(Settings_Headparts model)
        {
            foreach (var type in model.SourceConflictWinners.Keys)
            {
                model.SourceConflictWinners[type] = SourceConflictWinners[type].Source;
            }

            model.bUseVerboseScripts = bUseVerboseScripts;
        }
    }

    public class HeadPartSource: VM
    {
        public HeadPartSourceCandidate Source { get; set; }
    }

    public enum HeadPartSourceCandidate
    {
        AssetPack,
        HeadPartsMenu
    }
}
