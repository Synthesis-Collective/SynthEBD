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
    /// <summary>View model for a single head-part assignment (one head-part type) on an NPC: tracks the chosen part, validity color, and the parts available for the NPC's gender.</summary>
    public class VM_HeadPartAssignment : VM
    {
        private IEnvironmentStateProvider _environmentProvider;
        /// <summary>Autofac factory delegate for constructing an assignment.</summary>
        public delegate VM_HeadPartAssignment Factory(VM_HeadPart template, VM_Settings_Headparts parentConfig, HeadPart.TypeEnum type, IHasSynthEBDGender parentAssignmentGender, IHasHeadPartAssignments parentAssignment);
        /// <summary>Creates the assignment, wiring available-parts refresh, EditorID↔FormKey sync, validity coloring, and the clear command.</summary>
        /// <param name="template">The head-part template VM.</param>
        /// <param name="parentConfig">The head-parts settings VM.</param>
        /// <param name="type">The head-part type this assignment covers.</param>
        /// <param name="parentAssignmentGender">Supplies the NPC and its gender.</param>
        /// <param name="parentAssignment">The owning assignment set.</param>
        /// <param name="environmentProvider">Supplies the link cache for validity checks.</param>
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
                var assignment = parentConfig.Types[type].HeadPartList.FirstOrDefault(x => x.Label == EditorID);
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

            ClearSelection = new RelayCommand(
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

        /// <summary>Recomputes <see cref="AvailableHeadParts"/> as those head parts matching the NPC's gender.</summary>
        public void RefreshAvailable()
        {
            var all = ParentConfig.Types[Type].HeadPartList.ToHashSet();
            var available = all.Where(x => HeadPartGenderMatches(ParentAssignmentGender.Gender, x.AssociatedModel)).ToArray();
            AvailableHeadParts = new(available);
        }

        /// <summary>Whether a head part is allowed for the given gender.</summary>
        /// <param name="npcGender">The NPC's gender.</param>
        /// <param name="headPart">The head-part setting.</param>
        /// <returns><c>true</c> if the part allows that gender.</returns>
        public static bool HeadPartGenderMatches(Gender npcGender, HeadPartSetting headPart)
        {
            if ((npcGender == SynthEBD.Gender.Male && headPart.bAllowMale) || (npcGender == SynthEBD.Gender.Female && headPart.bAllowFemale)) { return true; }
            else { return false; }
        }

        /// <summary>Populates this assignment's FormKey/EditorID from a persisted consistency model.</summary>
        /// <param name="model">The consistency model; ignored when null.</param>
        /// <param name="type">The head-part type (unused; assignment already knows its type).</param>
        /// <param name="parentConfig">The head-parts settings VM (unused here).</param>
        /// <param name="parentAssignmentGender">The gender source (unused here).</param>
        /// <param name="parentAssignment">The owning assignment set (unused here).</param>
        /// <param name="environmentProvider">Environment provider (unused here; the field is used instead).</param>
        public void CopyInFromModel(HeadPartConsistency model, HeadPart.TypeEnum type, VM_Settings_Headparts parentConfig, IHasSynthEBDGender parentAssignmentGender, IHasHeadPartAssignments parentAssignment, IEnvironmentStateProvider environmentProvider)
        {
            if (model is not null)
            {
                FormKey = model.FormKey;
                EditorID = EditorIDHandler.GetEditorIDSafely<IHeadPartGetter>(FormKey, _environmentProvider.LinkCache);
            }
        }

        /// <summary>Projects this assignment into a <see cref="HeadPartConsistency"/> model.</summary>
        /// <returns>The populated model.</returns>
        public HeadPartConsistency DumpToModel()
        {
            return new HeadPartConsistency() { FormKey = FormKey, EditorID = EditorID };
        }
    }

    /// <summary>Implemented by view models that expose an NPC FormKey and gender (for gender-aware head-part filtering).</summary>
    public interface IHasSynthEBDGender
    {
        /// <summary>The NPC's FormKey.</summary>
        FormKey NPCFormKey { get; set; }
        /// <summary>The NPC's gender.</summary>
        Gender Gender { get; set; }
    };

    /// <summary>Implemented by view models that own a per-type map of head-part assignments.</summary>
    public interface IHasHeadPartAssignments
    {
        /// <summary>Head-part assignments keyed by head-part type.</summary>
        Dictionary<HeadPart.TypeEnum, VM_HeadPartAssignment> HeadParts { get; set; }
    }
}
