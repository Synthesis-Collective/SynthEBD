using Mutagen.Bethesda.Skyrim;
using static SynthEBD.VM_BodyShapeDescriptor;
using static SynthEBD.VM_NPCAttribute;

namespace SynthEBD;

public class SaveLoader
{
    // Some are public properties to allow for circular IoC dependencies
    private readonly MainState _state;
    private readonly Logger _logger;
    private readonly SynthEBDPaths _paths;
    private readonly VM_AssetPack.Factory _assetPackFactory;
    private readonly VM_HeightConfig.Factory _heightConfigFactory;
    public VM_Settings_General General { get; set; }
    public VM_SettingsTexMesh TexMesh { get; set; }
    private readonly VM_SettingsHeight _settingsHeight;
    public VM_SettingsBodyGen BodyGen { get; set; }
    public VM_Settings_Headparts HeadParts { get; set; }
    private readonly VM_SettingsModManager _modManager;
    private readonly VM_SettingsOBody _oBody;
    private readonly VM_BodyShapeDescriptorCreator _bodyShapeDescriptorCreator;
    private readonly VM_OBodyMiscSettings.Factory _oBodyMiscSettingsFactory;
    private readonly VM_ConsistencyUI _consistencyUi;
    private readonly VM_BlockListUI _blockList;
    private readonly VM_BodyGenConfig.Factory _bodyGenConfigFactory;
    private readonly VM_BodySlideSetting.Factory _bodySlideFactory;
    private readonly VM_BodyShapeDescriptorSelectionMenu.Factory _descriptorSelectionFactory;
    private readonly VM_SpecificNPCAssignment.Factory _specificNpcAssignmentFactory;
    private readonly VM_SpecificNPCAssignmentsUI _npcAssignmentsUi;
    private readonly PatcherSettingsSourceProvider _patcherSettingsProvider;
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
        MainState state,
        VM_AssetPack.Factory assetPackFactory,
        VM_HeightConfig.Factory heightConfigFactory,
        VM_SettingsHeight settingsHeight,
        VM_SettingsModManager modManager,
        VM_SettingsOBody oBody,
        VM_BodyShapeDescriptorCreator bodyShapeDescriptorCreator,
        VM_OBodyMiscSettings.Factory oBodyMiscSettingsFactory,
        VM_Settings_Headparts headParts,
        VM_ConsistencyUI consistencyUi,
        VM_BlockListUI blockList,
        VM_BodyGenConfig.Factory bodyGenConfigFactory,
        VM_BodySlideSetting.Factory bodySlideFactory,
        VM_BodyShapeDescriptorSelectionMenu.Factory descriptorSelectionFactory,
        VM_SpecificNPCAssignment.Factory specificNpcAssignmentFactory,
        VM_SpecificNPCAssignmentsUI npcAssignmentsUi,
        PatcherSettingsSourceProvider patcherSettingsProvider,
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
        _state = state;
        _logger = logger;
        _paths = paths;
        _assetPackFactory = assetPackFactory;
        _heightConfigFactory = heightConfigFactory;
        _settingsHeight = settingsHeight;
        _modManager = modManager;
        _oBody = oBody;
        _bodyShapeDescriptorCreator = bodyShapeDescriptorCreator;
        _oBodyMiscSettingsFactory = oBodyMiscSettingsFactory;
        _consistencyUi = consistencyUi;
        _blockList = blockList;
        _bodyGenConfigFactory = bodyGenConfigFactory;
        _bodySlideFactory = bodySlideFactory;
        _descriptorSelectionFactory = descriptorSelectionFactory;
        _specificNpcAssignmentFactory = specificNpcAssignmentFactory;
        _npcAssignmentsUi = npcAssignmentsUi;
        _patcherSettingsProvider = patcherSettingsProvider;
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
        HeadParts = headParts;
    }

    public void Reinitialize()
    {
        System.Windows.Forms.Cursor.Current = System.Windows.Forms.Cursors.WaitCursor;
        LoadInitialSettingsViewModels();
        LoadPluginViewModels();
        LoadFinalSettingsViewModels();
        System.Windows.Forms.Cursor.Current = System.Windows.Forms.Cursors.Default;
    }

    public void SaveAndRefreshPlugins()
    {
        System.Windows.Forms.Cursor.Current = System.Windows.Forms.Cursors.WaitCursor;
        SavePluginViewModels();
        LoadPluginViewModels();
        System.Windows.Forms.Cursor.Current = System.Windows.Forms.Cursors.Default;
    }

    public void SavePluginViewModels()
    {
        VM_AssetPack.DumpViewModelsToModels(TexMesh.AssetPacks, _state.AssetPacks);
        VM_HeightConfig.DumpViewModelsToModels(_settingsHeight.AvailableHeightConfigs, _state.HeightConfigs, _logger);
        VM_SettingsBodyGen.DumpViewModelToModel(BodyGen, PatcherSettings.BodyGen, _state.BodyGenConfigs);
    }

    public void LoadInitialSettingsViewModels() // view models that should be loaded before plugin VMs
    {
        // Load general settings
        _generalIO.LoadGeneralSettings(out var loadSuccess);
        VM_Settings_General.GetViewModelFromModel(General, _patcherSettingsProvider);

        // Initialize patchable races from general settings (required by some UI elements)
        _patcher.ResolvePatchableRaces();

        // Load texture and mesh settings
        PatcherSettings.TexMesh = _assetIO.LoadTexMeshSettings(out loadSuccess);
        VM_SettingsTexMesh.GetViewModelFromModel(TexMesh, PatcherSettings.TexMesh);

        PatcherSettings.BodyGen = _bodyGenIO.LoadBodyGenSettings(out loadSuccess);

        // load OBody settings before asset packs - asset packs depend on BodyGen but not vice versa
        PatcherSettings.OBody = _oBodyIO.LoadOBodySettings(out loadSuccess);
        PatcherSettings.OBody.ImportBodySlides(PatcherSettings.OBody.TemplateDescriptors);

        // load head part settings
        PatcherSettings.HeadParts = _headpartIO.LoadHeadPartSettings(out loadSuccess);

        // load heights
        PatcherSettings.Height = _heightIO.LoadHeightSettings(out loadSuccess);

        // load BlockList
        _state.BlockList = _blockListIO.LoadBlockList(out loadSuccess);
        VM_BlockListUI.GetViewModelFromModel(_state.BlockList, _blockList, _converters);

        // load Mod Manager Integration
        PatcherSettings.ModManagerIntegration = _modManagerIO.LoadModManagerSettings(out loadSuccess);
        VM_SettingsModManager.GetViewModelFromModel(PatcherSettings.ModManagerIntegration, _modManager);
    }

    public void LoadPluginViewModels()
    {
        // load bodygen configs before asset packs - asset packs depend on BodyGen but not vice versa
        _state.BodyGenConfigs = _bodyGenIO.LoadBodyGenConfigs(PatcherSettings.General.RaceGroupings, out var loadSuccess);
        VM_SettingsBodyGen.GetViewModelFromModel(_state.BodyGenConfigs, PatcherSettings.BodyGen, BodyGen, _bodyGenConfigFactory, General);

        VM_SettingsOBody.GetViewModelFromModel(PatcherSettings.OBody, _oBody, General.RaceGroupings, _bodyShapeDescriptorCreator, _oBodyMiscSettingsFactory, _bodySlideFactory, _descriptorSelectionFactory, _attributeCreator, _logger);

        _state.RecordTemplatePlugins = _assetIO.LoadRecordTemplates(out loadSuccess);
        _state.RecordTemplateLinkCache = _state.RecordTemplatePlugins.ToImmutableLinkCache();

        // load asset packs
        _state.AssetPacks = _assetIO.LoadAssetPacks(PatcherSettings.General.RaceGroupings, _state.RecordTemplatePlugins, _state.BodyGenConfigs, out loadSuccess); // load asset pack models from json
        VM_AssetPack.GetViewModelsFromModels(_state.AssetPacks, TexMesh, PatcherSettings.TexMesh, _assetPackFactory); // add asset pack view models to TexMesh shell view model here
        TexMesh.AssetPresenterPrimary.AssetPack = TexMesh.AssetPacks.Where(x => x.GroupName == TexMesh.LastViewedAssetPackName).FirstOrDefault();

        // load heights
        _state.HeightConfigs = _heightIO.LoadHeightConfigs(out loadSuccess);

        VM_HeightConfig.GetViewModelsFromModels(_settingsHeight.AvailableHeightConfigs, _state.HeightConfigs, _heightConfigFactory);
        VM_SettingsHeight.GetViewModelFromModel(_settingsHeight, PatcherSettings.Height); /// must do after populating configs
    }

    public void LoadFinalSettingsViewModels() // view models that should be loaded after plugin VMs because they depend on the loaded plugins
    {
        HeadParts.CopyInFromModel(PatcherSettings.HeadParts, General.RaceGroupings);

        // load specific assignments (must load after plugin view models)
        _state.SpecificNPCAssignments = _specificNPCassignmentsIO.LoadAssignments(out var loadSuccess);
        VM_SpecificNPCAssignmentsUI.GetViewModelFromModels(
            _assetPackFactory,
            TexMesh,
            BodyGen,
            HeadParts,
            _specificNpcAssignmentFactory,
            _npcAssignmentsUi,
            _state.SpecificNPCAssignments,
            _logger,
            _converters);

        // Load Consistency (must load after plugin view models)
        _state.Consistency = _miscIO.LoadConsistency(out loadSuccess);
        VM_ConsistencyUI.GetViewModelsFromModels(_state.Consistency, _consistencyUi.Assignments, TexMesh.AssetPacks, HeadParts, _logger);
    }

    public void DumpViewModelsToModels()
    {
        VM_Settings_General.DumpViewModelToModel(General, PatcherSettings.General);
        VM_SettingsTexMesh.DumpViewModelToModel(TexMesh, PatcherSettings.TexMesh);
        VM_AssetPack.DumpViewModelsToModels(TexMesh.AssetPacks, _state.AssetPacks);
        VM_SettingsHeight.DumpViewModelToModel(_settingsHeight, PatcherSettings.Height);
        VM_HeightConfig.DumpViewModelsToModels(_settingsHeight.AvailableHeightConfigs, _state.HeightConfigs, _logger);
        VM_SettingsBodyGen.DumpViewModelToModel(BodyGen, PatcherSettings.BodyGen, _state.BodyGenConfigs);
        PatcherSettings.OBody = _oBody.DumpViewModelToModel();
        HeadParts.DumpViewModelToModel(PatcherSettings.HeadParts);
        VM_SpecificNPCAssignmentsUI.DumpViewModelToModels(_npcAssignmentsUi, _state.SpecificNPCAssignments);
        VM_BlockListUI.DumpViewModelToModel(_blockList, _state.BlockList);
        VM_ConsistencyUI.DumpViewModelsToModels(_consistencyUi.Assignments, _state.Consistency);
        VM_SettingsModManager.DumpViewModelToModel(PatcherSettings.ModManagerIntegration, _modManager);
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

        SettingsIO_Misc.SaveSettingsSource(General, out saveSuccess, out exceptionStr);
        if (!saveSuccess) 
        {
            captionStr = "Error saving Load Source Settings: ";
            _logger.LogError(captionStr + exceptionStr); allExceptions += captionStr + exceptionStr + Environment.NewLine; showFinalExceptions = true;
        }

        if (showFinalExceptions)
        {
            string notificationStr = allExceptions;
            CustomMessageBox.DisplayNotificationOK("Errors were encountered upon closing", notificationStr);
        }
    }
}