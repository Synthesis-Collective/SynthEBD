using Mutagen.Bethesda.Skyrim;

namespace SynthEBD;

public class SaveLoader
{
    // Some are public properties to allow for circular IoC dependencies
    private readonly MainState _state;
    private readonly VM_AssetPack.Factory _assetPackFactory;
    public VM_Settings_General General { get; set; }
    public VM_SettingsTexMesh TexMesh { get; set; }
    private readonly VM_SettingsHeight _settingsHeight;
    public VM_SettingsBodyGen BodyGen { get; set; }
    private readonly VM_SettingsModManager _modManager;
    private readonly VM_SettingsOBody _oBody;
    private readonly VM_ConsistencyUI _consistencyUi;
    private readonly VM_BlockListUI _blockList;
    private readonly VM_BodyGenConfig.Factory _bodyGenConfigFactory;
    private readonly VM_SpecificNPCAssignment.Factory _specificNpcAssignmentFactory;
    private readonly VM_SpecificNPCAssignmentsUI _npcAssignmentsUi;

    public SaveLoader(
        MainState state,
        VM_AssetPack.Factory assetPackFactory,
        VM_SettingsHeight settingsHeight,
        VM_SettingsModManager modManager,
        VM_SettingsOBody oBody,
        VM_ConsistencyUI consistencyUi,
        VM_BlockListUI blockList,
        VM_BodyGenConfig.Factory bodyGenConfigFactory,
        VM_SpecificNPCAssignment.Factory specificNpcAssignmentFactory,
        VM_SpecificNPCAssignmentsUI npcAssignmentsUi)
    {
        _state = state;
        _assetPackFactory = assetPackFactory;
        _settingsHeight = settingsHeight;
        _modManager = modManager;
        _oBody = oBody;
        _consistencyUi = consistencyUi;
        _blockList = blockList;
        _bodyGenConfigFactory = bodyGenConfigFactory;
        _specificNpcAssignmentFactory = specificNpcAssignmentFactory;
        _npcAssignmentsUi = npcAssignmentsUi;
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
        VM_HeightConfig.DumpViewModelsToModels(_settingsHeight.AvailableHeightConfigs, _state.HeightConfigs);
        VM_SettingsBodyGen.DumpViewModelToModel(BodyGen, PatcherSettings.BodyGen, _state.BodyGenConfigs);
    }
    
    public void LoadInitialSettingsViewModels() // view models that should be loaded before plugin VMs
    {
        // Load general settings
        SettingsIO_General.LoadGeneralSettings(out var loadSuccess);
        VM_Settings_General.GetViewModelFromModel(General);

        // Initialize patchable races from general settings (required by some UI elements)
        Patcher.ResolvePatchableRaces();

        // Load texture and mesh settings
        PatcherSettings.TexMesh = SettingsIO_AssetPack.LoadTexMeshSettings(out loadSuccess);
        VM_SettingsTexMesh.GetViewModelFromModel(TexMesh, PatcherSettings.TexMesh);

        PatcherSettings.BodyGen = SettingsIO_BodyGen.LoadBodyGenSettings(out loadSuccess);

        // load OBody settings before asset packs - asset packs depend on BodyGen but not vice versa
        PatcherSettings.OBody = SettingsIO_OBody.LoadOBodySettings(out loadSuccess);
        PatcherSettings.OBody.ImportBodySlides(PatcherSettings.OBody.TemplateDescriptors);

        // load heights
        PatcherSettings.Height = SettingsIO_Height.LoadHeightSettings(out loadSuccess);

        // load BlockList
        _state.BlockList = SettingsIO_BlockList.LoadBlockList(out loadSuccess);
        VM_BlockListUI.GetViewModelFromModel(_state.BlockList, _blockList);

        // load Mod Manager Integration
        PatcherSettings.ModManagerIntegration = SettingsIO_ModManager.LoadModManagerSettings(out loadSuccess);
        VM_SettingsModManager.GetViewModelFromModel(PatcherSettings.ModManagerIntegration, _modManager);
    }

    public void LoadPluginViewModels()
    {
        // load bodygen configs before asset packs - asset packs depend on BodyGen but not vice versa
        _state.BodyGenConfigs = SettingsIO_BodyGen.LoadBodyGenConfigs(PatcherSettings.General.RaceGroupings, out var loadSuccess);
        VM_SettingsBodyGen.GetViewModelFromModel(_state.BodyGenConfigs, PatcherSettings.BodyGen, BodyGen, _bodyGenConfigFactory, General);

        VM_SettingsOBody.GetViewModelFromModel(PatcherSettings.OBody, _oBody, General.RaceGroupings);

        _state.RecordTemplatePlugins = SettingsIO_AssetPack.LoadRecordTemplates(out loadSuccess);
        _state.RecordTemplateLinkCache = _state.RecordTemplatePlugins.ToImmutableLinkCache();

        // load asset packs
        _state.AssetPacks = SettingsIO_AssetPack.LoadAssetPacks(PatcherSettings.General.RaceGroupings, _state.RecordTemplatePlugins, _state.BodyGenConfigs, out loadSuccess); // load asset pack models from json
        VM_AssetPack.GetViewModelsFromModels(_state.AssetPacks, TexMesh, PatcherSettings.TexMesh, _assetPackFactory); // add asset pack view models to TexMesh shell view model here
        TexMesh.AssetPresenterPrimary.AssetPack = TexMesh.AssetPacks.Where(x => x.GroupName == TexMesh.LastViewedAssetPackName).FirstOrDefault();

        // load heights
        _state.HeightConfigs = SettingsIO_Height.LoadHeightConfigs(out loadSuccess);

        VM_HeightConfig.GetViewModelsFromModels(_settingsHeight.AvailableHeightConfigs, _state.HeightConfigs);
        VM_SettingsHeight.GetViewModelFromModel(_settingsHeight, PatcherSettings.Height); /// must do after populating configs
    }

    public void LoadFinalSettingsViewModels() // view models that should be loaded after plugin VMs because they depend on the loaded plugins
    {
        // load specific assignments (must load after plugin view models)
        _state.SpecificNPCAssignments = SettingsIO_SpecificNPCAssignments.LoadAssignments(out var loadSuccess);
        VM_SpecificNPCAssignmentsUI.GetViewModelFromModels(
            _assetPackFactory,
            TexMesh,
            BodyGen,
            _specificNpcAssignmentFactory,
            _npcAssignmentsUi,
            _state.SpecificNPCAssignments);

        // Load Consistency (must load after plugin view models)
        _state.Consistency = SettingsIO_Misc.LoadConsistency(out loadSuccess);
        VM_ConsistencyUI.GetViewModelsFromModels(_state.Consistency, _consistencyUi.Assignments, TexMesh.AssetPacks);
    }

    public void DumpViewModelsToModels()
    {
        VM_Settings_General.DumpViewModelToModel(General, PatcherSettings.General);
        VM_SettingsTexMesh.DumpViewModelToModel(TexMesh, PatcherSettings.TexMesh);
        VM_AssetPack.DumpViewModelsToModels(TexMesh.AssetPacks, _state.AssetPacks);
        VM_SettingsHeight.DumpViewModelToModel(_settingsHeight, PatcherSettings.Height);
        VM_HeightConfig.DumpViewModelsToModels(_settingsHeight.AvailableHeightConfigs, _state.HeightConfigs);
        VM_SettingsBodyGen.DumpViewModelToModel(BodyGen, PatcherSettings.BodyGen, _state.BodyGenConfigs);
        VM_SettingsOBody.DumpViewModelToModel(PatcherSettings.OBody, _oBody);
        VM_SpecificNPCAssignmentsUI.DumpViewModelToModels(_npcAssignmentsUi, _state.SpecificNPCAssignments);
        VM_BlockListUI.DumpViewModelToModel(_blockList, _state.BlockList);
        VM_ConsistencyUI.DumpViewModelsToModels(_consistencyUi.Assignments, _state.Consistency);
        VM_SettingsModManager.DumpViewModelToModel(PatcherSettings.ModManagerIntegration, _modManager);
    }

    public void FinalClosing()
    {
        DumpViewModelsToModels();

        bool saveSuccess;
        string exceptionStr;
        string allExceptions = "";
        bool showFinalExceptions = false;

        JSONhandler<Settings_General>.SaveJSONFile(PatcherSettings.General, PatcherSettings.Paths.GeneralSettingsPath, out saveSuccess, out exceptionStr);
        if (!saveSuccess) { Logger.LogMessage("Error saving General Settings: " + exceptionStr); allExceptions += exceptionStr + Environment.NewLine; showFinalExceptions = true; }

        JSONhandler<Settings_TexMesh>.SaveJSONFile(PatcherSettings.TexMesh, PatcherSettings.Paths.TexMeshSettingsPath, out saveSuccess, out exceptionStr);
        if (!saveSuccess) { Logger.LogMessage("Error saving Texture and Mesh Settings: " + exceptionStr); allExceptions += exceptionStr + Environment.NewLine; showFinalExceptions = true; }
            
        SettingsIO_AssetPack.SaveAssetPacks(_state.AssetPacks, out saveSuccess);
        if (!saveSuccess) { allExceptions += exceptionStr + Environment.NewLine; showFinalExceptions = true; }

        JSONhandler<Settings_Height>.SaveJSONFile(PatcherSettings.Height, PatcherSettings.Paths.HeightSettingsPath, out saveSuccess, out exceptionStr);
        if (!saveSuccess) { Logger.LogMessage("Error saving Height Settings: " + exceptionStr); allExceptions += exceptionStr + Environment.NewLine; showFinalExceptions = true; }
            
        SettingsIO_Height.SaveHeightConfigs(_state.HeightConfigs, out saveSuccess);
        if (!saveSuccess) { allExceptions += exceptionStr + Environment.NewLine; showFinalExceptions = true; }

        JSONhandler<Settings_BodyGen>.SaveJSONFile(PatcherSettings.BodyGen, PatcherSettings.Paths.BodyGenSettingsPath, out saveSuccess, out exceptionStr);
        if (!saveSuccess) { Logger.LogMessage("Error saving BodyGen Settings: " + exceptionStr); allExceptions += exceptionStr + Environment.NewLine; showFinalExceptions = true; }
            
        SettingsIO_BodyGen.SaveBodyGenConfigs(_state.BodyGenConfigs.Female, out saveSuccess);
        if (!saveSuccess) { allExceptions += "Error saving BodyGen configs" + Environment.NewLine; showFinalExceptions = true; }

        SettingsIO_BodyGen.SaveBodyGenConfigs(_state.BodyGenConfigs.Male, out saveSuccess);
        if (!saveSuccess) { allExceptions += "Error saving BodyGen configs" + Environment.NewLine; showFinalExceptions = true; }

        JSONhandler<Settings_OBody>.SaveJSONFile(PatcherSettings.OBody, PatcherSettings.Paths.OBodySettingsPath, out saveSuccess, out exceptionStr);
        if (!saveSuccess) { Logger.LogMessage("Error saving OBody/AutoBody Settings: " + exceptionStr); allExceptions += exceptionStr + Environment.NewLine; showFinalExceptions = true; }

        SettingsIO_Misc.SaveConsistency(_state.Consistency, out saveSuccess);
        if (!saveSuccess) { allExceptions += "Error saving Consistency" + Environment.NewLine; showFinalExceptions = true; }

        SettingsIO_SpecificNPCAssignments.SaveAssignments(_state.SpecificNPCAssignments, out saveSuccess);
        if (!saveSuccess) { allExceptions += "Error saving Specific NPC Assignmentss" + Environment.NewLine; showFinalExceptions = true; }

        SettingsIO_BlockList.SaveBlockList(_state.BlockList, out saveSuccess);
        if (!saveSuccess) { allExceptions += "Error saving Block List" + Environment.NewLine; showFinalExceptions = true; }

        JSONhandler<Settings_ModManager>.SaveJSONFile(PatcherSettings.ModManagerIntegration, PatcherSettings.Paths.ModManagerSettingsPath, out saveSuccess, out exceptionStr);
        if (!saveSuccess) { Logger.LogMessage("Error saving Mod Manager Integration Settings: " + exceptionStr); allExceptions += exceptionStr + Environment.NewLine; showFinalExceptions = true; }

        SettingsIO_Misc.SaveSettingsSource(General, out saveSuccess, out exceptionStr);
        if (!saveSuccess) { allExceptions += exceptionStr + Environment.NewLine; showFinalExceptions = true; }

        if (showFinalExceptions)
        {
            string notificationStr = allExceptions;
            CustomMessageBox.DisplayNotificationOK("Errors were encountered upon closing", notificationStr);
        }
    }
}