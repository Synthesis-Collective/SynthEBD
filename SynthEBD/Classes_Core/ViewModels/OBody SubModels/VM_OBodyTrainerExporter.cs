using Microsoft.ML;
using Microsoft.ML.Data;
using Mutagen.Bethesda.Plugins;
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
    private readonly SynthEBDPaths _paths;
    private readonly IO_Aux _auxIO;
    public VM_OBodyTrainerExporter(Func<VM_SettingsOBody> parentVM, SynthEBDPaths paths, IO_Aux auxIO)
    {
        _parentVM = parentVM;
        _paths = paths;
        _auxIO = auxIO;

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
                    group.IsSelected = false;
                }
            });

        SelectAllSliders = new RelayCommand(
            canExecute: _ => true,
            execute: _ =>
            {
                foreach (var slider in AvailableSliders)
                {
                    slider.IsSelected = true;
                }
            });

        DeselectAllSliders = new RelayCommand(
            canExecute: _ => true,
            execute: _ =>
            {
                foreach (var slider in AvailableSliders)
                {
                    slider.IsSelected = false;
                }
            });

        TrainModel = new RelayCommand(
            canExecute: _ => true,
            execute: _ =>
            {
                CreateModel();
            });

        ExportTrainingSet = new RelayCommand(
            canExecute: _ => true,
            execute: _ =>
            {
                var currentDescriptor = AvailableDescriptors.Where(x => x.IsSelected).FirstOrDefault()?.Text ?? string.Empty;
                if (currentDescriptor == String.Empty)
                {
                    MessageWindow.DisplayNotificationOK("Error", "You must select a Descriptor to export annotations for");
                    return;
                }
                var fileName = "TrainingSet_" + currentDescriptor + "_" + DateTime.Now.ToString("yyyy-MM-dd-HH-mm", System.Globalization.CultureInfo.InvariantCulture);
                if (IO_Aux.SelectFileSave("", "CSV files (.csv|*.csv", ".csv", "Save Data Set", out string savePath, fileName))
                {
                    List<string> output = new();
                    var data = ExportTrainingLearningDTO(true, currentDescriptor);

                    var firstLine = "Label," + string.Join(",", data.SliderNames);
                    output.Add(firstLine);
                    foreach (var entry in data.DataEntries)
                    {
                        if (entry.Classification.IsNullOrWhitespace())
                        {
                            continue;
                        }
                        output.Add(entry.Classification + "," + String.Join(",", entry.Sliders));
                    }

                    var outputStr = string.Join(Environment.NewLine, output);

                    Task.Run(() => PatcherIO.WriteTextFileStatic(savePath, outputStr));
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
    public RelayCommand SelectAllSliders { get; }
    public RelayCommand DeselectAllSliders { get; }
    public bool IsBigSelected { get; set; } = true;
    public bool IsSmallSelected { get; set; } = false;
    public RelayCommand TrainModel { get; }
    public RelayCommand ExportTrainingSet { get; }

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

        foreach (var slider in AvailableSliders)
        {
            slider.DisplayedText = slider.SubscribedSlider.SliderName + " (" + GetSliderCount(slider.SubscribedSlider.SliderName).ToString() + ")";
        }
    }

    public int GetSliderCount(string sliderName)
    {
        int count = 0;
        foreach (var bodyslide in AvailableBodySlides.Where(x=> x.IsSelected).ToArray())
        {
            if (bodyslide.SubscribedBodySlide.AssociatedModel.SliderValues.ContainsKey(sliderName))
            {
                count++;
            }
        }
        return count;
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

    private TrainerExportLearningDTO? ExportTrainingLearningDTO(bool selectedOnly, string category)
    {
        var selectedBodySlides = (selectedOnly ? AvailableBodySlides.Where(x => x.IsSelected) : AvailableBodySlides)
                             .Select(x => x.SubscribedBodySlide.AssociatedModel)
                             .ToArray();

        var selectedSliders = (selectedOnly ? AvailableSliders.Where(x => x.IsSelected) : AvailableSliders)
                             .Select(x => x.SubscribedSlider.SliderName)
                             .ToArray();

        return IsBigSelected ? new TrainerExportLearningDTO(selectedBodySlides, selectedSliders, BodySliderType.Big, category) :
        IsSmallSelected ? new TrainerExportLearningDTO(selectedBodySlides, selectedSliders, BodySliderType.Small, category) :
        null;
    }


    private void CreateModel()
    {
        var currentDescriptor = AvailableDescriptors.Where(x => x.IsSelected).FirstOrDefault()?.Text ?? string.Empty;
        if (currentDescriptor == string.Empty)
        {
            return;
        }

        var trainerExportData = ExportTrainingLearningDTO(false, currentDescriptor);

        // Create a new MLContext
        var context = new MLContext();

        var data = context.Data.LoadFromEnumerable(trainerExportData.DataEntries.Select(entry =>
           new BodyslideData
           {
               BodyslideName = entry.BodyslideName,
               Classification = entry.Classification,
               Sliders = entry.Sliders
           }));

        // Define the pipeline
        var pipeline = context.Transforms.Conversion.MapValueToKey("Label", "Classification")
            .Append(context.Transforms.Concatenate("Features", "Sliders"))
            .Append(context.Transforms.Conversion.MapKeyToValue("Classification"))
            .Append(context.Transforms.Conversion.MapKeyToValue("Label"));

        // Assuming you have the following model variable after training
        ITransformer trainedModel = pipeline.Fit(data);

        // Save the model
        var modelPath = System.IO.Path.Combine(_paths.OBodySettingsPath, "Models", currentDescriptor + "_" + DateTime.Now.ToString());
        context.Model.Save(trainedModel, data.Schema, modelPath);

        IDataView predictions = trainedModel.Transform(data);
        var metrics = context.Regression.Evaluate(predictions, labelColumnName: "Label", scoreColumnName: "Score");
        

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

public class TrainerExportLearningDTO
{
    public List<BodyslideData> DataEntries { get; set; } = new();
    public List<string> SliderNames { get; set; } = new();
    public TrainerExportLearningDTO(IList<BodySlideSetting> SelectedBodySlides, IList<string> SelectedSliders, BodySliderType type, string descriptorCategory)
    {
        DataEntries = new();
        SliderNames = new(SelectedSliders);

        foreach (var bodySlide in SelectedBodySlides)
        {
            BodyslideData bsEntry = new() { BodyslideName = bodySlide.Label };
            var descriptors = bodySlide.BodyShapeDescriptors.Where(x => x.Category == descriptorCategory).ToList();
            if (descriptors.Any())
            {
                bsEntry.Classification = String.Join("|", descriptors.Select(x => x.Value));
            }

            List<int> sliderValues = new();
            foreach (var sliderName in SelectedSliders)
            {
                if (bodySlide.SliderValues.ContainsKey(sliderName))
                {
                    switch (type)
                    {
                        case BodySliderType.Big:
                            sliderValues.Add(bodySlide.SliderValues[sliderName].Big);
                            break;
                        case BodySliderType.Small:
                            sliderValues.Add(bodySlide.SliderValues[sliderName].Small);
                            break;
                    }
                }
                else
                {
                    sliderValues.Add(0);
                }
            }
            bsEntry.Sliders = sliderValues.ToArray();
            DataEntries.Add(bsEntry);
        }
    }
}

public class BodyslideData
{
    public string BodyslideName { get; set; }
    public string Classification { get; set; }
    public int[] Sliders { get; set; }
}

