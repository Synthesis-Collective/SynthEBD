using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Skyrim;
using Noggog;
using System.Collections.ObjectModel;
using ReactiveUI;

namespace SynthEBD;

public class VM_AssetPackDirectReplacerMenu : VM
{
    private readonly VM_AssetReplacerGroup.Factory _assetReplaceGroupFactory;

    public delegate VM_AssetPackDirectReplacerMenu Factory(VM_AssetPack parent);
    
    public VM_AssetPackDirectReplacerMenu(VM_AssetPack parent, VM_AssetReplacerGroup.Factory assetReplaceGroupFactory)
    {
        _assetReplaceGroupFactory = assetReplaceGroupFactory;
        ParentAssetPack = parent;

        AddGroup = new SynthEBD.RelayCommand(
            canExecute: _ => true,
            execute: _ => this.ReplacerGroups.Add(assetReplaceGroupFactory(this))
        );
    }
    public ObservableCollection<VM_AssetReplacerGroup> ReplacerGroups { get; set; } = new();
    public VM_AssetReplacerGroup DisplayedGroup { get; set; }
    public VM_AssetPack ParentAssetPack { get; set; }

    public RelayCommand AddGroup { get; }

    public void CopyInViewModelFromModels(List<AssetReplacerGroup> models)
    {
        foreach(var model in models)
        {
            var subVm = _assetReplaceGroupFactory(this);
            subVm.CopyInViewModelFromModel(model);
            ReplacerGroups.Add(subVm);
        }
    }

    public static List<AssetReplacerGroup> DumpViewModelToModels(VM_AssetPackDirectReplacerMenu viewModel)
    {
        List<AssetReplacerGroup> models = new List<AssetReplacerGroup>();
        foreach (var subViewModel in viewModel.ReplacerGroups)
        {
            models.Add(VM_AssetReplacerGroup.DumpViewModelToModel(subViewModel));
        }
        return models;
    }
}

public class VM_AssetReplacerGroup : VM
{
    private readonly IStateProvider _stateProvider;
    private readonly VM_Settings_General _generalSettingsVm;
    private readonly VM_Subgroup.Factory _subGroupFactory;

    public delegate VM_AssetReplacerGroup Factory(VM_AssetPackDirectReplacerMenu parent);
    
    public VM_AssetReplacerGroup(VM_AssetPackDirectReplacerMenu parent, IStateProvider stateProvider, VM_Settings_General generalSettingsVM, VM_Subgroup.Factory subGroupFactory)
    {
        _stateProvider = stateProvider;
        _generalSettingsVm = generalSettingsVM;
        _subGroupFactory = subGroupFactory;
        ParentMenu = parent;

        _stateProvider.WhenAnyValue(x => x.LinkCache)
            .Subscribe(x => lk = x)
            .DisposeWith(this);

        Remove = new SynthEBD.RelayCommand(
            canExecute: _ => true,
            execute: _ => ParentMenu.ReplacerGroups.Remove(this)
        );

        AddTopLevelSubgroup = new SynthEBD.RelayCommand(
            canExecute: _ => true,
            execute: _ => this.Subgroups.Add(subGroupFactory(parent.ParentAssetPack.RaceGroupingEditor.RaceGroupings, Subgroups, parent.ParentAssetPack, null, true))
        );
            
        this.WhenAnyValue(x => x.TemplateNPCFK).Subscribe(x =>
        {
            foreach (var sg in Subgroups)
            {
                SetTemplates(sg, TemplateNPCFK);
            }
        });
    }

    public string Label { get; set; } = "";
    public ObservableCollection<VM_Subgroup> Subgroups { get; set; } = new();

    public VM_AssetPackDirectReplacerMenu ParentMenu{ get; set; }

    public FormKey TemplateNPCFK { get; set; }
    
    public ILinkCache lk { get; private set; }
    public IEnumerable<Type> NPCType { get; set; } = typeof(INpcGetter).AsEnumerable();

    public RelayCommand Remove { get; }

    public RelayCommand AddTopLevelSubgroup { get; }

    public void CopyInViewModelFromModel(AssetReplacerGroup model)
    {
        Label = model.Label;
        TemplateNPCFK = model.TemplateNPCFormKey;
        foreach (var sg in model.Subgroups)
        {
            var sgVM = _subGroupFactory(
                _generalSettingsVm.RaceGroupingEditor.RaceGroupings,
                Subgroups, 
                ParentMenu.ParentAssetPack,
                null,
                true);
            sgVM.CopyInViewModelFromModel(sg);
            SetTemplates(sgVM, TemplateNPCFK);
            Subgroups.Add(sgVM);
        }
        ObservableCollection<VM_Subgroup> flattenedSubgroupList = VM_AssetPack.FlattenSubgroupVMs(Subgroups, new ObservableCollection<VM_Subgroup>());
        VM_AssetPack.LinkRequiredSubgroups(flattenedSubgroupList);
        VM_AssetPack.LinkExcludedSubgroups(flattenedSubgroupList);
    }

    public static AssetReplacerGroup DumpViewModelToModel(VM_AssetReplacerGroup viewModel)
    {
        AssetReplacerGroup model = new AssetReplacerGroup();
        model.Label = viewModel.Label;
        model.TemplateNPCFormKey = viewModel.TemplateNPCFK;
        foreach (var svm in viewModel.Subgroups)
        {
            model.Subgroups.Add(VM_Subgroup.DumpViewModelToModel(svm));
        }
        return model;
    }

    private static void SetTemplates(VM_Subgroup subgroup, FormKey templateNPCFormKey)
    {
        subgroup.PathsMenu.ReferenceNPCFK = new FormKey(templateNPCFormKey.ModKey, templateNPCFormKey.ID);
        foreach (var sg in subgroup.Subgroups)
        {
            SetTemplates(sg, templateNPCFormKey);
        }
    }
}