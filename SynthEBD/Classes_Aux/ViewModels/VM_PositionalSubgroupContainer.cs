using DynamicData.Binding;
using Noggog;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SynthEBD
{
    /// <summary>View model grouping the subgroup placeholders that share one top-level position (the "OR" alternatives at a single slot) in the asset-ordering UI.</summary>
    public class VM_PositionalSubgroupContainer : VM
    {
        /// <summary>Creates the container seeded from one placeholder, capturing its top-level index and caption.</summary>
        /// <param name="seedPlaceHolder">The first subgroup placeholder in this position.</param>
        /// <param name="parentCollection">The owning container collection.</param>
        public VM_PositionalSubgroupContainer(VM_SubgroupPlaceHolder seedPlaceHolder, VM_PositionalSubgroupContainerCollection parentCollection)
        {
            TopLevelIndex = seedPlaceHolder.GetTopLevelIndex();
            DisplayedCaption = seedPlaceHolder.ParentAssetPack.Subgroups[TopLevelIndex].Name;
            ParentCollection = parentCollection;
            ContainedSubgroupPlaceholders.Add(new(seedPlaceHolder, this));
        }

        public VM_PositionalSubgroupContainerCollection ParentCollection { get; set; }
        public int TopLevelIndex { get; set; }
        public string DisplayedCaption { get; set; }
        public ObservableCollection<VM_PositionalSubgroupEntry> ContainedSubgroupPlaceholders { get; set; } = new();

        /// <summary>Adds another subgroup placeholder (an OR alternative) to this position.</summary>
        /// <param name="subgroup">The placeholder to add.</param>
        public void AddSubgroup(VM_SubgroupPlaceHolder subgroup)
        {
            ContainedSubgroupPlaceholders.Add(new(subgroup, this));
        }

        /// <summary>Removes this container from its parent collection if it has no remaining entries.</summary>
        public void DeleteIfEmpty()
        {
            if (!ContainedSubgroupPlaceholders.Any())
            {
                ParentCollection.ContainersByIndex.Remove(this);
            }
        }
    }

    /// <summary>One entry within a <see cref="VM_PositionalSubgroupContainer"/>: a subgroup placeholder plus the "OR" separator shown before all but the last entry.</summary>
    public class VM_PositionalSubgroupEntry : VM
    {
        /// <summary>Creates the entry, computing its separator and tooltip and wiring the delete command.</summary>
        /// <param name="content">The subgroup placeholder this entry represents.</param>
        /// <param name="parentContainer">The owning position container.</param>
        public VM_PositionalSubgroupEntry(VM_SubgroupPlaceHolder content, VM_PositionalSubgroupContainer parentContainer)
        {
            Subgroup = content;
            ParentContainer = parentContainer;
            RefreshSeparator();
            ParentContainer.ContainedSubgroupPlaceholders.ToObservableChangeSet().Subscribe(_ => RefreshSeparator()).DisposeWith(this);

            ToolTip = Subgroup.GetNameChain(" -> ");

            DeleteMe = new RelayCommand(
                canExecute: _ => true,
                execute: _ => RemoveMe()
            );
        }
        public VM_SubgroupPlaceHolder Subgroup { get; set; }
        public string Separator { get; set; }
        public VM_PositionalSubgroupContainer ParentContainer { get; set; }
        public string ToolTip { get; set; }
        public RelayCommand DeleteMe { get; set; }

        /// <summary>Sets <see cref="Separator"/> to " OR " unless this is the last (or only) entry in its container.</summary>
        private void RefreshSeparator()
        {
            if (!ParentContainer.ContainedSubgroupPlaceholders.Any() || ParentContainer.ContainedSubgroupPlaceholders.Last() == this)
            {
                Separator = String.Empty;
            }
            else
            {
                Separator = " OR ";
            }
        }

        /// <summary>Removes this entry from its container and deletes the container if it becomes empty.</summary>
        public void RemoveMe()
        {
            ParentContainer.ContainedSubgroupPlaceholders.Remove(this);
            ParentContainer.DeleteIfEmpty();
        }
    }
}
