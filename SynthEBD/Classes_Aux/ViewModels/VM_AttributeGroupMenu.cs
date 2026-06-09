using DynamicData;
using System.Collections.ObjectModel;
using System.Linq;
using static SynthEBD.VM_NPCAttribute;

namespace SynthEBD;

/// <summary>
/// View model for the attribute-group menu: an editable collection of <see cref="VM_AttributeGroup"/> with
/// add/import commands and alphabetization, round-tripping to/from a set of <see cref="AttributeGroup"/>.
/// </summary>
public class VM_AttributeGroupMenu : VM
{
    private readonly VM_NPCAttributeCreator _attributeCreator;
    private readonly Logger _logger;
    /// <summary>Autofac factory delegate for constructing the menu (optionally exposing import-from-general).</summary>
    public delegate VM_AttributeGroupMenu Factory(VM_AttributeGroupMenu generalSettingsAttributes, bool showImportFromGeneralOption);
    /// <summary>Creates the menu, wiring add-group/import commands and the alphabetizer.</summary>
    /// <param name="generalSettingsAttributes">The General-Settings attribute-group menu (import source).</param>
    /// <param name="showImportFromGeneralOption">Whether to show the import action.</param>
    /// <param name="attributeCreator">Factory for attribute VMs.</param>
    /// <param name="logger">Logger for diagnostics.</param>
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
                ImportFromGeneralSettings();
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

    /// <summary>Syncs the group collection to a set of models: removes groups no longer present, adds missing ones, then resolves cross-group selections once every group exists.</summary>
    /// <param name="models">The attribute-group models to load.</param>
    public void CopyInViewModelFromModels(HashSet<AttributeGroup> models)
    {
        // first remove groups in view model that no longer exist in the dto, 
        var dtoGroups = models.Select(x => x.Label).ToArray();
        for (int i = 0; i < Groups.Count; i++)
        {
            if (!dtoGroups.Contains(Groups[i].Label))
            {
                Groups.RemoveAt(i);
                i--;
            }
        }

        var viewModelGroups = Groups.Select(x => x.Label).ToArray();
        // Then add any groups from the dto that are missing in the view model
        foreach (var model in models.Where(x => !viewModelGroups.Contains(x.Label)).ToArray())
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
                        var correspondingVM = Groups.First(x => x.Label == model.Label);
                        foreach (var groupAttribute in correspondingVM.Attributes)
                        {
                            var groupAttributes = groupAttribute.GroupedSubAttributes.Where(x => x.Type == NPCAttributeType.Group).ToArray();
                            foreach (var groupAtt in groupAttributes)
                            {
                                var castGroupAtt = (VM_NPCAttributeGroup)groupAtt.Attribute;

                                foreach (var selectable in castGroupAtt.SelectableAttributeGroups)
                                {
                                    selectable.IsSelected = subAttModel.SelectedLabels.Contains(selectable.SubscribedAttributeGroup.Label);
                                }
                            }
                        }
                    }
                }
            }
        }
    }

    /// <summary>Adds a single group VM populated from a model.</summary>
    /// <param name="model">The group model to add.</param>
    public void AddAttributeGroupFromModel(AttributeGroup model)
    {
        var attrGroup = new VM_AttributeGroup(this, _attributeCreator, _logger);
        attrGroup.CopyInViewModelFromModel(model);
        Groups.Add(attrGroup);
    }

    /// <summary>Projects the menu's groups back into the model set (cleared first), skipping empty groups.</summary>
    /// <param name="viewModel">The menu to project.</param>
    /// <param name="models">Target set (cleared and repopulated).</param>
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

    /// <summary>Appends General-Settings groups not present here, and overwrites the attributes of any that share a label.</summary>
    public void ImportFromGeneralSettings()
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
                var existingGroup = Groups.First(x => x.Label == attGroup.Label);
                existingGroup.Attributes.Clear();
                var tempGroup = attGroup.Copy(this);
                existingGroup.Attributes.AddRange(tempGroup.Attributes);
            }
        }
    }
}

/// <summary>Implemented by view models that expose an attribute-group menu.</summary>
public interface IHasAttributeGroupMenu
{
    /// <summary>The hosted attribute-group menu.</summary>
    public VM_AttributeGroupMenu AttributeGroupMenu { get; }
}