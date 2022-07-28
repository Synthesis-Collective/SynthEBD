using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SynthEBD
{
    public class VM_Settings_Headparts: VM, IHasAttributeGroupMenu
    {
        public VM_Settings_Headparts(VM_Settings_General generalSettingsVM, VM_SettingsOBody oBodySettings)
        {
            ImportMenu = new(this);
            AttributeGroupMenu = new(generalSettingsVM.AttributeGroupMenu, true);
            RaceGroupings = generalSettingsVM.RaceGroupings;
            OBodyDescriptors = oBodySettings.DescriptorUI;

            DisplayedMenu = ImportMenu; // change later to last displayed

            ViewImportMenu = new RelayCommand(
                canExecute: _ => true,
                execute: _ => { DisplayedMenu = ImportMenu; DisplayedHeadParts.DisplayedList = Eyebrows; DisplayedHeadPartsRules = EyebrowRules; }
            );

            ViewEyebrowsMenu = new RelayCommand(
                canExecute: _ => true,
                execute: _ => { DisplayedMenu = DisplayedHeadParts; DisplayedHeadParts.DisplayedList = Eyebrows; DisplayedHeadPartsRules = EyebrowRules; }
            );

            ViewEyesMenu = new RelayCommand(
                canExecute: _ => true,
                execute: _ => { DisplayedMenu = DisplayedHeadParts; DisplayedHeadParts.DisplayedList = Eyes; DisplayedHeadPartsRules = EyesRules; }
            );

            ViewFaceMenu = new RelayCommand(
                canExecute: _ => true,
                execute: _ => { DisplayedMenu = DisplayedHeadParts; DisplayedHeadParts.DisplayedList = Faces; DisplayedHeadPartsRules = FaceRules; }
            );

            ViewFacialHairMenu = new RelayCommand(
                canExecute: _ => true,
                execute: _ => { DisplayedMenu = DisplayedHeadParts; DisplayedHeadParts.DisplayedList = FacialHairs; DisplayedHeadPartsRules = FacialHairRules; }
            );

            ViewHairMenu = new RelayCommand(
                canExecute: _ => true,
                execute: _ => { DisplayedMenu = DisplayedHeadParts; DisplayedHeadParts.DisplayedList = Hairs; DisplayedHeadPartsRules = HairRules; }
            );

            ViewMiscMenu = new RelayCommand(
                canExecute: _ => true,
                execute: _ => { DisplayedMenu = DisplayedHeadParts; DisplayedHeadParts.DisplayedList = Misc; DisplayedHeadPartsRules = MiscRules; }
            );

            ViewScarsMenu = new RelayCommand(
                canExecute: _ => true,
                execute: _ => { DisplayedMenu = DisplayedHeadParts; DisplayedHeadParts.DisplayedList = Scars; DisplayedHeadPartsRules = ScarRules; }
            );
        }

        public object DisplayedMenu { get; set; }
        public VM_HeadPartImport ImportMenu { get; set; } 
        public VM_HeadPartList DisplayedHeadParts { get; set; } = new();
        public VM_HeadPartCategoryRules DisplayedHeadPartsRules { get; set; }

        public ObservableCollection<VM_HeadPart> Eyebrows { get; set; } = new();
        public VM_HeadPartCategoryRules EyebrowRules { get; set; }
        public ObservableCollection<VM_HeadPart> Eyes { get; set; } = new();
        public VM_HeadPartCategoryRules EyesRules { get; set; }
        public ObservableCollection<VM_HeadPart> Faces { get; set; } = new();
        public VM_HeadPartCategoryRules FaceRules { get; set; }
        public ObservableCollection<VM_HeadPart> FacialHairs { get; set; } = new();
        public VM_HeadPartCategoryRules FacialHairRules { get; set; }
        public ObservableCollection<VM_HeadPart> Hairs { get; set; } = new();
        public VM_HeadPartCategoryRules HairRules { get; set; }
        public ObservableCollection<VM_HeadPart> Misc { get; set; } = new();
        public VM_HeadPartCategoryRules MiscRules { get; set; }
        public ObservableCollection<VM_HeadPart> Scars { get; set; } = new();
        public VM_HeadPartCategoryRules ScarRules { get; set; }

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
        public RelayCommand ViewAttributeGroupsEditor { get; }
    }
}