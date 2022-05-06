using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;
using Mutagen.Bethesda.Plugins.Records;
using Loqui;
using Mutagen.Bethesda.Plugins.Cache;

namespace SynthEBD;

public class RecordGenerator
{
    public static void CombinationToRecords(List<SubgroupCombination> combinations, NPCInfo npcInfo, ILinkCache<ISkyrimMod, ISkyrimModGetter> recordTemplateLinkCache, Dictionary<string, dynamic> npcObjectMap, Dictionary<FormKey, Dictionary<string, dynamic>> objectCaches, SkyrimMod outputMod, List<FilePathReplacementParsed> assignedPaths)
    {
        HashSet<FilePathReplacementParsed> wnamPaths = new HashSet<FilePathReplacementParsed>();
        HashSet<FilePathReplacementParsed> headtexPaths = new HashSet<FilePathReplacementParsed>();
        List<FilePathReplacementParsed> nonHardcodedPaths = new List<FilePathReplacementParsed>();

        HardcodedRecordGenerator.CategorizePaths(combinations, npcInfo, recordTemplateLinkCache, wnamPaths, headtexPaths, nonHardcodedPaths, out int longestPath, true); // categorize everything as generic for now.

        if (!nonHardcodedPaths.Any() && !wnamPaths.Any() && !headtexPaths.Any()) { return; } // avoid making ITM if user blocks all assets of the type assigned (see AssetSelector.BlockAssetDistributionByExistingAssets())

        var currentNPC = outputMod.Npcs.GetOrAddAsOverride(npcInfo.NPC);
        objectCaches.Add(npcInfo.NPC.FormKey, new Dictionary<string, dynamic>(StringComparer.OrdinalIgnoreCase) { { "", currentNPC } });

        HardcodedRecordGenerator.AssignHardcodedRecords(wnamPaths, headtexPaths, npcInfo, recordTemplateLinkCache, npcObjectMap, objectCaches, outputMod);

        if (nonHardcodedPaths.Any())
        {
            AssignGenericAssetPaths(npcInfo, nonHardcodedPaths, currentNPC, recordTemplateLinkCache, outputMod, longestPath, true, false, npcObjectMap, objectCaches, assignedPaths);
        }
    }

    private class TemplateSignatureRecordPair
    {
        public HashSet<INpcGetter> TemplateSignature { get; set; }
        public IMajorRecord SubRecord { get; set; }
    }

    // assignedPaths is for logging purposes only
    public static void AssignGenericAssetPaths(NPCInfo npcInfo, List<FilePathReplacementParsed> nonHardcodedPaths, Npc rootNPC, ILinkCache<ISkyrimMod, ISkyrimModGetter> recordTemplateLinkCache, SkyrimMod outputMod, int longestPath, bool assignFromTemplate, bool suppressMissingPathErrors, Dictionary<string, dynamic> npcObjectMap, Dictionary<FormKey, Dictionary<string, dynamic>> objectCaches, List<FilePathReplacementParsed> assignedPaths)
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
                    Logger.LogError(Logger.GetNPCLogNameString(npcInfo.NPC) + ": Expected and failed to find an object at path: " + parentPath + ". Subrecords will not be assigned. Please report this error.");
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
                        RecordPathParser.SetSubObject(rootObj, currentSubPath, assetAssignment.Source);
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
                else if (RecordPathParser.GetObjectAtPath(rootNPC, group.Key, npcObjectMap, Patcher.MainLinkCache, true, Logger.GetNPCLogNameString(npcInfo.NPC) + " (Generated Override)", out currentObj, out currentObjInfo) && !currentObjInfo.IsNullFormLink) // if the current object is a sub-object of a template-derived record, it will not yet have been added to npcObjectMap in a previous iteration (note that it is added during this GetObjectAtPath() call so no need to add it again)
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
                        else if (!TraverseRecordFromNpc(currentObj, currentObjInfo, pathSignature, group, rootObj, currentSubPath, npcInfo, nonHardcodedPaths, outputMod, out currentObj))
                        {
                            continue;
                        }
                    }
                }
                #endregion
                #region Get object and traverse if the corresponding NPC Getter has an object at the curent subpath
                else if (RecordPathParser.GetObjectAtPath(npcInfo.NPC, group.Key, objectCaches[npcInfo.NPC.FormKey], Patcher.MainLinkCache, true, Logger.GetNPCLogNameString(npcInfo.NPC), out currentObj, out currentObjInfo) && !currentObjInfo.IsNullFormLink)
                {
                    if (currentObjInfo.HasFormKey)  // if the current object is a record, resolve it
                    {
                        if (!TraverseRecordFromNpc(currentObj, currentObjInfo, pathSignature, group, rootObj, currentSubPath, npcInfo, nonHardcodedPaths, outputMod, out currentObj))
                        {
                            continue;
                        }
                    }
                    else if (!currentObjInfo.IsNullFormLink) // if the current object is not a record, copy it directly
                    {
                        currentObj = CopyGenericObject(currentObj); // Make a copy to avoid inadvertently editing other NPCs that share the given object
                        RecordPathParser.SetSubObject(rootObj, currentSubPath, currentObj);
                    }
                }
                #endregion
                #region Get object at current subpath from template NPC
                else if (assignFromTemplate)
                {
                    var templateSignature = group.Select(x => x.TemplateNPC).Where(x => x is not null).ToHashSet();
                    if (TryGetGeneratedObject(pathSignature, group.Key, templateSignature, out currentObj, out int? indexIfInArray))
                    {
                        if (RecordPathParser.ObjectHasFormKey(currentObj, out FormKey? _))
                        {
                            SetViaFormKeyReplacement(currentObj, rootObj, currentSubPath);
                            LogRecordAlongPaths(group, currentObj);
                        }
                        else
                        {
                            RecordPathParser.SetSubObject(rootObj, currentSubPath, currentObj);
                        }

                        assignedPaths.AddRange(group);
                        RemovePathsFromList(nonHardcodedPaths, group); // remove because everything downstream has already been assigned
                    }
                    else if (GetObjectFromAvailableTemplates(group.Key, group.ToArray(), objectCaches, recordTemplateLinkCache, suppressMissingPathErrors, out currentObj, out currentObjInfo))
                    {
                        if (currentObjInfo.HasFormKey)
                        {
                            if (!TraverseRecordFromTemplate(rootObj, currentSubPath, currentObj, currentObjInfo, recordTemplateLinkCache, nonHardcodedPaths, group, templateSignature, templateSubRecords, outputMod, out currentObj))
                            {
                                continue;
                            }
                        }
                        else if (!currentObjInfo.IsNullFormLink)
                        {
                            currentObj = CopyGenericObject(currentObj); // Make a copy to avoid inadvertently editing other NPCs that share the given object
                            RecordPathParser.SetSubObject(rootObj, currentSubPath, currentObj);
                        }

                        AddGeneratedObjectToDictionary(pathSignature, group.Key, templateSignature, currentObj, currentObjInfo.IndexInParentArray);
                    }
                }
                #endregion
                else
                {
                    Logger.LogError("Error: neither NPC " + npcInfo.LogIDstring + " nor the record templates " + GetTemplateName(group) + " contained a record at " + group.Key + ". Cannot assign this record.");
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

    private static bool TraverseRecordFromNpc(dynamic currentObj, ObjectInfo currentObjInfo, HashSet<string> pathSignature, IGrouping<string, FilePathReplacementParsed> group, dynamic rootObj, string currentSubPath, NPCInfo npcInfo, List<FilePathReplacementParsed> allPaths, SkyrimMod outputMod, out dynamic outputObj)
    {
        outputObj = currentObj;
        IMajorRecord copiedRecord = null;
        if (!TryGetModifiedRecord(pathSignature, currentObjInfo.RecordFormKey, out copiedRecord) && !currentObjInfo.RecordFormKey.IsNull)
        {
            if (currentObjInfo.LoquiRegistration == null)
            {
                Logger.LogError("Could not determine record type for object of type " + currentObj.GetType().Name + ": " + Logger.GetNPCLogNameString(npcInfo.NPC) + " at path: " + group.Key + ". This subrecord will not be assigned.");
                RemovePathsFromList(allPaths, group);
                return false;
            }

            dynamic recordGroup = GetPatchRecordGroup(currentObjInfo.RecordType, outputMod);
            copiedRecord = (IMajorRecord)IGroupMixIns.DuplicateInAsNewRecord(recordGroup, (IMajorRecordGetter)currentObj);
            AssignEditorID(copiedRecord, currentObjInfo.RecordFormKey.ToString(), false);
        }
        if (copiedRecord == null)
        {
            Logger.LogError("Could not deep copy a record for NPC " + Logger.GetNPCLogNameString(npcInfo.NPC) + " at path: " + group.Key + ". This subrecord will not be assigned.");
            RemovePathsFromList(allPaths, group);
            return false;
        }

        SetViaFormKeyReplacement(copiedRecord, rootObj, currentSubPath);
        AddModifiedRecordToDictionary(pathSignature, currentObjInfo.RecordFormKey, copiedRecord);
        outputObj = copiedRecord;
        LogRecordAlongPaths(group, copiedRecord);
        return true;
    }

    private static bool TraverseRecordFromTemplate(dynamic rootObj, string currentSubPath, dynamic recordToCopy, ObjectInfo recordObjectInfo, ILinkCache<ISkyrimMod, ISkyrimModGetter> recordTemplateLinkCache, List<FilePathReplacementParsed> allPaths, IGrouping<string, FilePathReplacementParsed> group, HashSet<INpcGetter> templateSignature, HashSet<TemplateSignatureRecordPair> templateDerivedRecords, SkyrimMod outputMod, out dynamic currentObj)
    {
        IMajorRecord newRecord = null;
        HashSet<IMajorRecord> copiedRecords = new HashSet<IMajorRecord>(); // includes current record and its subrecords

        newRecord = DeepCopyRecordToPatch((IMajorRecordGetter)recordToCopy, recordObjectInfo.RecordFormKey.ModKey, recordTemplateLinkCache, outputMod, copiedRecords);

        if (newRecord == null)
        {
            Logger.LogError("Record template error: Could not obtain a subrecord from any template NPCs " + string.Join(", ", group.Select(x => x.TemplateNPC).Select(x => x.EditorID)) + " at path: " + group.Key + ". This subrecord will not be assigned.");
            RemovePathsFromList(allPaths, group);
            currentObj = recordToCopy;
            return false;
        }

        foreach (var record in copiedRecords.Where(x => x != newRecord))
        {
            templateDerivedRecords.Add(new TemplateSignatureRecordPair() { SubRecord = record, TemplateSignature = templateSignature });
        }

        IncrementEditorID(copiedRecords);

        SetViaFormKeyReplacement(newRecord, rootObj, currentSubPath);

        currentObj = newRecord;
        LogRecordAlongPaths(group, newRecord);
        return true;
    }

    public static dynamic GetObjectFromAvailableTemplates(string currentSubPath, FilePathReplacementParsed[] allPaths, Dictionary<FormKey, Dictionary<string, dynamic>> objectCaches, ILinkCache<ISkyrimMod, ISkyrimModGetter> recordTemplateLinkCache, bool suppressMissingPathErrors, out dynamic outputObj, out ObjectInfo outputObjInfo)
    {
        foreach (var templateNPC in allPaths.Select(x => x.TemplateNPC).Where(x => x is not null).ToHashSet())
        {
            if (!objectCaches.ContainsKey(templateNPC.FormKey))
            {
                objectCaches.Add(templateNPC.FormKey, new Dictionary<string, dynamic>(StringComparer.OrdinalIgnoreCase));
            }

            if (RecordPathParser.GetObjectAtPath(templateNPC, currentSubPath, objectCaches[templateNPC.FormKey], recordTemplateLinkCache, suppressMissingPathErrors, Logger.GetNPCLogNameString(templateNPC), out outputObj, out outputObjInfo))
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

    public static void SetViaFormKeyReplacement(IMajorRecord record, dynamic root, string currentSubPath)
    {
        if (RecordPathParser.PathIsArray(currentSubPath))
        {
            if (RecordPathParser.GetObjectAtPath(root, currentSubPath, new Dictionary<string, dynamic>(), Patcher.MainLinkCache, true, "", out dynamic _, out ObjectInfo arrayObjInfo))
            {
                SetRecordInArray(root, arrayObjInfo.IndexInParentArray.Value, record);
            }
            else
            {
                AddToFormLinkList(root, record);
            }
        }
        else if (RecordPathParser.GetSubObject(root, currentSubPath, out dynamic formLinkToSet))
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

    public static IMajorRecord DeepCopyRecordToPatch(dynamic sourceRecordObj, ModKey sourceModKey, ILinkCache<ISkyrimMod, ISkyrimModGetter> sourceLinkCache, SkyrimMod destinationMod, HashSet<IMajorRecord> copiedSubRecords)
    {
        dynamic group = GetPatchRecordGroup(sourceRecordObj, destinationMod);
        IMajorRecord copiedRecord = (IMajorRecord)IGroupMixIns.DuplicateInAsNewRecord(group, sourceRecordObj);
        copiedSubRecords.Add(copiedRecord);

        Dictionary<FormKey, FormKey> mapping = new Dictionary<FormKey, FormKey>();
        foreach (var fl in copiedRecord.ContainedFormLinks)
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

    public static dynamic GetOrAddGenericRecordAsOverride(IMajorRecordGetter recordGetter, SkyrimMod outputMod)
    {
        dynamic group = GetPatchRecordGroup(recordGetter, outputMod);
        return OverrideMixIns.GetOrAddAsOverride(group, recordGetter);
    }

    public static IGroup GetPatchRecordGroup(IMajorRecordGetter recordGetter, SkyrimMod outputMod)
    {
        var getterType = LoquiRegistration.GetRegister(recordGetter.GetType()).GetterType;
        return outputMod.GetTopLevelGroup(getterType);
    }

    public static dynamic GetPatchRecordGroup(Type loquiType, SkyrimMod outputMod) // must return dynamic so that the type IGroup<T> is determined at runtime. Returning IGroup causes IGroupMixIns.DuplicateInAsNewRecord() to complain.
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
            record.EditorID += "_Patched";
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
            if (EdidCounts.ContainsKey(newRecord.EditorID))
            {
                EdidCounts[newRecord.EditorID]++;
                newRecord.EditorID += EdidCounts[newRecord.EditorID].ToString("D4"); // pad with leading zeroes https://stackoverflow.com/questions/4325267/c-sharp-convert-int-to-string-with-padding-zeros
            }
            else
            {
                EdidCounts.Add(newRecord.EditorID, 1);
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
                templateNames.Add(item.TemplateNPC.EditorID);
            }
        }
        return string.Join(", ", templateNames);
    }

    public static void Reinitialize()
    {
        ModifiedRecordCounts = new Dictionary<string, int>();
        ModifiedRecords = new Dictionary<HashSet<string>, Dictionary<string, IMajorRecord>>(HashSet<string>.CreateSetComparer());
        GeneratedObjectsByPathAndTemplate = GeneratedObjectsByPathAndTemplate = new Dictionary<HashSet<string>, Dictionary<string, Dictionary<HashSet<string>, ObjectAtIndex>>>(HashSet<string>.CreateSetComparer());
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

    private static Dictionary<HashSet<string>, Dictionary<string, Dictionary<HashSet<string>, ObjectAtIndex>>> GeneratedObjectsByPathAndTemplate = new Dictionary<HashSet<string>, Dictionary<string, Dictionary<HashSet<string>, ObjectAtIndex>>>(HashSet<string>.CreateSetComparer());

    private class ObjectAtIndex
    {
        public dynamic generatedObj { get; set; } = null;
        public int? indexInTemplate { get; set; } = null;
    }

    private static bool TryGetGeneratedObject(HashSet<string> pathSignature, string pathRelativeToNPC, HashSet<INpcGetter> templateSignature, out dynamic storedObj, out int? indexIfInArray)
    {
        var templateSignatureStr = templateSignature.Select(x => x.FormKey.ToString()).ToHashSet();
        if (GeneratedObjectsByPathAndTemplate.ContainsKey(pathSignature) && GeneratedObjectsByPathAndTemplate[pathSignature].ContainsKey(pathRelativeToNPC) && GeneratedObjectsByPathAndTemplate[pathSignature][pathRelativeToNPC].ContainsKey(templateSignatureStr))
        {
            storedObj = GeneratedObjectsByPathAndTemplate[pathSignature][pathRelativeToNPC][templateSignatureStr].generatedObj;
            indexIfInArray = GeneratedObjectsByPathAndTemplate[pathSignature][pathRelativeToNPC][templateSignatureStr].indexInTemplate;
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
        if (!GeneratedObjectsByPathAndTemplate.ContainsKey(pathSignature))
        {
            GeneratedObjectsByPathAndTemplate.Add(pathSignature, new Dictionary<string, Dictionary<HashSet<string>, ObjectAtIndex>>());
        }

        if (!GeneratedObjectsByPathAndTemplate[pathSignature].ContainsKey(pathRelativeToNPC))
        {
            GeneratedObjectsByPathAndTemplate[pathSignature].Add(pathRelativeToNPC, new Dictionary<HashSet<string>, ObjectAtIndex>(HashSet<string>.CreateSetComparer()));
        }

        if (!GeneratedObjectsByPathAndTemplate[pathSignature][pathRelativeToNPC].ContainsKey(templateSignatureStr))
        {
            GeneratedObjectsByPathAndTemplate[pathSignature][pathRelativeToNPC].Add(templateSignatureStr, storedObjectAndIndex);
        }
    }

    public static void LogRecordAlongPaths(IGrouping<string, FilePathReplacementParsed> group, IMajorRecord record)
    {
        HashSet<GeneratedRecordInfo> assignedRecords = new HashSet<GeneratedRecordInfo>();
        var recordEntry = new GeneratedRecordInfo() { FormKey = record.FormKey.ToString(), EditorID = record.EditorID, SubRecords = record.ContainedFormLinks.Where(x => x.FormKey.ModKey == record.FormKey.ModKey).ToHashSet() };

        foreach (var entry in group)
        {
            entry.TraversedRecords.Add(recordEntry);
        }
    }

    private static Dictionary<string, Keyword> GeneratedKeywords = new Dictionary<string, Keyword>();

    public static void AddKeywordsToNPC(List<SubgroupCombination> assignedCombinations, Npc npc, SkyrimMod outputMod)
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
    private static void AddKeywordToNPC(Npc npc, string keyword, SkyrimMod outputMod)
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
}