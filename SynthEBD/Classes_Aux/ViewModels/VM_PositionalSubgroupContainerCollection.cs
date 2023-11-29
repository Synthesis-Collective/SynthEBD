using DynamicData.Binding;
using GongSolutions.Wpf.DragDrop;
using Noggog;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SynthEBD
{
    public class VM_PositionalSubgroupContainerCollection : VM, IDropTarget
    {
        private readonly Logger _logger;
        public delegate VM_PositionalSubgroupContainerCollection Factory(VM_AssetPack parent);
        public VM_PositionalSubgroupContainerCollection(VM_AssetPack parent, Logger logger)
        {
            ParentConfig = parent;
            _logger = logger;

            ContainersByIndex.ToObservableChangeSet().Subscribe(x => ToggleInstructionString()).DisposeWith(this);
        }
        public ObservableCollection<VM_PositionalSubgroupContainer> ContainersByIndex { get; set; } = new();
        public VM_AssetPack ParentConfig { get; set; }
        public string InstructionString { get; set; } = String.Empty;
        private string _instructionString = "Drag Subgroups here from the Tree View";

        public void InitializeFromCollection(IEnumerable<string> subgroupIDs)
        {
            foreach (var id in subgroupIDs)
            {
                if (ParentConfig.TryGetSubgroupByID(id, out var placeholder))
                {
                    AddSubgroup(placeholder);
                }
                else
                {
                    _logger.LogError(ParentConfig.GroupName + ": Positional Subgroup List could not find a subgroup with ID " + id);
                }
            }

            ToggleInstructionString();
        }

        public bool ContainsSubgroup(VM_SubgroupPlaceHolder subgroup)
        {
            return ContainersByIndex.SelectMany(x => x.ContainedSubgroupPlaceholders).Select(X => X.Subgroup).Contains(subgroup);
        }

        public string[] DumpToCollection()
        {
            return ContainersByIndex.SelectMany(x => x.ContainedSubgroupPlaceholders).Select(x => x.Subgroup.ID).ToArray();
        }

        public void AddSubgroup(VM_SubgroupPlaceHolder subgroup)
        {
            var topLevelIndex = subgroup.GetTopLevelIndex();
            var targetContainer = ContainersByIndex.Where(x => x.TopLevelIndex == topLevelIndex).FirstOrDefault();

            if (targetContainer == null)
            {
                ContainersByIndex.Add(new(subgroup, this));
                ContainersByIndex.Sort(x => x.TopLevelIndex, false);
            }
            else
            {
                targetContainer.AddSubgroup(subgroup);
            }
        }

        public void RemoveSubgroup(VM_SubgroupPlaceHolder subgroup)
        {
            for (int i = 0; i < ContainersByIndex.Count; i++)
            {
                var container = ContainersByIndex[i];
                for (int j = 0; j < container.ContainedSubgroupPlaceholders.Count; j++)
                {
                    var entry = container.ContainedSubgroupPlaceholders[j];

                    if (entry.Subgroup == subgroup)
                    {
                        entry.RemoveMe();
                        return;
                    }
                }
            }
        }

        private void ToggleInstructionString()
        {
            if (ContainersByIndex.Any())
            {
                InstructionString = String.Empty;
            }
            else
            {
                InstructionString = _instructionString;
            }
        }

        public void DragOver(IDropInfo dropInfo)
        {
            if (dropInfo.Data is VM_SubgroupPlaceHolder)
            {
                dropInfo.DropTargetAdorner = DropTargetAdorners.Highlight;
                dropInfo.Effects = DragDropEffects.Move;
            }
        }

        public void Drop(IDropInfo dropInfo)
        {
            // Assuming dropInfo.VisualTarget is the UI element being dropped on
            if (dropInfo.Data is VM_SubgroupPlaceHolder && dropInfo.VisualTarget is UC_PositionalSubgroupContainerCollection)
            {
                var collectionUserControl = dropInfo.VisualTarget as UC_PositionalSubgroupContainerCollection;
                if (collectionUserControl != null)
                {
                    var collectionViewModel = collectionUserControl.DataContext as VM_PositionalSubgroupContainerCollection;
                    if (collectionViewModel != null)
                    {
                        var draggedSubgroup = (VM_SubgroupPlaceHolder)dropInfo.Data;
                        if (draggedSubgroup != null)
                        {
                            if (collectionViewModel.ContainsSubgroup(draggedSubgroup))
                            {
                                CustomMessageBox.DisplayNotificationOK("Invalid Operation", "This list already contains subgroup " + draggedSubgroup.ID + ": " + draggedSubgroup.Name);
                            }
                            else
                            {
                                collectionViewModel.AddSubgroup(draggedSubgroup);
                            }
                        }
                    }
                }
            }
        }
    }
}
