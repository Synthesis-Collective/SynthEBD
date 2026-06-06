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

/// <summary>
/// View model for the OBody ML trainer/exporter. Lets the user pick BodySlide presets, slider groups, and
/// sliders, then either trains an ML.NET classification model per descriptor category or exports the labeled
/// training set as CSV.
/// </summary>
public class VM_OBodyTrainerExporter : VM
{
    private readonly Func<VM_SettingsOBody> _parentVM;
    private readonly SynthEBDPaths _paths;
    private readonly IO_Aux _auxIO;
    /// <summary>Wires the select/deselect group and slider commands, the Train-model command, and the Export-training-set (CSV) command.</summary>
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

    /// <summary>Rebuilds the available-BodySlide list from the parent OBody VM, grouped and sorted by slider group, pre-selecting those matching the currently displayed annotation rule set, then refreshes slider names and descriptor categories.</summary>
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

    /// <summary>Recomputes the available-sliders list to the union of sliders used by the currently selected BodySlides, sorted by name, with each entry's display text annotated with its usage count.</summary>
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

    /// <summary>Counts how many of the currently selected BodySlides use the named slider.</summary>
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

    /// <summary>Builds a slider-matrix <see cref="TrainerExportDTO"/> from the selected (or all) BodySlides and sliders, using the big/small slider type per the radio selection; null if neither type is chosen.</summary>
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

    /// <summary>Builds a per-row labeled <see cref="TrainerExportLearningDTO"/> (classified by the given descriptor category) from the selected (or all) BodySlides and sliders; null if neither big nor small type is chosen.</summary>
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


    /// <summary>Trains an ML.NET pipeline on the full BodySlide set labeled by the selected descriptor category and saves the model under the OBody settings "Models" folder. No-ops if no descriptor is selected.</summary>
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

/// <summary>
/// Selectable wrapper around a <see cref="VM_BodySlidePlaceHolder"/> for the trainer's checkbox list;
/// toggling its selection triggers the parent exporter to refresh available slider names.
/// </summary>
public class VM_SelectableBodySlidePlaceHolder : VM
{
    /// <summary>Builds the "[group] label" display string and subscribes selection changes to the parent's slider-name refresh (unless refresh is paused).</summary>
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

/// <summary>Selectable wrapper around a <see cref="BodySlideSlider"/> for the trainer's slider checkbox list.</summary>
public class VM_SelectableSlider : VM
{
    /// <summary>Captures the subscribed slider and seeds its display text from the slider name.</summary>
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

/// <summary>Simple selectable text item used for the trainer's slider-group and descriptor-category lists.</summary>
public class VM_SelectableMenuString : VM
{
    public string Text { get; set; }
    public bool IsSelected { get; set; }
}

/// <summary>
/// Dense slider matrix export: a 2D grid of slider values (rows = BodySlides, columns = sliders) for the
/// chosen big/small slider type, with missing sliders filled as zero.
/// </summary>
public class TrainerExportDTO
{
    /// <summary>Builds the column/row name arrays and fills the slider-value matrix from the selected BodySlides, using the big or small value per slider and 0 where absent.</summary>
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

/// <summary>
/// Labeled training-data export for ML: one <see cref="BodyslideData"/> row per BodySlide, each carrying its
/// slider-value vector and a classification label drawn from the given descriptor category.
/// </summary>
public class TrainerExportLearningDTO
{
    public List<BodyslideData> DataEntries { get; set; } = new();
    public List<string> SliderNames { get; set; } = new();
    /// <summary>Builds one labeled data row per BodySlide: classification = its descriptor values in the target category joined by "|", and a slider vector (big/small value, 0 where absent) over the selected sliders.</summary>
    public TrainerExportLearningDTO(IList<BodySlideSetting> SelectedBodySlides, IList<string> SelectedSliders, BodySliderType type, string descriptorCategory)
    {
        DataEntries = new();
        SliderNames = new(SelectedSliders);

        foreach (var bodySlide in SelectedBodySlides)
        {
            BodyslideData bsEntry = new() { BodyslideName = bodySlide.Label };
            var descriptors = bodySlide.EnumerateAllDescriptors().Where(x => x.Category == descriptorCategory).Distinct().ToList();
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

/// <summary>ML.NET feature row for a single BodySlide: name, classification label, and slider-value feature vector.</summary>
public class BodyslideData
{
    public string BodyslideName { get; set; }
    public string Classification { get; set; }
    public int[] Sliders { get; set; }
}

