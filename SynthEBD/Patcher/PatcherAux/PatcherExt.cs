using Mutagen.Bethesda;
using Mutagen.Bethesda.FormKeys.SkyrimSE;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Skyrim;

namespace SynthEBD;

/// <summary>
/// Mutagen extension helpers for the patcher. Currently provides the surrogate-NPC duplication
/// routine used by <see cref="SurrogateNPCProvider"/> in SkyPatcher mode.
/// </summary>
public static class PatcherExt
{
    /// <summary>
    /// Creates surrogate NPC records in the output mod by duplicating appearance-related
    /// data from source NPCs. Each surrogate gets a new FormKey and contains all visual
    /// appearance data needed for SkyPatcher's CopyVisualStyle to transfer the look to
    /// the original NPC at runtime.
    ///
    /// When <paramref name="onlyAppearance"/> is true, the surrogate includes:
    ///   - FormLink references: Race, WornArmor (skin), HeadTexture, HairColor, HeadParts
    ///   - Value-type data: FaceMorph, FaceParts, Height, Weight, TextureLighting, TintLayers
    ///
    /// Sub-records from <paramref name="modKeyToDuplicateFrom"/> are walked and duplicated
    /// into the output mod with new FormKeys, UNLESS their source mod appears in
    /// <paramref name="blockedMods"/>. Blocked sub-records are still referenced by the
    /// surrogate NPC but point to the original records rather than remapped copies.
    /// This is safe because SkyPatcher resolves these references at runtime.
    /// </summary>
    /// <param name="modToDuplicateInto">The output mod that receives the surrogate and duplicated sub-records.</param>
    /// <param name="recordsToDuplicate">Source records to process. When <paramref name="onlyAppearance"/> is true these must be <see cref="INpcGetter"/>s.</param>
    /// <param name="linkCache">Link cache used to resolve referenced records.</param>
    /// <param name="modKeyToDuplicateFrom">The mod whose owned sub-records are duplicated; must differ from the output mod's key.</param>
    /// <param name="mapping">By-ref original→duplicate FormKey map, populated here and applied via RemapLinks. Persists across calls.</param>
    /// <param name="onlyAppearance">When true, build appearance-only surrogate NPC records rather than full duplicates.</param>
    /// <param name="topLevelRemaps">By-ref map of original→surrogate FormKeys for the top-level records, for callers to locate the new records.</param>
    /// <param name="blockedMods">Mods whose sub-records are referenced but not duplicated; the surrogate keeps the original FormKeys.</param>
    /// <param name="typesToInspect">Unused passthrough type filter.</param>
    /// <exception cref="ArgumentException">Thrown if the source mod key equals the output mod key, or a non-NPC record is passed with <paramref name="onlyAppearance"/>.</exception>
    /// <exception cref="KeyNotFoundException">Thrown if an identified record cannot be resolved for duplication.</exception>
    public static void DuplicateFromOnlyReferencedNpcs<TMod, TModGetter>(
        this TMod modToDuplicateInto,
        IEnumerable<IMajorRecordGetter> recordsToDuplicate,
        ILinkCache<TMod, TModGetter> linkCache, 
        ModKey modKeyToDuplicateFrom,
        ref Dictionary<FormKey, FormKey> mapping, bool onlyAppearance,
        ref Dictionary<FormKey, FormKey> topLevelRemaps,
        HashSet<ModKey> blockedMods = null,
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

            // Sub-records from blocked mods are NOT duplicated. The surrogate
            // NPC's FormLinks still reference the originals (set above in the
            // onlyAppearance block). RemapLinks won't touch them because they
            // never enter the mapping dictionary.
            if (blockedMods != null && blockedMods.Contains(link.FormKey.ModKey)) return;

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

        if (onlyAppearance)
        {
            foreach (var record in recordsToDuplicate)
            {
                var npcGetter = record as INpcGetter;
                if (npcGetter is null)
                {
                    throw new ArgumentException("When onlyAppearance == true, recordsToDuplicate must be of type INpcGetter" +
                                                Environment.NewLine + "FormKey: " + record.FormKey.ToString());
                }

                var newNpc = new Npc(modToDuplicateInto, npcGetter.EditorID ?? npcGetter.Name?.String ?? npcGetter.FormKey.ToString() ?? "NewNpc");
                modToDuplicateInto.Npcs.Add(newNpc);
                topLevelRemaps.Add(record.FormKey, newNpc.FormKey);

                // ── FormLink properties ──
                // Each link is set to the original value first, then AddAllLinks
                // walks its sub-records for duplication. After RemapLinks at the
                // end, non-blocked sub-records get remapped to their duplicates
                // while blocked ones keep their original FormKeys.

                if (!npcGetter.Race.IsNull)
                {
                    AddAllLinks(npcGetter.Race);
                    newNpc.Race.SetTo(npcGetter.Race);
                }
                else
                {
                    newNpc.Race.SetTo(Skyrim.Race.DefaultRace);
                }

                if (!npcGetter.WornArmor.IsNull)
                {
                    AddAllLinks(npcGetter.WornArmor);
                    newNpc.WornArmor.SetTo(npcGetter.WornArmor);
                }

                if (!npcGetter.HeadTexture.IsNull)
                {
                    AddAllLinks(npcGetter.HeadTexture);
                    newNpc.HeadTexture.SetTo(npcGetter.HeadTexture);
                }

                if (!npcGetter.HairColor.IsNull)
                {
                    AddAllLinks(npcGetter.HairColor);
                    newNpc.HairColor.SetTo(npcGetter.HairColor);
                }

                newNpc.HeadParts.Clear();
                foreach (var hp in npcGetter.HeadParts)
                {
                    if (!hp.IsNull)
                    {
                        AddAllLinks(hp);
                        newNpc.HeadParts.Add(hp);
                    }
                }

                // ── Value-type properties ──
                // These contain no FormLinks and are deep-copied directly.

                newNpc.FaceMorph = npcGetter.FaceMorph?.DeepCopy();
                newNpc.FaceParts = npcGetter.FaceParts?.DeepCopy();
                newNpc.Height = npcGetter.Height;
                newNpc.Weight = npcGetter.Weight;
                newNpc.TextureLighting = npcGetter.TextureLighting;

                newNpc.TintLayers.Clear();
                if (npcGetter.TintLayers != null)
                {
                    newNpc.TintLayers.AddRange(npcGetter.TintLayers.Select(t => t.DeepCopy()));
                }

                if (npcGetter.Configuration.Flags.HasFlag(NpcConfiguration.Flag.Female))
                {
                    newNpc.Configuration.Flags |= NpcConfiguration.Flag.Female;
                }
                else
                {
                    newNpc.Configuration.Flags &= ~NpcConfiguration.Flag.Female;
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
                var newEdid = (rec.Record.EditorID ?? "NoEditorID") + SurrogateNPCProvider.SurrogateSuffix;
                var dup = rec.DuplicateIntoAsNewRecord(modToDuplicateInto, newEdid);
                dup.EditorID = newEdid;
                mapping[rec.Record.FormKey] = dup.FormKey;

                // record the remap only for the original top-level inputs, not the transitively-pulled sub-records
                // (the previous `Contains(dup)` tested the freshly-created duplicate against the source list, so it was always false)
                if (recordsToDuplicate.Any(r => r.FormKey.Equals(rec.Record.FormKey)))
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