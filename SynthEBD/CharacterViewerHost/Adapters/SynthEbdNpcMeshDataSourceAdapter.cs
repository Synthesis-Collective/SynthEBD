using System;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Skyrim;

namespace SynthEBD;

/// <summary>
/// Adapts SynthEBD's Mutagen-aware <see cref="NpcMeshResolver"/> to the
/// rendering tier's neutral <see cref="INpcMeshDataSource"/>. The
/// <see cref="NpcIdentity.CacheKey"/> carries a string-encoded
/// <see cref="FormKey"/>; the adapter parses it back, looks up the current
/// LinkCache from the host environment, converts the resolver's
/// <see cref="NpcMeshResolver.NpcMeshPaths"/> into the neutral
/// <see cref="ResolvedNpcMeshPaths"/> POCO, and adds NPC-record-derived
/// values (weight, height, hair color, eye head-part shape names) so the
/// rendering tier's LoadAsync entry point doesn't need to touch Mutagen.
///
/// <see cref="CurrentInvalidationToken"/> exposes the active LinkCache as the
/// reference-equality token — when Mutagen builds a new environment the
/// CharacterPreviewCache automatically drops every entry on the next access.
/// </summary>
public sealed class SynthEbdNpcMeshDataSourceAdapter : INpcMeshDataSource
{
    private readonly NpcMeshResolver _resolver;
    private readonly IEnvironmentStateProvider _env;

    public SynthEbdNpcMeshDataSourceAdapter(NpcMeshResolver resolver, IEnvironmentStateProvider env)
    {
        _resolver = resolver;
        _env = env;
    }

    public ResolvedNpcMeshPaths? Resolve(NpcIdentity identity)
    {
        if (!FormKey.TryFactory(identity.CacheKey, out var formKey)) return null;
        var linkCache = _env.LinkCache;
        var paths = _resolver.ResolveMeshPaths(formKey, linkCache);
        if (paths == null) return null;

        return WithNpcRecord(Convert(paths), formKey, linkCache);
    }

    public object? CurrentInvalidationToken => _env.LinkCache;

    /// <summary>Field-by-field copy from the resolver's still-Mutagen-coupled
    /// inner POCO into the neutral one. Once Phase C moves NpcMeshResolver into
    /// CharacterViewer.Skyrim this conversion folds into the resolver itself.</summary>
    internal static ResolvedNpcMeshPaths Convert(NpcMeshResolver.NpcMeshPaths src) =>
        new()
        {
            BodyMeshPath = src.BodyMeshPath,
            HandsMeshPath = src.HandsMeshPath,
            FeetMeshPath = src.FeetMeshPath,
            HeadMeshPath = src.HeadMeshPath,
            HairMeshPath = src.HairMeshPath,
            TailMeshPath = src.TailMeshPath,
            // Rendering-tier Sex enum matches SynthEBD's Gender ordinal layout.
            Sex = (Sex)(int)src.Gender,
            SkeletonPath = src.SkeletonPath,
            ResolutionChains = src.ResolutionChains,
            TxstTextures = src.TxstTextures,
            FaceTintPath = src.FaceTintPath,
            TextureLightingColor = src.TextureLightingColor,
        };

    /// <summary>Inflates a converted POCO with NPC-record values (weight, height,
    /// hair color RGB). Failure to resolve the record is non-fatal — the defaults
    /// baked into <see cref="ResolvedNpcMeshPaths"/> (weight 50, height 1.0, hair
    /// color null) take over via the unmodified <paramref name="basis"/>. Returns
    /// a new instance because the POCO's fields are init-only.</summary>
    internal static ResolvedNpcMeshPaths WithNpcRecord(ResolvedNpcMeshPaths basis,
        FormKey npcFormKey, ILinkCache linkCache)
    {
        if (!linkCache.TryResolve<INpcGetter>(npcFormKey, out var npcGetter)) return basis;

        int weight = Math.Clamp((int)npcGetter.Weight, 0, 100);

        // NPC.Height is a uniform scale multiplier (1.0 default). Guard against
        // zero/negative values from malformed records to avoid a collapsed render.
        float recordHeight = npcGetter.Height;
        float baseHeight = (float.IsFinite(recordHeight) && recordHeight > 0f) ? recordHeight : 1f;

        (float R, float G, float B)? hairRgb = null;
        if (!npcGetter.HairColor.IsNull)
        {
            var hclr = npcGetter.HairColor.TryResolve(linkCache);
            if (hclr != null)
            {
                var c = hclr.Color;
                hairRgb = (c.R / 255f, c.G / 255f, c.B / 255f);
            }
        }

        return new ResolvedNpcMeshPaths
        {
            BodyMeshPath = basis.BodyMeshPath,
            HandsMeshPath = basis.HandsMeshPath,
            FeetMeshPath = basis.FeetMeshPath,
            HeadMeshPath = basis.HeadMeshPath,
            HairMeshPath = basis.HairMeshPath,
            TailMeshPath = basis.TailMeshPath,
            Sex = basis.Sex,
            SkeletonPath = basis.SkeletonPath,
            ResolutionChains = basis.ResolutionChains,
            TxstTextures = basis.TxstTextures,
            FaceTintPath = basis.FaceTintPath,
            TextureLightingColor = basis.TextureLightingColor,
            NpcWeight = weight,
            NpcBaseHeight = baseHeight,
            HairColorRgb = hairRgb,
            // Authoritative IsEye input: EditorIDs of the record's Eyes-typed
            // head parts (+ Extra Parts). ApplyHeadPartsAsync unions in the
            // ASSIGNED eyes part separately when a preview replaces them.
            EyeShapeNames = HeadPartShapeNames.CollectFromNpcRecord(
                npcGetter, linkCache, HeadPart.TypeEnum.Eyes),
        };
    }
}
