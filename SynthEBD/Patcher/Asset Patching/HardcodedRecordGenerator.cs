using Loqui;
using Mutagen.Bethesda;
using Mutagen.Bethesda.FormKeys.SkyrimSE;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Skyrim;

namespace SynthEBD;

/// <summary>
/// Currently deprecated.
/// Originally created to accelerate the creation of records with "main" types (e.g. head, body textures)
/// However, the I had trouble getting the hardcoded record generation to mesh with any non-standard destination paths, and record generation generically (via reflection) works well enough, so I never fixed this class
/// </summary>
public class HardcodedRecordGenerator
{
    private readonly IEnvironmentStateProvider _environmentProvider;
    private readonly PatcherState _patcherState;
    private readonly Logger _logger;
    private readonly HeadPartSelector _headPartSelector;
    public HardcodedRecordGenerator(IEnvironmentStateProvider environmentProvider, PatcherState patcherState, Logger logger, HeadPartSelector headPartSelector)
    {
        _environmentProvider = environmentProvider;
        _patcherState = patcherState;
        _logger = logger;
        _headPartSelector = headPartSelector;
    }

    public void CategorizePaths(List<SubgroupCombination> combinations, NPCInfo npcInfo, ILinkCache<ISkyrimMod, ISkyrimModGetter> recordTemplateLinkCache, HashSet<FilePathReplacementParsed> wnamPaths, HashSet<FilePathReplacementParsed> headtexPaths, List<FilePathReplacementParsed> nonHardcodedPaths, out int longestPathLength, bool doNotHardCode)
    {
        longestPathLength = 0;
        foreach (var combination in combinations)
        {
            foreach (var subgroup in combination.ContainedSubgroups)
            {
                foreach (var path in subgroup.Paths)
                {
                    var parsed = new FilePathReplacementParsed(path, npcInfo, combination.AssetPack, recordTemplateLinkCache, combination, _logger);

                    if (!_patcherState.TexMeshSettings.bChangeNPCTextures && path.Source.EndsWith(".dds", StringComparison.OrdinalIgnoreCase)) { continue; }
                    if (!_patcherState.TexMeshSettings.bChangeNPCMeshes && path.Source.EndsWith(".nif", StringComparison.OrdinalIgnoreCase)) { continue; }

                    if (doNotHardCode)
                    {
                        nonHardcodedPaths.Add(parsed);
                        if (parsed.Destination.Length > longestPathLength)
                        {
                            longestPathLength = parsed.Destination.Length;
                        }
                    }
                    else
                    {
                        if (path.Destination.StartsWith("WornArmor")) { wnamPaths.Add(parsed); }
                        else if (path.Destination.StartsWith("HeadTexture")) { headtexPaths.Add(parsed); }
                        else
                        {
                            nonHardcodedPaths.Add(parsed);
                            if (parsed.Destination.Length > longestPathLength)
                            {
                                longestPathLength = parsed.Destination.Length;
                            }
                        }
                    }
                }
            }
        }
    }

    // All code below this point is deprecated in favor of generic record parsing. This code may be updated in the future if demand arises but it was deprecated prior to full debugging. Expect bugs if re-implementing.

    public static int GetLongestPath(IEnumerable<FilePathReplacementParsed> paths)
    {
        int longestPath = 0;
        foreach (var path in paths)
        {
            if (path.Destination.Length > longestPath)
            {
                longestPath = path.Destination.Length;
            }
        }
        return longestPath;
    }

    public void AssignHardcodedRecords(HashSet<FilePathReplacementParsed> wnamPaths, HashSet<FilePathReplacementParsed> headtexPaths, NPCInfo npcInfo, ILinkCache<ISkyrimMod, ISkyrimModGetter> recordTemplateLinkCache, Dictionary<string, dynamic> npcObjectMap, Dictionary<FormKey, Dictionary<string, dynamic>> objectCaches, ISkyrimMod outputMod, RecordGenerator recordGenerator)
    {
        if (headtexPaths.Any())
        {
            AssignHeadTexture(npcInfo, outputMod, _environmentProvider.LinkCache, recordTemplateLinkCache, headtexPaths, npcObjectMap, objectCaches, recordGenerator);
        }
        if (wnamPaths.Any())
        {
            AssignBodyTextures(npcInfo, outputMod, _environmentProvider.LinkCache, recordTemplateLinkCache, wnamPaths, npcObjectMap, objectCaches, recordGenerator);
        }
    }

    public static INpcGetter GetTemplateForHardcodedAssignments(HashSet<FilePathReplacementParsed> paths, HashSet<string> preferredPaths, string fallBackStartStr)
    {
        INpcGetter template = null;
        var preferredPath = paths.Where(x => preferredPaths.Contains(x.DestinationStr)).FirstOrDefault();
        if (preferredPath == null)
        {
            preferredPath = paths.Where(x => x.DestinationStr.StartsWith(fallBackStartStr)).FirstOrDefault();
        }

        if (preferredPath != null)
        {
            template = preferredPath.TemplateNPC;
        }

        return template;
    }
    public IMajorRecord AssignHeadTexture(NPCInfo npcInfo, ISkyrimMod outputMod, ILinkCache<ISkyrimMod, ISkyrimModGetter> mainLinkCache, ILinkCache<ISkyrimMod, ISkyrimModGetter> templateLinkCache, HashSet<FilePathReplacementParsed> paths, Dictionary<string, dynamic> npcObjectMap, Dictionary<FormKey, Dictionary<string, dynamic>> objectCaches, RecordGenerator recordGenerator)
    {
        var patchedNPC = outputMod.Npcs.GetOrAddAsOverride(npcInfo.NPC);

        TextureSet headTex = null;
        bool assignedFromDictionary = false;
        var pathSignature = paths.Select(x => x.Source).ToHashSet();

        INpcGetter templateNPC = GetTemplateForHardcodedAssignments(paths, HeadTexturePaths, "HeadTexture");

        if (npcInfo.NPC.HeadTexture != null && !npcInfo.NPC.HeadTexture.IsNull && RecordGenerator.TryGetModifiedRecord(pathSignature, npcInfo.NPC.HeadTexture.FormKey, out headTex))
        {
            assignedFromDictionary = true;
        }
        else if (npcInfo.NPC.HeadTexture != null && !npcInfo.NPC.HeadTexture.IsNull && mainLinkCache.TryResolve<ITextureSetGetter>(npcInfo.NPC.HeadTexture.FormKey, out var existingHeadTexture))
        {
            headTex = outputMod.TextureSets.AddNew();
            headTex.DeepCopyIn(existingHeadTexture);
            RecordGenerator.AssignEditorID(headTex, existingHeadTexture.FormKey.ToString(), false);
            RecordGenerator.AddModifiedRecordToDictionary(pathSignature, npcInfo.NPC.HeadTexture.FormKey, headTex);
        }
        else if (TryGetGeneratedRecord(pathSignature, templateNPC, out headTex))
        {
            assignedFromDictionary = true;
        }
        else if (templateNPC != null && !templateNPC.HeadTexture.IsNull && templateLinkCache.TryResolve<ITextureSetGetter>(templateNPC.HeadTexture.FormKey, out var templateHeadTexture))
        {
            HashSet<IMajorRecord> subRecords = new HashSet<IMajorRecord>();
            headTex = (TextureSet)RecordGenerator.DeepCopyRecordToPatch(templateHeadTexture, templateHeadTexture.FormKey.ModKey, templateLinkCache, outputMod, subRecords);
            RecordGenerator.IncrementEditorID(subRecords);
            RecordGenerator.AddGeneratedRecordToDictionary(pathSignature, templateNPC, headTex);
            RecordGenerator.CacheResolvedObject("HeadTexture", templateHeadTexture, objectCaches, templateNPC);
        }
        else
        {
            _logger.LogReport("Could not resolve a head texture from NPC " + Logger.GetNPCLogNameString(npcInfo.NPC) + " or its corresponding record template.", true, npcInfo);
            return null;
        }

        if (!assignedFromDictionary)
        {
            var additionalGenericPaths = new List<FilePathReplacementParsed>();

            foreach (var path in paths)
            {
                switch (path.DestinationStr)
                {
                    case "HeadTexture.Height": headTex.Height = path.Source; break;
                    case "HeadTexture.Diffuse": headTex.Diffuse = path.Source; break;
                    case "HeadTexture.NormalOrGloss": headTex.NormalOrGloss = path.Source; break;
                    case "HeadTexture.GlowOrDetailMap": headTex.GlowOrDetailMap = path.Source; break;
                    case "HeadTexture.BacklightMaskOrSpecular": headTex.BacklightMaskOrSpecular = path.Source; break;
                    default: additionalGenericPaths.Add(path); break;
                }
            }

            if (additionalGenericPaths.Any())
            {
                recordGenerator.AssignGenericAssetPaths(npcInfo, additionalGenericPaths, patchedNPC, templateLinkCache, outputMod, GetLongestPath(additionalGenericPaths), true, false, npcObjectMap, objectCaches, new List<FilePathReplacementParsed>(), Patcher.GetBlankHeadPartAssignment());
            }
        }

        npcObjectMap.Add("HeadTexture", headTex);

        patchedNPC.HeadTexture.SetTo(headTex);
        return headTex;
    }

    private Armor AssignBodyTextures(NPCInfo npcInfo, ISkyrimMod outputMod, ILinkCache<ISkyrimMod, ISkyrimModGetter> mainLinkCache, ILinkCache<ISkyrimMod, ISkyrimModGetter> templateLinkCache, HashSet<FilePathReplacementParsed> paths, Dictionary<string, dynamic> npcObjectMap, Dictionary<FormKey, Dictionary<string, dynamic>> objectCaches, RecordGenerator recordGenerator)
    {
        Armor newSkin = null;
        bool assignedFromTemplate = false;

        bool assignedFromDictionary = false;
        var pathSignature = paths.Select(x => x.Source).ToHashSet();

        INpcGetter templateNPC = GetTemplateForHardcodedAssignments(paths, WornArmorPaths, "WornArmor");

        if (npcInfo.NPC.WornArmor != null && !npcInfo.NPC.WornArmor.IsNull && RecordGenerator.TryGetModifiedRecord(pathSignature, npcInfo.NPC.WornArmor.FormKey, out newSkin))
        {
            assignedFromDictionary = true;
        }
        else if (!npcInfo.NPC.WornArmor.IsNull && mainLinkCache.TryResolve<IArmorGetter>(npcInfo.NPC.WornArmor.FormKey, out var existingWNAM))
        {
            newSkin = outputMod.Armors.AddNew();
            newSkin.DeepCopyIn(existingWNAM);
            RecordGenerator.AssignEditorID(newSkin, existingWNAM.FormKey.ToString(), false);
            RecordGenerator.AddModifiedRecordToDictionary(pathSignature, npcInfo.NPC.WornArmor.FormKey, newSkin);
        }
        else if (TryGetGeneratedRecord(pathSignature, templateNPC, out newSkin))
        {
            assignedFromDictionary = true;
        }
        else if (templateNPC != null && !templateNPC.WornArmor.IsNull && templateLinkCache.TryResolve<IArmorGetter>(templateNPC.WornArmor.FormKey, out var templateWNAM))
        {
            RecordGenerator.CacheResolvedObject("WornArmor", templateWNAM, objectCaches, templateNPC);
            RecordGenerator.CacheResolvedObject("WornArmor.Armature", templateWNAM.Armature, objectCaches, templateNPC);
            HashSet<IMajorRecord> subRecords = new HashSet<IMajorRecord>();
            newSkin = (Armor)RecordGenerator.DeepCopyRecordToPatch(templateWNAM, templateWNAM.FormKey.ModKey, templateLinkCache, outputMod, subRecords);
            RecordGenerator.IncrementEditorID(subRecords);
            assignedFromTemplate = true;
            RecordGenerator.AddGeneratedRecordToDictionary(pathSignature, templateNPC, newSkin);
        }
        else
        {
            _logger.LogReport("Could not resolve a body texture from NPC " + npcInfo.LogIDstring + " or its corresponding record template.", true, npcInfo);
            outputMod.Armors.Remove(newSkin);
            return null;
        }

        var patchedNPC = outputMod.Npcs.GetOrAddAsOverride(npcInfo.NPC);

        npcObjectMap.Add("WornArmor", newSkin);
        npcObjectMap.Add("WornArmor.Armature", newSkin.Armature);

        if (!assignedFromDictionary)
        {
            #region sort paths
            var hardcodedTorsoArmorAddonPaths = new HashSet<FilePathReplacementParsed>();
            var hardcodedHandsArmorAddonPaths = new HashSet<FilePathReplacementParsed>();
            var hardcodedFeetArmorAddonPaths = new HashSet<FilePathReplacementParsed>();
            var hardcodedTailArmorAddonPaths = new HashSet<FilePathReplacementParsed>();
            var genericArmorAddonPaths = new List<FilePathReplacementParsed>();
            var genericTorsoArmorAddonSubpaths = new List<FilePathReplacementParsed>();
            var genericHandsArmorAddonSubpaths = new List<FilePathReplacementParsed>();
            var genericFeetArmorAddonSubpaths = new List<FilePathReplacementParsed>();
            var genericTailArmorAddonSubpaths = new List<FilePathReplacementParsed>();

            foreach (var path in paths)
            {
                if (path.DestinationStr.StartsWith("WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Body)", StringComparison.OrdinalIgnoreCase))
                {
                    if (TorsoArmorAddonPaths.Contains(path.DestinationStr)) { hardcodedTorsoArmorAddonPaths.Add(path); }
                    else { genericTorsoArmorAddonSubpaths.Add(path); }
                }
                else if (path.DestinationStr.StartsWith("WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Hands)", StringComparison.OrdinalIgnoreCase))
                {
                    if (HandsArmorAddonPaths.Contains(path.DestinationStr)) { hardcodedHandsArmorAddonPaths.Add(path); }
                    else { genericHandsArmorAddonSubpaths.Add(path); }
                }
                else if (path.DestinationStr.StartsWith("WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Feet)", StringComparison.OrdinalIgnoreCase))
                {
                    if (FeetArmorAddonPaths.Contains(path.DestinationStr)) { hardcodedFeetArmorAddonPaths.Add(path); }
                    else { genericFeetArmorAddonSubpaths.Add(path); }
                }
                else if (path.DestinationStr.StartsWith("WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Tail)", StringComparison.OrdinalIgnoreCase))
                {
                    if (TailArmorAddonPaths.Contains(path.DestinationStr)) { hardcodedTailArmorAddonPaths.Add(path); }
                    else { genericTailArmorAddonSubpaths.Add(path); }
                }
                else
                {
                    genericArmorAddonPaths.Add(path);
                }
            }
            #endregion

            var allowedRaces = new HashSet<string>();
            allowedRaces.Add(npcInfo.NPC.Race.FormKey.ToString());
            var assetsRaceString = npcInfo.AssetsRace.ToString();
            if (!allowedRaces.Contains(assetsRaceString))
            {
                allowedRaces.Add(assetsRaceString);
            }
            allowedRaces.Add(Skyrim.Race.DefaultRace.FormKey.ToString());

            string subPath;
            if (hardcodedTorsoArmorAddonPaths.Any() || genericTorsoArmorAddonSubpaths.Any())
            {
                subPath = "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Body) && PatchableRaces.Contains(Race)]";
                var assignedTorso = AssignArmorAddon(patchedNPC, newSkin, npcInfo, outputMod, mainLinkCache, templateLinkCache, hardcodedTorsoArmorAddonPaths, genericTorsoArmorAddonSubpaths, ArmorAddonType.Torso, subPath, allowedRaces, assignedFromTemplate, npcObjectMap, objectCaches, recordGenerator);
            }
            if (hardcodedHandsArmorAddonPaths.Any() || genericHandsArmorAddonSubpaths.Any())
            {
                subPath = "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Hands) && PatchableRaces.Contains(Race)]";
                var assignedHands = AssignArmorAddon(patchedNPC, newSkin, npcInfo, outputMod, mainLinkCache, templateLinkCache, hardcodedHandsArmorAddonPaths, genericHandsArmorAddonSubpaths, ArmorAddonType.Hands, subPath, allowedRaces, assignedFromTemplate, npcObjectMap, objectCaches, recordGenerator);
            }
            if (hardcodedFeetArmorAddonPaths.Any() || genericFeetArmorAddonSubpaths.Any())
            {
                subPath = "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Feet) && PatchableRaces.Contains(Race)]";
                var assignedFeet = AssignArmorAddon(patchedNPC, newSkin, npcInfo, outputMod, mainLinkCache, templateLinkCache, hardcodedFeetArmorAddonPaths, genericFeetArmorAddonSubpaths, ArmorAddonType.Feet, subPath, allowedRaces, assignedFromTemplate, npcObjectMap, objectCaches, recordGenerator);
            }
            if (hardcodedTailArmorAddonPaths.Any() || genericTailArmorAddonSubpaths.Any())
            {
                subPath = "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Tail) && PatchableRaces.Contains(Race)]";
                var assignedTail = AssignArmorAddon(patchedNPC, newSkin, npcInfo, outputMod, mainLinkCache, templateLinkCache, hardcodedTailArmorAddonPaths, genericTailArmorAddonSubpaths, ArmorAddonType.Tail, subPath, allowedRaces, assignedFromTemplate, npcObjectMap, objectCaches, recordGenerator);
            }
            if (genericArmorAddonPaths.Any())
            {
                recordGenerator.AssignGenericAssetPaths(npcInfo, genericArmorAddonPaths, patchedNPC, templateLinkCache, outputMod, GetLongestPath(genericArmorAddonPaths), true, false, npcObjectMap, objectCaches, new List<FilePathReplacementParsed>(), Patcher.GetBlankHeadPartAssignment());
            }
        }
        else // if record is one that has previously been generated, update any SynthEBD-generated armature to ensure that the current NPC's race is present within the Additional Races collection.
        {
            foreach (var armatureLink in newSkin.Armature)
            {
                if (_environmentProvider.LinkCache.TryResolve<IArmorAddonGetter>(armatureLink.FormKey, out var armaGetter) && outputMod.ArmorAddons.ContainsKey(armatureLink.FormKey) && !armaGetter.AdditionalRaces.Select(x => x.FormKey.ToString()).Contains(npcInfo.NPC.Race.FormKey.ToString())) // 
                {
                    var armature = outputMod.ArmorAddons.GetOrAddAsOverride(armaGetter);
                    armature.AdditionalRaces.Add(npcInfo.NPC.Race);
                }
            }
        }

        patchedNPC.WornArmor.SetTo(newSkin);

        return newSkin;
    }

    private enum ArmorAddonType
    {
        Torso,
        Hands,
        Feet,
        Tail
    }

    private ArmorAddon AssignArmorAddon(Npc targetNPC, Armor parentArmorRecord, NPCInfo npcInfo, ISkyrimMod outputMod, ILinkCache<ISkyrimMod, ISkyrimModGetter> mainLinkCache, ILinkCache<ISkyrimMod, ISkyrimModGetter> templateLinkCache, HashSet<FilePathReplacementParsed> hardcodedPaths, List<FilePathReplacementParsed> additionalGenericPaths, ArmorAddonType type, string subPath, HashSet<string> currentRaceIDstrs, bool parentAssignedFromTemplate, Dictionary<string, dynamic> npcObjectMap, Dictionary<FormKey, Dictionary<string, dynamic>> objectCaches, RecordGenerator recordGenerator)
    {
        ArmorAddon newArmorAddon = null;
        IArmorAddonGetter sourceArmorAddon;
        HashSet<IArmorAddonGetter> candidateAAs = new HashSet<IArmorAddonGetter>();
        bool replaceExistingArmature = false;

        var pathSignature = hardcodedPaths.Select(x => x.Source).ToHashSet();

        INpcGetter templateNPC = GetTemplateForHardcodedAssignments(hardcodedPaths.Union(additionalGenericPaths).ToHashSet(), WornArmorPaths, subPath);

        // try to get the needed armor addon template record from the existing parent armor record
        candidateAAs = GetAvailableArmature(parentArmorRecord, mainLinkCache, templateLinkCache, !parentAssignedFromTemplate, parentAssignedFromTemplate);

        sourceArmorAddon = ChooseArmature(candidateAAs, type, currentRaceIDstrs);
        replaceExistingArmature = sourceArmorAddon is not null;

        if (sourceArmorAddon != null && !RecordGenerator.TryGetModifiedRecord(pathSignature, sourceArmorAddon.FormKey, out newArmorAddon))
        {
            newArmorAddon = outputMod.ArmorAddons.AddNew();
            newArmorAddon.DeepCopyIn(sourceArmorAddon);
            var assignedSkinTexture = AssignSkinTexture(newArmorAddon, parentAssignedFromTemplate, npcInfo, outputMod, mainLinkCache, templateLinkCache, hardcodedPaths, subPath, npcObjectMap, objectCaches);
            replaceExistingArmature = true;

            RecordGenerator.AssignEditorID(newArmorAddon, sourceArmorAddon.FormKey.ToString(), parentAssignedFromTemplate);
            RecordGenerator.AddModifiedRecordToDictionary(pathSignature, sourceArmorAddon.FormKey, newArmorAddon);
        }

        // try to get the needed armor record from the corresponding record template
        else if (templateNPC != null && !templateNPC.WornArmor.IsNull && templateNPC.WornArmor.TryResolve(templateLinkCache, out var templateArmorGetter))
        {
            candidateAAs = GetAvailableArmature(templateArmorGetter, mainLinkCache, templateLinkCache, false, true);
            sourceArmorAddon = ChooseArmature(candidateAAs, type, currentRaceIDstrs);
            replaceExistingArmature = sourceArmorAddon is not null;

            if (!TryGetGeneratedRecord(pathSignature, templateNPC, out newArmorAddon) && sourceArmorAddon != null)
            {
                HashSet<IMajorRecord> subRecords = new HashSet<IMajorRecord>();
                newArmorAddon = (ArmorAddon)RecordGenerator.DeepCopyRecordToPatch(sourceArmorAddon, sourceArmorAddon.FormKey.ModKey, templateLinkCache, outputMod, subRecords);
                RecordGenerator.IncrementEditorID(subRecords);
                RecordGenerator.AddGeneratedRecordToDictionary(pathSignature, templateNPC, newArmorAddon);

                var assignedSkinTexture = AssignSkinTexture(newArmorAddon, true, npcInfo, outputMod, mainLinkCache, templateLinkCache, hardcodedPaths, subPath, npcObjectMap, objectCaches);
            }
        }

        if (sourceArmorAddon == null)
        {
            _logger.LogReport("Could not resolve " + type.ToString() + " armature for NPC " + npcInfo.LogIDstring + " or its template.", true, npcInfo);
        }
        else if (replaceExistingArmature == false)
        {
            parentArmorRecord.Armature.Add(newArmorAddon);
        }
        else
        {
            var templateFK = sourceArmorAddon.FormKey.ToString();
            for (int i = 0; i < parentArmorRecord.Armature.Count; i++)
            {
                if (parentArmorRecord.Armature[i].FormKey.ToString() == templateFK)
                {
                    parentArmorRecord.Armature[i] = newArmorAddon.ToLinkGetter();
                }
            }
        }

        npcObjectMap.Add(subPath, newArmorAddon);

        if (additionalGenericPaths.Any())
        {
            recordGenerator.AssignGenericAssetPaths(npcInfo, additionalGenericPaths, targetNPC, templateLinkCache, outputMod, GetLongestPath(additionalGenericPaths), true, false, npcObjectMap, objectCaches, new List<FilePathReplacementParsed>(), Patcher.GetBlankHeadPartAssignment());
        }

        return newArmorAddon;
    }

    private HashSet<IArmorAddonGetter> GetAvailableArmature(IArmorGetter parentArmor, ILinkCache mainLinkCache, ILinkCache templateLinkCache, bool checkMainLinkCache, bool checkTemplateLinkCache)
    {
        HashSet<IArmorAddonGetter> candidateAAs = new HashSet<IArmorAddonGetter>();
        foreach (var aa in parentArmor.Armature)
        {
            if (checkMainLinkCache && aa.TryResolve(mainLinkCache, out var candidateAA))
            {
                candidateAAs.Add(candidateAA);
            }
            else if (checkTemplateLinkCache && aa.TryResolve(templateLinkCache, out var candidateAAfromTemplate))
            {
                candidateAAs.Add(candidateAAfromTemplate);
            }
        }
        return candidateAAs;
    }

    private TextureSet AssignSkinTexture(ArmorAddon parentArmorAddonRecord, bool parentAssignedFromTemplate, NPCInfo npcInfo, ISkyrimMod outputMod, ILinkCache<ISkyrimMod, ISkyrimModGetter> mainLinkCache, ILinkCache<ISkyrimMod, ISkyrimModGetter> templateLinkCache, HashSet<FilePathReplacementParsed> paths, string subPath, Dictionary<string, dynamic> npcObjectMap, Dictionary<FormKey, Dictionary<string, dynamic>> objectCaches)
    {
        INpcGetter templateNPC = GetTemplateForHardcodedAssignments(paths, WornArmorPaths, subPath);

        IFormLinkNullableGetter<ITextureSetGetter> parentSkinTexture = null;
        switch (npcInfo.Gender)
        {
            case Gender.Male: parentSkinTexture = parentArmorAddonRecord.SkinTexture.Male; break;
            case Gender.Female: parentSkinTexture = parentArmorAddonRecord.SkinTexture.Female; break;
        }


        TextureSet newSkinTexture = null;
        bool assignedFromDictionary = false;
        var pathSignature = paths.Select(x => x.Source).ToHashSet();

        if (parentSkinTexture != null && !parentSkinTexture.IsNull && RecordGenerator.TryGetModifiedRecord(pathSignature, npcInfo.NPC.HeadTexture.FormKey, out newSkinTexture))
        {
            assignedFromDictionary = true;
        }
        else if (parentSkinTexture != null && !parentSkinTexture.IsNull && mainLinkCache.TryResolve<ITextureSetGetter>(parentSkinTexture.FormKey, out var existingSkinTexture))
        {
            newSkinTexture = outputMod.TextureSets.AddNew();
            newSkinTexture.DeepCopyIn(existingSkinTexture);
            RecordGenerator.AssignEditorID(newSkinTexture, existingSkinTexture.FormKey.ToString(), parentAssignedFromTemplate);
            RecordGenerator.AddModifiedRecordToDictionary(pathSignature, npcInfo.NPC.HeadTexture.FormKey, newSkinTexture);

        }
        else if (TryGetGeneratedRecord(pathSignature, templateNPC, out newSkinTexture))
        {
            assignedFromDictionary = true;
        }
        // no reason to try getting texture from record template because if one existed it would be inherited along with parentArmorAddonRecord
        else
        {
            _logger.LogReport("Could not resolve a skin texture from NPC " + Logger.GetNPCLogNameString(npcInfo.NPC) + " or its corresponding record template.", true, npcInfo);
            return null;
        }

        if (!assignedFromDictionary)
        {
            foreach (var path in paths)
            {
                if (path.Destination.Contains("GlowOrDetailMap"))
                {
                    newSkinTexture.GlowOrDetailMap = path.Source;
                }
                else if (path.Destination.Contains("Diffuse"))
                {
                    newSkinTexture.Diffuse = path.Source;
                }
                else if (path.Destination.Contains("NormalOrGloss"))
                {
                    newSkinTexture.NormalOrGloss = path.Source;
                }
                else if (path.Destination.Contains("BacklightMaskOrSpecular"))
                {
                    newSkinTexture.BacklightMaskOrSpecular = path.Source;
                }
            }
        }

        switch (npcInfo.Gender)
        {
            case Gender.Male: parentArmorAddonRecord.SkinTexture.Male = newSkinTexture.ToNullableLinkGetter(); break;
            case Gender.Female: parentArmorAddonRecord.SkinTexture.Female = newSkinTexture.ToNullableLinkGetter(); break;
        }

        objectCaches[npcInfo.NPC.FormKey][subPath + ".SkinTexture"] = newSkinTexture;

        return newSkinTexture;
    }

    private static IArmorAddonGetter ChooseArmature(HashSet<IArmorAddonGetter> candidates, ArmorAddonType type, HashSet<string> requiredRaceFKstrs)
    {
        IEnumerable<IArmorAddonGetter> filteredFlags = null;
        switch (type)
        {
            case ArmorAddonType.Torso: filteredFlags = candidates.Where(x => x.BodyTemplate.FirstPersonFlags.HasFlag(BipedObjectFlag.Body)); break;
            case ArmorAddonType.Hands: filteredFlags = candidates.Where(x => x.BodyTemplate.FirstPersonFlags.HasFlag(BipedObjectFlag.Hands)); break;
            case ArmorAddonType.Feet: filteredFlags = candidates.Where(x => x.BodyTemplate.FirstPersonFlags.HasFlag(BipedObjectFlag.Feet)); break;
            case ArmorAddonType.Tail: filteredFlags = candidates.Where(x => x.BodyTemplate.FirstPersonFlags.HasFlag(BipedObjectFlag.Tail)); break;
        }
        if (filteredFlags == null || !filteredFlags.Any()) { return null; }
        return filteredFlags.Where(x => requiredRaceFKstrs.Contains(x.Race.FormKey.ToString())).FirstOrDefault();
    }

    private static HashSet<string> TorsoArmorAddonPaths = new HashSet<string>()
    {
        "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Body) && PatchableRaces.Contains(Race)].SkinTexture.Male.GlowOrDetailMap",
        "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Body) && PatchableRaces.Contains(Race)].SkinTexture.Female.GlowOrDetailMap",

        "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Body) && PatchableRaces.Contains(Race)].SkinTexture.Male.Diffuse",
        "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Body) && PatchableRaces.Contains(Race)].SkinTexture.Female.Diffuse",

        "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Body) && PatchableRaces.Contains(Race)].SkinTexture.Male.NormalOrGloss",
        "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Body) && PatchableRaces.Contains(Race)].SkinTexture.Female.NormalOrGloss",

        "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Body) && PatchableRaces.Contains(Race)].SkinTexture.Male.BacklightMaskOrSpecular",
        "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Body) && PatchableRaces.Contains(Race)].SkinTexture.Female.BacklightMaskOrSpecular"
    };

    private static HashSet<string> HandsArmorAddonPaths = new HashSet<string>()
    {
        "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Hands) && PatchableRaces.Contains(Race)].SkinTexture.Male.GlowOrDetailMap",
        "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Hands) && PatchableRaces.Contains(Race)].SkinTexture.Female.GlowOrDetailMap",

        "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Hands) && PatchableRaces.Contains(Race)].SkinTexture.Male.Diffuse",
        "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Hands) && PatchableRaces.Contains(Race)].SkinTexture.Female.Diffuse",

        "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Hands) && PatchableRaces.Contains(Race)].SkinTexture.Male.NormalOrGloss",
        "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Hands) && PatchableRaces.Contains(Race)].SkinTexture.Female.NormalOrGloss",

        "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Hands) && PatchableRaces.Contains(Race)].SkinTexture.Male.BacklightMaskOrSpecular",
        "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Hands) && PatchableRaces.Contains(Race)].SkinTexture.Female.BacklightMaskOrSpecular"
    };

    private static HashSet<string> FeetArmorAddonPaths = new HashSet<string>()
    {
        "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Feet) && PatchableRaces.Contains(Race)].SkinTexture.Male.GlowOrDetailMap",
        "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Feet) && PatchableRaces.Contains(Race)].SkinTexture.Female.GlowOrDetailMap",

        "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Feet) && PatchableRaces.Contains(Race)].SkinTexture.Male.Diffuse",
        "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Feet) && PatchableRaces.Contains(Race)].SkinTexture.Female.Diffuse",

        "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Feet) && PatchableRaces.Contains(Race)].SkinTexture.Male.NormalOrGloss",
        "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Feet) && PatchableRaces.Contains(Race)].SkinTexture.Female.NormalOrGloss",

        "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Feet) && PatchableRaces.Contains(Race)].SkinTexture.Male.BacklightMaskOrSpecular",
        "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Feet) && PatchableRaces.Contains(Race)].SkinTexture.Female.BacklightMaskOrSpecular"
    };

    private static HashSet<string> TailArmorAddonPaths = new HashSet<string>()
    {
        "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Tail) && PatchableRaces.Contains(Race)].SkinTexture.Male.GlowOrDetailMap",
        "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Tail) && PatchableRaces.Contains(Race)].SkinTexture.Female.GlowOrDetailMap",

        "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Tail) && PatchableRaces.Contains(Race)].SkinTexture.Male.Diffuse",
        "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Tail) && PatchableRaces.Contains(Race)].SkinTexture.Female.Diffuse",

        "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Tail) && PatchableRaces.Contains(Race)].SkinTexture.Male.NormalOrGloss",
        "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Tail) && PatchableRaces.Contains(Race)].SkinTexture.Female.NormalOrGloss",

        "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Tail) && PatchableRaces.Contains(Race)].SkinTexture.Male.BacklightMaskOrSpecular",
        "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Tail) && PatchableRaces.Contains(Race)].SkinTexture.Female.BacklightMaskOrSpecular"
    };

    private static HashSet<string> WornArmorPaths = new HashSet<string>().Concat(TorsoArmorAddonPaths).Concat(HandsArmorAddonPaths).Concat(FeetArmorAddonPaths).Concat(TailArmorAddonPaths).ToHashSet();

    private static HashSet<string> HeadTexturePaths = new HashSet<string>()
    {
        "HeadTexture.Height",
        "HeadTexture.Diffuse" ,
        "HeadTexture.NormalOrGloss",
        "HeadTexture.GlowOrDetailMap",
        "HeadTexture.BacklightMaskOrSpecular",
    };

    private static bool TryGetGeneratedRecord<T>(HashSet<string> pathSignature, INpcGetter template, out T record) where T : class
    {
        if (RecordGenerator.GeneratedRecordsByTempateNPC.ContainsKey(pathSignature) && RecordGenerator.GeneratedRecordsByTempateNPC[pathSignature].ContainsKey(template.FormKey.ToString()))
        {
            record = RecordGenerator.GeneratedRecordsByTempateNPC[pathSignature][template.FormKey.ToString()] as T;
            return record != null;
        }
        else
        {
            record = null;
            return false;
        }
    }

    public void ReplacerCombinationToRecords(SubgroupCombination combination, NPCInfo npcInfo, SkyrimMod outputMod, ILinkCache<ISkyrimMod, ISkyrimModGetter> recordTemplateLinkCache, Dictionary<string, dynamic> npcObjectMap, Dictionary<FormKey, Dictionary<string, dynamic>> objectCaches, Dictionary<HeadPart.TypeEnum, HeadPart> generatedHeadParts, RecordGenerator recordGenerator)
    {
        if (combination.DestinationType == SubgroupCombination.DestinationSpecifier.HeadPartFormKey)
        {
            AssignKnownHeadPartReplacer(combination, npcInfo.NPC, outputMod, generatedHeadParts, npcInfo, _headPartSelector, _environmentProvider);
        }
        else if (combination.DestinationType == SubgroupCombination.DestinationSpecifier.Generic)
        {
            List<FilePathReplacementParsed> nonHardcodedPaths = new List<FilePathReplacementParsed>();
            int longestPath = 0;

            foreach (var subgroup in combination.ContainedSubgroups)
            {
                foreach (var path in subgroup.Paths)
                {
                    var parsed = new FilePathReplacementParsed(path, npcInfo, combination.AssetPack, recordTemplateLinkCache, combination, _logger);

                    nonHardcodedPaths.Add(parsed);
                    if (parsed.Destination.Length > longestPath)
                    {
                        longestPath = parsed.Destination.Length;
                    }
                }
            }
            if (nonHardcodedPaths.Any())
            {
                var currentNPC = outputMod.Npcs.GetOrAddAsOverride(npcInfo.NPC);
                recordGenerator.AssignGenericAssetPaths(npcInfo, nonHardcodedPaths, currentNPC, null, outputMod, longestPath, false, true, npcObjectMap, objectCaches, new List<FilePathReplacementParsed>(), generatedHeadParts);
            }
        }
        else if (combination.DestinationType != SubgroupCombination.DestinationSpecifier.Main)
        {
            AssignSpecialCaseAssetReplacer(combination, npcInfo.NPC, outputMod, generatedHeadParts, npcInfo, _headPartSelector, _environmentProvider);
        }
    }
    private static void AssignKnownHeadPartReplacer(SubgroupCombination subgroupCombination, INpcGetter npcGetter, SkyrimMod outputMod, Dictionary<HeadPart.TypeEnum, HeadPart> generatedHeadParts, NPCInfo npcInfo, HeadPartSelector headPartSelector, IEnvironmentStateProvider environmentProvider)
    {
        var npc = outputMod.Npcs.GetOrAddAsOverride(npcGetter);
        var headPart = npc.HeadParts.Where(x => x.FormKey == subgroupCombination.ReplacerDestinationFormKey).FirstOrDefault();

        var pathSignature = new HashSet<string>();
        foreach (var subgroup in subgroupCombination.ContainedSubgroups)
        {
            pathSignature.UnionWith(subgroup.Paths.Select(x => x.Source));
        }

        for (int i = 0; i < npc.HeadParts.Count; i++)
        {
            if (npc.HeadParts[i].FormKey == subgroupCombination.ReplacerDestinationFormKey)
            {
                if (RecordGenerator.TryGetModifiedRecord(pathSignature, npc.HeadParts[i].FormKey, out HeadPart existingReplacer))
                {
                    //npc.HeadParts[i] = existingReplacer.AsLinkGetter();
                    headPartSelector.SetGeneratedHeadPart(existingReplacer, generatedHeadParts, npcInfo);
                }
                else if (environmentProvider.LinkCache.TryResolve<IHeadPartGetter>(npc.HeadParts[i].FormKey, out var hpGetter) && environmentProvider.LinkCache.TryResolve<ITextureSetGetter>(hpGetter.TextureSet.FormKey, out var tsGetter))
                {
                    var copiedHP = outputMod.HeadParts.AddNew();
                    copiedHP.DeepCopyIn(hpGetter);

                    var copiedTS = outputMod.TextureSets.AddNew();
                    copiedTS.DeepCopyIn(tsGetter);

                    foreach (var subgroup in subgroupCombination.ContainedSubgroups)
                    {
                        foreach (var path in subgroup.Paths)
                        {
                            if (path.Destination.EndsWith("TextureSet.Diffuse", StringComparison.OrdinalIgnoreCase))
                            {
                                copiedTS.Diffuse = path.Source;
                            }
                            else if (path.Destination.EndsWith("TextureSet.NormalOrGloss", StringComparison.OrdinalIgnoreCase))
                            {
                                copiedTS.NormalOrGloss = path.Source;
                            }
                        }
                    }
                    var tsEDID = EditorIDHandler.GetEditorIDSafely(copiedTS);
                    copiedTS.EditorID = tsEDID + "_" + subgroupCombination.AssetPack.Source.ShortName + "." + subgroupCombination.Signature;
                    copiedHP.TextureSet.SetTo(copiedTS);
                    var hpEDID = EditorIDHandler.GetEditorIDSafely(copiedHP);
                    copiedHP.EditorID = hpEDID + "_" + subgroupCombination.AssetPack.Source.ShortName + "." + subgroupCombination.Signature;

                    RecordGenerator.AddModifiedRecordToDictionary(pathSignature, npc.HeadParts[i].FormKey, copiedHP);

                    headPartSelector.SetGeneratedHeadPart(copiedHP, generatedHeadParts, npcInfo);
                }
                else
                {
                    // Warn user
                }
            }
        }
    }

    private static void AssignSpecialCaseAssetReplacer(SubgroupCombination subgroupCombination, INpcGetter npcGetter, SkyrimMod outputMod, Dictionary<HeadPart.TypeEnum, HeadPart> generatedHeadParts, NPCInfo npcInfo, HeadPartSelector headPartSelector, IEnvironmentStateProvider environmentProvider)
    {
        var npc = outputMod.Npcs.GetOrAddAsOverride(npcGetter);
        switch (subgroupCombination.DestinationType)
        {
            case SubgroupCombination.DestinationSpecifier.MarksFemaleHumanoid04RightGashR: AssignHeadPartByDiffusePath(subgroupCombination, npc, outputMod, "actors\\character\\female\\facedetails\\facefemalerightsidegash_04.dds", generatedHeadParts, npcInfo, headPartSelector, environmentProvider); break;
            case SubgroupCombination.DestinationSpecifier.MarksFemaleHumanoid06RightGashR: AssignHeadPartByDiffusePath(subgroupCombination, npc, outputMod, "actors\\character\\female\\facedetails\\facefemalerightsidegash_06.dds", generatedHeadParts, npcInfo, headPartSelector, environmentProvider); break;
            default: break; // Warn user
        }
    }

    private static void AssignHeadPartByDiffusePath(SubgroupCombination subgroupCombination, Npc npc, SkyrimMod outputMod, string diffusePath, Dictionary<HeadPart.TypeEnum, HeadPart> generatedHeadParts, NPCInfo npcInfo, HeadPartSelector headPartSelector, IEnvironmentStateProvider environmentProvider)
    {
        var pathSignature = new HashSet<string>();
        foreach (var subgroup in subgroupCombination.ContainedSubgroups)
        {
            pathSignature.UnionWith(subgroup.Paths.Select(x => x.Source));
        }

        for (int i = 0; i < npc.HeadParts.Count; i++)
        {
            if (RecordGenerator.TryGetModifiedRecord(pathSignature, npc.HeadParts[i].FormKey, out HeadPart existingReplacer))
            {
                //npc.HeadParts[i] = existingReplacer.AsLinkGetter();
                headPartSelector.SetGeneratedHeadPart(existingReplacer, generatedHeadParts, npcInfo);
            }
            else if (environmentProvider.LinkCache.TryResolve<IHeadPartGetter>(npc.HeadParts[i].FormKey, out var hpGetter) && environmentProvider.LinkCache.TryResolve<ITextureSetGetter>(hpGetter.TextureSet.FormKey, out var tsGetter) && tsGetter.Diffuse == diffusePath)
            {
                var copiedHP = outputMod.HeadParts.AddNew();
                copiedHP.DeepCopyIn(hpGetter);

                var copiedTS = outputMod.TextureSets.AddNew();
                copiedTS.DeepCopyIn(tsGetter);

                foreach (var subgroup in subgroupCombination.ContainedSubgroups)
                {
                    foreach (var path in subgroup.Paths)
                    {
                        if (path.Destination.EndsWith("TextureSet.Diffuse", StringComparison.OrdinalIgnoreCase))
                        {
                            copiedTS.Diffuse = path.Source;
                        }
                        else if (path.Destination.EndsWith("TextureSet.NormalOrGloss", StringComparison.OrdinalIgnoreCase))
                        {
                            copiedTS.NormalOrGloss = path.Source;
                        }
                    }
                }
                copiedTS.EditorID += "_" + subgroupCombination.AssetPack.Source.ShortName + "." + subgroupCombination.Signature;
                copiedHP.TextureSet.SetTo(copiedTS);
                copiedHP.EditorID += "_" + subgroupCombination.AssetPack.Source.ShortName + "." + subgroupCombination.Signature;

                RecordGenerator.AddModifiedRecordToDictionary(pathSignature, npc.HeadParts[i].FormKey, copiedHP);

                headPartSelector.SetGeneratedHeadPart(copiedHP, generatedHeadParts, npcInfo);
            }
        }
    }
}