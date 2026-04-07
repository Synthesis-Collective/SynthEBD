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
using System.Reactive.Linq;

namespace SynthEBD;

public class VM_ConfigPathRemapper : VM
{
    public delegate VM_ConfigPathRemapper Factory(VM_AssetPack parentAssetPack, Window_ConfigPathRemapper window);
    private readonly IEnvironmentStateProvider _environmentStateProvider;
    private readonly VM_SettingsModManager _modManagerSettings;
    private readonly VM_SubgroupPlaceHolder.Factory _subgroupFactory;
    private readonly RemappedPath.Factory _remappedPathFactory;

    public VM_ConfigPathRemapper(VM_AssetPack parentAssetPack, Window_ConfigPathRemapper window, IEnvironmentStateProvider environmentStateProvider, VM_SubgroupPlaceHolder.Factory subgroupFactory, VM_SettingsModManager modManagerSettings, RemappedPath.Factory remappedPathFactory)
    {
        _parentAssetPack = parentAssetPack;
        _environmentStateProvider = environmentStateProvider;
        _subgroupFactory = subgroupFactory;
        _modManagerSettings = modManagerSettings;
        _remappedPathFactory = remappedPathFactory;

        _hashMatchedVM = new("Some Assets Were Remapped with 100% Confidence", SubgroupsRemappedByHash);
        _predictionMatchedVM = new("Some assets were remapped by path similarity - please check that these are correct", SubgroupsRemappedByPathPrediction);
        _missingPathsVM = new(MissingPathSubgroups, "Some assets expected by the original config file are missing and could not be analyzed for remapping");
        _failedRemappingsVM = new(NewFilesUnmatched);
        _deprecatedPathsVM = new("Some subgroups contain assets for which a replacement could not be automatically selected. Please select one and opt-in for replacement if possible", DeprecatedPathSubgroups);
        _assetComparerVM = new();

        DisplayHashMatches = new RelayCommand(
            canExecute: _ => true,
            execute: async _ =>
            {
                DisplayedSubMenu = _hashMatchedVM;
                _hashMatchedVM.Refresh(SubgroupSearchText, SubgroupSearchCaseSensitive,PathSearchText, PathSearchCaseSensitive);
            });

        DisplayPathPredictionMatches = new RelayCommand(
            canExecute: _ => true,
            execute: async _ =>
            {
                DisplayedSubMenu = _predictionMatchedVM;
                _predictionMatchedVM.Refresh(SubgroupSearchText, SubgroupSearchCaseSensitive, PathSearchText, PathSearchCaseSensitive);
            });

        DisplayMissingPaths = new RelayCommand(
            canExecute: _ => true,
            execute: async _ =>
            {
                DisplayedSubMenu = _missingPathsVM;
                _missingPathsVM.Refresh(SubgroupSearchText, SubgroupSearchCaseSensitive, PathSearchText, PathSearchCaseSensitive);
            });

        DisplayFailedRemapping = new RelayCommand(
            canExecute: _ => true,
            execute: async _ =>
            {
                DisplayedSubMenu = _failedRemappingsVM;
                _failedRemappingsVM.Refresh(SubgroupSearchText, SubgroupSearchCaseSensitive, PathSearchText, PathSearchCaseSensitive);
            });

        DisplayDeprecatedPathSubgroups = new RelayCommand(
            canExecute: _ => true,
            execute: async _ =>
            {
                DisplayedSubMenu = _deprecatedPathsVM;
                _deprecatedPathsVM.Refresh(SubgroupSearchText, SubgroupSearchCaseSensitive, PathSearchText, PathSearchCaseSensitive);
            });

        DisplayAssetComparer = new RelayCommand(
            canExecute: _ => true,
            execute: async _ =>
            {
                DisplayedSubMenu = _assetComparerVM;
            });

        window.Events().Unloaded
            .Subscribe(_ => {
                RemapSelectedPaths();
                CreateRequestedSubgroups();
                }).DisposeWith(this);

        this.WhenAnyValue(a => a.SubgroupSearchText, b => b.SubgroupSearchCaseSensitive, c => c.PathSearchText, d => d.PathSearchCaseSensitive)
            .Throttle(TimeSpan.FromMilliseconds(100), RxApp.MainThreadScheduler)
            .Subscribe(z => DisplayedSubMenu?.Refresh(z.Item1, z.Item2, z.Item3, z.Item4))
            .DisposeWith(this);

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

                var invalidPaths = CheckNewPathLengths();
                if (invalidPaths.Any())
                {
                    var displayStrs = invalidPaths.Select(x => x + " (" + x.Length + ")").ToList();
                    displayStrs.Insert(0, "The following paths were too long to process. Please move the mod into a shorter or less deeply nested folder.");
                    displayStrs.Add(Environment.NewLine);
                    displayStrs.Add("Do you want to continue the update anyway (not recommended)?");
                    if (!MessageWindow.DisplayNotificationYesNo("Path Length Error", displayStrs, Environment.NewLine))
                    {
                        _currentFileExtensions.Clear();
                        NewPathsByFileName.Clear();
                        _allFiles_New.Clear();
                        return;
                    }
                }

                await Task.Run(() => ComputePathHashes(_hashingProgress));
                if (_missingCurrentPaths.Any())
                {
                    CreateMissingPathsObjects(_missingCurrentPaths.ToList());
                }

                RemapPathsByHash();

                PredictUpdatesByPathSimilarity();

                GetUnUpdatedPaths();

                //CheckTexturePathConflicts(); // may come back to this later

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

                if (DeprecatedPathSubgroups.Any())
                {
                    ShowDeprecatedPathSubgroups = true;
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
    private VM_ConfigRemapperPathSubstitutions _deprecatedPathsVM { get; set; }
    private VM_ConfigRemapperAssetComparer _assetComparerVM { get; set; }
    public IConfigRemapperSubVM DisplayedSubMenu { get; set; }
    public RelayCommand DisplayHashMatches { get; }
    public RelayCommand DisplayPathPredictionMatches { get; }
    public RelayCommand DisplayMissingPaths { get; }
    public RelayCommand DisplayFailedRemapping { get; }
    public RelayCommand DisplayDeprecatedPathSubgroups { get; }
    public RelayCommand DisplayAssetComparer { get; }
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
    public bool LimitHashMatchingByFileName { get; set; } = true;
    private List<string> _filesMatchedByHash_Existing { get; set; } = new();
    private List<string> _filesMatchedByHash_New { get; set; } = new(); // paths in the new mod that got matched by hash to files in the previous version
    private List<string> _unmatchedPaths_Current { get; set; } = new(); // paths in the current config file that do not have a hash match in the new mod
    private List<string> _allFiles_New { get; set; } = new();
    public ObservableCollection<SelectableFilePath> NewFilesUnmatched { get; set; } = new();
    public ObservableCollection<RemappedSubgroup> SubgroupsRemappedByPathPrediction { get; set; } = new();
    private List<string> _missingCurrentPaths { get; set; } = new();
    public ObservableCollection<RemappedSubgroup> MissingPathSubgroups { get; set; } = new();
    public bool ShowMissingSubgroups { get; set; } = false;
    public ObservableCollection<RemappedSubgroup> DeprecatedPathSubgroups { get; set; } = new();
    public bool ShowProgressBar { get; set; } = false;
    public bool ShowProgressDigits { get; set; } = false;
    public bool ShowProgressEndMessage { get; set; } = false;
    public bool ShowRemappedByHashList { get; set; } = false;
    public bool ShowPredictedPathUpdateList { get; set; } = false;
    public bool ShowUnpredictedPathUpdateList { get; set; } = false;
    public bool ShowDeprecatedPathSubgroups { get; set; } = false;

    private ConcurrentDictionary<string, string> _currentPathHashes { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    private ConcurrentDictionary<string, string> _newPathHashes { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public PathMatchModeHash HashMatchMode { get; set; } = PathMatchModeHash.Similar;
    public Dictionary<string, ObservableCollection<string>> NewPathsByFileName { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public string SubgroupSearchText { get; set; } = string.Empty;
    public bool SubgroupSearchCaseSensitive { get; set; } = false;
    public string PathSearchText { get; set; } = string.Empty;
    public bool PathSearchCaseSensitive { get; set; } = false;

    public ObservableCollection<MultimappedSubgroup> MultimappedSubgroups { get; set; } = new();
    
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

    private List<string> CheckNewPathLengths()
    {
        return _allFiles_New.Where(y => y.Length > _modManagerSettings.FilePathLimit).ToList();
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
                    var missingPath = _remappedPathFactory(this);
                    missingPath.OldPath = path;
                    logged.Paths.Add(missingPath);
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

                    if (LimitHashMatchingByFileName)
                    {
                        matchingEntries = matchingEntries.Where(x => Path.GetFileName(x.Key).Equals(Path.GetFileName(pathEntry.Source), StringComparison.OrdinalIgnoreCase)).ToList();
                    }

                    if (matchingEntries.Any())
                    {
                        var newSource = ChooseBestHashMatch(matchingEntries.Select(x => x.Key).ToList(), pathEntry.Source);
                        var recordEntry = _remappedPathFactory(this);
                        recordEntry.OldPath = pathEntry.Source;
                        recordEntry.NewPath = newSource;
                        recordEntry.CandidateNewPaths = NewPathsByFileName[Path.GetFileName(newSource)];

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
        var unmatchedPaths_New = allFiles_New_RelativePaths.Where(x => !_filesMatchedByHash_New.Contains(x)).ToList();
        List<string> ejectedPaths = new();

        PredictUpdatesByPathSimilarity(unmatchedPaths_New, ejectedPaths);

        if (ejectedPaths.Any())
        {
            List<string> ejectedPaths2 = new();
            PredictUpdatesByPathSimilarity(ejectedPaths, ejectedPaths2);
            foreach (var ejectedPath in ejectedPaths2)
            {
                // record no prediction
                bool isLikelyCharacterTexture = FilePathDestinationMap.FileNameToDestMap.Any(x =>
                    x.Key.Equals(Path.GetFileName(ejectedPath), StringComparison.OrdinalIgnoreCase));
                NewFilesUnmatched.Add(new(ejectedPath, isLikelyCharacterTexture));
            }
        }
    }

    private void PredictUpdatesByPathSimilarity(List<string> candidatePaths, List<string> ejectedPaths)
    {
        var subgroups = _parentAssetPack.GetAllSubgroups();
        foreach (var unmatchedPath in candidatePaths)
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
                matchingPaths = matchingPaths.OrderBy(x => GetMatchingDirCount(x, unmatchedPath)).ToList(); // lowest to highest, then alphabetically
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
                                    pathEntry = _remappedPathFactory(this);
                                    recordEntry.Paths.Add(pathEntry);
                                }

                                if (pathEntry.OldPath.IsNullOrEmpty() || matchingDirNamesCount > GetMatchingDirCount(pathEntry.OldPath, unmatchedPath))
                                {
                                    pathEntry.OldPath = pathToUpdate.Source;

                                    if (!pathEntry.NewPath.IsNullOrEmpty() && !ejectedPaths.Contains(pathEntry.NewPath)) // if a previous unmatched path was assigned to this subgroup but the new unmatched path is a better match, keep the old one and recirculate.
                                    {
                                        ejectedPaths.Add(pathEntry.NewPath);
                                    }
                                    
                                    pathEntry.NewPath = unmatchedPath;
                                    pathEntry.CandidateNewPaths = NewPathsByFileName[Path.GetFileName(unmatchedPath)];
                                    predictionMade = true;
                                }
                            }
                        }
                    }
                }
            }
            
            if (!predictionMade)
            {
                // record no prediction
                bool isLikelyCharacterTexture = FilePathDestinationMap.FileNameToDestMap.Any(x =>
                    x.Key.Equals(Path.GetFileName(unmatchedPath), StringComparison.OrdinalIgnoreCase));
                NewFilesUnmatched.Add(new(unmatchedPath, isLikelyCharacterTexture));
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

    private void CreateRequestedSubgroups()
    {
        var requestedSubgroups = SubgroupsRemappedByPathPrediction.SelectMany(x => x.Paths).Where(path => path.CreateSubgroupFrom).ToList();
        var requestedSubgroups2 = NewFilesUnmatched.Where(x => x.CreateSubgroupFromFile).ToList();
        HashSet<string> processed = new();

        if(requestedSubgroups.Any() || requestedSubgroups2.Any())
        {
            var filesToAdd = requestedSubgroups.Select(x => x.NewPath).And(requestedSubgroups2.Select(x => x.File)).ToList();

            var parentSubgroup = _subgroupFactory(new(), null, _parentAssetPack, _parentAssetPack.Subgroups);
            _parentAssetPack.Subgroups.Add(parentSubgroup);
            parentSubgroup.AssociatedModel.Name = "Additional Assets";
            parentSubgroup.AssociatedModel.ID = "AA";
            parentSubgroup.Name = "Additional Assets";

            foreach (var assetPath in filesToAdd)
            {
                if (processed.Contains(assetPath)) { continue; }

                string assetFile = Path.GetFileName(assetPath);
                string assetName = Path.GetFileNameWithoutExtension(assetPath);
                string destination = string.Empty;
                if (FilePathDestinationMap.FileNameToDestMap.ContainsKey(assetFile))
                {
                    destination = FilePathDestinationMap.FileNameToDestMap[assetFile];
                }

                var newModel = new AssetPack.Subgroup()
                {
                    Name = assetName,
                    Paths =
                    {
                        new() { 
                        Source = assetPath,
                        Destination = destination
                        }
                    }
                };
                
                var newSubgroup = _subgroupFactory(newModel, parentSubgroup, _parentAssetPack, parentSubgroup.Subgroups);
                parentSubgroup.Subgroups.Add(newSubgroup);
                processed.Add(assetPath);
            }
            parentSubgroup.AutoGenerateID(true, 0);
            parentSubgroup.Refresh(true);
        }    
    }

    public void GetUnUpdatedPaths()
    {
        var subgroups = _parentAssetPack.GetAllSubgroups();
        foreach (var subgroup in subgroups)
        {
            var placeholderSubgroup = new RemappedSubgroup(subgroup);
            foreach (var path in subgroup.AssociatedModel.Paths)
            {
                if (!SubgroupsRemappedByHash.Where(x => x.Paths.Any(y => y.OldPath == path.Source)).Any() &&
                    !SubgroupsRemappedByPathPrediction.Where(x => x.Paths.Any(y => y.OldPath == path.Source)).Any() &&
                    !MissingPathSubgroups.Where(x => x.Paths.Any(y => y.OldPath == path.Source)).Any() &&
                    !_allFiles_New.Contains(path.Source, StringComparer.OrdinalIgnoreCase) &&
                    !placeholderSubgroup.Paths.Any(x => x.OldPath == path.Source))
                {
                    var placeholderPath = _remappedPathFactory(this);
                    placeholderPath.OldPath = path.Source;
                    placeholderPath.AcceptRenaming = false;
                    placeholderPath.ShowCreateSubgroupOption = false;

                    var fileName = Path.GetFileName(path.Source);

                    if (NewPathsByFileName.ContainsKey(fileName))
                    {
                        placeholderPath.CandidateNewPaths = NewPathsByFileName[fileName];
                    }
                    else
                    {
                        placeholderPath.CandidateNewPaths.Add(path.Source);
                    }
                    placeholderSubgroup.Paths.Add(placeholderPath);
                }
            }
            if (placeholderSubgroup.Paths.Any())
            {
                DeprecatedPathSubgroups.Add(placeholderSubgroup);
            }
        }
    }

    public class MultimappedSubgroup : VM
    {
        public VM_SubgroupPlaceHolder SourceSubgroup { get; set; }
        public ObservableCollection<MultimappedTexture> MultimappedTextures { get; set; } = new();
    }

    public class MultimappedTexture : VM
    {
        public string OrigPath { get; set; }
        public ObservableCollection<string> NewPaths { get; set; } = new();
    }
    private void CheckTexturePathConflicts()
    {
        var allReplacements = SubgroupsRemappedByHash.And(SubgroupsRemappedByPathPrediction).ToList();

        foreach (var subgroup in allReplacements)
        {
            foreach (var otherSubgroup in allReplacements.Where(x => x != subgroup).ToList())
            {
                if (subgroup.SourceSubgroup != otherSubgroup.SourceSubgroup)
                {
                    continue;
                }

                foreach (var path in subgroup.Paths)
                {
                    if (!path.AcceptRenaming)
                    {
                        continue;
                    }

                    foreach (var otherPath in otherSubgroup.Paths)
                    {
                        if (path.OldPath != otherPath.OldPath ||
                            path.NewPath == otherPath.NewPath ||
                            otherPath.AcceptRenaming)
                        {
                            continue;
                        }

                        var existingRecord = MultimappedSubgroups.Where(x => x.SourceSubgroup == subgroup.SourceSubgroup).FirstOrDefault();
                        if (existingRecord == null)
                        {
                            existingRecord = new() { SourceSubgroup = subgroup.SourceSubgroup };
                            MultimappedSubgroups.Add(existingRecord);
                        }

                        var existingTexture = existingRecord.MultimappedTextures.Where(x => x.OrigPath == path.OldPath).FirstOrDefault();
                        if (existingTexture == null)
                        {
                            existingTexture = new() { OrigPath = path.OldPath };
                            existingTexture.NewPaths.Add(path.NewPath);
                        }
                        existingTexture.NewPaths.Add(otherPath.NewPath);
                    }
                }
            }
        }
    }

    public class RemappedSubgroup : VM
    {
        public RemappedSubgroup(VM_SubgroupPlaceHolder subgroup)
        {
            SourceSubgroup = subgroup;
        }
        public VM_SubgroupPlaceHolder SourceSubgroup { get; }
        public ObservableCollection<RemappedPath> Paths { get; set; } = new();
        public bool IsVisible { get; set; } = true;

        public bool SearchMatches(string subgroupSearchStr, bool subgroupCaseSensitive, string pathSearchStr, bool pathCaseSensitive)
        {
            bool subgroupNeedsSearch = !subgroupSearchStr.IsNullOrEmpty();
            bool subgroupSearchMatches = subgroupCaseSensitive && SourceSubgroup.ExtendedName.Contains(subgroupSearchStr) ||
                !subgroupCaseSensitive && SourceSubgroup.ExtendedName.Contains(subgroupSearchStr, StringComparison.OrdinalIgnoreCase);

            bool pathNeedsSearch = !pathSearchStr.IsNullOrEmpty();
            bool pathSearchMatches = false;

            if (pathNeedsSearch)
            {
                foreach (var path in Paths)
                {
                    if (pathCaseSensitive && (path.OldPath.Contains(pathSearchStr) || path.NewPath.Contains(pathSearchStr)) ||
                        !pathCaseSensitive && (path.OldPath.Contains(pathSearchStr, StringComparison.OrdinalIgnoreCase) || path.NewPath.Contains(pathSearchStr, StringComparison.OrdinalIgnoreCase)))
                    {
                        pathSearchMatches = true;
                        break;
                    }
                }
            }

            if ((!subgroupNeedsSearch || subgroupSearchMatches) && (!pathNeedsSearch || pathSearchMatches))
            {
                return true;
            }
            return false;
        }
    }

    public class RemappedPath : VM
    {
        public delegate RemappedPath Factory(VM_ConfigPathRemapper parentVM);
        public RemappedPath(IEnvironmentStateProvider environmentStateProvider, VM_ConfigPathRemapper parentVM)
        {
            this.WhenAnyValue(x => x.CreateSubgroupFrom).Subscribe(x =>
            {
                if(x)
                {
                    AcceptRenaming = false;
                }
            }).DisposeWith(this);

            this.WhenAnyValue(x => x.AcceptRenaming).Subscribe(x =>
            {
                if (x)
                {
                    CreateSubgroupFrom = false;
                }
            }).DisposeWith(this);

            this.WhenAnyValue(x => x.OldPath, y => y.NewPath).Subscribe(z =>
            {
                if (z.Item1.EndsWith(".dds", StringComparison.OrdinalIgnoreCase) || z.Item2.EndsWith(".dds", StringComparison.OrdinalIgnoreCase))
                {
                    CanShowComparison = true;
                }
                else
                {
                    CanShowComparison = false;
                }
            }).DisposeWith(this);

            ShowComparison = new RelayCommand(
                canExecute: _ => CanShowComparison,
                execute: _ =>
                {
                    var oldPathFull = Path.Combine(environmentStateProvider.DataFolderPath, OldPath);
                    var newPathFull = Path.Combine(parentVM.NewAssetDirectory, NewPath);

                    var comparisonVM = new VM_ConfigRemapperTextureComparer(oldPathFull, newPathFull);
                    var comparisonWindow = new Window_ConfigRemapperTextureComparer();
                    comparisonWindow.DataContext = comparisonVM;
                    comparisonWindow.ShowDialog();
                });
        }
        public string OldPath { get; set; } = string.Empty;
        public string NewPath { get; set; } = string.Empty;
        public bool AcceptRenaming { get; set; } = true;
        public ObservableCollection<string> CandidateNewPaths { get; set;} = new();
        public bool ShowCreateSubgroupOption { get; set; } = true;
        public bool CreateSubgroupFrom { get; set; } = false;

        public RelayCommand ShowComparison { get; set; }
        public bool CanShowComparison { get; set; } = false;
    }

    public class SelectableFilePath : VM
    {
        public string File { get; set; } = string.Empty;
        public bool CreateSubgroupFromFile { get; set; } = false;

        public bool IsVisible { get; set; } = true;
        public void Refresh(string searchStr, bool caseSensitive)
        {
            bool needsSearch = !searchStr.IsNullOrEmpty();
            bool matchesSearch = caseSensitive && File.Contains(searchStr) ||
                !caseSensitive && File.Contains(searchStr, StringComparison.OrdinalIgnoreCase);

            IsVisible = !needsSearch || matchesSearch;
        }

        public SelectableFilePath(string file, bool createSubgroupFromFile)
        {
            File = file;
            CreateSubgroupFromFile = createSubgroupFromFile;
        }
    }
}

public enum PathMatchModeHash 
{
    Shortest,
    Similar
}

public interface IConfigRemapperSubVM
{
    public void Refresh(string subgroupSearchStr, bool subgroupCaseSensitive, string pathSearchStr, bool pathCaseSensitive);
}
