using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Skyrim;
using Mutagen.Bethesda.FormKeys.SkyrimSE;
using Mutagen.Bethesda.Plugins.Records;

namespace SynthEBD;

public class Patcher
{
    private readonly MainState _state;
    private readonly VM_StatusBar _statusBar;
    private readonly CombinationLog _combinationLog;
    private readonly PatcherEnvironmentProvider _environmentProvider;

    public Patcher(MainState state, VM_StatusBar statusBar, CombinationLog combinationLog, PatcherEnvironmentProvider environmentProvider)
    {
        _state = state;
        _statusBar = statusBar;
        _combinationLog = combinationLog;
        _environmentProvider = environmentProvider;
    }

    //Synchronous version for debugging only
    //public static void RunPatcher(List<AssetPack> assetPacks, BodyGenConfigs bodyGenConfigs, List<HeightConfig> heightConfigs, Dictionary<string, NPCAssignment> consistency, HashSet<NPCAssignment> specificNPCAssignments, BlockList blockList, HashSet<string> linkedNPCNameExclusions, HashSet<LinkedNPCGroup> linkedNPCGroups, ILinkCache<ISkyrimMod, ISkyrimModGetter> recordTemplateLinkCache, List<SkyrimMod> recordTemplatePlugins, VM_StatusBar statusBar)
    public async Task RunPatcher()
    {
        var assetPacks = _state.AssetPacks.Where(x => PatcherSettings.TexMesh.SelectedAssetPacks.Contains(x.GroupName))
            .ToList();
        var outputMod = _environmentProvider.OutputMod;
        ResolvePatchableRaces();
        BodyGenTracker = new BodyGenAssignmentTracker();
        UniqueAssignmentsByName.Clear();
        UniqueNPCData.UniqueNameExclusions = PatcherSettings.General.LinkedNPCNameExclusions.ToHashSet();

        Logger.UpdateStatus("Patching", false);
        Logger.StartTimer();
        Logger.Instance.PatcherExecutionStart = DateTime.Now;

        _statusBar.IsPatching = true;

        HashSet<LinkedNPCGroupInfo> generatedLinkGroups = new HashSet<LinkedNPCGroupInfo>();
        CategorizedFlattenedAssetPacks availableAssetPacks = null;

        var allNPCs = PatcherEnvironmentProvider.Instance.Environment.LoadOrder.PriorityOrder.OnlyEnabledAndExisting().WinningOverrides<INpcGetter>();
        HashSet<INpcGetter> skippedLinkedNPCs = new HashSet<INpcGetter>();

        Keyword EBDFaceKW = null;
        Keyword EBDScriptKW = null;
        Spell EBDHelperSpell = null;

        Spell bodySlideAssignmentSpell = null;

        HeightConfig currentHeightConfig = null;

        Spell headPartAssignmentSpell = null;

        if (PatcherSettings.General.bChangeMeshesOrTextures)
        {
            UpdateRecordTemplateAdditonalRaces(assetPacks, _state.RecordTemplateLinkCache, _state.RecordTemplatePlugins);
            HashSet<FlattenedAssetPack> flattenedAssetPacks = new HashSet<FlattenedAssetPack>();
            flattenedAssetPacks = assetPacks.Select(x => FlattenedAssetPack.FlattenAssetPack(x)).ToHashSet();
            PathTrimmer.TrimFlattenedAssetPacks(flattenedAssetPacks, PatcherSettings.TexMesh.TrimPaths.ToHashSet());
            availableAssetPacks = new CategorizedFlattenedAssetPacks(flattenedAssetPacks);

            EBDCoreRecords.CreateCoreRecords(outputMod, out EBDFaceKW, out EBDScriptKW, out EBDHelperSpell);
            EBDCoreRecords.ApplyHelperSpell(outputMod, EBDHelperSpell);

            RecordGenerator.Reinitialize();
            _combinationLog.Reinitialize();

            HasAssetDerivedHeadParts = false;
        }

        // Several operations are performed that mutate the input settings. For Asset Packs this does not affect saved settings because the operations are performed on the derived FlattenedAssetPacks, but for BodyGen configs and OBody settings these are made directly to the settings files. Therefore, create a deep copy of the configs and operate on those to avoid altering the user's saved settings upon exiting the program
        bool serializationSuccess, deserializationSuccess;
        string serializatonException, deserializationException;

        BodyGenConfigs copiedBodyGenConfigs = JSONhandler<BodyGenConfigs>.Deserialize(JSONhandler<BodyGenConfigs>.Serialize(_state.BodyGenConfigs, out serializationSuccess, out serializatonException), out deserializationSuccess, out deserializationException);
        if (!serializationSuccess) { Logger.LogMessage("Error serializing BodyGen configs. Exception: " + serializatonException); Logger.LogErrorWithStatusUpdate("Patching aborted.", ErrorType.Error); return; }
        if (!deserializationSuccess) { Logger.LogMessage("Error deserializing BodyGen configs. Exception: " + deserializationException); Logger.LogErrorWithStatusUpdate("Patching aborted.", ErrorType.Error); return; }
        Settings_OBody copiedOBodySettings = JSONhandler<Settings_OBody>.Deserialize(JSONhandler<Settings_OBody>.Serialize(PatcherSettings.OBody, out serializationSuccess, out serializatonException), out deserializationSuccess, out deserializationException);
        if (!serializationSuccess) { Logger.LogMessage("Error serializing OBody Settings. Exception: " + serializatonException); Logger.LogErrorWithStatusUpdate("Patching aborted.", ErrorType.Error); return; }
        if (!deserializationSuccess) { Logger.LogMessage("Error deserializing OBody Settings. Exception: " + deserializationException); Logger.LogErrorWithStatusUpdate("Patching aborted.", ErrorType.Error); return; }
        copiedOBodySettings.CurrentlyExistingBodySlides = PatcherSettings.OBody.CurrentlyExistingBodySlides; // JSONIgnored so doesn't get serialized/deserialized

        if (PatcherSettings.General.BodySelectionMode == BodyShapeSelectionMode.BodyGen)
        {
            // Pre-process some aspects of the configs to improve performance. Mutates the input configs so be sure to use a copy to avoid altering users settings
            BodyGenPreprocessing.CompileBodyGenRaces(copiedBodyGenConfigs); // descriptor rules compiled here as well
            BodyGenPreprocessing.FlattenGroupAttributes(copiedBodyGenConfigs);
            BodyGenPreprocessing.ImplementDescriptorRules(copiedBodyGenConfigs);
            BodyGenTracker = new BodyGenAssignmentTracker();
        }
        else if (PatcherSettings.General.BodySelectionMode == BodyShapeSelectionMode.BodySlide)
        {
            copiedOBodySettings.BodySlidesFemale = copiedOBodySettings.BodySlidesFemale.Where(x => copiedOBodySettings.CurrentlyExistingBodySlides.Contains(x.Label)).ToList(); // don't assign BodySlides that have been uninstalled
            copiedOBodySettings.BodySlidesMale = copiedOBodySettings.BodySlidesMale.Where(x => copiedOBodySettings.CurrentlyExistingBodySlides.Contains(x.Label)).ToList();
            OBodyPreprocessing.CompilePresetRaces(copiedOBodySettings);
            OBodyPreprocessing.CompileRulesRaces(copiedOBodySettings);
            OBodyPreprocessing.FlattenGroupAttributes(copiedOBodySettings);

            BodySlideTracker = new Dictionary<FormKey, string>();

            if (PatcherSettings.General.BSSelectionMode == BodySlideSelectionMode.AutoBody && PatcherSettings.OBody.AutoBodySelectionMode == AutoBodySelectionMode.INI)
            {
                OBodyWriter.ClearOutputForIniMode();
            }
            else
            {
                OBodyWriter.ClearOutputForJsonMode();
                var bodyslidesLoaded = outputMod.Globals.AddNewShort();
                bodyslidesLoaded.EditorID = "SynthEBDBodySlidesLoaded";
                bodyslidesLoaded.Data = 0;

                JContainersDomain.CreateSynthEBDDomain();
                OBodyWriter.CreateBodySlideLoaderQuest(outputMod, bodyslidesLoaded);
                bodySlideAssignmentSpell = OBodyWriter.CreateOBodyAssignmentSpell(outputMod, bodyslidesLoaded);
                OBodyWriter.WriteBodySlideSPIDIni(bodySlideAssignmentSpell, copiedOBodySettings, outputMod);
            }
        }

        if (PatcherSettings.General.bChangeHeight)
        {
            currentHeightConfig = _state.HeightConfigs.Where(x => x.Label == PatcherSettings.Height.SelectedHeightConfig).FirstOrDefault();
            if (currentHeightConfig == null)
            {
                Logger.LogError("Could not find selected Height Config:" + PatcherSettings.Height.SelectedHeightConfig + ". Heights will not be assigned.");
            }
            else
            {
                HeightPatcher.AssignRacialHeight(currentHeightConfig, outputMod);
            }
        }

        var copiedHeadPartSettings = JSONhandler<Settings_Headparts>.Deserialize(JSONhandler<Settings_Headparts>.Serialize(PatcherSettings.HeadParts, out serializationSuccess, out serializatonException), out deserializationSuccess, out deserializationException);
        if (!serializationSuccess) { Logger.LogMessage("Error serializing Head Part configs. Exception: " + serializatonException); Logger.LogErrorWithStatusUpdate("Patching aborted.", ErrorType.Error); return; }
        if (!deserializationSuccess) { Logger.LogMessage("Error deserializing Head Part configs. Exception: " + deserializationException); Logger.LogErrorWithStatusUpdate("Patching aborted.", ErrorType.Error); return; }

        if (PatcherSettings.General.bChangeHeadParts)
        {
            // remove headparts that don't exist in current load order
            bool removedHeadParts = false;
            foreach (var typeSettings in copiedHeadPartSettings.Types.Values)
            {
                for (int i = 0; i < typeSettings.HeadParts.Count; i++)
                {
                    var headPartSetting = typeSettings.HeadParts[i];
                    if (PatcherEnvironmentProvider.Instance.Environment.LinkCache.TryResolve<IHeadPartGetter>(headPartSetting.HeadPartFormKey, out var headPartGetter))
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
                Logger.LogMessage("Some head parts will not be distributed because they are no longer present in your load order.");
            }

            HeadPartTracker = new Dictionary<FormKey, HeadPartSelection>();

            HeadPartPreprocessing.CompilePresetRaces(copiedHeadPartSettings);
            HeadPartPreprocessing.FlattenGroupAttributes(copiedHeadPartSettings);
            HeadPartPreprocessing.ConvertBodyShapeDescriptorRules(copiedHeadPartSettings);
            HeadPartPreprocessing.CompileGenderedHeadParts(copiedHeadPartSettings);
        }

        int npcCounter = 0;
        HashSet<Npc> headPartNPCs = new HashSet<Npc>();
        _statusBar.ProgressBarMax = allNPCs.Count();
        _statusBar.ProgressBarCurrent = 0;
        _statusBar.ProgressBarDisp = "Patched " + _statusBar.ProgressBarCurrent + " NPCs";
        // Patch main NPCs
        MainLoop(allNPCs, true, outputMod, availableAssetPacks, copiedBodyGenConfigs, copiedOBodySettings, currentHeightConfig, copiedHeadPartSettings, npcCounter, generatedLinkGroups, skippedLinkedNPCs, EBDFaceKW, EBDScriptKW, bodySlideAssignmentSpell, headPartNPCs);
        // Finish assigning non-primary linked NPCs
        MainLoop(skippedLinkedNPCs, false, outputMod, availableAssetPacks, copiedBodyGenConfigs, copiedOBodySettings, currentHeightConfig, copiedHeadPartSettings, npcCounter, generatedLinkGroups, skippedLinkedNPCs, EBDFaceKW, EBDScriptKW, bodySlideAssignmentSpell, headPartNPCs);

        // Perform headpart functionality if any headparts were patched | DEPRECATED
        /*
        if (headPartNPCs.Any())
        {
            Keyword EBDValidHeadPartActorKW = EBDCoreRecords.CreateHeadPartKeyword(outputMod);
            EBDCoreRecords.ApplyHeadPartKeyword(headPartNPCs, EBDValidHeadPartActorKW);
            HeadPartFunctions.ApplyNeededFaceTextures(headPartNPCs);
        }*/

        Logger.StopTimer();
        Logger.LogMessage("Finished patching in " + Logger.GetEllapsedTime());
        Logger.UpdateStatus("Finished Patching in " + Logger.GetEllapsedTime(), false);

        if (PatcherSettings.General.bChangeMeshesOrTextures)
        {
            _combinationLog.WriteToFile();
        }

        if (PatcherSettings.General.BodySelectionMode == BodyShapeSelectionMode.BodyGen)
        {
            BodyGenWriter.WriteBodyGenOutputs(copiedBodyGenConfigs);
        }
        else if (PatcherSettings.General.BodySelectionMode == BodyShapeSelectionMode.BodySlide)
        {
            if (PatcherSettings.General.BSSelectionMode == BodySlideSelectionMode.AutoBody && PatcherSettings.OBody.AutoBodySelectionMode == AutoBodySelectionMode.INI)
            {
                OBodyWriter.WriteAssignmentIni();
            }
            else
            {
                OBodyWriter.CopyBodySlideScript();
                OBodyWriter.WriteAssignmentDictionary();
            }
        }

        if (PatcherSettings.General.bChangeHeadParts || HasAssetDerivedHeadParts)
        {
            if (HasAssetDerivedHeadParts) // these checks not performed when running in Asset Mode only - user needs to be warned if patcher dips into the headpart distribution system while headparts are disabled
            {
                bool validation = true;
                if (!MiscValidation.VerifySPIDInstalled(PatcherEnvironmentProvider.Instance.Environment.DataFolderPath, true))
                {
                    Logger.LogMessage("WARNING: Your Asset Packs have generated new headparts whose distribution requires Spell Perk Item Distributor, which was not detected in your data folder. NPCs will not receive their new headparts until this is installed.");
                    validation = false;
                }

                if (!MiscValidation.VerifyJContainersInstalled(PatcherEnvironmentProvider.Instance.Environment.DataFolderPath, true))
                {
                    Logger.LogMessage("WARNING: Your Asset Packs have generated new headparts whose distribution requires JContainers, which was not detected in your data folder. NPCs will not receive their new headparts until this is installed.");
                    validation = false;
                }

                if (!validation)
                {
                    Logger.CallTimedLogErrorWithStatusUpdateAsync("WARNING: Missing dependencies for Asset-Generated Headparts. See Log.", ErrorType.Warning, 5);
                }
            }

            CommonScripts.CopyAllToOutputFolder();

            var headpartsLoaded = outputMod.Globals.AddNewShort();
            headpartsLoaded.EditorID = "SynthEBDHeadPartsLoaded";
            headpartsLoaded.Data = 0;

            JContainersDomain.CreateSynthEBDDomain();
            HeadPartWriter.CreateHeadPartLoaderQuest(outputMod, headpartsLoaded);
            headPartAssignmentSpell = HeadPartWriter.CreateHeadPartAssignmentSpell(outputMod, headpartsLoaded);
            HeadPartWriter.WriteHeadPartSPIDIni(headPartAssignmentSpell);

            HeadPartWriter.CopyHeadPartScript();
            HeadPartWriter.CleanPreviousOutputs();
            HeadPartWriter.WriteAssignmentDictionary();
        }

        string patchOutputPath = System.IO.Path.Combine(PatcherSettings.General.OutputDataFolder, PatcherSettings.General.PatchFileName + ".esp");
        PatcherIO.WritePatch(patchOutputPath, outputMod);

        _statusBar.IsPatching = false;
    }

    public static HashSet<IFormLinkGetter<IRaceGetter>> PatchableRaces;

    public static Dictionary<string, Dictionary<Gender, UniqueNPCData.UniqueNPCTracker>> UniqueAssignmentsByName = new Dictionary<string, Dictionary<Gender, UniqueNPCData.UniqueNPCTracker>>();

    private static void timer_Tick(object sender, EventArgs e)
    {
        Logger.UpdateStatus("Finished Patching", false);
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

    private void MainLoop(
        IEnumerable<INpcGetter> npcCollection, bool skipLinkedSecondaryNPCs, SkyrimMod outputMod,
        CategorizedFlattenedAssetPacks sortedAssetPacks, BodyGenConfigs bodyGenConfigs, Settings_OBody oBodySettings,
        HeightConfig currentHeightConfig, Settings_Headparts headPartSettings, int npcCounter,
        HashSet<LinkedNPCGroupInfo> generatedLinkGroups, HashSet<INpcGetter> skippedLinkedNPCs,
        Keyword EBDFaceKW, Keyword EBDScriptKW, Spell bodySlideAssignmentSpell,
        HashSet<Npc> headPartNPCs)
    {
        bool blockAssets;
        bool blockBodyShape;
        bool blockHeight;
        bool assetsAssigned = false;
        bool bodyShapeAssigned = false;

        BlockedNPC blockListNPCEntry;
        BlockedPlugin blockListPluginEntry;

        HashSet<FlattenedAssetPack> primaryAssetPacks = new HashSet<FlattenedAssetPack>();
        HashSet<FlattenedAssetPack> mixInAssetPacks = new HashSet<FlattenedAssetPack>();

        List<SubgroupCombination> assignedCombinations = new List<SubgroupCombination>();
        HashSet<LinkedNPCGroup> linkedGroupsHashSet = PatcherSettings.General.LinkedNPCGroups.ToHashSet();

        foreach (var npc in npcCollection)
        {
            npcCounter++;

            var currentNPCInfo = new NPCInfo(npc, linkedGroupsHashSet, generatedLinkGroups, _state.SpecificNPCAssignments, _state.Consistency);
            if (!currentNPCInfo.IsPatchable)
            {
                continue;
            }

            assetsAssigned = false;
            bodyShapeAssigned = false;
            assignedCombinations = new List<SubgroupCombination>();
            BodySlideSetting assignedBodySlide = null; // can be used by headpart function
            Dictionary<HeadPart.TypeEnum, HeadPart> generatedHeadParts = GetBlankHeadPartAssignment(); // head parts generated via the asset pack functionality

            #region Linked NPC Groups
            if (skipLinkedSecondaryNPCs && currentNPCInfo.LinkGroupMember == NPCInfo.LinkGroupMemberType.Secondary)
            {
                skippedLinkedNPCs.Add(npc);
                continue;
            }
            else
            {
                _statusBar.ProgressBarCurrent++;
            }
            #endregion

            if (_statusBar.ProgressBarCurrent % 1000 == 0)
            {
                _statusBar.ProgressBarDisp = "Patched " + _statusBar.ProgressBarCurrent + " NPCs";
            }

            #region link by name
            if (PatcherSettings.General.bLinkNPCsWithSameName && currentNPCInfo.IsValidLinkedUnique)
            {
                UniqueNPCData.InitializeUniqueNPC(currentNPCInfo);
            }
            #endregion

            #region Detailed logging
            if (PatcherSettings.General.VerboseModeNPClist.Contains(npc.FormKey) || PatcherSettings.General.bVerboseModeAssetsAll || PatcherSettings.General.bVerboseModeAssetsNoncompliant)
            {
                Logger.TriggerNPCReporting(currentNPCInfo);
            }
            if (PatcherSettings.General.VerboseModeNPClist.Contains(npc.FormKey) || PatcherSettings.General.bVerboseModeAssetsAll)
            {
                Logger.TriggerNPCReportingSave(currentNPCInfo);
            }

            Logger.InitializeNewReport(currentNPCInfo);
            #endregion

            #region Block List
            blockListNPCEntry = BlockListHandler.GetCurrentNPCBlockStatus(_state.BlockList, npc.FormKey);
            blockListPluginEntry = BlockListHandler.GetCurrentPluginBlockStatus(_state.BlockList, npc.FormKey);

            if (blockListNPCEntry.Assets || blockListPluginEntry.Assets || AssetSelector.BlockAssetDistributionByExistingAssets(currentNPCInfo)) { blockAssets = true; }
            else { blockAssets = false; }

            if (blockListNPCEntry.BodyShape || blockListPluginEntry.BodyShape || !OBodyPreprocessing.NPCIsEligibleForBodySlide(npc)) { blockBodyShape = true; }
            else { blockBodyShape = false; }

            if (blockListNPCEntry.Height || blockListPluginEntry.Height) { blockHeight = true; }
            else { blockHeight = false; }
            #endregion

            bodyShapeAssigned = false;

            if (PatcherSettings.General.ExcludePlayerCharacter && npc.FormKey.ToString() == Skyrim.Npc.Player.FormKey.ToString())
            {
                continue;
            }

            if (PatcherSettings.General.ExcludePresets && npc.EditorID != null && npc.EditorID.Contains("Preset"))
            {
                continue;
            }

            AssetAndBodyShapeSelector.AssetAndBodyShapeAssignment assignedPrimaryComboAndBodyShape = new AssetAndBodyShapeSelector.AssetAndBodyShapeAssignment();

            #region Primary Assets (and optionally Body Shape) assignment
            if (PatcherSettings.General.bChangeMeshesOrTextures && !blockAssets && PatcherSettings.General.PatchableRaces.Contains(currentNPCInfo.AssetsRace))
            {
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

                assignedPrimaryComboAndBodyShape = AssetAndBodyShapeSelector.ChooseCombinationAndBodyShape(out assetsAssigned, out bodyShapeAssigned, primaryAssetPacks, bodyGenConfigs, oBodySettings, currentNPCInfo, blockBodyShape, AssetAndBodyShapeSelector.AssetPackAssignmentMode.Primary, null);
                if (assetsAssigned)
                {
                    assignedCombinations.Add(assignedPrimaryComboAndBodyShape.AssignedCombination);
                    AssetSelector.RecordAssetConsistencyAndLinkedNPCs(assignedPrimaryComboAndBodyShape.AssignedCombination, currentNPCInfo);
                }
                if (bodyShapeAssigned)
                {
                    switch (PatcherSettings.General.BodySelectionMode)
                    {
                        case BodyShapeSelectionMode.BodyGen:
                            BodyGenTracker.NPCAssignments.Add(currentNPCInfo.NPC.FormKey, assignedPrimaryComboAndBodyShape.AssignedBodyGenMorphs.Select(x => x.Label).ToList());
                            BodyGenSelector.RecordBodyGenConsistencyAndLinkedNPCs(assignedPrimaryComboAndBodyShape.AssignedBodyGenMorphs, currentNPCInfo);
                            break;

                        case BodyShapeSelectionMode.BodySlide:
                            BodySlideTracker.Add(currentNPCInfo.NPC.FormKey, assignedPrimaryComboAndBodyShape.AssignedOBodyPreset.Label);
                            OBodySelector.RecordBodySlideConsistencyAndLinkedNPCs(assignedPrimaryComboAndBodyShape.AssignedOBodyPreset, currentNPCInfo);
                            assignedBodySlide = assignedPrimaryComboAndBodyShape.AssignedOBodyPreset;
                            break;
                    }
                }
            }
            #endregion

            #region Body Shape assignment (if assets not assigned with Assets)
            switch (PatcherSettings.General.BodySelectionMode)
            {
                case BodyShapeSelectionMode.None: break;

                case BodyShapeSelectionMode.BodyGen:
                    if (!blockBodyShape && PatcherSettings.General.PatchableRaces.Contains(currentNPCInfo.BodyShapeRace) && !bodyShapeAssigned && BodyGenSelector.BodyGenAvailableForGender(currentNPCInfo.Gender, bodyGenConfigs))
                    {
                        Logger.LogReport("Assigning a BodyGen morph independently of Asset Combination", false, currentNPCInfo);
                        var assignedMorphs = BodyGenSelector.SelectMorphs(currentNPCInfo, out bool success, bodyGenConfigs, null, out _);
                        if (success)
                        {
                            BodyGenTracker.NPCAssignments.Add(currentNPCInfo.NPC.FormKey, assignedMorphs.Select(x => x.Label).ToList());
                            BodyGenSelector.RecordBodyGenConsistencyAndLinkedNPCs(assignedMorphs, currentNPCInfo);
                            assignedPrimaryComboAndBodyShape.AssignedBodyGenMorphs = assignedMorphs;
                        }
                        else
                        {
                            Logger.LogReport("Could not independently assign a BodyGen Morph.", true, currentNPCInfo);
                        }
                    }
                    break;
                case BodyShapeSelectionMode.BodySlide:
                    if (!blockBodyShape && PatcherSettings.General.PatchableRaces.Contains(currentNPCInfo.BodyShapeRace) && !bodyShapeAssigned && OBodySelector.CurrentNPCHasAvailablePresets(currentNPCInfo, oBodySettings))
                    {
                        Logger.LogReport("Assigning a BodySlide preset independently of Asset Combination", false, currentNPCInfo);
                        assignedBodySlide = OBodySelector.SelectBodySlidePreset(currentNPCInfo, out bool success, oBodySettings, null, out _);
                        if (success)
                        {
                            BodySlideTracker.Add(currentNPCInfo.NPC.FormKey, assignedBodySlide.Label);
                            OBodySelector.RecordBodySlideConsistencyAndLinkedNPCs(assignedBodySlide, currentNPCInfo);
                            assignedPrimaryComboAndBodyShape.AssignedOBodyPreset = assignedBodySlide;
                        }
                        else
                        {
                            Logger.LogReport("Could not independently assign a BodySlide preset.", true, currentNPCInfo);
                        }
                    }
                    break;
            }
            #endregion

            // now that Body Shapes have been assigned, finish assigning mix-in combinations and asset replacers, and write them to the output file
            if (PatcherSettings.General.bChangeMeshesOrTextures && !blockAssets && PatcherSettings.General.PatchableRaces.Contains(currentNPCInfo.AssetsRace))
            {
                Dictionary<FormKey, Dictionary<string, dynamic>> objectCaches = new Dictionary<FormKey, Dictionary<string, dynamic>>();

                #region MixIn Asset assignment
                bool mixInAssigned = false;
                foreach (var mixInConfig in mixInAssetPacks)
                {
                    var assignedMixIn = AssetAndBodyShapeSelector.ChooseCombinationAndBodyShape(out mixInAssigned, out _, new HashSet<FlattenedAssetPack>() { mixInConfig }, bodyGenConfigs, oBodySettings, currentNPCInfo, blockBodyShape, AssetAndBodyShapeSelector.AssetPackAssignmentMode.MixIn, assignedPrimaryComboAndBodyShape);
                    if (mixInAssigned)
                    {
                        assignedCombinations.Add(assignedMixIn.AssignedCombination);
                        AssetSelector.RecordAssetConsistencyAndLinkedNPCs(assignedMixIn.AssignedCombination, currentNPCInfo, mixInConfig.GroupName);
                        assetsAssigned = true;
                    }
                }
                #endregion

                #region Asset Replacer assignment
                HashSet<SubgroupCombination> assetReplacerCombinations = new HashSet<SubgroupCombination>();
                if (assetsAssigned) // assign direct replacers that a come from the assigned primary asset pack
                {
                    foreach (var combination in assignedCombinations)
                    {
                        assetReplacerCombinations.UnionWith(AssetSelector.SelectAssetReplacers(combination.AssetPack, currentNPCInfo, assignedPrimaryComboAndBodyShape));
                    }
                }
                foreach (var replacerOnlyPack in mixInAssetPacks.Where(x => !x.Subgroups.Any() && x.AssetReplacerGroups.Any())) // add asset replacers from mix-in asset packs that ONLY have replacer assets, since they won't be contained in assignedCombinations
                {
                    assetReplacerCombinations.UnionWith(AssetSelector.SelectAssetReplacers(replacerOnlyPack, currentNPCInfo, assignedPrimaryComboAndBodyShape));
                }
                assignedCombinations.AddRange(assetReplacerCombinations);
                #endregion

                #region Generate Records
                if (assignedCombinations.Any())
                {
                    var npcRecord = outputMod.Npcs.GetOrAddAsOverride(currentNPCInfo.NPC);
                    var npcObjectMap = new Dictionary<string, dynamic>(StringComparer.OrdinalIgnoreCase) { { "", npcRecord } };
                    var assignedPaths = new List<FilePathReplacementParsed>(); // for logging only
                    RecordGenerator.CombinationToRecords(assignedCombinations, currentNPCInfo, _state.RecordTemplateLinkCache, npcObjectMap, objectCaches, outputMod, assignedPaths, generatedHeadParts);
                    _combinationLog.LogAssignment(currentNPCInfo, assignedCombinations, assignedPaths);
                    if (npcRecord.Keywords == null) { npcRecord.Keywords = new Noggog.ExtendedList<IFormLinkGetter<IKeywordGetter>>(); }
                    npcRecord.Keywords.Add(EBDFaceKW);
                    npcRecord.Keywords.Add(EBDScriptKW);
                    RecordGenerator.AddKeywordsToNPC(assignedCombinations, npcRecord, outputMod);

                    if (assignedPaths.Where(x => x.DestinationStr.StartsWith("HeadParts")).Any())
                    {
                        headPartNPCs.Add(npcRecord);
                    }
                }
                #endregion
            }

            if (PatcherSettings.TexMesh.bForceVanillaBodyMeshPath)
            {
                AssetSelector.SetVanillaBodyPath(currentNPCInfo, outputMod);
            }

            #region Height assignment
            if (PatcherSettings.General.bChangeHeight && !blockHeight && PatcherSettings.General.PatchableRaces.Contains(currentNPCInfo.HeightRace))
            {
                HeightPatcher.AssignNPCHeight(currentNPCInfo, currentHeightConfig, outputMod);
            }
            #endregion

            #region Head Part assignment
            HeadPartSelection assignedHeadParts = new();
            if (PatcherSettings.General.bChangeHeadParts)
            {
                assignedHeadParts = HeadPartSelector.AssignHeadParts(currentNPCInfo, headPartSettings, blockListNPCEntry, blockListPluginEntry, assignedBodySlide);
            }

            if (PatcherSettings.General.bChangeMeshesOrTextures) // needs to be done regardless of PatcherSettings.General.bChangeHeadParts status
            {
                HeadPartSelector.ResolveConflictsWithAssetAssignments(generatedHeadParts, assignedHeadParts);
                CheckForAssetDerivedHeadParts(generatedHeadParts); // triggers headpart output even if bChangeHeadParts is false
            }

            HeadPartTracker.Add(currentNPCInfo.NPC.FormKey, assignedHeadParts);
            #endregion

            Logger.SaveReport(currentNPCInfo);
        }
    }

    private static void UpdateRecordTemplateAdditonalRaces(List<AssetPack> assetPacks, ILinkCache<ISkyrimMod, ISkyrimModGetter> recordTemplateLinkCache, List<SkyrimMod> recordTemplatePlugins)
    {
        Dictionary<string, HashSet<string>> patchedTemplates = new Dictionary<string, HashSet<string>>();

        foreach (var assetPack in assetPacks)
        {
            var exclusions = assetPack.AdditionalRecordTemplateAssignments.SelectMany(x => x.Races).ToHashSet(); // don't include races that get their own record template in the default template's patching
            var racesToAdd = PatcherSettings.General.PatchableRaces.Where(x => !FormKeyHashSetComparer.Contains(exclusions, x)).ToHashSet();
            SetRecordTemplateAdditionalRaces(assetPack.DefaultRecordTemplateAdditionalRacesPaths, assetPack.DefaultRecordTemplate, racesToAdd, patchedTemplates, recordTemplateLinkCache, recordTemplatePlugins);

            foreach (var additionalTemplateEntry in assetPack.AdditionalRecordTemplateAssignments)
            {
                SetRecordTemplateAdditionalRaces(additionalTemplateEntry.AdditionalRacesPaths, additionalTemplateEntry.TemplateNPC, additionalTemplateEntry.Races, patchedTemplates, recordTemplateLinkCache, recordTemplatePlugins);
            }
        }
    }

    private static void SetRecordTemplateAdditionalRaces(HashSet<string> additionalRacesPaths, FormKey templateFK, HashSet<FormKey> racesToAdd, Dictionary<string, HashSet<string>> alreadyPatchedTemplates, ILinkCache<ISkyrimMod, ISkyrimModGetter> recordTemplateLinkCache, List<SkyrimMod> recordTemplatePlugins)
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
                Logger.LogError("Could not find record template plugin " + templateFK.ToString());
            }

            if (recordTemplateLinkCache.TryResolve<INpcGetter>(templateFK, out var template))
            {
                try
                {
                    if (!RecordPathParser.GetNearestParentGetter(template, path, recordTemplateLinkCache, false, Logger.GetNPCLogNameString(template), out IMajorRecordGetter parentRecordGetter, out string relativePath))
                    {
                        continue;
                    }

                    var parentRecord = RecordGenerator.GetOrAddGenericRecordAsOverride(parentRecordGetter, templateMod);

                    if (RecordPathParser.GetObjectAtPath(parentRecord, template, relativePath, new Dictionary<string, dynamic>(), recordTemplateLinkCache, false, Logger.GetNPCLogNameString(template), out dynamic additionalRaces))
                    {
                        foreach (var race in racesToAdd)
                        {
                            additionalRaces.Add(race.AsLink<IRaceGetter>());
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
                    Logger.LogError("Could not patch additional races expected at " + path + " in template NPC " + Logger.GetNPCLogNameString(template));
                    continue;
                }
            }
            else
            {
                Logger.LogError("Could not resolve template NPC " + templateFK.ToString());
            }
        }
    }

    public static void ResolvePatchableRaces()
    {
        if (PatcherEnvironmentProvider.Instance.Environment.LinkCache is null)
        {
            Logger.LogError("Error: Link cache is null.");
            return;
        }

        PatchableRaces = new HashSet<IFormLinkGetter<IRaceGetter>>();
        foreach (var raceFK in PatcherSettings.General.PatchableRaces)
        {
            if (PatcherEnvironmentProvider.Instance.Environment.LinkCache.TryResolve<IRaceGetter>(raceFK, out var raceGetter))
            {
                PatchableRaces.Add(raceGetter.ToLinkGetter());
            }
        }
        PatchableRaces.Add(Skyrim.Race.DefaultRace.Resolve(PatcherEnvironmentProvider.Instance.Environment.LinkCache).ToLinkGetter());
    }

    public static BodyGenAssignmentTracker BodyGenTracker = new BodyGenAssignmentTracker(); // tracks unique selected morphs so that only assigned morphs are written to the generated templates.ini
    public static Dictionary<FormKey, string> BodySlideTracker = new Dictionary<FormKey, string>(); // tracks which NPCs get which bodyslide presets
    public static Dictionary<FormKey, HeadPartSelection> HeadPartTracker = new();
    public class BodyGenAssignmentTracker
    {
        public Dictionary<FormKey, List<string>> NPCAssignments = new();
        public Dictionary<string, HashSet<string>> AllChosenMorphsMale = new();
        public Dictionary<string, HashSet<string>> AllChosenMorphsFemale = new();
    }

    public static Dictionary<HeadPart.TypeEnum, HeadPart> GetBlankHeadPartAssignment()
    {
        return new Dictionary<HeadPart.TypeEnum, HeadPart>()
        {
            { HeadPart.TypeEnum.Eyebrows, null },
            { HeadPart.TypeEnum.Eyes, null },
            { HeadPart.TypeEnum.Face, null },
            { HeadPart.TypeEnum.FacialHair, null },
            { HeadPart.TypeEnum.Hair, null },
            { HeadPart.TypeEnum.Misc, null },
            { HeadPart.TypeEnum.Scars, null }
        };
    }

    public static void SetGeneratedHeadPart(HeadPart hp, Dictionary<HeadPart.TypeEnum, HeadPart> dict)
    {
        if (hp.Type != null)
        {
            dict[hp.Type.Value] = hp;
        }
        else
        {
            Logger.LogMessage("Cannot assign a head part replacer for head part " + EditorIDHandler.GetEditorIDSafely(hp) + " because it does not have a specified Type.");
        }
    }

    public void CheckForAssetDerivedHeadParts(Dictionary<HeadPart.TypeEnum, HeadPart> assignments)
    {
        if (HasAssetDerivedHeadParts) { return; }
        
        foreach (var headPart in assignments.Values)
        {
            if (headPart != null)
            {
                HasAssetDerivedHeadParts = true;
                return;
            }
        }
    }

    private bool HasAssetDerivedHeadParts { get; set; } = false;
}