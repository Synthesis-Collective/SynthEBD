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
    [DebuggerDisplay("{Label}")]
    public class VM_BodySlidePlaceHolder : VM
    {
        private readonly PatcherState _patcherState;
        private readonly VM_SettingsOBody _obodyVM;
        private readonly BodySlideAnnotator _bodySlideAnnotator;
        public delegate VM_BodySlidePlaceHolder Factory(BodySlideSetting model, ObservableCollection<VM_BodySlidePlaceHolder> parentCollection);

        public VM_BodySlidePlaceHolder(BodySlideSetting model, ObservableCollection<VM_BodySlidePlaceHolder> parentCollection, PatcherState patcherState, VM_SettingsOBody oBodySettingsVM, BodySlideAnnotator bodySlideAnnotator)
        {
            _patcherState = patcherState;
            _obodyVM = oBodySettingsVM;
            _bodySlideAnnotator = bodySlideAnnotator;

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

        private void InitializeAnnotation()
        {
            bool hasAnnotations = AssociatedModel.BodyShapeDescriptors.Where(x => x.AnnotationState == BodyShapeAnnotationState.Manual).Any();
            if (hasAnnotations)
            {
                AssociatedModel.AnnotationState = BodyShapeAnnotationState.Manual;
            }

            if (_patcherState.OBodySettings.AutoApplyMissingAnnotations) // Trigger from _patcherState rather than the MiscUI VM because the BodySlide VMs load first
            {
                _bodySlideAnnotator.AnnotateBodySlide(AssociatedModel, _patcherState.OBodySettings.BodySlideClassificationRules, false, null);
            }
        }

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
                Label = Label.TrimEnd(selectedCloneIndex.ToString()) + cloneIndex.ToString();
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
