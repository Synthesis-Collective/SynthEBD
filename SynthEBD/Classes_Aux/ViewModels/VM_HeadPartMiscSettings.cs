using MahApps.Metro.IconPacks;
using Mutagen.Bethesda.Skyrim;
using Noggog;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SynthEBD
{
    public class VM_HeadPartMiscSettings: VM
    {
        public VM_HeadPartMiscSettings(VM_Settings_Headparts parentMenu, VM_SettingsBodyGen bodyGenVM)
        {
            ParentMenu = parentMenu;

            AvailableBodyGenConfigsMale = bodyGenVM.MaleConfigs;
            AvailableBodyGenConfigsFemale = bodyGenVM.FemaleConfigs;

            SetAllowedDescriptorMatchModes = new RelayCommand(
                canExecute: _ => true,
                execute: _ => SetMatchModes(AllowedStr, AllowedDescriptorMatchMode)
            );
            SetDisallowedDescriptorMatchModes = new RelayCommand(
                canExecute: _ => true,
                execute: _ => SetMatchModes(DisallowedStr, DisallowedDescriptorMatchMode)
            );
        }
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

        public VM_Settings_Headparts ParentMenu { get; set; }
        public bool bUseVerboseScripts { get; set; } = false;
        public VM_BodyGenConfig TrackedBodyGenConfigMale { get; set; }
        public ObservableCollection<VM_BodyGenConfig> AvailableBodyGenConfigsMale { get; set; }
        public VM_BodyGenConfig TrackedBodyGenConfigFemale { get; set; }
        public ObservableCollection<VM_BodyGenConfig> AvailableBodyGenConfigsFemale { get; set; }
        public RelayCommand SetAllowedDescriptorMatchModes { get; }
        public DescriptorMatchMode AllowedDescriptorMatchMode { get; set; } = DescriptorMatchMode.All;
        public RelayCommand SetDisallowedDescriptorMatchModes { get; }
        public DescriptorMatchMode DisallowedDescriptorMatchMode { get; set; } = DescriptorMatchMode.Any;

        private const string AllowedStr = "Allowed";
        private const string DisallowedStr = "Disallowed";

        public void SetMatchModes(string descriptorTypes, DescriptorMatchMode mode)
        {
            foreach (var entry in ParentMenu.Types)
            {
                switch(descriptorTypes)
                {
                    case AllowedStr: 
                        entry.Value.TypeRuleSet.AllowedBodySlideDescriptors.MatchMode = mode; 
                        entry.Value.TypeRuleSet.AllowedBodyGenDescriptorsMale.MatchMode = mode;
                        entry.Value.TypeRuleSet.AllowedBodyGenDescriptorsFemale.MatchMode = mode;
                        break;
                    case DisallowedStr: 
                        entry.Value.TypeRuleSet.DisallowedBodySlideDescriptors.MatchMode = mode;
                        entry.Value.TypeRuleSet.DisallowedBodyGenDescriptorsMale.MatchMode = mode;
                        entry.Value.TypeRuleSet.DisallowedBodyGenDescriptorsFemale.MatchMode = mode;
                        break;
                }
                
                foreach (var headPart in entry.Value.HeadPartList)
                {
                    switch (descriptorTypes)
                    {
                        case AllowedStr:
                            headPart.AssociatedModel.AllowedBodySlideMatchMode = mode;
                            headPart.AssociatedModel.AllowedBodyGenDescriptorMatchModeMale = mode;
                            headPart.AssociatedModel.AllowedBodyGenDescriptorMatchModeFemale = mode;
                            break;
                        case DisallowedStr:
                            headPart.AssociatedModel.DisallowedBodySlideMatchMode = mode;
                            headPart.AssociatedModel.DisallowedBodyGenDescriptorMatchModeMale = mode;
                            headPart.AssociatedModel.DisallowedBodyGenDescriptorMatchModeFemale = mode;
                            break;
                    }
                }
            }
        }

        public void GetViewModelFromModel(Settings_Headparts model)
        {
            foreach (var type in model.SourceConflictWinners.Keys)
            {
                SourceConflictWinners[type].Source = model.SourceConflictWinners[type];
            }

            if (!model.AssociatedBodyGenConfigNameMale.IsNullOrWhitespace())
            {
                TrackedBodyGenConfigMale = AvailableBodyGenConfigsMale.Where(x => x.Label == model.AssociatedBodyGenConfigNameMale).FirstOrDefault();
            }
            else
            {
                TrackedBodyGenConfigMale = null;
            }

            if (!model.AssociatedBodyGenConfigNameFemale.IsNullOrWhitespace())
            {
                TrackedBodyGenConfigFemale = AvailableBodyGenConfigsFemale.Where(x => x.Label == model.AssociatedBodyGenConfigNameFemale).FirstOrDefault();
            }
            else
            {
                TrackedBodyGenConfigFemale = null;
            }

            bUseVerboseScripts = model.bUseVerboseScripts;
        }

        public void MergeViewModelIntoModel(Settings_Headparts model)
        {
            foreach (var type in model.SourceConflictWinners.Keys)
            {
                model.SourceConflictWinners[type] = SourceConflictWinners[type].Source;
            }

            model.bUseVerboseScripts = bUseVerboseScripts;

            model.AssociatedBodyGenConfigNameMale = TrackedBodyGenConfigMale?.Label ?? string.Empty;
            model.AssociatedBodyGenConfigNameFemale = TrackedBodyGenConfigFemale?.Label ?? string.Empty;
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
