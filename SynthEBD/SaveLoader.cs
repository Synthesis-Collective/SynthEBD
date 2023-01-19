using Mutagen.Bethesda.Skyrim;
using ReactiveUI;
using System.Reactive.Linq;
using System.Security.Cryptography.X509Certificates;
using static SynthEBD.VM_BodyShapeDescriptor;
using static SynthEBD.VM_NPCAttribute;

namespace SynthEBD;

public class SaveLoader
{
    private readonly IEnvironmentStateProvider _environmentProvider;
    private readonly PatcherSettingsSourceProvider _patcherSettingsSourceProvider;
    private readonly PatcherEnvironmentSourceProvider _patcherEnvironmentSourceProvider;
    private readonly PatcherState _patcherState;
    private readonly Logger _logger;
    private readonly SynthEBDPaths _paths;
   
    private readonly PatchableRaceResolver _raceResolver;
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

    public SaveLoader(
        IEnvironmentStateProvider environmentProvider,
        PatcherState patcherState,
        PatcherSettingsSourceProvider patcherSettingsSourceProvider,
        PatcherEnvironmentSourceProvider patcherEnvironmentSourceProvider,
        Logger logger,
        SynthEBDPaths paths,
        PatchableRaceResolver raceResolver,
        SettingsIO_Misc miscIO,
        SettingsIO_General generalIO,
        SettingsIO_AssetPack assetIO,
        SettingsIO_BodyGen bodyGenIO,
        SettingsIO_OBody oBodyIO,
        SettingsIO_HeadParts headpartIO,
        SettingsIO_Height heightIO,
        SettingsIO_BlockList blockListIO,
        SettingsIO_ModManager modManagerIO,
        SettingsIO_SpecificNPCAssignments specificNPCassignmentsIO)
    {
        _environmentProvider = environmentProvider;
        _patcherState = patcherState;
        _logger = logger;
        _paths = paths;
        _patcherSettingsSourceProvider = patcherSettingsSourceProvider;
        _patcherEnvironmentSourceProvider = patcherEnvironmentSourceProvider;
        _raceResolver = raceResolver;
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
        _raceResolver.ResolvePatchableRaces(); // Initialize patchable races from general settings (required by some UI elements)
        _patcherState.TexMeshSettings = _assetIO.LoadTexMeshSettings(out loadSuccess); // Load texture and mesh settings
        _patcherState.BodyGenSettings = _bodyGenIO.LoadBodyGenSettings(out loadSuccess);
        _patcherState.OBodySettings = _oBodyIO.LoadOBodySettings(out loadSuccess);
        // load OBody settings before asset packs - asset packs depend on BodyGen but not vice versa
        _patcherState.OBodySettings.ImportBodySlides(_patcherState.OBodySettings.TemplateDescriptors, _oBodyIO, _environmentProvider.DataFolderPath);
        _patcherState.HeadPartSettings = _headpartIO.LoadHeadPartSettings(out loadSuccess); // load head part settings
        _patcherState.HeightSettings = _heightIO.LoadHeightSettings(out loadSuccess); // load heights
        _patcherState.BlockList = _blockListIO.LoadBlockList(out loadSuccess); // load BlockList
        _patcherState.ModManagerSettings = _modManagerIO.LoadModManagerSettings(out loadSuccess); // load Mod Manager Integration
    }

    public void LoadPlugins()
    {
        // load bodygen configs before asset packs - asset packs depend on BodyGen but not vice versa
        _patcherState.BodyGenConfigs = _bodyGenIO.LoadBodyGenConfigs(_patcherState.GeneralSettings.RaceGroupings, out var loadSuccess);
        _patcherState.RecordTemplatePlugins = _assetIO.LoadRecordTemplates(out loadSuccess);
        _patcherState.RecordTemplateLinkCache = _patcherState.RecordTemplatePlugins.ToImmutableLinkCache();
        _patcherState.AssetPacks = _assetIO.LoadAssetPacks(_patcherState.GeneralSettings.RaceGroupings, _patcherState.RecordTemplatePlugins, _patcherState.BodyGenConfigs, out loadSuccess); // load asset pack models from json
        _patcherState.HeightConfigs = _heightIO.LoadHeightConfigs(out loadSuccess); // load heights
    }

    public void LoadMetaSettings()
    {
        _patcherState.SpecificNPCAssignments = _specificNPCassignmentsIO.LoadAssignments(out var loadSuccess);
        _patcherState.Consistency = _miscIO.LoadConsistency(out loadSuccess);
    }

    public void SaveStateToDrive()
    {
        bool saveSuccess;
        string captionStr;
        string exceptionStr;
        string allExceptions = "";
        bool showFinalExceptions = false;

        JSONhandler<Settings_General>.SaveJSONFile(_patcherState.GeneralSettings, _paths.GeneralSettingsPath, out saveSuccess, out exceptionStr);
        if (!saveSuccess)
        {
            captionStr = "Error saving General Settings: ";
            _logger.LogError(captionStr + exceptionStr); allExceptions += captionStr + exceptionStr + Environment.NewLine; showFinalExceptions = true;
        }

        JSONhandler<Settings_TexMesh>.SaveJSONFile(_patcherState.TexMeshSettings, _paths.TexMeshSettingsPath, out saveSuccess, out exceptionStr);
        if (!saveSuccess) 
        {
            captionStr = "Error saving Texture and Mesh Settings: ";
            _logger.LogError(captionStr + exceptionStr); allExceptions += captionStr + exceptionStr + Environment.NewLine; showFinalExceptions = true;
        }

        _assetIO.SaveAssetPacks(_patcherState.AssetPacks, out saveSuccess);
        if (!saveSuccess) 
        {
            captionStr = "Error saving Asset Packs: ";
            _logger.LogError(captionStr); allExceptions += captionStr + Environment.NewLine; showFinalExceptions = true;
        }

        JSONhandler<Settings_Height>.SaveJSONFile(_patcherState.HeightSettings, _paths.HeightSettingsPath, out saveSuccess, out exceptionStr);
        if (!saveSuccess) 
        {
            captionStr = "Error saving Height Settings: ";
            _logger.LogError(captionStr + exceptionStr); allExceptions += captionStr + exceptionStr + Environment.NewLine; showFinalExceptions = true;
        }

        _heightIO.SaveHeightConfigs(_patcherState.HeightConfigs, out saveSuccess);
        if (!saveSuccess) 
        {
            captionStr = "Error saving Height Configs: ";
            _logger.LogError(captionStr); allExceptions += captionStr + Environment.NewLine; showFinalExceptions = true;
        }

        JSONhandler<Settings_BodyGen>.SaveJSONFile(_patcherState.BodyGenSettings, _paths.BodyGenSettingsPath, out saveSuccess, out exceptionStr);
        if (!saveSuccess) 
        {
            captionStr = "Error saving BodyGen Settings: ";
            _logger.LogError(captionStr + exceptionStr); allExceptions += captionStr + exceptionStr + Environment.NewLine; showFinalExceptions = true;
        }

        _bodyGenIO.SaveBodyGenConfigs(_patcherState.BodyGenConfigs.Female, out saveSuccess);
        if (!saveSuccess) 
        {
            captionStr = "Error saving BodyGen configs";
            _logger.LogError(captionStr); allExceptions += captionStr + Environment.NewLine; showFinalExceptions = true;
        }

        _bodyGenIO.SaveBodyGenConfigs(_patcherState.BodyGenConfigs.Male, out saveSuccess);
        if (!saveSuccess) 
        {
            captionStr = "Error saving BodyGen configs";
            _logger.LogError(captionStr); allExceptions += captionStr + Environment.NewLine; showFinalExceptions = true;
        }

        JSONhandler<Settings_OBody>.SaveJSONFile(_patcherState.OBodySettings, _paths.OBodySettingsPath, out saveSuccess, out exceptionStr);
        if (!saveSuccess) 
        {
            captionStr = "Error saving OBody/AutoBody Settings: ";
            _logger.LogError(captionStr + exceptionStr); allExceptions += captionStr + exceptionStr + Environment.NewLine; showFinalExceptions = true;
        }

        JSONhandler<Settings_Headparts>.SaveJSONFile(_patcherState.HeadPartSettings, _paths.HeadPartsSettingsPath, out saveSuccess, out exceptionStr);
        if (!saveSuccess) 
        {
            captionStr = "Error saving Head Parts Settings: ";
            _logger.LogError(captionStr + exceptionStr); allExceptions += captionStr + exceptionStr + Environment.NewLine; showFinalExceptions = true;
        }

        _miscIO.SaveConsistency(_patcherState.Consistency, out saveSuccess);
        if (!saveSuccess) 
        {
            captionStr = "Error saving Consistency";
            _logger.LogError(captionStr); allExceptions += captionStr + Environment.NewLine; showFinalExceptions = true;
        }

        _specificNPCassignmentsIO.SaveAssignments(_patcherState.SpecificNPCAssignments, out saveSuccess);
        if (!saveSuccess) 
        {
            captionStr = "Error saving Specific NPC Assignments";
            _logger.LogError(captionStr); allExceptions += captionStr + Environment.NewLine; showFinalExceptions = true;
        }

        _blockListIO.SaveBlockList(_patcherState.BlockList, out saveSuccess);
        if (!saveSuccess) 
        {
            captionStr = "Error saving Block List";
            _logger.LogError(captionStr); allExceptions += captionStr + Environment.NewLine; showFinalExceptions = true;
        }

        JSONhandler<Settings_ModManager>.SaveJSONFile(_patcherState.ModManagerSettings, _paths.ModManagerSettingsPath, out saveSuccess, out exceptionStr);
        if (!saveSuccess) 
        {
            captionStr = "Error saving Mod Manager Integration Settings: ";
            _logger.LogError(captionStr + exceptionStr); allExceptions += captionStr + exceptionStr + Environment.NewLine; showFinalExceptions = true;
        }

        _patcherSettingsSourceProvider.SaveSettingsSource(out saveSuccess, out exceptionStr);
        if (!saveSuccess) 
        {
            captionStr = "Error saving Load Source Settings: ";
            _logger.LogError(captionStr + exceptionStr); allExceptions += captionStr + exceptionStr + Environment.NewLine; showFinalExceptions = true;
        }

        if (_environmentProvider.RunMode == EnvironmentMode.Standalone)
        {
            var envSource = new StandaloneEnvironmentSource()
            {
                GameEnvironmentDirectory = _environmentProvider.DataFolderPath,
                SkyrimVersion = _environmentProvider.SkyrimVersion,
                OutputModName = _environmentProvider.OutputModName
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
        _miscIO.SaveConsistency(_patcherState.Consistency, out _);
    }
}