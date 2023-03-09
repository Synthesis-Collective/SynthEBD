using Noggog;
using ReactiveUI;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SynthEBD
{
    public class VM_SpecificNPCAssignmentPlaceHolder : VM
    {
        private readonly VM_SettingsTexMesh _texMeshSettings;
        public delegate VM_SpecificNPCAssignmentPlaceHolder Factory(NPCAssignment model, ObservableCollection<VM_SpecificNPCAssignmentPlaceHolder> parentCollection);
        public VM_SpecificNPCAssignmentPlaceHolder(NPCAssignment model, ObservableCollection<VM_SpecificNPCAssignmentPlaceHolder> parentCollection, VM_SettingsTexMesh texMeshSettings)
        {
            _texMeshSettings = texMeshSettings;

            AssociatedModel = model;
            DispName = model.DispName;
            ParentCollection = parentCollection;

            this.WhenAnyValue(x => x.AssociatedViewModel.DispName).Subscribe(y => DispName = y).DisposeWith(this);
        }
        public string DispName { get; set; } = "New Assignment";
        public NPCAssignment AssociatedModel { get; set; }
        public VM_SpecificNPCAssignment? AssociatedViewModel { get; set; }
        public ObservableCollection<VM_SpecificNPCAssignmentPlaceHolder> ParentCollection { get; }

        public void SyncAssetOrderFromMain()
        {
            AssociatedModel.AssetOrder.Clear();

            foreach (var item in _texMeshSettings.AssetOrderingMenu.AssignmentOrder)
            {
                AssociatedModel.AssetOrder.Add(item);
            }

            if (AssociatedViewModel != null)
            {
                AssociatedViewModel.AssetOrderingMenu.AssignmentOrder.Clear();
                AssociatedViewModel.AssetOrderingMenu.CopyInFromModel(AssociatedModel.AssetOrder);
            }
        }
    }
}
