using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ReactiveUI;

namespace SynthEBD
{
    public class VM_AssetReplacementAssignment : INotifyPropertyChanged
    {
        public VM_AssetReplacementAssignment(VM_AssetPack parent, ObservableCollection<VM_AssetReplacementAssignment> parentCollection)
        {
            ReplacerName = "";
            SubgroupIDs = new ObservableCollection<VM_CollectionMemberString>();
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
            });

            this.WhenAnyValue(x => x.ParentAssetPack).Subscribe(x =>
            {
                if (parent != null)
                {
                    SubscribedReplacerGroup = parent.ReplacersMenu.ReplacerGroups.Where(x => x.Label == ReplacerName).FirstOrDefault();
                }
            });
        }
        public string ReplacerName { get; set; }
        public ObservableCollection<VM_CollectionMemberString> SubgroupIDs { get; set; }
        public VM_AssetPack ParentAssetPack { get; set; }
        public ObservableCollection<VM_AssetReplacementAssignment> ParentCollection { get; set; }

        public VM_AssetReplacerGroup SubscribedReplacerGroup { get; set; }
        public RelayCommand DeleteCommand { get; set; }
        public RelayCommand AddSubgroupCommand { get; set; }

        public event PropertyChangedEventHandler PropertyChanged;

        public static VM_AssetReplacementAssignment GetViewModelFromModel(NPCAssignment.AssetReplacerAssignment model, VM_AssetPack parentAssetPack, ObservableCollection<VM_AssetReplacementAssignment> parentCollection)
        {
            VM_AssetReplacementAssignment viewModel = new VM_AssetReplacementAssignment(parentAssetPack, parentCollection);
            viewModel.ReplacerName = model.ReplacerName;
            foreach (var id in model.SubgroupIDs)
            {
                viewModel.SubgroupIDs.Add(new VM_CollectionMemberString(id, viewModel.SubgroupIDs));
            }
            return viewModel;
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
}
