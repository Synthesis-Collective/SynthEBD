using System.Collections.ObjectModel;

namespace SynthEBD;

/// <summary>
/// View model for the BodyGen config editor's template-groups menu. Holds the editable list of
/// template-group names (<see cref="TemplateGroups"/>) that templates and racial mappings reference.
/// </summary>
public class VM_BodyGenGroupsMenu : VM
{
    /// <summary>Wires up the AddTemplateGroup command that appends a new blank group string.</summary>
    public VM_BodyGenGroupsMenu(VM_BodyGenConfig parentMenu)
    {
        ParentMenu = parentMenu;

        AddTemplateGroup = new RelayCommand(
            canExecute: _ => true,
            execute: _ => TemplateGroups.Add(new VM_CollectionMemberString("", TemplateGroups))
        );
    }

    public ObservableCollection<VM_CollectionMemberString> TemplateGroups { get; set; } = new();
    public VM_BodyGenConfig ParentMenu { get; set; }

    public RelayCommand AddTemplateGroup { get; }
}