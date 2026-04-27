using Mutagen.Bethesda;
using System.IO;
using System.Text.Json;
using Mutagen.Bethesda.Archives;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;
using Mutagen.Bethesda.Plugins.Order;
using ReactiveUI;
using Noggog;
using Noggog.WPF;
using System.Collections.Concurrent;

namespace SynthEBD;

public class PathedArchiveReader
{
    public IArchiveReader? Reader { get; set; }
    public Noggog.FilePath FilePath { get; set; }
}

public class BSAHandler : ViewModel
{
    private readonly IEnvironmentStateProvider _environmentProvider;
    private readonly Logger _logger;
    private HashSet<ModKey> _enabledMods = new();
    private HashSet<string> _enabledModNames = new();

    public ConcurrentDictionary<ModKey, HashSet<IArchiveReader>> OpenReaders = new();
    
    // NEW: The core cache mechanism
    private readonly ConcurrentDictionary<IArchiveReader, Dictionary<string, IArchiveFile>> _archiveFileCache = new();

    // ─── BSA Disk Index Cache ────────────────────────────────────────────────
    //
    // Caches the list of file paths inside each BSA to disk so that on
    // subsequent runs we can answer "does this BSA contain path X?" without
    // opening the BSA at all. The actual IArchiveReader is only created
    // lazily when file extraction is requested.
    //
    // _pathOnlyIndex:      BSA absolute path → set of internal file paths (strings)
    // _deferredBsaPaths:   ModKey → list of BSA paths that are indexed but not yet opened
    // _readersByBsaPath:   BSA absolute path → opened IArchiveReader (populated lazily)
    // _allBsaPathsForMod:  ModKey → all BSA paths (both opened and deferred)

    private readonly ConcurrentDictionary<string, HashSet<string>> _pathOnlyIndex
        = new(StringComparer.OrdinalIgnoreCase);
    
    private readonly ConcurrentDictionary<ModKey, List<string>> _deferredBsaPaths = new();
    
    private readonly ConcurrentDictionary<string, IArchiveReader> _readersByBsaPath
        = new(StringComparer.OrdinalIgnoreCase);
    
    private readonly ConcurrentDictionary<ModKey, List<string>> _allBsaPathsForMod = new();

    private bool _diskCacheLoaded = false;
    private bool _diskCacheDirty = false;

    public BSAHandler(IEnvironmentStateProvider environmentProvider, Logger logger)
    {
        _environmentProvider = environmentProvider;
        _logger = logger;
        _environmentProvider.WhenAnyValue(x => x.LoadOrder)
            .Subscribe(x =>
            {
                _enabledMods = x.ListedOrder.Where(x => x.Enabled).Select(y => y.ModKey).ToHashSet();
                _enabledModNames = _enabledMods.Select(x => x.FileName.String).ToHashSet();
            }).DisposeWith(this);
    }

    // Helper method to populate the cache immediately upon opening a reader
    private void CacheReaderFiles(IArchiveReader reader)
    {
        if (!_archiveFileCache.ContainsKey(reader))
        {
            var fileDict = new Dictionary<string, IArchiveFile>(StringComparer.OrdinalIgnoreCase);
            foreach (var file in reader.Files)
            {
                // TryAdd ignores duplicates if a malformed BSA has overlapping internal paths
                fileDict.TryAdd(file.Path, file); 
            }
            _archiveFileCache.TryAdd(reader, fileDict);
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  BSA DISK INDEX CACHE
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Serializable representation of the BSA file index cache.
    /// Stored as JSON on disk between patcher runs.
    /// </summary>
    private class BsaIndexCacheData
    {
        public int Version { get; set; } = 1;
        public List<BsaIndexEntry> Entries { get; set; } = new();
    }

    private class BsaIndexEntry
    {
        public string BsaPath { get; set; }
        public long LastWriteTimeTicks { get; set; }
        public long FileSize { get; set; }
        public string[] FilePaths { get; set; }
    }

    /// <summary>
    /// Loads the BSA file index cache from disk. Call this early in the patcher
    /// run (before any BSA operations) to enable deferred reader opening.
    ///
    /// For each cached BSA whose file on disk still has the same last-write-time
    /// and size, the file listing is loaded into <see cref="_pathOnlyIndex"/>
    /// without opening the actual BSA archive. The reader will only be created
    /// lazily if file extraction is requested.
    /// </summary>
    public void LoadBsaIndexCache(string cachePath)
    {
        if (string.IsNullOrEmpty(cachePath) || !File.Exists(cachePath))
        {
            _diskCacheLoaded = true;
            return;
        }

        try
        {
            string json = File.ReadAllText(cachePath);
            var cacheData = JsonSerializer.Deserialize<BsaIndexCacheData>(json);
            if (cacheData == null || cacheData.Version != 1)
            {
                _diskCacheLoaded = true;
                return;
            }

            foreach (var entry in cacheData.Entries)
            {
                if (string.IsNullOrEmpty(entry.BsaPath) || entry.FilePaths == null)
                    continue;

                // Validate that the BSA file still exists and hasn't changed
                if (!File.Exists(entry.BsaPath))
                    continue;

                try
                {
                    var fileInfo = new FileInfo(entry.BsaPath);
                    if (fileInfo.LastWriteTimeUtc.Ticks != entry.LastWriteTimeTicks ||
                        fileInfo.Length != entry.FileSize)
                    {
                        // BSA has changed since cache was written — skip this entry
                        continue;
                    }
                }
                catch
                {
                    continue;
                }

                // Populate the path-only index from the cache
                var pathSet = new HashSet<string>(entry.FilePaths, StringComparer.OrdinalIgnoreCase);
                _pathOnlyIndex[entry.BsaPath] = pathSet;
            }

            _logger.LogMessage("BSA index cache loaded: " + _pathOnlyIndex.Count +
                " BSA(s) indexed from disk cache.");
        }
        catch (Exception ex)
        {
            _logger.LogMessage("Warning: Could not load BSA index cache: " + ex.Message);
        }

        _diskCacheLoaded = true;
    }

    /// <summary>
    /// Saves the BSA file index cache to disk. Call this after all BSAs have
    /// been opened/indexed during the patcher run. Only writes if the cache
    /// has been modified (new BSAs opened that weren't in the disk cache).
    /// </summary>
    public void SaveBsaIndexCache(string cachePath)
    {
        if (string.IsNullOrEmpty(cachePath) || !_diskCacheDirty)
            return;

        try
        {
            var cacheData = new BsaIndexCacheData();

            foreach (var kvp in _pathOnlyIndex)
            {
                string bsaPath = kvp.Key;
                try
                {
                    var fileInfo = new FileInfo(bsaPath);
                    if (!fileInfo.Exists) continue;

                    cacheData.Entries.Add(new BsaIndexEntry
                    {
                        BsaPath = bsaPath,
                        LastWriteTimeTicks = fileInfo.LastWriteTimeUtc.Ticks,
                        FileSize = fileInfo.Length,
                        FilePaths = kvp.Value.ToArray()
                    });
                }
                catch
                {
                    // Skip entries we can't stat
                }
            }

            string dir = Path.GetDirectoryName(cachePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            string json = JsonSerializer.Serialize(cacheData, new JsonSerializerOptions { WriteIndented = false });
            File.WriteAllText(cachePath, json);

            _logger.LogMessage("BSA index cache saved: " + cacheData.Entries.Count + " BSA(s).");
        }
        catch (Exception ex)
        {
            _logger.LogMessage("Warning: Could not save BSA index cache: " + ex.Message);
        }
    }

    /// <summary>
    /// Registers a BSA in the path-only index after opening it (for persistence
    /// to the disk cache on the next save). Called by CacheReaderFiles paths
    /// that actually opened a reader.
    /// </summary>
    private void IndexReaderPaths(string bsaAbsPath, IArchiveReader reader)
    {
        if (_pathOnlyIndex.ContainsKey(bsaAbsPath))
            return;

        var pathSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (_archiveFileCache.TryGetValue(reader, out var fileCache))
        {
            foreach (var key in fileCache.Keys)
                pathSet.Add(key);
        }
        else
        {
            foreach (var file in reader.Files)
                pathSet.Add(file.Path);
        }

        _pathOnlyIndex[bsaAbsPath] = pathSet;
        _readersByBsaPath[bsaAbsPath] = reader;
        _diskCacheDirty = true;
    }

    /// <summary>
    /// Lazily opens a BSA reader for a deferred (cache-indexed) BSA when
    /// file extraction is actually needed. Opens the reader, builds the
    /// IArchiveFile cache, and returns the reader.
    /// </summary>
    private IArchiveReader LazyOpenBsaReader(string bsaAbsPath)
    {
        if (_readersByBsaPath.TryGetValue(bsaAbsPath, out var existing))
            return existing;

        try
        {
            var reader = Archive.CreateReader(
                _environmentProvider.SkyrimVersion.ToGameRelease(),
                bsaAbsPath);

            if (reader != null)
            {
                CacheReaderFiles(reader);
                _readersByBsaPath[bsaAbsPath] = reader;
                return reader;
            }
        }
        catch
        {
            _logger.LogError("Unable to lazily open archive reader for BSA: " + bsaAbsPath);
        }

        return null;
    }

    public bool ReferencedPathExists(string expectedFilePath, out bool archiveExists, out string modName)
    {
        // ... (Unchanged logic) ...
        archiveExists = false;
        modName = "";

        var splitPath = expectedFilePath.Split(Path.DirectorySeparatorChar);
        if (splitPath.Length < 2)
        {
            return false;
        }

        string modKeyStr = splitPath[0].Trim();
        if (!ModKey.TryFromNameAndExtension(modKeyStr, out ModKey modKey))
        {
            return false;
        }
        else
        {
            modName = modKeyStr;
        }

        if (!_enabledModNames.Contains(modKeyStr, StringComparer.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!TryOpenCorrespondingArchiveReaders(modKey, out var archiveReaders))
        {
            return false;
        }
        else
        {
            archiveExists = true;
        }

        var subPath = Path.Join(splitPath.ToList().GetRange(1, splitPath.Length - 1).ToArray());

        return ReadersOrDeferredHaveFile(subPath, modKey, archiveReaders, out _);
    }

    public bool ReferencedPathExists(string expectedFilePath, IEnumerable<ModKey> candidateMods, out bool archiveExists, out string modName)
    {
        // ... (Unchanged logic) ...
        archiveExists = false;
        modName = "";

        foreach (var candidateMod in candidateMods)
        {
            if (!_enabledMods.Contains(candidateMod))
            {
                continue;
            }

            if (!TryOpenCorrespondingArchiveReaders(candidateMod, out var archiveReaders))
            {
                continue;
            }
            else
            {
                archiveExists = true;
            }

            if (ReadersOrDeferredHaveFile(expectedFilePath, candidateMod, archiveReaders, out _))
            {
                return true;
            }
        }
        return false;
    }

    public bool TryOpenCorrespondingArchiveReaders(ModKey modKey, out HashSet<IArchiveReader> archiveReaders)
    {
        archiveReaders = new HashSet<IArchiveReader>();
        if (OpenReaders.TryGetValue(modKey, out var cachedReaders))
        {
            archiveReaders = cachedReaders;
            return true;
        }

        // Track whether we have at least one indexed BSA (real or deferred)
        bool hasAnyBsa = false;
        var deferredPaths = new List<string>();
        var modBsaPaths = new List<string>();
        
        foreach (var bsaFile in Archive.GetApplicableArchivePaths(_environmentProvider.SkyrimVersion.ToGameRelease(), _environmentProvider.DataFolderPath, modKey))
        {
            string bsaAbsPath = bsaFile.Path.ToString();
            modBsaPaths.Add(bsaAbsPath);

            // Check if this BSA is already indexed from the disk cache
            if (_pathOnlyIndex.ContainsKey(bsaAbsPath) && !_readersByBsaPath.ContainsKey(bsaAbsPath))
            {
                // Disk cache hit — defer opening the reader until extraction is needed
                deferredPaths.Add(bsaAbsPath);
                hasAnyBsa = true;
                continue;
            }

            // Already opened via lazy path
            if (_readersByBsaPath.TryGetValue(bsaAbsPath, out var existingReader))
            {
                archiveReaders.Add(existingReader);
                hasAnyBsa = true;
                continue;
            }

            // Cache miss — open the reader now
            try
            {
                var reader = Archive.CreateReader(_environmentProvider.SkyrimVersion.ToGameRelease(), bsaFile);
                if (reader != null)
                {
                    CacheReaderFiles(reader);
                    IndexReaderPaths(bsaAbsPath, reader);
                    archiveReaders.Add(reader);
                    hasAnyBsa = true;
                }
            }
            catch
            {
                _logger.LogError("Unable to open archive reader to BSA file " + bsaFile.Path);
            }
        }

        // Store deferred paths for this mod
        if (deferredPaths.Count > 0)
        {
            _deferredBsaPaths[modKey] = deferredPaths;
        }

        // Store all BSA paths for this mod (for EnsureAllArchivesIndexed)
        if (modBsaPaths.Count > 0)
        {
            _allBsaPathsForMod[modKey] = modBsaPaths;
        }
        
        if (hasAnyBsa && !OpenReaders.ContainsKey(modKey))
        {
            OpenReaders.TryAdd(modKey, archiveReaders);
            return true;
        }

        return false;
    }

    public List<PathedArchiveReader> OpenBSAArchiveReaders(string currentDataDir, ModKey currentPlugin)
    {
        if (currentPlugin == null || currentPlugin.IsNull) { return new List<PathedArchiveReader>(); }
        if (!Directory.Exists(currentDataDir))
        {
            Console.WriteLine("Warning: Tried to search for BSAs in folder {0} but the folder does not exist.", currentDataDir);
            return new List<PathedArchiveReader>();
        }

        var readers = new List<PathedArchiveReader>();

        foreach (var bsaFile in Archive.GetApplicableArchivePaths(_environmentProvider.SkyrimVersion.ToGameRelease(), currentDataDir, currentPlugin))
        {
            try
            {
                var bsaReader = Archive.CreateReader(_environmentProvider.SkyrimVersion.ToGameRelease(), bsaFile);
                if (bsaReader != null)
                {
                    CacheReaderFiles(bsaReader); // NEW: Cache the files immediately
                    IndexReaderPaths(bsaFile.Path.ToString(), bsaReader);
                    readers.Add(new PathedArchiveReader() { Reader = bsaReader, FilePath = bsaFile });
                }
            }
            catch
            {
                _logger.LogError("Could not open archive " + bsaFile.Path);
            }
        }
        return readers;
    }

    public bool TryExtractFileFromBSA(IArchiveFile file, string destPath)
    {
        // ... (Includes the leak fixes applied previously) ...
        string? dirPath = Path.GetDirectoryName(destPath);
    
        if (string.IsNullOrEmpty(dirPath))
        {
            _logger.LogError("Could not determine the output directory for " + destPath);
            return false;
        }

        if (!Directory.Exists(dirPath))
        {
            try
            {
                Directory.CreateDirectory(dirPath);
            }
            catch (Exception ex)
            {
                _logger.LogError($"Could not create directory at {dirPath}. Error: {ex.Message}");
                return false; 
            }
        }

        try
        {
            using var sourceStream = file.AsStream();
            using var fileStream = File.Create(destPath);
            sourceStream.CopyTo(fileStream);
        }
        catch (Exception ex)
        {
            _logger.LogError($"Could not extract file from BSA: {file.Path} to {destPath}. Error: {ex.Message}");
            return false;
        }
    
        return File.Exists(destPath);
    }

    public bool TryGetFile(string subpath, IArchiveReader bsaReader, out IArchiveFile file)
    {
        file = null;
        if (bsaReader == null) { return false; }

        // NEW: Check the dictionary cache instead of iterating IEnumerable
        if (_archiveFileCache.TryGetValue(bsaReader, out var fileCache))
        {
            return fileCache.TryGetValue(subpath, out file);
        }

        // Fallback safety net (if a reader bypassed CacheReaderFiles somehow)
        var files = bsaReader.Files.Where(candidate => candidate.Path.Equals(subpath, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (files.Any())
        {
            file = files.First();
            return true;
        }

        return false;
    }

    /// <summary>
    /// Locates <paramref name="subpath"/> in the specific BSA at
    /// <paramref name="bsaAbsPath"/> rather than broadcasting across every
    /// indexed archive. Used by hosts that have already chosen a particular
    /// BSA via scoped resolution and need extraction to honor that choice
    /// (otherwise broadcast extraction can pick a same-named file from a
    /// different archive — e.g. vanilla FaceGen leaking into a mod-scoped
    /// render). Lazily opens deferred BSAs the first time they're touched.
    /// </summary>
    public bool TryFindFileInArchive(string bsaAbsPath, string subpath, out IArchiveFile archiveFile)
    {
        archiveFile = null;
        if (string.IsNullOrEmpty(bsaAbsPath)) return false;

        if (_readersByBsaPath.TryGetValue(bsaAbsPath, out var openReader))
        {
            return TryGetFile(subpath, openReader, out archiveFile);
        }

        // Deferred BSA — lazily open and try again.
        if (_pathOnlyIndex.ContainsKey(bsaAbsPath))
        {
            return TryGetFileFromDeferredBsa(subpath, bsaAbsPath, out archiveFile);
        }

        return false;
    }

    /// <summary>
    /// Checks whether a file exists in a deferred (disk-cached) BSA and, if so,
    /// lazily opens the reader and returns the IArchiveFile for extraction.
    /// </summary>
    private bool TryGetFileFromDeferredBsa(string subpath, string bsaAbsPath, out IArchiveFile file)
    {
        file = null;

        // First check the path-only index for existence (cheap string lookup)
        if (!_pathOnlyIndex.TryGetValue(bsaAbsPath, out var pathSet) ||
            !pathSet.Contains(subpath))
        {
            return false;
        }

        // File exists in this BSA — lazily open the reader now
        var reader = LazyOpenBsaReader(bsaAbsPath);
        if (reader == null) return false;

        return TryGetFile(subpath, reader, out file);
    }

    public bool ReadersHaveFile(string subpath, HashSet<IArchiveReader> bsaReaders, out IArchiveFile archiveFile)
    {
        // Check opened readers first
        foreach (var reader in bsaReaders)
        {
            if (TryGetFile(subpath, reader, out archiveFile))
            {
                return true;
            }
        }

        archiveFile = null;
        return false;
    }

    /// <summary>
    /// Checks both opened readers AND deferred (disk-cached) BSAs for a mod.
    /// If the file is found in a deferred BSA, the reader is lazily opened.
    /// </summary>
    public bool ReadersOrDeferredHaveFile(string subpath, ModKey modKey, HashSet<IArchiveReader> bsaReaders, out IArchiveFile archiveFile)
    {
        // Check opened readers first
        if (ReadersHaveFile(subpath, bsaReaders, out archiveFile))
            return true;

        // Check deferred BSAs for this mod
        if (_deferredBsaPaths.TryGetValue(modKey, out var deferredPaths))
        {
            foreach (var bsaPath in deferredPaths)
            {
                if (TryGetFileFromDeferredBsa(subpath, bsaPath, out archiveFile))
                {
                    // Promote this reader to the OpenReaders set so future lookups find it
                    if (_readersByBsaPath.TryGetValue(bsaPath, out var reader))
                    {
                        bsaReaders.Add(reader);
                    }
                    return true;
                }
            }
        }

        archiveFile = null;
        return false;
    }

    /// <summary>
    /// Opens BSA archives for ALL enabled mods in the load order — or, when the
    /// disk index cache is loaded, indexes them without opening readers.
    /// 
    /// Call this once before using <see cref="TryFindFileInAnyArchive"/> to ensure
    /// full coverage. Without this, only BSAs for mods that have already been queried
    /// via <see cref="TryOpenCorrespondingArchiveReaders"/> will be searchable.
    ///
    /// This is idempotent — already-opened/indexed readers are cached and won't be reopened.
    /// </summary>
    public void EnsureAllArchivesOpened()
    {
        foreach (var modKey in _enabledMods)
        {
            // TryOpenCorrespondingArchiveReaders caches results in OpenReaders,
            // so repeat calls for the same ModKey are a no-op dictionary lookup.
            // When the disk cache is loaded, BSAs with cached file listings
            // are indexed without opening the actual archive reader.
            TryOpenCorrespondingArchiveReaders(modKey, out _);
        }
    }

    /// <summary>
    /// Searches ALL opened AND deferred BSA archive readers for a file at the given sub-path.
    ///
    /// This is a broad fallback for cases where the file's owning mod isn't known
    /// (e.g., a head part record in plugin A references a mesh that ships in plugin B's
    /// BSA). Call <see cref="EnsureAllArchivesOpened"/> first to guarantee full coverage.
    ///
    /// When a file is found in a deferred (disk-cached) BSA, the reader is lazily opened
    /// at that point for extraction.
    /// </summary>
    public bool TryFindFileInAnyArchive(string subpath, out IArchiveFile archiveFile)
    {
        return TryFindFileInAnyArchive(subpath, out archiveFile, out _);
    }

    /// <summary>
    /// Same as <see cref="TryFindFileInAnyArchive(string, out IArchiveFile)"/> but
    /// also reports the absolute path of the BSA the file was found in.
    /// </summary>
    public bool TryFindFileInAnyArchive(string subpath, out IArchiveFile archiveFile, out string? containingBsaPath)
    {
        // Pass 1: Check already-opened readers (fast path).
        // Iterate _readersByBsaPath so we know which BSA each reader belongs to.
        foreach (var kvp in _readersByBsaPath)
        {
            if (TryGetFile(subpath, kvp.Value, out archiveFile))
            {
                containingBsaPath = kvp.Key;
                return true;
            }
        }

        // Pass 2: Check deferred (disk-cached) BSAs
        foreach (var kvp in _deferredBsaPaths)
        {
            foreach (var bsaPath in kvp.Value)
            {
                if (TryGetFileFromDeferredBsa(subpath, bsaPath, out archiveFile))
                {
                    // Promote the reader so future lookups find it via OpenReaders
                    if (_readersByBsaPath.TryGetValue(bsaPath, out var reader) &&
                        OpenReaders.TryGetValue(kvp.Key, out var readerSet))
                    {
                        readerSet.Add(reader);
                    }
                    containingBsaPath = bsaPath;
                    return true;
                }
            }
        }

        archiveFile = null;
        containingBsaPath = null;
        return false;
    }
}