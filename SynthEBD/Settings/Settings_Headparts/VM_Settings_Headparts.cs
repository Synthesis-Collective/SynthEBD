using Mutagen.Bethesda.Skyrim;
using ReactiveUI;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Noggog;

namespace SynthEBD;

/// <summary>
/// View model for the Head Parts settings tab, backing the <see cref="Settings_Headparts"/> model.
/// Hosts the import menu, the per-type <see cref="VM_HeadPartList"/> sub-menus (Eyebrows, Eyes,
/// Face, FacialHair, Hair, Misc, Scars), and the misc settings menu, swapping the displayed
/// sub-menu via the View* commands. Shares the general settings' attribute groups and race groupings.
/// </summary>
public class VM_Settings_Headparts: VM, IHasAttributeGroupMenu
{
    private readonly IEnvironmentStateProvider _environmentProvider;
    private readonly VM_Settings_General _generalSettingsVM;
    private readonly VM_HeadPartList.Factory _listFactory;
    private readonly Logger _logger;
    /// <summary>
    /// Builds the import and misc-settings sub-menus, mirrors shared attribute groups / race
    /// groupings / OBody descriptors / body-shape mode from general settings, and wires the
    /// View* navigation <see cref="RelayCommand"/>s that swap <see cref="DisplayedMenu"/>.
    /// </summary>
    public VM_Settings_Headparts(VM_Settings_General generalSettingsVM, VM_SettingsOBody oBodySettings, VM_SettingsBodyGen bodyGenSettings, VM_HeadPartList.Factory listFactory, VM_HeadPartPlaceHolder.Factory placeHolderFactory, Logger logger, IEnvironmentStateProvider environmentProvider)
    {
        _environmentProvider = environmentProvider;
        _generalSettingsVM = generalSettingsVM;
        _listFactory = listFactory;
        _logger = logger;

        ImportMenu = new VM_HeadPartImport(this, logger, environmentProvider, placeHolderFactory);
        SettingsMenu = new(this, bodyGenSettings);
        AttributeGroupMenu = generalSettingsVM.AttributeGroupMenu;
        RaceGroupings = generalSettingsVM.RaceGroupingEditor.RaceGroupings;
        OBodyDescriptors = oBodySettings.DescriptorUI;

        BodyShapeMode = generalSettingsVM.BodySelectionMode;
        generalSettingsVM.WhenAnyValue(x => x.BodySelectionMode).Subscribe(x => BodyShapeMode = x).DisposeWith(this);

        DisplayedMenu = ImportMenu;

        ViewImportMenu = new RelayCommand(
            canExecute: _ => true,
            execute: _ =>
            {
                DisplayedMenu = ImportMenu;
            }
        );

        ViewEyebrowsMenu = new RelayCommand(
            canExecute: _ => true,
            execute: _ => {
                DisplayedMenu = Types[HeadPart.TypeEnum.Eyebrows];
            }
        );

        ViewEyesMenu = new RelayCommand(
            canExecute: _ => true,
            execute: _ => {
                DisplayedMenu = Types[HeadPart.TypeEnum.Eyes];
            }
        );

        ViewFaceMenu = new RelayCommand(
            canExecute: _ => true,
            execute: _ => {
                DisplayedMenu = Types[HeadPart.TypeEnum.Face];
            }
        );

        ViewFacialHairMenu = new RelayCommand(
            canExecute: _ => true,
            execute: _ => {
                DisplayedMenu = Types[HeadPart.TypeEnum.FacialHair];
            }
        );

        ViewHairMenu = new RelayCommand(
            canExecute: _ => true,
            execute: _ => {
                DisplayedMenu = Types[HeadPart.TypeEnum.Hair];
            }
        );

        ViewMiscMenu = new RelayCommand(
            canExecute: _ => true,
            execute: _ => {
                DisplayedMenu = Types[HeadPart.TypeEnum.Misc];
            }
        );

        ViewScarsMenu = new RelayCommand(
            canExecute: _ => true,
            execute: _ => {
                DisplayedMenu = Types[HeadPart.TypeEnum.Scars];
            }
        );

        ViewSettingsMenu = new RelayCommand(
            canExecute: _ => true,
            execute: _ =>
            {
                DisplayedMenu = SettingsMenu;
            }
        );
    }

    public object DisplayedMenu { get; set; }
    public VM_HeadPartImport ImportMenu { get; set; }
    public Dictionary<HeadPart.TypeEnum, VM_HeadPartList> Types { get; set; }
    public VM_AttributeGroupMenu AttributeGroupMenu { get; }
    public VM_HeadPartMiscSettings SettingsMenu { get; }
    public ObservableCollection<VM_RaceGrouping> RaceGroupings { get; set; }
    public VM_BodyShapeDescriptorCreationMenu OBodyDescriptors { get; set; }
    public BodyShapeSelectionMode BodyShapeMode { get; set; }
    public RelayCommand ViewImportMenu { get; }
    public RelayCommand ViewEyebrowsMenu { get; }
    public RelayCommand ViewEyesMenu { get; }
    public RelayCommand ViewFaceMenu { get; }
    public RelayCommand ViewFacialHairMenu { get; }
    public RelayCommand ViewHairMenu { get; }
    public RelayCommand ViewMiscMenu { get; }
    public RelayCommand ViewScarsMenu { get; }
    public RelayCommand ViewSettingsMenu { get; }

    /// <summary>Builds the per-type <see cref="VM_HeadPartList"/> dictionary; called after construction (configs reference shared race groupings).</summary>
    public void Initialize()
    {
        Types = new()
        {
            { HeadPart.TypeEnum.Eyebrows, _listFactory(_generalSettingsVM.RaceGroupingEditor.RaceGroupings, HeadPart.TypeEnum.Eyebrows) },
            { HeadPart.TypeEnum.Eyes, _listFactory(_generalSettingsVM.RaceGroupingEditor.RaceGroupings, HeadPart.TypeEnum.Eyes) },
            { HeadPart.TypeEnum.Face, _listFactory(_generalSettingsVM.RaceGroupingEditor.RaceGroupings, HeadPart.TypeEnum.Face) },
            { HeadPart.TypeEnum.FacialHair, _listFactory(_generalSettingsVM.RaceGroupingEditor.RaceGroupings, HeadPart.TypeEnum.FacialHair) },
            { HeadPart.TypeEnum.Hair, _listFactory(_generalSettingsVM.RaceGroupingEditor.RaceGroupings, HeadPart.TypeEnum.Hair) },
            { HeadPart.TypeEnum.Misc, _listFactory(_generalSettingsVM.RaceGroupingEditor.RaceGroupings, HeadPart.TypeEnum.Misc) },
            { HeadPart.TypeEnum.Scars, _listFactory(_generalSettingsVM.RaceGroupingEditor.RaceGroupings, HeadPart.TypeEnum.Scars) }
        };
    }

    /// <summary>Model → VM: loads the misc settings (first), then each per-type head-part list from the model.</summary>
    public void CopyInFromModel(Settings_Headparts model, ObservableCollection<VM_RaceGrouping> raceGroupings)
    {
        if (model == null)
        {
            return;
        }

        _logger.LogStartupEventStart("Loading UI for HeadParts Menu");
        RaceGroupings = raceGroupings;
        SettingsMenu.GetViewModelFromModel(model); // must load before the VM_HeadPartLists
        foreach (var type in model.Types.Keys)
        {
            Types[type].CopyInFromModel(model.Types[type], RaceGroupings, AttributeGroupMenu);
        }
        _logger.LogStartupEventEnd("Loading UI for HeadParts Menu");
    }

    /// <summary>VM → Model: dumps each per-type head-part list and merges the misc settings into a new <see cref="Settings_Headparts"/>.</summary>
    public Settings_Headparts DumpViewModelToModel()
    {
        Settings_Headparts model = new();
        foreach (var type in model.Types.Keys)
        {
            Types[type].DumpToModel(model.Types[type]);
        }
        SettingsMenu.MergeViewModelIntoModel(model);
        return model;
    }
}