using Mutagen.Bethesda.Skyrim;
using System.Security.Cryptography.X509Certificates;
using static SynthEBD.VM_BodyShapeDescriptor;
using static SynthEBD.VM_NPCAttribute;

namespace SynthEBD;

public class SaveLoader
{
    // Some are public properties to allow for circular IoC dependencies
    private readonly IStateProvider _stateProvider;
    private readonly PatcherSettingsSourceProvider _patcherSettingsSourceProvider;
    private readonly PatcherEnvironmentSourceProvider _patcherEnvironmentSourceProvider;
    private readonly MainState _state;
    private readonly Logger _logger;
    private readonly SynthEBDPaths _paths;
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
    private readonly Patcher _patcher;
    private readonly SettingsIO_Misc _miscIO;
    private readonly SettingsIO_General _generalIO;
    private readonly SettingsIO_AssetPack _assetIO;
    private readonly SettingsIO_BodyGen _bodyGenIO;
    private readonly SettingsIO_OBody _oBodyIO;
    private readonly SettingsIO_HeadParts _headpartIO;
    private readonly SettingsIO_Height _heightIO;
    private readonly SettingsIO_BlockList _blockListIO;
    private readonly SettingsIO_ModManager _modManagerIO;
    private readonly SettingsIO_SpecificNPCAssignments _specificNPCassignmentsIO;
    private readonly Converters _converters;
    private readonly VM_NPCAttributeCreator _attributeCreator;

    public SaveLoader(
        IStateProvider stateProvider,
        MainState state,
        VM_Settings_General generalSettings,
        VM_SettingsTexMesh texMeshSettings,
        VM_SettingsBodyGen bodyGenSettings,
        VM_AssetPack.Factory assetPackFactory,
        VM_HeightConfig.Factory heightConfigFactory,
        VM_SettingsHeight settingsHeight,
        VM_SettingsModManager modManager,
        VM_SettingsOBody oBody,
        VM_Settings_Headparts headPartSettings,
        VM_BodyShapeDescriptorCreator bodyShapeDescriptorCreator,
        VM_OBodyMiscSettings.Factory oBodyMiscSettingsFactory,
        VM_Settings_Headparts headParts,
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
        PatcherSettingsSourceProvider patcherSettingsSourceProvider,
        PatcherEnvironmentSourceProvider patcherEnvironmentSourceProvider,
        Logger logger,
        SynthEBDPaths paths,
        Patcher patcher,
        SettingsIO_Misc miscIO,
        SettingsIO_General generalIO,
        SettingsIO_AssetPack assetIO,
        SettingsIO_BodyGen bodyGenIO,
        SettingsIO_OBody oBodyIO,
        SettingsIO_HeadParts headpartIO,
        SettingsIO_Height heightIO,
        SettingsIO_BlockList blockListIO,
        SettingsIO_ModManager modManagerIO,
        SettingsIO_SpecificNPCAssignments specificNPCassignmentsIO,
        Converters converters,
        VM_NPCAttributeCreator attributeCreator)
    {
        _stateProvider = stateProvider;
        _state = state;
        _logger = logger;
        _paths = paths;
        _assetPackFactory = assetPackFactory;
        _heightConfigFactory = heightConfigFactory;
        _generalSettingsVM = generalSettings;
        _texMeshSettingsVM = texMeshSettings;
        _bodyGenSettingsVM = bodyGenSettings;
        _heightSettingsVM = settingsHeight;
        _headPartSettingsVM = headPartSettings;
        _settingsModManager = modManager;
        _settingsOBody = oBody;
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
        _patcherSettingsSourceProvider = patcherSettingsSourceProvider;
        _patcherEnvironmentSourceProvider = patcherEnvironmentSourceProvider;
        _patcher = patcher;
        _miscIO = miscIO;
        _generalIO = generalIO;
        _assetIO = assetIO;
        _bodyGenIO = bodyGenIO;
        _oBodyIO = oBodyIO;
        _headpartIO = headpartIO;
        _heightIO = heightIO;
        _blockListIO = blockListIO;
        _specificNPCassignmentsIO = specificNPCassignmentsIO;
        _modManagerIO = modManagerIO;
        _converters = converters;
        _attributeCreator = attributeCreator;
        _headPartSettingsVM = headParts;
    }

    public void Reinitialize()
    {
        System.Windows.Forms.Cursor.Current = System.Windows.Forms.Cursors.WaitCursor;
        LoadAllSettings();
        LoadInitialSettingsViewModels();
        LoadPluginViewModels();
        LoadFinalSettingsViewModels();
        System.Windows.Forms.Cursor.Current = System.Windows.Forms.Cursors.Default;
    }

    public void SaveAndRefreshPlugins()
    {
        System.Windows.Forms.Cursor.Current = System.Windows.Forms.Cursors.WaitCursor;
        SavePluginViewModels();
        LoadPlugins();
        LoadPluginViewModels();
        System.Windows.Forms.Cursor.Current = System.Windows.Forms.Cursors.Default;
    }

    public void SavePluginViewModels()
    {
        VM_AssetPack.DumpViewModelsToModels(_texMeshSettingsVM.AssetPacks, _state.AssetPacks);
        VM_HeightConfig.DumpViewModelsToModels(_heightSettingsVM.AvailableHeightConfigs, _state.HeightConfigs, _logger);
        VM_SettingsBodyGen.DumpViewModelToModel(_bodyGenSettingsVM, PatcherSettings.BodyGen, _state.BodyGenConfigs);
    }

    public void LoadAllSettings()
    {
        LoadInitialSettings();
        LoadPlugins();
        LoadMetaSettings();
    }

    public void LoadInitialSettings()
    {
        _generalIO.LoadGeneralSettings(out var loadSuccess); // Load general settings                                                           
        _patcher.ResolvePatchableRaces(); // Initialize patchable races from general settings (required by some UI elements)
        PatcherSettings.TexMesh = _assetIO.LoadTexMeshSettings(out loadSuccess); // Load texture and mesh settings
        PatcherSettings.BodyGen = _bodyGenIO.LoadBodyGenSettings(out loadSuccess);
        PatcherSettings.OBody = _oBodyIO.LoadOBodySettings(out loadSuccess);
        // load OBody settings before asset packs - asset packs depend on BodyGen but not vice versa
        PatcherSettings.OBody.ImportBodySlides(PatcherSettings.OBody.TemplateDescriptors, _oBodyIO, _stateProvider.DataFolderPath);
        PatcherSettings.HeadParts = _headpartIO.LoadHeadPartSettings(out loadSuccess); // load head part settings
        PatcherSettings.Height = _heightIO.LoadHeightSettings(out loadSuccess); // load heights
        _state.BlockList = _blockListIO.LoadBlockList(out loadSuccess); // load BlockList
        PatcherSettings.ModManagerIntegration = _modManagerIO.LoadModManagerSettings(out loadSuccess); // load Mod Manager Integration
    }

    public void LoadInitialSettingsViewModels() // view models that should be loaded before plugin VMs
    {
        // Load general settings
        VM_Settings_General.GetViewModelFromModel(_generalSettingsVM, _patcherSettingsSourceProvider, _raceAliasFactory, _linkedNPCFactory, _raceGroupingFactory, _stateProvider.LinkCache);
        VM_SettingsTexMesh.GetViewModelFromModel(_texMeshSettingsVM, PatcherSettings.TexMesh);     
        VM_BlockListUI.GetViewModelFromModel(_state.BlockList, _blockList, _blockedNPCFactory, _blockedPluginFactory);
        VM_SettingsModManager.GetViewModelFromModel(PatcherSettings.ModManagerIntegration, _settingsModManager);
    }

    public void LoadPlugins()
    {
        // load bodygen configs before asset packs - asset packs depend on BodyGen but not vice versa
        _state.BodyGenConfigs = _bodyGenIO.LoadBodyGenConfigs(PatcherSettings.General.RaceGroupings, out var loadSuccess);
        _state.RecordTemplatePlugins = _assetIO.LoadRecordTemplates(out loadSuccess);
        _state.RecordTemplateLinkCache = _state.RecordTemplatePlugins.ToImmutableLinkCache();
        _state.AssetPacks = _assetIO.LoadAssetPacks(PatcherSettings.General.RaceGroupings, _state.RecordTemplatePlugins, _state.BodyGenConfigs, out loadSuccess); // load asset pack models from json
        _state.HeightConfigs = _heightIO.LoadHeightConfigs(out loadSuccess); // load heights
    }

    public void LoadPluginViewModels()
    {
        VM_SettingsBodyGen.GetViewModelFromModel(_state.BodyGenConfigs, PatcherSettings.BodyGen, _bodyGenSettingsVM, _bodyGenConfigFactory, _generalSettingsVM);
        VM_SettingsOBody.GetViewModelFromModel(PatcherSettings.OBody, _settingsOBody, _generalSettingsVM.RaceGroupings, _bodyShapeDescriptorCreator, _oBodyMiscSettingsFactory, _bodySlideFactory, _descriptorSelectionFactory, _attributeCreator, _logger);
        // load asset packs after BodyGen/BodySlide
        VM_AssetPack.GetViewModelsFromModels(_state.AssetPacks, _texMeshSettingsVM, PatcherSettings.TexMesh, _assetPackFactory); // add asset pack view models to TexMesh shell view model here
        _texMeshSettingsVM.AssetPresenterPrimary.AssetPack = _texMeshSettingsVM.AssetPacks.Where(x => x.GroupName == _texMeshSettingsVM.LastViewedAssetPackName).FirstOrDefault();
        
        VM_HeightConfig.GetViewModelsFromModels(_heightSettingsVM.AvailableHeightConfigs, _state.HeightConfigs, _heightConfigFactory, _heightAssignmentFactory);
        VM_SettingsHeight.GetViewModelFromModel(_heightSettingsVM, PatcherSettings.Height); /// must do after populating configs
    }

    public void LoadMetaSettings()
    {
        _state.SpecificNPCAssignments = _specificNPCassignmentsIO.LoadAssignments(out var loadSuccess);
        _state.Consistency = _miscIO.LoadConsistency(out loadSuccess);
    }

    public void LoadFinalSettingsViewModels() // view models that should be loaded after plugin VMs because they depend on the loaded plugins
    {
        _headPartSettingsVM.CopyInFromModel(PatcherSettings.HeadParts, _generalSettingsVM.RaceGroupings);

        // load specific assignments (must load after plugin view models)
        VM_SpecificNPCAssignmentsUI.GetViewModelFromModels(
            _assetPackFactory,
            _texMeshSettingsVM,
            _bodyGenSettingsVM,
            _headPartSettingsVM,
            _specificNpcAssignmentFactory,
            _npcAssignmentsUi,
            _state.SpecificNPCAssignments,
            _logger,
            _converters,
            _stateProvider);

        // Load Consistency (must load after plugin view models)
        VM_ConsistencyUI.GetViewModelsFromModels(_state.Consistency, _consistencyUi.Assignments, _texMeshSettingsVM.AssetPacks, _headPartSettingsVM, _logger);
    }

    public void DumpViewModelsToModels()
    {
        VM_Settings_General.DumpViewModelToModel(_generalSettingsVM, PatcherSettings.General);
        VM_SettingsTexMesh.DumpViewModelToModel(_texMeshSettingsVM, PatcherSettings.TexMesh);
        VM_AssetPack.DumpViewModelsToModels(_texMeshSettingsVM.AssetPacks, _state.AssetPacks);
        VM_SettingsHeight.DumpViewModelToModel(_heightSettingsVM, PatcherSettings.Height);
        VM_HeightConfig.DumpViewModelsToModels(_heightSettingsVM.AvailableHeightConfigs, _state.HeightConfigs, _logger);
        VM_SettingsBodyGen.DumpViewModelToModel(_bodyGenSettingsVM, PatcherSettings.BodyGen, _state.BodyGenConfigs);
        PatcherSettings.OBody = _settingsOBody.DumpViewModelToModel();
        _headPartSettingsVM.DumpViewModelToModel(PatcherSettings.HeadParts);
        VM_SpecificNPCAssignmentsUI.DumpViewModelToModels(_npcAssignmentsUi, _state.SpecificNPCAssignments);
        VM_BlockListUI.DumpViewModelToModel(_blockList, _state.BlockList);
        VM_ConsistencyUI.DumpViewModelsToModels(_consistencyUi.Assignments, _state.Consistency);
        VM_SettingsModManager.DumpViewModelToModel(PatcherSettings.ModManagerIntegration, _settingsModManager);
    }

    public void SaveEntireState()
    {
        DumpViewModelsToModels();

        bool saveSuccess;
        string captionStr;
        string exceptionStr;
        string allExceptions = "";
        bool showFinalExceptions = false;

        JSONhandler<Settings_General>.SaveJSONFile(PatcherSettings.General, _paths.GeneralSettingsPath, out saveSuccess, out exceptionStr);
        if (!saveSuccess)
        {
            captionStr = "Error saving General Settings: ";
            _logger.LogError(captionStr + exceptionStr); allExceptions += captionStr + exceptionStr + Environment.NewLine; showFinalExceptions = true;
        }

        JSONhandler<Settings_TexMesh>.SaveJSONFile(PatcherSettings.TexMesh, _paths.TexMeshSettingsPath, out saveSuccess, out exceptionStr);
        if (!saveSuccess) 
        {
            captionStr = "Error saving Texture and Mesh Settings: ";
            _logger.LogError(captionStr + exceptionStr); allExceptions += captionStr + exceptionStr + Environment.NewLine; showFinalExceptions = true;
        }

        _assetIO.SaveAssetPacks(_state.AssetPacks, out saveSuccess);
        if (!saveSuccess) 
        {
            captionStr = "Error saving Asset Packs: ";
            _logger.LogError(captionStr); allExceptions += captionStr + Environment.NewLine; showFinalExceptions = true;
        }

        JSONhandler<Settings_Height>.SaveJSONFile(PatcherSettings.Height, _paths.HeightSettingsPath, out saveSuccess, out exceptionStr);
        if (!saveSuccess) 
        {
            captionStr = "Error saving Height Settings: ";
            _logger.LogError(captionStr + exceptionStr); allExceptions += captionStr + exceptionStr + Environment.NewLine; showFinalExceptions = true;
        }

        _heightIO.SaveHeightConfigs(_state.HeightConfigs, out saveSuccess);
        if (!saveSuccess) 
        {
            captionStr = "Error saving Height Configs: ";
            _logger.LogError(captionStr); allExceptions += captionStr + Environment.NewLine; showFinalExceptions = true;
        }

        JSONhandler<Settings_BodyGen>.SaveJSONFile(PatcherSettings.BodyGen, _paths.BodyGenSettingsPath, out saveSuccess, out exceptionStr);
        if (!saveSuccess) 
        {
            captionStr = "Error saving BodyGen Settings: ";
            _logger.LogError(captionStr + exceptionStr); allExceptions += captionStr + exceptionStr + Environment.NewLine; showFinalExceptions = true;
        }

        _bodyGenIO.SaveBodyGenConfigs(_state.BodyGenConfigs.Female, out saveSuccess);
        if (!saveSuccess) 
        {
            captionStr = "Error saving BodyGen configs";
            _logger.LogError(captionStr); allExceptions += captionStr + Environment.NewLine; showFinalExceptions = true;
        }

        _bodyGenIO.SaveBodyGenConfigs(_state.BodyGenConfigs.Male, out saveSuccess);
        if (!saveSuccess) 
        {
            captionStr = "Error saving BodyGen configs";
            _logger.LogError(captionStr); allExceptions += captionStr + Environment.NewLine; showFinalExceptions = true;
        }

        JSONhandler<Settings_OBody>.SaveJSONFile(PatcherSettings.OBody, _paths.OBodySettingsPath, out saveSuccess, out exceptionStr);
        if (!saveSuccess) 
        {
            captionStr = "Error saving OBody/AutoBody Settings: ";
            _logger.LogError(captionStr + exceptionStr); allExceptions += captionStr + exceptionStr + Environment.NewLine; showFinalExceptions = true;
        }

        JSONhandler<Settings_Headparts>.SaveJSONFile(PatcherSettings.HeadParts, _paths.HeadPartsSettingsPath, out saveSuccess, out exceptionStr);
        if (!saveSuccess) 
        {
            captionStr = "Error saving Head Parts Settings: ";
            _logger.LogError(captionStr + exceptionStr); allExceptions += captionStr + exceptionStr + Environment.NewLine; showFinalExceptions = true;
        }

        _miscIO.SaveConsistency(_state.Consistency, out saveSuccess);
        if (!saveSuccess) 
        {
            captionStr = "Error saving Consistency";
            _logger.LogError(captionStr); allExceptions += captionStr + Environment.NewLine; showFinalExceptions = true;
        }

        _specificNPCassignmentsIO.SaveAssignments(_state.SpecificNPCAssignments, out saveSuccess);
        if (!saveSuccess) 
        {
            captionStr = "Error saving Specific NPC Assignments";
            _logger.LogError(captionStr); allExceptions += captionStr + Environment.NewLine; showFinalExceptions = true;
        }

        _blockListIO.SaveBlockList(_state.BlockList, out saveSuccess);
        if (!saveSuccess) 
        {
            captionStr = "Error saving Block List";
            _logger.LogError(captionStr); allExceptions += captionStr + Environment.NewLine; showFinalExceptions = true;
        }

        JSONhandler<Settings_ModManager>.SaveJSONFile(PatcherSettings.ModManagerIntegration, _paths.ModManagerSettingsPath, out saveSuccess, out exceptionStr);
        if (!saveSuccess) 
        {
            captionStr = "Error saving Mod Manager Integration Settings: ";
            _logger.LogError(captionStr + exceptionStr); allExceptions += captionStr + exceptionStr + Environment.NewLine; showFinalExceptions = true;
        }

        _patcherSettingsSourceProvider.SaveSettingsSource(_generalSettingsVM, out saveSuccess, out exceptionStr);
        if (!saveSuccess) 
        {
            captionStr = "Error saving Load Source Settings: ";
            _logger.LogError(captionStr + exceptionStr); allExceptions += captionStr + exceptionStr + Environment.NewLine; showFinalExceptions = true;
        }

        if (_stateProvider.RunMode == Mode.Standalone)
        {
            var envSource = new StandaloneEnvironmentSource()
            {
                GameEnvironmentDirectory = _stateProvider.DataFolderPath,
                SkyrimVersion = _stateProvider.SkyrimVersion,
                OutputModName = _stateProvider.OutputModName
            };
            JSONhandler<StandaloneEnvironmentSource>.SaveJSONFile(envSource, _patcherEnvironmentSourceProvider.SourcePath, out saveSuccess, out exceptionStr);
            if (!saveSuccess)
            {
                captionStr = "Error saving Environment Source Settings: ";
                _logger.LogError(captionStr + exceptionStr); allExceptions += captionStr + exceptionStr + Environment.NewLine; showFinalExceptions = true;
            }
        }

        if (showFinalExceptions)
        {
            string notificationStr = allExceptions;
            CustomMessageBox.DisplayNotificationOK("Errors were encountered upon closing", notificationStr);
        }
    }

    public void SaveConsistency()
    {
        _miscIO.SaveConsistency(_state.Consistency, out _);
    }
}