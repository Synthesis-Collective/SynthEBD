using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Skyrim;

namespace SynthEBD;

/// <summary>
/// Produces the texture- and mesh-override sets that <see cref="VM_CharacterViewer.ApplyTextureOverrides"/>
/// and <see cref="VM_CharacterViewer.ApplyMeshOverrides"/> consume for a selected asset-pack subgroup,
/// and resolves the effective race set for a subgroup by walking its parent chain. Used by the TexMesh
/// Render preview flow in <see cref="VM_AssetPresenter"/>.
///
/// <para><b>Record-driven resolution.</b> Rather than pattern-matching known substrings out of a
/// destination path (which only recognizes the fixed <see cref="FilePathDestinationMap"/> vocabulary),
/// every destination is resolved the way the patcher resolves it: the path is walked against the asset
/// pack's record-template NPC (and, as a fallback, the preview NPC) via <see cref="RecordPathParser"/>,
/// and the target body part / biped slots are read from the <em>actual resolved <c>ArmorAddon</c></em>'s
/// <c>BodyTemplate.FirstPersonFlags</c>. This lets the preview display any asset a config references —
/// a non-standard armature, a custom slot, an armature selected by a condition that never names its
/// slot — instead of silently dropping it. Texture slot (diffuse/normal/...) and the head-texture case
/// remain a fixed Mutagen property vocabulary. Worn-armor <c>AlternateTextures</c> resolve their target
/// 3D object <c>Name</c> so the texture lands on that named sub-shape (see
/// <see cref="ResolveAlternateTextureShapeName"/>) rather than the whole body.</para>
/// </summary>
public class SubgroupTextureMapper
{
    private readonly RecordPathParser _recordPathParser;
    private readonly Logger _logger;

    /// <summary>Creates the mapper.</summary>
    /// <param name="recordPathParser">Resolves each destination path against a record-template / preview NPC
    /// to recover the target armature (and, for alternate textures, the target 3D object name).</param>
    /// <param name="logger">Logger for path-resolution diagnostics.</param>
    public SubgroupTextureMapper(RecordPathParser recordPathParser, Logger logger)
    {
        _recordPathParser = recordPathParser;
        _logger = logger;
    }

    /// <summary>
    /// The overwrite key for an accumulated preview override: body part + texture slot, plus an
    /// optional shape name that distinguishes a worn-armor <c>AlternateTextures</c> override
    /// (which targets one named sub-shape) from the body-wide skin override of the same slot, so
    /// the two coexist instead of clobbering each other.
    /// </summary>
    public readonly record struct OverrideKey(string BodyPart, int Slot, string? ShapeName);

    /// <summary>
    /// The record roots a destination path is resolved against, in priority order: the asset pack's
    /// record-template NPC first (it holds the config's intended armatures / alternate textures), the
    /// loaded preview NPC second (a fallback for template-less packs, resolved through the live load
    /// order). Each root is paired with the link cache that can resolve <em>its</em> subrecords — the
    /// record-template link cache for the template, the environment link cache for the preview NPC.
    /// </summary>
    public sealed class DestinationResolutionContext
    {
        public INpcGetter? TemplateNpc { get; init; }
        public ILinkCache? TemplateLinkCache { get; init; }
        public INpcGetter? PreviewNpc { get; init; }
        public ILinkCache? PreviewLinkCache { get; init; }
        public Gender Gender { get; init; }
    }

    /// <summary>How a resolved destination feeds the renderer.</summary>
    private enum DestinationKind
    {
        /// <summary><c>HeadTexture.&lt;slot&gt;</c> on the NPC — the face texture.</summary>
        HeadTexture,
        /// <summary>An armature's <c>SkinTexture.&lt;sex&gt;.&lt;slot&gt;</c> — a body-part skin texture.</summary>
        SkinTexture,
        /// <summary>An armature's <c>WorldModel.&lt;sex&gt;.AlternateTextures[...]</c> — a per-shape texture.</summary>
        AlternateTexture,
        /// <summary>An armature's <c>WorldModel.&lt;sex&gt;.File</c> — a mesh (feeds the mesh-override channel).</summary>
        Mesh
    }

    /// <summary>A destination resolved to its renderer meaning: what kind of asset it is, which biped
    /// slots it occupies (from the resolved armature), which texture slot, and (for alternate textures)
    /// the target 3D object name. <see cref="Source"/> is the config's source asset path.</summary>
    private readonly record struct ResolvedDestination(
        DestinationKind Kind,
        int BipedSlotsMask,
        int TextureSlot,
        string? ShapeName,
        string Source);

    /// <summary>
    /// Resolves a flat set of <see cref="FilePathReplacement"/> paths (all sharing one resolution
    /// <paramref name="ctx"/>, i.e. one asset pack + preview NPC) into an <see cref="OverrideKey"/> →
    /// <see cref="TextureOverride"/> map, keeping the last-seen replacement per key. Mesh destinations
    /// are ignored here (they feed <see cref="MapPathsMeshOverrides"/>). This is the shared core used by
    /// the subgroup / combination overloads and by the Specific-NPC / Consistency preview surfaces, which
    /// resolve each contributing subgroup's paths against its own pack's context.
    /// </summary>
    public Dictionary<OverrideKey, TextureOverride> MapPathsTextures(IEnumerable<FilePathReplacement> paths, DestinationResolutionContext ctx)
    {
        var result = new Dictionary<OverrideKey, TextureOverride>();
        if (paths == null) return result;

        foreach (var path in paths)
        {
            if (TryResolveDestination(path, ctx, out var resolved))
            {
                EmitTextureOverrides(resolved, result);
            }
        }
        return result;
    }

    /// <summary>
    /// Builds the <see cref="OverrideKey"/> → <see cref="TextureOverride"/> map for the given subgroup's
    /// own Paths, resolving each destination against the record template / preview NPC. Mesh destinations
    /// are ignored here (they feed <see cref="MapSubgroupMeshOverrides"/>).
    /// </summary>
    public Dictionary<OverrideKey, TextureOverride> MapSubgroupTextures(VM_SubgroupPlaceHolder node, DestinationResolutionContext ctx)
    {
        if (node?.AssociatedModel?.Paths == null) return new Dictionary<OverrideKey, TextureOverride>();
        return MapPathsTextures(node.AssociatedModel.Paths, ctx);
    }

    /// <summary>
    /// Builds the <see cref="OverrideKey"/> → <see cref="TextureOverride"/> map for a fully-resolved
    /// <see cref="SubgroupCombination"/> (one valid distribution roll). Walks every contained
    /// <see cref="FlattenedSubgroup"/>'s already-inherited <c>Paths</c>, keeping the last-seen replacement
    /// per key. This reflects only the subgroups the distribution simulator actually selected for the
    /// preview NPC. Drives the "Select from Config File" action.
    /// </summary>
    public Dictionary<OverrideKey, TextureOverride> MapCombinationTextures(SubgroupCombination combination, DestinationResolutionContext ctx)
    {
        if (combination?.ContainedSubgroups == null) return new Dictionary<OverrideKey, TextureOverride>();
        var paths = combination.ContainedSubgroups
            .Where(sg => sg?.Paths != null)
            .SelectMany(sg => sg.Paths);
        return MapPathsTextures(paths, ctx);
    }

    /// <summary>
    /// Mesh-override counterpart to <see cref="MapPathsTextures"/>: resolves a flat set of paths (sharing
    /// one <paramref name="ctx"/>) into the auxiliary-armature <see cref="MeshOverride"/> list. Shared by
    /// the subgroup / combination overloads and the Specific-NPC / Consistency preview surfaces.
    /// </summary>
    public List<MeshOverride> MapPathsMeshOverrides(IEnumerable<FilePathReplacement> paths, DestinationResolutionContext ctx)
    {
        return BuildMeshOverrides(paths ?? Enumerable.Empty<FilePathReplacement>(), ctx);
    }

    /// <summary>
    /// Assembles the record-path resolution context for one asset pack + preview NPC: the pack's
    /// race-specific record-template NPC (resolved through the pack's record-template link cache) as the
    /// primary root, and the preview NPC (with <paramref name="previewLinkCache"/>) as a fallback root for
    /// template-less packs. Mirrors <see cref="FilePathReplacementParsed"/>'s template selection. The gate
    /// gender is the preview NPC's own gender when it resolves, else the pack's declared gender.
    /// </summary>
    public static DestinationResolutionContext BuildContext(VM_AssetPack? pack, INpcGetter? previewNpc, ILinkCache? previewLinkCache)
    {
        var recordTemplateLinkCache = pack?.RecordTemplateLinkCache;
        INpcGetter? templateNpc = null;

        if (pack != null && recordTemplateLinkCache != null)
        {
            FormKey npcRace = previewNpc?.Race != null && !previewNpc.Race.IsNull ? previewNpc.Race.FormKey : FormKey.Null;

            FormKey templateFK = new FormKey();
            foreach (var additionalTemplate in pack.AdditionalRecordTemplateAssignments)
            {
                if (additionalTemplate.RaceFormKeys.Contains(npcRace))
                {
                    templateFK = additionalTemplate.TemplateNPC;
                    break;
                }
            }
            if (templateFK.IsNull) templateFK = pack.DefaultTemplateFK;

            recordTemplateLinkCache.TryResolve<INpcGetter>(templateFK, out templateNpc);
        }

        Gender gender = previewNpc != null ? NPCInfo.GetGender(previewNpc) : (pack?.Gender ?? Gender.Male);

        return new DestinationResolutionContext
        {
            TemplateNpc = templateNpc,
            TemplateLinkCache = recordTemplateLinkCache,
            PreviewNpc = previewNpc,
            PreviewLinkCache = previewLinkCache,
            Gender = gender,
        };
    }

    /// <summary>
    /// Builds the neutral <see cref="MeshOverride"/> list for a selected subgroup: one per non-base biped
    /// armature it (or an ancestor) defines a <c>WorldModel.&lt;sex&gt;.File</c> for. The driving case is
    /// an auxiliary armature on a non-base slot (e.g. slot 52) that a mod adds to the actor only at runtime
    /// by script, so it never appears in the resolver's static WornArmor walk and must be synthesized from
    /// the config selection.
    ///
    /// <para>Base armatures (Body/Hands/Feet/Hair/Tail/Head) are intentionally excluded — those are already
    /// resolved onto the rendered NPC, and the texture channel handles their skin overrides. Only generic
    /// "Slot{n}" armatures become mesh overrides, so the base body is never double-rendered. Same-slot
    /// <c>SkinTexture.&lt;sex&gt;.*</c> paths in the selection are bundled onto the override; absent that,
    /// the renderer falls back to the NIF's own <c>BSShaderTextureSet</c>.</para>
    /// </summary>
    public List<MeshOverride> MapSubgroupMeshOverrides(VM_SubgroupPlaceHolder node, DestinationResolutionContext ctx)
    {
        if (node == null) return new List<MeshOverride>();

        // The mesh template may sit on the selected leaf or on an ancestor;
        // walk the chain leaf-first so the most-specific definition wins.
        var chain = new List<VM_SubgroupPlaceHolder> { node };
        chain.AddRange(node.GetParents());

        var paths = chain
            .Where(link => link?.AssociatedModel?.Paths != null)
            .SelectMany(link => link.AssociatedModel.Paths);
        return BuildMeshOverrides(paths, ctx);
    }

    /// <summary>
    /// Mesh-override variant for a fully-resolved <see cref="SubgroupCombination"/> (one valid distribution
    /// roll). Each contained <see cref="FlattenedSubgroup"/> already carries its inherited <c>Paths</c>, so
    /// the union of every position's paths is the complete asset set the NPC would receive. Used by the
    /// "Select from Config File" action once the distribution simulator has produced a compatible combination.
    /// </summary>
    public List<MeshOverride> MapCombinationMeshOverrides(SubgroupCombination combination, DestinationResolutionContext ctx)
    {
        if (combination?.ContainedSubgroups == null) return new List<MeshOverride>();
        var paths = combination.ContainedSubgroups
            .Where(sg => sg?.Paths != null)
            .SelectMany(sg => sg.Paths);
        return BuildMeshOverrides(paths, ctx);
    }

    /// <summary>Emits one <see cref="TextureOverride"/> per base/auxiliary biped slot a resolved texture
    /// destination occupies (a multi-slot armature's skin texture applies to every slot's shapes, matching
    /// the game). Mesh destinations are ignored. The head-texture case routes to the single "Head" part.</summary>
    private static void EmitTextureOverrides(ResolvedDestination resolved, Dictionary<OverrideKey, TextureOverride> result)
    {
        if (resolved.Kind == DestinationKind.Mesh) return;

        if (resolved.Kind == DestinationKind.HeadTexture)
        {
            result[new OverrideKey("Head", resolved.TextureSlot, null)] =
                new TextureOverride("Head", resolved.TextureSlot, resolved.Source, null);
            return;
        }

        foreach (int bit in EnumerateSetBits(resolved.BipedSlotsMask))
        {
            string? bodyPart = VM_CharacterViewer.BipedFlagToBodyPart(bit);
            if (bodyPart == null) continue;
            result[new OverrideKey(bodyPart, resolved.TextureSlot, resolved.ShapeName)] =
                new TextureOverride(bodyPart, resolved.TextureSlot, resolved.Source, resolved.ShapeName);
        }
    }

    /// <summary>
    /// Builds the auxiliary-armature <see cref="MeshOverride"/> list from a flat set of
    /// <see cref="FilePathReplacement"/>s. Resolves every path once; records the first-seen mesh source per
    /// biped slot and the first-seen <c>SkinTexture</c> source per (slot, texture-slot); then, for each
    /// non-base slot that has a mesh, emits a <see cref="MeshOverride"/> with the same-slot skin textures
    /// bundled on. See <see cref="MapSubgroupMeshOverrides"/> for the rationale on excluding base armatures.
    /// </summary>
    private List<MeshOverride> BuildMeshOverrides(IEnumerable<FilePathReplacement> paths, DestinationResolutionContext ctx)
    {
        // biped flag -> first-seen mesh source for that slot
        var meshBySlot = new Dictionary<int, string>();
        // biped flag -> (tex slot -> source), first-seen per (slot, texslot)
        var texBySlot = new Dictionary<int, Dictionary<int, string>>();

        foreach (var path in paths)
        {
            if (!TryResolveDestination(path, ctx, out var resolved)) continue;

            if (resolved.Kind == DestinationKind.Mesh)
            {
                foreach (int bit in EnumerateSetBits(resolved.BipedSlotsMask))
                {
                    if (!meshBySlot.ContainsKey(bit)) meshBySlot[bit] = resolved.Source;
                }
            }
            else if (resolved.Kind == DestinationKind.SkinTexture)
            {
                foreach (int bit in EnumerateSetBits(resolved.BipedSlotsMask))
                {
                    if (!texBySlot.TryGetValue(bit, out var d)) { d = new(); texBySlot[bit] = d; }
                    if (!d.ContainsKey(resolved.TextureSlot)) d[resolved.TextureSlot] = resolved.Source;
                }
            }
        }

        var result = new List<MeshOverride>();
        foreach (var (flag, meshSource) in meshBySlot)
        {
            string? key = VM_CharacterViewer.BipedFlagToBodyPart(flag);
            // Only synthesize for non-base armatures (generic "Slot{n}"); base
            // body parts are already on the rendered NPC.
            if (key == null || !key.StartsWith("Slot", StringComparison.Ordinal)) continue;

            Dictionary<int, string>? textures = null;
            if (texBySlot.TryGetValue(flag, out var tex) && tex.Count > 0) textures = tex;

            result.Add(new MeshOverride
            {
                Key = key,
                MeshPath = meshSource,
                BipedSlots = flag,
                Kind = MeshOverrideKind.Skin,
                Textures = textures,
            });
        }

        return result;
    }

    /// <summary>
    /// Resolves one destination path to its renderer meaning. Gender-gates opposite-sex destinations,
    /// handles the NPC-level <c>HeadTexture</c> case directly, and otherwise resolves the target
    /// <c>ArmorAddon</c> against each candidate root (record template, then preview NPC) to read its real
    /// biped slots and classify the tail as a skin texture, an alternate texture (recovering the target
    /// shape name), or a mesh. Returns false — and logs why — when nothing resolves, so an unrenderable
    /// path is skipped rather than mis-applied.
    /// </summary>
    private bool TryResolveDestination(FilePathReplacement path, DestinationResolutionContext ctx, out ResolvedDestination resolved)
    {
        resolved = default;
        if (path == null || string.IsNullOrWhiteSpace(path.Destination) || string.IsNullOrWhiteSpace(path.Source)) return false;
        string dest = path.Destination;

        // Gender gate: skip a gendered destination that targets the opposite sex. A gender-agnostic
        // destination (e.g. HeadTexture) names neither and always passes.
        string otherSex = ctx.Gender == Gender.Female ? "Male" : "Female";
        if (dest.Contains("." + otherSex + ".", StringComparison.OrdinalIgnoreCase)) return false;

        // Head texture — NPC-level, no armature to resolve.
        if (dest.StartsWith("HeadTexture", StringComparison.OrdinalIgnoreCase))
        {
            int? headSlot = VM_CharacterViewer.ParseTextureSlot(dest);
            if (headSlot == null) return false;
            resolved = new ResolvedDestination(DestinationKind.HeadTexture, 0, headSlot.Value, null, path.Source);
            return true;
        }

        // Armature-based — resolve the real ARMA to read its biped slots and classify the tail.
        foreach (var (root, cache) in EnumerateRoots(ctx))
        {
            if (!TryResolveArmature(dest, root, cache, out var arma, out string tail)) continue;

            var flags = arma!.BodyTemplate?.FirstPersonFlags;
            int mask = flags.HasValue ? (int)flags.Value : 0;
            if (mask == 0)
            {
                _logger.LogMessage("SubgroupTextureMapper: armature for '" + dest + "' has no biped slots — skipping.");
                return false;
            }

            if (tail.Contains("AlternateTextures", StringComparison.OrdinalIgnoreCase))
            {
                int? slot = VM_CharacterViewer.ParseTextureSlot(tail);
                string? shape = ResolveAlternateTextureShapeName(dest, root, cache);
                // Without a resolved target shape we cannot honour the AlternateTexture correctly;
                // skip rather than fall back to the body-wide channel.
                if (slot == null || string.IsNullOrWhiteSpace(shape)) return false;
                resolved = new ResolvedDestination(DestinationKind.AlternateTexture, mask, slot.Value, shape, path.Source);
                return true;
            }
            if (tail.Contains("SkinTexture", StringComparison.OrdinalIgnoreCase))
            {
                int? slot = VM_CharacterViewer.ParseTextureSlot(tail);
                if (slot == null) return false;
                resolved = new ResolvedDestination(DestinationKind.SkinTexture, mask, slot.Value, null, path.Source);
                return true;
            }
            if (tail.Contains("WorldModel", StringComparison.OrdinalIgnoreCase) && tail.Contains("File", StringComparison.OrdinalIgnoreCase))
            {
                resolved = new ResolvedDestination(DestinationKind.Mesh, mask, -1, null, path.Source);
                return true;
            }

            _logger.LogMessage("SubgroupTextureMapper: unrecognized armature destination tail '" + tail + "' for '" + dest + "' — skipping.");
            return false;
        }

        _logger.LogMessage("SubgroupTextureMapper: could not resolve armature for destination '" + dest + "' — skipping.");
        return false;
    }

    /// <summary>Yields the candidate (root record, link cache) pairs a destination is resolved against,
    /// in priority order: record-template NPC first, preview NPC second. Skips a pair whose NPC or link
    /// cache is null.</summary>
    private static IEnumerable<(INpcGetter root, ILinkCache cache)> EnumerateRoots(DestinationResolutionContext ctx)
    {
        if (ctx.TemplateNpc != null && ctx.TemplateLinkCache != null)
            yield return (ctx.TemplateNpc, ctx.TemplateLinkCache);
        if (ctx.PreviewNpc != null && ctx.PreviewLinkCache != null)
            yield return (ctx.PreviewNpc, ctx.PreviewLinkCache);
    }

    /// <summary>
    /// Resolves the <c>ArmorAddon</c> a destination targets by walking the path up to and including its
    /// <c>Armature[...]</c> array specifier against <paramref name="root"/>, and returns the remaining tail
    /// (everything after the specifier, e.g. <c>SkinTexture.Female.Diffuse.GivenPath</c>) for classification.
    /// Returns false when the path has no <c>Armature[...]</c> segment or it doesn't resolve to an armature.
    /// </summary>
    private bool TryResolveArmature(string destination, INpcGetter root, ILinkCache linkCache, out IArmorAddonGetter? arma, out string tail)
    {
        arma = null;
        tail = "";

        var segments = RecordPathParser.SplitPath(destination).ToList();
        int armIndex = segments.FindIndex(s => s.Equals("Armature", StringComparison.OrdinalIgnoreCase));
        if (armIndex < 0 || armIndex + 1 >= segments.Count || !RecordPathParser.PathIsArray(segments[armIndex + 1])) return false;

        string prefix = RecordGenerator.BuildPath(segments.GetRange(0, armIndex + 2));
        int tailStart = armIndex + 2;
        tail = tailStart < segments.Count ? RecordGenerator.BuildPath(segments.GetRange(tailStart, segments.Count - tailStart)) : "";

        var objectCache = new Dictionary<string, dynamic>(StringComparer.OrdinalIgnoreCase);
        if (_recordPathParser.GetObjectAtPath(root, root, prefix, objectCache, linkCache, true, "AssetPresenter armature preview", out dynamic obj)
            && obj is IArmorAddonGetter resolvedArma)
        {
            arma = resolvedArma;
            return true;
        }
        return false;
    }

    /// <summary>
    /// Recovers the 3D object name that a worn-armor <c>AlternateTextures</c> destination targets, by
    /// walking the destination — up to and including its <c>AlternateTextures[...]</c> array specifier —
    /// against <paramref name="root"/> and reading the selected alternate texture's <c>Name</c>. This
    /// mirrors how the patcher resolves these paths, so the preview matches the record's <c>Name</c>
    /// (e.g. "BodyShapeB") against the loaded body NIF's shape of the same name instead of guessing by
    /// geometry index (the alternate texture's <c>Index</c> field is NOT the NIF shape enumeration order).
    /// Returns null when the path has no resolvable <c>AlternateTextures[...]</c> segment or the record has
    /// no matching entry.
    /// </summary>
    private string? ResolveAlternateTextureShapeName(string destination, INpcGetter root, ILinkCache linkCache)
    {
        string? namePath = BuildAlternateTextureNamePath(destination);
        if (namePath == null) return null;

        var objectCache = new Dictionary<string, dynamic>(StringComparer.OrdinalIgnoreCase);
        if (_recordPathParser.GetObjectAtPath(root, root, namePath, objectCache, linkCache,
                true, "AssetPresenter AlternateTexture preview", out dynamic result)
            && result is string name && !string.IsNullOrWhiteSpace(name))
        {
            _logger.LogMessage("SubgroupTextureMapper: AlternateTexture '" + destination + "' → shape '" + name + "'");
            return name;
        }

        _logger.LogMessage("SubgroupTextureMapper: could not resolve AlternateTexture target for '" + destination +
            "' against " + EditorIDHandler.GetEditorIDSafely(root) + " — skipping (not applied as body texture).");
        return null;
    }

    /// <summary>
    /// Rewrites an <c>AlternateTextures</c> destination path so it resolves to the selected alternate
    /// texture's <c>Name</c>: keeps the path through the <c>AlternateTextures[...]</c> array specifier
    /// and replaces the trailing <c>NewTexture.&lt;slot&gt;.GivenPath</c> tail with <c>Name</c>.
    /// Returns null when the path contains no <c>AlternateTextures</c> segment followed by an array specifier.
    /// </summary>
    private static string? BuildAlternateTextureNamePath(string destination)
    {
        var segments = RecordPathParser.SplitPath(destination).ToList();
        int atIndex = segments.FindIndex(s => s.Equals("AlternateTextures", StringComparison.OrdinalIgnoreCase));
        if (atIndex < 0 || atIndex + 1 >= segments.Count || !RecordPathParser.PathIsArray(segments[atIndex + 1])) return null;

        var prefix = RecordGenerator.BuildPath(segments.GetRange(0, atIndex + 2));
        return prefix + ".Name";
    }

    /// <summary>Yields each individually-set bit of a biped-slot mask as its own single-bit value
    /// (e.g. <c>Body | Hands</c> → 4, 8), so each can be mapped to a body part via
    /// <see cref="VM_CharacterViewer.BipedFlagToBodyPart"/>.</summary>
    private static IEnumerable<int> EnumerateSetBits(int mask)
    {
        for (int b = 0; b < 32; b++)
        {
            int bit = 1 << b;
            if ((mask & bit) != 0) yield return bit;
        }
    }

    /// <summary>
    /// Walks the subgroup's parent chain and computes the effective allowed race set.
    /// Starts with the subgroup's own Allowed* merged set; intersects with each
    /// ancestor's non-empty Allowed* set; subtracts every level's Disallowed* set.
    /// Returns an empty set when no constraints are present at any level (caller
    /// should fall back to the Default preview NPC pair).
    /// </summary>
    public HashSet<FormKey> ResolveEffectiveRaces(VM_SubgroupPlaceHolder node, List<RaceGrouping> groupings)
    {
        if (node == null) return new HashSet<FormKey>();

        var chain = new List<VM_SubgroupPlaceHolder> { node };
        chain.AddRange(node.GetParents());

        HashSet<FormKey>? allowed = null;
        var disallowed = new HashSet<FormKey>();

        foreach (var link in chain)
        {
            var m = link.AssociatedModel;
            var linkAllowed = RaceGrouping.MergeRaceAndGroupingList(m.AllowedRaceGroupings, groupings, m.AllowedRaces);
            var linkDisallowed = RaceGrouping.MergeRaceAndGroupingList(m.DisallowedRaceGroupings, groupings, m.DisallowedRaces);

            if (linkAllowed.Count > 0)
            {
                allowed = allowed == null ? new HashSet<FormKey>(linkAllowed) : new HashSet<FormKey>(allowed.Intersect(linkAllowed));
            }
            disallowed.UnionWith(linkDisallowed);
        }

        if (allowed == null) return new HashSet<FormKey>();
        allowed.ExceptWith(disallowed);
        return allowed;
    }
}
