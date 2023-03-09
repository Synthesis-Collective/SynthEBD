using Noggog;
using Noggog.WPF;
using ReactiveUI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Linq;
using System.Text;
using System.Threading.Tasks;
using static SynthEBD.VM_BodyShapeDescriptor;
using static SynthEBD.VM_NPCAttribute;
using static System.Resources.ResXFileRef;

namespace SynthEBD
{
    public class ViewModelLoader : VM
    {
        private readonly IEnvironmentStateProvider _environmentProvider;
        private readonly PatcherSettingsSourceProvider _patcherSettingsSourceProvider;
        private readonly PatcherState _patcherState;
        private readonly Logger _logger;
        private readonly SynthEBDPaths _paths;
        private readonly SaveLoader _saveLoader;
        private readonly Converters _converters;

        private readonly VM_AssetPack.Factory _assetPackFactory;
        private readonly VM_HeightConfig.Factory _heightConfigFactory;
        private readonly VM_Settings_General _generalSettingsVM;
        private readonly VM_SettingsTexMesh _texMeshSettingsVM;
        private readonly VM_SettingsHeight _heightSettingsVM;
        private readonly VM_SettingsBodyGen _bodyGenSettingsVM;
        private readonly VM_Settings_Headparts _headPartSettingsVM;
        private readonly VM_SettingsModManager _settingsModManager;
        private readonly VM_SettingsOBody _settingsOBody;
        private readonly VM_SpecificNPCAssignmentsUI _specificAssignmentsUI;
        private readonly VM_BodyShapeDescriptorCreator _bodyShapeDescriptorCreator;
        private readonly VM_OBodyMiscSettings.Factory _oBodyMiscSettingsFactory;
        private readonly VM_ConsistencyUI _consistencyUi;
        private readonly VM_BlockListUI _blockList;
        private readonly VM_RaceAlias.Factory _raceAliasFactory;
        private readonly VM_RaceGrouping.Factory _raceGroupingFactory;
        private readonly VM_LinkedNPCGroup.Factory _linkedNPCFactory;
        private readonly VM_BodyGenConfig.Factory _bodyGenConfigFactory;
        private readonly VM_BodySlideSetting.Factory _bodySlideFactory;
        private readonly VM_BodyShapeDescriptorSelectionMenu.Factory _descriptorSelectionFactory;
        private readonly VM_HeightAssignment.Factory _heightAssignmentFactory;
        private readonly VM_SpecificNPCAssignment.Factory _specificNpcAssignmentFactory;
        private readonly VM_BlockedNPC.Factory _blockedNPCFactory;
        private readonly VM_BlockedPlugin.Factory _blockedPluginFactory;
        private readonly VM_SpecificNPCAssignmentsUI _npcAssignmentsUi;
        private readonly VM_NPCAttributeCreator _attributeCreator;

        public ViewModelLoader(
            IEnvironmentStateProvider environmentProvider, 
            PatcherSettingsSourceProvider patcherSettingsSourceProvider, 
            PatcherState patcherState, 
            Logger logger, 
            SynthEBDPaths paths, 
            SaveLoader saveLoader,
            Converters converters,
            VM_AssetPack.Factory assetPackFactory, 
            VM_HeightConfig.Factory heightConfigFactory, 
            VM_Settings_General generalSettingsVM, 
            VM_SettingsTexMesh texMeshSettingsVM, 
            VM_SettingsHeight heightSettingsVM, 
            VM_SettingsBodyGen bodyGenSettingsVM, 
            VM_Settings_Headparts headPartSettingsVM, 
            VM_SettingsModManager settingsModManager, 
            VM_SettingsOBody settingsOBody,
            VM_SpecificNPCAssignmentsUI specificAssignmentsUI,
            VM_BodyShapeDescriptorCreator bodyShapeDescriptorCreator, 
            VM_OBodyMiscSettings.Factory oBodyMiscSettingsFactory, 
            VM_ConsistencyUI consistencyUi, 
            VM_BlockListUI blockList, 
            VM_RaceAlias.Factory raceAliasFactory, 
            VM_RaceGrouping.Factory raceGroupingFactory, 
            VM_LinkedNPCGroup.Factory linkedNPCFactory, 
            VM_BodyGenConfig.Factory bodyGenConfigFactory, 
            VM_BodySlideSetting.Factory bodySlideFactory, 
            VM_BodyShapeDescriptorSelectionMenu.Factory descriptorSelectionFactory, 
            VM_HeightAssignment.Factory heightAssignmentFactory, 
            VM_SpecificNPCAssignment.Factory specificNpcAssignmentFactory, 
            VM_BlockedNPC.Factory blockedNPCFactory, 
            VM_BlockedPlugin.Factory blockedPluginFactory, 
            VM_SpecificNPCAssignmentsUI npcAssignmentsUi,
            VM_NPCAttributeCreator attributeCreator)
        {
            _environmentProvider = environmentProvider;
            _patcherSettingsSourceProvider = patcherSettingsSourceProvider;
            _patcherState = patcherState;
            _logger = logger;
            _paths = paths;
            _saveLoader = saveLoader;
            _converters = converters;
            _assetPackFactory = assetPackFactory;
            _heightConfigFactory = heightConfigFactory;
            _generalSettingsVM = generalSettingsVM;
            _texMeshSettingsVM = texMeshSettingsVM;
            _heightSettingsVM = heightSettingsVM;
            _bodyGenSettingsVM = bodyGenSettingsVM;
            _headPartSettingsVM = headPartSettingsVM;
            _settingsModManager = settingsModManager;
            _settingsOBody = settingsOBody;
            _specificAssignmentsUI = specificAssignmentsUI;
            _bodyShapeDescriptorCreator = bodyShapeDescriptorCreator;
            _oBodyMiscSettingsFactory = oBodyMiscSettingsFactory;
            _consistencyUi = consistencyUi;
            _blockList = blockList;
            _raceAliasFactory = raceAliasFactory;
            _raceGroupingFactory = raceGroupingFactory;
            _linkedNPCFactory = linkedNPCFactory;
            _bodyGenConfigFactory = bodyGenConfigFactory;
            _bodySlideFactory = bodySlideFactory;
            _descriptorSelectionFactory = descriptorSelectionFactory;
            _heightAssignmentFactory = heightAssignmentFactory;
            _specificNpcAssignmentFactory = specificNpcAssignmentFactory;
            _blockedNPCFactory = blockedNPCFactory;
            _blockedPluginFactory = blockedPluginFactory;
            _npcAssignmentsUi = npcAssignmentsUi;
            _attributeCreator = attributeCreator;

            Observable.CombineLatest(
                _patcherSettingsSourceProvider.WhenAnyValue(x => x.UsePortableSettings),
                _patcherSettingsSourceProvider.WhenAnyValue(x => x.PortableSettingsFolder),
                (_, _) => { return 0; })
            .Subscribe(_ => {
                Reinitialize();
            }).DisposeWith(this);
        }

        public void SaveAndRefreshPlugins()
        {
            System.Windows.Forms.Cursor.Current = System.Windows.Forms.Cursors.WaitCursor;
            SavePluginViewModels();
            _saveLoader.LoadPlugins();
            LoadPluginViewModels();
            System.Windows.Forms.Cursor.Current = System.Windows.Forms.Cursors.Default;
        }

        public void Reinitialize()
        {
            System.Windows.Forms.Cursor.Current = System.Windows.Forms.Cursors.WaitCursor;
            _saveLoader.LoadAllSettings();
            LoadInitialSettingsViewModels();
            LoadPluginViewModels();
            LoadFinalSettingsViewModels();
            _logger.WriteStartupLog();
            System.Windows.Forms.Cursor.Current = System.Windows.Forms.Cursors.Default;
        }

        public void LoadInitialSettingsViewModels() // view models that should be loaded before plugin VMs
        {
            // Load general settings
            _generalSettingsVM.CopyInFromModel(_patcherState.GeneralSettings, _raceAliasFactory, _linkedNPCFactory, _environmentProvider.LinkCache);
            _texMeshSettingsVM.CopyInViewModelFromModel(_patcherState.TexMeshSettings);
            _blockList.CopyInViewModelFromModel(_patcherState.BlockList, _blockedNPCFactory, _blockedPluginFactory);
            _settingsModManager.CopyInViewModelFromModel(_patcherState.ModManagerSettings);
        }

        public void LoadPluginViewModels()
        {
            _bodyGenSettingsVM.CopyInViewModelFromModel(_patcherState.BodyGenConfigs, _patcherState.BodyGenSettings, _bodyGenConfigFactory, _generalSettingsVM.RaceGroupingEditor.RaceGroupings);
            _settingsOBody.CopyInViewModelFromModel(_patcherState.OBodySettings, _bodyShapeDescriptorCreator, _oBodyMiscSettingsFactory, _descriptorSelectionFactory, _attributeCreator, _logger);
            // load asset packs after BodyGen/BodySlide
            VM_AssetPack.GetViewModelsFromModels(_patcherState.AssetPacks, _texMeshSettingsVM, _patcherState.TexMeshSettings, _assetPackFactory, _generalSettingsVM.RaceGroupingEditor.RaceGroupings, _logger); // add asset pack view models to TexMesh shell view model here
            _texMeshSettingsVM.AssetPresenterPrimary.AssetPack = _texMeshSettingsVM.AssetPacks.Where(x => x.GroupName == _texMeshSettingsVM.LastViewedAssetPackName).FirstOrDefault();

            VM_HeightConfig.GetViewModelsFromModels(_heightSettingsVM.AvailableHeightConfigs, _patcherState.HeightConfigs, _heightConfigFactory, _heightAssignmentFactory, _logger);
            _heightSettingsVM.CopyInFromModel(_patcherState.HeightSettings); /// must do after populating configs
        }

        public void LoadFinalSettingsViewModels() // view models that should be loaded after plugin VMs because they depend on the loaded plugins
        {
            _texMeshSettingsVM.AssetOrderingMenu.CopyInFromModel(_patcherState.TexMeshSettings?.AssetOrder ?? null);
            _headPartSettingsVM.Initialize();
            _headPartSettingsVM.CopyInFromModel(_patcherState.HeadPartSettings, _generalSettingsVM.RaceGroupingEditor.RaceGroupings);

            // load specific assignments (must load after plugin view models)
            _specificAssignmentsUI.GetViewModelFromModels(_patcherState.SpecificNPCAssignments);

            // Load Consistency (must load after plugin view models)
            VM_ConsistencyUI.GetViewModelsFromModels(_patcherState.Consistency, _consistencyUi.Assignments, _texMeshSettingsVM.AssetPacks, _headPartSettingsVM, _logger);
        }

        public void SavePluginViewModels()
        {
            VM_AssetPack.DumpViewModelsToModels(_texMeshSettingsVM.AssetPacks, _patcherState.AssetPacks);
            VM_HeightConfig.DumpViewModelsToModels(_heightSettingsVM.AvailableHeightConfigs, _patcherState.HeightConfigs, _logger);
            _patcherState.BodyGenConfigs = _bodyGenSettingsVM.DumpBodyGenConfigsToModels();
        }

        public void DumpViewModelsToModels()
        {
            _patcherState.GeneralSettings = _generalSettingsVM.DumpViewModelToModel();
            _patcherState.TexMeshSettings = _texMeshSettingsVM.DumpViewModelToModel();
            _patcherState.HeightSettings = _heightSettingsVM.DumpViewModelToModel();
            _patcherState.BodyGenSettings = _bodyGenSettingsVM.DumpViewModelToModel();
            _patcherState.OBodySettings = _settingsOBody.DumpViewModelToModel();
            _patcherState.HeadPartSettings = _headPartSettingsVM.DumpViewModelToModel();
            _patcherState.SpecificNPCAssignments = _npcAssignmentsUi.DumpViewModelToModels();
            _patcherState.BlockList = _blockList.DumpViewModelToModel();
            _patcherState.Consistency = _consistencyUi.DumpViewModelsToModels();
            _patcherState.ModManagerSettings = _settingsModManager.DumpViewModelToModel();
            SavePluginViewModels();
        }

        public void SaveViewModelsToDrive()
        {
            DumpViewModelsToModels();
            _saveLoader.SaveStateToDrive();
        }
    }
}
