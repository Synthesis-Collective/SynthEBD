using Mutagen.Bethesda;
using Mutagen.Bethesda.Environments;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Skyrim;
using Noggog;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ReactiveUI;
using Mutagen.Bethesda.Plugins.Cache.Internals.Implementations;

namespace SynthEBD
{
    public class VM_SpecificNPCAssignment : INotifyPropertyChanged, IHasForcedAssets
    {
        public VM_SpecificNPCAssignment(ObservableCollection<VM_AssetPack> assetPacks, VM_SettingsBodyGen bodyGenSettings, VM_SettingsOBody oBodySettings, VM_Settings_General generalSettingsVM)
        {
            this.PropertyChanged += TriggerDispNameUpdate;
            
            this.DispName = "New Assignment";
            this.NPCFormKey = new FormKey();
            this.ForcedAssetPack = new VM_AssetPack(assetPacks, bodyGenSettings, oBodySettings.DescriptorUI, generalSettingsVM, null, null);
            this.ForcedSubgroups = new ObservableCollection<VM_Subgroup>();
            this.ForcedMixIns = new ObservableCollection<VM_MixInSpecificAssignment>();
            this.ForcedAssetReplacements = new ObservableCollection<VM_AssetReplacementAssignment>();
            this.ForcedHeight = "";
            this.ForcedBodyGenMorphs = new ObservableCollection<VM_BodyGenTemplate>();
            this.ForcedBodySlide = "";

            this.Gender = Gender.Female;
            this.AvailableAssetPacks = new ObservableCollection<VM_AssetPack>(); // filtered by gender
            this.AvailableMixInAssetPacks = new ObservableCollection<VM_AssetPack>(); // filtered by gender
            this.SubscribedAssetPacks = assetPacks;

            this.AvailableSubgroups = new ObservableCollection<VM_Subgroup>();

            this.SubscribedBodyGenSettings = bodyGenSettings;
            this.AvailableMorphs = new ObservableCollection<VM_BodyGenTemplate>(); // filtered by gender

            this.lk = GameEnvironmentProvider.MyEnvironment.LinkCache;
            this.NPCFormKeyTypes = typeof(INpcGetter).AsEnumerable();

            this.PropertyChanged += TriggerGenderUpdate;
            this.PropertyChanged += TriggerAvailableAssetPackUpdate;
            this.PropertyChanged += TriggerAvailableSubgroupsUpdate;
            this.PropertyChanged += TriggerAvailableMorphsUpdate;
            
            this.SubscribedAssetPacks.CollectionChanged += TriggerAvailableAssetPackUpdate;
            
            this.ForcedAssetPack.PropertyChanged += TriggerAvailableSubgroupsUpdate;
            this.ForcedSubgroups.CollectionChanged += TriggerAvailableSubgroupsUpdate;
            
            this.ForcedBodyGenMorphs.CollectionChanged += TriggerAvailableMorphsUpdate;
            this.SubscribedBodyGenSettings.PropertyChanged += TriggerAvailableMorphsUpdate;
            this.SubscribedBodyGenSettings.MaleConfigs.CollectionChanged += TriggerAvailableMorphsUpdate;
            this.SubscribedBodyGenSettings.FemaleConfigs.CollectionChanged += TriggerAvailableMorphsUpdate;
            this.SubscribedBodyGenSettings.CurrentMaleConfig.PropertyChanged += TriggerAvailableMorphsUpdate;
            this.SubscribedBodyGenSettings.CurrentFemaleConfig.PropertyChanged += TriggerAvailableMorphsUpdate;

            UpdateAvailableAssetPacks(this);
            UpdateAvailableBodySlides(oBodySettings, generalSettingsVM);

            this.WhenAnyValue(x => x.NPCFormKey).Subscribe(x => UpdateAvailableBodySlides(oBodySettings, generalSettingsVM));
            oBodySettings.BodySlidesUI.WhenAnyValue(x => x.BodySlidesFemale).Subscribe(x => UpdateAvailableBodySlides(oBodySettings, generalSettingsVM));
            oBodySettings.BodySlidesUI.WhenAnyValue(x => x.BodySlidesMale).Subscribe(x => UpdateAvailableBodySlides(oBodySettings, generalSettingsVM));

            this.WhenAnyValue(x => x.ForcedAssetPack).Subscribe(x =>
            {
                foreach (var replacer in ForcedAssetReplacements)
                {
                    replacer.ParentAssetPack = ForcedAssetPack;
                }
            });

            SubscribedGeneralSettings = generalSettingsVM;

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
                execute: x => this.ForcedMixIns.Add(new VM_MixInSpecificAssignment(this, assetPacks, bodyGenSettings, oBodySettings, generalSettingsVM, ForcedMixIns))
                );

            AddForcedReplacer = new SynthEBD.RelayCommand(
                canExecute: _ => true,
                execute: x => this.ForcedAssetReplacements.Add(new VM_AssetReplacementAssignment(ForcedAssetPack, ForcedAssetReplacements))
                );
        }

        // Caption
        public string DispName { get; set; }

        //User-editable
        public FormKey NPCFormKey { get; set; }
        public VM_AssetPack ForcedAssetPack { get; set; }
        public ObservableCollection<VM_Subgroup> ForcedSubgroups { get; set; }
        public ObservableCollection<VM_MixInSpecificAssignment> ForcedMixIns { get; set; }
        public ObservableCollection<VM_AssetReplacementAssignment> ForcedAssetReplacements { get; set; }
        public string ForcedHeight { get; set; }
        public ObservableCollection<VM_BodyGenTemplate> ForcedBodyGenMorphs { get; set; }
        public string ForcedBodySlide { get; set; }

        //Needed by UI
        public ObservableCollection<VM_AssetPack> AvailableAssetPacks { get; set; }
        public ObservableCollection<VM_AssetPack> SubscribedAssetPacks { get; set; }

        public ObservableCollection<VM_Subgroup> AvailableSubgroups { get; set; }

        public ObservableCollection<VM_AssetPack> AvailableMixInAssetPacks { get; set; }
        public ObservableCollection<VM_BodyGenTemplate> AvailableMorphs { get; set; }
        public VM_SettingsBodyGen SubscribedBodyGenSettings { get; set; }
        public ObservableCollection<VM_BodySlideSetting> SubscribedBodySlides { get; set; }
        public ObservableCollection<VM_BodySlideSetting> AvailableBodySlides { get; set; }
        public VM_BodyGenTemplate SelectedTemplate { get; set; }

        public Gender Gender;

        public VM_Settings_General SubscribedGeneralSettings { get; set; }

        public ILinkCache lk { get; set; }
        public IEnumerable<Type> NPCFormKeyTypes { get; set; }

        public RelayCommand DeleteForcedSubgroup { get; set; }

        public RelayCommand DeleteForcedMorph { get; set; }
        public RelayCommand AddForcedMixIn { get; set; }
        public RelayCommand AddForcedReplacer { get; set; }

        public event PropertyChangedEventHandler PropertyChanged;

        public static VM_SpecificNPCAssignment GetViewModelFromModel(NPCAssignment model, ObservableCollection<VM_AssetPack> assetPacks, VM_SettingsBodyGen BGVM, VM_SettingsOBody oBodySettings, VM_Settings_General generalSettingsVM)
        {
            var viewModel = new VM_SpecificNPCAssignment(assetPacks, BGVM, oBodySettings, generalSettingsVM);
            viewModel.NPCFormKey = model.NPCFormKey;

            if (viewModel.NPCFormKey.IsNull)
            {
                // Warn User
                return null;
            }

            var npcFormLink = new FormLink<INpcGetter>(viewModel.NPCFormKey);

            if (!npcFormLink.TryResolve(GameEnvironmentProvider.MyEnvironment.LinkCache, out var npcRecord))
            {
                // Warn User
                return null;
            }

            viewModel.Gender = getGender(viewModel.NPCFormKey);

            bool assetPackFound = false;
            if (model.AssetPackName.Length == 0) { assetPackFound = true; }
            else
            {
                foreach (var ap in assetPacks)
                {
                    if (ap.groupName == model.AssetPackName)
                    {
                        viewModel.ForcedAssetPack = ap;
                        assetPackFound = true;

                        foreach (var id in model.SubgroupIDs)
                        {
                            var foundSubgroup = GetSubgroupByID(ap.subgroups, id);
                            if (foundSubgroup != null)
                            {
                                viewModel.ForcedSubgroups.Add(foundSubgroup);
                                continue;
                            }
                            else
                            {
                                // Warn User
                            }
                        }

                        foreach (var replacer in model.AssetReplacerAssignments)
                        {
                            viewModel.ForcedAssetReplacements.Add(VM_AssetReplacementAssignment.GetViewModelFromModel(replacer, ap, viewModel.ForcedAssetReplacements));
                        }
                    }
                }
                if (assetPackFound == false)
                {
                    // Warn user
                }
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
                    templates = BGVM.CurrentMaleConfig.TemplateMorphUI.Templates;
                    break;
                case Gender.Female:
                    templates = BGVM.CurrentFemaleConfig.TemplateMorphUI.Templates;
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
                    // Warn User
                }
            }

            viewModel.ForcedBodySlide = model.BodySlidePreset;

            viewModel.DispName = Converters.CreateNPCDispNameFromFormKey(viewModel.NPCFormKey);

            return viewModel;
        }

        public static NPCAssignment DumpViewModelToModel(VM_SpecificNPCAssignment viewModel)
        {
            NPCAssignment model = new NPCAssignment();
            model.DispName = viewModel.DispName;
            model.AssetPackName = viewModel.ForcedAssetPack.groupName;
            model.SubgroupIDs = viewModel.ForcedSubgroups.Select(subgroup => subgroup.ID).ToList();

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

        public static void UpdateAvailableAssetPacks(VM_SpecificNPCAssignment assignment)
        {
            for (int i = 0; i < assignment.AvailableAssetPacks.Count; i++)
            {
                var ap = assignment.AvailableAssetPacks[i];
                if (assignment.ForcedAssetPack != ap)
                {
                    assignment.AvailableAssetPacks.Remove(ap);
                }
            }

            assignment.AvailableMixInAssetPacks.Clear();

            assignment.AvailableAssetPacks.Insert(0, new VM_AssetPack(new ObservableCollection<VM_AssetPack>(), new VM_SettingsBodyGen(new VM_Settings_General()), new VM_BodyShapeDescriptorCreationMenu(), new VM_Settings_General(), null, null) { groupName = "" }); // blank entry
            foreach (var assetPack in assignment.SubscribedAssetPacks)
            {
                if (assignment.ForcedAssetPack == assetPack) { continue;}

                if (assetPack.gender == assignment.Gender)
                {
                    if (assetPack.ConfigType == AssetPackType.Primary)
                    {
                        assignment.AvailableAssetPacks.Add(assetPack);
                    }
                    else if (assetPack.ConfigType == AssetPackType.MixIn && !assignment.ForcedMixIns.Where(x => x.ForcedAssetPack != null && x.ForcedAssetPack.groupName != assetPack.groupName).Any())
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
            foreach (var topLevelSubgroup in assignment.ForcedAssetPack.subgroups)
            {
                bool topLevelTaken = false;
                foreach (var forcedSubgroup in assignment.ForcedSubgroups)
                {
                    if (topLevelSubgroup == forcedSubgroup || ContainsSubgroupID(topLevelSubgroup.Subgroups, forcedSubgroup.ID))
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
        public void TriggerDispNameUpdate(object sender, PropertyChangedEventArgs e)
        {
            if (this.NPCFormKey.IsNull == false)
            {
                this.DispName = Converters.CreateNPCDispNameFromFormKey(this.NPCFormKey);
            }
        }

        public void TriggerGenderUpdate(object sender, PropertyChangedEventArgs e)
        {
            this.Gender = getGender(this.NPCFormKey);
        }

        public static Gender getGender (FormKey NPCFormKey)
        {
            var npcFormLink = new FormLink<INpcGetter>(NPCFormKey);

            if (npcFormLink.TryResolve(GameEnvironmentProvider.MyEnvironment.LinkCache, out var npcRecord))
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

            // Warn User
            return Gender.Male;
        }

        public class VM_MixInSpecificAssignment : INotifyPropertyChanged, IHasForcedAssets
        {
            public VM_MixInSpecificAssignment(VM_SpecificNPCAssignment parent, ObservableCollection<VM_AssetPack> assetPacks, VM_SettingsBodyGen bodyGenSettings, VM_SettingsOBody oBodySettings, VM_Settings_General generalSettingsVM, ObservableCollection<VM_MixInSpecificAssignment> parentCollection)
            {
                ParentCollection = parentCollection;
                Parent = parent;

                this.AvailableMixInAssetPacks = Parent.AvailableMixInAssetPacks;
                this.ForcedAssetPack = new VM_AssetPack(assetPacks, bodyGenSettings, oBodySettings.DescriptorUI, generalSettingsVM, null, null);
                this.ForcedSubgroups = new ObservableCollection<VM_Subgroup>();
                this.AvailableSubgroups = new ObservableCollection<VM_Subgroup>();

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
            }
            public VM_AssetPack ForcedAssetPack { get; set; }
            public ObservableCollection<VM_AssetPack> AvailableMixInAssetPacks { get; set; }
            public ObservableCollection<VM_Subgroup> ForcedSubgroups { get; set; }
            public ObservableCollection<VM_Subgroup> AvailableSubgroups { get; set; }
            public ObservableCollection<VM_MixInSpecificAssignment> ParentCollection { get; set; }
            public VM_SpecificNPCAssignment Parent { get; set; }

            public RelayCommand DeleteCommand { get; set; }
            public RelayCommand DeleteForcedSubgroup { get; set; }

            public event PropertyChangedEventHandler PropertyChanged;

            public void TriggerAvailableSubgroupsUpdate(object sender, NotifyCollectionChangedEventArgs e)
            {
                UpdateAvailableSubgroups(this);
            }
        }
    }

    public interface IHasForcedAssets
    {
        public VM_AssetPack ForcedAssetPack { get; set; }
        ObservableCollection<VM_Subgroup> ForcedSubgroups { get; set; }
        public ObservableCollection<VM_Subgroup> AvailableSubgroups { get; set; }
    }
}
