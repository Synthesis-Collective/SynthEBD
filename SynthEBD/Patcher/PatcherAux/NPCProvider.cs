using Mutagen.Bethesda;
using Mutagen.Bethesda.FormKeys.SkyrimSE;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Skyrim;
using Noggog;

namespace SynthEBD;

public class NPCProvider
{
    private readonly IOutputEnvironmentStateProvider _environmentStateProvider;
    private readonly PatcherState _patcherState;
    private Dictionary<FormKey, FormKey> _formKeyMap = new();
    private Dictionary<FormKey, Npc> _importedNPCMap = new();

    public const string importedSuffix = "_SynthEBD_Imported";

    public NPCProvider(IOutputEnvironmentStateProvider environmentStateProvider, PatcherState patcherState)
    {
        _environmentStateProvider = environmentStateProvider;
        _patcherState = patcherState;
    }

    public void Reinitialize()
    {
        _formKeyMap.Clear();
    }

    public Npc? GetNpc(INpcGetter npcGetter, bool onlyFromImportedNpcs, bool ignoreOutputModContext)
    {
        if (_importedNPCMap.TryGetValue(npcGetter.FormKey, out Npc importedNpc))
        {
            return importedNpc;
        }
        if (onlyFromImportedNpcs)
        {
            return null;
        }
        
        var outputMod = _environmentStateProvider.OutputMod;
        
        _environmentStateProvider.LinkCache.TryResolveContext(npcGetter.FormKey, typeof(INpcGetter), out var context);
        if (context.ModKey.Equals(outputMod.ModKey) && ignoreOutputModContext)
        {
            var allContexts = npcGetter.ToLink().ResolveAllContexts<ISkyrimMod, ISkyrimModGetter, INpc, INpcGetter>(_environmentStateProvider.LinkCache).ToArray();
            var prePatchingWinningOverride = allContexts.FirstOrDefault(x => !x.ModKey.Equals(outputMod.ModKey));
            if (prePatchingWinningOverride != null)
            {
                context = prePatchingWinningOverride;
            }
        }

        if (context.ModKey.Equals(outputMod.ModKey))
        {
            return context.Record as Npc;
        }
        
        Npc npcRecord = null;

        if (!_patcherState.GeneralSettings.BlockedModsFromImport.Contains(context.ModKey) && !outputMod.ModKey.Equals(context.ModKey))
        {
            Dictionary<FormKey, FormKey> remappedNpcs = new();
            outputMod.DuplicateFromOnlyReferencedNpcs(new List<INpcGetter>() { context.Record as INpcGetter },
                _environmentStateProvider.LinkCache, context.ModKey, ref _formKeyMap, true, ref remappedNpcs);

            var remappedNpcFk = remappedNpcs[npcGetter.FormKey];

            npcRecord = outputMod.Npcs.First(x => x.FormKey.Equals(remappedNpcFk));
            if (!npcRecord.EditorID.IsNullOrWhitespace())
            {
                npcRecord.EditorID += "_SynthEBD";
            }
            _importedNPCMap.Add(npcGetter.FormKey, npcRecord);
        }
        else
        {
            return _environmentStateProvider.OutputMod.Npcs.GetOrAddAsOverride(npcGetter);
        }
        
        return npcRecord;
    }

    public bool TryGetImportedFormKey(FormKey templateFormKey, out FormKey importedFormKey)
    {
        if (_formKeyMap.ContainsKey(templateFormKey))
        {
            importedFormKey = _formKeyMap[templateFormKey];
            return true;
        }
        else
        {
            importedFormKey = default;
            return false;
        }
    }
}
public static class PatcherExt
{
    public static void DuplicateFromOnlyReferencedNpcs<TMod, TModGetter>(
        this TMod modToDuplicateInto,
        IEnumerable<IMajorRecordGetter> recordsToDuplicate,
        ILinkCache<TMod, TModGetter> linkCache, 
        ModKey modKeyToDuplicateFrom,
        ref Dictionary<FormKey, FormKey> mapping, bool onlySkin,
        ref Dictionary<FormKey, FormKey> topLevelRemaps,
        params Type[] typesToInspect)
        where TModGetter : class, IModGetter
        where TMod : class, TModGetter, IMod, ISkyrimMod
    {
        if (modKeyToDuplicateFrom == modToDuplicateInto.ModKey)
        {
            throw new ArgumentException("Cannot pass the target mod's Key as the one to extract and self contain");
        }

        // Compile list of things to duplicate
        HashSet<IFormLinkGetter> identifiedLinks = new();
        HashSet<FormKey> passedLinks = new();
        var implicits = Implicits.Get(modToDuplicateInto.GameRelease);

        void AddAllLinks(IFormLinkGetter link)
        {
            if (link.FormKey.IsNull) return;
            if (!passedLinks.Add(link.FormKey)) return;
            if (implicits.RecordFormKeys.Contains(link.FormKey)) return;

            if (!linkCache.TryResolve(link.FormKey, link.Type, out var linkRec))
            {
                return;
            }

            if (link.FormKey.ModKey == modKeyToDuplicateFrom)
            {
                identifiedLinks.Add(link);
            }

            foreach (var containedLink in linkRec.EnumerateFormLinks())
            {
                if (containedLink.FormKey.ModKey != modKeyToDuplicateFrom) continue;
                AddAllLinks(containedLink);
            }
        }

        if (onlySkin)
        {
            foreach (var record in recordsToDuplicate)
            {
                var npcGetter = record as INpcGetter;
                if (npcGetter is null)
                {
                    throw new ArgumentException("When onlySkin == true, recordsToDuplicate must be of type INpcGetter" +
                                                Environment.NewLine + "FormKey: " + record.FormKey.ToString());
                }

                var newNpc = new Npc(modToDuplicateInto, npcGetter.EditorID ?? npcGetter.Name?.String ?? npcGetter.FormKey.ToString() ?? "NewNpc");
                newNpc.Race.SetTo(Skyrim.Race.DefaultRace);
                modToDuplicateInto.Npcs.Add(newNpc);
                topLevelRemaps.Add(record.FormKey, newNpc.FormKey);
       
                if (!npcGetter.HeadTexture.IsNull)
                {
                    AddAllLinks(npcGetter.HeadTexture);
                    newNpc.HeadTexture.SetTo(npcGetter.HeadTexture);
                }

                if (!npcGetter.WornArmor.IsNull)
                {
                    AddAllLinks(npcGetter.WornArmor);
                    newNpc.WornArmor.SetTo(npcGetter.WornArmor);
                }
            }
        }
        else
        {
            foreach (var rec in recordsToDuplicate)
            {
                identifiedLinks.Add(rec.ToLink());
                AddAllLinks(new FormLinkInformation(rec.FormKey, rec.Registration.GetterType));
            }
        }

        // Duplicate in the records
        foreach (var identifiedRec in identifiedLinks)
        {
            if (!linkCache.TryResolveContext(identifiedRec.FormKey, identifiedRec.Type, out var rec))
            {
                throw new KeyNotFoundException($"Could not locate record to make self contained: {identifiedRec}");
            }

            if (!mapping.ContainsKey(rec.Record.FormKey))
            {
                var newEdid = (rec.Record.EditorID ?? "NoEditorID") + NPCProvider.importedSuffix;
                var dup = rec.DuplicateIntoAsNewRecord(modToDuplicateInto, newEdid);
                dup.EditorID = newEdid;
                mapping[rec.Record.FormKey] = dup.FormKey;

                if (recordsToDuplicate.Contains(dup))
                {
                    topLevelRemaps.Add(rec.Record.FormKey, dup.FormKey);
                }
            }
            
            // ToDo
            // Move this out of loop, and remove off a new IEnumerable<IFormLinkGetter> call
            modToDuplicateInto.Remove(identifiedRec.FormKey, identifiedRec.Type);
        }

        // Remap links
        modToDuplicateInto.RemapLinks(mapping);
    }
}