using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Skyrim;
using Noggog;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using ReactiveUI;
using DynamicData.Binding;
using Noggog.WPF;

namespace SynthEBD;

public class VM_SpecificNPCAssignment : ViewModel, IHasForcedAssets
{
    public VM_SpecificNPCAssignment(MainWindow_ViewModel mainVM)
    {
        SubscribedGeneralSettings = mainVM.GeneralSettingsVM;
        SubscribedOBodySettings = mainVM.OBodySettingsVM;
        SubscribedBodyGenSettings = mainVM.BodyGenSettingsVM;

        this.ForcedAssetPack = new VM_AssetPack(mainVM);

        this.SubscribedAssetPacks = mainVM.TexMeshSettingsVM.AssetPacks;

        this.WhenAnyValue(x => x.NPCFormKey).Subscribe(x => RefreshAll());
            
        this.SubscribedAssetPacks.ToObservableChangeSet().Subscribe(x => RefreshAssets());
        this.WhenAnyValue(x => x.ForcedAssetPack).Subscribe(x => UpdateAvailableSubgroups(this));
        this.ForcedSubgroups.ToObservableChangeSet().Subscribe(x => UpdateAvailableSubgroups(this));

        this.WhenAnyValue(x => x.SubscribedBodyGenSettings).Subscribe(x => UpdateAvailableMorphs(this));
        this.ForcedBodyGenMorphs.ToObservableChangeSet().Subscribe(x => UpdateAvailableMorphs(this));
        this.SubscribedBodyGenSettings.MaleConfigs.ToObservableChangeSet().Subscribe(x => UpdateAvailableMorphs(this));
        this.SubscribedBodyGenSettings.FemaleConfigs.ToObservableChangeSet().Subscribe(x => UpdateAvailableMorphs(this));
        this.SubscribedBodyGenSettings.WhenAnyValue(x => x.CurrentMaleConfig).Subscribe(x => UpdateAvailableMorphs(this));
        this.SubscribedBodyGenSettings.WhenAnyValue(x => x.CurrentFemaleConfig).Subscribe(x => UpdateAvailableMorphs(this));

        this.WhenAnyValue(x => x.NPCFormKey).Subscribe(x => UpdateAvailableBodySlides(SubscribedOBodySettings, SubscribedGeneralSettings));
        SubscribedOBodySettings.BodySlidesUI.WhenAnyValue(x => x.BodySlidesFemale).Subscribe(x => UpdateAvailableBodySlides(SubscribedOBodySettings, SubscribedGeneralSettings));
        SubscribedOBodySettings.BodySlidesUI.WhenAnyValue(x => x.BodySlidesMale).Subscribe(x => UpdateAvailableBodySlides(SubscribedOBodySettings, SubscribedGeneralSettings));

        this.WhenAnyValue(x => x.ForcedAssetPack).Subscribe(x =>
        {
            ForcedAssetReplacements.Remove(ForcedAssetReplacements.Where(x => x.ParentAssetPack != ForcedAssetPack));
        });

        DeleteForcedAssetPack = new SynthEBD.RelayCommand(
            canExecute: _ => true,
            execute: x =>
            {
                this.ForcedSubgroups.Clear();
                this.ForcedAssetPack = new VM_AssetPack(mainVM);
            }
        );
        DeleteForcedSubgroup = new SynthEBD.RelayCommand(
            canExecute: _ => true,
            execute: x => this.ForcedSubgroups.Remove((VM_Subgroup)x)
        );

        DeleteForcedMorph = new SynthEBD.RelayCommand(
            canExecute: _ => true,
            execute: x => this.ForcedBodyGenMorphs.Remove((VM_BodyGenTemplate)x)
        );

        AddForcedMixIn = new SynthEBD.RelayCommand(
            canExecute: _ => true,
            execute: x => this.ForcedMixIns.Add(new VM_MixInSpecificAssignment(this, mainVM, ForcedMixIns))
        );

        AddForcedReplacer = new SynthEBD.RelayCommand(
            canExecute: _ => true,
            execute: x => this.ForcedAssetReplacements.Add(new VM_AssetReplacementAssignment(ForcedAssetPack, ForcedAssetReplacements))
        );

        DeleteForcedMixInSubgroup = new SynthEBD.RelayCommand(
            canExecute: _ => true,
            execute: x =>
            {
                var toDelete = (VM_Subgroup)x;
                foreach (var mixin in ForcedMixIns)
                {
                    if (mixin.ForcedSubgroups.Contains(toDelete))
                    {
                        mixin.ForcedSubgroups.Remove(toDelete);
                    }
                }
            }
        );

        UpdateAvailableAssetPacks(this);
        UpdateAvailableBodySlides(SubscribedOBodySettings, SubscribedGeneralSettings);
    }

    // Caption
    public string DispName { get; set; } = "New Assignment";

    //User-editable
    public FormKey NPCFormKey { get; set; } = new();
    public VM_AssetPack ForcedAssetPack { get; set; }
    public ObservableCollection<VM_Subgroup> ForcedSubgroups { get; set; } = new();
    public ObservableCollection<VM_MixInSpecificAssignment> ForcedMixIns { get; set; } = new();
    public ObservableCollection<VM_AssetReplacementAssignment> ForcedAssetReplacements { get; set; } = new();
    public string ForcedHeight { get; set; } = "";
    public ObservableCollection<VM_BodyGenTemplate> ForcedBodyGenMorphs { get; set; } = new();
    public string ForcedBodySlide { get; set; } = "";

    //Needed by UI
    public ObservableCollection<VM_AssetPack> AvailableAssetPacks { get; set; } = new();
    public ObservableCollection<VM_AssetPack> SubscribedAssetPacks { get; set; }

    public ObservableCollection<VM_Subgroup> AvailableSubgroups { get; set; } = new();

    public ObservableCollection<VM_AssetPack> AvailableMixInAssetPacks { get; set; } = new();
    public ObservableCollection<VM_BodyGenTemplate> AvailableMorphs { get; set; } = new();
    public VM_SettingsBodyGen SubscribedBodyGenSettings { get; set; }
    public ObservableCollection<VM_BodySlideSetting> SubscribedBodySlides { get; set; }
    public ObservableCollection<VM_BodySlideSetting> AvailableBodySlides { get; set; }
    public VM_BodyGenTemplate SelectedTemplate { get; set; }

    public Gender Gender = Gender.Female;

    public VM_Settings_General SubscribedGeneralSettings { get; set; }
    public VM_SettingsOBody SubscribedOBodySettings { get; set; }
    public ILinkCache lk => PatcherEnvironmentProvider.Environment.LinkCache;
    public IEnumerable<Type> NPCFormKeyTypes { get; set; } = typeof(INpcGetter).AsEnumerable();
    public RelayCommand DeleteForcedAssetPack { get; set; }
    public RelayCommand DeleteForcedSubgroup { get; set; }
    public RelayCommand DeleteForcedMorph { get; set; }
    public RelayCommand AddForcedMixIn { get; set; }
    public RelayCommand AddForcedReplacer { get; set; }

    public RelayCommand DeleteForcedMixInSubgroup { get; set; }

    public static VM_SpecificNPCAssignment GetViewModelFromModel(NPCAssignment model, MainWindow_ViewModel mainVM)
    {
        var viewModel = new VM_SpecificNPCAssignment(mainVM);
        viewModel.NPCFormKey = model.NPCFormKey;

        if (viewModel.NPCFormKey.IsNull)
        {
            return null;
        }

        var npcFormLink = new FormLink<INpcGetter>(viewModel.NPCFormKey);

        if (!npcFormLink.TryResolve(PatcherEnvironmentProvider.Environment.LinkCache, out var npcRecord))
        {
            Logger.LogMessage("Warning: the target NPC of the Specific NPC Assignment with FormKey " + viewModel.NPCFormKey.ToString() + " was not found in the current load order.");
            Logger.SwitchViewToLogDisplay();
        }

        viewModel.Gender = GetGender(viewModel.NPCFormKey);

        bool assetPackFound = false;
        if (model.AssetPackName.Length == 0) { assetPackFound = true; }
        else
        {
            LinkAssetPackToForcedAssignment(model, viewModel, model.AssetPackName, mainVM.TexMeshSettingsVM.AssetPacks);
        }

        foreach (var forcedMixIn in model.MixInAssignments)
        {
            viewModel.ForcedMixIns.Add(VM_MixInSpecificAssignment.GetViewModelFromModel(forcedMixIn, viewModel, mainVM, viewModel.ForcedMixIns));
        }

        if (model.Height != null)
        {
            viewModel.ForcedHeight = model.Height.ToString();
        }
        else
        {
            viewModel.ForcedHeight = "";
        }

        ObservableCollection<VM_BodyGenTemplate> templates = new ObservableCollection<VM_BodyGenTemplate>();
        switch (viewModel.Gender)
        {
            case Gender.Male:
                if (mainVM.BodyGenSettingsVM.CurrentMaleConfig != null)
                {
                    templates = mainVM.BodyGenSettingsVM.CurrentMaleConfig.TemplateMorphUI.Templates;
                }
                else
                {
                    templates = new ObservableCollection<VM_BodyGenTemplate>();
                }
                break;
            case Gender.Female:
                if (mainVM.BodyGenSettingsVM.CurrentFemaleConfig != null)
                {
                    templates = mainVM.BodyGenSettingsVM.CurrentFemaleConfig.TemplateMorphUI.Templates;
                }
                else
                {
                    templates = new ObservableCollection<VM_BodyGenTemplate>();
                }
                break;
        }

        foreach (var forcedMorph in model.BodyGenMorphNames)
        {
            bool morphFound = false;
            foreach (var morph in templates)
            {
                if (morph.Label == forcedMorph)
                {
                    viewModel.ForcedBodyGenMorphs.Add(morph);
                    morphFound = true;
                    break; ;
                }
            }
            if (morphFound == false)
            {
                Logger.LogError("Warning: The forced BodyGen morph " + forcedMorph + " for NPC " + viewModel.DispName + " no longer exists.");
                Logger.SwitchViewToLogDisplay();
            }
        }


        foreach (var replacer in model.AssetReplacerAssignments)
        {
            var parentAssetPack = mainVM.TexMeshSettingsVM.AssetPacks.Where(x => x.GroupName == replacer.AssetPackName).FirstOrDefault();
            if (parentAssetPack != null)
            {
                viewModel.ForcedAssetReplacements.Add(VM_AssetReplacementAssignment.GetViewModelFromModel(replacer, parentAssetPack, viewModel.ForcedAssetReplacements));
            }
            else
            {
                Logger.LogError("Warning: The forced Asset Replacer " + replacer.AssetPackName + " for NPC " + viewModel.DispName + " no longer exists.");
                Logger.SwitchViewToLogDisplay();
            }
        }

        viewModel.ForcedBodySlide = model.BodySlidePreset;

        viewModel.DispName = Converters.CreateNPCDispNameFromFormKey(viewModel.NPCFormKey);

        return viewModel;
    }

    private static bool LinkAssetPackToForcedAssignment(NPCAssignment model, IHasForcedAssets viewModel, string assetPackName, ObservableCollection<VM_AssetPack> assetPacks)
    {
        bool assetPackFound = false;
        foreach (var ap in assetPacks)
        {
            if (ap.GroupName == assetPackName)
            {
                viewModel.ForcedAssetPack = ap;
                assetPackFound = true;

                foreach (var id in model.SubgroupIDs)
                {
                    var foundSubgroup = GetSubgroupByID(ap.Subgroups, id);
                    if (foundSubgroup != null)
                    {
                        viewModel.ForcedSubgroups.Add(foundSubgroup);
                        continue;
                    }
                    else
                    {
                        Logger.LogError("Warning: The forced Subgroup " + id + " for NPC " + model.DispName + " no longer exists.");
                        Logger.SwitchViewToLogDisplay();
                    }
                }
            }
        }

        if (!assetPackFound)
        {
            Logger.LogError("Warning: The forced Asset Pack " + assetPackName + " for NPC " + model.DispName + " no longer exists.");
            Logger.SwitchViewToLogDisplay();
        }

        return assetPackFound;
    }

    private static bool LinkAssetPackToForcedAssignment(NPCAssignment.MixInAssignment model, IHasForcedAssets viewModel, string assetPackName, ObservableCollection<VM_AssetPack> assetPacks, string npcName)
    {
        bool assetPackFound = false;
        foreach (var ap in assetPacks)
        {
            if (ap.GroupName == assetPackName)
            {
                viewModel.ForcedAssetPack = ap;
                assetPackFound = true;

                foreach (var id in model.SubgroupIDs)
                {
                    var foundSubgroup = GetSubgroupByID(ap.Subgroups, id);
                    if (foundSubgroup != null)
                    {
                        viewModel.ForcedSubgroups.Add(foundSubgroup);
                        continue;
                    }
                    else
                    {
                        Logger.LogError("Warning: The forced Subgroup " + id + " for NPC " + npcName + " no longer exists.");
                        Logger.SwitchViewToLogDisplay();
                    }
                }
            }
        }

        if (!assetPackFound)
        {
            Logger.LogError("Warning: The forced Asset Pack " + assetPackName + " for NPC " + npcName + " no longer exists.");
            Logger.SwitchViewToLogDisplay();
        }

        return assetPackFound;
    }

    public static NPCAssignment DumpViewModelToModel(VM_SpecificNPCAssignment viewModel)
    {
        NPCAssignment model = new NPCAssignment();
        model.DispName = viewModel.DispName;

        if (viewModel.ForcedAssetPack != null)
        {
            model.AssetPackName = viewModel.ForcedAssetPack.GroupName;
            model.SubgroupIDs = viewModel.ForcedSubgroups.Select(subgroup => subgroup.ID).ToList();

            model.AssetReplacerAssignments.Clear();
            foreach (var replacer in viewModel.ForcedAssetReplacements)
            {
                model.AssetReplacerAssignments.Add(VM_AssetReplacementAssignment.DumpViewModelToModel(replacer));
            }
        }

        foreach (var mixin in viewModel.ForcedMixIns)
        {
            if (!model.MixInAssignments.Select(x => x.AssetPackName).Contains(mixin.ForcedAssetPack.GroupName))
            {
                model.MixInAssignments.Add(VM_MixInSpecificAssignment.DumpViewModelToModel(mixin));
            }
        }

        if (viewModel.ForcedHeight == "")
        {
            model.Height = null;
        }
        else if (float.TryParse(viewModel.ForcedHeight, out var height))
        {
            model.Height = height;
        }
        else
        {
            Logger.LogError("Error parsing Specific NPC Assignment " + viewModel.DispName + ". Cannot parse height: " + viewModel.ForcedHeight);
        }
            
        model.BodyGenMorphNames = viewModel.ForcedBodyGenMorphs.Select(morph => morph.Label).ToList();
        model.BodySlidePreset = viewModel.ForcedBodySlide;
        model.NPCFormKey = viewModel.NPCFormKey;
        return model;
    }

    /*
    public void TriggerAvailableAssetPackUpdate(object sender, PropertyChangedEventArgs e)
    {
        UpdateAvailableAssetPacks(this);
    }

    public void TriggerAvailableAssetPackUpdate(object sender, NotifyCollectionChangedEventArgs e)
    {
        UpdateAvailableAssetPacks(this);
    }

    public void TriggerAvailableSubgroupsUpdate(object sender, PropertyChangedEventArgs e)
    {
        UpdateAvailableSubgroups(this);
    }

    public void TriggerAvailableSubgroupsUpdate(object sender, NotifyCollectionChangedEventArgs e)
    {
        UpdateAvailableSubgroups(this);
    }

    public void TriggerAvailableMorphsUpdate(object sender, PropertyChangedEventArgs e)
    {
        UpdateAvailableMorphs(this);
    }

    public void TriggerAvailableMorphsUpdate(object sender, NotifyCollectionChangedEventArgs e)
    {
        UpdateAvailableMorphs(this);
    }
    */

    public void UpdateAvailableAssetPacks(VM_SpecificNPCAssignment assignment)
    {
        assignment.AvailableAssetPacks.Clear();
        assignment.AvailableMixInAssetPacks.Clear();
        foreach (var assetPack in assignment.SubscribedAssetPacks)
        {
            if (assetPack.Gender == assignment.Gender)
            {
                if (assetPack.ConfigType == AssetPackType.Primary)
                {
                    assignment.AvailableAssetPacks.Add(assetPack);
                }
                else if (assetPack.ConfigType == AssetPackType.MixIn)
                {
                    assignment.AvailableMixInAssetPacks.Add(assetPack);
                }
            }
        }
    }

    public static void UpdateAvailableSubgroups(IHasForcedAssets assignment)
    {
        assignment.AvailableSubgroups.Clear();
        if (assignment.ForcedAssetPack == null) { return; }
        foreach (var topLevelSubgroup in assignment.ForcedAssetPack.Subgroups)
        {
            bool topLevelTaken = false;
            foreach (var forcedSubgroup in assignment.ForcedSubgroups)
            {
                if (topLevelSubgroup.ID == forcedSubgroup.ID || ContainsSubgroupID(topLevelSubgroup.Subgroups, forcedSubgroup.ID))
                {
                    topLevelTaken = true;
                    break;
                }
            }
            if (topLevelTaken == false)
            {
                assignment.AvailableSubgroups.Add(topLevelSubgroup);
            }
        }
    }

    public static bool ContainsSubgroupID(ObservableCollection<VM_Subgroup> subgroups, string id)
    {
        foreach(var sg in subgroups)
        {
            if (sg.ID == id) { return true; }
            else
            {
                if (ContainsSubgroupID(sg.Subgroups, id) == true) { return true; }
            }
        }
        return false;
    }

    public static VM_Subgroup GetSubgroupByID(ObservableCollection<VM_Subgroup> subgroups, string id)
    {
        foreach (var sg in subgroups)
        {
            if (sg.ID == id) { return sg; }
            else
            {
                var candidate = GetSubgroupByID(sg.Subgroups, id);
                if (candidate != null) { return candidate; }
            }
        }
        return null;
    }

    public static void UpdateAvailableMorphs(VM_SpecificNPCAssignment assignment)
    {
        // clear available morphs besides the ones that are forced (removing those from the available morph list also clears their combobox selection)
        assignment.AvailableMorphs.Clear();

        var allTemplateList = new ObservableCollection<VM_BodyGenTemplate>();
        switch (assignment.Gender)
        {
            case Gender.Male:
                if (assignment.SubscribedBodyGenSettings.CurrentMaleConfig != null && assignment.SubscribedBodyGenSettings.CurrentMaleConfig.TemplateMorphUI != null)
                { 
                    allTemplateList = assignment.SubscribedBodyGenSettings.CurrentMaleConfig.TemplateMorphUI.Templates;
                } 
                break;
            case Gender.Female:
                if (assignment.SubscribedBodyGenSettings.CurrentFemaleConfig != null && assignment.SubscribedBodyGenSettings.CurrentFemaleConfig.TemplateMorphUI != null)
                {
                    allTemplateList = assignment.SubscribedBodyGenSettings.CurrentFemaleConfig.TemplateMorphUI.Templates;
                }
                break;
        }

        foreach (var candidateMorph in allTemplateList)
        {
            if (assignment.ForcedBodyGenMorphs.Contains(candidateMorph))
            {
                continue;
            }

            bool groupOccupied = false;

            var candidateGroups = candidateMorph.GroupSelectionCheckList.CollectionMemberStrings.Where(x => x.IsSelected).Select(x => x.SubscribedString.Content).ToArray();

            foreach (var alreadyForcedMorph in assignment.ForcedBodyGenMorphs)
            {
                var forcedGroups = alreadyForcedMorph.GroupSelectionCheckList.CollectionMemberStrings.Where(x => x.IsSelected).Select(x => x.SubscribedString.Content).ToArray();

                if (candidateGroups.Intersect(forcedGroups).ToArray().Length > 0)
                {
                    groupOccupied = true;
                    break;
                }
            }

            if (groupOccupied == false)
            {
                assignment.AvailableMorphs.Add(candidateMorph);
            }
        }
    }

    public void UpdateAvailableBodySlides(VM_SettingsOBody oBodySettings, VM_Settings_General generalSettingsVM)
    {
        switch(Gender)
        {
            case Gender.Male: SubscribedBodySlides = oBodySettings.BodySlidesUI.BodySlidesMale; break;
            case Gender.Female: SubscribedBodySlides = oBodySettings.BodySlidesUI.BodySlidesFemale; break;
        }
        AvailableBodySlides = new ObservableCollection<VM_BodySlideSetting>() { new VM_BodySlideSetting(oBodySettings.DescriptorUI, generalSettingsVM.RaceGroupings, AvailableBodySlides, oBodySettings) { Label = "" } }; // blank entry
        AvailableBodySlides.AddRange(SubscribedBodySlides);
    }
    /*
    public void TriggerDispNameUpdate(object sender, PropertyChangedEventArgs e)
    {
        if (this.NPCFormKey.IsNull == false)
        {
            this.DispName = Converters.CreateNPCDispNameFromFormKey(this.NPCFormKey);
        }
    }*/

    public void RefreshAll()
    {
        if (this.NPCFormKey.IsNull)
        {
            return;
        }

        this.DispName = Converters.CreateNPCDispNameFromFormKey(this.NPCFormKey);
        this.Gender = GetGender(this.NPCFormKey);

        UpdateAvailableAssetPacks(this);
        UpdateAvailableSubgroups(this);
        UpdateAvailableMorphs(this);
        UpdateAvailableBodySlides(SubscribedOBodySettings, SubscribedGeneralSettings);
    }
        
    public void RefreshAssets()
    {
        UpdateAvailableAssetPacks(this);
        UpdateAvailableSubgroups(this);
    }

    /*
    public void TriggerGenderUpdate(object sender, PropertyChangedEventArgs e)
    {
        this.Gender = GetGender(this.NPCFormKey);
    }*/

    public static Gender GetGender (FormKey NPCFormKey)
    {
        var npcFormLink = new FormLink<INpcGetter>(NPCFormKey);

        if (npcFormLink.TryResolve(PatcherEnvironmentProvider.Environment.LinkCache, out var npcRecord))
        {
            if (npcRecord.Configuration.Flags.HasFlag(NpcConfiguration.Flag.Female))
            {
                return Gender.Female;
            }
            else
            {
                return Gender.Male;
            }
        }

        if (!NPCFormKey.IsNull)
        {
            Logger.LogError("Could not resolve gender of NPC with FormKey " + NPCFormKey.ToString() + " because it does not exist in the current load order.");
            Logger.SwitchViewToLogDisplay();
        }
        return Gender.Male;
    }

    public class VM_MixInSpecificAssignment : ViewModel, IHasForcedAssets
    {
        public VM_MixInSpecificAssignment(VM_SpecificNPCAssignment parent, MainWindow_ViewModel mainVM, ObservableCollection<VM_MixInSpecificAssignment> parentCollection)
        {
            ParentCollection = parentCollection;
            Parent = parent;

            this.AvailableMixInAssetPacks = Parent.AvailableMixInAssetPacks;
            this.ForcedAssetPack = new VM_AssetPack(mainVM);

            this.WhenAnyValue(x => x.ForcedAssetPack).Subscribe(x => UpdateAvailableSubgroups(this));
            this.ForcedSubgroups.CollectionChanged += TriggerAvailableSubgroupsUpdate;

            DeleteCommand = new SynthEBD.RelayCommand(
                canExecute: _ => true,
                execute: x =>
                {
                    ParentCollection.Remove(this);
                }
            );

            DeleteForcedSubgroup = new SynthEBD.RelayCommand(
                canExecute: _ => true,
                execute: x => this.ForcedSubgroups.Remove((VM_Subgroup)x)
            );

            AddForcedReplacer = new SynthEBD.RelayCommand(
                canExecute: _ => true,
                execute: x => this.ForcedAssetReplacements.Add(new VM_AssetReplacementAssignment(ForcedAssetPack, ForcedAssetReplacements))
            );

            this.WhenAnyValue(x => x.ForcedAssetPack).Subscribe(x =>
            {
                foreach (var replacer in ForcedAssetReplacements)
                {
                    replacer.ParentAssetPack = ForcedAssetPack;
                }
            });
        }
        public VM_AssetPack ForcedAssetPack { get; set; }
        public ObservableCollection<VM_AssetPack> AvailableMixInAssetPacks { get; set; }
        public ObservableCollection<VM_Subgroup> ForcedSubgroups { get; set; } = new();
        public ObservableCollection<VM_Subgroup> AvailableSubgroups { get; set; } = new();
        public ObservableCollection<VM_AssetReplacementAssignment> ForcedAssetReplacements { get; set; } = new();
        public ObservableCollection<VM_MixInSpecificAssignment> ParentCollection { get; set; }
        public VM_SpecificNPCAssignment Parent { get; set; }

        public RelayCommand DeleteCommand { get; set; }
        public RelayCommand DeleteForcedSubgroup { get; set; }
        public RelayCommand AddForcedReplacer { get; set; }

        public void TriggerAvailableSubgroupsUpdate(object sender, NotifyCollectionChangedEventArgs e)
        {
            UpdateAvailableSubgroups(this);
        }

        public static VM_MixInSpecificAssignment GetViewModelFromModel(NPCAssignment.MixInAssignment model, VM_SpecificNPCAssignment parent, MainWindow_ViewModel mainVM, ObservableCollection<VM_MixInSpecificAssignment> parentCollection)
        {
            var viewModel = new VM_MixInSpecificAssignment(parent, mainVM, parentCollection);
            LinkAssetPackToForcedAssignment(model, viewModel, model.AssetPackName, mainVM.TexMeshSettingsVM.AssetPacks, parent.DispName);
            return viewModel;
        }
        public static NPCAssignment.MixInAssignment DumpViewModelToModel(VM_MixInSpecificAssignment viewModel)
        {
            NPCAssignment.MixInAssignment model = new NPCAssignment.MixInAssignment();
            model.AssetPackName = viewModel.ForcedAssetPack.GroupName;
            model.SubgroupIDs = viewModel.ForcedSubgroups.Select(subgroup => subgroup.ID).ToList();

            model.AssetReplacerAssignments.Clear();
            foreach (var replacer in viewModel.ForcedAssetReplacements)
            {
                model.AssetReplacerAssignments.Add(VM_AssetReplacementAssignment.DumpViewModelToModel(replacer));
            }
            return model;
        }
    }
}

public interface IHasForcedAssets
{
    public VM_AssetPack ForcedAssetPack { get; set; }
    ObservableCollection<VM_Subgroup> ForcedSubgroups { get; set; }
    public ObservableCollection<VM_Subgroup> AvailableSubgroups { get; set; }
}