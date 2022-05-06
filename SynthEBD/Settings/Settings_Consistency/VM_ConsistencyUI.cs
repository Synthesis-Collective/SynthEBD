using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Skyrim;
using Noggog;
using System.Collections.ObjectModel;
using System.ComponentModel;
using Noggog.WPF;
using ReactiveUI;

namespace SynthEBD;

public class VM_ConsistencyUI : ViewModel
{
    public VM_ConsistencyUI()
    {
        this.PropertyChanged += RefereshCurrentAssignment;
        
        _linkCache = PatcherEnvironmentProvider.Instance.WhenAnyValue(x => x.Environment.LinkCache)
            .ToProperty(this, nameof(lk), default(ILinkCache))
            .DisposeWith(this);

        DeleteCurrentNPC = new SynthEBD.RelayCommand(
            canExecute: _ => true,
            execute: x =>
            {
                this.CurrentlyDisplayedAssignment = null;
                this.Assignments.Remove(CurrentlyDisplayedAssignment);
            }
        );

        DeleteAllAssets = new SynthEBD.RelayCommand(
            canExecute: _ => true,
            execute: x =>
            {
                foreach (var assignment in Assignments)
                {
                    assignment.AssetPackName = "";
                    assignment.SubgroupIDs.Clear();
                    assignment.AssetReplacements.Clear();
                    assignment.MixInAssignments.Clear();
                }
                Logger.CallTimedLogErrorWithStatusUpdateAsync("Cleared asset consistency", ErrorType.Warning, 2);
            }
        );

        DeleteAllBodyShape = new SynthEBD.RelayCommand(
            canExecute: _ => true,
            execute: x =>
            {
                foreach (var assignment in Assignments)
                {
                    assignment.BodyGenMorphNames.Clear();
                    assignment.BodySlidePreset = "";
                }
                Logger.CallTimedLogErrorWithStatusUpdateAsync("Cleared body shape consistency", ErrorType.Warning, 2);
            }
        );

        DeleteAllHeight = new SynthEBD.RelayCommand(
            canExecute: _ => true,
            execute: x =>
            {
                foreach (var assignment in Assignments)
                {
                    assignment.Height = "";
                }
                Logger.CallTimedLogErrorWithStatusUpdateAsync("Cleared height consistency", ErrorType.Warning, 2);
            }
        );

        DeleteAllNPCs = new SynthEBD.RelayCommand(
            canExecute: _ => true,
            execute: x =>
            {
                if (CustomMessageBox.DisplayNotificationYesNo("Confirmation", "Are you sure you want to completely clear the consistency file?"))
                {
                    this.CurrentlyDisplayedAssignment = null;
                    this.Assignments.Clear();
                }
                Logger.CallTimedLogErrorWithStatusUpdateAsync("Cleared all consistency", ErrorType.Warning, 2);
            }
        );
    }

    public ObservableCollection<VM_ConsistencyAssignment> Assignments { get; set; } = new();
    public VM_ConsistencyAssignment CurrentlyDisplayedAssignment { get; set; } = null;
    private readonly ObservableAsPropertyHelper<ILinkCache> _linkCache;
    public ILinkCache lk => _linkCache.Value;
    public IEnumerable<Type> AllowedFormKeyTypes { get; set; } = typeof(INpcGetter).AsEnumerable();
    public FormKey SelectedNPCFormKey { get; set; } = new();

    public RelayCommand DeleteCurrentNPC { get; set; }

    public RelayCommand DeleteAllAssets { get; set; }
    public RelayCommand DeleteAllBodyShape { get; set; }
    public RelayCommand DeleteAllHeight { get; set; }
    public RelayCommand DeleteAllNPCs { get; set; }

    public void RefereshCurrentAssignment(object sender, PropertyChangedEventArgs e)
    {
        this.CurrentlyDisplayedAssignment = this.Assignments.Where(x => x.NPCFormKey.ToString() == SelectedNPCFormKey.ToString()).FirstOrDefault();
    }

    public static void GetViewModelsFromModels(Dictionary<string, NPCAssignment> models, ObservableCollection<VM_ConsistencyAssignment> viewModels, ObservableCollection<VM_AssetPack> AssetPackVMs)
    {
        viewModels.Clear();
        foreach (var model in models)
        {
            if (model.Value == null) { continue; }
            viewModels.Add(VM_ConsistencyAssignment.GetViewModelFromModel(model.Value, AssetPackVMs));
        }
    }

    public static void DumpViewModelsToModels(ObservableCollection<VM_ConsistencyAssignment> viewModels, Dictionary<string, NPCAssignment> models)
    {
        models.Clear();
        foreach (var viewModel in viewModels)
        {
            models.Add(viewModel.NPCFormKey.ToString(), VM_ConsistencyAssignment.DumpViewModelToModel(viewModel));
        }
    }
}