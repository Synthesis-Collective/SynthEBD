using System;
using System.Collections.Generic;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Skyrim;

namespace SynthEBD;

/// <summary>
/// Collects the FaceGen shape names belonging to head parts of a given type,
/// feeding CharacterViewer.Rendering's <c>ResolvedNpcMeshPaths.EyeShapeNames</c>
/// (the renderer's authoritative IsEye input). The Creation Kit — and SynthEBD's
/// FaceGenPreviewService — bakes one shape per geometry-bearing head part, named
/// after its EditorID, so these EditorID sets identify eyeball shapes exactly,
/// including custom eyes authored as BSLSP_ENVMAP with names the renderer's
/// plural-"Eyes" heuristic misses (e.g. "FoxGloveEyeMesh", which rendered with
/// SSAO-blackened eyes until classified via its HeadPart record).
///
/// Mirrors NPC Plugin Chooser 2's FaceGenConsistencyAnalyzer.CollectShapeNamesOfType;
/// keep the walk semantics in sync: NPC parts + race defaults for slot types the
/// NPC does not occupy + Extra Parts recursion with a cycle guard, returned
/// OrdinalIgnoreCase to match the engine's case-insensitive shape-name/EditorID
/// reconciliation.
/// </summary>
public static class HeadPartShapeNames
{
    /// <summary>EditorIDs of the NPC's effective head parts of
    /// <paramref name="type"/>: the NPC's own parts, plus the race's defaults
    /// for slot types the NPC does not occupy, each with its Extra Parts
    /// (which inherit the typed parent's classification — their own Type is
    /// typically null/Misc).</summary>
    public static HashSet<string> CollectFromNpcRecord(
        INpcGetter npc, ILinkCache linkCache, HeadPart.TypeEnum type)
    {
        var names = NewSet();
        var visited = new HashSet<FormKey>();

        // NPC's own head parts; record the slot Types it occupies so race
        // defaults for those slots are skipped.
        var npcSlotTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (npc.HeadParts != null)
        {
            foreach (var link in npc.HeadParts)
            {
                if (link == null || link.IsNull) continue;
                if (!linkCache.TryResolve<IHeadPartGetter>(link.FormKey, out var hp)) continue;
                var slot = hp.Type.ToString();
                if (!string.IsNullOrEmpty(slot)) npcSlotTypes.Add(slot);
                if (hp.Type == type) Collect(hp, linkCache, names, visited);
            }
        }

        if (!npc.Race.IsNull && linkCache.TryResolve<IRaceGetter>(npc.Race.FormKey, out var race))
        {
            bool female = npc.Configuration.Flags.HasFlag(NpcConfiguration.Flag.Female);
            var headData = female ? race.HeadData?.Female : race.HeadData?.Male;
            if (headData?.HeadParts != null)
            {
                foreach (var hpRef in headData.HeadParts)
                {
                    if (hpRef.Head.IsNull) continue;
                    if (!linkCache.TryResolve<IHeadPartGetter>(hpRef.Head.FormKey, out var hp)) continue;
                    var slot = hp.Type.ToString();
                    if (!string.IsNullOrEmpty(slot) && npcSlotTypes.Contains(slot)) continue;
                    if (hp.Type == type) Collect(hp, linkCache, names, visited);
                }
            }
        }

        return names;
    }

    /// <summary>EditorIDs of one head part + its Extra Parts — for a preview
    /// ASSIGNMENT, where the baked shape is the assigned part regardless of
    /// what the NPC record references. Empty set when the part doesn't
    /// resolve.</summary>
    public static HashSet<string> CollectFromHeadPart(FormKey headPartKey, ILinkCache linkCache)
    {
        var names = NewSet();
        if (!headPartKey.IsNull && linkCache.TryResolve<IHeadPartGetter>(headPartKey, out var hp))
            Collect(hp, linkCache, names, new HashSet<FormKey>());
        return names;
    }

    private static HashSet<string> NewSet() => new(StringComparer.OrdinalIgnoreCase);

    private static void Collect(IHeadPartGetter hp, ILinkCache linkCache,
        HashSet<string> names, HashSet<FormKey> visited)
    {
        if (!visited.Add(hp.FormKey)) return;
        if (!string.IsNullOrEmpty(hp.EditorID)) names.Add(hp.EditorID!);
        if (hp.ExtraParts == null) return;
        foreach (var ep in hp.ExtraParts)
        {
            if (ep == null || ep.IsNull) continue;
            if (linkCache.TryResolve<IHeadPartGetter>(ep.FormKey, out var extra))
                Collect(extra, linkCache, names, visited);
        }
    }
}
