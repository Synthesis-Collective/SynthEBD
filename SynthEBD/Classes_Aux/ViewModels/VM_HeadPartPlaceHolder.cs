using Mutagen.Bethesda.Fallout4;
using Mutagen.Bethesda.Plugins.Cache;
using Noggog;
using ReactiveUI;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Media;

namespace SynthEBD
{
    public class VM_HeadPartPlaceHolder : VM
    {
        private IEnvironmentStateProvider _environmentProvider;
        public delegate VM_HeadPartPlaceHolder Factory(HeadPartSetting associatedModel, ObservableCollection<VM_HeadPartPlaceHolder> parentCollection);
        public VM_HeadPartPlaceHolder(HeadPartSetting associatedModel, ObservableCollection<VM_HeadPartPlaceHolder> parentCollection, IEnvironmentStateProvider environmentProvider)
        {
            _environmentProvider = environmentProvider;

            AssociatedModel = associatedModel;
            ParentCollection = parentCollection;

            Label = associatedModel.EditorID;

            if (_environmentProvider.LinkCache.TryResolve(AssociatedModel.HeadPartFormKey, out var testHeadPartGetter))
            {
                BorderColor = CommonColors.Green;
            }
            else
            {
                BorderColor = CommonColors.Red;
            }

            DeleteMe = new SynthEBD.RelayCommand(
                canExecute: _ => true,
                execute: _ => ParentCollection.Remove(this)
            );

            this.WhenAnyValue(x => x.AssociatedViewModel.Label).Subscribe(y => Label = y).DisposeWith(this);
            this.WhenAnyValue(x => x.AssociatedViewModel.BorderColor).Subscribe(y => BorderColor = y).DisposeWith(this);
        }

        public string Label { get; set; }
        public ObservableCollection<VM_HeadPartPlaceHolder> ParentCollection { get; set; }
        public SolidColorBrush BorderColor { get; set; }
        public HeadPartSetting AssociatedModel { get; set; }
        public VM_HeadPart? AssociatedViewModel { get; set; }
        public RelayCommand DeleteMe { get; }
    }
}
