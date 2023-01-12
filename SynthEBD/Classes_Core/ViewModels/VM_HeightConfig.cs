using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Skyrim;
using Noggog;
using System.Collections.ObjectModel;
using ReactiveUI;

namespace SynthEBD;

public class VM_HeightConfig : VM
{
    private readonly IEnvironmentStateProvider _stateProvider;
    private readonly Logger _logger;
    private readonly VM_HeightAssignment.Factory _assignmentFactory;
    public delegate VM_HeightConfig Factory();
    public VM_HeightConfig(IEnvironmentStateProvider stateProvider, Logger logger, SettingsIO_Height heightIO, VM_HeightAssignment.Factory assignmentFactory)
    {
        _stateProvider = stateProvider;
        _logger = logger;
        _assignmentFactory = assignmentFactory;

        AddHeightAssignment = new RelayCommand(
            canExecute: _ => true,
            execute: _ => HeightAssignments.Add(_assignmentFactory(HeightAssignments))
        );

        SetAllDistModes = new RelayCommand(
            canExecute: _ => true,
            execute: _ =>
            {
                foreach (var assignment in HeightAssignments)
                {
                    assignment.DistributionMode = GlobalDistMode;
                }
            }
        );

        Save = new RelayCommand(
            canExecute: _ => true,
            execute: _ =>
            {
                heightIO.SaveHeightConfig(DumpViewModelToModel(), out bool saveSuccess);
                if (saveSuccess)
                {
                    _logger.CallTimedNotifyStatusUpdateAsync(Label + " Saved.", 2, new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.Yellow));
                }
                else
                {
                    _logger.CallTimedLogErrorWithStatusUpdateAsync("Could not save " + Label + ".", ErrorType.Error, 5);
                }
            }
        );
    }

    public string Label { get; set; } = "New Height Configuration";
    public ObservableCollection<VM_HeightAssignment> HeightAssignments { get; set; } = new();
    public DistMode GlobalDistMode { get; set; } = DistMode.uniform;
    public HeightConfig SubscribedHeightConfig { get; set; } = new();
    public string SourcePath { get; set; } = "";
    public RelayCommand AddHeightAssignment { get; }
    public RelayCommand SetAllDistModes { get; }
    public RelayCommand Save { get; }

    public static void GetViewModelsFromModels(ObservableCollection<VM_HeightConfig> viewModels, List<HeightConfig> models, VM_HeightConfig.Factory configFactory, VM_HeightAssignment.Factory assignmentFactory)
    {
        viewModels.Clear();
        for (int i = 0; i < models.Count; i++)
        {
            var vm = configFactory();
            vm.Label = models[i].Label;
            vm.HeightAssignments = VM_HeightAssignment.GetViewModelsFromModels(models[i].HeightAssignments, assignmentFactory);
            vm.SubscribedHeightConfig = models[i];
            vm.SourcePath = models[i].FilePath;

            viewModels.Add(vm);
        }
    }

    public static void DumpViewModelsToModels(ObservableCollection<VM_HeightConfig> viewModels, List<HeightConfig> models, Logger logger)
    {
        models.Clear();
        foreach (var vm in viewModels)
        {
            models.Add(vm.DumpViewModelToModel());
        }
    }

    public HeightConfig DumpViewModelToModel()
    {
        var model = new HeightConfig();
        model.Label = Label;
        model.HeightAssignments = VM_HeightAssignment.DumpViewModelsToModels(model.HeightAssignments, HeightAssignments, _logger);
        model.FilePath = SourcePath;
        return model;
    }
}
public class VM_HeightAssignment : VM
{
    private readonly IEnvironmentStateProvider _stateProvider;
    public delegate VM_HeightAssignment Factory(ObservableCollection<VM_HeightAssignment> parentCollection);
    public VM_HeightAssignment(ObservableCollection<VM_HeightAssignment> parentCollection, IEnvironmentStateProvider stateProvider)
    {
        _stateProvider = stateProvider;
        DeleteCommand = new RelayCommand(canExecute: _ => true, execute: _ => parentCollection.Remove(this));
        
        _stateProvider.WhenAnyValue(x => x.LinkCache)
            .Subscribe(x => lk = x)
            .DisposeWith(this);
    }

    public string Label { get; set; } = "";
    public ObservableCollection<FormKey> Races { get; set; } = new();
    public DistMode DistributionMode { get; set; } = DistMode.uniform;
    public string MaleHeightBase { get; set; } = "1.000000";
    public string MaleHeightRange { get; set; } = "0.020000";
    public string FemaleHeightBase { get; set; } = "1.000000";
    public string FemaleHeightRange { get; set; } = "0.020000";

    public IEnumerable<Type> FormKeyPickerTypes { get; set; } = typeof(IRaceGetter).AsEnumerable();
    public ILinkCache lk { get; private set; }
    public RelayCommand DeleteCommand { get; }

    public static ObservableCollection<VM_HeightAssignment> GetViewModelsFromModels(HashSet<HeightAssignment> models, VM_HeightAssignment.Factory factory)
    {
        ObservableCollection<VM_HeightAssignment> viewModels = new ObservableCollection<VM_HeightAssignment>();
        foreach (var model in models)
        {
            var vm = factory(viewModels);
            vm.Label = model.Label;
            vm.Races = new ObservableCollection<FormKey>(model.Races);
            vm.MaleHeightBase = model.HeightMale.ToString();
            vm.MaleHeightRange = model.HeightMaleRange.ToString();
            vm.FemaleHeightBase = model.HeightFemale.ToString();
            vm.FemaleHeightRange = model.HeightFemaleRange.ToString();

            viewModels.Add(vm);
        }

        return viewModels;
    }

    public static HashSet<HeightAssignment> DumpViewModelsToModels(HashSet<HeightAssignment> models, ObservableCollection<VM_HeightAssignment> viewModels, Logger logger)
    {
        foreach (var vm in viewModels)
        {
            HeightAssignment ha = new HeightAssignment();
            ha.Label = vm.Label;
            ha.Races = vm.Races.ToHashSet();

            if (float.TryParse(vm.MaleHeightBase, out var maleHeight))
            {
                ha.HeightMale = maleHeight;
            }
            else
            {
                logger.LogError("Cannot parse male height " + vm.MaleHeightBase + " for Height Assignment: " + ha.Label);
            }

            if (float.TryParse(vm.FemaleHeightBase, out var femaleHeight))
            {
                ha.HeightFemale = femaleHeight;
            }
            else
            {
                logger.LogError("Cannot parse female height " + vm.FemaleHeightBase + " for Height Assignment: " + ha.Label);
            }

            if (float.TryParse(vm.MaleHeightRange, out var maleHeightRange))
            {
                ha.HeightMaleRange = maleHeightRange;
            }
            else
            {
                logger.LogError("Cannot parse male height range " + vm.MaleHeightRange + " for Height Assignment: " + ha.Label);
            }

            if (float.TryParse(vm.FemaleHeightRange, out var femaleHeightRange))
            {
                ha.HeightFemaleRange = femaleHeightRange;
            }
            else
            {
                logger.LogError("Cannot parse female height range " + vm.FemaleHeightRange + " for Height Assignment: " + ha.Label);
            }

            models.Add(ha);
        }

        return models;
    }
}