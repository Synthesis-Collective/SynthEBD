using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Skyrim;
using Noggog;
using System.Collections.ObjectModel;
using ReactiveUI;
using System.Reactive.Linq;

namespace SynthEBD;

/// <summary>
/// View model for an asset pack's Direct Replacer menu: owns the collection of
/// <see cref="VM_AssetReplacerGroup"/>s and drives selection of the displayed group, persisting the
/// previously displayed group's subgroup and loading the newly selected group's first subgroup.
/// </summary>
public class VM_AssetPackDirectReplacerMenu : VM
{
    private readonly VM_AssetReplacerGroup.Factory _assetReplaceGroupFactory;

    /// <summary>Autofac factory delegate for constructing a <see cref="VM_AssetPackDirectReplacerMenu"/>.</summary>
    public delegate VM_AssetPackDirectReplacerMenu Factory(VM_AssetPack parent);

    /// <summary>
    /// Wires the DisplayedGroup buffered-pair subscription (dumps the previous group's displayed
    /// subgroup and loads the current group's first subgroup) and the AddGroup command.
    /// </summary>
    public VM_AssetPackDirectReplacerMenu(VM_AssetPack parent, VM_AssetReplacerGroup.Factory assetReplaceGroupFactory, VM_Subgroup.Factory subgroupFactory)
    {
        _assetReplaceGroupFactory = assetReplaceGroupFactory;
        ParentAssetPack = parent;

       this.WhenAnyValue(vm => vm.DisplayedGroup)
          .Buffer(2, 1)
          .Select(b => (Previous: b[0], Current: b[1]))
          .Subscribe(t => {
              if (t.Previous != null && t.Previous.DisplayedSubgroup != null)
              {
                  t.Previous.DisplayedSubgroup.DumpViewModelToModel();
              }

              if (t.Current != null && t.Current.Subgroups.Any())
              {
                  t.Current.DisplayedSubgroup = subgroupFactory(t.Current.Subgroups.First(), ParentAssetPack, true);
                  t.Current.DisplayedSubgroup.PathsMenu.ReferenceNPCFK = t.Current.TemplateNPCFK;
                  t.Current.DisplayedSubgroup.CopyInViewModelFromModel();
              }
          });

        this.WhenAnyValue(x => x.DisplayedGroup).Subscribe(x =>
        {
            
        }).DisposeWith(this);

        AddGroup = new RelayCommand(
            canExecute: _ => true,
            execute: _ => ReplacerGroups.Add(assetReplaceGroupFactory(this))
        );
    }
    public ObservableCollection<VM_AssetReplacerGroup> ReplacerGroups { get; set; } = new();
    public VM_AssetReplacerGroup DisplayedGroup { get; set; }
    public VM_AssetPack ParentAssetPack { get; set; }

    public RelayCommand AddGroup { get; }

    /// <summary>Models → view models: builds one <see cref="VM_AssetReplacerGroup"/> per model and appends them to <see cref="ReplacerGroups"/>.</summary>
    public void CopyInViewModelFromModels(List<AssetReplacerGroup> models)
    {
        foreach(var model in models)
        {
            var subVm = _assetReplaceGroupFactory(this);
            subVm.CopyInViewModelFromModel(model);
            ReplacerGroups.Add(subVm);
        }
    }

    /// <summary>View models → models: serializes every group in <see cref="ReplacerGroups"/> into a list of <see cref="AssetReplacerGroup"/>.</summary>
    public static List<AssetReplacerGroup> DumpViewModelToModels(VM_AssetPackDirectReplacerMenu viewModel)
    {
        List<AssetReplacerGroup> models = new List<AssetReplacerGroup>();
        foreach (var subViewModel in viewModel.ReplacerGroups)
        {
            models.Add(subViewModel.DumpViewModelToModel());
        }
        return models;
    }
}

/// <summary>
/// View model for a single <see cref="AssetReplacerGroup"/>: a named set of replacement subgroups
/// applied relative to a template NPC. Manages the placeholder subgroup tree and the currently
/// displayed/edited subgroup.
/// </summary>
public class VM_AssetReplacerGroup : VM, IHasSubgroupViewModels
{
    private readonly IEnvironmentStateProvider _environmentProvider;
    private readonly VM_Settings_General _generalSettingsVm;
    private readonly VM_Subgroup.Factory _subgroupFactory;
    private readonly VM_SubgroupPlaceHolder.Factory _subGroupPlaceHolderFactory;

    /// <summary>Autofac factory delegate for constructing a <see cref="VM_AssetReplacerGroup"/>.</summary>
    public delegate VM_AssetReplacerGroup Factory(VM_AssetPackDirectReplacerMenu parent);

    /// <summary>
    /// Follows the environment link cache, drives the SelectedPlaceHolder buffered-pair subscription
    /// (dumps the previously selected subgroup and loads the newly selected one), keeps the displayed
    /// subgroup's reference NPC in sync with <see cref="TemplateNPCFK"/>, and wires the Remove and
    /// AddTopLevelSubgroup commands.
    /// </summary>
    public VM_AssetReplacerGroup(VM_AssetPackDirectReplacerMenu parent, IEnvironmentStateProvider environmentProvider, VM_Settings_General generalSettingsVM, VM_Subgroup.Factory subgroupFactory, VM_SubgroupPlaceHolder.Factory subGroupPlaceHolderFactory)
    {
        _environmentProvider = environmentProvider;
        _generalSettingsVm = generalSettingsVM;
        _subgroupFactory = subgroupFactory;
        _subGroupPlaceHolderFactory = subGroupPlaceHolderFactory;
        ParentMenu = parent;

        _environmentProvider.WhenAnyValue(x => x.LinkCache)
            .Subscribe(x => lk = x)
            .DisposeWith(this);

        this.WhenAnyValue(vm => vm.SelectedPlaceHolder)
         .Buffer(2, 1)
         .Select(b => (Previous: b[0], Current: b[1]))
         .Subscribe(t => {
             if (t.Previous != null && t.Previous.AssociatedViewModel != null)
             {
                 t.Previous.AssociatedModel = t.Previous.AssociatedViewModel.DumpViewModelToModel();
             }

             if (t.Current != null)
             {
                 DisplayedSubgroup = _subgroupFactory(t.Current, ParentMenu.ParentAssetPack, true);
                 DisplayedSubgroup.CopyInViewModelFromModel();
                 DisplayedSubgroup.PathsMenu.ReferenceNPCFK = TemplateNPCFK;
                 ParentMenu.ParentAssetPack.SelectedPlaceHolder = t.Current;
                 t.Current.GetDDSPaths();
             }
         }).DisposeWith(this);

        Remove = new RelayCommand(
            canExecute: _ => true,
            execute: _ => ParentMenu.ReplacerGroups.Remove(this)
        );

        AddTopLevelSubgroup = new RelayCommand(
            canExecute: _ => true,
            execute: _ => Subgroups.Add(subGroupPlaceHolderFactory(new AssetPack.Subgroup(), null, parent.ParentAssetPack, Subgroups))
        );

        this.WhenAnyValue(x => x.TemplateNPCFK).Subscribe(x =>
        {
            if (DisplayedSubgroup != null)
            {
                DisplayedSubgroup.PathsMenu.ReferenceNPCFK = x;
            }
        }).DisposeWith(this);
    }

    public string Label { get; set; } = "";
    public ObservableCollection<VM_SubgroupPlaceHolder> Subgroups { get; set; } = new();
    public VM_Subgroup DisplayedSubgroup { get; set; }
    public VM_SubgroupPlaceHolder SelectedPlaceHolder { get; set; }
    public VM_AssetPackDirectReplacerMenu ParentMenu{ get; set; }
    public FormKey TemplateNPCFK { get; set; }
    public ILinkCache lk { get; private set; }
    public IEnumerable<Type> NPCType { get; set; } = typeof(INpcGetter).AsEnumerable();
    public RelayCommand Remove { get; }
    public RelayCommand AddTopLevelSubgroup { get; }

    /// <summary>Model → view model: loads the label, template NPC, and placeholder subgroup tree from an <see cref="AssetReplacerGroup"/>.</summary>
    public void CopyInViewModelFromModel(AssetReplacerGroup model)
    {
        Label = model.Label;
        TemplateNPCFK = model.TemplateNPCFormKey;
        foreach (var sg in model.Subgroups)
        {
            var sgVM = _subGroupPlaceHolderFactory(
                sg,
                null,
                ParentMenu.ParentAssetPack,
                Subgroups);
            Subgroups.Add(sgVM);
        }
    }

    /// <summary>View model → model: serializes the label, template NPC, and (saved) subgroup tree into a new <see cref="AssetReplacerGroup"/>.</summary>
    public AssetReplacerGroup DumpViewModelToModel()
    {
        AssetReplacerGroup model = new AssetReplacerGroup();
        model.Label = Label;
        model.TemplateNPCFormKey = TemplateNPCFK;

        if (DisplayedSubgroup != null)
        {
            DisplayedSubgroup.AssociatedPlaceHolder.AssociatedModel = DisplayedSubgroup.DumpViewModelToModel();
        }

        foreach (var svm in Subgroups)
        {
            svm.SaveToModel();
            model.Subgroups.Add(svm.AssociatedModel);
        }
        return model;
    }
}