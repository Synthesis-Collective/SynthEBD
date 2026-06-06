using Noggog;
using ReactiveUI;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Media;

namespace SynthEBD
{
    /// <summary>
    /// Lightweight list-node view model for a <see cref="BodySlideSetting"/> in the OBody BodySlide list.
    /// Holds display label/border color and visibility (hidden vs. shown), runs auto-annotation on load,
    /// and lazily references the heavyweight <see cref="VM_BodySlideSetting"/> only while selected.
    /// </summary>
    [DebuggerDisplay("{Label}")]
    public class VM_BodySlidePlaceHolder : VM
    {
        private readonly PatcherState _patcherState;
        private readonly VM_SettingsOBody _obodyVM;
        private readonly BodySlideAnnotator _bodySlideAnnotator;
        private readonly AnnotationLibraryAnnotator _libraryAnnotator;
        /// <summary>Autofac factory delegate for <see cref="VM_BodySlidePlaceHolder"/>.</summary>
        public delegate VM_BodySlidePlaceHolder Factory(BodySlideSetting model, ObservableCollection<VM_BodySlidePlaceHolder> parentCollection);

        /// <summary>Seeds label/border color, runs initial annotation, and subscribes to mirror the associated VM's label/color plus recompute visibility from the hidden flag and the menu's "show hidden" toggle.</summary>
        public VM_BodySlidePlaceHolder(BodySlideSetting model, ObservableCollection<VM_BodySlidePlaceHolder> parentCollection, PatcherState patcherState, VM_SettingsOBody oBodySettingsVM, BodySlideAnnotator bodySlideAnnotator, AnnotationLibraryAnnotator libraryAnnotator)
        {
            _patcherState = patcherState;
            _obodyVM = oBodySettingsVM;
            _bodySlideAnnotator = bodySlideAnnotator;
            _libraryAnnotator = libraryAnnotator;

            AssociatedModel = model;
            Label = model.Label;
            ParentCollection = parentCollection;

            InitializeAnnotation();
            InitializeBorderColor();

            this.WhenAnyValue(x => x.AssociatedViewModel.Label).Subscribe(y => Label = y).DisposeWith(this);
            this.WhenAnyValue(x => x.AssociatedViewModel.BorderColor).Subscribe(y => BorderColor = y).DisposeWith(this);

            IsHidden = AssociatedModel.HideInMenu;

            this.WhenAnyValue(x => x.IsHidden).Subscribe(x =>
            {
                if (!oBodySettingsVM.BodySlidesUI.ShowHidden && IsHidden)
                {
                    IsVisible = false;
                }
                else
                {
                    IsVisible = true;
                }
                if (AssociatedViewModel != null)
                {
                    AssociatedViewModel.UpdateStatusDisplay();
                }  
            }).DisposeWith(this);

        }

        public string Label { get; set; }
        public bool IsVisible { get; set; } = true;
        public bool IsHidden { get; set; } = false; // not the same as IsVisible. IsVisible can be set true if the "show hidden" button is checked.
        public SolidColorBrush BorderColor { get; set; }
        public BodySlideSetting AssociatedModel { get; set; }
        public VM_BodySlideSetting? AssociatedViewModel { get; set; }
        public ObservableCollection<VM_BodySlidePlaceHolder> ParentCollection { get; set; }

        /// <summary>Marks the model as manually annotated if any manual descriptors exist, then (when auto-apply is enabled) runs the annotation-library and rule-based annotators in sequence.</summary>
        private void InitializeAnnotation()
        {
            bool hasAnnotations = AssociatedModel.EnumerateAllDescriptors().Any(x => x.Source == BodyShapeAnnotationSource.Manual);
            if (hasAnnotations)
            {
                AssociatedModel.AnnotationState = BodyShapeAnnotationState.Manual;
            }

            if (_patcherState.OBodySettings.AutoApplyMissingAnnotations) // Trigger from _patcherState rather than the MiscUI VM because the BodySlide VMs load first
            {
                // Tier 1: annotation library (per-weight descriptors shipped with SynthEBD or provided by the user)
                _libraryAnnotator.Annotate(AssociatedModel);

                // Tier 2: rule-based annotator
                _bodySlideAnnotator.AnnotateBodySlide(AssociatedModel, _patcherState.OBodySettings.BodySlideClassificationRules, _patcherState.OBodySettings.TemplateDescriptors.Flatten().Select(x => x.ID).ToHashSet(), false, null);
            }
        }

        /// <summary>Sets the border color: "missing" if the referenced BodySlide no longer exists, "hidden" if hidden in menu, otherwise the color for the model's annotation state. Mirrors VM_BodySlideSetting.UpdateStatusDisplay().</summary>
        public void InitializeBorderColor() // this should follow the same logic as VM_BodySlideSettings.UpdateStatusDisplay()
        {
            if (!_patcherState.OBodySettings.CurrentlyExistingBodySlides.Contains(AssociatedModel.ReferencedBodySlide))
            {
                BorderColor = VM_BodySlideSetting.BorderColorMissing;
            }
            else if (AssociatedModel.HideInMenu)
            {
                BorderColor = VM_BodySlideSetting.BorderColorHidden;
            }
            else
            {
                BorderColor = VM_BodySlideSetting.AnnotationToColor[AssociatedModel.AnnotationState];
            }
        }

        /// <summary>Appends/bumps a trailing numeric suffix on this clone's label to make it unique among siblings referencing the same BodySlide, updating model and associated VM. Returns the parent-collection index of the last existing clone (a suggested insert position).</summary>
        public int RenameByIndex()
        {
            int cloneIndex = 0;
            int lastClonePosition = 0;

            for (int i = 0; i < ParentCollection.Count; i++)
            {
                var clone = ParentCollection[i];
                if (AssociatedModel.ReferencedBodySlide != clone.AssociatedModel.ReferencedBodySlide) 
                { 
                    continue; 
                }
                lastClonePosition = i;
                if (GetTrailingInt(clone.Label, out int currentIndex) && currentIndex > cloneIndex)
                {
                    cloneIndex = currentIndex;
                }
            }

            if (cloneIndex == 0)
            {
                cloneIndex = 2;
            }
            else
            {
                cloneIndex++;
            }

            if (GetTrailingInt(Label, out int selectedCloneIndex))
            {
                Label = Label.TrimEnd(selectedCloneIndex.ToString().ToArray()) + cloneIndex.ToString();
            }
            else
            {
                Label += cloneIndex;
            }

            AssociatedModel.Label = Label;
            if (AssociatedViewModel != null)
            {
                AssociatedViewModel.Label = Label;
            }
            return lastClonePosition;
        }

        /// <summary>Parses the trailing run of digits at the end of <paramref name="input"/> into <paramref name="number"/>; returns false if there is no trailing integer.</summary>
        private static bool GetTrailingInt(string input, out int number)
        {
            number = 0;
            var stack = new Stack<char>();

            for (var i = input.Length - 1; i >= 0; i--)
            {
                if (!char.IsNumber(input[i]))
                {
                    break;
                }

                stack.Push(input[i]);
            }

            var result = new string(stack.ToArray());
            if (result == null || !int.TryParse(result, out number))
            {
                return false;
            }
            else
            {
                return true;
            }
        }
    }
}
