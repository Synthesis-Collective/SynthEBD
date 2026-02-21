using System.Windows.Documents;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Skyrim;
using Mutagen.Bethesda.FormKeys.SkyrimSE;
using Mutagen.Bethesda.Plugins.Records;
using Noggog;

namespace SynthEBD;

public class Patcher
{
    private readonly IOutputEnvironmentStateProvider _environmentProvider;
    private readonly PatcherState _patcherState;
    private readonly VM_StatusBar _statusBar;
    private readonly CombinationLog _combinationLog;
    private readonly SynthEBDPaths _paths;
    private readonly Logger _logger;
    public readonly PatchableRaceResolver _raceResolver;
    private readonly VerboseLoggingNPCSelector _verboseModeNPCSelector;
    private readonly AssetAndBodyShapeSelector _assetAndBodyShapeSelector;
    private readonly AssetSelector _assetSelector;
    private readonly AssetReplacerSelector _assetReplacerSelector;
    private readonly RecordGenerator _recordGenerator;
    private readonly RecordPathParser _recordPathParser;
    private readonly BodyGenPreprocessing _bodyGenPreprocessing;
    private readonly BodyGenSelector _bodyGenSelector;
    private readonly BodyGenWriter _bodyGenWriter;
    private readonly HeightPatcher _heightPatcher;
    private readonly OBodyPreprocessing _oBodyPreprocessing;
    private readonly OBodySelector _oBodySelector;
    private readonly OBodyWriter _oBodyWriter;
    private readonly HeadPartPreprocessing _headPartPreprocessing;
    private readonly HeadPartSelector _headPartSelector;
    private readonly HeadPartWriter _headPartWriter;
    private readonly CommonScripts _commonScripts;
    private readonly FaceTextureScriptWriter _faceTextureScriptWriter;
    private readonly EBDScripts _EBDScripts;
    private readonly JContainersDomain _jContainersDomain;
    private readonly QuestInit _questInit;
    private readonly DictionaryMapper _dictionaryMapper;
    private readonly UpdateHandler _updateHandler;
    private readonly MiscValidation _miscValidation;
    private readonly NPCInfo.Factory _npcInfoFactory;
    private readonly VanillaBodyPathSetter _vanillaBodyPathSetter;
    private readonly UniqueNPCData _uniqueNPCData;
    private readonly Converters _converters;
    private readonly BodySlideAnnotator _bodySlideAnnotator;
    private readonly HeadPartAuxFunctions _headPartAuxFunctions;
    private readonly EasyNPCProfileParser _easyNPCProfileParser;
    private readonly NPC2ProfileParser _npc2ProfileParser;
    private readonly NPCProvider _npcProvider;
    private readonly SkyPatcherInterface _skyPatcherInterface;
    private readonly AssetAssignmentJsonDictHandler _assetAssignmentJsonDictHandler;
    private readonly FaceGenPatcher _faceGenPatcher;

    private Dictionary<NPCInfo, List<SelectedAssetContainer>> _assetAssignmentTransfers = new(); // Storage for moving assignments between selection (to be parallelized) and application (serial).
    private Dictionary<NPCInfo, Dictionary<HeadPart.TypeEnum, FormKey>> _assignedHeadPartTransfers = new(); // for moving assignments between selection (to be parallelized) and application (serial). 
    private Dictionary<NPCInfo, float> _heightAssignmentTransfers = new(); // storage for moving assignments between selection and application
    
    private AssetStatsTracker _assetsStatsTracker { get; set; }
    private int _patchedNpcCount { get; set; }

    public Patcher(IOutputEnvironmentStateProvider environmentProvider, PatcherState patcherState, VM_StatusBar statusBar, CombinationLog combinationLog, SynthEBDPaths paths, Logger logger, PatchableRaceResolver raceResolver, VerboseLoggingNPCSelector verboseModeNPCSelector, AssetAndBodyShapeSelector assetAndBodyShapeSelector, AssetSelector assetSelector, AssetReplacerSelector assetReplacerSelector, RecordGenerator recordGenerator, RecordPathParser recordPathParser, BodyGenPreprocessing bodyGenPreprocessing, BodyGenSelector bodyGenSelector, BodyGenWriter bodyGenWriter, HeightPatcher heightPatcher, OBodyPreprocessing oBodyPreprocessing, OBodySelector oBodySelector, OBodyWriter oBodyWriter, HeadPartPreprocessing headPartPreProcessing, HeadPartSelector headPartSelector, HeadPartWriter headPartWriter, HeadPartAuxFunctions headPartAuxFunctions, CommonScripts commonScripts, FaceTextureScriptWriter faceTextureScriptWriter, EBDScripts ebdScripts, JContainersDomain jContainersDomain, QuestInit questInit, DictionaryMapper dictionaryMapper, UpdateHandler updateHandler, MiscValidation miscValidation, PatcherIO patcherIO, NPCInfo.Factory npcInfoFactory, VanillaBodyPathSetter vanillaBodyPathSetter, UniqueNPCData uniqueNPCData, Converters converters, BodySlideAnnotator bodySlideAnnotator, EasyNPCProfileParser easyNPCProfileParser, NPC2ProfileParser npc2ProfileParser, NPCProvider npcProvider, SkyPatcherInterface skyPatcherInterface, AssetAssignmentJsonDictHandler assetAssignmentJsonDictHandler, FaceGenPatcher faceGenPatcher)
    {
        _environmentProvider = environmentProvider;
        _patcherState = patcherState;
        _statusBar = statusBar;
        _combinationLog = combinationLog;
        _paths = paths;
        _logger = logger;
        _raceResolver = raceResolver;
        _verboseModeNPCSelector = verboseModeNPCSelector;
        _assetAndBodyShapeSelector = assetAndBodyShapeSelector;
        _assetSelector = assetSelector;
        _assetReplacerSelector = assetReplacerSelector;
        _recordGenerator = recordGenerator;
        _recordPathParser = recordPathParser;
        _bodyGenPreprocessing = bodyGenPreprocessing;
        _bodyGenSelector = bodyGenSelector;
        _bodyGenWriter = bodyGenWriter;
        _heightPatcher = heightPatcher;
        _oBodyPreprocessing = oBodyPreprocessing;
        _oBodySelector = oBodySelector;
        _oBodyWriter = oBodyWriter;
        _headPartPreprocessing = headPartPreProcessing;
        _headPartSelector = headPartSelector;
        _headPartWriter = headPartWriter;
        _headPartAuxFunctions = headPartAuxFunctions;
        _commonScripts = commonScripts;
        _faceTextureScriptWriter = faceTextureScriptWriter;
        _EBDScripts = ebdScripts;
        _jContainersDomain = jContainersDomain;
        _questInit = questInit;
        _dictionaryMapper = dictionaryMapper;
        _updateHandler = updateHandler;
        _miscValidation = miscValidation;    
        _npcInfoFactory = npcInfoFactory;  
        _vanillaBodyPathSetter = vanillaBodyPathSetter;
        _uniqueNPCData = uniqueNPCData;
        _converters = converters;
        _bodySlideAnnotator = bodySlideAnnotator;
        _easyNPCProfileParser = easyNPCProfileParser;
        _npc2ProfileParser = npc2ProfileParser;
        _npcProvider = npcProvider;
        _skyPatcherInterface = skyPatcherInterface;
        _assetAssignmentJsonDictHandler = assetAssignmentJsonDictHandler;
        _faceGenPatcher = faceGenPatcher;

        _assetsStatsTracker = new(_patcherState, _logger, _environmentProvider.LinkCache);
    }

    //Synchronous version for debugging only
    //public static void RunPatcher(List<AssetPack> assetPacks, BodyGenConfigs bodyGenConfigs, List<HeightConfig> heightConfigs, Dictionary<string, NPCAssignment> consistency, HashSet<NPCAssignment> specificNPCAssignments, BlockList blockList, HashSet<string> linkedNPCNameExclusions, HashSet<LinkedNPCGroup> linkedNPCGroups, ILinkCache<ISkyrimMod, ISkyrimModGetter> recordTemplateLinkCache, List<SkyrimMod> recordTemplatePlugins, VM_StatusBar statusBar)
    public async Task RunPatcher()
    {
        // General pre-patching tasks: 
        if (!_patcherState.GeneralSettings.bUIopened)
        {
            _logger.LogMessage("====SynthEBD UI has never been opened. SynthEBD will not do anything until the UI is configured====");
        }

        if (_paths.OutputDataFolder.IsNullOrEmpty())
        {
            if (_patcherState.GeneralSettings.OutputDataFolder.IsNullOrEmpty())
            {
                _paths.OutputDataFolder = _environmentProvider.DataFolderPath;
            }
            else
            {
                _paths.OutputDataFolder = _patcherState.GeneralSettings.OutputDataFolder;
            }
        }

        var outputMod = _environmentProvider.OutputMod;
        var allNPCs = _environmentProvider.LoadOrder.PriorityOrder.OnlyEnabledAndExisting().WinningOverrides<INpcGetter>().OrderBy(x => _converters.FormKeyStringToFormIDString(x.FormKey.ToString())).ToArray();
        _raceResolver.ResolvePatchableRaces();
        _uniqueNPCData.Reinitialize();
        HashSet<LinkedNPCGroupInfo> generatedLinkGroups = new HashSet<LinkedNPCGroupInfo>();
        HashSet<INpcGetter> skippedLinkedNPCs = new HashSet<INpcGetter>();

        // Script copying: All scripts are copied to the output folder even if the respective patcher functionality is unused. Script activity is controlled by a global variable. This prevents potential nastiness from missing script files if user toggles patcher functionalities
        _commonScripts.CopyAllToOutputFolder();
        _oBodyWriter.CopyBodySlideScript();
        _headPartWriter.CopyHeadPartScript();
        _jContainersDomain.CreateSynthEBDDomain();
        _questInit.WriteQuestSeqFile();

        // UI Pre-patching tasks:
        _logger.UpdateStatus("Patching", false);
        _logger.StartTimer();
        _logger.PatcherExecutionStart = DateTime.Now;
        _statusBar.IsPatching = true;

        // Asset Pre-patching tasks:
        _assetsStatsTracker = new(_patcherState, _logger, _environmentProvider.LinkCache);
        _assetAssignmentJsonDictHandler.Reinitialize();
        _assetAssignmentTransfers = new();
        
        var assetPacks = _patcherState.AssetPacks
            .Where(x => _patcherState.TexMeshSettings.SelectedAssetPacks.Contains(x.GroupName))
            .Select(x => JSONhandler<AssetPack>.CloneViaJSON(x))
            .ToList();
        CategorizedFlattenedAssetPacks availableAssetPacks = null;
        Keyword EBDFaceKW = null;
        Keyword EBDScriptKW = null;
        Spell EBDHelperSpell = null;
        Keyword synthEBDFaceKW = null;
        Spell synthEBDHelperSpell = null;
        GlobalShort gEnableTextureLoaderScript = null;
        GlobalShort gFaceTextureVerboseMode = null;

        bool useFaceMeshMode = _patcherState.TexMeshSettings.FacePatchingMode == FacePatchingMode.Mesh;

        if (!useFaceMeshMode)
        {
            // write resources for EBD face texture script even if it will be superceded by the updated version
            EBDCoreRecords.CreateCoreRecords(outputMod, out EBDFaceKW, out EBDScriptKW, out EBDHelperSpell, _patcherState.TexMeshSettings.bLegacyEBDMode);

            // write resources for new face texture script even if it will be deactivated
            GlobalShort gEnableFaceTextureScript;
            (synthEBDFaceKW, gEnableFaceTextureScript, gFaceTextureVerboseMode) = _faceTextureScriptWriter.InitializeToggleRecords(outputMod);
            _faceTextureScriptWriter.CopyFaceTextureScript();
            synthEBDHelperSpell = _faceTextureScriptWriter.CreateSynthEBDFaceTextureSpell(outputMod, synthEBDFaceKW, gEnableFaceTextureScript, gFaceTextureVerboseMode, _patcherState.TexMeshSettings.TriggerEvents);
            
            gEnableTextureLoaderScript = outputMod.Globals.AddNewShort();
            gEnableTextureLoaderScript.EditorID = "SynthEBD_TextureLoaderScriptActive";
            gEnableTextureLoaderScript.Data = Convert.ToInt16(_patcherState.GeneralSettings.bChangeMeshesOrTextures && _patcherState.TexMeshSettings.bPureScriptMode);
        }
        
        HashSet<FlattenedAssetPack> flattenedAssetPacks = new();
        if (_patcherState.GeneralSettings.bChangeMeshesOrTextures)
        {
            UpdateRecordTemplateAdditonalRaces(assetPacks, _patcherState.RecordTemplateLinkCache, _patcherState.RecordTemplatePlugins);
            flattenedAssetPacks = assetPacks.Select(x => FlattenedAssetPack.FlattenAssetPack(x, _dictionaryMapper, _patcherState)).ToHashSet();
            PathTrimmer.TrimFlattenedAssetPacks(flattenedAssetPacks, _patcherState.TexMeshSettings.TrimPaths.ToHashSet());
            availableAssetPacks = new CategorizedFlattenedAssetPacks(flattenedAssetPacks);

            if (!useFaceMeshMode)
            {
                if (_patcherState.TexMeshSettings.bLegacyEBDMode)
                {
                    ApplyRacialSpell.ApplySpell(outputMod, EBDHelperSpell, _environmentProvider.LinkCache, _patcherState);
                }
                else
                {
                    ApplyRacialSpell.ApplySpell(outputMod, synthEBDHelperSpell, _environmentProvider.LinkCache, _patcherState);
                }

                if (_patcherState.TexMeshSettings.bApplyFixedScripts) { _EBDScripts.ApplyFixedScripts(); }
                
                _assetAssignmentJsonDictHandler.CreateTextureLoaderQuest(_environmentProvider.OutputMod, gEnableTextureLoaderScript, gFaceTextureVerboseMode);
            }

            _assetSelector.Reinitialize();
            _recordGenerator.Reinitialize();
            _combinationLog.Reinitialize();
        }

        _npcProvider.Reinitialize();

        // BodyGen Pre-patching tasks:
        BodyGenTracker = new BodyGenAssignmentTracker();

        // BodySlide Pre-patching tasks:
        _oBodyWriter.ClearOutputForJsonMode();
        var gEnableBodySlideScript = outputMod.Globals.AddNewShort();
        gEnableBodySlideScript.EditorID = "SynthEBD_BodySlideScriptActive";
        gEnableBodySlideScript.Data = 0; // default to 0; patcher will change later if one of several conditions are met
        var gBodySlideVerboseMode = outputMod.Globals.AddNewShort();
        gBodySlideVerboseMode.EditorID = "SynthEBD_BodySlideVerboseMode";
        gBodySlideVerboseMode.Data = Convert.ToInt16(_patcherState.OBodySettings.bUseVerboseScripts);
        _oBodyWriter.CreateBodySlideLoaderQuest(outputMod, gEnableBodySlideScript, gBodySlideVerboseMode);
        Spell bodySlideAssignmentSpell = _oBodyWriter.CreateOBodyAssignmentSpell(outputMod, gBodySlideVerboseMode);

        // Mutual BodyGen/BodySlide Pre-patching tasks:
        // Several operations are performed that mutate the input settings. For Asset Packs this does not affect saved settings because the operations are performed on the derived FlattenedAssetPacks, but for BodyGen configs and OBody settings these are made directly to the settings files. Therefore, create a deep copy of the configs and operate on those to avoid altering the user's saved settings upon exiting the program
        bool serializationSuccess, deserializationSuccess;
        string serializatonException, deserializationException;

        BodyGenConfigs copiedBodyGenConfigs = JSONhandler<BodyGenConfigs>.Deserialize(JSONhandler<BodyGenConfigs>.Serialize(_patcherState.BodyGenConfigs, out serializationSuccess, out serializatonException), out deserializationSuccess, out deserializationException);
        if (!serializationSuccess) { _logger.LogMessage("Error serializing BodyGen configs. Exception: " + serializatonException); _logger.LogErrorWithStatusUpdate("Patching aborted.", ErrorType.Error); return; }
        if (!deserializationSuccess) { _logger.LogMessage("Error deserializing BodyGen configs. Exception: " + deserializationException); _logger.LogErrorWithStatusUpdate("Patching aborted.", ErrorType.Error); return; }

        Settings_OBody copiedOBodySettings = _patcherState.OBodySettings.DeepCopyByExpressionTree(); // can't copy via json because some needed properties are JSONignored.

        if (_patcherState.GeneralSettings.BodySelectionMode == BodyShapeSelectionMode.BodyGen)
        {
            // Pre-process some aspects of the configs to improve performance. Mutates the input configs so be sure to use a copy to avoid altering users settings
            _bodyGenPreprocessing.CompileBodyGenRaces(copiedBodyGenConfigs); // descriptor rules compiled here as well
            _bodyGenPreprocessing.LinkTemplatesToParentConfigs(copiedBodyGenConfigs);
            BodyGenTracker = new BodyGenAssignmentTracker();
        }
        else if (_patcherState.GeneralSettings.BodySelectionMode == BodyShapeSelectionMode.BodySlide)
        {
            copiedOBodySettings.BodySlidesFemale = copiedOBodySettings.BodySlidesFemale.Where(x => copiedOBodySettings.CurrentlyExistingBodySlides.Contains(x.ReferencedBodySlide)).ToList(); // don't assign BodySlides that have been uninstalled
            copiedOBodySettings.BodySlidesMale = copiedOBodySettings.BodySlidesMale.Where(x => copiedOBodySettings.CurrentlyExistingBodySlides.Contains(x.ReferencedBodySlide)).ToList();
            _oBodyPreprocessing.CompilePresetRaces(copiedOBodySettings);
            _oBodyPreprocessing.CompileRulesRaces(copiedOBodySettings);

            if (copiedOBodySettings.AutoApplyMissingAnnotations)
            {
                _bodySlideAnnotator.AnnotateBodySlides(copiedOBodySettings.BodySlidesMale.And(copiedOBodySettings.BodySlidesFemale).ToList(), copiedOBodySettings.BodySlideClassificationRules, false);
            }

            BodySlideTracker = new();

            if (_patcherState.GeneralSettings.BSSelectionMode == BodySlideSelectionMode.AutoBody && _patcherState.OBodySettings.AutoBodySelectionMode == AutoBodySelectionMode.INI)
            {
                _oBodyWriter.ClearOutputForIniMode();
            }
            else if (_patcherState.GeneralSettings.BSSelectionMode == BodySlideSelectionMode.OBody && _patcherState.OBodySettings.OBodySelectionMode == OBodySelectionMode.Native)
            {
                _updateHandler.CleanSPIDiniOBody();
                gEnableBodySlideScript.Data = 0;
            }
            else
            {
                //OBodyWriter.WriteBodySlideSPIDIni(bodySlideAssignmentSpell, copiedOBodySettings, outputMod);
                _updateHandler.CleanSPIDiniOBody();
                ApplyRacialSpell.ApplySpell(outputMod, bodySlideAssignmentSpell, _environmentProvider.LinkCache, _patcherState);
                _updateHandler.CleanOldBodySlideDict();
                gEnableBodySlideScript.Data = 1;
            }
        }

        // Height Pre-patching tasks:
        _heightPatcher.Reinitialize();
        _heightAssignmentTransfers = new();
        HeightConfig currentHeightConfig = null;
        if (_patcherState.GeneralSettings.bChangeHeight)
        {
            currentHeightConfig = _patcherState.HeightConfigs.Where(x => x.Label == _patcherState.HeightSettings.SelectedHeightConfig).FirstOrDefault();
            if (currentHeightConfig == null)
            {
                _logger.LogError("Could not find selected Height Config:" + _patcherState.HeightSettings.SelectedHeightConfig + ". Heights will not be assigned.");
            }
            else
            {
                _heightPatcher.AssignRacialHeight(currentHeightConfig, outputMod);
            }
        }

        // HeadPart Pre-patching tasks:
        _headPartSelector.Reinitialize();
        _headPartWriter.CleanPreviousOutputs();
        var gEnableHeadParts = outputMod.Globals.AddNewShort();
        gEnableHeadParts.EditorID = "SynthEBD_HeadPartScriptActive";
        gEnableHeadParts.Data = 0; // default to 0; patcher will change later if one of several conditions are met
        var gHeadpartsVerboseMode = outputMod.Globals.AddNewShort();
        gHeadpartsVerboseMode.EditorID = "SynthEBD_HeadPartsVerboseMode";
        gHeadpartsVerboseMode.Data = Convert.ToInt16(_patcherState.HeadPartSettings.bUseVerboseScripts);

        _headPartWriter.CreateHeadPartLoaderQuest(outputMod, gEnableHeadParts, gHeadpartsVerboseMode);
        Spell headPartAssignmentSpell = HeadPartWriter.CreateHeadPartAssignmentSpell(outputMod, gHeadpartsVerboseMode);
        //HeadPartWriter.WriteHeadPartSPIDIni(headPartAssignmentSpell);
        _updateHandler.CleanSPIDiniHeadParts();
        ApplyRacialSpell.ApplySpell(outputMod, headPartAssignmentSpell, _environmentProvider.LinkCache, _patcherState);

        var copiedHeadPartSettings = JSONhandler<Settings_Headparts>.Deserialize(JSONhandler<Settings_Headparts>.Serialize(_patcherState.HeadPartSettings, out serializationSuccess, out serializatonException), out deserializationSuccess, out deserializationException);
        if (!serializationSuccess) { _logger.LogMessage("Error serializing Head Part configs. Exception: " + serializatonException); _logger.LogErrorWithStatusUpdate("Patching aborted.", ErrorType.Error); return; }
        if (!deserializationSuccess) { _logger.LogMessage("Error deserializing Head Part configs. Exception: " + deserializationException); _logger.LogErrorWithStatusUpdate("Patching aborted.", ErrorType.Error); return; }

        Dictionary<NPCInfo, Dictionary<HeadPart.TypeEnum, FormKey>> configGeneratedHeadPartsDict = new();
        
        if (_patcherState.GeneralSettings.bChangeHeadParts)
        {
            _assignedHeadPartTransfers = new();
            
            // remove headparts that don't exist in current load order
            bool removedHeadParts = false;
            foreach (var typeSettings in copiedHeadPartSettings.Types.Values)
            {
                for (int i = 0; i < typeSettings.HeadParts.Count; i++)
                {
                    var headPartSetting = typeSettings.HeadParts[i];
                    if (_environmentProvider.LinkCache.TryResolve<IHeadPartGetter>(headPartSetting.HeadPartFormKey, out var headPartGetter))
                    {
                        headPartSetting.ResolvedHeadPart = headPartGetter;
                    }
                    else
                    {
                        removedHeadParts = true;
                        typeSettings.HeadParts.RemoveAt(i);
                        i--;
                    }
                }
            }
            if (removedHeadParts)
            {
                _logger.LogMessage("Some head parts will not be distributed because they are no longer present in your load order.");
            }

            _headPartPreprocessing.CompilePresetRaces(copiedHeadPartSettings);
            _headPartPreprocessing.ConvertBodyShapeDescriptorRules(copiedHeadPartSettings);
            _headPartPreprocessing.CompileGenderedHeadParts(copiedHeadPartSettings);
        }
        
        

        // Run main patching operations
        
        _vanillaBodyPathSetter.Reinitialize();
        switch (_patcherState.GeneralSettings.AppearanceMergerType)
        {
            case AppearanceMergeType.EasyNPC:
                _easyNPCProfileParser.Reinitialize(_patcherState.GeneralSettings.EasyNPCprofilePath);
                break;
            case AppearanceMergeType.NPC2:
                _npc2ProfileParser.Reinitialize(_patcherState.GeneralSettings.NPC2TokenPath);
                break;
            case AppearanceMergeType.None:
            default:
                // No initialization needed
                break;
        }
        _skyPatcherInterface.Reinitialize();

        _patchedNpcCount = 0;
        _statusBar.ProgressBarMax = allNPCs.Count();
        _statusBar.ProgressBarCurrent = 0;
        _statusBar.ProgressBarDisp = "Made seleections for " + _statusBar.ProgressBarCurrent + " NPCs";
        
        // Selection: This section can be paralellized
        
        // Patch main NPCs
        var assignmentStopWatch = System.Diagnostics.Stopwatch.StartNew();
        AssignmentLoop(allNPCs, true, outputMod, availableAssetPacks, copiedBodyGenConfigs, copiedOBodySettings, currentHeightConfig, copiedHeadPartSettings, generatedLinkGroups, skippedLinkedNPCs);
        // Finish assigning non-primary linked NPCs
        AssignmentLoop(skippedLinkedNPCs, false, outputMod, availableAssetPacks, copiedBodyGenConfigs, copiedOBodySettings, currentHeightConfig, copiedHeadPartSettings, generatedLinkGroups, skippedLinkedNPCs);
        assignmentStopWatch.Stop();
        _logger.LogMessage($"Variant selection completed in {assignmentStopWatch.Elapsed:mm\\:ss}");
        
        _statusBar.ProgressBarCurrent = 0;
        _statusBar.ProgressBarDisp = "Applied seleections for " + _statusBar.ProgressBarCurrent + " NPCs";
        // Application: This section must be serial 
        var recordGenStopWatch = System.Diagnostics.Stopwatch.StartNew();
        _recordGenerator.ApplySelectedAssets(_assetAssignmentTransfers, flattenedAssetPacks,
            configGeneratedHeadPartsDict, _combinationLog, EBDFaceKW, EBDScriptKW,
            synthEBDFaceKW, _assetAssignmentJsonDictHandler, _statusBar);
        if (useFaceMeshMode)
        {
            _logger.LogMessage("Starting FaceGen NIF patching...");
            _statusBar.ProgressBarCurrent = 0;
            _statusBar.ProgressBarMax = _assetAssignmentTransfers.Count;
            _statusBar.ProgressBarDisp = "Patching FaceGen NIFs for 0 / " + _statusBar.ProgressBarMax + " NPCs";

            foreach (var kvp in _assetAssignmentTransfers)
            {
                var npcInfo = kvp.Key;
                var assetContainers = kvp.Value;

                _faceGenPatcher.PatchFaceGenNifs(npcInfo, assetContainers);
                
                _statusBar.ProgressBarCurrent++;
                if (_statusBar.ProgressBarCurrent % 50 == 0 || _statusBar.ProgressBarCurrent == _statusBar.ProgressBarMax)
                {
                    _statusBar.ProgressBarDisp = "Patching FaceGen NIFs for " + _statusBar.ProgressBarCurrent + " / " + _statusBar.ProgressBarMax + " NPCs";
                }
            }
        }
        recordGenStopWatch.Stop();
        _logger.LogMessage($"Record generation completed in {recordGenStopWatch.Elapsed:mm\\:ss}");
        
        // Now that potential body modifications are complete, set vanilla mesh paths if necessary
        if (_patcherState.TexMeshSettings.bForceVanillaBodyMeshPath)
        {
            _vanillaBodyPathSetter.SetVanillaBodyMeshPaths(outputMod, allNPCs);
        }

        if (_patcherState.GeneralSettings.bChangeMeshesOrTextures)
        {
            _assetAssignmentJsonDictHandler.WriteAssignmentDictionaryScriptMode();
            _combinationLog.WriteToFile(availableAssetPacks);
        }
        
        _heightPatcher.ApplySelectedHeights(_heightAssignmentTransfers, _environmentProvider.OutputMod, _statusBar);
        
        if (_skyPatcherInterface.HasEntries()) // place outside of the Textures and Meshes block because it can also have entries from the Vanilla Body Mesh Setter
        {
            _skyPatcherInterface.WriteIni();
        }

        if (_patcherState.GeneralSettings.BodySelectionMode == BodyShapeSelectionMode.BodyGen)
        {
            _bodyGenWriter.WriteBodyGenOutputs(copiedBodyGenConfigs, _paths.OutputDataFolder);
        }
        else if (_patcherState.GeneralSettings.BodySelectionMode == BodyShapeSelectionMode.BodySlide)
        {
            if (_patcherState.GeneralSettings.BSSelectionMode == BodySlideSelectionMode.AutoBody && _patcherState.OBodySettings.AutoBodySelectionMode == AutoBodySelectionMode.INI)
            {
                _oBodyWriter.WriteAssignmentIni();
            }
            else if (_patcherState.GeneralSettings.BSSelectionMode == BodySlideSelectionMode.OBody && _patcherState.OBodySettings.OBodySelectionMode == OBodySelectionMode.Native)
            {
                _oBodyWriter.WriteNativeAssignmentDictionary();
            }
            else
            {
                _oBodyWriter.WriteAssignmentDictionaryScriptMode();
            }
        }

        if (_patcherState.GeneralSettings.bChangeHeight)
        {
            _heightPatcher.WriteAssignmentDictionaryScriptMode();
        }

        if ((_patcherState.GeneralSettings.bChangeHeadParts && _assignedHeadPartTransfers.Any()) || configGeneratedHeadPartsDict.Any())
        {
            _headPartSelector.ResolveConflictsWithAssetAssignments(_assignedHeadPartTransfers, configGeneratedHeadPartsDict); // now _assignedHeadPartTransfers contains all winning headpart assignments
            
            _headPartSelector.EnsureHeadPartRaceCompatibility(_assignedHeadPartTransfers);
            
            if (configGeneratedHeadPartsDict.Any() && !_patcherState.GeneralSettings.bChangeHeadParts) // these checks not performed when running in Asset Mode only - user needs to be warned if patcher dips into the headpart distribution system while headparts are disabled
            {
                bool validation = true;

                if (!_miscValidation.VerifyJContainersInstalled(true))
                {
                    _logger.LogMessage("WARNING: Your Asset Packs have generated new headparts whose distribution requires JContainers, which was not detected in your data folder. NPCs will not receive their new headparts until this is installed.");
                    validation = false;
                }

                if (!validation)
                {
                    _logger.CallTimedLogErrorWithStatusUpdateAsync("WARNING: Missing dependencies for Asset-Generated Headparts. See Log.", ErrorType.Warning, 5);
                }
            }

            _headPartAuxFunctions.ApplyNeededFaceTextures(_assignedHeadPartTransfers);
            gEnableHeadParts.Data = 1;
            _headPartWriter.WriteAssignmentDictionary(_assignedHeadPartTransfers);
        }

        if (_patcherState.GeneralSettings.bChangeMeshesOrTextures)
        {
            _assetsStatsTracker.WriteReport();
        }

        if (_environmentProvider.RunMode == EnvironmentMode.Standalone)
        {
            string patchOutputPath = System.IO.Path.Combine(_paths.OutputDataFolder, _environmentProvider.OutputMod.ModKey.ToString());
            PatcherIO.WritePatch(patchOutputPath, outputMod, _logger, _environmentProvider);
        }

        _logger.StopTimer();
        _logger.LogMessage("Finished patching in " + _logger.GetEllapsedTime());
        _logger.UpdateStatus("Finished Patching in " + _logger.GetEllapsedTime(), false);

        _statusBar.IsPatching = false;
    }

    private void timer_Tick(object sender, EventArgs e)
    {
        _logger.UpdateStatus("Finished Patching", false);
    }

    public class CategorizedFlattenedAssetPacks
    {
        public CategorizedFlattenedAssetPacks(HashSet<FlattenedAssetPack> availableAssetPacks)
        {
            PrimaryMale = availableAssetPacks.Where(x => x.Gender == Gender.Male && x.Type == FlattenedAssetPack.AssetPackType.Primary).ToHashSet();
            PrimaryFemale = availableAssetPacks.Where(x => x.Gender == Gender.Female && x.Type == FlattenedAssetPack.AssetPackType.Primary).ToHashSet();
            MixInMale = availableAssetPacks.Where(x => x.Gender == Gender.Male && x.Type == FlattenedAssetPack.AssetPackType.MixIn).ToHashSet();
            MixInFemale = availableAssetPacks.Where(x => x.Gender == Gender.Female && x.Type == FlattenedAssetPack.AssetPackType.MixIn).ToHashSet();
        }
        public HashSet<FlattenedAssetPack> PrimaryMale { get; set; }
        public HashSet<FlattenedAssetPack> PrimaryFemale { get; set; }
        public HashSet<FlattenedAssetPack> MixInMale { get; set; }
        public HashSet<FlattenedAssetPack> MixInFemale { get; set; }
    }

    private void AssignmentLoop(
        IEnumerable<INpcGetter> npcCollection, bool skipLinkedSecondaryNPCs, ISkyrimMod outputMod,
        CategorizedFlattenedAssetPacks sortedAssetPacks, BodyGenConfigs bodyGenConfigs, Settings_OBody oBodySettings,
        HeightConfig currentHeightConfig, Settings_Headparts headPartSettings, 
        HashSet<LinkedNPCGroupInfo> generatedLinkGroups, HashSet<INpcGetter> skippedLinkedNPCs)
    {
        bool blockAssets;
        bool blockBodyShape;
        bool blockHeight;
        bool blockHeadParts;
        bool assetsAssigned = false;
        bool bodyShapeAssigned = false;

        HashSet<FlattenedAssetPack> primaryAssetPacks = new HashSet<FlattenedAssetPack>();
        HashSet<FlattenedAssetPack> mixInAssetPacks = new HashSet<FlattenedAssetPack>();

        List<SubgroupCombination> assignedCombinations = new List<SubgroupCombination>();
        HashSet<LinkedNPCGroup> linkedGroupsHashSet = _patcherState.GeneralSettings.LinkedNPCGroups.ToHashSet();

        int npcCount = npcCollection.Count();
        var npcArray = npcCollection.ToArray();

        foreach (var npc in npcArray)
        {
            _statusBar.ProgressBarCurrent++;

            var currentNPCInfo = _npcInfoFactory(npc, linkedGroupsHashSet, generatedLinkGroups);
            _logger.CurrentNPCInfo = currentNPCInfo;
            Npc npcRecord = null;

            #region Detailed logging
            if (_patcherState.GeneralSettings.VerboseModeNPClist.Contains(npc.FormKey) || _patcherState.GeneralSettings.bVerboseModeAssetsAll || _patcherState.GeneralSettings.bVerboseModeAssetsNoncompliant)
            {
                _logger.TriggerNPCReporting(currentNPCInfo);
            }
            
            if (_patcherState.GeneralSettings.VerboseModeNPClist.Contains(npc.FormKey) || _patcherState.GeneralSettings.bVerboseModeAssetsAll) // if logging is done via non-compliant assets, the downstream callers will trigger save if the NPC is found to be non-compliant so don't short-circuit that logic here.
            {
                _logger.TriggerNPCReportingSave(currentNPCInfo);
            }

            if (!currentNPCInfo.Report.SaveCurrentNPCLog && _verboseModeNPCSelector.VerboseLoggingForCurrentNPC(currentNPCInfo)) // don't re-evaluate logging rules if the NPC already needs to be logged.
            {
                _logger.TriggerNPCReporting(currentNPCInfo);
                _logger.TriggerNPCReportingSave(currentNPCInfo);
            }

            _logger.InitializeNewReport(currentNPCInfo);
            #endregion

            if (!currentNPCInfo.IsPatchable)
            {
                _logger.LogReport("NPC skipped because its race or alias for all patcher functions are not included in the General Settings' Patchable Races", false, currentNPCInfo);
                _logger.SaveReport(currentNPCInfo);
                continue;
            }

            assetsAssigned = false;
            bodyShapeAssigned = false;
            assignedCombinations = new List<SubgroupCombination>(); // Do not change to hash set - must maintain order
            List<BodySlideSetting> assignedBodySlides = new(); // can be used by headpart function
            List<BodyGenConfig.BodyGenTemplate> assignedMorphs = null; // can be used by headpart function

            #region Linked NPC Groups
            if (skipLinkedSecondaryNPCs && currentNPCInfo.LinkGroupMember == NPCInfo.LinkGroupMemberType.Secondary)
            {
                skippedLinkedNPCs.Add(npc);
                _logger.LogReport("NPC temporarily skipped because it is a secondary Linked NPC Group member and the primary has not yet been assigned", false, currentNPCInfo);
                _logger.SaveReport(currentNPCInfo);
                continue;
            }
            #endregion

            if (_patchedNpcCount % 100 == 0 || _statusBar.ProgressBarCurrent == _statusBar.ProgressBarMax)
            {
                _statusBar.ProgressBarDisp = "Made selections for " + _patchedNpcCount + " NPCs";
            }

            #region link by name
            if (_patcherState.GeneralSettings.bLinkNPCsWithSameName && currentNPCInfo.IsValidLinkedUnique)
            {
                _uniqueNPCData.InitializeUniqueNPC(currentNPCInfo);
            }
            #endregion

            #region Block List
            blockAssets = IsBlockedForAssets(currentNPCInfo);
            blockBodyShape = IsBlockedForBodyShape(currentNPCInfo);
            blockHeight = IsBlockedForHeight(currentNPCInfo);
            blockHeadParts = IsBlockedForHeadParts(currentNPCInfo);
            #endregion

            bodyShapeAssigned = false;

            if (_patcherState.GeneralSettings.ExcludePlayerCharacter && npc.FormKey.ToString() == Skyrim.Npc.Player.FormKey.ToString())
            {
                _logger.LogReport("NPC skipped because Player patching is disabled", false, currentNPCInfo);
                _logger.SaveReport(currentNPCInfo);
                continue;
            }

            if (_patcherState.GeneralSettings.ExcludePresets && npc.EditorID != null && npc.EditorID.Contains("Preset"))
            {
                _logger.LogReport("NPC skipped because Preset patching is disabled", false, currentNPCInfo);
                _logger.SaveReport(currentNPCInfo);
                continue;
            }

            if (_patcherState.GeneralSettings.bFilterNPCsByArmature && !AppearsHumanoidByArmature(npc))
            {
                _logger.LogReport("NPC skipped because its WornArmor skin does not have a torso, hands, and feet", false, currentNPCInfo);
                _logger.SaveReport(currentNPCInfo);
                continue;
            }

            AssetAndBodyShapeSelector.AssetAndBodyShapeAssignment primaryAssetsAndBodyShape = new AssetAndBodyShapeSelector.AssetAndBodyShapeAssignment();

            #region Asset Assignment
            if (_patcherState.GeneralSettings.bChangeMeshesOrTextures && !blockAssets && _raceResolver.PatchableRaceFormKeys.Contains(currentNPCInfo.AssetsRace))
            {
                assetsAssigned = false;
                switch (currentNPCInfo.Gender)
                {
                    case Gender.Female:
                        primaryAssetPacks = sortedAssetPacks.PrimaryFemale;
                        mixInAssetPacks = sortedAssetPacks.MixInFemale;
                        break;
                    case Gender.Male:
                        primaryAssetPacks = sortedAssetPacks.PrimaryMale;
                        mixInAssetPacks = sortedAssetPacks.MixInMale;
                        break;
                }

                List<string> assetOrder = _patcherState.TexMeshSettings.AssetOrder;
                if (currentNPCInfo?.SpecificNPCAssignment?.AssetOrder != null) {  assetOrder = currentNPCInfo.SpecificNPCAssignment.AssetOrder; }

                foreach (var item in assetOrder)
                {
                    #region Primary Asset assignment
                    if (item == VM_AssetOrderingMenu.PrimaryLabel && !blockBodyShape)
                    {
                        if (_assetsStatsTracker.HasGenderedConfigs[currentNPCInfo.Gender])
                        {
                            primaryAssetsAndBodyShape = _assetAndBodyShapeSelector.ChooseCombinationAndBodyShape(out assetsAssigned, out bodyShapeAssigned, primaryAssetPacks, bodyGenConfigs, oBodySettings, currentNPCInfo, AssetSelector.AssetPackAssignmentMode.Primary, assignedCombinations);
                            if (assetsAssigned)
                            {
                                assignedCombinations.Add(primaryAssetsAndBodyShape.Assets);
                                _assetSelector.RecordPrimaryAssetConsistencyAndLinkedNPCs(primaryAssetsAndBodyShape.Assets, currentNPCInfo);
                            }
                            if (bodyShapeAssigned)
                            {
                                switch (_patcherState.GeneralSettings.BodySelectionMode)
                                {
                                    case BodyShapeSelectionMode.BodyGen:
                                        BodyGenTracker.NPCAssignments.Add(currentNPCInfo.NPC.FormKey, primaryAssetsAndBodyShape.BodyGenMorphs.Select(x => x.Label).ToList());
                                        _bodyGenSelector.RecordBodyGenConsistencyAndLinkedNPCs(primaryAssetsAndBodyShape.BodyGenMorphs, currentNPCInfo);
                                        assignedMorphs = primaryAssetsAndBodyShape.BodyGenMorphs;
                                        break;

                                    case BodyShapeSelectionMode.BodySlide:
                                        BodySlideTracker.Add(currentNPCInfo.NPC.FormKey, primaryAssetsAndBodyShape.BodySlidePresets.Select(x => x.ReferencedBodySlide).ToList());
                                        _oBodySelector.RecordBodySlideConsistencyAndLinkedNPCs(primaryAssetsAndBodyShape.BodySlidePresets, currentNPCInfo);
                                        assignedBodySlides = primaryAssetsAndBodyShape.BodySlidePresets;
                                        break;
                                }
                            }
                        }
                        _assetsStatsTracker.LogNPCAssets(currentNPCInfo, assetsAssigned);
                    }
                    else if (item == VM_AssetOrderingMenu.PrimaryLabel && blockBodyShape)
                    {
                        var assignedCombination = _assetSelector.AssignAssets(currentNPCInfo, AssetSelector.AssetPackAssignmentMode.Primary, primaryAssetPacks, assignedMorphs, assignedBodySlides, out _);
                        if (assignedCombination != null)
                        {
                            assignedCombinations.Add(assignedCombination);
                            _assetSelector.RecordPrimaryAssetConsistencyAndLinkedNPCs(assignedCombination, currentNPCInfo);
                        }
                    }
                    #endregion
                    else
                    {
                        #region MixIn Asset assignment
                        var currentMixIn = mixInAssetPacks.Where(x => x.GroupName == item).FirstOrDefault();
                        if (currentMixIn != null)
                        {
                            var assignedMixIn = _assetSelector.AssignAssets(currentNPCInfo, AssetSelector.AssetPackAssignmentMode.MixIn, new HashSet<FlattenedAssetPack>() { currentMixIn }, assignedMorphs, assignedBodySlides, out bool mixInDeclined);
                            _assetSelector.RecordMixInAssetConsistencyAndLinkedNPCs(assignedMixIn, currentNPCInfo, currentMixIn.GroupName, mixInDeclined);
                            if (assignedMixIn != null)
                            {
                                assignedCombinations.Add(assignedMixIn);
                                assetsAssigned = true;
                            }
                        }
                        #endregion
                    }
                }

                #region Asset Replacer assignment
                if (_patcherState.TexMeshSettings.bEnableAssetReplacers)
                {
                    HashSet<SubgroupCombination> assetReplacerCombinations = new HashSet<SubgroupCombination>();
                    if (assetsAssigned) // assign direct replacers that a come from the assigned primary asset pack
                    {
                        foreach (var combination in assignedCombinations)
                        {
                            assetReplacerCombinations.UnionWith(_assetReplacerSelector.SelectAssetReplacers(combination.AssetPack, currentNPCInfo, assignedMorphs, assignedBodySlides));
                        }
                    }
                    foreach (var replacerOnlyPack in mixInAssetPacks.Where(x => !x.Subgroups.Any() && x.AssetReplacerGroups.Any())) // add asset replacers from mix-in asset packs that ONLY have replacer assets, since they won't be contained in assignedCombinations
                    {
                        assetReplacerCombinations.UnionWith(_assetReplacerSelector.SelectAssetReplacers(replacerOnlyPack, currentNPCInfo, assignedMorphs, assignedBodySlides));
                    }
                    assignedCombinations.AddRange(assetReplacerCombinations);
                }
                else
                {
                    _logger.LogReport("Asset replacement will not be performed because it is disabled in the Textures & Meshes menu", false, currentNPCInfo);
                }
                #endregion

                var assignmentTransfers = assignedCombinations.Select(x => new SelectedAssetContainer(x)).ToList();
                _assetAssignmentTransfers.Add(currentNPCInfo, assignmentTransfers);
                _combinationLog.LogCombinationSelections(currentNPCInfo, assignedCombinations);
            }
            #endregion

            #region Force Vanilla Body Paths
            if (_patcherState.TexMeshSettings.bForceVanillaBodyMeshPath)
            {
                _vanillaBodyPathSetter.RegisterAssetAssignedMeshes(assignedCombinations);
                if (_vanillaBodyPathSetter.IsBlockedForVanillaBodyPaths(currentNPCInfo))
                {
                    _vanillaBodyPathSetter.RegisterBlockedFromVanillaBodyPaths(currentNPCInfo);
                }
            }
            #endregion
            #region Body Shape assignment (if assets not assigned with Assets)
            switch (_patcherState.GeneralSettings.BodySelectionMode)
            {
                case BodyShapeSelectionMode.None: break;

                case BodyShapeSelectionMode.BodyGen:
                    if (!blockBodyShape && _raceResolver.PatchableRaceFormKeys.Contains(currentNPCInfo.BodyShapeRace) && !bodyShapeAssigned && BodyGenSelector.BodyGenAvailableForGender(currentNPCInfo.Gender, bodyGenConfigs))
                    {
                        _logger.LogReport("Assigning a BodyGen morph independently of Asset Combination", false, currentNPCInfo);
                        assignedMorphs = _bodyGenSelector.SelectMorphs(currentNPCInfo, out bool success, bodyGenConfigs, null, new List<SubgroupCombination>(), out _);
                        if (success)
                        {
                            BodyGenTracker.NPCAssignments.Add(currentNPCInfo.NPC.FormKey, assignedMorphs.Select(x => x.Label).ToList());
                            _bodyGenSelector.RecordBodyGenConsistencyAndLinkedNPCs(assignedMorphs, currentNPCInfo);
                            primaryAssetsAndBodyShape.BodyGenMorphs = assignedMorphs;
                        }
                        else
                        {
                            _logger.LogReport("Could not independently assign a BodyGen Morph.", true, currentNPCInfo);
                        }
                    }
                    break;
                case BodyShapeSelectionMode.BodySlide:
                    if (!blockBodyShape && _raceResolver.PatchableRaceFormKeys.Contains(currentNPCInfo.BodyShapeRace) && !bodyShapeAssigned && _oBodySelector.CurrentNPCHasAvailablePresets(currentNPCInfo, oBodySettings))
                    {
                        _logger.LogReport("Assigning a BodySlide preset independently of Asset Combination", false, currentNPCInfo);
                        assignedBodySlides = _oBodySelector.SelectBodySlidePresets(currentNPCInfo, out bool success, oBodySettings, new List<SubgroupCombination>(), out _);
                        if (success)
                        {
                            BodySlideTracker.Add(currentNPCInfo.NPC.FormKey, assignedBodySlides.Select(x => x.ReferencedBodySlide).ToList());
                            _oBodySelector.RecordBodySlideConsistencyAndLinkedNPCs(assignedBodySlides, currentNPCInfo);
                            primaryAssetsAndBodyShape.BodySlidePresets = assignedBodySlides;
                        }
                        else
                        {
                            _logger.LogReport("Could not independently assign a BodySlide preset.", true, currentNPCInfo);
                        }
                    }
                    break;
            }
            #endregion

            #region Height assignment
            if (_patcherState.GeneralSettings.bChangeHeight && !blockHeight && _raceResolver.PatchableRaceFormKeys.Contains(currentNPCInfo.HeightRace))
            {
                var height =_heightPatcher.AssignNPCHeight(currentNPCInfo, currentHeightConfig, outputMod);
                if (height.HasValue)
                {
                    _heightAssignmentTransfers.Add(currentNPCInfo, height.Value);  
                }
            }
            #endregion

            #region Head Part assignment

            if (_patcherState.GeneralSettings.bChangeHeadParts && !blockHeadParts && _raceResolver.PatchableRaceFormKeys.Contains(currentNPCInfo.HeadPartsRace))
            {
                var headPartAssignments = _headPartSelector.AssignHeadParts(currentNPCInfo, headPartSettings, assignedBodySlides, assignedMorphs);
                if (headPartAssignments.Any())
                {
                    _assignedHeadPartTransfers.Add(currentNPCInfo, headPartAssignments);
                }
            }
            #endregion

            _logger.SaveReport(currentNPCInfo);
            
            _patchedNpcCount++;
        }
    }

    private void UpdateRecordTemplateAdditonalRaces(List<AssetPack> assetPacks, ILinkCache<ISkyrimMod, ISkyrimModGetter> recordTemplateLinkCache, List<SkyrimMod> recordTemplatePlugins)
    {
        Dictionary<string, HashSet<string>> patchedTemplates = new Dictionary<string, HashSet<string>>();

        foreach (var assetPack in assetPacks)
        {
            var exclusions = assetPack.AdditionalRecordTemplateAssignments.SelectMany(x => x.Races).ToHashSet(); // don't include races that get their own record template in the default template's patching
            var racesToAdd = _patcherState.GeneralSettings.PatchableRaces.Where(x => !FormKeyHashSetComparer.Contains(exclusions, x)).ToHashSet();
            SetRecordTemplateAdditionalRaces(assetPack.DefaultRecordTemplateAdditionalRacesPaths, assetPack.DefaultRecordTemplate, racesToAdd, patchedTemplates, recordTemplateLinkCache, recordTemplatePlugins);

            foreach (var additionalTemplateEntry in assetPack.AdditionalRecordTemplateAssignments)
            {
                SetRecordTemplateAdditionalRaces(additionalTemplateEntry.AdditionalRacesPaths, additionalTemplateEntry.TemplateNPC, additionalTemplateEntry.Races, patchedTemplates, recordTemplateLinkCache, recordTemplatePlugins);
            }
        }
    }

    private void SetRecordTemplateAdditionalRaces(HashSet<string> additionalRacesPaths, FormKey templateFK, HashSet<FormKey> racesToAdd, Dictionary<string, HashSet<string>> alreadyPatchedTemplates, ILinkCache<ISkyrimMod, ISkyrimModGetter> recordTemplateLinkCache, List<SkyrimMod> recordTemplatePlugins)
    {
        foreach (var path in additionalRacesPaths)
        {
            if (alreadyPatchedTemplates.ContainsKey(templateFK.ToString()) && alreadyPatchedTemplates[templateFK.ToString()].Contains(path))
            {
                continue;
            }

            var templateMod = recordTemplatePlugins.Where(x => x.ModKey.ToString() == templateFK.ModKey.ToString()).FirstOrDefault();
            if (templateMod == null)
            {
                _logger.LogError("Could not find record template plugin " + templateFK.ToString());
            }

            if (recordTemplateLinkCache.TryResolve<INpcGetter>(templateFK, out var template))
            {
                try
                {
                    if (!_recordPathParser.GetNearestParentGetter(template, path, recordTemplateLinkCache, false, _logger.GetNPCLogNameString(template), out IMajorRecordGetter parentRecordGetter, out string relativePath))
                    {
                        continue;
                    }

                    var parentRecord = RecordGenerator.GetOrAddGenericRecordAsOverride(parentRecordGetter, templateMod);

                    if (_recordPathParser.GetObjectAtPath(parentRecord, template, relativePath, new Dictionary<string, dynamic>(), recordTemplateLinkCache, false, _logger.GetNPCLogNameString(template), out dynamic additionalRaces))
                    {
                        foreach (var race in racesToAdd)
                        {
                            additionalRaces.Add(race.ToLink<IRaceGetter>());
                        }
                    }
                    if (!alreadyPatchedTemplates.ContainsKey(templateFK.ToString()))
                    {
                        alreadyPatchedTemplates.Add(templateFK.ToString(), new HashSet<string>());
                    }
                    alreadyPatchedTemplates[templateFK.ToString()].Add(path);
                }
                catch
                {
                    _logger.LogError("Could not patch additional races expected at " + path + " in template NPC " + _logger.GetNPCLogNameString(template));
                    continue;
                }
            }
            else
            {
                _logger.LogError("Could not resolve template NPC " + templateFK.ToString());
            }
        }
    }

    public class SelectedAssetContainer
    {
        public SelectedAssetContainer(SubgroupCombination combination)
        {
            AssetPackName = combination.AssetPack.GroupName;
            LoggingLabel = combination.AssignmentName;
            Signature = combination.Signature;
            CombinationType = combination.AssetPack.Type;
            Paths = combination.ContainedSubgroups.SelectMany(x => x.Paths).ToList();
            KeywordsToApply = combination.ContainedSubgroups.SelectMany(x => x.AddKeywords).ToList();
        }
        
        public string AssetPackName { get; set; }
        public string LoggingLabel { get; set; } // for logging only
        public string Signature { get; set; }  // for logging only
        public FlattenedAssetPack.AssetPackType CombinationType { get; set; }
        public List<FilePathReplacement> Paths { get; set; }
        public List<string> KeywordsToApply { get; set; }
        public HashSet<GeneratedRecordInfo> TraversedRecords { get; set; } = new(); // for logging only
    }

    public static BodyGenAssignmentTracker BodyGenTracker = new BodyGenAssignmentTracker(); // tracks unique selected morphs so that only assigned morphs are written to the generated templates.ini
    public static Dictionary<FormKey, List<string>> BodySlideTracker = new Dictionary<FormKey, List<string>>(); // tracks which NPCs get which bodyslide presets. The List<string> contains multiple entries ONLY if OBodySelectionMode == Native and 

    public class BodyGenAssignmentTracker
    {
        public Dictionary<FormKey, List<string>> NPCAssignments = new();
        public Dictionary<string, HashSet<string>> AllChosenMorphsMale = new();
        public Dictionary<string, HashSet<string>> AllChosenMorphsFemale = new();
    }

    public class AssetStatsTracker
    {
        public class AssignablePairing
        {
            public int Assignable { get; set; } = 0;
            public int Assigned { get; set; } = 0;
        }

        public Dictionary<Gender, bool> HasGenderedConfigs { get; set; } = new();

        public Dictionary<Gender, Dictionary<IFormLinkGetter<IRaceGetter>, AssignablePairing>> AssignmentsByGenderAndRace { get; set; } = new()
        {
            {   Gender.Male, new Dictionary<IFormLinkGetter<IRaceGetter>, AssignablePairing>()},
            {   Gender.Female, new Dictionary<IFormLinkGetter<IRaceGetter>, AssignablePairing>()} 
        };
        private readonly Logger _logger;
        private readonly ILinkCache _linkCache;

        public AssetStatsTracker(PatcherState patcherState, Logger logger, ILinkCache linkCache)
        {
            HasGenderedConfigs.Add(Gender.Male, false);
            HasGenderedConfigs.Add(Gender.Female, false);

            if (patcherState.AssetPacks.Where(x => x.Gender == Gender.Male && patcherState.TexMeshSettings.SelectedAssetPacks.Contains(x.GroupName)).Any()) { HasGenderedConfigs[Gender.Male] = true; }
            if (patcherState.AssetPacks.Where(x => x.Gender == Gender.Female && patcherState.TexMeshSettings.SelectedAssetPacks.Contains(x.GroupName)).Any()) { HasGenderedConfigs[Gender.Female] = true; }
            _logger = logger;
            _linkCache = linkCache;
        }

        public void LogNPCAssets(NPCInfo npcInfo, bool primaryAssetsAssigned)
        {
            if (!AssignmentsByGenderAndRace.ContainsKey(npcInfo.Gender))
            {
                Dictionary<IFormLinkGetter<IRaceGetter>, AssignablePairing> entry = new();
                AssignmentsByGenderAndRace.Add(npcInfo.Gender, entry);
            }

            if (!AssignmentsByGenderAndRace[npcInfo.Gender].ContainsKey(npcInfo.NPC.Race))
            {
                AssignmentsByGenderAndRace[npcInfo.Gender].Add(npcInfo.NPC.Race, new AssignablePairing());
            }

            AssignmentsByGenderAndRace[npcInfo.Gender][npcInfo.NPC.Race].Assignable++;
            if (primaryAssetsAssigned)
            {
                AssignmentsByGenderAndRace[npcInfo.Gender][npcInfo.NPC.Race].Assigned++;
            }
        }

        public void WriteReport()
        {
            if (!HasGenderedConfigs[Gender.Male] && !HasGenderedConfigs[Gender.Female])
            {
                _logger.LogMessage("No primary asset config files were installed.");
                return;
            }
            else
            {
                _logger.LogMessage("Primary asset pack assignment counts:");
            }

            if (!HasGenderedConfigs[Gender.Male])
            {
                _logger.LogMessage("No primary asset config files for male NPCs were installed");
            }
            else
            {
                foreach (var entry in AssignmentsByGenderAndRace[Gender.Male].OrderBy(x => GetRaceDisplayString(x.Key)).ToArray())
                {
                    if (entry.Key.TryResolve(_linkCache, out _))
                    {
                        _logger.LogMessage(FormatEntry(Gender.Male, entry.Key, entry.Value));
                    }
                }
            }

            if (!HasGenderedConfigs[Gender.Female])
            {
                _logger.LogMessage("No primary asset config files for female NPCs were installed");
            }
            else
            {
                foreach (var entry in AssignmentsByGenderAndRace[Gender.Female].OrderBy(x => GetRaceDisplayString(x.Key)).ToArray())
                {
                    if (entry.Key.TryResolve(_linkCache, out _))
                    {
                        _logger.LogMessage(FormatEntry(Gender.Female, entry.Key, entry.Value));
                    }
                }
            }
        }

        private string FormatEntry(Gender gender, IFormLinkGetter<IRaceGetter> raceLink, AssignablePairing assignablePairing)
        {
            string raceDispStr = GetRaceDisplayString(raceLink);

            string percentage = "0";
            if (assignablePairing.Assignable > 0) { percentage = (assignablePairing.Assigned * 100 / assignablePairing.Assignable).ToString("N2"); }

            return raceDispStr + " " + gender.ToString() + ": " + assignablePairing.Assigned + " of " + assignablePairing.Assignable + " (" + percentage + "%)";
        }

        private string GetRaceDisplayString(IFormLinkGetter<IRaceGetter> raceLink)
        {
            string raceDispStr = "";
            if (raceLink.TryResolve(_linkCache, out var raceGetter))
            {
                raceDispStr = raceGetter.FormKey.ToString();
                if (raceGetter.EditorID != null) { raceDispStr = raceGetter.EditorID.Replace("Race", " ", StringComparison.OrdinalIgnoreCase).Trim(); }

                /* This would be nice but even the base game has two Races with the same name (Breton and Afflicted). Leaving for now, but probably not worth dressing up.
                if (raceGetter.Name != null) { raceDispStr = raceGetter.Name.String; }
                else if (raceGetter.EditorID != null) { raceDispStr = raceGetter.EditorID; }

                if (raceGetter.Name != null && raceGetter.EditorID != null && raceGetter.EditorID.Contains("Vampire", StringComparison.OrdinalIgnoreCase) && !raceGetter.Name.String.Contains("Vampire", StringComparison.OrdinalIgnoreCase))
                {
                    raceDispStr += " (Vampire)";
                }
                */
            }
            return raceDispStr;
        }
    }

    public bool IsBlockedForAssets(NPCInfo npcInfo)
    {
        if (npcInfo.BlockedNPCEntry.Assets)
        {
            _logger.LogReport("Current NPC is blocked from asset assignment via the NPC block list", false, npcInfo);
            return true;
        }
        else if (npcInfo.BlockedPluginEntry.Assets)
        {
            _logger.LogReport("Current NPC is blocked from asset assignment via the Plugin block list", false, npcInfo);
            return true;
        }
        else if (_assetSelector.BlockAssetDistributionByExistingAssets(npcInfo))
        {
            return true; // logging handled by assetSelector
        }
        return false;
    }

    public bool IsBlockedForBodyShape(NPCInfo npcInfo)
    {
        if (npcInfo.BlockedNPCEntry.BodyShape)
        {
            _logger.LogReport("Current NPC is blocked from body shape assignment via the NPC block list", false, npcInfo);
            return true;
        }
        else if (npcInfo.BlockedPluginEntry.BodyShape)
        {
            _logger.LogReport("Current NPC is blocked from body shape assignment via the Plugin block list", false, npcInfo);
            return true;
        }
        else if (!_oBodyPreprocessing.NPCIsEligibleForBodySlide(npcInfo.NPC))
        {
            _logger.LogReport("Current NPC is blocked from body shape assignment because it inherits traits from a template NPC", false, npcInfo);
            return true; // logging handled by assetSelector
        }
        return false;
    }

    public bool IsBlockedForHeight(NPCInfo npcInfo)
    {
        if (npcInfo.BlockedNPCEntry.Height)
        {
            _logger.LogReport("Current NPC is blocked from height assignment via the NPC block list", false, npcInfo);
            return true;
        }
        else if (npcInfo.BlockedPluginEntry.Height)
        {
            _logger.LogReport("Current NPC is blocked from height assignment via the Plugin block list", false, npcInfo);
            return true;
        }
        return false;
    }

    public bool IsBlockedForHeadParts(NPCInfo npcInfo)
    {
        if (npcInfo.BlockedNPCEntry.HeadParts)
        {
            _logger.LogReport("Current NPC is blocked from head part assignment via the NPC block list", false, npcInfo);
            return true;
        }
        else if (npcInfo.BlockedPluginEntry.HeadParts)
        {
            _logger.LogReport("Current NPC is blocked from head part assignment via the Plugin block list", false, npcInfo);
            return true;
        }
        if (_headPartSelector.BlockNPCWithCustomFaceGen(npcInfo))
        {
            if (npcInfo.SpecificNPCAssignment != null && npcInfo.SpecificNPCAssignment.HeadParts != null && npcInfo.SpecificNPCAssignment.HeadParts.Where(x => x.Value != null && x.Value.FormKey != null && !x.Value.FormKey.IsNull).Any())
            {
                _logger.LogReport("Head part assignment is NOT blocked for current NPC despite potentially having a custom face sculpt because you have a head part set in this NPC's Specific NPC Assignment", false, npcInfo);
            }
            else
            {
                _logger.LogReport("Head part assignment is blocked for current NPC because it might have custom FaceGen", false, npcInfo);
                return true;
            }  
        }
        return false;
    }

    private bool AppearsHumanoidByArmature(INpcGetter npc) // tries to identify creatures that are wrongly assigned a humanoid race via their armature
    {
        if (npc.WornArmor == null || npc.WornArmor.IsNull)
        {
            return true;
        }

        var armor = npc.WornArmor.TryResolve(_environmentProvider.LinkCache);
        if (armor == null || armor.Armature == null || armor.Armature.Count == 0) { return true; }

        if (armor.Armature.Count >= 3)
        {
            var arma = armor.Armature.Select(x => x.TryResolve(_environmentProvider.LinkCache)).Where(x => x != null && x.BodyTemplate != null).ToList();
            var toMatch = new List<BipedObjectFlag>() { BipedObjectFlag.Body, BipedObjectFlag.Hands, BipedObjectFlag.Feet };

            for (int i = 0; i < arma.Count; i++)
            {
                var armature = arma[i];

                for (int j = 0; j < toMatch.Count; j++)
                {
                    var bodypart = toMatch[j];
                    if (armature.BodyTemplate.FirstPersonFlags.HasFlag(bodypart))
                    {
                        toMatch.Remove(bodypart);
                        arma.Remove(armature);
                        i--;
                        break;
                    }
                }
            }

            if (toMatch.Count == 0) // npc has skin with torso, hands, and feet components
            {
                return true;
            }
        }
        return false;
    }
}