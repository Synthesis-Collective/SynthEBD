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
    /// <summary>View model for an asset pack's miscellaneous menu: descriptor match-mode setters, BSA association, missing-descriptor cleanup, and mix-in specific-assignment helpers.</summary>
    public class VM_AssetPackMiscMenu : VM
    {
        private readonly VM_AssetPack _parent;

        /// <summary>Autofac factory delegate for constructing the menu under an asset pack.</summary>
        public delegate VM_AssetPackMiscMenu Factory(VM_AssetPack parentPack);
        /// <summary>Creates the menu, wiring the descriptor-match-mode, delete-missing-descriptor, and add-mix-in-to-specific-assignments commands plus load-order/BSA tracking.</summary>
        /// <param name="parentPack">The owning asset pack VM.</param>
        /// <param name="environmentProvider">Supplies the load order and link cache.</param>
        /// <param name="specificAssignmentsUI">Specific-NPC-assignments UI (target of the add-mix-in command).</param>
        /// <param name="mixInFactory">Factory for mix-in specific assignments.</param>
        /// <param name="logger">Logger for diagnostics.</param>
        public VM_AssetPackMiscMenu(VM_AssetPack parentPack, IEnvironmentStateProvider environmentProvider, VM_SpecificNPCAssignmentsUI specificAssignmentsUI, VM_SpecificNPCAssignment.VM_MixInSpecificAssignment.Factory mixInFactory, Logger logger)
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

            DeleteMissingDescriptors = new RelayCommand(
                canExecute: _ => true,
                execute: _ => parentPack.DeleteMissingDescriptors()
            );

            AddMixInToSpecificAssignments = new RelayCommand(
                canExecute: _ => true,
                execute: _ => { 
                    foreach (var assignment in specificAssignmentsUI.Assignments.Where(x => VM_SpecificNPCAssignment.GetGender(x.AssociatedModel.NPCFormKey, logger, environmentProvider) == _parent.Gender).ToArray())
                    {
                        var existingMixInAssignment = assignment.AssociatedModel.MixInAssignments.Where(x => x.AssetPackName == _parent.GroupName).FirstOrDefault();
                        if (existingMixInAssignment != null)
                        {
                            if (OverrideExistingSNA)
                            {
                                existingMixInAssignment.DeclinedAssignment = AsDeclinedSNA;
                            }
                        }
                        else
                        {
                            var newMixIn = new NPCAssignment.MixInAssignment();
                            newMixIn.AssetPackName = _parent.GroupName;
                            newMixIn.DeclinedAssignment = AsDeclinedSNA;
                            assignment.AssociatedModel.MixInAssignments.Add(newMixIn);
                        }
                    }
                }
            );
        }

        public RelayCommand SetAllowedDescriptorMatchModes { get; }
        public DescriptorMatchMode AllowedDescriptorMatchMode { get; set; } = DescriptorMatchMode.All;
        public RelayCommand SetDisallowedDescriptorMatchModes { get; }
        public DescriptorMatchMode DisallowedDescriptorMatchMode { get; set; } = DescriptorMatchMode.Any;
        public RelayCommand DeleteMissingDescriptors { get; }
        public bool ShowMixInCommands { get; set; } = false;
        public RelayCommand AddMixInToSpecificAssignments { get; }
        public bool AsDeclinedSNA { get; set; }
        public bool OverrideExistingSNA { get; set; }

        private const string AllowedStr = "Allowed";
        private const string DisallowedStr = "Disallowed";
        public ObservableCollection<ModKey> AssociatedBsaModKeys { get; set; } = new();
        public IEnumerable<ModKey> LoadOrder { get; private set; }

        /// <summary>Loads the associated BSA mod keys from a model.</summary>
        /// <param name="model">The asset-pack model to read.</param>
        public void CopyInViewModelFromModel(AssetPack model)
        {
            AssociatedBsaModKeys.Clear();
            AssociatedBsaModKeys.AddRange(model.AssociatedBsaModKeys);
        }

        /// <summary>Writes the associated BSA mod keys back into a model.</summary>
        /// <param name="model">The asset-pack model to update.</param>
        public void MergeIntoModel(AssetPack model)
        {
            model.AssociatedBsaModKeys.Clear();
            model.AssociatedBsaModKeys.AddRange(AssociatedBsaModKeys);
        }

        /// <summary>Refreshes the source-validity color of the displayed subgroup's paths (e.g. after BSA associations change).</summary>
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

        /// <summary>Applies a descriptor match mode to the displayed subgroup and recursively to every subgroup (and replacer subgroup) in the pack.</summary>
        /// <param name="descriptorTypes">"Allowed" or "Disallowed".</param>
        /// <param name="mode">The match mode to apply.</param>
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

        /// <summary>Recursively sets the allowed/disallowed BodyGen and BodySlide descriptor match modes on a subgroup and all its descendants.</summary>
        /// <param name="subgroup">The subgroup to update.</param>
        /// <param name="descriptorType">"Allowed" or "Disallowed".</param>
        /// <param name="mode">The match mode to apply.</param>
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
