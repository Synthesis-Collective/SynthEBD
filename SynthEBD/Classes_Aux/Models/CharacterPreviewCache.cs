using System.Collections.Generic;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Skyrim;

namespace SynthEBD;

/// <summary>
/// Process-wide cache of expensive read-only inputs to the 3D preview pipeline.
///
/// Each VM_CharacterViewer is short-lived (e.g. the BodySlide menu disposes the
/// previous viewer on every preset switch to release its GL context), which
/// throws away the per-instance NIF parse cache and resolved-mesh-path data.
/// Hoisting both into a singleton lets a freshly-constructed viewer skip the
/// link-cache traversal and NIF re-parse when it's loading the same preview NPC
/// the previous viewer just released.
///
/// Two layers:
///   * <see cref="MeshBuilder"/> — owns the parsed-NIF LRU keyed on
///     (nifPath, mtime, skeletonPath, mtime). Shared across all viewers.
///   * <see cref="GetOrResolveMeshPaths"/> — caches NpcMeshResolver output keyed
///     on (LinkCache identity, NPC FormKey). The head-override path is applied
///     by the caller after retrieval since it's a per-load decoration, not part
///     of the resolved record chain.
///
/// Invalidation: a new LinkCache reference (env reload) drops the path cache
/// automatically on next access. The mesh LRU self-invalidates via file mtimes.
/// Call <see cref="Clear"/> from any explicit env-refresh hook for belt-and-suspenders.
/// </summary>
public class CharacterPreviewCache
{
    private readonly Logger _logger;
    private readonly CharacterViewerLogGate _logGate;
    private readonly NpcMeshResolver _npcMeshResolver;

    public NifMeshBuilder MeshBuilder { get; }

    private const int MeshPathsCacheMaxEntries = 32;
    private readonly Dictionary<FormKey, NpcMeshResolver.NpcMeshPaths?> _meshPathsCache = new();
    private readonly LinkedList<FormKey> _meshPathsLru = new();
    private readonly object _meshPathsLock = new();
    private object? _meshPathsLinkCacheToken;

    public CharacterPreviewCache(Logger logger, CharacterViewerLogGate logGate, NpcMeshResolver npcMeshResolver)
    {
        _logger = logger;
        _logGate = logGate;
        _npcMeshResolver = npcMeshResolver;
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
    /// Drops every cached resolved path and parsed NIF. Call when the patcher
    /// environment is rebuilt so subsequent viewer loads re-resolve from the
    /// new link cache and re-read NIFs from disk.
    /// </summary>
    public void Clear()
    {
        lock (_meshPathsLock)
        {
            _meshPathsCache.Clear();
            _meshPathsLru.Clear();
            _meshPathsLinkCacheToken = null;
        }
        MeshBuilder.ClearCache();
    }
}
