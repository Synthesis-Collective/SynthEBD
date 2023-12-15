using Noggog;
using ReactiveUI;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Composition;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SynthEBD;

public class VM_OBodyTrainerExporter : VM
{
    private readonly Func<VM_SettingsOBody> _parentVM;

    public VM_OBodyTrainerExporter(Func<VM_SettingsOBody> parentVM)
    {
        _parentVM = parentVM;

        AddSelectedGroups = new RelayCommand(
            canExecute: _ => true,
            execute: _ =>
            {
                PauseSliderRefresh = true;
                foreach (var group in SelectedSliderGroups.Where(x => x.IsSelected))
                {
                    foreach (var bodySlide in SelectedBodySlides.Where(x => x.SubscribedBodySlide.AssociatedModel.SliderGroup == group.Text))
                    {
                        bodySlide.IsSelected = true;
                    }
                    group.IsSelected = false;
                }
                PauseSliderRefresh = false;
                RefreshAvaliableSliderNames();
            });

        RemoveSelectedGroups = new RelayCommand(
            canExecute: _ => true,
            execute: _ =>
            {
                foreach (var group in SelectedSliderGroups.Where(x => x.IsSelected))
                {
                    foreach (var bodySlide in SelectedBodySlides.Where(x => x.SubscribedBodySlide.AssociatedModel.SliderGroup == group.Text))
                    {
                        bodySlide.IsSelected = false;
                    }
                    group.IsSelected= false;
                }
            });
    }
    public ObservableCollection<VM_SelectableBodySlidePlaceHolder> SelectedBodySlides { get; set; } = new();
    public ObservableCollection<VM_SelectableSlider> SelectedSliders { get; set; } = new();
    public bool PauseSliderRefresh = false;
    public ObservableCollection<VM_SelectableMenuString> SelectedDescriptors { get; set; } = new();
    public ObservableCollection<VM_SelectableMenuString> SelectedSliderGroups { get; set; } = new();
    public RelayCommand AddSelectedGroups { get; }
    public RelayCommand RemoveSelectedGroups { get; }

    public void Reinitialize()
    {
        var parentVM = _parentVM();
        SelectedBodySlides.Clear();
        PauseSliderRefresh = true; // lock down until the intial set is loaded
        var toAdd = new List<VM_SelectableBodySlidePlaceHolder>();
        foreach (var bs in parentVM.BodySlidesUI.CurrentlyDisplayedBodySlides)
        {
            var newShell = new VM_SelectableBodySlidePlaceHolder(bs,this);
            if (parentVM.AnnotatorUI.DisplayedRuleSet != null && bs.AssociatedModel.SliderGroup == parentVM.AnnotatorUI.DisplayedRuleSet.BodyTypeGroup)
            {
                newShell.IsSelected = true;
            }
            toAdd.Add(newShell);
        }

        var groups = toAdd.GroupBy(x => x.SubscribedBodySlide.AssociatedModel.SliderGroup).OrderBy(x => x.Key).ToArray();
        foreach (var group in groups)
        {
            SelectedSliderGroups.Add(new() { Text = group.Key });
            var bodyslides = group.OrderBy(x => x.SubscribedBodySlide.AssociatedModel.Label).ToArray();
            SelectedBodySlides.AddRange(bodyslides);
        }

        PauseSliderRefresh = false;
        RefreshAvaliableSliderNames();

        foreach (var descriptor in parentVM.DescriptorUI.TemplateDescriptors)
        {
            SelectedDescriptors.Add(new() { Text = descriptor.Category });
        }
    }

    public void RefreshAvaliableSliderNames()
    {
        HashSet<BodySlideSlider> availableSliders = SelectedBodySlides.Where(x => x.IsSelected)
            .Select(x => x.SubscribedBodySlide.AssociatedModel)
            .SelectMany(x => x.SliderValues)
            .Select(x => x.Value)
            .ToHashSet();

        for (int i = 0; i < SelectedSliders.Count; i++)
        {
            if (!availableSliders.Contains(SelectedSliders[i].SubscribedSlider))
            {
                SelectedSliders.RemoveAt(i);
                i--;
            }
        }

        var existingSliders = SelectedSliders.Select(x => x.SubscribedSlider).ToHashSet();
        foreach (var slider in availableSliders.Where(x => !existingSliders.Contains(x)))
        {
            if (!SelectedSliders.Where(x => x.SubscribedSlider.SliderName == slider.SliderName).Any())
            {
                SelectedSliders.Add(new(slider));
            }
        }

        SelectedSliders.Sort(x => x.SubscribedSlider.SliderName, false);
    }
}

public class VM_SelectableBodySlidePlaceHolder : VM
{
    public VM_SelectableBodySlidePlaceHolder(VM_BodySlidePlaceHolder master, VM_OBodyTrainerExporter parent)
    {
        SubscribedBodySlide = master;

        Label = string.Concat("[", master.AssociatedModel.SliderGroup, "] ", master.Label);

        this.WhenAnyValue(x => x.IsSelected).Subscribe(_ =>
        {
            if (!parent.PauseSliderRefresh)
            {
                parent.RefreshAvaliableSliderNames();
            }
        }).DisposeWith(this);
    }

    public VM_BodySlidePlaceHolder SubscribedBodySlide { get; }
    public string Label { get; set; }
    public bool IsSelected { get; set; }
}

public class VM_SelectableSlider : VM
{
    public VM_SelectableSlider(BodySlideSlider slider)
    {
        SubscribedSlider = slider;
    }
    public BodySlideSlider SubscribedSlider { get; }
    public bool IsSelected { get; set; }
}

public class VM_SelectableMenuString : VM
{
    public string Text { get; set; }
    public bool IsSelected { get; set; }
}
