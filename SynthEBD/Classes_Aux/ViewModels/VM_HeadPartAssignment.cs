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
using Noggog;

namespace SynthEBD
{
    public class VM_HeadPartAssignment : VM
    {
        private IEnvironmentStateProvider _environmentProvider;
        public delegate VM_HeadPartAssignment Factory(VM_HeadPart template, VM_Settings_Headparts parentConfig, HeadPart.TypeEnum type, IHasSynthEBDGender parentAssignmentGender, IHasHeadPartAssignments parentAssignment);
        public VM_HeadPartAssignment(VM_HeadPart template, VM_Settings_Headparts parentConfig, HeadPart.TypeEnum type, IHasSynthEBDGender parentAssignmentGender, IHasHeadPartAssignments parentAssignment, IEnvironmentStateProvider environmentProvider)
        {
            _environmentProvider = environmentProvider;
            
            Type = type;
            ParentConfig = parentConfig;
            ParentAssignmentGender = parentAssignmentGender;
            ParentAssignment = parentAssignment;

            ParentConfig.Types[Type].HeadPartList.ToObservableChangeSet().Subscribe(x => RefreshAvailable()).DisposeWith(this);
            this.WhenAnyValue(x => x.ParentAssignmentGender.NPCFormKey).Subscribe(x => RefreshAvailable()).DisposeWith(this);

            this.WhenAnyValue(x => x.EditorID).Subscribe(x =>
                {
                var assignment = parentConfig.Types[type].HeadPartList.Where(x => x.Label == EditorID).FirstOrDefault();
                if (assignment != null)
                {
                    FormKey = assignment.AssociatedModel.HeadPartFormKey;
                }
            }).DisposeWith(this);

            this.WhenAnyValue(x => x.FormKey).Subscribe(x =>
            {
                if (FormKey.IsNull) { BorderColor = new(Colors.Yellow); }
                else if (_environmentProvider.LinkCache.TryResolve<IHeadPartGetter>(FormKey, out _)) { BorderColor = new(Colors.Green); }
                else { BorderColor = new(Colors.Red); }
            }).DisposeWith(this);

            ClearSelection = new SynthEBD.RelayCommand(
                canExecute: _ => true,
                execute: _ => {
                    FormKey = new();
                    EditorID = String.Empty;
                }
            );
        }
        public FormKey FormKey { get; set; }
        public string EditorID { get; set; }
        public IHasSynthEBDGender ParentAssignmentGender { get; set; }
        public IHasHeadPartAssignments ParentAssignment { get; set; }
        public Gender? Gender { get; set; }
        public HeadPart.TypeEnum Type { get; set; }
        public VM_Settings_Headparts ParentConfig { get; set; }
        public ObservableCollection<VM_HeadPartPlaceHolder> AvailableHeadParts { get; set; }
        public RelayCommand ClearSelection { get; set; }
        public SolidColorBrush BorderColor { get; set; }

        public void RefreshAvailable()
        {
            var all = ParentConfig.Types[Type].HeadPartList.ToHashSet();
            var available = all.Where(x => HeadPartGenderMatches(ParentAssignmentGender.Gender, x.AssociatedModel)).ToArray();
            AvailableHeadParts = new(available);
        }

        public static bool HeadPartGenderMatches(Gender npcGender, HeadPartSetting headPart)
        {
            if ((npcGender == SynthEBD.Gender.Male && headPart.bAllowMale) || (npcGender == SynthEBD.Gender.Female && headPart.bAllowFemale)) { return true; }
            else { return false; }
        }

        public void CopyInFromModel(HeadPartConsistency model, HeadPart.TypeEnum type, VM_Settings_Headparts parentConfig, IHasSynthEBDGender parentAssignmentGender, IHasHeadPartAssignments parentAssignment, IEnvironmentStateProvider environmentProvider)
        {
            if (model is not null)
            {
                FormKey = model.FormKey;
                EditorID = EditorIDHandler.GetEditorIDSafely<IHeadPartGetter>(FormKey, _environmentProvider.LinkCache);
            }
        }

        public HeadPartConsistency DumpToModel()
        {
            return new HeadPartConsistency() { FormKey = FormKey, EditorID = EditorID };
        }
    }

    public interface IHasSynthEBDGender
    {
        FormKey NPCFormKey { get; set; }
        Gender Gender { get; set; }
    };

    public interface IHasHeadPartAssignments
    {
        Dictionary<HeadPart.TypeEnum, VM_HeadPartAssignment> HeadParts { get; set; }
    }
}
