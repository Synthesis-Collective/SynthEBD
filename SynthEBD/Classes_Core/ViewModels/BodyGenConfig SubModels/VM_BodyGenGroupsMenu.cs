using System.Collections.ObjectModel;

namespace SynthEBD;

public class VM_BodyGenGroupsMenu : VM
{
    public VM_BodyGenGroupsMenu(VM_BodyGenConfig parentMenu)
    {
        ParentMenu = parentMenu;

        AddTemplateGroup = new SynthEBD.RelayCommand(
            canExecute: _ => true,
            execute: _ => TemplateGroups.Add(new VM_CollectionMemberString("", TemplateGroups))
        );
    }

    public ObservableCollection<VM_CollectionMemberString> TemplateGroups { get; set; } = new();
    public VM_BodyGenConfig ParentMenu { get; set; }

    public RelayCommand AddTemplateGroup { get; }
    public RelayCommand RemoveTemplateGroup { get; }
}