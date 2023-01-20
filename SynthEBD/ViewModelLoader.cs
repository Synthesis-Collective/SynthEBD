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
        private readonly PatcherEnvironmentSourceProvider _patcherEnvironmentSourceProvider;
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
            PatcherEnvironmentSourceProvider patcherEnvironmentSourceProvider, 
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
            _patcherEnvironmentSourceProvider = patcherEnvironmentSourceProvider;
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
            });
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
            System.Windows.Forms.Cursor.Current = System.Windows.Forms.Cursors.Default;
        }

        public void LoadInitialSettingsViewModels() // view models that should be loaded before plugin VMs
        {
            // Load general settings
            VM_Settings_General.GetViewModelFromModel(_generalSettingsVM, _patcherSettingsSourceProvider, _patcherState, _raceAliasFactory, _linkedNPCFactory, _environmentProvider.LinkCache);
            VM_SettingsTexMesh.GetViewModelFromModel(_texMeshSettingsVM, _patcherState.TexMeshSettings);
            VM_BlockListUI.GetViewModelFromModel(_patcherState.BlockList, _blockList, _blockedNPCFactory, _blockedPluginFactory);
            VM_SettingsModManager.GetViewModelFromModel(_patcherState.ModManagerSettings, _settingsModManager);
        }

        public void LoadPluginViewModels()
        {
            VM_SettingsBodyGen.GetViewModelFromModel(_patcherState.BodyGenConfigs, _patcherState.BodyGenSettings, _bodyGenSettingsVM, _bodyGenConfigFactory, _generalSettingsVM.RaceGroupingEditor.RaceGroupings);
            VM_SettingsOBody.GetViewModelFromModel(_patcherState.OBodySettings, _settingsOBody, _generalSettingsVM.RaceGroupingEditor.RaceGroupings, _bodyShapeDescriptorCreator, _oBodyMiscSettingsFactory, _bodySlideFactory, _descriptorSelectionFactory, _attributeCreator, _logger);
            // load asset packs after BodyGen/BodySlide
            VM_AssetPack.GetViewModelsFromModels(_patcherState.AssetPacks, _texMeshSettingsVM, _patcherState.TexMeshSettings, _assetPackFactory, _raceGroupingFactory, _generalSettingsVM.RaceGroupingEditor.RaceGroupings); // add asset pack view models to TexMesh shell view model here
            _texMeshSettingsVM.AssetPresenterPrimary.AssetPack = _texMeshSettingsVM.AssetPacks.Where(x => x.GroupName == _texMeshSettingsVM.LastViewedAssetPackName).FirstOrDefault();

            VM_HeightConfig.GetViewModelsFromModels(_heightSettingsVM.AvailableHeightConfigs, _patcherState.HeightConfigs, _heightConfigFactory, _heightAssignmentFactory);
            VM_SettingsHeight.GetViewModelFromModel(_heightSettingsVM, _patcherState.HeightSettings); /// must do after populating configs
        }

        public void SavePluginViewModels()
        {
            VM_AssetPack.DumpViewModelsToModels(_texMeshSettingsVM.AssetPacks, _patcherState.AssetPacks);
            VM_HeightConfig.DumpViewModelsToModels(_heightSettingsVM.AvailableHeightConfigs, _patcherState.HeightConfigs, _logger);
            VM_SettingsBodyGen.DumpViewModelToModel(_bodyGenSettingsVM, _patcherState.BodyGenSettings, _patcherState.BodyGenConfigs);
        }

        public void LoadFinalSettingsViewModels() // view models that should be loaded after plugin VMs because they depend on the loaded plugins
        {
            _headPartSettingsVM.CopyInFromModel(_patcherState.HeadPartSettings, _generalSettingsVM.RaceGroupingEditor.RaceGroupings);

            // load specific assignments (must load after plugin view models)
            VM_SpecificNPCAssignmentsUI.GetViewModelFromModels(
                _assetPackFactory,
                _texMeshSettingsVM,
                _bodyGenSettingsVM,
                _headPartSettingsVM,
                _specificNpcAssignmentFactory,
                _npcAssignmentsUi,
                _patcherState.SpecificNPCAssignments,
                _logger,
                _converters,
                _environmentProvider);

            // Load Consistency (must load after plugin view models)
            VM_ConsistencyUI.GetViewModelsFromModels(_patcherState.Consistency, _consistencyUi.Assignments, _texMeshSettingsVM.AssetPacks, _headPartSettingsVM, _logger);
        }

        public void DumpViewModelsToModels()
        {
            _generalSettingsVM.DumpViewModelToModel(_generalSettingsVM);
            VM_SettingsTexMesh.DumpViewModelToModel(_texMeshSettingsVM, _patcherState.TexMeshSettings);
            VM_AssetPack.DumpViewModelsToModels(_texMeshSettingsVM.AssetPacks, _patcherState.AssetPacks);
            VM_SettingsHeight.DumpViewModelToModel(_heightSettingsVM, _patcherState.HeightSettings);
            VM_HeightConfig.DumpViewModelsToModels(_heightSettingsVM.AvailableHeightConfigs, _patcherState.HeightConfigs, _logger);
            VM_SettingsBodyGen.DumpViewModelToModel(_bodyGenSettingsVM, _patcherState.BodyGenSettings, _patcherState.BodyGenConfigs);
            _patcherState.OBodySettings = _settingsOBody.DumpViewModelToModel();
            _headPartSettingsVM.DumpViewModelToModel(_patcherState.HeadPartSettings);
            VM_SpecificNPCAssignmentsUI.DumpViewModelToModels(_npcAssignmentsUi, _patcherState.SpecificNPCAssignments);
            VM_BlockListUI.DumpViewModelToModel(_blockList, _patcherState.BlockList);
            VM_ConsistencyUI.DumpViewModelsToModels(_consistencyUi.Assignments, _patcherState.Consistency);
            VM_SettingsModManager.DumpViewModelToModel(_patcherState.ModManagerSettings, _settingsModManager);
        }

        public void SaveViewModelsToDrive()
        {
            DumpViewModelsToModels();
            _saveLoader.SaveStateToDrive();
        }
    }
}