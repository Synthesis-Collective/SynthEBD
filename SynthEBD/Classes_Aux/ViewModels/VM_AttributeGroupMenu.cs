using System.Collections.ObjectModel;

namespace SynthEBD;

public class VM_AttributeGroupMenu : VM
{
    public delegate VM_AttributeGroupMenu Factory(bool showImportFromGeneralOption);
    
    public VM_AttributeGroupMenu(VM_AttributeGroupMenu generalSettingsAttributes, bool showImportFromGeneralOption)
    {
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
    public ObservableCollection<VM_AttributeGroup> Groups { get; set; } = new();
    public VM_AttributeGroup DisplayedGroup { get; set; } = null;
    public VM_AttributeGroupMenu GeneralSettingsAttributes { get; set; }
    public bool ShowImportFromGeneralOption { get; }
    public RelayCommand AddGroup { get; }
    public RelayCommand ImportAttributeGroups { get; }

    public void CopyInViewModelFromModels(HashSet<AttributeGroup> models)
    {
        Groups.Clear();
        // first add each group to the menu
        foreach (var model in models)
        {
            var attrGroup = new VM_AttributeGroup(this);
            attrGroup.CopyInViewModelFromModel(model, this);
            Groups.Add(attrGroup);
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
                        var correspondingVM = Groups.Where(x => x.Label == model.Label).First();
                        foreach (var groupAttribute in correspondingVM.Attributes)
                        {
                            var groupAttributes = groupAttribute.GroupedSubAttributes.Where(x => x.Type == NPCAttributeType.Group);
                            foreach (var groupAtt in groupAttributes)
                            {
                                var castGroupAtt = (VM_NPCAttributeGroup)groupAtt.Attribute;
                                var checkListEntries = castGroupAtt.SelectableAttributeGroups.Where(x => subAttModel.SelectedLabels.Contains(x.SubscribedAttributeGroup.Label)).ToHashSet();
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
    public VM_AttributeGroupMenu AttributeGroupMenu { get; }
}