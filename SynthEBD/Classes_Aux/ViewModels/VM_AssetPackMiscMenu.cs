using Noggog;
using ReactiveUI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Documents;

namespace SynthEBD
{
    public class VM_AssetPackMiscMenu : VM
    {
        private readonly VM_AssetPack _parent;

        public delegate VM_AssetPackMiscMenu Factory(VM_AssetPack parentPack);
        public VM_AssetPackMiscMenu(VM_AssetPack parentPack, VM_SpecificNPCAssignmentsUI specificAssignmentsUI, VM_SpecificNPCAssignment.VM_MixInSpecificAssignment.Factory mixInFactory)
        {
            _parent = parentPack;

            _parent.WhenAnyValue(x => x.ConfigType).Subscribe(y =>
            {
                ShowMixInCommands = y == AssetPackType.MixIn;
            }).DisposeWith(this);

            SetAllowedDescriptorMatchModes = new RelayCommand(
                canExecute: _ => true,
                execute: _ => SetMatchModes(AllowedStr, AllowedDescriptorMatchMode)
            );
            SetDisallowedDescriptorMatchModes = new RelayCommand(
                canExecute: _ => true,
                execute: _ => SetMatchModes(DisallowedStr, DisallowedDescriptorMatchMode)
            );

            AddMixInToSpecificAssignments = new RelayCommand(
                canExecute: _ => true,
                execute: _ => { 
                    foreach (var assignment in specificAssignmentsUI.Assignments.Where(x => x.Gender == _parent.Gender))
                    {
                        var existingMixInAssignment = assignment.ForcedMixIns.Where(x => x.ForcedAssetPack.GroupName == _parent.GroupName).FirstOrDefault();
                        if (existingMixInAssignment != null)
                        {
                            if (OverrideExistingSNA)
                            {
                                existingMixInAssignment.Decline = AsDeclinedSNA;
                            }
                        }
                        else
                        {
                            var newMixIn = mixInFactory(assignment);
                            newMixIn.ForcedAssetPack = _parent;
                            newMixIn.Decline = AsDeclinedSNA;
                            assignment.ForcedMixIns.Add(newMixIn);
                        }
                    }
                }
            );
        }

        public RelayCommand SetAllowedDescriptorMatchModes { get; }
        public DescriptorMatchMode AllowedDescriptorMatchMode { get; set; } = DescriptorMatchMode.All;
        public RelayCommand SetDisallowedDescriptorMatchModes { get; }
        public DescriptorMatchMode DisallowedDescriptorMatchMode { get; set; } = DescriptorMatchMode.Any;
        public bool ShowMixInCommands { get; set; } = false;
        public RelayCommand AddMixInToSpecificAssignments { get; }
        public bool AsDeclinedSNA { get; set; }
        public bool OverrideExistingSNA { get; set; }

        private const string AllowedStr = "Allowed";
        private const string DisallowedStr = "Disallowed";

        public void SetMatchModes(string descriptorTypes, DescriptorMatchMode mode)
        {
            foreach(var subgroup in _parent.Subgroups)
            {
                SetSubgroupMatchModes(subgroup, descriptorTypes, mode);
            }
            foreach (var replacer in _parent.ReplacersMenu.ReplacerGroups)
            {
                foreach (var subgroup in replacer.Subgroups)
                {
                    SetSubgroupMatchModes(subgroup, descriptorTypes, mode);
                }    
            }
        }

        public static void SetSubgroupMatchModes(VM_Subgroup subgroup, string descriptorType, DescriptorMatchMode mode)
        {
            switch(descriptorType)
            {
                case AllowedStr:
                    subgroup.AllowedBodyGenDescriptors.MatchMode = mode;
                    subgroup.AllowedBodySlideDescriptors.MatchMode = mode;
                    break;
                case DisallowedStr:
                    subgroup.DisallowedBodyGenDescriptors.MatchMode = mode;
                    subgroup.DisallowedBodySlideDescriptors.MatchMode = mode;
                    break;
            }

            foreach (var sg in subgroup.Subgroups)
            {
                SetSubgroupMatchModes(sg, descriptorType, mode);
            }
        }
    }
}