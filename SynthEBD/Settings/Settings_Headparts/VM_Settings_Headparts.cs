using Mutagen.Bethesda.Skyrim;
using ReactiveUI;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SynthEBD;

public class VM_Settings_Headparts: VM, IHasAttributeGroupMenu
{
    public VM_Settings_Headparts(VM_Settings_General generalSettingsVM, VM_SettingsOBody oBodySettings, VM_HeadPartImport headPartImportVM, VM_HeadPartList.Factory listFactory, VM_HeadPart.Factory headPartFactory, Logger logger)
    {
        ImportMenu = headPartImportVM;
        SettingsMenu = new();
        AttributeGroupMenu = generalSettingsVM.AttributeGroupMenu;
        RaceGroupings = generalSettingsVM.RaceGroupings;
        OBodyDescriptors = oBodySettings.DescriptorUI;

        BodyShapeMode = generalSettingsVM.BodySelectionMode;
        //generalSettingsVM.WhenAnyValue(x => x.BodySelectionMode).Subscribe(x => BodyShapeMode = x);

        DisplayedMenu = ImportMenu;

        Types = new()
        {
            { HeadPart.TypeEnum.Eyebrows, listFactory(HeadPart.TypeEnum.Eyebrows, generalSettingsVM.RaceGroupings) },
            { HeadPart.TypeEnum.Eyes, listFactory(HeadPart.TypeEnum.Eyes, generalSettingsVM.RaceGroupings) },
            { HeadPart.TypeEnum.Face, listFactory(HeadPart.TypeEnum.Face, generalSettingsVM.RaceGroupings) },
            { HeadPart.TypeEnum.FacialHair, listFactory(HeadPart.TypeEnum.FacialHair, generalSettingsVM.RaceGroupings) },
            { HeadPart.TypeEnum.Hair, listFactory(HeadPart.TypeEnum.Hair, generalSettingsVM.RaceGroupings) },
            { HeadPart.TypeEnum.Misc, listFactory(HeadPart.TypeEnum.Misc, generalSettingsVM.RaceGroupings) },
            { HeadPart.TypeEnum.Scars, listFactory(HeadPart.TypeEnum.Scars, generalSettingsVM.RaceGroupings) }
        };

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
    public VM_HeadPartList DisplayedHeadParts { get; set; } 
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
    public void CopyInFromModel(Settings_Headparts model, ObservableCollection<VM_RaceGrouping> raceGroupings)
    {
        RaceGroupings = raceGroupings;
        foreach (var type in model.Types.Keys)
        {
            Types[type].CopyInFromModel(model.Types[type], RaceGroupings, AttributeGroupMenu);
        }
        SettingsMenu.GetViewModelFromModel(model);
    }

    public void DumpViewModelToModel(Settings_Headparts model)
    {
        foreach (var type in model.Types.Keys)
        {
            Types[type].DumpToModel(model.Types[type]);
        }
        SettingsMenu.DumpViewModelToModel(model);
    }
}