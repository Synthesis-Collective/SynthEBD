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
    /// <summary>View model for the head-parts miscellaneous settings: patching mode, per-type source-conflict winners, BodyGen config tracking, and descriptor match-mode setters.</summary>
    public class VM_HeadPartMiscSettings: VM
    {
        /// <summary>Creates the settings VM and wires the allowed/disallowed descriptor-match-mode commands.</summary>
        /// <param name="parentMenu">The owning head-parts settings VM.</param>
        /// <param name="bodyGenVM">BodyGen settings VM (source of available configs).</param>
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
        public HeadPartPatchingMode PatchingMode { get; set; } = HeadPartPatchingMode.NifEdit;
        public IEnumerable<HeadPartPatchingMode> PatchingModeOptions { get; } = Enum.GetValues<HeadPartPatchingMode>();
        public bool bUseVerboseScripts { get; set; } = false;
        public bool bSkyPatcherModeHeadparts { get; set; } = false;
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

        /// <summary>Applies a descriptor match mode across every head-part type's rule set and its head parts.</summary>
        /// <param name="descriptorTypes">"Allowed" or "Disallowed".</param>
        /// <param name="mode">The match mode to apply.</param>
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

        /// <summary>Populates this VM (patching mode, source-conflict winners, tracked BodyGen configs, flags) from a <see cref="Settings_Headparts"/> model.</summary>
        /// <param name="model">The settings model to load.</param>
        public void GetViewModelFromModel(Settings_Headparts model)
        {
            PatchingMode = model.PatchingMode;
            foreach (var type in model.SourceConflictWinners.Keys)
            {
                SourceConflictWinners[type].Source = model.SourceConflictWinners[type];
            }

            if (!model.AssociatedBodyGenConfigNameMale.IsNullOrWhitespace())
            {
                TrackedBodyGenConfigMale = AvailableBodyGenConfigsMale.FirstOrDefault(x => x.Label == model.AssociatedBodyGenConfigNameMale);
            }
            else
            {
                TrackedBodyGenConfigMale = null;
            }

            if (!model.AssociatedBodyGenConfigNameFemale.IsNullOrWhitespace())
            {
                TrackedBodyGenConfigFemale = AvailableBodyGenConfigsFemale.FirstOrDefault(x => x.Label == model.AssociatedBodyGenConfigNameFemale);
            }
            else
            {
                TrackedBodyGenConfigFemale = null;
            }

            bUseVerboseScripts = model.bUseVerboseScripts;
            bSkyPatcherModeHeadparts = model.bSkyPatcherModeHeadparts;
        }

        /// <summary>Writes this VM's values back into a <see cref="Settings_Headparts"/> model.</summary>
        /// <param name="model">The settings model to update.</param>
        public void MergeViewModelIntoModel(Settings_Headparts model)
        {
            model.PatchingMode = PatchingMode;
            foreach (var type in model.SourceConflictWinners.Keys)
            {
                model.SourceConflictWinners[type] = SourceConflictWinners[type].Source;
            }

            model.bUseVerboseScripts = bUseVerboseScripts;
            model.bSkyPatcherModeHeadparts = bSkyPatcherModeHeadparts;

            model.AssociatedBodyGenConfigNameMale = TrackedBodyGenConfigMale?.Label ?? string.Empty;
            model.AssociatedBodyGenConfigNameFemale = TrackedBodyGenConfigFemale?.Label ?? string.Empty;
        }
    }

    /// <summary>View model wrapping the chosen winner of a head-part source conflict.</summary>
    public class HeadPartSource: VM
    {
        /// <summary>The subsystem that wins the conflict.</summary>
        public HeadPartSourceCandidate Source { get; set; }
    }

    /// <summary>Which subsystem wins a head-part source conflict.</summary>
    public enum HeadPartSourceCandidate
    {
        /// <summary>The asset pack provides the head part.</summary>
        AssetPack,
        /// <summary>The head-parts menu provides the head part.</summary>
        HeadPartsMenu
    }
}
