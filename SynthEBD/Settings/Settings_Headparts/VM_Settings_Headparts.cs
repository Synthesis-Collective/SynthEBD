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
    public VM_Settings_Headparts(VM_Settings_General generalSettingsVM, VM_SettingsOBody oBodySettings)
    {
        ImportMenu = new(this);
        SettingsMenu = new();
        AttributeGroupMenu = generalSettingsVM.AttributeGroupMenu;
        RaceGroupings = generalSettingsVM.RaceGroupings;
        OBodyDescriptors = oBodySettings.DescriptorUI;

        BodyShapeMode = generalSettingsVM.BodySelectionMode;
        generalSettingsVM.WhenAnyValue(x => x.BodySelectionMode).Subscribe(x => BodyShapeMode = x);

        DisplayedMenu = ImportMenu;

        Types = new()
        {
            { HeadPart.TypeEnum.Eyebrows, new VM_HeadPartList(HeadPart.TypeEnum.Eyebrows, generalSettingsVM.RaceGroupings, this, oBodySettings) },
            { HeadPart.TypeEnum.Eyes, new VM_HeadPartList(HeadPart.TypeEnum.Eyes, generalSettingsVM.RaceGroupings, this, oBodySettings) },
            { HeadPart.TypeEnum.Face, new VM_HeadPartList(HeadPart.TypeEnum.Face, generalSettingsVM.RaceGroupings, this, oBodySettings) },
            { HeadPart.TypeEnum.FacialHair, new VM_HeadPartList(HeadPart.TypeEnum.FacialHair, generalSettingsVM.RaceGroupings, this, oBodySettings) },
            { HeadPart.TypeEnum.Hair, new VM_HeadPartList(HeadPart.TypeEnum.Hair, generalSettingsVM.RaceGroupings, this, oBodySettings) },
            { HeadPart.TypeEnum.Misc, new VM_HeadPartList(HeadPart.TypeEnum.Misc, generalSettingsVM.RaceGroupings, this, oBodySettings) },
            { HeadPart.TypeEnum.Scars, new VM_HeadPartList(HeadPart.TypeEnum.Scars, generalSettingsVM.RaceGroupings, this, oBodySettings) }
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
    public void CopyInFromModel(Settings_Headparts model, VM_SettingsOBody oBody)
    {
        foreach (var type in model.Types.Keys)
        {
            Types[type].CopyInFromModel(model.Types[type], RaceGroupings, AttributeGroupMenu, this, oBody);
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