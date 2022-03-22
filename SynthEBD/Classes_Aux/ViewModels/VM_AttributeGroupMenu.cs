using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SynthEBD
{
    public class VM_AttributeGroupMenu : INotifyPropertyChanged
    {
        public VM_AttributeGroupMenu(VM_AttributeGroupMenu generalSettingsAttributes, bool showImportFromGeneralOption)
        {
            Groups = new ObservableCollection<VM_AttributeGroup>();
            DisplayedGroup = null;
            GeneralSettingsAttributes = generalSettingsAttributes;
            ShowImportFromGeneralOption = showImportFromGeneralOption;

            AddGroup = new SynthEBD.RelayCommand(
                canExecute: _ => true,
                execute: _ => this.Groups.Add(new VM_AttributeGroup(this))
                );


            ImportAttributeGroups = new SynthEBD.RelayCommand(
                canExecute: _ => true,
                execute: _ =>
                {
                    var alreadyContainedGroups = Groups.Select(x => x.Label).ToHashSet();
                    foreach (var attGroup in GeneralSettingsAttributes.Groups)
                    {
                        if (!alreadyContainedGroups.Contains(attGroup.Label))
                        {
                            Groups.Add(VM_AttributeGroup.Copy(attGroup, this));
                        }
                    }
                }
                );
        }
        public ObservableCollection<VM_AttributeGroup> Groups { get; set; }
        public VM_AttributeGroup DisplayedGroup { get; set; }
        public VM_AttributeGroupMenu GeneralSettingsAttributes { get; set; }
        public bool ShowImportFromGeneralOption { get; set; }
        public RelayCommand AddGroup { get; }
        public RelayCommand ImportAttributeGroups { get; }

        public event PropertyChangedEventHandler PropertyChanged;

        public static void GetViewModelFromModels(HashSet<AttributeGroup> models, VM_AttributeGroupMenu viewModel)
        {
            viewModel.Groups.Clear();
            // first add each group to the menu
            foreach (var model in models)
            {
                viewModel.Groups.Add(VM_AttributeGroup.GetViewModelFromModel(model, viewModel));
            }
            // then set IsSelected once all the groups are populated (VM_AttributeGroup.GetViewModelFromModel can't do this because if model[i] references model[i+1], the correposndoing viewModel[i] can't have that selection checked because viewModel[i+1] hasn't been built yet.
            foreach (var model in models)
            {
                foreach (var att in model.Attributes)
                {
                    foreach (var subAtt in att.SubAttributes)
                    {
                        if (subAtt.Type == NPCAttributeType.Group)
                        {
                            var subAttModel = (NPCAttributeGroup)subAtt;
                            var correspondingVM = viewModel.Groups.Where(x => x.Label == model.Label).First();
                            foreach (var groupAttribute in correspondingVM.Attributes)
                            {
                                var groupAttributes = groupAttribute.GroupedSubAttributes.Where(x => x.Type == NPCAttributeType.Group);
                                foreach (var groupAtt in groupAttributes)
                                {
                                    var castGroupAtt = (VM_NPCAttributeGroup)groupAtt.Attribute;
                                    var checkListEntries = castGroupAtt.AttributeCheckList.AttributeSelections.Where(x => subAttModel.SelectedLabels.Contains(x.Label)).ToHashSet();
                                    foreach (var toSelect in checkListEntries)
                                    {
                                        toSelect.IsSelected = true;
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }

        public static void DumpViewModelToModels(VM_AttributeGroupMenu viewModel, HashSet<AttributeGroup> models)
        {
            models.Clear();
            foreach (var subVM in viewModel.Groups)
            {
                var model = VM_AttributeGroup.DumpViewModelToModel(subVM);
                if (model.Attributes.Any())
                {
                    models.Add(model);
                }
            }
        }
    }

    public interface IHasAttributeGroupMenu
    {
        public VM_AttributeGroupMenu AttributeGroupMenu { get; set; }
    }
}
