using System;
using System.Collections.Generic;
using System.IO;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Skyrim;
using Pfim;

namespace SynthEBD;

/// <summary>
/// BGRA32 pixel payload decoded from a DDS on disk. Data length == Width*Height*4.
/// Treated as immutable by the cache — callers that need to mutate (face/hair tint
/// blending) must clone the array before writing, or cache corruption follows.
/// </summary>
public readonly record struct DdsPixels(byte[] Data, int Width, int Height);

/// <summary>
/// Process-wide cache of expensive read-only inputs to the 3D preview pipeline.
///
/// Each VM_CharacterViewer is short-lived (e.g. the BodySlide menu disposes the
/// previous viewer on every preset switch to release its GL context), which
/// throws away the per-instance NIF parse cache, resolved-mesh-path data, and
/// decoded DDS pixel buffers. Hoisting all three into a singleton lets a
/// freshly-constructed viewer skip the link-cache traversal, NIF re-parse, and
/// Pfim DDS re-decode when it's loading the same preview NPC the previous
/// viewer just released.
///
/// Three layers:
///   * <see cref="MeshBuilder"/> — owns the parsed-NIF LRU keyed on
///     (nifPath, mtime, skeletonPath, mtime). Shared across all viewers.
///   * <see cref="GetOrResolveMeshPaths"/> — caches NpcMeshResolver output keyed
///     on (LinkCache identity, NPC FormKey). The head-override path is applied
///     by the caller after retrieval since it's a per-load decoration, not part
///     of the resolved record chain.
///   * <see cref="GetOrLoadDdsPixels"/> — LRU cache of BGRA32 pixel arrays keyed
///     on game-relative texture path. GL texture handles are still created per
///     viewer (context-specific), but the Pfim decode + Rgb24→Rgba32 conversion
///     happens once. Preset switching is the hot path — a typical NPC pulls
///     ~20 textures and decoding dominated the ~3.6s latency per switch.
///
/// Invalidation: a new LinkCache reference (env reload) drops the path cache
/// automatically on next access. The mesh LRU self-invalidates via file mtimes.
/// The pixel cache doesn't track mtimes — call <see cref="Clear"/> from an
/// explicit env-refresh hook if textures may have been edited on disk between
/// sessions. In normal viewer use the files don't change during a run.
/// </summary>
public class CharacterPreviewCache
{
    private readonly Logger _logger;
    private readonly CharacterViewerLogGate _logGate;
    private readonly NpcMeshResolver _npcMeshResolver;
    private readonly GameAssetResolver _assetResolver;

    public NifMeshBuilder MeshBuilder { get; }

    private const int MeshPathsCacheMaxEntries = 32;
    private readonly Dictionary<FormKey, NpcMeshResolver.NpcMeshPaths?> _meshPathsCache = new();
    private readonly LinkedList<FormKey> _meshPathsLru = new();
    private readonly object _meshPathsLock = new();
    private object? _meshPathsLinkCacheToken;

    // Sized for ~4 full NPCs' worth of unique diffuse/normal/specular/env maps
    // (head + body + hands + feet + hair ≈ 30 textures each). LRU eviction is
    // enough since the preset-switch hot path reloads the same NPC's textures.
    private const int PixelCacheMaxEntries = 128;
    private readonly Dictionary<string, DdsPixels?> _pixelCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly LinkedList<string> _pixelLru = new();
    private readonly object _pixelLock = new();

    public CharacterPreviewCache(
        Logger logger,
        CharacterViewerLogGate logGate,
        NpcMeshResolver npcMeshResolver,
        GameAssetResolver assetResolver)
    {
        _logger = logger;
        _logGate = logGate;
        _npcMeshResolver = npcMeshResolver;
        _assetResolver = assetResolver;
        MeshBuilder = new NifMeshBuilder(logger, logGate);
    }

    /// <summary>
    /// Returns cached NpcMeshPaths for this NPC under the given LinkCache, or
    /// resolves and caches if absent. A null result (NPC unresolvable) is also
    /// cached so repeat lookups don't redo the failing traversal.
    /// </summary>
    public NpcMeshResolver.NpcMeshPaths? GetOrResolveMeshPaths(FormKey npcFormKey, ILinkCache linkCache)
    {
        lock (_meshPathsLock)
        {
            // Drop everything if the active LinkCache instance has been replaced
            // (e.g. env reload). Reference identity is sufficient — a new
            // environment build always produces a new ILinkCache instance.
            if (!ReferenceEquals(_meshPathsLinkCacheToken, linkCache))
            {
                _meshPathsCache.Clear();
                _meshPathsLru.Clear();
                _meshPathsLinkCacheToken = linkCache;
            }
            else if (_meshPathsCache.TryGetValue(npcFormKey, out var cached))
            {
                // LRU touch
                _meshPathsLru.Remove(npcFormKey);
                _meshPathsLru.AddFirst(npcFormKey);
                if (_logGate != null && _logGate.Verbose)
                    _logger?.LogMessage("CharacterPreviewCache: NpcMeshPaths cache hit for " + npcFormKey);
                return cached;
            }
        }

        var resolved = _npcMeshResolver.ResolveMeshPaths(npcFormKey, linkCache);

        lock (_meshPathsLock)
        {
            // Re-check the token in case another thread invalidated mid-resolve.
            if (!ReferenceEquals(_meshPathsLinkCacheToken, linkCache)) return resolved;

            _meshPathsCache[npcFormKey] = resolved;
            _meshPathsLru.AddFirst(npcFormKey);
            while (_meshPathsLru.Count > MeshPathsCacheMaxEntries)
            {
                var oldest = _meshPathsLru.Last!.Value;
                _meshPathsLru.RemoveLast();
                _meshPathsCache.Remove(oldest);
            }
        }

        return resolved;
    }

    /// <summary>
    /// Returns BGRA32 pixel data for the given game-relative DDS path, decoding
    /// through Pfim on cache miss and caching the result for subsequent viewers.
    /// A null result (unresolvable path / unsupported format / decode failure)
    /// is also cached so repeat lookups don't redo the failing work.
    ///
    /// The returned <see cref="DdsPixels.Data"/> array is treated as immutable
    /// by the cache — callers that blend tints on the CPU must clone before
    /// mutating. <see cref="GlTextureManager"/>'s tint paths do exactly that.
    /// </summary>
    public DdsPixels? GetOrLoadDdsPixels(string relativeGamePath)
    {
        if (string.IsNullOrWhiteSpace(relativeGamePath)) return null;

        lock (_pixelLock)
        {
            if (_pixelCache.TryGetValue(relativeGamePath, out var cached))
            {
                _pixelLru.Remove(relativeGamePath);
                _pixelLru.AddFirst(relativeGamePath);
                if (_logGate != null && _logGate.Verbose)
                    _logger?.LogMessage("CharacterPreviewCache: DdsPixels cache hit for '" + relativeGamePath + "'");
                return cached;
            }
        }

        var decoded = DecodeDds(relativeGamePath);

        lock (_pixelLock)
        {
            // Re-check in case a parallel caller already populated the entry.
            if (_pixelCache.TryGetValue(relativeGamePath, out var racedCached))
            {
                _pixelLru.Remove(relativeGamePath);
                _pixelLru.AddFirst(relativeGamePath);
                return racedCached;
            }

            _pixelCache[relativeGamePath] = decoded;
            _pixelLru.AddFirst(relativeGamePath);
            while (_pixelLru.Count > PixelCacheMaxEntries)
            {
                var oldest = _pixelLru.Last!.Value;
                _pixelLru.RemoveLast();
                _pixelCache.Remove(oldest);
            }
        }

        return decoded;
    }

    /// <summary>
    /// Decodes a DDS via Pfim into BGRA32. Handles the two formats Pfim emits
    /// for Skyrim assets — Rgba32 (blittable) and Rgb24 (needs padding to 4-byte
    /// stride). Matches the behavior previously in GlTextureManager.LoadDdsPixels
    /// so swapping callers from the direct Pfim path to this cached path yields
    /// pixel-identical output.
    /// </summary>
    private DdsPixels? DecodeDds(string relativeGamePath)
    {
        string? resolved = _assetResolver.ResolveAssetPath(relativeGamePath);
        if (resolved == null || !File.Exists(resolved)) return null;

        try
        {
            using var image = Pfimage.FromFile(resolved);
            int width = image.Width;
            int height = image.Height;
            int rowBytes = width * 4;
            int expectedSize = height * rowBytes;

            switch (image.Format)
            {
                case Pfim.ImageFormat.Rgba32:
                {
                    byte[] data = new byte[expectedSize];
                    if (image.Stride == rowBytes)
                        Buffer.BlockCopy(image.Data, 0, data, 0, expectedSize);
                    else
                        for (int y = 0; y < height; y++)
                            Buffer.BlockCopy(image.Data, y * image.Stride, data, y * rowBytes, rowBytes);
                    return new DdsPixels(data, width, height);
                }
                case Pfim.ImageFormat.Rgb24:
                {
                    byte[] data = new byte[expectedSize];
                    int srcStride = image.Stride;
                    for (int y = 0; y < height; y++)
                        for (int x = 0; x < width; x++)
                        {
                            int srcIdx = y * srcStride + x * 3;
                            int dstIdx = (y * width + x) * 4;
                            data[dstIdx] = image.Data[srcIdx];
                            data[dstIdx + 1] = image.Data[srcIdx + 1];
                            data[dstIdx + 2] = image.Data[srcIdx + 2];
                            data[dstIdx + 3] = 255;
                        }
                    return new DdsPixels(data, width, height);
                }
                case Pfim.ImageFormat.Rgb8:
                {
                    // 8-bit grayscale (e.g. Skyrim _S specular maps): one intensity
                    // byte per pixel, broadcast to B/G/R with full alpha.
                    byte[] data = new byte[expectedSize];
                    int srcStride = image.Stride;
                    for (int y = 0; y < height; y++)
                        for (int x = 0; x < width; x++)
                        {
                            byte v = image.Data[y * srcStride + x];
                            int dstIdx = (y * width + x) * 4;
                            data[dstIdx] = v;
                            data[dstIdx + 1] = v;
                            data[dstIdx + 2] = v;
                            data[dstIdx + 3] = 255;
                        }
                    return new DdsPixels(data, width, height);
                }
                default:
                    if (_logGate != null && _logGate.Verbose)
                        _logger?.LogMessage("CharacterPreviewCache: Unsupported DDS format " +
                            image.Format + " for '" + relativeGamePath + "'");
                    return null;
            }
        }
        catch (Exception ex)
        {
            if (_logGate != null && _logGate.Verbose)
                _logger?.LogMessage("CharacterPreviewCache: Failed to decode '" +
                    relativeGamePath + "': " + ex.Message);
            return null;
        }
    }

    /// <summary>
    /// Drops every cached resolved path, parsed NIF, and decoded pixel buffer.
    /// Call when the patcher environment is rebuilt so subsequent viewer loads
    /// re-resolve from the new link cache and re-read NIFs / textures from disk.
    /// </summary>
    public void Clear()
    {
        lock (_meshPathsLock)
        {
            _meshPathsCache.Clear();
            _meshPathsLru.Clear();
            _meshPathsLinkCacheToken = null;
        }
        lock (_pixelLock)
        {
            _pixelCache.Clear();
            _pixelLru.Clear();
        }
        MeshBuilder.ClearCache();
    }
}
