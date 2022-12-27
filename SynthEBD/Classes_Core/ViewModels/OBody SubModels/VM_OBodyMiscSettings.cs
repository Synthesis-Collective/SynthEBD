using System.Collections.ObjectModel;

namespace SynthEBD;

public class VM_OBodyMiscSettings : VM
{
    private readonly Logger _logger;
    private readonly RaceMenuIniHandler _raceMenuHandler;
    public delegate VM_OBodyMiscSettings Factory();
    public VM_OBodyMiscSettings(Logger logger, RaceMenuIniHandler raceMenuHandler)
    {
        _logger = logger;
        _raceMenuHandler = raceMenuHandler;

        AddMaleSliderGroup = new RelayCommand(
            canExecute: _ => true,
            execute: _ => MaleBodySlideGroups.Add(new VM_CollectionMemberString("", MaleBodySlideGroups))
        );

        AddFemaleSliderGroup = new RelayCommand(
            canExecute: _ => true,
            execute: _ => FemaleBodySlideGroups.Add(new VM_CollectionMemberString("", FemaleBodySlideGroups))
        );

        SetRaceMenuINI = new(
            canExecute: _ => true,
            execute: _ =>
            {
                if (_raceMenuHandler.SetRaceMenuIniForBodySlide())
                {
                    _logger.CallTimedLogErrorWithStatusUpdateAsync("RaceMenu Ini set successfully", ErrorType.Warning, 2); // Warning yellow font is easier to see than green
                }
                else
                {
                    _logger.LogErrorWithStatusUpdate("Error encountered trying to set RaceMenu's ini.", ErrorType.Error);
                }
            }
        );
    }

    public ObservableCollection<VM_CollectionMemberString> MaleBodySlideGroups { get; set; } = new();
    public ObservableCollection<VM_CollectionMemberString> FemaleBodySlideGroups { get; set; } = new();
    public bool UseVerboseScripts { get; set; } = false;
    public AutoBodySelectionMode AutoBodySelectionMode { get; set; } = AutoBodySelectionMode.INI;
    public RelayCommand SetRaceMenuINI { get; set; }

    public RelayCommand AddMaleSliderGroup { get; set; }
    public RelayCommand AddFemaleSliderGroup { get; set; }

    public VM_OBodyMiscSettings GetViewModelFromModel(Settings_OBody model)
    {
        VM_OBodyMiscSettings viewModel = new VM_OBodyMiscSettings(_logger, _raceMenuHandler);
        viewModel.MaleBodySlideGroups.Clear();
        foreach (var g in model.MaleSliderGroups)
        {
            viewModel.MaleBodySlideGroups.Add(new VM_CollectionMemberString(g, viewModel.MaleBodySlideGroups));
        }
        viewModel.FemaleBodySlideGroups.Clear();
        foreach (var g in model.FemaleSliderGroups)
        {
            viewModel.FemaleBodySlideGroups.Add(new VM_CollectionMemberString(g, viewModel.FemaleBodySlideGroups));
        }
        viewModel.UseVerboseScripts = model.bUseVerboseScripts;
        viewModel.AutoBodySelectionMode = model.AutoBodySelectionMode;
        return viewModel;
    }

    public static void DumpViewModelToModel(Settings_OBody model, VM_OBodyMiscSettings viewModel)
    {
        model.MaleSliderGroups = viewModel.MaleBodySlideGroups.Select(x => x.Content).ToHashSet();
        model.FemaleSliderGroups = viewModel.FemaleBodySlideGroups.Select(x => x.Content).ToHashSet();
        model.bUseVerboseScripts = viewModel.UseVerboseScripts;
        model.AutoBodySelectionMode = viewModel.AutoBodySelectionMode;
    }
}