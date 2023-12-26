using Mutagen.Bethesda.Fallout4;
using Noggog;
using ReactiveUI;
using System.Collections.ObjectModel;

namespace SynthEBD;

public class VM_OBodyMiscSettings : VM
{
    private readonly Logger _logger;
    private readonly RaceMenuIniHandler _raceMenuHandler;
    private readonly VM_Settings_General _generalSettingsVM;
    private readonly Func<VM_SettingsOBody> _parentMenu;
    public delegate VM_OBodyMiscSettings Factory();
    public VM_OBodyMiscSettings(Logger logger, RaceMenuIniHandler raceMenuHandler, VM_Settings_General generalSettingsVM, Func<VM_SettingsOBody> parentMenu)
    {
        _logger = logger;
        _raceMenuHandler = raceMenuHandler;
        _generalSettingsVM = generalSettingsVM;
        _parentMenu = parentMenu;

        generalSettingsVM.WhenAnyValue(x => x.BSSelectionMode).Subscribe(mode => {
            
            switch(mode)
            {
                case BodySlideSelectionMode.OBody:
                    ShowOBodySelectionMode = true;
                    ShowAutoBodySelectionMode = false;
                    break;
                case BodySlideSelectionMode.AutoBody:
                    ShowAutoBodySelectionMode = true;
                    ShowOBodySelectionMode = false;
                    break;
            }
        }).DisposeWith(this);

        generalSettingsVM.WhenAnyValue(x => x.bShowTroubleshootingSettings).Subscribe(x => bShowTroubleshootingSettings = x).DisposeWith(this);

        this.WhenAnyValue(x => x.OBodySelectionMode).Subscribe(mode => ShowOBodyNativeOptions = mode == OBodySelectionMode.Native).DisposeWith(this);

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

        RemoveStashedDescriptors = new(
            canExecute: _ => true,
            execute: _ =>
            {
                var toRemove = StashedDescriptors.Where(x => x.IsSelected).Select(x => x.Text).ToArray();

                var parentMenu = _parentMenu();

                foreach (var bs in parentMenu.BodySlidesUI.BodySlidesMale.And(parentMenu.BodySlidesUI.BodySlidesFemale).ToArray())
                {
                    bs.AssociatedModel.BodyShapeDescriptors.RemoveWhere(d => toRemove.Contains(d.ToLabelSignature().ToString()));
                }

                StashedDescriptors.RemoveWhere(x => x.IsSelected);

                ShowRemoveStashedDescriptorsButton = StashedDescriptors.Any();
            }
        );
    }

    public ObservableCollection<VM_CollectionMemberString> MaleBodySlideGroups { get; set; } = new();
    public ObservableCollection<VM_CollectionMemberString> FemaleBodySlideGroups { get; set; } = new();
    public bool UseVerboseScripts { get; set; } = false;
    public AutoBodySelectionMode AutoBodySelectionMode { get; set; } = AutoBodySelectionMode.INI;
    public OBodySelectionMode OBodySelectionMode { get; set; } = OBodySelectionMode.Native;
    public RelayCommand SetRaceMenuINI { get; set; }
    public bool OBodyEnableMultipleAssignments { get; set; } = false;
    public bool ShowOBodyNativeOptions { get; set; } = false;
    public RelayCommand AddMaleSliderGroup { get; set; }
    public RelayCommand AddFemaleSliderGroup { get; set; }
    public bool ShowAutoBodySelectionMode { get; set; }
    public bool ShowOBodySelectionMode { get; set; }
    public bool AutoApplyMissingAnnotations { get; set; } = true;
    public bool bShowTroubleshootingSettings { get; set; } = false;
    public ObservableCollection<VM_SelectableMenuString> StashedDescriptors { get; set; } = new();
    public RelayCommand RemoveStashedDescriptors { get; }
    public bool ShowRemoveStashedDescriptorsButton { get; set; } = false;

    public void CopyInViewModelFromModel(Settings_OBody model)
    {
        MaleBodySlideGroups.Clear();
        foreach (var g in model.MaleSliderGroups)
        {
            MaleBodySlideGroups.Add(new VM_CollectionMemberString(g, MaleBodySlideGroups));
        }
        FemaleBodySlideGroups.Clear();
        foreach (var g in model.FemaleSliderGroups)
        {
            FemaleBodySlideGroups.Add(new VM_CollectionMemberString(g, FemaleBodySlideGroups));
        }
        UseVerboseScripts = model.bUseVerboseScripts;
        AutoBodySelectionMode = model.AutoBodySelectionMode;
        AutoApplyMissingAnnotations = model.AutoApplyMissingAnnotations;
        OBodySelectionMode = model.OBodySelectionMode;
        OBodyEnableMultipleAssignments = model.OBodyEnableMultipleAssignments;

        var parentMenu = _parentMenu();

        var uiDescriptors = parentMenu.DescriptorUI.TemplateDescriptors.SelectMany(x => x.Descriptors).Select(y => y.Signature).ToArray();

        HashSet<string> stashedDescriptorSignatures = new();

        foreach (var bs in parentMenu.BodySlidesUI.BodySlidesMale.And(parentMenu.BodySlidesUI.BodySlidesFemale))
        {
            foreach (var descriptor in bs.AssociatedModel.BodyShapeDescriptors)
            {
                var str = descriptor.ToLabelSignature().ToString();
                if (!uiDescriptors.Contains(str) && !stashedDescriptorSignatures.Contains(str))
                {
                    stashedDescriptorSignatures.Add(str);
                }
            }
        }

        StashedDescriptors.Clear();
        foreach (var str in stashedDescriptorSignatures)
        {
            StashedDescriptors.Add(new() { Text = str, IsSelected = true });
        }

        ShowRemoveStashedDescriptorsButton = StashedDescriptors.Any();
    }

    public void DumpViewModelToModel(Settings_OBody model)
    {
        model.MaleSliderGroups = MaleBodySlideGroups.Select(x => x.Content).ToHashSet();
        model.FemaleSliderGroups = FemaleBodySlideGroups.Select(x => x.Content).ToHashSet();
        model.bUseVerboseScripts = UseVerboseScripts;
        model.AutoBodySelectionMode = AutoBodySelectionMode;
        model.AutoApplyMissingAnnotations = AutoApplyMissingAnnotations;
        model.OBodySelectionMode = OBodySelectionMode;
        model.OBodyEnableMultipleAssignments = OBodyEnableMultipleAssignments;
    }

    public List<string> ResetTroubleShootingToDefault(bool preparationMode)
    {
        var changes = new List<string>();

        if (UseVerboseScripts)
        {
            if (preparationMode)
            {
                changes.Add("Use Verbose Scripts: True --> False");
            }
            else
            {
                UseVerboseScripts = false;
            }
        }

        return changes;
    }
}