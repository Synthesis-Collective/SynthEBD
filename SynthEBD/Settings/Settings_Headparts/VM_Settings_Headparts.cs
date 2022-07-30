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
        AttributeGroupMenu = generalSettingsVM.AttributeGroupMenu;
        RaceGroupings = generalSettingsVM.RaceGroupings;
        OBodyDescriptors = oBodySettings.DescriptorUI;

        BodyShapeMode = generalSettingsVM.BodySelectionMode;
        generalSettingsVM.WhenAnyValue(x => x.BodySelectionMode).Subscribe(x => BodyShapeMode = x);

        DisplayedMenu = DisplayedHeadPartMenuType.Import; // change later to last displayed

        Eyebrows = new VM_HeadPartList(oBodySettings.DescriptorUI, generalSettingsVM.RaceGroupings, this, oBodySettings);
        Eyes = new VM_HeadPartList(oBodySettings.DescriptorUI, generalSettingsVM.RaceGroupings, this, oBodySettings);
        Faces = new VM_HeadPartList(oBodySettings.DescriptorUI, generalSettingsVM.RaceGroupings, this, oBodySettings);
        FacialHairs = new VM_HeadPartList(oBodySettings.DescriptorUI, generalSettingsVM.RaceGroupings, this, oBodySettings);
        Hairs = new VM_HeadPartList(oBodySettings.DescriptorUI, generalSettingsVM.RaceGroupings, this, oBodySettings);
        Misc = new VM_HeadPartList(oBodySettings.DescriptorUI, generalSettingsVM.RaceGroupings, this, oBodySettings);
        Scars = new VM_HeadPartList(oBodySettings.DescriptorUI, generalSettingsVM.RaceGroupings, this, oBodySettings);

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
    public BodyShapeSelectionMode BodyShapeMode { get; set; }

    public RelayCommand ViewImportMenu { get; }
    public RelayCommand ViewEyebrowsMenu { get; }
    public RelayCommand ViewEyesMenu { get; }
    public RelayCommand ViewFaceMenu { get; }
    public RelayCommand ViewFacialHairMenu { get; }
    public RelayCommand ViewHairMenu { get; }
    public RelayCommand ViewMiscMenu { get; }
    public RelayCommand ViewScarsMenu { get; }

    public void CopyInFromModel(Settings_Headparts model, VM_SettingsOBody oBody)
    {
        Eyebrows.CopyInFromModel(model.EyebrowSettings, RaceGroupings, AttributeGroupMenu, this, oBody);
        Eyes.CopyInFromModel(model.EyebrowSettings, RaceGroupings, AttributeGroupMenu, this, oBody);
        Faces.CopyInFromModel(model.FaceSettings, RaceGroupings, AttributeGroupMenu, this, oBody);
        FacialHairs.CopyInFromModel(model.FacialHairSettings, RaceGroupings, AttributeGroupMenu, this, oBody);
        Hairs.CopyInFromModel(model.HairSettings, RaceGroupings, AttributeGroupMenu, this, oBody);
        Misc.CopyInFromModel(model.MiscSettings, RaceGroupings, AttributeGroupMenu, this, oBody);
        Scars.CopyInFromModel(model.ScarsSettings, RaceGroupings, AttributeGroupMenu, this, oBody);
    }

    public void DumpViewModelToModel(Settings_Headparts model)
    {
        Eyebrows.DumpToModel(model.EyebrowSettings);
        Eyes.DumpToModel(model.EyeSettings);
        Faces.DumpToModel(model.FaceSettings);
        FacialHairs.DumpToModel(model.FacialHairSettings);
        Hairs.DumpToModel(model.HairSettings);
        Misc.DumpToModel(model.MiscSettings);
        Scars.DumpToModel(model.ScarsSettings);
    }
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