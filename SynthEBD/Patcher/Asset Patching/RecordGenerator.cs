using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;
using Mutagen.Bethesda.Plugins.Records;
using Loqui;
using Mutagen.Bethesda.Plugins.Cache;
using Noggog;

namespace SynthEBD;

public class RecordGenerator
{
    private readonly IEnvironmentStateProvider _environmentProvider;
    private readonly PatcherState _patcherState;
    private readonly Logger _logger;
    private readonly SynthEBDPaths _paths;
    private readonly HardcodedRecordGenerator _hardcodedRecordGenerator;
    private readonly HeadPartSelector _headPartSelector;
    private readonly RecordPathParser _recordPathParser;
    public RecordGenerator(IEnvironmentStateProvider environmentProvider, PatcherState patcherState, Logger logger, SynthEBDPaths paths, HardcodedRecordGenerator hardcodedRecordGenerator, HeadPartSelector headPartSelector, RecordPathParser recordPathParser)
    {
        _environmentProvider = environmentProvider;
        _patcherState = patcherState;
        _logger = logger;
        _paths = paths;
        _hardcodedRecordGenerator = hardcodedRecordGenerator;
        _headPartSelector = headPartSelector;
        _recordPathParser = recordPathParser;
    }
    public void CombinationToRecords(List<SubgroupCombination> combinations, NPCInfo npcInfo, ILinkCache<ISkyrimMod, ISkyrimModGetter> recordTemplateLinkCache, Dictionary<string, dynamic> npcObjectMap, Dictionary<FormKey, Dictionary<string, dynamic>> objectCaches, ISkyrimMod outputMod, List<FilePathReplacementParsed> assignedPaths, Dictionary<HeadPart.TypeEnum, HeadPart> generatedHeadParts)
    {
        HashSet<FilePathReplacementParsed> wnamPaths = new HashSet<FilePathReplacementParsed>();
        HashSet<FilePathReplacementParsed> headtexPaths = new HashSet<FilePathReplacementParsed>();
        List<FilePathReplacementParsed> nonHardcodedPaths = new List<FilePathReplacementParsed>();

        _hardcodedRecordGenerator.CategorizePaths(combinations, npcInfo, recordTemplateLinkCache, wnamPaths, headtexPaths, nonHardcodedPaths, out int longestPath, true); // categorize everything as generic for now.

        if (!nonHardcodedPaths.Any() && !wnamPaths.Any() && !headtexPaths.Any()) { return; } // avoid making ITM if user blocks all assets of the type assigned (see AssetSelector.BlockAssetDistributionByExistingAssets())

        var currentNPC = outputMod.Npcs.GetOrAddAsOverride(npcInfo.NPC);
        objectCaches.Add(npcInfo.NPC.FormKey, new Dictionary<string, dynamic>(StringComparer.OrdinalIgnoreCase) { { "", currentNPC } });

        _hardcodedRecordGenerator.AssignHardcodedRecords(wnamPaths, headtexPaths, npcInfo, recordTemplateLinkCache, npcObjectMap, objectCaches, outputMod, this);

        if (nonHardcodedPaths.Any())
        {
            AssignGenericAssetPaths(npcInfo, nonHardcodedPaths, currentNPC, recordTemplateLinkCache, outputMod, longestPath, true, false, npcObjectMap, objectCaches, assignedPaths, generatedHeadParts);
        }
    }

    private class TemplateSignatureRecordPair
    {
        public HashSet<INpcGetter> TemplateSignature { get; set; }
        public IMajorRecord SubRecord { get; set; }
    }

    // assignedPaths is for logging purposes only
    public void AssignGenericAssetPaths(NPCInfo npcInfo, List<FilePathReplacementParsed> nonHardcodedPaths, Npc rootNPC, ILinkCache<ISkyrimMod, ISkyrimModGetter> recordTemplateLinkCache, ISkyrimMod outputMod, int longestPath, bool canAssignFromTemplate, bool suppressMissingPathErrors, Dictionary<string, dynamic> npcObjectMap, Dictionary<FormKey, Dictionary<string, dynamic>> objectCaches, List<FilePathReplacementParsed> assignedPaths, Dictionary<HeadPart.TypeEnum, HeadPart> generatedHeadParts)
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

            var groupedPathsAtI = nonHardcodedPaths.GroupBy(x => BuildPath(x.Destination.ToList().GetRange(0, i + 1))); // group paths by the current path segment

            foreach (var group in groupedPathsAtI)
            {
                #region Get root object at current path segment
                string parentPath = BuildPath(group.First().Destination.ToList().GetRange(0, i));
                string currentSubPath = group.First().Destination[i];
                var rootObj = npcObjectMap[parentPath];
                if (rootObj == null)
                {
                    _logger.LogError(Logger.GetNPCLogNameString(npcInfo.NPC) + ": Expected and failed to find an object at path: " + parentPath + ". Subrecords will not be assigned. Please report this error.");
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
                        _recordPathParser.SetPropertyValue(rootObj, currentSubPath, assetAssignment.Source);
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
                else if (EasyNPCHandler.Permits(npcInfo.NPC, currentSubPath, _patcherState.TexMeshSettings.bEasyNPCCompatibilityMode) && _recordPathParser.GetObjectAtPath(rootNPC, rootNPC, group.Key, npcObjectMap, _environmentProvider.LinkCache, true, Logger.GetNPCLogNameString(npcInfo.NPC) + " (Generated Override)", out currentObj, out currentObjInfo) && !currentObjInfo.IsNullFormLink) // if the current object is a sub-object of a template-derived record, it will not yet have been added to npcObjectMap in a previous iteration (note that it is added during this GetObjectAtPath() call so no need to add it again)
                {
                    npcSetterHasObject = true;
                    if (currentObjInfo.HasFormKey) // else does not need handling - if the NPC setter already has a given non-record object along the path, no further action is needed at this path segment.
                    {
                        if (currentObjInfo.RecordFormKey.ModKey.Equals(outputMod.ModKey)) // This is a subrecord of a template-derived deep copied record. Now that the path signature of the given template-derived subrecord is known, cache it
                        {
                            var generatedSubRecord = templateSubRecords.Where(x => x.SubRecord == currentObj).FirstOrDefault();
                            if (generatedSubRecord != null)
                            {
                                AddGeneratedObjectToDictionary(pathSignature, group.Key, generatedSubRecord.TemplateSignature, currentObj, currentObjInfo.IndexInParentArray);
                                templateSubRecords.Remove(generatedSubRecord);
                                LogRecordAlongPaths(group, currentObj);
                            }
                        }
                        else if (!TraverseRecordFromNpc(currentObj, currentObjInfo, pathSignature, group, rootObj, currentSubPath, npcInfo, nonHardcodedPaths, generatedHeadParts, outputMod, out currentObj))
                        {
                            continue;
                        }
                    }
                }
                #endregion
                #region Get object and traverse if the corresponding NPC Getter has an object at the curent subpath
                else if (EasyNPCHandler.Permits(npcInfo.NPC, currentSubPath, _patcherState.TexMeshSettings.bEasyNPCCompatibilityMode) && _recordPathParser.GetObjectAtPath(npcInfo.NPC, npcInfo.NPC, group.Key, objectCaches[npcInfo.NPC.FormKey], _environmentProvider.LinkCache, true, Logger.GetNPCLogNameString(npcInfo.NPC), out currentObj, out currentObjInfo) && !currentObjInfo.IsNullFormLink)
                {
                    if (currentObjInfo.HasFormKey)  // if the current object is a record, resolve it
                    {
                        if (!TraverseRecordFromNpc(currentObj, currentObjInfo, pathSignature, group, rootObj, currentSubPath, npcInfo, nonHardcodedPaths, generatedHeadParts, outputMod, out currentObj))
                        {
                            continue;
                        }
                    }
                    else if (!currentObjInfo.IsNullFormLink) // if the current object is not a record, copy it directly
                    {
                        currentObj = CopyGenericObject(currentObj); // Make a copy to avoid inadvertently editing other NPCs that share the given object
                        _recordPathParser.SetPropertyValue(rootObj, currentSubPath, currentObj);
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
                        }
                        else
                        {
                            _recordPathParser.SetPropertyValue(rootObj, currentSubPath, currentObj);
                        }

                        assignedPaths.AddRange(group);
                        RemovePathsFromList(nonHardcodedPaths, group); // remove because everything downstream has already been assigned
                    }
                    else if (GetObjectFromAvailableTemplates(group.Key, group.ToArray(), objectCaches, recordTemplateLinkCache, suppressMissingPathErrors, out currentObj, out currentObjInfo))
                    {
                        if (currentObjInfo.HasFormKey)
                        {
                            if (!TraverseRecordFromTemplate(rootObj, currentSubPath, currentObj, currentObjInfo, recordTemplateLinkCache, nonHardcodedPaths, group, templateSignature, templateSubRecords, generatedHeadParts, outputMod, rootNPC, npcInfo, out currentObj))
                            {
                                continue;
                            }
                        }
                        else if (!currentObjInfo.IsNullFormLink)
                        {
                            currentObj = CopyGenericObject(currentObj); // Make a copy to avoid inadvertently editing other NPCs that share the given object
                            _recordPathParser.SetPropertyValue(rootObj, currentSubPath, currentObj);
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

    private bool TraverseRecordFromNpc(dynamic currentObj, ObjectInfo currentObjInfo, HashSet<string> pathSignature, IGrouping<string, FilePathReplacementParsed> group, dynamic rootObj, string currentSubPath, NPCInfo npcInfo, List<FilePathReplacementParsed> allPaths, Dictionary<HeadPart.TypeEnum, HeadPart> generatedHeadParts, ISkyrimMod outputMod, out dynamic outputObj)
    {
        outputObj = currentObj;
        IMajorRecord copiedRecord = null;
        bool isHeadPart = false;
        if (!TryGetModifiedRecord(pathSignature, currentObjInfo.RecordFormKey, out copiedRecord) && !currentObjInfo.RecordFormKey.IsNull)
        {
            if (currentObjInfo.LoquiRegistration == null)
            {
                _logger.LogError("Could not determine record type for object of type " + currentObj.GetType().Name + ": " + Logger.GetNPCLogNameString(npcInfo.NPC) + " at path: " + group.Key + ". This subrecord will not be assigned.");
                RemovePathsFromList(allPaths, group);
                return false;
            }

            dynamic recordGroup = GetPatchRecordGroup(currentObjInfo.RecordType, outputMod);
            copiedRecord = (IMajorRecord)IGroupMixIns.DuplicateInAsNewRecord(recordGroup, (IMajorRecordGetter)currentObj);
            AssignEditorID(copiedRecord, currentObjInfo.RecordFormKey.ToString(), false);
        }
        if (copiedRecord == null)
        {
            _logger.LogError("Could not deep copy a record for NPC " + Logger.GetNPCLogNameString(npcInfo.NPC) + " at path: " + group.Key + ". This subrecord will not be assigned.");
            RemovePathsFromList(allPaths, group);
            return false;
        }

        var trialHeadPart = copiedRecord as HeadPart;

        if (trialHeadPart is not null) // special handling for head parts
        {
            var headPart = copiedRecord as HeadPart;
            _headPartSelector.SetGeneratedHeadPart(trialHeadPart, generatedHeadParts, npcInfo);
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

    private bool TraverseRecordFromTemplate(dynamic rootObj, string currentSubPath, dynamic recordToCopy, ObjectInfo recordObjectInfo, ILinkCache<ISkyrimMod, ISkyrimModGetter> recordTemplateLinkCache, List<FilePathReplacementParsed> allPaths, IGrouping<string, FilePathReplacementParsed> group, HashSet<INpcGetter> templateSignature, HashSet<TemplateSignatureRecordPair> templateDerivedRecords, Dictionary<HeadPart.TypeEnum, HeadPart> generatedHeadParts, ISkyrimMod outputMod, IMajorRecordGetter rootRecord, NPCInfo npcInfo, out dynamic currentObj)
    {
        IMajorRecord newRecord = null;
        HashSet<IMajorRecord> copiedRecords = new HashSet<IMajorRecord>(); // includes current record and its subrecords

        newRecord = DeepCopyRecordToPatch((IMajorRecordGetter)recordToCopy, recordObjectInfo.RecordFormKey.ModKey, recordTemplateLinkCache, outputMod, copiedRecords);

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
            _headPartSelector.SetGeneratedHeadPart(trialHeadPart, generatedHeadParts, npcInfo);
        }
        else
        {
            SetViaFormKeyReplacement(newRecord, rootObj, currentSubPath, rootRecord);
        }

        currentObj = newRecord;
        LogRecordAlongPaths(group, newRecord);
        return true;
    }

    public dynamic GetObjectFromAvailableTemplates(string currentSubPath, FilePathReplacementParsed[] allPaths, Dictionary<FormKey, Dictionary<string, dynamic>> objectCaches, ILinkCache<ISkyrimMod, ISkyrimModGetter> recordTemplateLinkCache, bool suppressMissingPathErrors, out dynamic outputObj, out ObjectInfo outputObjInfo)
    {
        foreach (var templateNPC in allPaths.Select(x => x.TemplateNPC).Where(x => x is not null).ToHashSet())
        {
            if (!objectCaches.ContainsKey(templateNPC.FormKey))
            {
                objectCaches.Add(templateNPC.FormKey, new Dictionary<string, dynamic>(StringComparer.OrdinalIgnoreCase));
            }

            if (_recordPathParser.GetObjectAtPath(templateNPC, templateNPC, currentSubPath, objectCaches[templateNPC.FormKey], recordTemplateLinkCache, suppressMissingPathErrors, Logger.GetNPCLogNameString(templateNPC), out outputObj, out outputObjInfo))
            {
                return true;
            }
        }

        outputObj = null;
        outputObjInfo = null;
        return false;
    }

    public static dynamic CopyGenericObject(dynamic input) // expand later to make more performant
    {
        var copy = DeepCopyByExpressionTrees.DeepCopyByExpressionTree(input);
        return copy;
    }
    public static void RemovePathsFromList(List<FilePathReplacementParsed> allPaths, IGrouping<string, FilePathReplacementParsed> toRemove)
    {
        foreach (var path in toRemove)
        {
            allPaths.Remove(path);
        }
    }

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

    public static void AddToFormLinkList<TMajor>(IList<IFormLinkGetter<TMajor>> list, IMajorRecord record)
        where TMajor : class, IMajorRecordGetter
    {
        list.Add(record.AsLink<TMajor>());
    }

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

    public static dynamic GetOrAddGenericRecordAsOverride(IMajorRecordGetter recordGetter, ISkyrimMod outputMod)
    {
        dynamic group = GetPatchRecordGroup(recordGetter, outputMod);
        return OverrideMixIns.GetOrAddAsOverride(group, recordGetter);
    }

    public static IGroup GetPatchRecordGroup(IMajorRecordGetter recordGetter, ISkyrimMod outputMod)
    {
        var getterType = LoquiRegistration.GetRegister(recordGetter.GetType()).GetterType;
        return outputMod.GetTopLevelGroup(getterType);
    }

    public static dynamic GetPatchRecordGroup(Type loquiType, ISkyrimMod outputMod) // must return dynamic so that the type IGroup<T> is determined at runtime. Returning IGroup causes IGroupMixIns.DuplicateInAsNewRecord() to complain.
    {
        return outputMod.GetTopLevelGroup(loquiType);
    }

    public static void SetRecordInArray(dynamic root, int index, IMajorRecord value)
    {
        root[index].SetTo(value.FormKey);
    }

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

    public static void Reinitialize()
    {
        ModifiedRecordCounts = new Dictionary<string, int>();
        ModifiedRecords = new Dictionary<HashSet<string>, Dictionary<string, IMajorRecord>>(HashSet<string>.CreateSetComparer());
        CachedObjectsByPathAndTemplate = CachedObjectsByPathAndTemplate = new Dictionary<HashSet<string>, Dictionary<string, Dictionary<HashSet<string>, ObjectAtIndex>>>(HashSet<string>.CreateSetComparer());
        GeneratedRecordsByTempateNPC = GeneratedRecordsByTempateNPC = new Dictionary<HashSet<string>, Dictionary<string, IMajorRecord>>(HashSet<string>.CreateSetComparer());
        EdidCounts = new Dictionary<string, int>();
    }

    public static Dictionary<string, int> EdidCounts = new Dictionary<string, int>(); // tracks the number of times a given record template was assigned so that a newly copied record can have its editor ID incremented

    private static Dictionary<string, int> ModifiedRecordCounts = new Dictionary<string, int>(); // for modified Editor IDs only

    //Dictionary[SourcePaths.ToHashSet()][OriginalRecordGetter.FormKey.ToString()] = IMajorRecord Generated
    private static Dictionary<HashSet<string>, Dictionary<string, IMajorRecord>> ModifiedRecords = new Dictionary<HashSet<string>, Dictionary<string, IMajorRecord>>(HashSet<string>.CreateSetComparer()); // https://stackoverflow.com/questions/5910137/how-do-i-use-hashsett-as-a-dictionary-key

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
    public static Dictionary<HashSet<string>, Dictionary<string, IMajorRecord>> GeneratedRecordsByTempateNPC = new Dictionary<HashSet<string>, Dictionary<string, IMajorRecord>>(HashSet<string>.CreateSetComparer()); // https://stackoverflow.com/questions/5910137/how-do-i-use-hashsett-as-a-dictionary-key

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

    private static Dictionary<HashSet<string>, Dictionary<string, Dictionary<HashSet<string>, ObjectAtIndex>>> CachedObjectsByPathAndTemplate = new Dictionary<HashSet<string>, Dictionary<string, Dictionary<HashSet<string>, ObjectAtIndex>>>(HashSet<string>.CreateSetComparer());

    private class ObjectAtIndex
    {
        public dynamic generatedObj { get; set; } = null;
        public int? indexInTemplate { get; set; } = null;
    }

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

    public static void LogRecordAlongPaths(IGrouping<string, FilePathReplacementParsed> group, IMajorRecord record)
    {
        HashSet<GeneratedRecordInfo> assignedRecords = new HashSet<GeneratedRecordInfo>();
        var recordEntry = new GeneratedRecordInfo() { FormKey = record.FormKey.ToString(), EditorID = record.EditorID ?? "NoEditorID", SubRecords = record.EnumerateFormLinks().Where(x => x.FormKey.ModKey == record.FormKey.ModKey).ToHashSet() };

        foreach (var entry in group)
        {
            entry.TraversedRecords.Add(recordEntry);
        }
    }

    private static Dictionary<string, Keyword> GeneratedKeywords = new Dictionary<string, Keyword>();

    public static void AddKeywordsToNPC(List<SubgroupCombination> assignedCombinations, Npc npc, ISkyrimMod outputMod)
    {
        foreach (var combination in assignedCombinations)
        {
            foreach (var subgroup in combination.ContainedSubgroups)
            {
                foreach (var keyword in subgroup.AddKeywords)
                {
                    if (!string.IsNullOrWhiteSpace(keyword))
                    {
                        AddKeywordToNPC(npc, keyword, outputMod);
                    }
                }
            }
        }
    }
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

    private class EasyNPCHandler
    {
        public static bool Permits(INpcGetter npcGetter, string currentSubPath, bool bEasyNPCCompatibilityMode)
        {
            if (currentSubPath == "WornArmor" && bEasyNPCCompatibilityMode && npcGetter.WornArmor != null && npcGetter.WornArmor.Equals(Mutagen.Bethesda.FormKeys.SkyrimSE.Skyrim.Armor.SkinNaked))
            {
                return false;
            }
            else
            {
                return true;
            }
        }
    }
}