using DynamicData;
using DynamicData.Binding;
using ReactiveUI;
using Noggog;
using System.Collections.ObjectModel;
using System.Reactive.Linq;

namespace SynthEBD
{
    public class VM_AssetOrderingMenu : VM
    {
        private readonly VM_SettingsTexMesh _texMeshVM;
        public const string PrimaryLabel = "Primary & Body Shape";
        
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

        private void UpdateAssignmentOrder()
        {
            if (!AssignmentOrder.Contains(PrimaryLabel))
            {
                AssignmentOrder.Add(PrimaryLabel);
            }
            // add any new mix ins to list
            var mixInLabels = _texMeshVM.AssetPacks.Where(x => x.ConfigType == AssetPackType.MixIn && x.IsSelected).Select(x => x.GroupName);
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

        public void CopyInFromViewModel(List<string> assetOrder)
        {
            AssignmentOrder.Clear();
            foreach (var asset in assetOrder)
            {
                if (_texMeshVM.AssetPacks.Select(x => x.GroupName).Contains(asset))
                {
                    AssignmentOrder.Add(asset);
                }
            }
        }

        public List<string> DumpToModel()
        {
            return AssignmentOrder.ToList();
        }
    }
}
