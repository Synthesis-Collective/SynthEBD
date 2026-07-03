using System.Collections.Concurrent;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Aspects;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Plugins.Records;

namespace SynthEBD;

/// <summary>
/// Builds and caches FormKey-to-display-name indices for the named FormKey pickers. Mutagen's own
/// pickers search the identifier cache, which only carries FormKeys and EditorIDs — showing record
/// NAMES requires enumerating winning-override records, which is what this indexer does once per
/// (link cache, scoped-type set) on a background thread, storing only lightweight
/// <see cref="RecordDisplayData"/> entries.
///
/// <para>Indices are keyed by the link-cache INSTANCE, so when the environment rebuilds (new link
/// cache object) fresh indices are built automatically; stale ones are pruned once a handful of
/// caches have accumulated. Works with any <see cref="ILinkCache"/>, including the record-template
/// cache.</para>
/// </summary>
public sealed class RecordNameIndexer
{
    private static readonly Lazy<RecordNameIndexer> _instance = new(() => new RecordNameIndexer());
    public static RecordNameIndexer Instance => _instance.Value;

    private const int MaxCachedLinkCaches = 6;

    // Keyed by link-cache instance (reference equality via the default comparer).
    private readonly ConcurrentDictionary<ILinkCache, ConcurrentDictionary<string, Task<IReadOnlyDictionary<FormKey, RecordDisplayData>>>> _indices = new();

    private RecordNameIndexer() { }

    /// <summary>
    /// Returns (building it on first request) the FormKey-to-display index covering the given
    /// scoped record types in the given link cache. Highest-priority (winning) override wins.
    /// </summary>
    public Task<IReadOnlyDictionary<FormKey, RecordDisplayData>> GetIndexAsync(ILinkCache linkCache, IEnumerable<Type> scopedTypes)
    {
        var types = scopedTypes.Where(x => x != null).Distinct().OrderBy(x => x.FullName).ToArray();
        var typeKey = string.Join("|", types.Select(x => x.FullName));

        if (_indices.Count > MaxCachedLinkCaches)
        {
            // The environment link cache is replaced on rebuild; drop indices for caches other
            // than the ones in active use rather than growing without bound.
            var stale = _indices.Keys.Where(k => !ReferenceEquals(k, linkCache)).ToList();
            foreach (var key in stale.Take(_indices.Count - 1))
            {
                _indices.TryRemove(key, out _);
            }
        }

        var perCache = _indices.GetOrAdd(linkCache, _ => new ConcurrentDictionary<string, Task<IReadOnlyDictionary<FormKey, RecordDisplayData>>>());
        return perCache.GetOrAdd(typeKey, _ => Task.Run(() => BuildIndex(linkCache, types)));
    }

    private static IReadOnlyDictionary<FormKey, RecordDisplayData> BuildIndex(ILinkCache linkCache, Type[] types)
    {
        var result = new Dictionary<FormKey, RecordDisplayData>();
        // ListedOrder ascends in priority; overwriting by FormKey leaves the winning override.
        foreach (var mod in linkCache.ListedOrder)
        {
            foreach (var type in types)
            {
                IEnumerable<IMajorRecordGetter> records;
                try
                {
                    records = mod.EnumerateMajorRecords(type);
                }
                catch (Exception)
                {
                    continue; // Unknown/unenumerable type for this game mode; nothing to index.
                }

                foreach (var record in records)
                {
                    string? name = (record as INamedGetter)?.Name?.ToString();
                    result[record.FormKey] = new RecordDisplayData(record.FormKey, name, record.EditorID);
                }
            }
        }
        return result;
    }
}
