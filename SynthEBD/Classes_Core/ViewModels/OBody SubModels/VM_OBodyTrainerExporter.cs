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
                foreach (var group in AvailableSliderGroups.Where(x => x.IsSelected))
                {
                    foreach (var bodySlide in AvailableBodySlides.Where(x => x.SubscribedBodySlide.AssociatedModel.SliderGroup == group.Text))
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
                foreach (var group in AvailableSliderGroups.Where(x => x.IsSelected))
                {
                    foreach (var bodySlide in AvailableBodySlides.Where(x => x.SubscribedBodySlide.AssociatedModel.SliderGroup == group.Text))
                    {
                        bodySlide.IsSelected = false;
                    }
                    group.IsSelected= false;
                }
            });
    }
    public ObservableCollection<VM_SelectableBodySlidePlaceHolder> AvailableBodySlides { get; set; } = new();
    public ObservableCollection<VM_SelectableSlider> AvailableSliders { get; set; } = new();
    public bool PauseSliderRefresh = false;
    public ObservableCollection<VM_SelectableMenuString> AvailableDescriptors { get; set; } = new();
    public ObservableCollection<VM_SelectableMenuString> AvailableSliderGroups { get; set; } = new();
    public RelayCommand AddSelectedGroups { get; }
    public RelayCommand RemoveSelectedGroups { get; }
    public bool IsBigSelected { get; set; } = true;
    public bool IsSmallSelected { get; set; } = false;

    public void Reinitialize()
    {
        var parentVM = _parentVM();
        AvailableBodySlides.Clear();
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
            AvailableSliderGroups.Add(new() { Text = group.Key });
            var bodyslides = group.OrderBy(x => x.SubscribedBodySlide.AssociatedModel.Label).ToArray();
            AvailableBodySlides.AddRange(bodyslides);
        }

        PauseSliderRefresh = false;
        RefreshAvaliableSliderNames();

        foreach (var descriptor in parentVM.DescriptorUI.TemplateDescriptors)
        {
            AvailableDescriptors.Add(new() { Text = descriptor.Category });
        }
    }

    public void RefreshAvaliableSliderNames()
    {
        HashSet<BodySlideSlider> availableSliders = AvailableBodySlides.Where(x => x.IsSelected)
            .Select(x => x.SubscribedBodySlide.AssociatedModel)
            .SelectMany(x => x.SliderValues)
            .Select(x => x.Value)
            .ToHashSet();

        for (int i = 0; i < AvailableSliders.Count; i++)
        {
            if (!availableSliders.Contains(AvailableSliders[i].SubscribedSlider))
            {
                AvailableSliders.RemoveAt(i);
                i--;
            }
        }

        var existingSliders = AvailableSliders.Select(x => x.SubscribedSlider).ToHashSet();
        foreach (var slider in availableSliders.Where(x => !existingSliders.Contains(x)))
        {
            if (!AvailableSliders.Where(x => x.SubscribedSlider.SliderName == slider.SliderName).Any())
            {
                AvailableSliders.Add(new(slider));
            }
        }

        AvailableSliders.Sort(x => x.SubscribedSlider.SliderName, false);
    }

    private TrainerExportDTO? ExportTrainingDTO(bool selectedOnly)
    {
        var selectedBodySlides = (selectedOnly ? AvailableBodySlides.Where(x => x.IsSelected) : AvailableBodySlides)
                             .Select(x => x.SubscribedBodySlide.AssociatedModel)
                             .ToArray();

        var selectedSliders = (selectedOnly ? AvailableSliders.Where(x => x.IsSelected) : AvailableSliders)
                             .Select(x => x.SubscribedSlider.SliderName)
                             .ToArray();

        return IsBigSelected ? new TrainerExportDTO(selectedBodySlides, selectedSliders, BodySliderType.Big) :
        IsSmallSelected ? new TrainerExportDTO(selectedBodySlides, selectedSliders, BodySliderType.Small) :
        null;
    }

    private void ComputeMCA()
    {
        var dataObj = ExportTrainingDTO(false); // export matrix where each row is a categorical measurement and each column is the BodySlide's nth slider value (0 if missing)

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
        DisplayedText = SubscribedSlider.SliderName;
    }
    public BodySlideSlider SubscribedSlider { get; }
    public string DisplayedText { get; set; }
    public double Weight { get; set; }
    public bool IsSelected { get; set; }
}

public class VM_SelectableMenuString : VM
{
    public string Text { get; set; }
    public bool IsSelected { get; set; }
}

public class TrainerExportDTO
{
    public TrainerExportDTO(IList<BodySlideSetting> SelectedBodySlides, IList<string> SelectedSliders, BodySliderType type)
    {
        ColumnNames = SelectedSliders.ToArray();
        RowNames = SelectedBodySlides.Select(x => x.Label + " (" + type.ToString() + ")").ToArray();

        SliderValues = new int[SelectedBodySlides.Count, ColumnNames.Length];

        for (int i = 0; i < SelectedBodySlides.Count; i++)
        {
            var bodySlide = SelectedBodySlides[i];

            for (int j = 0; j < ColumnNames.Length; j++)
            {
                var sliderName = ColumnNames[j];

                if (bodySlide.SliderValues.ContainsKey(sliderName))
                {
                    switch (type)
                    {
                        case BodySliderType.Big:
                            SliderValues[i, j] = bodySlide.SliderValues[sliderName].Big;
                            break;
                        case BodySliderType.Small:
                            SliderValues[i, j] = bodySlide.SliderValues[sliderName].Small;
                            break;
                    }
                }
                else
                {
                    SliderValues[i, j] = 0;
                }
            }
        }
    }
    public string[] ColumnNames { get; set; }
    public string[] RowNames { get; set; }
    public int[,] SliderValues { get; set; }
}