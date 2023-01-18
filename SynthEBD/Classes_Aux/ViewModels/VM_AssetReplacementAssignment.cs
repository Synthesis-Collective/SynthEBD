using System.Collections.ObjectModel;
using ReactiveUI;
using Noggog;

namespace SynthEBD;

public class VM_AssetReplacementAssignment : VM
{
    public VM_AssetReplacementAssignment(VM_AssetPack parent, ObservableCollection<VM_AssetReplacementAssignment> parentCollection)
    {
        ParentAssetPack = parent;
        ParentCollection = parentCollection;

        DeleteCommand = new SynthEBD.RelayCommand(
            canExecute: _ => true,
            execute: x => ParentCollection.Remove(this)
        );

        AddSubgroupCommand = new SynthEBD.RelayCommand(
            canExecute: _ => true,
            execute: x => SubgroupIDs.Add(new VM_CollectionMemberString("", SubgroupIDs))
        );

        this.WhenAnyValue(x => x.ReplacerName).Subscribe(x =>
        {
            if (parent != null)
            {
                SubscribedReplacerGroup = parent.ReplacersMenu.ReplacerGroups.Where(x => x.Label == ReplacerName).FirstOrDefault();
            }
        }).DisposeWith(this);

        this.WhenAnyValue(x => x.ParentAssetPack).Subscribe(x =>
        {
            if (parent != null)
            {
                SubscribedReplacerGroup = parent.ReplacersMenu.ReplacerGroups.Where(x => x.Label == ReplacerName).FirstOrDefault();
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

    public void CopyInViewModelFromModel(NPCAssignment.AssetReplacerAssignment model)
    {
        this.ReplacerName = model.ReplacerName;
        foreach (var id in model.SubgroupIDs)
        {
            this.SubgroupIDs.Add(new VM_CollectionMemberString(id, this.SubgroupIDs));
        }
    }

    public static NPCAssignment.AssetReplacerAssignment DumpViewModelToModel(VM_AssetReplacementAssignment viewModel)
    {
        NPCAssignment.AssetReplacerAssignment model = new NPCAssignment.AssetReplacerAssignment();
        model.AssetPackName = viewModel.ParentAssetPack.GroupName;
        model.ReplacerName = viewModel.ReplacerName;
        model.SubgroupIDs = viewModel.SubgroupIDs.Select(x => x.Content).ToList();
        return model;
    }
}