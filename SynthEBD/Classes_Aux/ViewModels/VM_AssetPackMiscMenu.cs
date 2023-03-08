using DynamicData.Binding;
using Mutagen.Bethesda.Plugins;
using Noggog;
using ReactiveUI;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
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
        public VM_AssetPackMiscMenu(VM_AssetPack parentPack, IEnvironmentStateProvider environmentProvider, VM_SpecificNPCAssignmentsUI specificAssignmentsUI, VM_SpecificNPCAssignment.VM_MixInSpecificAssignment.Factory mixInFactory)
        {
            _parent = parentPack;

            _parent.WhenAnyValue(x => x.ConfigType).Subscribe(y =>
            {
                ShowMixInCommands = y == AssetPackType.MixIn;
            }).DisposeWith(this);

            environmentProvider.WhenAnyValue(x => x.LoadOrder)
           .Subscribe(x => LoadOrder = x.Where(y => y.Value != null && y.Value.Enabled).Select(x => x.Value.ModKey)).DisposeWith(this);

            AssociatedBsaModKeys.ToObservableChangeSet().Subscribe(_ => UpdateFilePathStatus()).DisposeWith(this);

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
                    foreach (var assignment in specificAssignmentsUI.Assignments.Where(x => x.Gender == _parent.Gender).ToArray())
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
        public ObservableCollection<ModKey> AssociatedBsaModKeys { get; set; } = new();
        public IEnumerable<ModKey> LoadOrder { get; private set; }

        public void CopyInViewModelFromModel(AssetPack model)
        {
            AssociatedBsaModKeys.Clear();
            AssociatedBsaModKeys.AddRange(model.AssociatedBsaModKeys);
        }

        public void MergeIntoModel(AssetPack model)
        {
            model.AssociatedBsaModKeys.Clear();
            model.AssociatedBsaModKeys.AddRange(AssociatedBsaModKeys);
        }

        private void UpdateFilePathStatus()
        {
            if (_parent.DisplayedSubgroup != null)
            {
                foreach (var path in _parent.DisplayedSubgroup.PathsMenu.Paths)
                {
                    path.RefreshSourceColor();
                }
            }
        }

        public void SetMatchModes(string descriptorTypes, DescriptorMatchMode mode)
        {
            if (_parent.DisplayedSubgroup != null)
            {
                switch (descriptorTypes)
                {
                    case AllowedStr:
                        _parent.DisplayedSubgroup.AllowedBodyGenDescriptors.MatchMode = mode;
                        _parent.DisplayedSubgroup.AllowedBodySlideDescriptors.MatchMode = mode;
                        break;
                    case DisallowedStr:
                        _parent.DisplayedSubgroup.DisallowedBodyGenDescriptors.MatchMode = mode;
                        _parent.DisplayedSubgroup.DisallowedBodySlideDescriptors.MatchMode = mode;
                        break;
                }
            }

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

        public static void SetSubgroupMatchModes(VM_SubgroupPlaceHolder subgroup, string descriptorType, DescriptorMatchMode mode)
        {
            switch(descriptorType)
            {
                case AllowedStr:
                    subgroup.AssociatedModel.AllowedBodyGenMatchMode = mode;
                    subgroup.AssociatedModel.AllowedBodySlideMatchMode = mode;
                    break;
                case DisallowedStr:
                    subgroup.AssociatedModel.DisallowedBodyGenMatchMode = mode;
                    subgroup.AssociatedModel.DisallowedBodySlideMatchMode = mode;
                    break;
            }

            foreach (var sg in subgroup.Subgroups)
            {
                SetSubgroupMatchModes(sg, descriptorType, mode);
            }
        }
    }
}
