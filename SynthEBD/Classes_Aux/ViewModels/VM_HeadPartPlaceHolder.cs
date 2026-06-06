using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Skyrim;
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
    /// <summary>
    /// Lightweight list-row view model standing in for a head part in the head-part list: shows its label
    /// and a validity border color, and mirrors the full <see cref="AssociatedViewModel"/>'s label/color
    /// when one is bound.
    /// </summary>
    public class VM_HeadPartPlaceHolder : VM
    {
        private IEnvironmentStateProvider _environmentProvider;
        /// <summary>Autofac factory delegate for constructing a placeholder within a parent collection.</summary>
        public delegate VM_HeadPartPlaceHolder Factory(HeadPartSetting associatedModel, ObservableCollection<VM_HeadPartPlaceHolder> parentCollection);
        /// <summary>Creates the row, resolving its label/validity from the head-part record and wiring delete + label/color mirroring.</summary>
        /// <param name="associatedModel">The head-part setting this row represents.</param>
        /// <param name="parentCollection">The list this row belongs to.</param>
        /// <param name="environmentProvider">Supplies the link cache for resolving the head part.</param>
        public VM_HeadPartPlaceHolder(HeadPartSetting associatedModel, ObservableCollection<VM_HeadPartPlaceHolder> parentCollection, IEnvironmentStateProvider environmentProvider)
        {
            _environmentProvider = environmentProvider;

            AssociatedModel = associatedModel;
            ParentCollection = parentCollection;

            Label = associatedModel.EditorID;
            if (Label.IsNullOrWhitespace())
            {
                Label = EditorIDHandler.GetEditorIDSafely<IHeadPartGetter>(associatedModel.HeadPartFormKey, _environmentProvider.LinkCache);
            }

            if (_environmentProvider.LinkCache.TryResolve<IHeadPartGetter>(AssociatedModel.HeadPartFormKey, out var testHeadPartGetter))
            {
                BorderColor = CommonColors.Green;
            }
            else
            {
                BorderColor = CommonColors.Red;
            }

            DeleteMe = new RelayCommand(
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
