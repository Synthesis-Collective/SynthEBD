using System.Collections.ObjectModel;

namespace SynthEBD;

public class VM_OBodyMiscSettings : VM
{
    public VM_OBodyMiscSettings()
    {
        AddMaleSliderGroup = new RelayCommand(
            canExecute: _ => true,
            execute: _ => MaleBodySlideGroups.Add(new VM_CollectionMemberString("", MaleBodySlideGroups))
        );

        AddFemaleSliderGroup = new RelayCommand(
            canExecute: _ => true,
            execute: _ => FemaleBodySlideGroups.Add(new VM_CollectionMemberString("", FemaleBodySlideGroups))
        );
    }

    public ObservableCollection<VM_CollectionMemberString> MaleBodySlideGroups { get; set; } = new();
    public ObservableCollection<VM_CollectionMemberString> FemaleBodySlideGroups { get; set; } = new();
    public bool UseVerboseScripts { get; set; } = false;
    public AutoBodySelectionMode AutoBodySelectionMode { get; set; } = AutoBodySelectionMode.INI;
    public RelayCommand SetRaceMenuINI { get; } = new(
        canExecute: _ => true,
        execute: _ =>
        {
            if (RaceMenuIniHandler.SetRaceMenuIniForBodySlide())
            {
                Logger.CallTimedLogErrorWithStatusUpdateAsync("RaceMenu Ini set successfully", ErrorType.Warning, 2); // Warning yellow font is easier to see than green
            }
            else
            {
                Logger.LogErrorWithStatusUpdate("Error encountered trying to set RaceMenu's ini.", ErrorType.Error);
            }
        }
    );

    public RelayCommand AddMaleSliderGroup { get; set; }
    public RelayCommand AddFemaleSliderGroup { get; set; }

    public static VM_OBodyMiscSettings GetViewModelFromModel(Settings_OBody model)
    {
        VM_OBodyMiscSettings viewModel = new VM_OBodyMiscSettings();
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