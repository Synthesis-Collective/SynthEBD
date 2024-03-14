using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using Noggog;
using System.Collections.Concurrent;
using System.Windows.Controls;
using System.Collections.ObjectModel;

namespace SynthEBD;

public class VM_ConfigPathRemapper : VM
{
    public delegate VM_ConfigPathRemapper Factory(VM_AssetPack parentAssetPack);
    private readonly IEnvironmentStateProvider _environmentStateProvider;
    public VM_ConfigPathRemapper(VM_AssetPack parentAssetPack, IEnvironmentStateProvider environmentStateProvider)
    {
        _parentAssetPack = parentAssetPack;
        _environmentStateProvider = environmentStateProvider;

        SelectNewAssetDirectory = new RelayCommand(
            canExecute: _ => true,
            execute: _ =>
            {
                if (IO_Aux.SelectFolder("", out string folder))
                {
                    NewAssetDirectory = folder;
                }
            });

        _hashingProgress = new(progress =>
        {
            Interlocked.Increment(ref _progressCurrent);
            ProgressCurrent = _progressCurrent;

            if (ProgressCurrent == ProgressMax - 1)
            {
                ShowProgressDigits = false;
                ShowProgressEndMessage = true;
            }
        });

        RemapPaths = new RelayCommand(
            canExecute: _ => true,
            execute: async _ =>
            {
                if (NewAssetDirectory.IsNullOrEmpty() || !Directory.Exists(NewAssetDirectory))
                {
                    MessageWindow.DisplayNotificationOK("No Can Do", "New asset source directory is invalid");
                    return;
                }

                GetCurrentFileExtensions();

                UpdatedSubgroups.Clear();
                await Task.Run(() => ComputePathHashes(_hashingProgress));
                if (_missingCurrentPaths.Any())
                {
                    CreateMissingPathsObjects(_missingCurrentPaths.ToList());
                }

                RemapPathsByHash();

                PredictUpdatesByPathSimilarity();

                ShowProgressEndMessage = false;
                ShowProgressBar = false;

                ShowMissingSubgroups = MissingPathSubgroups.Any();

                if (UpdatedSubgroups.Any())
                {
                    ShowRemappedByHashList = true;
                }

                if (PredictedUpdateSubgroups.Any())
                {
                    ShowPredictedPathUpdateList = true;
                }

                if (NewFilesUnmatched.Any())
                {
                    ShowUnpredictedPathUpdateList = true;
                }
            });
    }

    private VM_AssetPack _parentAssetPack { get; set; }
    public string NewAssetDirectory { get; set; } = string.Empty;
    public RelayCommand SelectNewAssetDirectory { get; }
    public RelayCommand RemapPaths { get; }
    public int ProgressCurrent { get; set; }
    public int ProgressMax { get; set; }
    private int _progressCurrent = 0;
    private Progress<int> _hashingProgress { get; }
    private List<string> _currentFileExtensions { get; set; } = new(); // files extensions used in the original config file (so as to ignore xml files, preview images, etc from the updated mod archive)
    public ObservableCollection<RemappedSubgroup> UpdatedSubgroups { get; set; } = new();
    private List<string> _filesMatchedByHash_Existing { get; set; } = new();
    private List<string> _filesMatchedByHash_New { get; set; } = new(); // paths in the new mod that got matched by hash to files in the previous version
    private List<string> _unmatchedPaths_Current { get; set; } = new(); // paths in the current config file that do not have a hash match in the new mod
    private List<string> _unmatchedPaths_New { get; set; } = new(); // paths in the new mod that do not have a hash match in the current config file
    private List<string> _allFiles_New { get; set; } = new();
    public ObservableCollection<string> NewFilesUnmatched { get; set; } = new();
    public ObservableCollection<RemappedSubgroup> PredictedUpdateSubgroups { get; set; } = new();
    private List<string> _missingCurrentPaths { get; set; } = new();
    public ObservableCollection<RemappedSubgroup> MissingPathSubgroups { get; set; } = new();
    public bool ShowMissingSubgroups { get; set; } = false;
    public bool ShowProgressBar { get; set; } = false;
    public bool ShowProgressDigits { get; set; } = false;
    public bool ShowProgressEndMessage { get; set; } = false;
    public bool ShowRemappedByHashList { get; set; } = false;
    public bool ShowPredictedPathUpdateList { get; set; } = false;
    public bool ShowUnpredictedPathUpdateList { get; set; } = false;

    private ConcurrentDictionary<string, string> _currentPathHashes { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    private ConcurrentDictionary<string, string> _newPathHashes { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public PathMatchModeHash HashMatchMode { get; set; } = PathMatchModeHash.Similar;

    private void GetCurrentFileExtensions()
    {
        var allSubgroups = _parentAssetPack.GetAllSubgroups();
        foreach (var subgroup in allSubgroups)
        {
            foreach (var path in subgroup.AssociatedModel.Paths)
            {
                var extension = Path.GetExtension(path.Source);
                if (!_currentFileExtensions.Contains(extension))
                {
                    _currentFileExtensions.Add(extension);
                }
            }
        }
    }

    private async Task ComputePathHashes(IProgress<int> progress)
    {
        ShowProgressBar = true;
        ShowProgressDigits = true;
        var currentReferencedFilePaths = _parentAssetPack.GetAllSubgroups().SelectMany(x => x.AssociatedModel.Paths).Select(x => Path.Combine(_environmentStateProvider.DataFolderPath, x.Source)).ToList();
        _allFiles_New = Directory.GetFiles(NewAssetDirectory, "*", SearchOption.AllDirectories).Where(x => _currentFileExtensions.Contains(Path.GetExtension(x), StringComparer.OrdinalIgnoreCase)).ToList();
        ProgressMax = currentReferencedFilePaths.Count + _allFiles_New.Count;
        _progressCurrent = 0;

        var missingFiles = new ConcurrentBag<string>();

        Parallel.ForEach(currentReferencedFilePaths, path =>
        {
            if (File.Exists(path))
            {
                var hash = MiscFunctions.CalculateMD5(path);
                if (!hash.IsNullOrWhitespace())
                {
                    _currentPathHashes.TryAdd(Path.GetRelativePath(_environmentStateProvider.DataFolderPath, path), hash);
                }
            }
            else
            {
                missingFiles.Add(Path.GetRelativePath(_environmentStateProvider.DataFolderPath, path));
            }
            progress.Report(1);
        });

        Parallel.ForEach(_allFiles_New, path =>
        {
            if (File.Exists(path))
            {
                var hash = MiscFunctions.CalculateMD5(path);
                if (!hash.IsNullOrWhitespace())
                {
                    _newPathHashes.TryAdd(Path.GetRelativePath(NewAssetDirectory, path), hash);
                }
            }               
            progress.Report(1);
        });

        if (missingFiles.Any())
        {
            _missingCurrentPaths = missingFiles.ToList();
        }
    }
    
    private void CreateMissingPathsObjects(List<string> missingPaths)
    {
        var subgroups = _parentAssetPack.GetAllSubgroups();
        foreach (var subgroup in subgroups)
        {
            var logged = new RemappedSubgroup(subgroup);
            foreach (var path in subgroup.AssociatedModel.Paths.Select(x => x.Source).ToArray())
            {
                if (missingPaths.Contains(path))
                {
                    logged.Paths.Add(new RemappedPath()
                    {
                        OldPath = path
                    });
                }
            }

            if (logged.Paths.Any())
            {
                MissingPathSubgroups.Add(logged);
            }
        }
    }

    private void RemapPathsByHash()
    {
        var subgroups = _parentAssetPack.GetAllSubgroups();
        foreach ( var subgroup in subgroups)
        {
            var remappedHolder = new RemappedSubgroup(subgroup);

            var paths = subgroup.AssociatedModel.Paths.ToList();
            for (int i = 0; i < paths.Count; i++)
            {
                var path = paths[i];
                if (_currentPathHashes.ContainsKey(path.Source))
                {
                    var currentHash = _currentPathHashes[path.Source];
                    var matchingEntries = _newPathHashes.Where(x => x.Value.Equals(currentHash)).ToList();
                    if (matchingEntries.Any())
                    {
                        var newSource = ChooseBestHashMatch(matchingEntries.Select(x => x.Key).ToList(), path.Source);
                        var recordEntry = new RemappedPath()
                        {
                            OldPath = path.Source,
                            NewPath = newSource
                        };

                        _filesMatchedByHash_New.AddRange(matchingEntries.Select(x => x.Key));
                        _filesMatchedByHash_Existing.Add(path.Source);

                        remappedHolder.Paths.Add(recordEntry);
                        path.Source = newSource;
                    }
                    else if (!_unmatchedPaths_Current.Contains(path.Source))
                    {
                        _unmatchedPaths_Current.Add(path.Source);
                    }
                }
                else if (!_unmatchedPaths_Current.Contains(path.Source))
                {
                    _unmatchedPaths_Current.Add(path.Source);
                }
            }

            if (remappedHolder.Paths.Any())
            {
                subgroup.AssociatedModel.Paths = paths.ToHashSet();
                UpdatedSubgroups.Add(remappedHolder);
            }
        }
    }

    private string ChooseBestHashMatch(List<string> matches, string origPath)
    {
        switch(HashMatchMode)
        {
            case PathMatchModeHash.Shortest:
                return matches.ToList().OrderBy(x => GetDirectoryNestingCount(x)).First();
            case PathMatchModeHash.Similar:
                return matches.ToList().OrderBy(x => GetMatchingDirCount(x, origPath)).First();
            default:
                return matches.First();
        }
    }

    private int GetDirectoryNestingCount(string filePath)
    {
        var parentDir = Directory.GetParent(filePath)?.FullName ?? string.Empty;
        if (parentDir.IsNullOrWhitespace())
        {
            return -1;
        }
        else
        {
            return parentDir.Split(Path.DirectorySeparatorChar).Length;
        }
    }

    private void PredictUpdatesByPathSimilarity()
    {
        var allFiles_New_RelativePaths = _allFiles_New.Select(x => Path.GetRelativePath(NewAssetDirectory, x)).ToList();
        _unmatchedPaths_New = allFiles_New_RelativePaths.Where(x => !_filesMatchedByHash_New.Contains(x)).ToList();

        var subgroups = _parentAssetPack.GetAllSubgroups();

        foreach (var unmatchedPath in _unmatchedPaths_New)
        {
            string currentFileName = Path.GetFileName(unmatchedPath);
            List<string> matchingPaths = new();

            // get all paths with the same file name
            foreach (var subgroup in subgroups)
            {
                foreach (var existingPath in subgroup.AssociatedModel.Paths)
                {
                    if (currentFileName.Equals(Path.GetFileName(existingPath.Source), StringComparison.OrdinalIgnoreCase) && !_filesMatchedByHash_New.Contains(existingPath.Source) && !matchingPaths.Contains(existingPath.Source))
                    {
                        matchingPaths.Add(existingPath.Source);
                    }
                }
            }

            // predict which one is a match based on the folder structure
            bool predictionMade = false;
            if (matchingPaths.Any())
            {
                matchingPaths.OrderBy(x => GetMatchingDirCount(x, unmatchedPath));
                if (GetMatchingDirCount(matchingPaths.First(), unmatchedPath) > 1)
                {
                    // make predictions here
                    foreach (var subgroup in subgroups)
                    {
                        foreach (var pathToUpdate in subgroup.AssociatedModel.Paths)
                        {
                            if (pathToUpdate.Source == matchingPaths.First())
                            {
                                var recordEntry = PredictedUpdateSubgroups.Where(x => x.SourceSubgroup == subgroup).FirstOrDefault();
                                if (recordEntry == null)
                                {
                                    recordEntry = new(subgroup);
                                    PredictedUpdateSubgroups.Add(recordEntry);
                                }

                                recordEntry.Paths.Add(new()
                                {
                                    OldPath = pathToUpdate.Source,
                                    NewPath = unmatchedPath
                                });
                            }
                        }
                    }

                    predictionMade = true;
                }
            }
            
            if (!predictionMade)
            {
                // record no prediction
                NewFilesUnmatched.Add(unmatchedPath);
            }
        }
    }

    private int GetMatchingDirCount(string path1, string path2)
    {
        var split1 = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { path1.Split(Path.DirectorySeparatorChar) };
        var split2 = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { path2.Split(Path.DirectorySeparatorChar) };

        return split1.Intersect(split2).Count();
    }

    public class RemappedSubgroup
    {
        public RemappedSubgroup(VM_SubgroupPlaceHolder subgroup)
        {
            SourceSubgroup = subgroup;
        }
        public VM_SubgroupPlaceHolder SourceSubgroup { get; }
        public ObservableCollection<RemappedPath> Paths { get; set; } = new();
    }

    public class RemappedPath
    {
        public string OldPath { get; set; } = string.Empty;
        public string NewPath { get; set; } = string.Empty;
    }
}

public enum PathMatchModeHash 
{
    Shortest,
    Similar
}
