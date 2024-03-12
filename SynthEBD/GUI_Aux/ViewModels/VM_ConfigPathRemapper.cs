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

namespace SynthEBD
{
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
                    UpdatedSubgroups.Clear();
                    await Task.Run(() => ComputePathHashes(_hashingProgress));
                    if (_missingCurrentPaths.Any())
                    {
                        CreateMissingPathsObjects(_missingCurrentPaths.ToList());
                    }
                    RemapPathsByHash();
                    ShowProgressEndMessage = false;
                    ShowProgressBar = false;
                    ShowMissingSubgroups = MissingPathSubgroups.Any();
                    if (UpdatedSubgroups.Any())
                    {
                        ShowRemappedByHashList = true;
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
        public ObservableCollection<RemappedSubgroup> UpdatedSubgroups { get; set; } = new();
        private List<string> _missingCurrentPaths { get; set; } = new();
        public ObservableCollection<RemappedSubgroup> MissingPathSubgroups { get; set; } = new();
        public bool ShowMissingSubgroups { get; set; } = false;
        public bool ShowProgressBar { get; set; } = false;
        public bool ShowProgressDigits { get; set; } = false;
        public bool ShowProgressEndMessage { get; set; } = false;
        public bool ShowRemappedByHashList { get; set; } = false;

        private ConcurrentDictionary<string, string> _currentPathHashes { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        private ConcurrentDictionary<string, string> _newPathHashes { get; set; } = new(StringComparer.OrdinalIgnoreCase);

        private async Task ComputePathHashes(IProgress<int> progress)
        {
            ShowProgressBar = true;
            ShowProgressDigits = true;
            var currentFiles = _parentAssetPack.GetAllSubgroups().SelectMany(x => x.AssociatedModel.Paths).Select(x => Path.Combine(_environmentStateProvider.DataFolderPath, x.Source)).ToList();
            var newFiles = Directory.GetFiles(NewAssetDirectory, "*", SearchOption.AllDirectories);
            ProgressMax = currentFiles.Count + newFiles.Length;
            _progressCurrent = 0;

            var missingFiles = new ConcurrentBag<string>();

            Parallel.ForEach(currentFiles, path =>
            {
                if (File.Exists(path))
                {
                    var hash = MiscFunctions.CalculateMD5(path);
                    if (!hash.IsNullOrWhitespace())
                    {
                        _currentPathHashes.TryAdd(path, hash);
                    }
                }
                else
                {
                    missingFiles.Add(Path.GetRelativePath(_environmentStateProvider.DataFolderPath, path));
                }
                progress.Report(1);
            });

            Parallel.ForEach(newFiles, path =>
            {
                if (File.Exists(path))
                {
                    var hash = MiscFunctions.CalculateMD5(path);
                    if (!hash.IsNullOrWhitespace())
                    {
                        _newPathHashes.TryAdd(path, hash);
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
                    var sourcePath = Path.Combine(_environmentStateProvider.DataFolderPath, path.Source);
                    if (_currentPathHashes.ContainsKey(sourcePath))
                    {
                        var currentHash = _currentPathHashes[sourcePath];
                        var matchingEntries = _newPathHashes.Where(x => x.Value.Equals(currentHash));
                        if (matchingEntries.Any())
                        {
                            var newSource = Path.GetRelativePath(NewAssetDirectory, matchingEntries.First().Key);
                            var recordEntry = new RemappedPath()
                            {
                                OldPath = path.Source,
                                NewPath = newSource
                            };
                            remappedHolder.Paths.Add(recordEntry);
                            path.Source = newSource;
                        }
                    }
                }

                if (remappedHolder.Paths.Any())
                {
                    subgroup.AssociatedModel.Paths = paths.ToHashSet();
                    UpdatedSubgroups.Add(remappedHolder);
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
        }
    }
}
