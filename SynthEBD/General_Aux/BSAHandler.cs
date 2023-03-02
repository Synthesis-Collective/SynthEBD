using Mutagen.Bethesda;
using System.IO;
using Mutagen.Bethesda.Archives;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;
using Mutagen.Bethesda.Plugins.Order;
using ReactiveUI;
using Noggog;
using Noggog.WPF;

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

    //This function expects a FilePathReplacement-formatted expectedFilePath (e.g. one that starts with the mod name, such as "Skyrim.esm\textures\myTextures.dds"
    //Do not use for searching all archive readers at a particular destination path.
    public bool ReferencedPathExists(string expectedFilePath, out bool archiveExists, out string modName)
    {
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

        if (ReadersHaveFile(subPath, archiveReaders, out _))
        {
            return true;
        }
        else
        {
            return false;
        }
    }

    public bool ReferencedPathExists(string expectedFilePath, IEnumerable<ModKey> candidateMods, out bool archiveExists, out string modName)
    {
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
        if (OpenReaders.ContainsKey(modKey))
        {
            archiveReaders = OpenReaders[modKey];
            return true;
        }
        else
        {
            foreach (var bsaFile in Archive.GetApplicableArchivePaths(_environmentProvider.SkyrimVersion.ToGameRelease(), _environmentProvider.DataFolderPath, modKey))
            {
                try
                {
                    var reader = Archive.CreateReader(_environmentProvider.SkyrimVersion.ToGameRelease(), bsaFile);
                    if (reader != null)
                    {
                        archiveReaders.Add(reader);
                    }
                }
                catch
                {
                    _logger.LogError("Unable to open archive reader to BSA file " + bsaFile.Path);
                }
            }
            if (archiveReaders.Any())
            {
                OpenReaders.Add(modKey, archiveReaders);
                return true;
            }
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
                readers.Add(new PathedArchiveReader() { Reader = bsaReader, FilePath = bsaFile });
            }
            catch
            {
                _logger.LogError("Could not open archive " + bsaFile.Path);
            }
        }
        return readers;
    }

    public void ExtractFileFromBSA(IArchiveFile file, string destPath)
    {
        string? dirPath = Path.GetDirectoryName(destPath);
        if (dirPath != null)
        {
            if (Directory.Exists(dirPath) == false)
            {
                try
                {
                    Directory.CreateDirectory(dirPath);
                }
                catch
                {
                    _logger.LogError("Could not create directory at " + dirPath + ". Check path length and permissions.");
                }
            }
            try
            {
                using var fileStream = File.Create(destPath);
                file.AsStream().CopyTo(fileStream);
            }
            catch
            {
                _logger.LogError("Could not extract file from BSA: " + file.Path + " to " + destPath + ". Check path length and permissions.");
            }
        }
        else
        {
            throw new Exception("Could not create the output directory at " + dirPath);
        }
    }

    public bool TryGetFile(string subpath, IArchiveReader bsaReader, out IArchiveFile file)
    {
        file = null;
        if (bsaReader == null) { return false; }
        var files = bsaReader.Files.Where(candidate => candidate.Path.Equals(subpath, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (files.Any())
        {
            file = files.First();
            return true;
        }
        else
        {
            return false;
        }
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

    public Dictionary<ModKey, HashSet<IArchiveReader>> OpenReaders = new Dictionary<ModKey, HashSet<IArchiveReader>>();
}