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
    public class VM_PositionalSubgroupContainer : VM
    {
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

        public void AddSubgroup(VM_SubgroupPlaceHolder subgroup)
        {
            ContainedSubgroupPlaceholders.Add(new(subgroup, this));
        }

        public void DeleteIfEmpty()
        {
            if (!ContainedSubgroupPlaceholders.Any())
            {
                ParentCollection.ContainersByIndex.Remove(this);
            }
        }
    }

    public class VM_PositionalSubgroupEntry : VM
    {
        public VM_PositionalSubgroupEntry(VM_SubgroupPlaceHolder content, VM_PositionalSubgroupContainer parentContainer)
        {
            Subgroup = content;
            ParentContainer = parentContainer;
            RefreshSeparator();
            ParentContainer.ContainedSubgroupPlaceholders.ToObservableChangeSet().Subscribe(_ => RefreshSeparator()).DisposeWith(this);

            DeleteMe = new RelayCommand(
                canExecute: _ => true,
                execute: _ => RemoveMe()
            );
        }
        public VM_SubgroupPlaceHolder Subgroup { get; set; }
        public string Separator { get; set; }
        public VM_PositionalSubgroupContainer ParentContainer { get; set; }
        public RelayCommand DeleteMe { get; set; }

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

        public void RemoveMe()
        {
            ParentContainer.ContainedSubgroupPlaceholders.Remove(this);
            ParentContainer.DeleteIfEmpty();
        }
    }
}
