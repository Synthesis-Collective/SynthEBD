using System.Collections.ObjectModel;
using Noggog.WPF;

namespace SynthEBD;

public class VM_BodyGenGroupsMenu : ViewModel
{
    public VM_BodyGenGroupsMenu(VM_BodyGenConfig parentMenu)
    {
        this.TemplateGroupsCheckList = new VM_CollectionMemberStringCheckboxList(this.TemplateGroups);
        this.ParentMenu = parentMenu;

        AddTemplateGroup = new SynthEBD.RelayCommand(
            canExecute: _ => true,
            execute: _ => this.TemplateGroups.Add(new VM_CollectionMemberString("", this.TemplateGroups))
        );
    }

    public ObservableCollection<VM_CollectionMemberString> TemplateGroups { get; set; } = new();
    public VM_CollectionMemberStringCheckboxList TemplateGroupsCheckList { get; set; }
    public VM_BodyGenConfig ParentMenu { get; set; }

    public RelayCommand AddTemplateGroup { get; }
    public RelayCommand RemoveTemplateGroup { get; }
}