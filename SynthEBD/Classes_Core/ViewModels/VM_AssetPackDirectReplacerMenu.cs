using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Skyrim;
using Noggog;
using System.Collections.ObjectModel;
using ReactiveUI;

namespace SynthEBD;

public class VM_AssetPackDirectReplacerMenu : VM
{
    public VM_AssetPackDirectReplacerMenu(VM_AssetPack parent, VM_BodyShapeDescriptorCreationMenu OBodyDescriptorMenu)
    {
        ParentAssetPack = parent;

        AddGroup = new SynthEBD.RelayCommand(
            canExecute: _ => true,
            execute: _ => this.ReplacerGroups.Add(new VM_AssetReplacerGroup(this, OBodyDescriptorMenu))
        );
    }
    public ObservableCollection<VM_AssetReplacerGroup> ReplacerGroups { get; set; } = new();
    public VM_AssetReplacerGroup DisplayedGroup { get; set; }
    public VM_AssetPack ParentAssetPack { get; set; }

    public RelayCommand AddGroup { get; }

    public static VM_AssetPackDirectReplacerMenu GetViewModelFromModels(List<AssetReplacerGroup> models, VM_AssetPack parentAssetPack, VM_Settings_General generalSettingsVM, VM_BodyShapeDescriptorCreationMenu OBodyDescriptorMenu)
    {
        VM_AssetPackDirectReplacerMenu viewModel = new VM_AssetPackDirectReplacerMenu(parentAssetPack, OBodyDescriptorMenu);
        foreach(var model in models)
        {
            viewModel.ReplacerGroups.Add(VM_AssetReplacerGroup.GetViewModelFromModel(model, viewModel, generalSettingsVM, OBodyDescriptorMenu));
        }

        return viewModel;
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
    public VM_AssetReplacerGroup(VM_AssetPackDirectReplacerMenu parent, VM_BodyShapeDescriptorCreationMenu OBodyDescriptorMenu)
    {
        this.ParentMenu = parent;
        
        PatcherEnvironmentProvider.Instance.WhenAnyValue(x => x.Environment.LinkCache)
            .Subscribe(x => lk = x)
            .DisposeWith(this);

        Remove = new SynthEBD.RelayCommand(
            canExecute: _ => true,
            execute: _ => ParentMenu.ReplacerGroups.Remove(this)
        );

        AddTopLevelSubgroup = new SynthEBD.RelayCommand(
            canExecute: _ => true,
            execute: _ => this.Subgroups.Add(new VM_Subgroup(parent.ParentAssetPack.RaceGroupingList, Subgroups, parent.ParentAssetPack, OBodyDescriptorMenu, true))
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

    public static VM_AssetReplacerGroup GetViewModelFromModel(AssetReplacerGroup model, VM_AssetPackDirectReplacerMenu parentMenu, VM_Settings_General generalSettingsVM, VM_BodyShapeDescriptorCreationMenu OBodyDescriptorMenu)
    {
        VM_AssetReplacerGroup viewModel = new VM_AssetReplacerGroup(parentMenu, OBodyDescriptorMenu);
        viewModel.Label = model.Label;
        viewModel.TemplateNPCFK = model.TemplateNPCFormKey;
        foreach (var sg in model.Subgroups)
        {
            var sgVM = new VM_Subgroup(
                generalSettingsVM.RaceGroupings,
                viewModel.Subgroups, 
                viewModel.ParentMenu.ParentAssetPack,
                OBodyDescriptorMenu,
                true);
            sgVM.CopyInViewModelFromModel(sg, generalSettingsVM);
            SetTemplates(sgVM, viewModel.TemplateNPCFK);
            viewModel.Subgroups.Add(sgVM);
        }
        ObservableCollection<VM_Subgroup> flattenedSubgroupList = VM_AssetPack.FlattenSubgroupVMs(viewModel.Subgroups, new ObservableCollection<VM_Subgroup>());
        VM_AssetPack.LinkRequiredSubgroups(flattenedSubgroupList);
        VM_AssetPack.LinkExcludedSubgroups(flattenedSubgroupList);

        return viewModel;
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