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
using ReactiveUI;
using Mutagen.Bethesda.Starfield;
using static SynthEBD.AssetPack;

namespace SynthEBD;

public class VM_ConfigPathRemapper : VM
{
    public delegate VM_ConfigPathRemapper Factory(VM_AssetPack parentAssetPack, Window_ConfigPathRemapper window);
    private readonly IEnvironmentStateProvider _environmentStateProvider;
    public VM_ConfigPathRemapper(VM_AssetPack parentAssetPack, Window_ConfigPathRemapper window, IEnvironmentStateProvider environmentStateProvider)
    {
        _parentAssetPack = parentAssetPack;
        _environmentStateProvider = environmentStateProvider;
        _hashMatchedVM = new("Some Assets Were Remapped with 100% Confidence", SubgroupsRemappedByHash);
        _predictionMatchedVM = new("Some assets were remapped by path similarity - please check that these are correct", SubgroupsRemappedByPathPrediction);
        _missingPathsVM = new(MissingPathSubgroups);
        _failedRemappingsVM = new(NewFilesUnmatched);

        DisplayHashMatches = new RelayCommand(
            canExecute: _ => true,
            execute: async _ =>
            {
                DisplayedSubMenu = _hashMatchedVM;
            });

        DisplayPathPredictionMatches = new RelayCommand(
            canExecute: _ => true,
            execute: async _ =>
            {
                DisplayedSubMenu = _predictionMatchedVM;
            });

        DisplayMissingPaths = new RelayCommand(
            canExecute: _ => true,
            execute: async _ =>
            {
                DisplayedSubMenu = _missingPathsVM;
            });

        DisplayFailedRemapping = new RelayCommand(
            canExecute: _ => true,
            execute: async _ =>
            {
                DisplayedSubMenu = _failedRemappingsVM;
            });

        window.Events().Unloaded
            .Subscribe(_ => RemapSelectedPaths()).DisposeWith(this);

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
                SortFilesByName();

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

                if (SubgroupsRemappedByHash.Any())
                {
                    ShowRemappedByHashList = true;
                }

                if (SubgroupsRemappedByPathPrediction.Any())
                {
                    ShowPredictedPathUpdateList = true;
                }

                if (NewFilesUnmatched.Any())
                {
                    ShowUnpredictedPathUpdateList = true;
                }
                ProcessingComplete = true;
                ShowRemapButton = false;
            });
    }

    private VM_AssetPack _parentAssetPack { get; set; }
    private VM_ConfigRemapperPathSubstitutions _hashMatchedVM { get; set; }
    private VM_ConfigRemapperPathSubstitutions _predictionMatchedVM { get; set; }
    private VM_ConfigRemapperMissingPaths _missingPathsVM { get; set; }
    private VM_ConfigRemapperFailedRemappings _failedRemappingsVM { get; set; }
    public VM DisplayedSubMenu { get; set; }
    public RelayCommand DisplayHashMatches { get; }
    public RelayCommand DisplayPathPredictionMatches { get; }
    public RelayCommand DisplayMissingPaths { get; }
    public RelayCommand DisplayFailedRemapping { get; }
    public bool ProcessingComplete { get; set; } = false;

    public string NewAssetDirectory { get; set; } = string.Empty;
    public RelayCommand SelectNewAssetDirectory { get; }
    public RelayCommand RemapPaths { get; }
    public bool ShowRemapButton { get; set; } = true;
    public int ProgressCurrent { get; set; }
    public int ProgressMax { get; set; }
    private int _progressCurrent = 0;
    private Progress<int> _hashingProgress { get; }
    private List<string> _currentFileExtensions { get; set; } = new(); // files extensions used in the original config file (so as to ignore xml files, preview images, etc from the updated mod archive)
    public ObservableCollection<RemappedSubgroup> SubgroupsRemappedByHash { get; set; } = new();
    private List<string> _filesMatchedByHash_Existing { get; set; } = new();
    private List<string> _filesMatchedByHash_New { get; set; } = new(); // paths in the new mod that got matched by hash to files in the previous version
    private List<string> _unmatchedPaths_Current { get; set; } = new(); // paths in the current config file that do not have a hash match in the new mod
    private List<string> _unmatchedPaths_New { get; set; } = new(); // paths in the new mod that do not have a hash match in the current config file
    private List<string> _allFiles_New { get; set; } = new();
    public ObservableCollection<string> NewFilesUnmatched { get; set; } = new();
    public ObservableCollection<RemappedSubgroup> SubgroupsRemappedByPathPrediction { get; set; } = new();
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
    public Dictionary<string, ObservableCollection<string>> NewPathsByFileName { get; set; } = new(StringComparer.OrdinalIgnoreCase);

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

    private void SortFilesByName()
    {
        if (!_allFiles_New.Any()) // in case execution order changes later
        {
            _allFiles_New = Directory.GetFiles(NewAssetDirectory, "*", SearchOption.AllDirectories).Where(x => _currentFileExtensions.Contains(Path.GetExtension(x), StringComparer.OrdinalIgnoreCase)).ToList();
        }

        var pathsByFileName = _allFiles_New.GroupBy(x => Path.GetFileName(x).ToLower()).ToArray();
        foreach (var group in pathsByFileName)
        {
            var newCollection = new ObservableCollection<string>();
            newCollection.AddRange(group.Select(x => Path.GetRelativePath(NewAssetDirectory, x)));
            NewPathsByFileName.Add(group.Key, newCollection);
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
                var pathEntry = paths[i];
                if (_currentPathHashes.ContainsKey(pathEntry.Source))
                {
                    var currentHash = _currentPathHashes[pathEntry.Source];
                    var matchingEntries = _newPathHashes.Where(x => x.Value.Equals(currentHash)).ToList();
                    if (matchingEntries.Any())
                    {
                        var newSource = ChooseBestHashMatch(matchingEntries.Select(x => x.Key).ToList(), pathEntry.Source);
                        var recordEntry = new RemappedPath()
                        {
                            OldPath = pathEntry.Source,
                            NewPath = newSource,
                            CandidateNewPaths = NewPathsByFileName[Path.GetFileName(newSource)]
                        };

                        _filesMatchedByHash_New.AddRange(matchingEntries.Select(x => x.Key));
                        _filesMatchedByHash_Existing.Add(pathEntry.Source);

                        remappedHolder.Paths.Add(recordEntry);
                    }
                    else if (!_unmatchedPaths_Current.Contains(pathEntry.Source))
                    {
                        _unmatchedPaths_Current.Add(pathEntry.Source);
                    }
                }
                else if (!_unmatchedPaths_Current.Contains(pathEntry.Source))
                {
                    _unmatchedPaths_Current.Add(pathEntry.Source);
                }
            }

            if (remappedHolder.Paths.Any())
            {
                subgroup.AssociatedModel.Paths = paths.ToHashSet();
                SubgroupsRemappedByHash.Add(remappedHolder);
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
                return matches.ToList().OrderBy(x => GetMatchingDirCount(x, origPath)).Last(); // lowest to highest
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
                matchingPaths = matchingPaths.OrderBy(x => GetMatchingDirCount(x, unmatchedPath)).ToList(); // lowest to highest
                var bestMatchingPath = matchingPaths.Last();
                var matchingDirNamesCount = GetMatchingDirCount(bestMatchingPath, unmatchedPath);
                if (matchingDirNamesCount > 1) // 1 because the first folder name should always be a match (e.g. "textures")
                {
                    // make predictions here
                    foreach (var subgroup in subgroups)
                    {
                        foreach (var pathToUpdate in subgroup.AssociatedModel.Paths)
                        {
                            if (pathToUpdate.Source == bestMatchingPath && !PathWasRemappedByHash(subgroup, pathToUpdate))
                            {
                                var recordEntry = SubgroupsRemappedByPathPrediction.Where(x => x.SourceSubgroup == subgroup).FirstOrDefault();
                                if (recordEntry == null)
                                {
                                    recordEntry = new(subgroup);
                                    SubgroupsRemappedByPathPrediction.Add(recordEntry);
                                }

                                var pathEntry = recordEntry.Paths.Where(x => x.OldPath == bestMatchingPath).FirstOrDefault();
                                if (pathEntry == null)
                                {
                                    pathEntry = new();
                                    recordEntry.Paths.Add(pathEntry);
                                }

                                if (pathEntry.OldPath.IsNullOrEmpty() || matchingDirNamesCount > GetMatchingDirCount(pathEntry.OldPath, unmatchedPath))
                                {
                                    pathEntry.OldPath = pathToUpdate.Source;
                                    pathEntry.NewPath = unmatchedPath;
                                    pathEntry.CandidateNewPaths = NewPathsByFileName[Path.GetFileName(unmatchedPath)];
                                }
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

    private bool PathWasRemappedByHash(VM_SubgroupPlaceHolder subgroup, FilePathReplacement pathToUpdate)
    {
        var matchingSubgroup = SubgroupsRemappedByHash.Where(x => x.SourceSubgroup == subgroup).FirstOrDefault();
        if (matchingSubgroup != null)
        {
            var matchingPath = matchingSubgroup.Paths.Where(x => x.OldPath == pathToUpdate.Source).FirstOrDefault();
            if (matchingPath != null)
            {
                return true;
            }
        }

        return false;
    }

    private int GetMatchingDirCount(string path1, string path2)
    {
        var split1 = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { (Path.GetDirectoryName(path1) ?? path1).Split(Path.DirectorySeparatorChar) };
        var split2 = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { (Path.GetDirectoryName(path2) ?? path1).Split(Path.DirectorySeparatorChar) };

        return split1.Intersect(split2).Count();
    }

    private void RemapSelectedPaths()
    {
        foreach (var subgroupEntry in SubgroupsRemappedByHash)
        {
            foreach (var remappedPath in subgroupEntry.Paths.Where(x => x.AcceptRenaming).ToArray())
            {
                foreach (var pathEntry in subgroupEntry.SourceSubgroup.AssociatedModel.Paths.Where(x => x.Source == remappedPath.OldPath).ToArray())
                {
                    pathEntry.Source = remappedPath.NewPath;
                }
            }
        }

        foreach (var subgroupEntry in SubgroupsRemappedByPathPrediction)
        {
            foreach (var remappedPath in subgroupEntry.Paths.Where(x => x.AcceptRenaming).ToArray())
            {
                foreach (var pathEntry in subgroupEntry.SourceSubgroup.AssociatedModel.Paths.Where(x => x.Source == remappedPath.OldPath).ToArray())
                {
                    pathEntry.Source = remappedPath.NewPath;
                }
            }
        }
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
        public bool AcceptRenaming { get; set; } = true;
        public ObservableCollection<string> CandidateNewPaths { get; set;} = new();
    }
}

public enum PathMatchModeHash 
{
    Shortest,
    Similar
}
