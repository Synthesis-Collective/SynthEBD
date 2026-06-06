using DynamicData;
using DynamicData.Binding;
using ReactiveUI;
using Noggog;
using System.Collections.ObjectModel;
using System.Reactive.Linq;

namespace SynthEBD
{
    /// <summary>View model for the asset assignment-order UI: maintains the ordered list of the primary pack plus selected mix-in packs, keeping it in sync as packs are added/removed/selected.</summary>
    public class VM_AssetOrderingMenu : VM
    {
        private readonly VM_SettingsTexMesh _texMeshVM;
        /// <summary>Caption for the fixed primary/body-shape entry that always heads the order.</summary>
        public const string PrimaryLabel = "Primary & Body Shape";

        /// <summary>Creates the menu and subscribes to keep the assignment order synced to the asset-pack list and selections.</summary>
        /// <param name="texMeshVM">The texture/mesh settings VM holding the asset packs.</param>
        public VM_AssetOrderingMenu(VM_SettingsTexMesh texMeshVM)
        {
            _texMeshVM = texMeshVM;

            _texMeshVM.AssetPacks.ToObservableChangeSet().Subscribe(_ => UpdateAssignmentOrder()).DisposeWith(this);

            _texMeshVM.AssetPacks
            .ToObservableChangeSet()
            .Transform(x =>
                x.WhenAnyObservable(y => y.UpdateOrderingMenu)
                .Subscribe(_ => UpdateAssignmentOrder()))
            .DisposeMany() // Dispose subscriptions related to removed attributes
            .Subscribe()  // Execute my instructions
            .DisposeWith(this);
        }

        public ObservableCollection<string> AssignmentOrder { get; set; } = new();

        /// <summary>Ensures the primary entry is present, appends newly-selected mix-ins, and drops mix-ins that are no longer selected.</summary>
        private void UpdateAssignmentOrder()
        {
            if (!AssignmentOrder.Contains(PrimaryLabel))
            {
                AssignmentOrder.Add(PrimaryLabel);
            }
            // add any new mix ins to list
            var mixInLabels = _texMeshVM.AssetPacks.Where(x => x.ConfigType == AssetPackType.MixIn && x.IsSelected).Select(x => x.GroupName).ToArray();
            foreach (var mixin in mixInLabels)
            {
                if (!AssignmentOrder.Contains(mixin))
                {
                    AssignmentOrder.Add(mixin);
                }
            }

            // remove any deleted mixins from list
            for (int i = 0; i < AssignmentOrder.Count; i++)
            {
                var mixin = AssignmentOrder[i];
                if (mixin == PrimaryLabel) { continue; }
                if (!mixInLabels.Contains(mixin))
                {
                    AssignmentOrder.RemoveAt(i);
                    i--;
                }
            }
        }

        /// <summary>Loads the saved order, keeping only entries that still correspond to a known pack (or the primary), then re-syncs.</summary>
        /// <param name="assetOrder">The saved order; null is ignored.</param>
        public void CopyInFromModel(List<string> assetOrder)
        {
            if (assetOrder == null)
            {
                return;
            }

            AssignmentOrder.Clear();
            foreach (var asset in assetOrder)
            {
                if (_texMeshVM.AssetPacks.Select(x => x.GroupName).Contains(asset) || asset == PrimaryLabel)
                {
                    AssignmentOrder.Add(asset);
                }
            }
            UpdateAssignmentOrder();
        }

        /// <summary>Returns the current assignment order as a list.</summary>
        /// <returns>The ordered entry labels.</returns>
        public List<string> DumpToModel()
        {
            return AssignmentOrder.ToList();
        }
    }
}
