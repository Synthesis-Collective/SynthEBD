using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SynthEBD
{
    class VM_BodyGenGroupsMenu : INotifyPropertyChanged
    {
        public VM_BodyGenGroupsMenu(VM_BodyGenConfig parentMenu)
        {
            this.TemplateGroups = new ObservableCollection<VM_CollectionMemberString>();
            this.TemplateGroupsCheckList = new VM_CollectionMemberStringCheckboxList(this.TemplateGroups);
            this.ParentMenu = parentMenu;

            AddTemplateGroup = new SynthEBD.RelayCommand(
                canExecute: _ => true,
                execute: _ => this.TemplateGroups.Add(new VM_CollectionMemberString("", this.TemplateGroups))
                );
        }

        public ObservableCollection<VM_CollectionMemberString> TemplateGroups { get; set; }
        public VM_CollectionMemberStringCheckboxList TemplateGroupsCheckList { get; set; }
        public VM_BodyGenConfig ParentMenu { get; set; }

        public RelayCommand AddTemplateGroup { get; }
        public RelayCommand RemoveTemplateGroup { get; }
        public event PropertyChangedEventHandler PropertyChanged;
    }
}
