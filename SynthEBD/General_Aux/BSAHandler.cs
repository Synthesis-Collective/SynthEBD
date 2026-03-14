using Mutagen.Bethesda;
using System.IO;
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

        return ReadersHaveFile(subPath, archiveReaders, out _);
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

            if (ReadersHaveFile(expectedFilePath, archiveReaders, out _))
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
        
        foreach (var bsaFile in Archive.GetApplicableArchivePaths(_environmentProvider.SkyrimVersion.ToGameRelease(), _environmentProvider.DataFolderPath, modKey))
        {
            try
            {
                var reader = Archive.CreateReader(_environmentProvider.SkyrimVersion.ToGameRelease(), bsaFile);
                if (reader != null)
                {
                    CacheReaderFiles(reader); // NEW: Cache the files immediately
                    archiveReaders.Add(reader);
                }
            }
            catch
            {
                _logger.LogError("Unable to open archive reader to BSA file " + bsaFile.Path);
            }
        }
        
        if (archiveReaders.Any() && !OpenReaders.ContainsKey(modKey))
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

    public bool ReadersHaveFile(string subpath, HashSet<IArchiveReader> bsaReaders, out IArchiveFile archiveFile)
    {
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
}