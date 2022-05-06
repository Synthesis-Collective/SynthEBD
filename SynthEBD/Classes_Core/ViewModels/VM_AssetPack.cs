
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Skyrim;
using Noggog;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows;
using ReactiveUI;
using System.Windows.Media;
using GongSolutions.Wpf.DragDrop;
using System.Windows.Controls;
using Noggog.WPF;

namespace SynthEBD;

public class VM_AssetPack : ViewModel, IHasAttributeGroupMenu, IDropTarget, IHasSubgroupViewModels
{
    public VM_AssetPack(MainWindow_ViewModel mainVM)
    {
        this.ParentCollection = mainVM.TexMeshSettingsVM.AssetPacks;

        this.CurrentBodyGenSettings = mainVM.BodyGenSettingsVM;
        switch (this.Gender)
        {
            case Gender.Female: this.AvailableBodyGenConfigs = this.CurrentBodyGenSettings.FemaleConfigs; break;
            case Gender.Male: this.AvailableBodyGenConfigs = this.CurrentBodyGenSettings.MaleConfigs; break;
        }

        this.PropertyChanged += RefreshTrackedBodyGenConfig;
        this.CurrentBodyGenSettings.PropertyChanged += RefreshTrackedBodyGenConfig;

        this.AttributeGroupMenu = new VM_AttributeGroupMenu(mainVM.GeneralSettingsVM.AttributeGroupMenu, true);

        this.ReplacersMenu = new VM_AssetPackDirectReplacerMenu(this, mainVM.OBodySettingsVM.DescriptorUI);

        this.DistributionRules = new VM_ConfigDistributionRules(mainVM.GeneralSettingsVM.RaceGroupings, this, mainVM.OBodySettingsVM.DescriptorUI);

        this.BodyShapeMode = mainVM.GeneralSettingsVM.BodySelectionMode;
        mainVM.GeneralSettingsVM.WhenAnyValue(x => x.BodySelectionMode).Subscribe(x => BodyShapeMode = x);

        RecordTemplateLinkCache = mainVM.RecordTemplateLinkCache;

        ParentMenuVM = mainVM.TexMeshSettingsVM;

        this.WhenAnyValue(x => x.DisplayedSubgroup).Subscribe(x => UpdatePreviewImages());

        this.WhenAnyValue(x => x.Gender).Subscribe(x => SetDefaultRecordTemplate());

        AddSubgroup = new SynthEBD.RelayCommand(
            canExecute: _ => true,
            execute: _ => { Subgroups.Add(new VM_Subgroup(mainVM.GeneralSettingsVM.RaceGroupings, Subgroups, this, mainVM.OBodySettingsVM.DescriptorUI, false)); }
        );

        RemoveAssetPackConfigFile = new SynthEBD.RelayCommand(
            canExecute: _ => true,
            execute: _ => {
                if (FileDialogs.ConfirmFileDeletion(this.SourcePath, "Asset Pack Config File"))
                {
                    ParentCollection.Remove(this);
                }
            }
        );

        AddAdditionalRecordTemplateAssignment = new SynthEBD.RelayCommand(
            canExecute: _ => true,
            execute: _ => { this.AdditionalRecordTemplateAssignments.Add(new VM_AdditionalRecordTemplate(this.RecordTemplateLinkCache, this.AdditionalRecordTemplateAssignments)); }
        );

        AddRecordTemplateAdditionalRacesPath = new SynthEBD.RelayCommand(
            canExecute: _ => true,
            execute: _ => { this.DefaultRecordTemplateAdditionalRacesPaths.Add(new VM_CollectionMemberString("", this.DefaultRecordTemplateAdditionalRacesPaths)); }
        );

        MergeWithAssetPack = new SynthEBD.RelayCommand(
            canExecute: _ => true,
            execute: _ => { MergeInAssetPack(mainVM); }
        );

        ValidateButton = new SynthEBD.RelayCommand(
            canExecute: _ => true,
            execute: _ => {
                if (Validate(mainVM.BodyGenConfigs, out List<string> errors))
                {
                    CustomMessageBox.DisplayNotificationOK("Validation", "No errors found.");
                }
                else
                {
                    Logger.LogMessage(String.Join(Environment.NewLine, errors));
                    mainVM.DisplayedViewModel = mainVM.LogDisplayVM;
                }
            }
        );

        SaveButton = new SynthEBD.RelayCommand(
            canExecute: _ => true,
            execute: _ => {
                SettingsIO_AssetPack.SaveAssetPack(DumpViewModelToModel(this), out bool success);
                if (success)
                {
                    Logger.CallTimedNotifyStatusUpdateAsync(GroupName + " Saved.", 2, new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.Yellow));
                }
                else
                {
                    Logger.CallTimedLogErrorWithStatusUpdateAsync(GroupName + " could not be saved.", ErrorType.Error, 3);
                }
            }
        );

        DiscardButton = new SynthEBD.RelayCommand(
            canExecute: _ => true,
            execute: _ => {
                var reloaded = SettingsIO_AssetPack.LoadAssetPack(SourcePath, PatcherSettings.General.RaceGroupings, mainVM.RecordTemplatePlugins, mainVM.BodyGenConfigs, out bool success);
                if (!success)
                {
                    Logger.CallTimedLogErrorWithStatusUpdateAsync(GroupName + " could not be reloaded from drive.", ErrorType.Error, 3);
                }
                var reloadedVM = VM_AssetPack.GetViewModelFromModel(reloaded, mainVM);
                this.IsSelected = reloadedVM.IsSelected;
                this.AttributeGroupMenu = reloadedVM.AttributeGroupMenu;
                this.AvailableBodyGenConfigs = reloadedVM.AvailableBodyGenConfigs;
                this.ConfigType = reloadedVM.ConfigType;
                this.CurrentBodyGenSettings = reloadedVM.CurrentBodyGenSettings;
                this.DefaultTemplateFK = reloadedVM.DefaultTemplateFK;
                this.DefaultRecordTemplateAdditionalRacesPaths = reloadedVM.DefaultRecordTemplateAdditionalRacesPaths;
                this.DistributionRules = reloadedVM.DistributionRules;
                this.Gender = reloadedVM.Gender;
                this.GroupName = reloadedVM.GroupName;
                this.ReplacersMenu = reloadedVM.ReplacersMenu;
                this.ShortName = reloadedVM.ShortName;
                this.SourcePath = reloadedVM.SourcePath;
                this.Subgroups = reloadedVM.Subgroups;
                this.TrackedBodyGenConfig = reloadedVM.TrackedBodyGenConfig;
                Logger.CallTimedNotifyStatusUpdateAsync("Discarded Changes", 2, new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.Yellow));
            }
        );

        SelectedSubgroupChanged = new SynthEBD.RelayCommand(
            canExecute: _ => true,
            execute: x => this.DisplayedSubgroup = (VM_Subgroup)x
        );
    }

    public string GroupName { get; set; } = "";
    public string ShortName { get; set; } = "";
    public AssetPackType ConfigType { get; set; }
    public Gender Gender { get; set; } = Gender.Male;
    public bool DisplayAlerts { get; set; } = true;
    public string UserAlert { get; set; } = "";
    public ObservableCollection<VM_Subgroup> Subgroups { get; set; } = new();
    public ObservableCollection<VM_RaceGrouping> RaceGroupingList { get; set; } = new();

    public VM_BodyGenConfig TrackedBodyGenConfig { get; set; }
    public ObservableCollection<VM_BodyGenConfig> AvailableBodyGenConfigs { get; set; }
    public VM_SettingsBodyGen CurrentBodyGenSettings { get; set; }
    public ObservableCollection<VM_CollectionMemberString> DefaultRecordTemplateAdditionalRacesPaths { get; set; } = new();
    public bool IsSelected { get; set; } = true;

    public string SourcePath { get; set; } = "";

    public ILinkCache<ISkyrimMod, ISkyrimModGetter> RecordTemplateLinkCache { get; set; }

    public FormKey DefaultTemplateFK { get; set; } = new();
    public VM_AttributeGroupMenu AttributeGroupMenu { get; set; }

    public IEnumerable<Type> NPCFormKeyTypes { get; set; } = typeof(INpcGetter).AsEnumerable();

    public ObservableCollection<VM_AdditionalRecordTemplate> AdditionalRecordTemplateAssignments { get; set; } = new();
    public VM_AssetPackDirectReplacerMenu ReplacersMenu { get; set; }
    public VM_ConfigDistributionRules DistributionRules { get; set; }

    public ObservableCollection<VM_AssetPack> ParentCollection { get; set; }

    public VM_Subgroup DisplayedSubgroup { get; set; }

    public RelayCommand RemoveAssetPackConfigFile { get; }
    public RelayCommand AddSubgroup { get; }
    public RelayCommand AddAdditionalRecordTemplateAssignment { get; }
    public RelayCommand AddRecordTemplateAdditionalRacesPath { get; }
    public RelayCommand MergeWithAssetPack { get; }
    public RelayCommand ValidateButton { get; }
    public RelayCommand SaveButton { get; }
    public RelayCommand DiscardButton { get; }
    public RelayCommand SelectedSubgroupChanged { get; }
    public BodyShapeSelectionMode BodyShapeMode { get; set; }
    public bool ShowPreviewImages { get; set; }
    public ObservableCollection<VM_PreviewImage> PreviewImages { get; set; } = new();

    public VM_SettingsTexMesh ParentMenuVM { get; set; }
    public Dictionary<Gender, string> GenderEnumDict { get; } = new Dictionary<Gender, string>() // referenced by xaml; don't trust VS reference count
    {
        {Gender.Male, "Male"},
        {Gender.Female, "Female"},
    };

    public bool Validate(BodyGenConfigs bodyGenConfigs, out List<string> errors)
    {
        var model = DumpViewModelToModel(this);
        errors = new List<string>();
        return model.Validate(errors, bodyGenConfigs);
    }

    public async void UpdatePreviewImages()
    {
        //this.PreviewImages.Clear();
        this.PreviewImages = new ObservableCollection<VM_PreviewImage>();
        if (this.DisplayedSubgroup == null) { return; }
        foreach (var sourcedFile in this.DisplayedSubgroup.ImagePaths)
        {
            Pfim.IImage image = await Task.Run(() => Pfim.Pfim.FromFile(sourcedFile.Path));
            if (image != null)
            {
                var converted = Graphics.WpfImage(image).FirstOrDefault();
                if (converted != null)
                {
                    converted.Stretch = Stretch.Uniform;
                    converted.StretchDirection = System.Windows.Controls.StretchDirection.DownOnly;
                    PreviewImages.Add(new VM_PreviewImage(converted, sourcedFile.Source));
                }
            }    
        }
        return;
    }

    public static void GetViewModelsFromModels(List<AssetPack> assetPacks, Settings_TexMesh texMeshSettings, MainWindow_ViewModel mainVM)
    {
        mainVM.TexMeshSettingsVM.AssetPacks.Clear();
        for (int i = 0; i < assetPacks.Count; i++)
        {
            var viewModel = GetViewModelFromModel(assetPacks[i], mainVM);
            viewModel.IsSelected = texMeshSettings.SelectedAssetPacks.Contains(assetPacks[i].GroupName);
            mainVM.TexMeshSettingsVM.AssetPacks.Add(viewModel);
        }
    }
    public static VM_AssetPack GetViewModelFromModel(AssetPack model, MainWindow_ViewModel mainVM)
    {
        var viewModel = new VM_AssetPack(mainVM);
        viewModel.GroupName = model.GroupName;
        viewModel.ShortName = model.ShortName;
        viewModel.ConfigType = model.ConfigType;
        viewModel.Gender = model.Gender;
        viewModel.DisplayAlerts = model.DisplayAlerts;
        viewModel.UserAlert = model.UserAlert;

        viewModel.RaceGroupingList = new ObservableCollection<VM_RaceGrouping>(mainVM.GeneralSettingsVM.RaceGroupings);

        if (model.AssociatedBodyGenConfigName != "")
        {
            switch(viewModel.Gender)
            {
                case Gender.Female:
                    viewModel.TrackedBodyGenConfig = mainVM.BodyGenSettingsVM.FemaleConfigs.Where(x => x.Label == model.AssociatedBodyGenConfigName).FirstOrDefault();
                    break;
                case Gender.Male:
                    viewModel.TrackedBodyGenConfig = mainVM.BodyGenSettingsVM.MaleConfigs.Where(x => x.Label == model.AssociatedBodyGenConfigName).FirstOrDefault();
                    break;
            }
        }
        else
        {
            viewModel.TrackedBodyGenConfig = new VM_BodyGenConfig(mainVM.GeneralSettingsVM, new ObservableCollection<VM_BodyGenConfig>(), mainVM.BodyGenSettingsVM);
        }

        VM_AttributeGroupMenu.GetViewModelFromModels(model.AttributeGroups, viewModel.AttributeGroupMenu);

        viewModel.ReplacersMenu = VM_AssetPackDirectReplacerMenu.GetViewModelFromModels(model.ReplacerGroups, viewModel, mainVM.GeneralSettingsVM, mainVM.OBodySettingsVM.DescriptorUI);

        viewModel.DefaultTemplateFK = model.DefaultRecordTemplate;
        foreach(var additionalTemplateAssignment in model.AdditionalRecordTemplateAssignments)
        {
            var assignmentVM = new VM_AdditionalRecordTemplate(mainVM.RecordTemplateLinkCache, viewModel.AdditionalRecordTemplateAssignments);
            assignmentVM.RaceFormKeys = new ObservableCollection<FormKey>(additionalTemplateAssignment.Races);
            assignmentVM.TemplateNPC = additionalTemplateAssignment.TemplateNPC;
            assignmentVM.AdditionalRacesPaths = VM_CollectionMemberString.InitializeCollectionFromHashSet(additionalTemplateAssignment.AdditionalRacesPaths);
            viewModel.AdditionalRecordTemplateAssignments.Add(assignmentVM);
        }

        foreach (var path in model.DefaultRecordTemplateAdditionalRacesPaths)
        {
            viewModel.DefaultRecordTemplateAdditionalRacesPaths.Add(new VM_CollectionMemberString(path, viewModel.DefaultRecordTemplateAdditionalRacesPaths));
        }

        foreach (var sg in model.Subgroups)
        {
            viewModel.Subgroups.Add(VM_Subgroup.GetViewModelFromModel(sg, mainVM.GeneralSettingsVM, viewModel.Subgroups, viewModel, mainVM.OBodySettingsVM.DescriptorUI, false));
        }

        // go back through now that all subgroups have corresponding view models, and link the required and excluded subgroups
        ObservableCollection<VM_Subgroup> flattenedSubgroupList = FlattenSubgroupVMs(viewModel.Subgroups, new ObservableCollection<VM_Subgroup>());
        LinkRequiredSubgroups(flattenedSubgroupList);
        LinkExcludedSubgroups(flattenedSubgroupList);

        viewModel.DistributionRules = VM_ConfigDistributionRules.GetViewModelFromModel(model.DistributionRules, mainVM.GeneralSettingsVM.RaceGroupings, viewModel, mainVM.OBodySettingsVM.DescriptorUI);

        viewModel.SourcePath = model.FilePath;

        return viewModel;
    }

    public static void DumpViewModelsToModels(ObservableCollection<VM_AssetPack> viewModels, List<AssetPack> models)
    {
        models.Clear();

        foreach (var vm in viewModels)
        {
            models.Add(DumpViewModelToModel(vm));
        }
    }

    public static AssetPack DumpViewModelToModel(VM_AssetPack viewModel)
    {
        AssetPack model = new AssetPack();
        model.GroupName = viewModel.GroupName;
        model.ShortName = viewModel.ShortName;
        model.ConfigType = viewModel.ConfigType;
        model.Gender = viewModel.Gender;
        model.DisplayAlerts = viewModel.DisplayAlerts;
        model.UserAlert = viewModel.UserAlert;

        if (viewModel.TrackedBodyGenConfig != null)
        {
            model.AssociatedBodyGenConfigName = viewModel.TrackedBodyGenConfig.Label;
        }

        model.DefaultRecordTemplate = viewModel.DefaultTemplateFK;
        model.AdditionalRecordTemplateAssignments = viewModel.AdditionalRecordTemplateAssignments.Select(x => VM_AdditionalRecordTemplate.DumpViewModelToModel(x)).ToHashSet();
        model.DefaultRecordTemplateAdditionalRacesPaths = viewModel.DefaultRecordTemplateAdditionalRacesPaths.Select(x => x.Content).ToHashSet();

        VM_AttributeGroupMenu.DumpViewModelToModels(viewModel.AttributeGroupMenu, model.AttributeGroups);

        foreach (var svm in viewModel.Subgroups)
        {
            model.Subgroups.Add(VM_Subgroup.DumpViewModelToModel(svm));
        }

        model.ReplacerGroups = VM_AssetPackDirectReplacerMenu.DumpViewModelToModels(viewModel.ReplacersMenu);

        model.DistributionRules = VM_ConfigDistributionRules.DumpViewModelToModel(viewModel.DistributionRules);

        model.FilePath = viewModel.SourcePath;

        return model;
    }

    public static ObservableCollection<VM_Subgroup> FlattenSubgroupVMs(ObservableCollection<VM_Subgroup> currentLevelSGs, ObservableCollection<VM_Subgroup> flattened)
    {
        foreach(var sg in currentLevelSGs)
        {
            flattened.Add(sg);
            FlattenSubgroupVMs(sg.Subgroups, flattened);
        }
        return flattened;
    }

    public static void LinkRequiredSubgroups(ObservableCollection<VM_Subgroup> flattenedSubgroups)
    {
        foreach (var sg in flattenedSubgroups)
        {
            foreach (string id in sg.RequiredSubgroupIDs)
            {
                foreach (var candidate in flattenedSubgroups)
                {
                    if (candidate.ID == id)
                    {
                        sg.RequiredSubgroups.Add(candidate);
                        break;
                    }
                }
            }
        }
    }

    public static void LinkExcludedSubgroups(ObservableCollection<VM_Subgroup> flattenedSubgroups)
    {
        foreach (var sg in flattenedSubgroups)
        {
            foreach (string id in sg.ExcludedSubgroupIDs)
            {
                foreach (var candidate in flattenedSubgroups)
                {
                    if (candidate.ID == id)
                    {
                        sg.ExcludedSubgroups.Add(candidate);
                        break;
                    }
                }
            }
        }
    }


    public void RemoveAssetPackDialog()
    {
        bool result = CustomMessageBox.DisplayNotificationYesNo("Confirm Deletion", "Are you sure you want to permanently delete this config file?");
            
        switch (result)
        {
            case true:
                if (File.Exists(this.SourcePath))
                {
                    try
                    {
                        File.Delete(this.SourcePath);
                    }
                    catch
                    {
                        Logger.LogError("Could not delete file at " + this.SourcePath);
                        Logger.CallTimedLogErrorWithStatusUpdateAsync("Could not delete Asset Pack Config File", ErrorType.Error, 5);
                    }
                }
                    
                break;
            case false:
                break;
        }
    }

    public void RefreshTrackedBodyGenConfig(object sender, PropertyChangedEventArgs e)
    {
        switch (this.Gender)
        {
            case Gender.Female: this.AvailableBodyGenConfigs = this.CurrentBodyGenSettings.FemaleConfigs; break;
            case Gender.Male: this.AvailableBodyGenConfigs = this.CurrentBodyGenSettings.MaleConfigs; break;
        }
    }

    public void MergeInAssetPack(MainWindow_ViewModel mainVM)
    {
        List<string> newSubgroupNames = new List<string>();

        if (IO_Aux.SelectFile(PatcherSettings.Paths.AssetPackDirPath, "Config files (*.json)|*.json", "Select config file to merge in", out string path))
        {
            var newAssetPack = SettingsIO_AssetPack.LoadAssetPack(path, PatcherSettings.General.RaceGroupings, mainVM.RecordTemplatePlugins, mainVM.BodyGenConfigs, out bool loadSuccess);
            if (loadSuccess)
            {
                var newAssetPackVM = VM_AssetPack.GetViewModelFromModel(newAssetPack, mainVM);
                    
                // first add completely new top-level subgroups if necessary
                foreach (var subgroup in newAssetPackVM.Subgroups)
                {
                    if (!this.Subgroups.Select(x => x .ID).Contains(subgroup.ID, StringComparer.OrdinalIgnoreCase))
                    {
                        var clone = subgroup.Clone() as VM_Subgroup;
                        clone.ParentAssetPack = this;
                        clone.ParentCollection = this.Subgroups;
                        this.Subgroups.Add(clone);
                        newSubgroupNames.Add(clone.ID + ": " + clone.Name);
                    }
                }

                // merge existing subgroups
                foreach (var subgroup in this.Subgroups)
                {
                    var matchedSubgroup = newAssetPackVM.Subgroups.Where(x => x.ID == subgroup.ID).FirstOrDefault();
                    if (matchedSubgroup != null)
                    {
                        MergeSubgroupLists(subgroup.Subgroups, matchedSubgroup.Subgroups, this, newSubgroupNames);
                    }
                }

                if (newSubgroupNames.Any())
                {
                    CustomMessageBox.DisplayNotificationOK("Config Merger", "The following subgroups were imported:" + Environment.NewLine + string.Join(Environment.NewLine, newSubgroupNames));
                }
            }
            else
            {
                CustomMessageBox.DisplayNotificationOK("Config Merger", "That file could not be parsed as a valid Asset Config Plugin File.");
            }
        }
    }

    public static void MergeSubgroupLists(ObservableCollection<VM_Subgroup> ListA, ObservableCollection<VM_Subgroup> ListB, VM_AssetPack parentAssetPack, List<string> newSubgroupNames)
    {
        foreach (VM_Subgroup candidateSubgroup in ListB)
        {
            var matchedSubgroup = ListA.Where(x => x.ID == candidateSubgroup.ID).FirstOrDefault();
            if (matchedSubgroup is null)
            {
                var clone = candidateSubgroup.Clone() as VM_Subgroup;
                clone.ParentAssetPack = parentAssetPack;
                clone.ParentCollection = ListA;
                ListA.Add(clone);
                newSubgroupNames.Add(clone.ID + ": " + clone.Name);
            }
            else
            {
                MergeSubgroupLists(matchedSubgroup.Subgroups, candidateSubgroup.Subgroups, parentAssetPack, newSubgroupNames);
            }
        }
    }

    public void SetDefaultRecordTemplate()
    {
        switch(Gender)
        {
            case Gender.Male:
                if (RecordTemplateLinkCache.TryResolve("DefaultMale", out var defaultMaleRec))
                {
                    DefaultTemplateFK = defaultMaleRec.FormKey;
                }
                break;
            case Gender.Female:
                if (RecordTemplateLinkCache.TryResolve("DefaultFemale", out var defaultFemaleRec))
                {
                    DefaultTemplateFK = defaultFemaleRec.FormKey;
                }
                break;
        }
            
    }

    public void DragOver(IDropInfo dropInfo)
    {
        if (dropInfo.Data is VM_Subgroup)
        {
            dropInfo.DropTargetAdorner = DropTargetAdorners.Highlight;
            dropInfo.Effects = DragDropEffects.Move;
            if (dropInfo.KeyStates.HasFlag(DragDropKeyStates.RightMouseButton))
            {
                DropInitiatedRightClick = true;
            }
        }
    }

    public void Drop(IDropInfo dropInfo)
    {
        if (dropInfo.Data is VM_Subgroup)
        {
            var draggedSubgroup = (VM_Subgroup)dropInfo.Data;
            if (dropInfo.TargetItem is VM_Subgroup)
            {
                VM_Subgroup dropTarget = (VM_Subgroup)dropInfo.TargetItem;

                if (dropTarget.Name == draggedSubgroup.Name && dropTarget.ID == draggedSubgroup.ID) { return; }

                var clone = (VM_Subgroup)draggedSubgroup.Clone(dropTarget.Subgroups);
                clone.ParentCollection = dropTarget.Subgroups;
                clone.ParentAssetPack = dropTarget.ParentAssetPack;
                dropTarget.Subgroups.Add(clone);
            }
            else if (dropInfo.VisualTarget is TreeView)
            {
                var targetTV = (TreeView)dropInfo.VisualTarget;
                var dropTarget = (VM_AssetPack)targetTV.DataContext;
                if (targetTV.Name == "TVsubgroups" && dropTarget != null)
                {
                    var clone = (VM_Subgroup)draggedSubgroup.Clone(dropTarget.Subgroups);
                    clone.ParentCollection = dropTarget.Subgroups;
                    clone.ParentAssetPack = dropTarget;
                    dropTarget.Subgroups.Add(clone);
                }
            }

            if (!DropInitiatedRightClick)
            {
                draggedSubgroup.ParentCollection.Remove(draggedSubgroup);
            }
        }

        DropInitiatedRightClick = false;
    }

    public bool DropInitiatedRightClick { get; set; }
}

public interface IHasSubgroupViewModels
{
    ObservableCollection<VM_Subgroup> Subgroups { get; }
}