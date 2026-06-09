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
    /// <summary>
    /// View model and drag-drop target for the positional subgroup-ordering list: holds one
    /// <see cref="VM_PositionalSubgroupContainer"/> per top-level position, accepts subgroup drops, and
    /// round-trips to/from the saved list of subgroup IDs.
    /// </summary>
    public class VM_PositionalSubgroupContainerCollection : VM, IDropTarget
    {
        private readonly Logger _logger;
        /// <summary>Autofac factory delegate for constructing the collection under an asset pack.</summary>
        public delegate VM_PositionalSubgroupContainerCollection Factory(VM_AssetPack parent);
        /// <summary>Creates the collection and toggles the "drag here" instruction text as containers come and go.</summary>
        /// <param name="parent">The owning asset-pack VM.</param>
        /// <param name="logger">Logger for unresolved-subgroup errors.</param>
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

        /// <summary>Populates the list from saved subgroup IDs, logging any that cannot be resolved in the parent pack.</summary>
        /// <param name="subgroupIDs">The saved subgroup IDs, in order.</param>
        public void InitializeFromCollection(IEnumerable<string> subgroupIDs)
        {
            foreach (var id in subgroupIDs.Where(x => !x.IsNullOrWhitespace()))
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

        /// <summary>Whether the given subgroup is already present anywhere in the list.</summary>
        /// <param name="subgroup">The subgroup to look for.</param>
        /// <returns><c>true</c> if present.</returns>
        public bool ContainsSubgroup(VM_SubgroupPlaceHolder subgroup)
        {
            return ContainersByIndex.SelectMany(x => x.ContainedSubgroupPlaceholders).Select(X => X.Subgroup).Contains(subgroup);
        }

        /// <summary>Returns the ordered subgroup IDs across all positions.</summary>
        /// <returns>The subgroup IDs in display order.</returns>
        public string[] DumpToCollection()
        {
            return ContainersByIndex.SelectMany(x => x.ContainedSubgroupPlaceholders).Select(x => x.Subgroup.ID).ToArray();
        }

        /// <summary>Adds a subgroup to the container for its top-level position, creating and re-sorting a new container if that position has none yet.</summary>
        /// <param name="subgroup">The subgroup to add.</param>
        public void AddSubgroup(VM_SubgroupPlaceHolder subgroup)
        {
            var topLevelIndex = subgroup.GetTopLevelIndex();
            var targetContainer = ContainersByIndex.FirstOrDefault(x => x.TopLevelIndex == topLevelIndex);

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

        /// <summary>Removes the given subgroup from whichever container holds it.</summary>
        /// <param name="subgroup">The subgroup to remove.</param>
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

        /// <summary>Shows the "drag here" instruction only when the list is empty.</summary>
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

        /// <summary>Drag-over handler: accepts a dragged subgroup placeholder with a move/highlight effect.</summary>
        /// <param name="dropInfo">The gong-wpf-dragdrop drop info.</param>
        public void DragOver(IDropInfo dropInfo)
        {
            if (dropInfo.Data is VM_SubgroupPlaceHolder)
            {
                dropInfo.DropTargetAdorner = DropTargetAdorners.Highlight;
                dropInfo.Effects = DragDropEffects.Move;
            }
        }

        /// <summary>Drop handler: adds the dropped subgroup to this list, warning if it is already present.</summary>
        /// <param name="dropInfo">The gong-wpf-dragdrop drop info.</param>
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
                                MessageWindow.DisplayNotificationOK("Invalid Operation", "This list already contains subgroup " + draggedSubgroup.ID + ": " + draggedSubgroup.Name);
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
