using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;
using Mutagen.Bethesda.Plugins.Records;
using Loqui;
using Mutagen.Bethesda.Plugins.Cache;


namespace SynthEBD;

/// <summary>
/// Turns a chosen <see cref="SubgroupCombination"/> into actual Skyrim record overrides on the output mod: resolves
/// record paths via <see cref="RecordPathParser"/>, deep-copies/overrides WornArmor/Armature/HeadTexture/TextureSet
/// (and head parts), assigns the asset file paths, deduplicates generated records, and emits SkyPatcher/keyword side
/// effects. Runs after <see cref="AssetSelector"/> in the patching pipeline. Holds per-run static dedup caches.
/// </summary>
public class RecordGenerator
{
    private readonly IOutputEnvironmentStateProvider _environmentProvider;
    private readonly PatcherState _patcherState;
    private readonly Logger _logger;
    private readonly HardcodedRecordGenerator _hardcodedRecordGenerator;
    private readonly HeadPartSelector _headPartSelector;
    private readonly RecordPathParser _recordPathParser;
    private readonly SurrogateNPCProvider _surrogateNpcProvider;
    private readonly ArmorPatcher _armorPatcher;
    private readonly SkinPatcher _skinPatcher;
    private readonly FacePartCompliance _facePartComplianceMaintainer;
    private readonly SkyPatcherInterface _skyPatcherInterface;
    private readonly HeadPartAuxFunctions _headPartAuxFunctions;
    private HashSet<FormKey> skinWNAMsToStrip;
    
    /// <summary>Resolves the many collaborators used during record generation (output mod, path parser, hardcoded generator, head-part/armor/skin patchers, SkyPatcher interface, etc.).</summary>
    public RecordGenerator(IOutputEnvironmentStateProvider environmentProvider, PatcherState patcherState, Logger logger, SynthEBDPaths paths, HardcodedRecordGenerator hardcodedRecordGenerator, HeadPartSelector headPartSelector, RecordPathParser recordPathParser, SurrogateNPCProvider surrogateNpcProvider, ArmorPatcher armorPatcher, SkinPatcher skinPatcher, FacePartCompliance facePartComplianceMaintainer, SkyPatcherInterface skyPatcherInterface, HeadPartAuxFunctions headPartAuxFunctions)
    {
        _environmentProvider = environmentProvider;
        _patcherState = patcherState;
        _logger = logger;
        _hardcodedRecordGenerator = hardcodedRecordGenerator;
        _headPartSelector = headPartSelector;
        _recordPathParser = recordPathParser;
        _surrogateNpcProvider = surrogateNpcProvider;
        _armorPatcher = armorPatcher;
        _skinPatcher = skinPatcher;
        _facePartComplianceMaintainer = facePartComplianceMaintainer;
        _skyPatcherInterface = skyPatcherInterface;
        _headPartAuxFunctions = headPartAuxFunctions;
        skinWNAMsToStrip = new();
    }

    /// <summary>
    /// Resets all per-run state: the static dedup dictionaries and EditorID counters, the face-part compliance maintainer,
    /// and the set of worn-armor FormKeys whose skins should be stripped (resolved from the configured stripped-WNAM
    /// EditorIDs, including "Patched" variants in EasyNPC compatibility mode).
    /// </summary>
    public void Reinitialize()
    {
        ModifiedRecordCounts = new Dictionary<string, int>();
        ModifiedRecords = new Dictionary<HashSet<string>, Dictionary<string, IMajorRecord>>(HashSet<string>.CreateSetComparer());
        CachedObjectsByPathAndTemplate = new Dictionary<HashSet<string>, Dictionary<string, Dictionary<HashSet<string>, ObjectAtIndex>>>(HashSet<string>.CreateSetComparer());
        GeneratedRecordsByTempateNPC = new Dictionary<HashSet<string>, Dictionary<string, IMajorRecord>>(HashSet<string>.CreateSetComparer());
        EdidCounts = new Dictionary<string, int>();
        GeneratedKeywords = new Dictionary<string, Keyword>(); // reset per run: the cached Keyword records live in the per-run output mod, so a stale entry would orphan-reference a discarded mod
        _facePartComplianceMaintainer.Reinitialize();

        skinWNAMsToStrip = new();
        var editorIDsToSearch = new HashSet<string>(_patcherState.TexMeshSettings.StrippedSkinWNAMs);
        if (_patcherState.TexMeshSettings.bEasyNPCCompatibilityMode && editorIDsToSearch.Any())
        {
            var patchedEditorIDs = new HashSet<string>();
            foreach (var editorID in editorIDsToSearch)
            {
                if (!editorID.EndsWith("Patched"))
                {
                    patchedEditorIDs.Add(editorID + "Patched");
                }
            }

            editorIDsToSearch.UnionWith(patchedEditorIDs);
        }

        var armors = _environmentProvider.LoadOrder.PriorityOrder.OnlyEnabledAndExisting().WinningOverrides<IArmorGetter>().ToArray();
        foreach (var armor in armors)
        {
            if (editorIDsToSearch.Contains(EditorIDHandler.GetEditorIDSafely(armor)))
            {
                skinWNAMsToStrip.Add(armor.FormKey);
            }
        }
    }

    /// <summary>
    /// Top-level driver: iterates every NPC's selected assets and writes record overrides for each. Per NPC it obtains a
    /// surrogate or direct NPC override, strips configured skin armor, converts the combination to records, adds EBD/face
    /// keywords and custom keywords, patches armor/skin alt-textures, emits SkyPatcher SetSkin in script mode, logs JSON
    /// assignments, and collects generated head parts (or warns when head-part patching is disabled). Mutates the output
    /// mod, <paramref name="generatedHeadPartsDictionary"/>, the combination log, and the UI status bar.
    /// </summary>
    public void ApplySelectedAssets(Dictionary<FormKey, (NPCInfo NpcInfo, List<Patcher.SelectedAssetContainer> Assets)> selectedAssets, HashSet<FlattenedAssetPack> flattenedAssetPacks, Dictionary<FormKey, (NPCInfo NpcInfo, Dictionary<HeadPart.TypeEnum, FormKey> HeadParts)> generatedHeadPartsDictionary, CombinationLog combinationLog, Keyword EBDFaceKW, Keyword EBDScriptKW, Keyword synthEBDFaceKW, AssetAssignmentJsonDictHandler assetAssignmentJsonDictHandler, VM_StatusBar statusBar)
    {
        generatedHeadPartsDictionary.Clear();
        HashSet<string> suppressedHeadPartPackNames = new();
        
        statusBar.ProgressBarMax = selectedAssets.Count;
        foreach (var npcAssetEntry in selectedAssets)
        {
            statusBar.ProgressBarCurrent++;
            
            if (statusBar.ProgressBarCurrent % 100 == 0 || statusBar.ProgressBarCurrent == statusBar.ProgressBarMax)
            {
                statusBar.ProgressBarDisp = "Applied selections for " + statusBar.ProgressBarCurrent + " NPCs";
            }
            
            var currentNPCInfo = npcAssetEntry.Value.NpcInfo;
            var assignments = npcAssetEntry.Value.Assets;
            
            if (assignments.Any())
            {
                Npc npcRecord;
                if (_patcherState.TexMeshSettings.bSkyPatcherModeAssets)
                {
                    if (_surrogateNpcProvider.TryGetSurrogateNpc(currentNPCInfo.NPC, out var surrogateNpc))
                    {
                        npcRecord = surrogateNpc;
                        currentNPCInfo.NPC = npcRecord;
                    }
                    else
                    {
                        _logger.LogMessage("WARNING: Could not create surrogate for NPC " +
                                           currentNPCInfo.NPC.FormKey + ". Falling back to direct override.");
                        npcRecord = _environmentProvider.OutputMod.Npcs.GetOrAddAsOverride(currentNPCInfo.NPC);
                    }
                }
                else
                {
                    npcRecord = _environmentProvider.OutputMod.Npcs.GetOrAddAsOverride(currentNPCInfo.NPC);
                }
                
                if (_patcherState.TexMeshSettings.StrippedSkinWNAMs.Any())
                {
                    currentNPCInfo.NPC = StripSpecifiedSkinArmor(npcRecord, _environmentProvider.LinkCache, _environmentProvider.OutputMod);
                }
                
                var npcObjectMap = new Dictionary<string, dynamic>(StringComparer.OrdinalIgnoreCase) { { "", npcRecord } };
                var objectCaches = new Dictionary<FormKey, Dictionary<string, dynamic>>();
                var replacedRecords = new Dictionary<FormKey, FormKey>();
                var recordsFromTemplates = new HashSet<IMajorRecord>(); // needed for downstream quality check
                var assignedPaths = new List<FilePathReplacementParsed>(); // for logging only
                var generatedHeadPartFormKeys = new Dictionary<HeadPart.TypeEnum, FormKey>();
                CombinationToRecords(assignments, flattenedAssetPacks, currentNPCInfo, _patcherState.RecordTemplateLinkCache, npcObjectMap, objectCaches, replacedRecords, recordsFromTemplates, assignedPaths, generatedHeadPartFormKeys);
                combinationLog.LogAssignedRecords(currentNPCInfo, assignments);

                if (_patcherState.TexMeshSettings.FacePatchingMode == FacePatchingMode.Script)
                {
                    _facePartComplianceMaintainer.CheckAndFixFaceName(currentNPCInfo);

                    if (npcRecord.Keywords == null)
                    {
                        npcRecord.Keywords = new Noggog.ExtendedList<IFormLinkGetter<IKeywordGetter>>();
                    }

                    if (npcRecord.HeadTexture.TryGetModKey(out var headTextureSourceMod) &&
                        headTextureSourceMod.Equals(_environmentProvider.OutputMod
                            .ModKey)) // if the patcher tried to patch but didn't set a head texture, don't apply the headpart script to this NPC
                    {
                        if (_patcherState.TexMeshSettings.bLegacyEBDMode)
                        {
                            npcRecord.Keywords.Add(EBDFaceKW);
                            npcRecord.Keywords.Add(EBDScriptKW);
                        }
                        else
                        {
                            npcRecord.Keywords.Add(synthEBDFaceKW);
                        }
                    }
                }

                AddCustomKeywordsToNPC(assignments, npcRecord, _environmentProvider.OutputMod);

                if (_patcherState.TexMeshSettings.bPatchArmors)
                {
                    _armorPatcher.PatchArmorTextures(currentNPCInfo, replacedRecords, _environmentProvider.OutputMod);
                }
                if (_patcherState.TexMeshSettings.bPatchSkinAltTextures)
                {
                    _skinPatcher.PatchAltTextures(currentNPCInfo, replacedRecords, _environmentProvider.OutputMod);
                }
                _skinPatcher.ValidateArmorFlags(npcRecord, recordsFromTemplates, _environmentProvider.OutputMod);

                if (_patcherState.TexMeshSettings.bSkyPatcherModeAssets && _patcherState.TexMeshSettings.FacePatchingMode == FacePatchingMode.Script)
                {
                    // Script mode: emit standalone SetSkin here. CopyVisualStyle is not
                    // needed because face textures are applied by script at runtime.
                    // Mesh mode: SetSkin is deferred to the unified FaceGen loop where it
                    // is combined with CopyVisualStyle via ApplySkinAndVisualStyle on a
                    // single ini line.
                    _skyPatcherInterface.ApplySkin(currentNPCInfo.OriginalNPC.FormKey, npcRecord.WornArmor.FormKey);
                }
                
                assetAssignmentJsonDictHandler.LogNPCAssignments(currentNPCInfo, _environmentProvider.OutputMod);
                
                if (generatedHeadPartFormKeys.Any())
                {
                    if (_patcherState.GeneralSettings.bChangeHeadParts)
                    {
                        // Headpart patching is enabled — add to dictionary for downstream processing
                        var npcFk = currentNPCInfo.NPC.FormKey;
                        if (generatedHeadPartsDictionary.TryGetValue(npcFk, out var existingGenHp))
                        {
                            foreach (var genHpKvp in generatedHeadPartFormKeys)
                            {
                                existingGenHp.HeadParts.TryAdd(genHpKvp.Key, genHpKvp.Value);
                            }
                        }
                        else
                        {
                            generatedHeadPartsDictionary[npcFk] = (currentNPCInfo, generatedHeadPartFormKeys);
                        }
                    }
                    else
                    {
                        // Headpart patching is disabled — suppress these headparts and
                        // record which asset packs tried to generate them
                        foreach (var a in assignments)
                        {
                            suppressedHeadPartPackNames.Add(a.AssetPackName);
                        }
                    }
                }
            }
        }
        
        if (suppressedHeadPartPackNames.Any())
        {
            var sortedPackNames = suppressedHeadPartPackNames.OrderBy(n => n).ToList();
            _logger.LogMessage("");
            _logger.LogMessage("=======================Warning========================");
            _logger.LogMessage("The following config files are attempting to patch headparts, but headpart patching is disabled in your General Settings. These headparts will not be applied in-game unless you enable headpart patching");
            foreach (var packName in sortedPackNames)
            {
                _logger.LogMessage("* " + packName);
            }
            _logger.LogMessage("=====================================================");
            _logger.LogMessage("");
        }
    }

    /// <summary>
    /// Converts one NPC's asset assignments into records: categorizes the file paths into WornArmor/HeadTexture/generic
    /// buckets, short-circuits if nothing is assignable (to avoid creating an ITM), assigns hardcoded records, then
    /// assigns the remaining generic paths, and finally records the traversed records back onto the assignments for logging.
    /// </summary>
    public void CombinationToRecords(List<Patcher.SelectedAssetContainer> assignments, HashSet<FlattenedAssetPack> flattenedAssetPacks, NPCInfo npcInfo, ILinkCache<ISkyrimMod, ISkyrimModGetter> recordTemplateLinkCache, Dictionary<string, dynamic> npcObjectMap, Dictionary<FormKey, Dictionary<string, dynamic>> objectCaches, Dictionary<FormKey, FormKey> replacedRecords, HashSet<IMajorRecord> recordsFromTemplates, List<FilePathReplacementParsed> assignedPaths, Dictionary<HeadPart.TypeEnum, FormKey> generatedHeadParts)
    {
        HashSet<FilePathReplacementParsed> wnamPaths = new HashSet<FilePathReplacementParsed>();
        HashSet<FilePathReplacementParsed> headtexPaths = new HashSet<FilePathReplacementParsed>();
        List<FilePathReplacementParsed> nonHardcodedPaths = new List<FilePathReplacementParsed>();

        _hardcodedRecordGenerator.CategorizePaths(assignments, flattenedAssetPacks, npcInfo, recordTemplateLinkCache, wnamPaths, headtexPaths, nonHardcodedPaths, out int longestPath, true); 

        if (!nonHardcodedPaths.Any() && !wnamPaths.Any() && !headtexPaths.Any()) { return; } // avoid making ITM if user blocks all assets of the type assigned (see AssetSelector.BlockAssetDistributionByExistingAssets())

        var currentNPC = _environmentProvider.OutputMod.Npcs.GetOrAddAsOverride(npcInfo.NPC);
        objectCaches.Add(npcInfo.NPC.FormKey, new Dictionary<string, dynamic>(StringComparer.OrdinalIgnoreCase) { { "", currentNPC } });

        _hardcodedRecordGenerator.AssignHardcodedRecords(wnamPaths, headtexPaths, npcInfo, recordTemplateLinkCache, npcObjectMap, objectCaches, replacedRecords, recordsFromTemplates, this);

        // snapshot before AssignGenericAssetPaths modifies the list (removes paths as they complete or hit cache)
        var allNonHardcodedPaths = nonHardcodedPaths.ToList();

        if (nonHardcodedPaths.Any())
        {
            AssignGenericAssetPaths(npcInfo, nonHardcodedPaths, currentNPC, recordTemplateLinkCache, longestPath, true, false, npcObjectMap, objectCaches, assignedPaths, generatedHeadParts, replacedRecords, recordsFromTemplates);
        }

        //logging: compile traversed records from all original paths (nonHardcodedPaths is partially/fully cleared by AssignGenericAssetPaths)
        foreach (var p in allNonHardcodedPaths)
        {
            var entry = assignments.FirstOrDefault(x => x.AssetPackName == p.AssetPackName);
            if (entry == null) { continue; }
            foreach (var record in p.TraversedRecords)
            {
                entry.TraversedRecords.Add(record);
            }
        }
    }

    /// <summary>Pairs a deep-copied subrecord with the set of template NPCs (signature) it was derived from, so the generated object can later be cached by that signature.</summary>
    private class TemplateSignatureRecordPair
    {
        public HashSet<INpcGetter> TemplateSignature { get; set; }
        public IMajorRecord SubRecord { get; set; }
    }

    // assignedPaths is for logging purposes only
    /// <summary>
    /// Walks every generic (non-hardcoded) destination path one segment at a time, grouping paths by shared prefix. At
    /// each segment it locates or creates the object: assigns the source asset string at leaf segments; traverses
    /// existing objects on the NPC setter/getter (deep-copying records to the patch as needed); or pulls objects from the
    /// record templates (with caching). Head parts get special handling. Mutates the output mod and the various caches.
    /// <paramref name="assignedPaths"/> is for logging only.
    /// </summary>
    public void AssignGenericAssetPaths(NPCInfo npcInfo, List<FilePathReplacementParsed> nonHardcodedPaths, Npc rootNPC, ILinkCache<ISkyrimMod, ISkyrimModGetter> recordTemplateLinkCache, int longestPath, bool canAssignFromTemplate, bool suppressMissingPathErrors, Dictionary<string, dynamic> npcObjectMap, Dictionary<FormKey, Dictionary<string, dynamic>> objectCaches, List<FilePathReplacementParsed> assignedPaths, Dictionary<HeadPart.TypeEnum, FormKey> generatedHeadParts, Dictionary<FormKey, FormKey> replacedRecords, HashSet<IMajorRecord> recordsFromTemplates)
    {
        HashSet<TemplateSignatureRecordPair> templateSubRecords = new HashSet<TemplateSignatureRecordPair>();

        dynamic currentObj = null;

        for (int i = 0; i < longestPath; i++)
        {
            #region Remove paths that were already assigned
            for (int j = 0; j < nonHardcodedPaths.Count; j++)
            {
                if (i == nonHardcodedPaths[j].Destination.Length)
                {
                    nonHardcodedPaths.RemoveAt(j);
                    j--;
                }
            }
            #endregion

            var groupedPathsAtI = nonHardcodedPaths.GroupBy(x => BuildPath(x.Destination.ToList().GetRange(0, i + 1))).ToArray(); // group paths by the current path segment

            foreach (var group in groupedPathsAtI)
            {
                #region Get root object at current path segment
                string parentPath = BuildPath(group.First().Destination.ToList().GetRange(0, i));
                string currentSubPath = group.First().Destination[i];
                var rootObj = npcObjectMap[parentPath];
                if (rootObj == null)
                {
                    _logger.LogError(_logger.GetNPCLogNameString(npcInfo.NPC) + ": Expected and failed to find an object at path: " + parentPath + ". Subrecords will not be assigned. Please report this error.");
                    RemovePathsFromList(nonHardcodedPaths, group);
                    continue;
                }
                var pathSignature = group.Select(x => x.Source).ToHashSet();
                #endregion

                // step through the path
                bool skipObjectMapAssignment = false;
                bool npcSetterHasObject = npcObjectMap.ContainsKey(group.Key);
                ObjectInfo currentObjInfo = null;

                #region Assign asset paths
                if (group.First().Destination.Length == i + 1) // if this is the last part of the path, attempt to assign the Source asset to the Destination
                {
                    foreach (var assetAssignment in group)
                    {
                        _recordPathParser.SetPropertyValue(rootObj, currentSubPath, assetAssignment.Source); // assetAssignment.Source is always going to be a string, so no need to worry about rootObj being an array and calling this.SetSubObject
                        currentObj = assetAssignment.Source;
                        assignedPaths.Add(assetAssignment);
                    }
                    skipObjectMapAssignment = true;
                }
                #endregion
                #region Traverse if NPC Setter's object linkage map already contains an object at the given subpath (e.g. if this function was called by an upstream hardcoded path assignment)
                else if (npcSetterHasObject) // if the current subpath has already been added to the given NPC record, 
                {
                    currentObj = npcObjectMap[group.Key];
                }
                #endregion
                #region Traverse if NPC Setter record already has object at the current subpath but it has not yet been added to NPC object linkage map
                else if (_recordPathParser.GetObjectAtPath(rootNPC, rootNPC, group.Key, npcObjectMap, _environmentProvider.LinkCache, true, _logger.GetNPCLogNameString(npcInfo.NPC) + " (Generated Override)", out currentObj, out currentObjInfo) && !currentObjInfo.IsNullFormLink) // if the current object is a sub-object of a template-derived record, it will not yet have been added to npcObjectMap in a previous iteration (note that it is added during this GetObjectAtPath() call so no need to add it again)
                {
                    npcSetterHasObject = true;
                    if (currentObjInfo.HasFormKey) // else does not need handling - if the NPC setter already has a given non-record object along the path, no further action is needed at this path segment.
                    {
                        if (currentObjInfo.RecordFormKey.ModKey.Equals(_environmentProvider.OutputMod.ModKey) && !IsImportedForSkyPatcher(currentObj)) // This is a subrecord of a template-derived deep copied record. Now that the path signature of the given template-derived subrecord is known, cache it
                        {
                            var generatedSubRecord = templateSubRecords.FirstOrDefault(x => x.SubRecord == currentObj);
                            if (generatedSubRecord != null)
                            {
                                AddGeneratedObjectToDictionary(pathSignature, group.Key, generatedSubRecord.TemplateSignature, currentObj, currentObjInfo.IndexInParentArray);
                                templateSubRecords.Remove(generatedSubRecord);
                                LogRecordAlongPaths(group, currentObj);
                            }
                        }
                        else if (!TraverseRecordFromNpc(currentObj, currentObjInfo, pathSignature, group, rootObj, currentSubPath, npcInfo, nonHardcodedPaths, generatedHeadParts, replacedRecords, out currentObj))
                        {
                            continue;
                        }
                    }
                }
                #endregion
                #region Get object and traverse if the corresponding NPC Getter has an object at the curent subpath
                else if (_recordPathParser.GetObjectAtPath(npcInfo.NPC, npcInfo.NPC, group.Key, objectCaches[npcInfo.NPC.FormKey], _environmentProvider.LinkCache, true, _logger.GetNPCLogNameString(npcInfo.NPC), out currentObj, out currentObjInfo) && !currentObjInfo.IsNullFormLink)
                {
                    if (currentObjInfo.HasFormKey)  // if the current object is a record, resolve it
                    {
                        if (!TraverseRecordFromNpc(currentObj, currentObjInfo, pathSignature, group, rootObj, currentSubPath, npcInfo, nonHardcodedPaths, generatedHeadParts, replacedRecords, out currentObj))
                        {
                            continue;
                        }
                    }
                    else if (!currentObjInfo.IsNullFormLink) // if the current object is not a record, copy it directly
                    {
                        currentObj = CopyGenericObject(currentObj); // Make a copy to avoid inadvertently editing other NPCs that share the given object
                        SetSubObject(rootObj, currentSubPath, currentObj, rootNPC, _environmentProvider.LinkCache);
                    }
                }
                #endregion
                #region Get object at current subpath from template NPC
                else if (canAssignFromTemplate)
                {
                    var templateSignature = group.Select(x => x.TemplateNPC).Where(x => x is not null).ToHashSet();
                    if (_patcherState.TexMeshSettings.bCacheRecords && TryGetCachedObject(pathSignature, group.Key, templateSignature, out currentObj, out int? indexIfInArray))
                    {
                        if (RecordPathParser.ObjectHasFormKey(currentObj, out FormKey? _))
                        {
                            SetViaFormKeyReplacement(currentObj, rootObj, currentSubPath, rootNPC);
                            LogRecordAlongPaths(group, currentObj);
                            recordsFromTemplates.Add(currentObj);
                        }
                        else
                        {
                            SetSubObject(rootObj, currentSubPath, currentObj, rootNPC, recordTemplateLinkCache);
                        }

                        assignedPaths.AddRange(group);
                        RemovePathsFromList(nonHardcodedPaths, group); // remove because everything downstream has already been assigned
                    }
                    else if (GetObjectFromAvailableTemplates(group.Key, group.ToArray(), objectCaches, recordTemplateLinkCache, suppressMissingPathErrors, out currentObj, out currentObjInfo))
                    {
                        if (currentObjInfo.HasFormKey)
                        {
                            if (!TraverseRecordFromTemplate(rootObj, currentSubPath, currentObj, currentObjInfo, recordTemplateLinkCache, nonHardcodedPaths, group, templateSignature, templateSubRecords, generatedHeadParts, rootNPC, npcInfo, out currentObj))
                            {
                                continue;
                            }
                            else
                            {
                                recordsFromTemplates.Add(currentObj);
                            }
                        }
                        else if (!currentObjInfo.IsNullFormLink)
                        {
                            currentObj = CopyGenericObject(currentObj); // Make a copy to avoid inadvertently editing other NPCs that share the given object
                            SetSubObject(rootObj, currentSubPath, currentObj, rootNPC, recordTemplateLinkCache);
                        }

                        AddGeneratedObjectToDictionary(pathSignature, group.Key, templateSignature, currentObj, currentObjInfo.IndexInParentArray);
                    }
                }
                #endregion
                else
                {
                    _logger.LogError("Error: neither NPC " + npcInfo.LogIDstring + " nor the record templates " + GetTemplateName(group) + " contained a record at " + group.Key + ". Cannot assign this record.");
                    RemovePathsFromList(nonHardcodedPaths, group);
                }

                if (!skipObjectMapAssignment)
                {
                    switch (npcSetterHasObject)
                    {
                        case false: npcObjectMap.Add(group.Key, currentObj); break;
                        case true: npcObjectMap[group.Key] = currentObj; break;
                    }
                }
            }
        }
    }

    /// <summary>
    /// Resolves a record encountered along the NPC's own data into an editable copy on the output mod (reusing a prior
    /// copy for the same path signature when available), assigns it via FormKey replacement on the parent (or routes head
    /// parts to the head-part selector), records the dedup mapping, and outputs the copied record.
    /// </summary>
    /// <returns>False (and removes the path group) if the record could not be typed or copied; true on success.</returns>
    private bool TraverseRecordFromNpc(dynamic currentObj, ObjectInfo currentObjInfo, HashSet<string> pathSignature, IGrouping<string, FilePathReplacementParsed> group, dynamic rootObj, string currentSubPath, NPCInfo npcInfo, List<FilePathReplacementParsed> allPaths, Dictionary<HeadPart.TypeEnum, FormKey> generatedHeadParts, Dictionary<FormKey, FormKey> replacedRecords, out dynamic outputObj)
    {
        outputObj = currentObj;
        IMajorRecord copiedRecord = null;
        if (!TryGetModifiedRecord(pathSignature, currentObjInfo.RecordFormKey, out copiedRecord) && !currentObjInfo.RecordFormKey.IsNull)
        {
            if (currentObjInfo.LoquiRegistration == null)
            {
                _logger.LogError("Could not determine record type for object of type " + currentObj.GetType().Name + ": " + _logger.GetNPCLogNameString(npcInfo.NPC) + " at path: " + group.Key + ". This subrecord will not be assigned.");
                RemovePathsFromList(allPaths, group);
                return false;
            }

            dynamic recordGroup = GetPatchRecordGroup(currentObjInfo.RecordType, _environmentProvider.OutputMod);
            copiedRecord = (IMajorRecord)IGroupMixIns.DuplicateInAsNewRecord(recordGroup, (IMajorRecordGetter)currentObj);
            AssignEditorID(copiedRecord, currentObjInfo.RecordFormKey.ToString(), false);
            if (!replacedRecords.ContainsKey(currentObjInfo.RecordFormKey))
            {
                replacedRecords.Add(currentObjInfo.RecordFormKey, copiedRecord.FormKey);
            }
        }
        if (copiedRecord == null)
        {
            _logger.LogError("Could not deep copy a record for NPC " + _logger.GetNPCLogNameString(npcInfo.NPC) + " at path: " + group.Key + ". This subrecord will not be assigned.");
            RemovePathsFromList(allPaths, group);
            return false;
        }

        var trialHeadPart = copiedRecord as HeadPart;

        if (trialHeadPart is not null) // special handling for head parts
        {
            if (!trialHeadPart.Flags.HasFlag(HeadPart.Flag.IsExtraPart))
            {
                _headPartSelector.SetGeneratedHeadPart(trialHeadPart, generatedHeadParts, npcInfo);
            }
        }
        else
        {
            SetViaFormKeyReplacement(copiedRecord, rootObj, currentSubPath, npcInfo.NPC);
        }

        AddModifiedRecordToDictionary(pathSignature, currentObjInfo.RecordFormKey, copiedRecord);
        outputObj = copiedRecord;
        LogRecordAlongPaths(group, copiedRecord);
        return true;
    }

    /// <summary>
    /// Deep-copies a record (and its subrecords) from a record template into the output mod, registers the subrecords
    /// under the template signature for later caching, increments their EditorIDs, then assigns the new record via FormKey
    /// replacement (or routes head parts to the head-part selector).
    /// </summary>
    /// <returns>False (and removes the path group) if no template subrecord could be obtained; true on success.</returns>
    private bool TraverseRecordFromTemplate(dynamic rootObj, string currentSubPath, dynamic recordToCopy, ObjectInfo recordObjectInfo, ILinkCache<ISkyrimMod, ISkyrimModGetter> recordTemplateLinkCache, List<FilePathReplacementParsed> allPaths, IGrouping<string, FilePathReplacementParsed> group, HashSet<INpcGetter> templateSignature, HashSet<TemplateSignatureRecordPair> templateDerivedRecords, Dictionary<HeadPart.TypeEnum, FormKey> generatedHeadParts, IMajorRecordGetter rootRecord, NPCInfo npcInfo, out dynamic currentObj)
    {
        IMajorRecord newRecord = null;
        HashSet<IMajorRecord> copiedRecords = new HashSet<IMajorRecord>(); // includes current record and its subrecords

        newRecord = DeepCopyRecordToPatch((IMajorRecordGetter)recordToCopy, recordObjectInfo.RecordFormKey.ModKey, recordTemplateLinkCache, _environmentProvider.OutputMod, copiedRecords);

        if (newRecord == null)
        {
            _logger.LogError("Record template error: Could not obtain a subrecord from any template NPCs " + string.Join(", ", group.Select(x => x.TemplateNPC).Select(x => EditorIDHandler.GetEditorIDSafely(x))) + " at path: " + group.Key + ". This subrecord will not be assigned.");
            RemovePathsFromList(allPaths, group);
            currentObj = recordToCopy;
            return false;
        }

        foreach (var record in copiedRecords.Where(x => x != newRecord))
        {
            templateDerivedRecords.Add(new TemplateSignatureRecordPair() { SubRecord = record, TemplateSignature = templateSignature });
        }

        IncrementEditorID(copiedRecords);

        var trialHeadPart = newRecord as HeadPart; 

        if (trialHeadPart is not null) // special handling for head parts
        {
            if (!trialHeadPart.Flags.HasFlag(HeadPart.Flag.IsExtraPart))
            {
                _headPartSelector.SetGeneratedHeadPart(trialHeadPart, generatedHeadParts, npcInfo);
            }
        }
        else
        {
            SetViaFormKeyReplacement(newRecord, rootObj, currentSubPath, rootRecord);
        }

        currentObj = newRecord;
        LogRecordAlongPaths(group, newRecord);
        return true;
    }

    /// <summary>Returns the first object found at <paramref name="currentSubPath"/> across the candidate template NPCs (caching per-template lookups), or false if none have it.</summary>
    public dynamic GetObjectFromAvailableTemplates(string currentSubPath, FilePathReplacementParsed[] allPaths, Dictionary<FormKey, Dictionary<string, dynamic>> objectCaches, ILinkCache<ISkyrimMod, ISkyrimModGetter> recordTemplateLinkCache, bool suppressMissingPathErrors, out dynamic outputObj, out ObjectInfo outputObjInfo)
    {
        foreach (var templateNPC in allPaths.Select(x => x.TemplateNPC).Where(x => x is not null).ToHashSet())
        {
            if (!objectCaches.ContainsKey(templateNPC.FormKey))
            {
                objectCaches.Add(templateNPC.FormKey, new Dictionary<string, dynamic>(StringComparer.OrdinalIgnoreCase));
            }

            if (_recordPathParser.GetObjectAtPath(templateNPC, templateNPC, currentSubPath, objectCaches[templateNPC.FormKey], recordTemplateLinkCache, suppressMissingPathErrors, _logger.GetNPCLogNameString(templateNPC), out outputObj, out outputObjInfo))
            {
                return true;
            }
        }

        outputObj = null;
        outputObjInfo = null;
        return false;
    }

    /// <summary>Deep-copies a non-record object (via expression-tree cloning) so edits do not leak to other NPCs sharing it.</summary>
    public static dynamic CopyGenericObject(dynamic input) // expand later to make more performant
    {
        var copy = DeepCopyByExpressionTrees.DeepCopyByExpressionTree(input);
        return copy;
    }
    /// <summary>Removes every path in the given group from the working path list (used to drop paths that can no longer be assigned).</summary>
    public static void RemovePathsFromList(List<FilePathReplacementParsed> allPaths, IGrouping<string, FilePathReplacementParsed> toRemove)
    {
        foreach (var path in toRemove)
        {
            allPaths.Remove(path);
        }
    }

    /// <summary>Sets a (non-record) value on the parent object at the given subpath, handling both array-index and named-property destinations.</summary>
    public void SetSubObject(dynamic rootObj, string currentSubPath, dynamic value, IMajorRecordGetter rootRecord, ILinkCache linkCache)
    {
        if (RecordPathParser.PathIsArray(currentSubPath))
        {
            if (_recordPathParser.GetObjectAtPath(rootObj, rootRecord, currentSubPath, new Dictionary<string, dynamic>(), linkCache, true, "", out dynamic _, out ObjectInfo arrayObjInfo))
            {
                SetObjectInArray(rootObj, arrayObjInfo.IndexInParentArray.Value, value);
            }
        }
        else
        {
            _recordPathParser.SetPropertyValue(rootObj, currentSubPath, value);
        }
    }

    /// <summary>Assigns <paramref name="value"/> into <paramref name="root"/> at the given array index.</summary>
    public void SetObjectInArray(dynamic root, int index, dynamic value)
    {
        root[index] = value;
    }

    /// <summary>Points the parent's FormLink at the given record's FormKey, handling array destinations (set existing index or append) and single FormLink properties.</summary>
    public void SetViaFormKeyReplacement(IMajorRecord record, dynamic rootObj, string currentSubPath, IMajorRecordGetter rootRecord)
    {
        if (RecordPathParser.PathIsArray(currentSubPath))
        {
            if (_recordPathParser.GetObjectAtPath(rootObj, rootRecord, currentSubPath, new Dictionary<string, dynamic>(), _environmentProvider.LinkCache, true, "", out dynamic _, out ObjectInfo arrayObjInfo))
            {
                SetRecordInArray(rootObj, arrayObjInfo.IndexInParentArray.Value, record);
            }
            else
            {
                AddToFormLinkList(rootObj, record);
            }
        }
        else if (RecordPathParser.GetSubObject(rootObj, currentSubPath, out dynamic formLinkToSet))
        {
            formLinkToSet.SetTo(record.FormKey);
        }
    }

    /// <summary>Sets the FormLink at the given array index to point at <paramref name="value"/>'s FormKey.</summary>
    public static void SetRecordInArray(dynamic root, int index, IMajorRecord value)
    {
        root[index].SetTo(value.FormKey);
    }

    /// <summary>Appends a FormLink to <paramref name="record"/> onto a FormLink list.</summary>
    public static void AddToFormLinkList<TMajor>(IList<IFormLinkGetter<TMajor>> list, IMajorRecord record)
        where TMajor : class, IMajorRecordGetter
    {
        list.Add(record.ToLink<TMajor>());
    }

    /// <summary>Reassembles a split record path back into its dotted string form, omitting the dot before array-index segments.</summary>
    public static string BuildPath(List<string> splitPath)
    {
        string output = "";
        for (int i = 0; i < splitPath.Count; i++)
        {
            if (i > 0 && !RecordPathParser.PathIsArray(splitPath[i]))
            {
                output += ".";
            }
            output += splitPath[i];
        }
        return output;
    }

    /// <summary>
    /// Duplicates a record into the destination mod and recursively deep-copies every FormLink that points within the
    /// source mod, remapping the new record's links to the copies. Collects all copied records (root + subrecords) into
    /// <paramref name="copiedSubRecords"/>.
    /// </summary>
    public static IMajorRecord DeepCopyRecordToPatch(dynamic sourceRecordObj, ModKey sourceModKey, ILinkCache<ISkyrimMod, ISkyrimModGetter> sourceLinkCache, ISkyrimMod destinationMod, HashSet<IMajorRecord> copiedSubRecords)
    {
        dynamic group = GetPatchRecordGroup(sourceRecordObj, destinationMod);
        IMajorRecord copiedRecord = (IMajorRecord)IGroupMixIns.DuplicateInAsNewRecord(group, sourceRecordObj);
        copiedSubRecords.Add(copiedRecord);

        Dictionary<FormKey, FormKey> mapping = new Dictionary<FormKey, FormKey>();
        foreach (var fl in copiedRecord.EnumerateFormLinks())
        {
            if (fl.FormKey.ModKey == sourceModKey && !fl.FormKey.IsNull && sourceLinkCache.TryResolve(fl.FormKey, fl.Type, out var subRecord))
            {
                var copiedSubRecord = DeepCopyRecordToPatch(subRecord, sourceModKey, sourceLinkCache, destinationMod, copiedSubRecords);
                mapping.Add(fl.FormKey, copiedSubRecord.FormKey);
            }
        }
        if (mapping.Any())
        {
            copiedRecord.RemapLinks(mapping);
        }

        return copiedRecord;
    }

    /// <summary>Gets or adds the given record as an override in the correct top-level group of the output mod.</summary>
    public static dynamic GetOrAddGenericRecordAsOverride(IMajorRecordGetter recordGetter, ISkyrimMod outputMod)
    {
        dynamic group = GetPatchRecordGroup(recordGetter, outputMod);
        return OverrideMixIns.GetOrAddAsOverride(group, recordGetter);
    }

    /// <summary>Returns the output mod's top-level group matching the record getter's registered getter type.</summary>
    public static IGroup GetPatchRecordGroup(IMajorRecordGetter recordGetter, ISkyrimMod outputMod)
    {
        var getterType = LoquiRegistration.GetRegister(recordGetter.GetType()).GetterType;
        return outputMod.GetTopLevelGroup(getterType);
    }

    public static dynamic GetPatchRecordGroup(Type loquiType, ISkyrimMod outputMod) // must return dynamic so that the type IGroup<T> is determined at runtime. Returning IGroup causes IGroupMixIns.DuplicateInAsNewRecord() to complain.
    {
        return outputMod.GetTopLevelGroup(loquiType);
    }

    /// <summary>Stores a resolved object in the per-NPC object cache under the given path (creating the NPC's cache entry if needed); no-op if already cached.</summary>
    public static void CacheResolvedObject(string path, dynamic toCache, Dictionary<FormKey, Dictionary<string, dynamic>> objectCaches, INpcGetter npcGetter)
    {
        if (!objectCaches.ContainsKey(npcGetter.FormKey))
        {
            objectCaches.Add(npcGetter.FormKey, new Dictionary<string, dynamic>(StringComparer.OrdinalIgnoreCase));
            objectCaches[npcGetter.FormKey].Add("", npcGetter);
        }

        if (!objectCaches[npcGetter.FormKey].ContainsKey(path))
        {
            objectCaches[npcGetter.FormKey].Add(path, toCache);
        }
    }

    /// <summary>
    /// Sets a unique EditorID on a generated record. Template-derived records get a global counter suffix; records copied
    /// from an existing NPC record get a "_Patched" suffix plus a per-original-FormKey counter. Mutates static counters.
    /// </summary>
    public static void AssignEditorID(IMajorRecord record, string templateFKstr, bool copiedFromTemplate)
    {
        if (copiedFromTemplate)
        {
            IncrementEditorID(new HashSet<IMajorRecord>() { record });
        }
        else
        {
            record.EditorID = EditorIDHandler.GetEditorIDSafely(record) + "_Patched";

            if (ModifiedRecordCounts.ContainsKey(templateFKstr))
            {
                ModifiedRecordCounts[templateFKstr]++;
            }
            else
            {
                ModifiedRecordCounts.Add(templateFKstr, 1);
            }

            record.EditorID += ModifiedRecordCounts[templateFKstr].ToString("D4");
        }
    }

    /// <summary>Appends a 4-digit occurrence counter to each record's EditorID to keep generated EditorIDs unique. Mutates the static <see cref="EdidCounts"/> map.</summary>
    public static void IncrementEditorID(HashSet<IMajorRecord> records)
    {
        foreach (var newRecord in records)
        {
            if (EdidCounts.ContainsKey(newRecord.EditorID ?? "NoEditorID"))
            {
                EdidCounts[newRecord.EditorID ?? "NoEditorID"]++;
                newRecord.EditorID += EdidCounts[newRecord.EditorID ?? "NoEditorID"].ToString("D4"); // pad with leading zeroes https://stackoverflow.com/questions/4325267/c-sharp-convert-int-to-string-with-padding-zeros
            }
            else
            {
                EdidCounts.Add(newRecord.EditorID ?? "NoEditorID", 1);
                newRecord.EditorID += 1.ToString("D4");
            }
        }
    }

    /// <summary>Returns a comma-joined list of the template NPC EditorIDs referenced by the path group (for error messages).</summary>
    public static string GetTemplateName(IGrouping<string, FilePathReplacementParsed> group)
    {
        List<string> templateNames = new List<string>();
        foreach (var item in group)
        {
            if (item.TemplateNPC != null)
            {
                templateNames.Add(item.TemplateNPC.EditorID ?? "NoEditorID");
            }
        }
        return string.Join(", ", templateNames);
    }
    
    /// <summary>Returns true if the object is a record whose EditorID carries the surrogate-NPC suffix (i.e. it was merged in by the SkyPatcher surrogate provider).</summary>
    private bool IsImportedForSkyPatcher(dynamic currentObj) // determines if the given formkey is from a record merged-in from NpcProvider
    {
        var record = currentObj as IMajorRecord;
        if (record != null && record.EditorID != null && record.EditorID.EndsWith(SurrogateNPCProvider.SurrogateSuffix))
        {
            return true;
        }
        return false;
    }

    /// <summary>Per-EditorID occurrence counter used by <see cref="IncrementEditorID"/> to suffix duplicate EditorIDs uniquely.</summary>
    public static Dictionary<string, int> EdidCounts = new Dictionary<string, int>(); // tracks the number of times a given record template was assigned so that a newly copied record can have its editor ID incremented

    /// <summary>Per-original-FormKey counter used by <see cref="AssignEditorID"/> to suffix "_Patched" EditorIDs uniquely.</summary>
    private static Dictionary<string, int> ModifiedRecordCounts = new Dictionary<string, int>(); // for modified Editor IDs only

    //Dictionary[SourcePaths.ToHashSet()][OriginalRecordGetter.FormKey.ToString()] = IMajorRecord Generated
    /// <summary>Cache of overrides generated from existing records, keyed by source-path signature then original FormKey string. Enables reuse across NPCs sharing the same assets.</summary>
    private static Dictionary<HashSet<string>, Dictionary<string, IMajorRecord>> ModifiedRecords = new Dictionary<HashSet<string>, Dictionary<string, IMajorRecord>>(HashSet<string>.CreateSetComparer()); // https://stackoverflow.com/questions/5910137/how-do-i-use-hashsett-as-a-dictionary-key

    /// <summary>Looks up a previously generated override for the given original FormKey under the given path signature in the static <see cref="ModifiedRecords"/> cache.</summary>
    public static bool TryGetModifiedRecord<T>(HashSet<string> pathSignature, FormKey originalFormKey, out T record) where T : class
    {
        string fkStr = originalFormKey.ToString();

        if (ModifiedRecords.ContainsKey(pathSignature) && ModifiedRecords[pathSignature].ContainsKey(fkStr))
        {
            record = ModifiedRecords[pathSignature][fkStr] as T;
            return record != null;
        }
        else
        {
            record = null;
            return false;
        }
    }

    /// <summary>Records a generated override keyed by path signature and original FormKey in the static <see cref="ModifiedRecords"/> cache (for cross-NPC reuse and dedup).</summary>
    public static void AddModifiedRecordToDictionary(HashSet<string> pathSignature, FormKey originalFormKey, IMajorRecord record)
    {
        string fkStr = originalFormKey.ToString();

        if (!ModifiedRecords.ContainsKey(pathSignature))
        {
            ModifiedRecords.Add(pathSignature, new Dictionary<string, IMajorRecord>());
        }

        if (!ModifiedRecords[pathSignature].ContainsKey(fkStr))
        {
            ModifiedRecords[pathSignature].Add(fkStr, null);
        }

        ModifiedRecords[pathSignature][fkStr] = record;
    }

    //Dictionary[SourcePaths.ToHashSet()][RecordTemplate.FormKey.ToString()] = IMajorRecord Generated
    /// <summary>Cache of records generated from record templates, keyed by source-path signature then template NPC FormKey string.</summary>
    public static Dictionary<HashSet<string>, Dictionary<string, IMajorRecord>> GeneratedRecordsByTempateNPC = new Dictionary<HashSet<string>, Dictionary<string, IMajorRecord>>(HashSet<string>.CreateSetComparer()); // https://stackoverflow.com/questions/5910137/how-do-i-use-hashsett-as-a-dictionary-key

    /// <summary>Records a template-derived generated record keyed by path signature and template NPC FormKey in <see cref="GeneratedRecordsByTempateNPC"/> (no-op if already present).</summary>
    public static void AddGeneratedRecordToDictionary(HashSet<string> pathSignature, INpcGetter template, IMajorRecord record)
    {
        var templateFKstring = template.FormKey.ToString();
        if (!GeneratedRecordsByTempateNPC.ContainsKey(pathSignature))
        {
            GeneratedRecordsByTempateNPC.Add(pathSignature, new Dictionary<string, IMajorRecord>());
        }

        if (!GeneratedRecordsByTempateNPC[pathSignature].ContainsKey(templateFKstring))
        {
            GeneratedRecordsByTempateNPC[pathSignature].Add(templateFKstring, record);
        }
    }

    //Dictionary[SourcePaths.ToHashSet()][SubPathStr][RecordTemplate.FormKey.ToString()] = Object Generated

    /// <summary>Cache of generated sub-objects keyed by source-path signature, then path-relative-to-NPC, then template signature; lets deep traversals skip re-copying identical template-derived objects.</summary>
    private static Dictionary<HashSet<string>, Dictionary<string, Dictionary<HashSet<string>, ObjectAtIndex>>> CachedObjectsByPathAndTemplate = new Dictionary<HashSet<string>, Dictionary<string, Dictionary<HashSet<string>, ObjectAtIndex>>>(HashSet<string>.CreateSetComparer());

    /// <summary>Cached generated object plus its index within the template's parent array (if it came from an array element).</summary>
    private class ObjectAtIndex
    {
        public dynamic generatedObj { get; set; } = null;
        public int? indexInTemplate { get; set; } = null;
    }

    /// <summary>Retrieves a cached generated object for the (path signature, subpath, template signature) triple from <see cref="CachedObjectsByPathAndTemplate"/>, if present and non-null.</summary>
    private static bool TryGetCachedObject(HashSet<string> pathSignature, string pathRelativeToNPC, HashSet<INpcGetter> templateSignature, out dynamic storedObj, out int? indexIfInArray)
    {
        var templateSignatureStr = templateSignature.Select(x => x.FormKey.ToString()).ToHashSet();
        if (CachedObjectsByPathAndTemplate.ContainsKey(pathSignature) && CachedObjectsByPathAndTemplate[pathSignature].ContainsKey(pathRelativeToNPC) && CachedObjectsByPathAndTemplate[pathSignature][pathRelativeToNPC].ContainsKey(templateSignatureStr))
        {
            storedObj = CachedObjectsByPathAndTemplate[pathSignature][pathRelativeToNPC][templateSignatureStr].generatedObj;
            indexIfInArray = CachedObjectsByPathAndTemplate[pathSignature][pathRelativeToNPC][templateSignatureStr].indexInTemplate;
            return storedObj != null;
        }
        storedObj = null;
        indexIfInArray = null;
        return false;
    }

    /// <summary>Caches a generated object (and its array index) under the (path signature, subpath, template signature) triple in <see cref="CachedObjectsByPathAndTemplate"/>.</summary>
    private static void AddGeneratedObjectToDictionary(HashSet<string> pathSignature, string pathRelativeToNPC, HashSet<INpcGetter> templateSignature, dynamic storedObj, int? storedIndex)
    {
        var storedObjectAndIndex = new ObjectAtIndex() { generatedObj = storedObj, indexInTemplate = storedIndex };

        var templateSignatureStr = templateSignature.Select(x => x.FormKey.ToString()).ToHashSet();
        if (!CachedObjectsByPathAndTemplate.ContainsKey(pathSignature))
        {
            CachedObjectsByPathAndTemplate.Add(pathSignature, new Dictionary<string, Dictionary<HashSet<string>, ObjectAtIndex>>());
        }

        if (!CachedObjectsByPathAndTemplate[pathSignature].ContainsKey(pathRelativeToNPC))
        {
            CachedObjectsByPathAndTemplate[pathSignature].Add(pathRelativeToNPC, new Dictionary<HashSet<string>, ObjectAtIndex>(HashSet<string>.CreateSetComparer()));
        }

        if (!CachedObjectsByPathAndTemplate[pathSignature][pathRelativeToNPC].ContainsKey(templateSignatureStr))
        {
            CachedObjectsByPathAndTemplate[pathSignature][pathRelativeToNPC].Add(templateSignatureStr, storedObjectAndIndex);
        }
    }

    /// <summary>Records a <see cref="GeneratedRecordInfo"/> for the record (and its same-mod subrecords) onto every path in the group, for the assignment report.</summary>
    public static void LogRecordAlongPaths(IGrouping<string, FilePathReplacementParsed> group, IMajorRecord record)
    {
        var recordEntry = new GeneratedRecordInfo() { FormKey = record.FormKey.ToString(), EditorID = record.EditorID ?? "NoEditorID", SubRecords = record.EnumerateFormLinks().Where(x => x.FormKey.ModKey == record.FormKey.ModKey).ToHashSet() };

        foreach (var entry in group)
        {
            entry.TraversedRecords.Add(recordEntry);
        }
    }

    /// <summary>Records a <see cref="GeneratedRecordInfo"/> for the record (and its same-mod subrecords) onto every path in the sequence, for the assignment report.</summary>
    public static void LogRecordAlongPaths(IEnumerable<FilePathReplacementParsed> paths, IMajorRecord record)
    {
        var recordEntry = new GeneratedRecordInfo() { FormKey = record.FormKey.ToString(), EditorID = record.EditorID ?? "NoEditorID", SubRecords = record.EnumerateFormLinks().Where(x => x.FormKey.ModKey == record.FormKey.ModKey).ToHashSet() };

        foreach (var entry in paths)
        {
            entry.TraversedRecords.Add(recordEntry);
        }
    }

    /// <summary>Cache of keyword records created for custom keyword strings, so the same keyword is reused rather than
    /// re-created. Reset per run in <see cref="Reinitialize"/> (the keyword records live in the per-run output mod).</summary>
    // THREADING (future parallel selection): this and the other RecordGenerator static caches are on the GENERATION/write
    // phase (ApplySelectedAssets mutates the output mod), which must stay single-threaded -- so they intentionally need no
    // synchronization as long as record generation runs serially after the parallel selection phase.
    private static Dictionary<string, Keyword> GeneratedKeywords = new Dictionary<string, Keyword>();

    /// <summary>Adds every non-blank custom keyword declared by the assigned asset entries to the NPC (creating keyword records as needed).</summary>
    public static void AddCustomKeywordsToNPC(List<Patcher.SelectedAssetContainer> assignedAssetEntries, Npc npc, ISkyrimMod outputMod)
    {
        foreach (var entry in assignedAssetEntries)
        {
            foreach (var keyword in entry.KeywordsToApply)
            {
                if (!string.IsNullOrWhiteSpace(keyword))
                {
                    AddKeywordToNPC(npc, keyword, outputMod);
                }
            }
        }
    }
    /// <summary>Adds the named keyword to the NPC, reusing a previously generated keyword record or creating a new one in the output mod and caching it in <see cref="GeneratedKeywords"/>.</summary>
    private static void AddKeywordToNPC(Npc npc, string keyword, ISkyrimMod outputMod)
    {
        if (GeneratedKeywords.ContainsKey(keyword))
        {
            npc.Keywords.Add(GeneratedKeywords[keyword]);
        }
        else
        {
            var kw = outputMod.Keywords.AddNew();
            kw.EditorID = keyword;
            npc.Keywords.Add(kw);
            GeneratedKeywords.Add(keyword, kw);
        }
    }

    /// <summary>If the NPC's worn armor is in the configured strip set, returns an override with WornArmor cleared; otherwise returns the input unchanged.</summary>
    public INpcGetter StripSpecifiedSkinArmor(INpcGetter npcGetter, ILinkCache linkCache, ISkyrimMod outputMod)
    {
        if (npcGetter.WornArmor != null && skinWNAMsToStrip.Contains(npcGetter.WornArmor.FormKey))
        {
            var npc = outputMod.Npcs.GetOrAddAsOverride(npcGetter);
            npc.WornArmor.Clear();
            return npc;
        }
        return npcGetter;
    }
}