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
            execute: _ => ReplacerGroups.Add(assetReplaceGroupFactory(this))
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

public class VM_AssetReplacerGroup : VM, IHasSubgroupViewModels
{
    private readonly IEnvironmentStateProvider _environmentProvider;
    private readonly VM_Settings_General _generalSettingsVm;
    private readonly VM_Subgroup.Factory _subgroupFactory;
    private readonly VM_SubgroupPlaceHolder.Factory _subGroupPlaceHolderFactory;

    public delegate VM_AssetReplacerGroup Factory(VM_AssetPackDirectReplacerMenu parent);
    
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

        Remove = new SynthEBD.RelayCommand(
            canExecute: _ => true,
            execute: _ => ParentMenu.ReplacerGroups.Remove(this)
        );

        AddTopLevelSubgroup = new SynthEBD.RelayCommand(
            canExecute: _ => true,
            execute: _ => Subgroups.Add(subGroupPlaceHolderFactory(new AssetPack.Subgroup(), null, parent.ParentAssetPack, Subgroups))
        );

        SelectedSubgroupChanged = new RelayCommand(
            canExecute: _ => true,
            execute: x =>
            {
                if (DisplayedSubgroup != null)
                {
                    DisplayedSubgroup.AssociatedPlaceHolder.AssociatedModel = DisplayedSubgroup.DumpViewModelToModel();
                }
                var displayedSubgroupPlaceHolder = (VM_SubgroupPlaceHolder)x;
                DisplayedSubgroup = _subgroupFactory(_generalSettingsVm.RaceGroupingEditor.RaceGroupings, ParentMenu.ParentAssetPack, null, false);
                DisplayedSubgroup.CopyInViewModelFromModel(displayedSubgroupPlaceHolder);
            });
        /*
        this.WhenAnyValue(x => x.TemplateNPCFK).Subscribe(x =>
        {
            foreach (var sg in Subgroups)
            {
                SetTemplates(sg, TemplateNPCFK);
            }
        }).DisposeWith(this);*/
    }

    public string Label { get; set; } = "";
    public ObservableCollection<VM_SubgroupPlaceHolder> Subgroups { get; set; } = new();
    public VM_Subgroup DisplayedSubgroup { get; set; }
    public VM_AssetPackDirectReplacerMenu ParentMenu{ get; set; }
    public FormKey TemplateNPCFK { get; set; }
    public ILinkCache lk { get; private set; }
    public IEnumerable<Type> NPCType { get; set; } = typeof(INpcGetter).AsEnumerable();
    public RelayCommand Remove { get; }
    public RelayCommand AddTopLevelSubgroup { get; }
    public RelayCommand SelectedSubgroupChanged { get; }

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
            //SetTemplates(sgVM, TemplateNPCFK);
            Subgroups.Add(sgVM);
        }
    }

    public static AssetReplacerGroup DumpViewModelToModel(VM_AssetReplacerGroup viewModel)
    {
        AssetReplacerGroup model = new AssetReplacerGroup();
        model.Label = viewModel.Label;
        model.TemplateNPCFormKey = viewModel.TemplateNPCFK;
        /*
        foreach (var svm in viewModel.Subgroups)
        {
            model.Subgroups.Add(svm.DumpViewModelToModel());
        }
        */
        return model;
    }

    /*
    private static void SetTemplates(VM_Subgroup subgroup, FormKey templateNPCFormKey)
    {
        subgroup.PathsMenu.ReferenceNPCFK = new FormKey(templateNPCFormKey.ModKey, templateNPCFormKey.ID);
        foreach (var sg in subgroup.Subgroups)
        {
            SetTemplates(sg, templateNPCFormKey);
        }
    }
    */
}