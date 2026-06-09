using System.Collections.ObjectModel;
using ReactiveUI;
using Noggog;

namespace SynthEBD;

/// <summary>View model for a per-NPC assignment of a replacer asset group (by name) and the specific subgroup IDs to apply.</summary>
public class VM_AssetReplacementAssignment : VM
{
    /// <summary>Creates the assignment VM, wiring delete/add-subgroup commands and keeping the resolved replacer group in sync with the chosen name.</summary>
    /// <param name="parent">The owning asset-pack VM.</param>
    /// <param name="parentCollection">The collection of assignments this belongs to.</param>
    public VM_AssetReplacementAssignment(VM_AssetPack parent, ObservableCollection<VM_AssetReplacementAssignment> parentCollection)
    {
        ParentAssetPack = parent;
        ParentCollection = parentCollection;

        DeleteCommand = new RelayCommand(
            canExecute: _ => true,
            execute: x => ParentCollection.Remove(this)
        );

        AddSubgroupCommand = new RelayCommand(
            canExecute: _ => true,
            execute: x => SubgroupIDs.Add(new VM_CollectionMemberString("", SubgroupIDs))
        );

        this.WhenAnyValue(x => x.ReplacerName).Subscribe(x =>
        {
            if (parent != null)
            {
                SubscribedReplacerGroup = parent.ReplacersMenu.ReplacerGroups.FirstOrDefault(x => x.Label == ReplacerName);
            }
        }).DisposeWith(this);

        this.WhenAnyValue(x => x.ParentAssetPack).Subscribe(x =>
        {
            if (parent != null)
            {
                SubscribedReplacerGroup = parent.ReplacersMenu.ReplacerGroups.FirstOrDefault(x => x.Label == ReplacerName);
            }
        }).DisposeWith(this);
    }
    public string ReplacerName { get; set; } = "";
    public ObservableCollection<VM_CollectionMemberString> SubgroupIDs { get; set; } = new();
    public VM_AssetPack ParentAssetPack { get; set; }
    public ObservableCollection<VM_AssetReplacementAssignment> ParentCollection { get; set; }

    public VM_AssetReplacerGroup SubscribedReplacerGroup { get; set; }
    public RelayCommand DeleteCommand { get; set; }
    public RelayCommand AddSubgroupCommand { get; set; }

    /// <summary>Populates this VM from a persisted <see cref="NPCAssignment.AssetReplacerAssignment"/> model.</summary>
    /// <param name="model">The model to load.</param>
    public void CopyInViewModelFromModel(NPCAssignment.AssetReplacerAssignment model)
    {
        this.ReplacerName = model.ReplacerName;
        foreach (var id in model.SubgroupIDs)
        {
            this.SubgroupIDs.Add(new VM_CollectionMemberString(id, this.SubgroupIDs));
        }
    }

    /// <summary>Projects a VM back into a <see cref="NPCAssignment.AssetReplacerAssignment"/> model.</summary>
    /// <param name="viewModel">The view model to project.</param>
    /// <returns>The populated model.</returns>
    public static NPCAssignment.AssetReplacerAssignment DumpViewModelToModel(VM_AssetReplacementAssignment viewModel)
    {
        NPCAssignment.AssetReplacerAssignment model = new NPCAssignment.AssetReplacerAssignment();
        model.AssetPackName = viewModel.ParentAssetPack.GroupName;
        model.ReplacerName = viewModel.ReplacerName;
        model.SubgroupIDs = viewModel.SubgroupIDs.Select(x => x.Content).ToList();
        return model;
    }
}