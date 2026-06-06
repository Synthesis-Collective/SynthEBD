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
    /// <summary>
    /// Lightweight list-item view model for a <see cref="NPCAssignment"/> in the Specific NPC
    /// Assignments UI. Holds the display name and the model, and lazily owns the heavyweight
    /// <see cref="VM_SpecificNPCAssignment"/> editor that is built when the row is selected.
    /// </summary>
    public class VM_SpecificNPCAssignmentPlaceHolder : VM
    {
        private readonly VM_SettingsTexMesh _texMeshSettings;
        /// <summary>Autofac factory delegate for constructing a <see cref="VM_SpecificNPCAssignmentPlaceHolder"/>.</summary>
        public delegate VM_SpecificNPCAssignmentPlaceHolder Factory(NPCAssignment model, ObservableCollection<VM_SpecificNPCAssignmentPlaceHolder> parentCollection);
        /// <summary>
        /// Seeds the model, display name, and parent collection, and keeps <see cref="DispName"/>
        /// in sync with the associated editor view model's display name.
        /// </summary>
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

        /// <summary>
        /// Replaces this assignment's asset order with the order currently configured in the main
        /// Tex/Mesh asset-ordering menu, and pushes the result into the editor view model if open.
        /// </summary>
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
