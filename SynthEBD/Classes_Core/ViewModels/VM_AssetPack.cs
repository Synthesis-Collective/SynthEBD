
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache.Internals.Implementations;
using Mutagen.Bethesda.Skyrim;
using Noggog;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using ReactiveUI;

namespace SynthEBD
{
    public class VM_AssetPack : INotifyPropertyChanged
    {
        public VM_AssetPack(ObservableCollection<VM_AssetPack> parentCollection, VM_SettingsBodyGen bodygenSettingsVM, VM_BodyShapeDescriptorCreationMenu OBodyDescriptorMenu, VM_Settings_General generalSettingsVM, ImmutableLoadOrderLinkCache<ISkyrimMod, ISkyrimModGetter> recordTemplateLinkCache, MainWindow_ViewModel mainVM)
        {
            this.groupName = "";
            this.ShortName = "";
            this.gender = Gender.Male;
            this.displayAlerts = true;
            this.userAlert = "";
            this.subgroups = new ObservableCollection<VM_Subgroup>();

            this.RaceGroupingList = new ObservableCollection<VM_RaceGrouping>();

            this.IsSelected = true;

            this.ParentCollection = parentCollection;

            this.SourcePath = "";

            this.CurrentBodyGenSettings = bodygenSettingsVM;
            switch (this.gender)
            {
                case Gender.Female: this.AvailableBodyGenConfigs = this.CurrentBodyGenSettings.FemaleConfigs; break;
                case Gender.Male: this.AvailableBodyGenConfigs = this.CurrentBodyGenSettings.MaleConfigs; break;
            }

            this.PropertyChanged += RefreshTrackedBodyGenConfig;
            this.CurrentBodyGenSettings.PropertyChanged += RefreshTrackedBodyGenConfig;

            this.DefaultTemplateFK = new FormKey();
            this.NPCFormKeyTypes = typeof(INpcGetter).AsEnumerable();

            this.AdditionalRecordTemplateAssignments = new ObservableCollection<VM_AdditionalRecordTemplate>();
            this.RecordTemplateAdditionalRacesPaths = new ObservableCollection<VM_CollectionMemberString>();

            this.AttributeGroupMenu = new VM_AttributeGroupMenu();

            this.ReplacersMenu = new VM_AssetPackDirectReplacerMenu(this, OBodyDescriptorMenu);

            this.BodyShapeMode = generalSettingsVM.BodySelectionMode;
            generalSettingsVM.WhenAnyValue(x => x.BodySelectionMode).Subscribe(x => BodyShapeMode = x);

            RecordTemplateLinkCache = recordTemplateLinkCache;

            /*
            switch (this.gender)
            {
                case Gender.female: this.TrackedBodyGenConfig = this.CurrentBodyGenSettings.CurrentFemaleConfig; break;
                case Gender.male: this.TrackedBodyGenConfig = this.CurrentBodyGenSettings.CurrentMaleConfig; break;
            }*/

            RemoveAssetPackConfigFile = new SynthEBD.RelayCommand(
                canExecute: _ => true,
                execute: _ => { FileDialogs.ConfirmFileDeletion(this.SourcePath, "Asset Pack Config File"); this.ParentCollection.Remove(this); }
                );

            AddAdditionalRecordTemplateAssignment = new SynthEBD.RelayCommand(
                canExecute: _ => true,
                execute: _ => { this.AdditionalRecordTemplateAssignments.Add(new VM_AdditionalRecordTemplate(this.RecordTemplateLinkCache, this.AdditionalRecordTemplateAssignments)); }
                );

            AddRecordTemplateAdditionalRacesPath = new SynthEBD.RelayCommand(
                canExecute: _ => true,
                execute: _ => { this.RecordTemplateAdditionalRacesPaths.Add(new VM_CollectionMemberString("", this.RecordTemplateAdditionalRacesPaths)); }
                );

            ImportAttributeGroups = new SynthEBD.RelayCommand(
                canExecute: _ => true,
                execute: _ =>
                {
                    var alreadyContainedGroups = AttributeGroupMenu.Groups.Select(x => x.Label).ToHashSet();
                    foreach (var attGroup in generalSettingsVM.AttributeGroupMenu.Groups)
                    {
                        if (!alreadyContainedGroups.Contains(attGroup.Label))
                        {
                            AttributeGroupMenu.Groups.Add(VM_AttributeGroup.Copy(attGroup, AttributeGroupMenu));
                        }
                    }
                }
                );

            ValidateButton = new SynthEBD.RelayCommand(
                canExecute: _ => true,
                execute: _ => {
                    if (Validate(mainVM.BodyGenConfigs, out List<string> errors))
                    {
                        MessageBox.Show("No errors found.");
                    }
                    else
                    {
                        Logger.LogMessage(String.Join(Environment.NewLine, errors));
                        mainVM.DisplayedViewModel = mainVM.LogDisplayVM;
                    }
                }
                );
        }

        public string groupName { get; set; }
        public string ShortName { get; set; }
        public AssetPackType ConfigType { get; set; }
        public Gender gender { get; set; }
        public bool displayAlerts { get; set; }
        public string userAlert { get; set; }
        public ObservableCollection<VM_Subgroup> subgroups { get; set; }
        public ObservableCollection<VM_RaceGrouping> RaceGroupingList { get; set; }

        public VM_BodyGenConfig TrackedBodyGenConfig { get; set; }
        public ObservableCollection<VM_BodyGenConfig> AvailableBodyGenConfigs { get; set; }
        public VM_SettingsBodyGen CurrentBodyGenSettings { get; set; }
        public ObservableCollection<VM_CollectionMemberString> RecordTemplateAdditionalRacesPaths { get; set; }
        public bool IsSelected { get; set; }

        public string SourcePath { get; set; }

        public ImmutableLoadOrderLinkCache<ISkyrimMod, ISkyrimModGetter> RecordTemplateLinkCache { get; set; }

        public FormKey DefaultTemplateFK { get; set; }
        public VM_AttributeGroupMenu AttributeGroupMenu { get; set; }

        public IEnumerable<Type> NPCFormKeyTypes { get; set; }

        public ObservableCollection<VM_AdditionalRecordTemplate> AdditionalRecordTemplateAssignments { get; set; }
        public VM_AssetPackDirectReplacerMenu ReplacersMenu { get; set; }

        public ObservableCollection<VM_AssetPack> ParentCollection { get; set; }

        public RelayCommand RemoveAssetPackConfigFile { get; }

        public RelayCommand AddAdditionalRecordTemplateAssignment { get; }
        public RelayCommand AddRecordTemplateAdditionalRacesPath { get; }
        public RelayCommand ImportAttributeGroups { get; }

        public RelayCommand ValidateButton { get; }

        public BodyShapeSelectionMode BodyShapeMode { get; set; }

        public Dictionary<Gender, string> GenderEnumDict { get; } = new Dictionary<Gender, string>() // referenced by xaml; don't trust VS reference count
        {
            {Gender.Male, "Male"},
            {Gender.Female, "Female"},
        };

        public bool Validate(BodyGenConfigs bodyGenConfigs, out List<string> errors)
        {
            var model = DumpViewModelToViewModel(this);
            errors = new List<string>();
            return model.Validate(errors, bodyGenConfigs);
        }

        public static ObservableCollection<VM_AssetPack> GetViewModelsFromModels(List<AssetPack> assetPacks, VM_Settings_General generalSettingsVM, Settings_TexMesh texMeshSettings, VM_SettingsBodyGen bodygenSettingsVM, VM_BodyShapeDescriptorCreationMenu OBodyDescriptorMenu, ImmutableLoadOrderLinkCache<ISkyrimMod, ISkyrimModGetter> recordTemplateLinkCache, MainWindow_ViewModel mainVM)
        {
            ObservableCollection<VM_AssetPack> viewModels = new ObservableCollection<VM_AssetPack>();

            for (int i = 0; i < assetPacks.Count; i++)
            {
                var viewModel = GetViewModelFromModel(assetPacks[i], generalSettingsVM, viewModels, bodygenSettingsVM, OBodyDescriptorMenu, recordTemplateLinkCache, mainVM);
                viewModel.IsSelected = texMeshSettings.SelectedAssetPacks.Contains(assetPacks[i].GroupName);
                viewModels.Add(viewModel);
            }
            return viewModels;
        }
        public static VM_AssetPack GetViewModelFromModel(AssetPack model, VM_Settings_General generalSettingsVM, ObservableCollection<VM_AssetPack> parentCollection, VM_SettingsBodyGen bodygenSettingsVM, VM_BodyShapeDescriptorCreationMenu OBodyDescriptorMenu, ImmutableLoadOrderLinkCache<ISkyrimMod, ISkyrimModGetter> recordTemplateLinkCache, MainWindow_ViewModel mainVM)
        {
            var viewModel = new VM_AssetPack(parentCollection, bodygenSettingsVM, OBodyDescriptorMenu, generalSettingsVM, recordTemplateLinkCache, mainVM);
            viewModel.groupName = model.GroupName;
            viewModel.ShortName = model.ShortName;
            viewModel.ConfigType = model.ConfigType;
            viewModel.gender = model.Gender;
            viewModel.displayAlerts = model.DisplayAlerts;
            viewModel.userAlert = model.UserAlert;

            viewModel.RaceGroupingList = new ObservableCollection<VM_RaceGrouping>(generalSettingsVM.RaceGroupings);

            if (model.AssociatedBodyGenConfigName != "")
            {
                switch(viewModel.gender)
                {
                    case Gender.Female:
                        viewModel.TrackedBodyGenConfig = bodygenSettingsVM.FemaleConfigs.Where(x => x.Label == model.AssociatedBodyGenConfigName).FirstOrDefault();
                        break;
                    case Gender.Male:
                        viewModel.TrackedBodyGenConfig = bodygenSettingsVM.MaleConfigs.Where(x => x.Label == model.AssociatedBodyGenConfigName).FirstOrDefault();
                        break;
                }
            }
            else
            {
                viewModel.TrackedBodyGenConfig = new VM_BodyGenConfig(generalSettingsVM);
            }

            VM_AttributeGroupMenu.GetViewModelFromModels(model.AttributeGroups, viewModel.AttributeGroupMenu);

            viewModel.ReplacersMenu = VM_AssetPackDirectReplacerMenu.GetViewModelFromModels(model.ReplacerGroups, viewModel, generalSettingsVM, OBodyDescriptorMenu);

            viewModel.DefaultTemplateFK = model.DefaultRecordTemplate;
            foreach(var additionalTemplateAssignment in model.AdditionalRecordTemplateAssignments)
            {
                var assignmentVM = new VM_AdditionalRecordTemplate(recordTemplateLinkCache, viewModel.AdditionalRecordTemplateAssignments);
                assignmentVM.RaceFormKeys = new ObservableCollection<FormKey>(additionalTemplateAssignment.Races);
                assignmentVM.TemplateNPC = additionalTemplateAssignment.TemplateNPC;
                viewModel.AdditionalRecordTemplateAssignments.Add(assignmentVM);
            }

            foreach (var path in model.RecordTemplateAdditionalRacesPaths)
            {
                viewModel.RecordTemplateAdditionalRacesPaths.Add(new VM_CollectionMemberString(path, viewModel.RecordTemplateAdditionalRacesPaths));
            }

            foreach (var sg in model.Subgroups)
            {
                viewModel.subgroups.Add(VM_Subgroup.GetViewModelFromModel(sg, generalSettingsVM, viewModel.subgroups, viewModel, OBodyDescriptorMenu, false));
            }

            // go back through now that all subgroups have corresponding view models, and link the required and excluded subgroups
            ObservableCollection<VM_Subgroup> flattenedSubgroupList = FlattenSubgroupVMs(viewModel.subgroups, new ObservableCollection<VM_Subgroup>());
            LinkRequiredSubgroups(flattenedSubgroupList);
            LinkExcludedSubgroups(flattenedSubgroupList);

            viewModel.SourcePath = model.FilePath;

            return viewModel;
        }

        public static void DumpViewModelsToModels(ObservableCollection<VM_AssetPack> viewModels, List<AssetPack> models)
        {
            models.Clear();

            foreach (var vm in viewModels)
            {
                models.Add(DumpViewModelToViewModel(vm));
            }
        }

        public static AssetPack DumpViewModelToViewModel(VM_AssetPack viewModel)
        {
            AssetPack model = new AssetPack();
            model.GroupName = viewModel.groupName;
            model.ShortName = viewModel.ShortName;
            model.ConfigType = viewModel.ConfigType;
            model.Gender = viewModel.gender;
            model.DisplayAlerts = viewModel.displayAlerts;
            model.UserAlert = viewModel.userAlert;

            if (viewModel.TrackedBodyGenConfig != null)
            {
                model.AssociatedBodyGenConfigName = viewModel.TrackedBodyGenConfig.Label;
            }

            model.DefaultRecordTemplate = viewModel.DefaultTemplateFK;
            model.AdditionalRecordTemplateAssignments = viewModel.AdditionalRecordTemplateAssignments.Select(x => VM_AdditionalRecordTemplate.DumpViewModelToModel(x)).ToHashSet();
            model.RecordTemplateAdditionalRacesPaths = viewModel.RecordTemplateAdditionalRacesPaths.Select(x => x.Content).ToHashSet();

            VM_AttributeGroupMenu.DumpViewModelToModels(viewModel.AttributeGroupMenu, model.AttributeGroups);

            foreach (var svm in viewModel.subgroups)
            {
                model.Subgroups.Add(VM_Subgroup.DumpViewModelToModel(svm));
            }

            model.ReplacerGroups = VM_AssetPackDirectReplacerMenu.DumpViewModelToModels(viewModel.ReplacersMenu);

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
            MessageBoxResult result = MessageBox.Show("Are you sure you want to permanently delete this config file?", "", MessageBoxButton.YesNo);
            switch (result)
            {
                case MessageBoxResult.Yes:
                    if (File.Exists(this.SourcePath))
                    {
                        try
                        {
                            File.Delete(this.SourcePath);
                        }
                        catch
                        {
                            //Warn User
                        }
                    }
                    
                    break;
                case MessageBoxResult.No:
                    break;
            }
        }

        public void RefreshTrackedBodyGenConfig(object sender, PropertyChangedEventArgs e)
        {
            switch (this.gender)
            {
                case Gender.Female: this.AvailableBodyGenConfigs = this.CurrentBodyGenSettings.FemaleConfigs; break;
                case Gender.Male: this.AvailableBodyGenConfigs = this.CurrentBodyGenSettings.MaleConfigs; break;
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
    }
    
}
