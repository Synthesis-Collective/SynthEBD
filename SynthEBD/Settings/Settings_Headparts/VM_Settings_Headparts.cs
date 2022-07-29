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
        AttributeGroupMenu = generalSettingsVM.AttributeGroupMenu;
        RaceGroupings = generalSettingsVM.RaceGroupings;
        OBodyDescriptors = oBodySettings.DescriptorUI;

        DisplayedMenu = DisplayedHeadPartMenuType.Import; // change later to last displayed

        Eyebrows = new VM_HeadPartList(oBodySettings.DescriptorUI, generalSettingsVM.RaceGroupings, this);
        Eyes = new VM_HeadPartList(oBodySettings.DescriptorUI, generalSettingsVM.RaceGroupings, this);
        Faces = new VM_HeadPartList(oBodySettings.DescriptorUI, generalSettingsVM.RaceGroupings, this);
        FacialHairs = new VM_HeadPartList(oBodySettings.DescriptorUI, generalSettingsVM.RaceGroupings, this);
        Hairs = new VM_HeadPartList(oBodySettings.DescriptorUI, generalSettingsVM.RaceGroupings, this);
        Misc = new VM_HeadPartList(oBodySettings.DescriptorUI, generalSettingsVM.RaceGroupings, this);
        Scars = new VM_HeadPartList(oBodySettings.DescriptorUI, generalSettingsVM.RaceGroupings, this);

    ViewImportMenu = new RelayCommand(
            canExecute: _ => true,
            execute: _ => { 
                DisplayedMenu = DisplayedHeadPartMenuType.Import; }
        );

        ViewEyebrowsMenu = new RelayCommand(
            canExecute: _ => true,
            execute: _ => { 
                DisplayedMenu = DisplayedHeadPartMenuType.Eyebrows;
            }
        );

        ViewEyesMenu = new RelayCommand(
            canExecute: _ => true,
            execute: _ => { 
                DisplayedMenu = DisplayedHeadPartMenuType.Eyes; }
        );

        ViewFaceMenu = new RelayCommand(
            canExecute: _ => true,
            execute: _ => { 
                DisplayedMenu = DisplayedHeadPartMenuType.Faces; }
        );

        ViewFacialHairMenu = new RelayCommand(
            canExecute: _ => true,
            execute: _ => { 
                DisplayedMenu = DisplayedHeadPartMenuType.FacialHairs; }
        );

        ViewHairMenu = new RelayCommand(
            canExecute: _ => true,
            execute: _ => { 
                DisplayedMenu = DisplayedHeadPartMenuType.Hairs; }
        );

        ViewMiscMenu = new RelayCommand(
            canExecute: _ => true,
            execute: _ => { 
                DisplayedMenu = DisplayedHeadPartMenuType.Misc; }
        );

        ViewScarsMenu = new RelayCommand(
            canExecute: _ => true,
            execute: _ => { 
                DisplayedMenu = DisplayedHeadPartMenuType.Scars; }
        );
    }

    public DisplayedHeadPartMenuType DisplayedMenu { get; set; }
    public VM_HeadPartImport ImportMenu { get; set; } 
    public VM_HeadPartList DisplayedHeadParts { get; set; } 
    public VM_HeadPartList Eyebrows { get; set; }
    public VM_HeadPartList Eyes { get; set; }
    public VM_HeadPartList Faces { get; set; }
    public VM_HeadPartList FacialHairs { get; set; } 
    public VM_HeadPartList Hairs { get; set; } 
    public VM_HeadPartList Misc { get; set; } 
    public VM_HeadPartList Scars { get; set; }

    public VM_AttributeGroupMenu AttributeGroupMenu { get; }
    public ObservableCollection<VM_RaceGrouping> RaceGroupings { get; set; }
    public VM_BodyShapeDescriptorCreationMenu OBodyDescriptors { get; set; }

    public RelayCommand ViewImportMenu { get; }
    public RelayCommand ViewEyebrowsMenu { get; }
    public RelayCommand ViewEyesMenu { get; }
    public RelayCommand ViewFaceMenu { get; }
    public RelayCommand ViewFacialHairMenu { get; }
    public RelayCommand ViewHairMenu { get; }
    public RelayCommand ViewMiscMenu { get; }
    public RelayCommand ViewScarsMenu { get; }
}
public enum DisplayedHeadPartMenuType
{
    Import,
    Eyebrows,
    Eyes,
    Faces,
    FacialHairs,
    Hairs,
    Misc,
    Scars
};