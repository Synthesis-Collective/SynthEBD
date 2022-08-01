using DynamicData.Binding;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;
using ReactiveUI;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Media;

namespace SynthEBD
{
    public class VM_HeadPartAssignment : VM
    {
        public VM_HeadPartAssignment(VM_HeadPart template, ObservableCollection<VM_HeadPartAssignment> parentCollection, VM_Settings_Headparts parentConfig, HeadPart.TypeEnum type, IHasSynthEBDGender parentAssignment)
        {
            if (template is not null)
            {
                FormKey = template.FormKey;
                EditorID = template.Label;
            }
            Type = type;
            ParentCollection = parentCollection;
            ParentConfig = parentConfig;
            ParentAssignment = parentAssignment;

            ParentCollection.ToObservableChangeSet().Subscribe(x => RefreshAvailable());
            this.WhenAnyValue(x => x.EditorID).Subscribe(x =>
                {
                var assignment = parentConfig.Types[type].HeadPartList.Where(x => x.Label == EditorID).FirstOrDefault();
                if (assignment != null)
                {
                    FormKey = assignment.FormKey;
                }
            });

            this.WhenAnyValue(x => x.FormKey).Subscribe(x =>
            {
                if (FormKey.IsNull) { BorderColor = new(Colors.Yellow); }
                else if (PatcherEnvironmentProvider.Instance.Environment.LinkCache.TryResolve<IHeadPartGetter>(FormKey, out _)) { BorderColor = new(Colors.Green); }
                else { BorderColor = new(Colors.Red); }
            });

            

            DeleteMe = new SynthEBD.RelayCommand(
                canExecute: _ => true,
                execute: _ => ParentCollection.Remove(this)
            );
        }
        public FormKey FormKey { get; set; }
        public string EditorID { get; set; }
        public IHasSynthEBDGender ParentAssignment { get; set; }
        public Gender? Gender { get; set; }
        public HeadPart.TypeEnum Type { get; set; }
        public ObservableCollection<VM_HeadPartAssignment> ParentCollection { get; set; }
        public VM_Settings_Headparts ParentConfig { get; set; }
        public ObservableCollection<VM_HeadPart> AvailableHeadParts { get; set; }
        public RelayCommand DeleteMe { get; set; }
        public SolidColorBrush BorderColor { get; set; }

        public void RefreshAvailable()
        {
            var all = ParentConfig.Types[Type].HeadPartList.ToHashSet();
            var used = ParentCollection.Select(x => x.FormKey).ToHashSet();
            if (FormKey != null)
            {
                used.Remove(FormKey);
            }
            var available = all.Where(x => !used.Contains(x.FormKey) && HeadPartGenderMatches(ParentAssignment.Gender, x));
            AvailableHeadParts = new ObservableCollection<VM_HeadPart>(available);
        }

        public static bool HeadPartGenderMatches(Gender npcGender, VM_HeadPart headPart)
        {
            if ((npcGender == SynthEBD.Gender.Male && headPart.bAllowMale) || (npcGender == SynthEBD.Gender.Female && headPart.bAllowFemale)) { return true; }
            else { return false; }
        }

        public static VM_HeadPartAssignment GetViewModelFromModel(FormKeyEditorIDPair assignment, HeadPart.TypeEnum type, ObservableCollection<VM_HeadPartAssignment> parentCollection, VM_Settings_Headparts parentConfig, IHasSynthEBDGender parentAssignment)
        {
            var referencedHeadPart = parentConfig.Types[type].HeadPartList.Where(x => x.FormKey.Equals(assignment.FormKey)).FirstOrDefault();
            return new VM_HeadPartAssignment(referencedHeadPart, parentCollection, parentConfig, type, parentAssignment);
        }

        public FormKeyEditorIDPair DumpToModel()
        {
            return new FormKeyEditorIDPair() { FormKey = FormKey, EditorID = EditorID };
        }
    }

    public interface IHasSynthEBDGender
    {
        Gender Gender { get; set; }
    };
}
