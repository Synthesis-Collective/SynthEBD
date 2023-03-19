using DynamicData;
using System.Collections.ObjectModel;
using static SynthEBD.VM_NPCAttribute;

namespace SynthEBD;

public class VM_AttributeGroupMenu : VM
{
    private readonly VM_NPCAttributeCreator _attributeCreator;
    private readonly Logger _logger;
    public delegate VM_AttributeGroupMenu Factory(VM_AttributeGroupMenu generalSettingsAttributes, bool showImportFromGeneralOption);
    public VM_AttributeGroupMenu(VM_AttributeGroupMenu generalSettingsAttributes, bool showImportFromGeneralOption, VM_NPCAttributeCreator attributeCreator, Logger logger)
    {
        _attributeCreator = attributeCreator;
        _logger = logger;

        GeneralSettingsAttributes = generalSettingsAttributes;
        ShowImportFromGeneralOption = showImportFromGeneralOption;

        AddGroup = new RelayCommand(
            canExecute: _ => true,
            execute: _ => Groups.Add(new VM_AttributeGroup(this, _attributeCreator, _logger))
        );


        ImportAttributeGroups = new RelayCommand(
            canExecute: _ => true,
            execute: _ =>
            {
                var alreadyContainedGroups = Groups.Select(x => x.Label).ToHashSet();
                foreach (var attGroup in GeneralSettingsAttributes.Groups)
                {
                    if (!alreadyContainedGroups.Contains(attGroup.Label))
                    {
                        Groups.Add(attGroup.Copy(this));
                    }
                    else
                    { // overwrite existing definitions
                        var existingGroup = Groups.Where(x => x.Label == attGroup.Label).First();
                        existingGroup.Attributes.Clear();
                        var tempGroup = attGroup.Copy(this);
                        existingGroup.Attributes.AddRange(tempGroup.Attributes);
                    }
                }
            }
        );

        Alphabetizer = new(Groups, x => x.Label, new(System.Windows.Media.Colors.MediumPurple));
    }
    public ObservableCollection<VM_AttributeGroup> Groups { get; set; } = new();
    public VM_AttributeGroup DisplayedGroup { get; set; } = null;
    public VM_AttributeGroupMenu GeneralSettingsAttributes { get; set; }
    public bool ShowImportFromGeneralOption { get; }
    public RelayCommand AddGroup { get; }
    public RelayCommand ImportAttributeGroups { get; }

    public VM_Alphabetizer<VM_AttributeGroup, string> Alphabetizer { get; set; }

    public void CopyInViewModelFromModels(HashSet<AttributeGroup> models)
    {
        Groups.Clear();
        // first add each group to the menu
        foreach (var model in models)
        {
            var attrGroup = new VM_AttributeGroup(this, _attributeCreator, _logger);
            attrGroup.CopyInViewModelFromModel(model);
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
                            var groupAttributes = groupAttribute.GroupedSubAttributes.Where(x => x.Type == NPCAttributeType.Group).ToArray();
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