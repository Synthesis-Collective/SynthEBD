using DynamicData;
using DynamicData.Binding;
using Microsoft.Build.Tasks.Deployment.ManifestUtilities;
using Noggog;
using ReactiveUI;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows;
using static Mutagen.Bethesda.Plugins.Binary.Processing.BinaryFileProcessor;
using static SynthEBD.VM_CollectionMemberStringCheckboxList;

namespace SynthEBD;

public class VM_ConfigDrafter : VM
{
    private readonly ConfigDrafter _configDrafter;
    private readonly IEnvironmentStateProvider _environmentProvider;
    private readonly PatcherState _patcherState;
    private readonly VM_DrafterArchiveContainer.Factory _archiveContainerFactory;
    private readonly VM_7ZipInterface.Factory _7ZipInterfaceVM;

    public VM_ConfigDrafter(ConfigDrafter configDrafter, IEnvironmentStateProvider environmentProvider, PatcherState patcherState, VM_DrafterArchiveContainer.Factory archiveContainerFactory, VM_7ZipInterface.Factory sevenZipInterfaceVM)
    {
        _configDrafter = configDrafter;
        _environmentProvider = environmentProvider;
        _patcherState = patcherState;
        _archiveContainerFactory = archiveContainerFactory;
        _7ZipInterfaceVM = sevenZipInterfaceVM;

        canExecuteDrafting = this.WhenAnyValue(x => x.NotYetDrafted);
        this.WhenAnyValue(x => x.MutlipletHandlingSelection).Subscribe(selection =>
        {
            switch(selection)
            {
                case _multipletHandlingReplace: MultipletHandlingMode = MultipletHandlingMode.Replace; break;
                case _multipletHandlingIgnore: MultipletHandlingMode = MultipletHandlingMode.Ignore; break;
            }
        }).DisposeWith(this);

        _categorizePaths = () =>
        {
            UnmatchedTextures.Clear();
            if (ValidateExistingDirectories())
            {
                var searchDirs = SelectedTextureFolders.Select(x => x.DirPath).ToList();
                var texturePaths = _configDrafter.GetDDSFiles(searchDirs);
                (_categorizedTexturePaths, _uncategorizedTexturePaths) = _configDrafter.CategorizeFiles(texturePaths);
                if (_uncategorizedTexturePaths.Any())
                {
                    HasUnmatchedTextures = true;
                    foreach (var path in _uncategorizedTexturePaths.Where(x => !IgnoredPaths.Contains(x))) // ignore paths that were already ignored due to being multiplets
                    {
                        UnmatchedTextures.Add(new(_configDrafter.RemoveRootFolder(path, searchDirs, !IsUsingModManager), UnmatchedTextures));
                    }
                }
                else
                {
                    HasUnmatchedTextures = false;
                }
            }
        };

        SelectAllUncategorizedButton = new RelayCommand(
            canExecute: _ => true,
            execute: _ =>
            {
                foreach (var path in UnmatchedTextures)
                {
                    path.IsSelected = true;
                }
            });

        DeselectAllUncategorizedButton = new RelayCommand(
            canExecute: _ => true,
            execute: _ =>
            {
                foreach (var path in UnmatchedTextures)
                {
                    path.IsSelected = false;
                }
            });

        duplicateCheckProgress = new(report =>
        {
            HashingProgressCurrent = report.Item1;
            HashingProgressMax = report.Item2;
            CurrentlyHashingFile = report.Item3;
        });

        CheckDuplicatesButton = ReactiveCommand.CreateFromTask(
            execute: async _ =>
            {
                if (ValidateExistingDirectories())
                {
                    MultipletTextureGroups.Clear();
                    var searchDirs = SelectedTextureFolders.Select(x => x.DirPath).ToList();
                    var texturePaths = _configDrafter.GetDDSFiles(searchDirs);
                    var multiples =  await Task.Run(async () => ComputeFileDuplicates(texturePaths, duplicateCheckProgress, SelectedTextureFolders.Select(x => x.DirPath).ToList(), IsUsingModManager, configDrafter));
                    Noggog.ListExt.AddRange(MultipletTextureGroups, multiples.Result);
                    HasMultiplets = multiples.Result.Any();
                    HashingProgressCurrent = 0;
                    CurrentlyHashingFile = string.Empty;
                }
            });

        RemoveDuplicatesButton = new RelayCommand(
           canExecute: _ => true,
           execute: _ =>
           {
               switch(MultipletHandlingMode)
               {
                   case MultipletHandlingMode.Ignore:
                       foreach (var multiplet in MultipletTextureGroups)
                       {
                           multiplet.ToIgnoreList(IgnoredPaths);
                           multiplet.RemoveSelected();
                       }
                       break;

                   case MultipletHandlingMode.Replace:
                       if (!PreVerifyMultiplets(MultipletTextureGroups, out string failedChecks))
                       {
                           MessageWindow.DisplayNotificationOK("Please Fix Duplicate File Selections", failedChecks);
                       }
                       else
                       {
                           foreach (var multiplet in MultipletTextureGroups)
                           {
                               MultipletDTOs.Add(multiplet.ToMultiplet());
                               multiplet.RemoveSelected();
                           }
                       }
                       break;
               }
               
           });

        ShowDuplicatesPopupButton = new RelayCommand(
           canExecute: _ => true,
           execute: _ =>
           {
               List<string> toShow = new();
               foreach (var multiplet in MultipletTextureGroups)
               {
                   toShow.Add(multiplet.FileName);
                   foreach (var file in multiplet.FilePaths)
                   {
                       string toAppend = "\t" + file.DisplayedPath;
                       if (!file.IsSelected)
                       {
                           toAppend += " [KEEP]";
                       }
                       toShow.Add(toAppend);
                   }
                   toShow.Add("");
               }

               MessageWindow.DisplayNotificationOK("Press the white button to copy list", string.Join(Environment.NewLine, toShow));
           });

        DraftConfigButton = ReactiveCommand.CreateFromTask(
            execute: async _ =>
            {
                HashSet<string> unmatchedTextures = new();

                if (ValidateExistingDirectories())
                {
                    Noggog.ListExt.AddRange(IgnoredPaths, UnmatchedTextures.Where(x => !x.IsSelected).Select(x => x.Content).ToArray());

                    if (HasMultiplets && MultipletHandlingMode == MultipletHandlingMode.Replace && !PreVerifyMultiplets(MultipletTextureGroups, out string failedChecks))
                    {
                        MessageWindow.DisplayNotificationOK("Please Fix Duplicate File Selections", failedChecks);
                        return;
                    }

                    if (!CurrentConfig.RaceGroupingEditor.RaceGroupings.Any())
                    {
                        CurrentConfig.RaceGroupingEditor.ImportFromGeneralSettings();
                    }

                    if (!CurrentConfig.AttributeGroupMenu.Groups.Any())
                    {
                        CurrentConfig.AttributeGroupMenu.ImportFromGeneralSettings();
                    }

                    IgnoredPaths.AddRange(_existingFilePaths); // for upgrading config

                    var status = _configDrafter.DraftConfigFromTextures(CurrentConfig, _categorizedTexturePaths, _uncategorizedTexturePaths, IgnoredPaths, MultipletDTOs, MultipletHandlingMode, SelectedTextureFolders.Select(x => x.DirPath).ToList(), !IsUsingModManager, AutoApplyNames, AutoApplyRules, AutoApplyLinkage, out bool hasTNGTextures, out bool hasEtcTextures);

                    if (status == _configDrafter.SuccessString)
                    {
                        if (CurrentConfig.GroupName.IsNullOrWhitespace() || CurrentConfig.GroupName == VM_AssetPack._defaultGroupName)
                        {
                            CurrentConfig.GroupName = GeneratedModName.IsNullOrWhitespace() ? "New Asset Pack" : GeneratedModName;
                        }
                        if (SelectedFileArchives.Any() && (CurrentConfig.ShortName.IsNullOrWhitespace() || CurrentConfig.ShortName == VM_AssetPack._defaultPrefix))
                        {
                            CurrentConfig.ShortName = SelectedFileArchives.First().Prefix;
                        }
                        NotYetDrafted = false;
                        HasEtcTextures = hasEtcTextures;
                        HasTNGTextures = hasTNGTextures;

                        if(IsUpgrade)
                        {
                            var currentSubgroups = CurrentConfig.GetAllSubgroups();
                            var addedSubgroups = currentSubgroups.Where(sg => !_existingSubgroups.Contains(sg)).ToList();

                            string message;
                            if (addedSubgroups.Any())
                            {
                                List<string> messages = new() { "Subgroups Added:" };
                                messages.AddRange(addedSubgroups.Select(x => x.ID + ": " + x.GetNameChain(" -> ")));
                                message = string.Join(Environment.NewLine, messages);
                            }
                            else
                            {
                                message = "No new subgroups were added";
                            }

                            MessageWindow.DisplayNotificationOK("Config File Update", message);
                        }
                    }
                    else
                    {
                        MessageWindow.DisplayNotificationOK("Error drafting config file", status);
                    }
                }
            }, canExecuteDrafting);

        ExtractArchivesButton = ReactiveCommand.CreateFromTask(
            execute: async _ =>
            {
                HashSet<string> unmatchedTextures = new();

                if (await ValidateContainers())
                {
                    var destinationDirs = await ExtractArchives();
                    if (destinationDirs.Any())
                    {
                        SelectedTextureFolders.Clear();

                        if (IsUsingModManager)
                        {
                            SelectedTextureFolders.Add(new(SelectedTextureFolders, _categorizePaths) { DirPath = Path.Combine(_patcherState.ModManagerSettings.CurrentInstallationFolder, GeneratedModName) });
                        }
                        else
                        {
                            foreach (var dir in destinationDirs)
                            {
                                SelectedTextureFolders.Add(new(SelectedTextureFolders, _categorizePaths) { DirPath = dir });
                            }
                        }

                        SelectedFileArchives.Clear();
                        SelectedSource = DrafterTextureSource.Directories;

                        _categorizePaths();
                    }
                }
            });

        AddFileArchiveButton = new RelayCommand(
            canExecute: _ => true,
            execute: _ =>
            {
                var newArchive = _archiveContainerFactory();
                if (SelectedFileArchives.Any() && !SelectedFileArchives.First().Prefix.IsNullOrWhitespace())
                {
                    newArchive.Prefix = SelectedFileArchives.First().Prefix;
                }
                SelectedFileArchives.Add(newArchive);
            });

        AddDirectoryButton = new RelayCommand(
            canExecute: _ => true,
            execute: _ =>
            {
                SelectedTextureFolders.Add(new(SelectedTextureFolders, _categorizePaths));
            });
    }

    public VM_AssetPack CurrentConfig { get; set; }

    public string GeneratedModName { get; set; }
    public bool IsUsingModManager { get; set; }
    public bool LockGeneratedModName { get; set; } // referenced by View for locking textbox
    public string ModNameToolTip { get; set; }
    public bool AutoApplyNames { get; set; } = true;
    public bool AutoApplyRules { get; set; } = true;
    public bool AutoApplyLinkage { get; set; } = true;

    public bool NotYetDrafted { get; set; } = true;
    IObservable<bool> canExecuteDrafting { get; }

    public DrafterTextureSource SelectedSource { get; set; } = DrafterTextureSource.Archives;
    public ObservableCollection<VM_DrafterArchiveContainer> SelectedFileArchives { get; set; } = new();
    public ObservableCollection<VM_SelectableDirectoryWrapper> SelectedTextureFolders { get; set; } = new();

    public ObservableCollection<VM_SimpleSelectableCollectionMemberString> UnmatchedTextures { get; set; } = new();
    public bool HasUnmatchedTextures { get; set; } = false;
    private List<string> _categorizedTexturePaths = new();
    private List<string> _uncategorizedTexturePaths = new();
    private Action _categorizePaths { get; }
    public RelayCommand SelectAllUncategorizedButton { get; }
    public RelayCommand DeselectAllUncategorizedButton { get; }

    public bool HasEtcTextures { get; set; } = false;
    public DrafterBodyType SelectedBodyType { get; set; }
    public bool HasTNGTextures { get; set; } = false;

    public IReactiveCommand CheckDuplicatesButton { get; }
    public ObservableCollection<VM_FileDuplicateContainer> MultipletTextureGroups { get; set; } = new();
    public bool HasMultiplets { get; set; } = false;
    public List<Multiplet> MultipletDTOs { get; set; } = new();
    private List<string> IgnoredPaths { get; set; } = new();
    public RelayCommand RemoveDuplicatesButton { get; }
    public RelayCommand ShowDuplicatesPopupButton { get; }
    public MultipletHandlingMode MultipletHandlingMode { get; set; } = MultipletHandlingMode.Replace;
    private const string _multipletHandlingReplace = "Replace With Primary";
    private const string _multipletHandlingIgnore = "Ignore Non-Primary";
    public ObservableCollection<string> MultipletHandlingOptions { get; set; } = new() { _multipletHandlingReplace, _multipletHandlingIgnore };
    public string MutlipletHandlingSelection { get; set; } = _multipletHandlingReplace;

    public string CurrentlyHashingFile { get; set; } = String.Empty;
    public int HashingProgressCurrent { get; set; } = 0;
    public int HashingProgressMax { get; set; } = 1;
    private Progress<(int, int, string)> duplicateCheckProgress { get; }

    public IReactiveCommand DraftConfigButton { get; }
    public IReactiveCommand ExtractArchivesButton { get; }

    public RelayCommand AddDirectoryButton { get; }
    public RelayCommand AddFileArchiveButton { get; }
    private List<string> _existingFilePaths { get; set; } = new(); // for upgrading config
    private List<VM_SubgroupPlaceHolder> _existingSubgroups { get; set; } = new(); // for upgrading config
    public bool IsUpgrade { get; set; } = false;
    public void InitializeTo(VM_AssetPack config)
    {
        CurrentConfig = config;

        IsUsingModManager = (_patcherState.ModManagerSettings.ModManagerType == ModManager.ModOrganizer2 && !_patcherState.ModManagerSettings.MO2Settings.ModFolderPath.IsNullOrWhitespace() && Directory.Exists(_patcherState.ModManagerSettings.MO2Settings.ModFolderPath)) ||
            (_patcherState.ModManagerSettings.ModManagerType == ModManager.Vortex && !_patcherState.ModManagerSettings.VortexSettings.StagingFolderPath.IsNullOrWhitespace() && Directory.Exists(_patcherState.ModManagerSettings.VortexSettings.StagingFolderPath));

        LockGeneratedModName = !IsUsingModManager;

        if (LockGeneratedModName)
        {
            GeneratedModName = "You can only select a mod name if you have filled out your mods folder in the Mod Manager Integration Settings";
            ModNameToolTip = "Textures will be extracted to your Data folder (or overwrite if using MO2)";
        }
        else
        {
            GeneratedModName = String.Empty;
            ModNameToolTip = "A new mod with this name will appear in your mod manager's mod list after drafting";
        }

        if (!SelectedFileArchives.Any())
        {
            SelectedFileArchives.Add(_archiveContainerFactory());
        }

        if (!SelectedTextureFolders.Any())
        {
            SelectedTextureFolders.Add(new(SelectedTextureFolders, _categorizePaths));
            _categorizedTexturePaths.Clear();
            _uncategorizedTexturePaths.Clear();
            UnmatchedTextures.Clear();
            HasUnmatchedTextures = false;
        }
        else
        {
            _categorizePaths();
        }

        HasEtcTextures = false;
        HasTNGTextures = false;
        HasMultiplets = false;
        HashingProgressCurrent = 0;
        CurrentlyHashingFile = String.Empty;
        NotYetDrafted = true;
        IgnoredPaths.Clear();
        MultipletTextureGroups.Clear();
        MultipletDTOs.Clear();

        var allSubgroups = config.GetAllSubgroups();
        _existingSubgroups.Clear();
        _existingSubgroups.AddRange(allSubgroups);

        _existingFilePaths.Clear();
        _existingFilePaths.AddRange(allSubgroups.SelectMany(subgroup => subgroup.AssociatedModel.Paths).Select(pathVM => pathVM.Source));

        IsUpgrade = _existingFilePaths.Any();
    }

    private bool ValidateExistingDirectories()
    {
        foreach (var selection in SelectedTextureFolders)
        {
            if (!ValidateExistingDirectory(selection.DirPath))
            {
                return false;
            }
        }
        return true;
    }
    public bool ValidateExistingDirectory(string directory)
    {
        if (!Directory.Exists(directory))
        {
            MessageWindow.DisplayNotificationOK("Drafter Error", "The selected mod directory does not exist: " + directory);
            return false;
        }

        if (IsUsingModManager)
        {
            var texturesDir = Path.Combine(directory, "Textures");
            if (!Directory.Exists(texturesDir))
            {
                MessageWindow.DisplayNotificationOK("Drafter Error", "The selected mod directory must contain a Textures folder: " + directory);
                return false;
            }
            if (Directory.GetDirectories(texturesDir).Length < 1)
            {
                MessageWindow.DisplayNotificationOK("Drafter Error", "The selected mod directory must contain a Textures\\* folder: " + directory);
                return false;
            }
        }
        return true;
    }

    public async Task<bool> ValidateContainers()
    {
        if (!SelectedFileArchives.Any())
        {
            MessageWindow.DisplayNotificationOK("Drafter Error", "No file archives were selected");
            return false;
        }

        if (GeneratedModName.IsNullOrWhitespace())
        {
            MessageWindow.DisplayNotificationOK("Drafter Error", "You must enter a name for the mod to which the files will be extracted. Recommend something like \"SynthEBD - This Texture Mod\"");
            return false;
        }

        List<string> fileNames = new();
        List<List<string>> archiveContents = new();

        for (int i = 0; i < SelectedFileArchives.Count; i++)
        {
            var container = SelectedFileArchives[i];

            if (fileNames.Contains(container.FilePath))
            {
                MessageWindow.DisplayNotificationOK("Drafter Error", "Cannot select the same file multiple times: " + Environment.NewLine + container.FilePath);
                return false;
            }
            fileNames.Add(container.FilePath);

            if (!File.Exists(container.FilePath))
            {
                MessageWindow.DisplayNotificationOK("Drafter Error", "The following file from Archive #" + (i + 1).ToString() + " does not exist: " + Environment.NewLine + container.FilePath);
                return false;
            }
            if (container.Prefix.IsNullOrWhitespace())
            {
                MessageWindow.DisplayNotificationOK("Drafter Error", "You need to assign a prefix to Archive #" + (i + 1).ToString());
                return false;
            }

            var currentArchiveContents = await _7ZipInterfaceVM().GetArchiveContents(container.FilePath, true, _patcherState.GeneralSettings.Close7ZipWhenFinished, 1000);
            if (!currentArchiveContents.Any())
            {
                MessageWindow.DisplayNotificationOK("Drafter Error", "The following file has no contents upon extraction: " + Environment.NewLine + container.FilePath);
                return false;
            }
            foreach (var path in currentArchiveContents)
            {
                for (int j = 0; j < archiveContents.Count; j++)
                {
                    if (archiveContents[j].Contains(path) && container.Prefix == SelectedFileArchives[j].Prefix)
                    {
                        MessageWindow.DisplayNotificationOK("Drafter Error", "The following file in Archive #" + (i + 1).ToString() + " was also found in Archive # " + (j + 1).ToString() + ":" + Environment.NewLine + path + Environment.NewLine + "You must assign a different Prefix to one of these archives");
                        return false;
                    }
                }
            }
            archiveContents.Add(currentArchiveContents);
        }

        return true;
    }

    public async Task<List<string>> ExtractArchives()
    {
        List<string> destinationDirs = new();
        var destinationDir = "";
        foreach (var archiveFile in SelectedFileArchives)
        {
            if (_patcherState.ModManagerSettings.ModManagerType == ModManager.None)
            {
                destinationDir = Path.Combine(_environmentProvider.DataFolderPath, "Textures", archiveFile.Prefix);
            }
            else
            {
                destinationDir = Path.Combine(_patcherState.ModManagerSettings.CurrentInstallationFolder, GeneratedModName, "Textures", archiveFile.Prefix);
            }

            if (!destinationDirs.Contains(destinationDir))
            {
                destinationDirs.Add(destinationDir);
            }
            var succes = await _7ZipInterfaceVM().ExtractArchive(archiveFile.FilePath, destinationDir, true, _patcherState.GeneralSettings.Close7ZipWhenFinished, 1000);
        }
        return destinationDirs;
    }
    // 
    public static async Task<ObservableCollection<VM_FileDuplicateContainer>> ComputeFileDuplicates(List<string> texturePaths, IProgress<(int, int,string)> progress, List<string> selectedTextureFolders, bool isUsingModManager, ConfigDrafter configDrafter)
    {
        ObservableCollection<VM_FileDuplicateContainer> multipletTextureGroups = new();

        var texturesByFileName = texturePaths.GroupBy(x => x.Split(Path.DirectorySeparatorChar).Last().ToLower());

        int currentGroupingIndex = 0;
        int maxGroupings = texturesByFileName.Count();

        foreach (var fileGrouping in texturesByFileName)
        {
            progress.Report((currentGroupingIndex, maxGroupings, fileGrouping.Key));

            var checksumGroupings = fileGrouping.GroupBy(x => MiscFunctions.CalculateMD5(x)).ToArray();
            int groupCount = 0;
            foreach (var sharedChecksumGroup in checksumGroupings.Where(group => group.Count() > 1).ToArray())
            {
                groupCount++;

                var multiplet = new VM_FileDuplicateContainer();
                multiplet.FileName = fileGrouping.Key;
                if (checksumGroupings.Count() > 1)
                {
                    multiplet.FileName += " (Group " + groupCount + ")";
                }

                foreach (var filePath in sharedChecksumGroup)
                {
                    multiplet.FilePaths.Add(new(filePath, multiplet.FilePaths, selectedTextureFolders, !isUsingModManager, configDrafter) { IsSelected = true });
                }
                if (multiplet.FilePaths.Any())
                {
                    configDrafter.ChooseLeastSpecificPath(multiplet.FilePaths); // uncheck the best candidate
                    multipletTextureGroups.Add(multiplet);
                }
            }
            
            currentGroupingIndex++;
        }

        return multipletTextureGroups;
    }

    private bool PreVerifyMultiplets(IEnumerable<VM_FileDuplicateContainer> multiples, out string failureNames)
    {
        var failedChecks = multiples.Where(multiplet => multiplet.FilePaths.Where(texture => !texture.IsSelected).Count() != 1).ToList();
        if (failedChecks.Any())
        {
            failureNames = "When handling duplicate files by replacement, each group of replicate textures must have exactly ONE texture unchecked to serve as the source texture. The following textures need correction:" + Environment.NewLine + Environment.NewLine + String.Join(Environment.NewLine, failedChecks.Select(x => x.FileName));
            return false;
        }
        failureNames = String.Empty;
        return true;
    }
}

public class VM_FileDuplicateContainer : VM
{
    public string FileName { get; set; }
    public ObservableCollection<VM_FileMultiplet> FilePaths { get; set; } = new();

    public class VM_FileMultiplet : VM
    {
        public VM_FileMultiplet(string fullPath, ObservableCollection<VM_FileMultiplet> parentCollection, List<string> rootPaths, bool trimPrefix, ConfigDrafter configDrafter)
        {
            ParentCollection = parentCollection;
            FullPath = fullPath;

            DisplayedPath = configDrafter.RemoveRootFolder(FullPath, rootPaths, trimPrefix);

            DeleteCommand = new RelayCommand(canExecute: _ => true, execute: _ => ParentCollection.Remove(this));
        }
        public string DisplayedPath { get; set; }
        public string FullPath { get; set; }
        public ObservableCollection<VM_FileMultiplet> ParentCollection { get; }
        public bool IsSelected { get; set; } = false;
        public RelayCommand DeleteCommand { get; }
    }

    public Multiplet ToMultiplet() // checking for single deselected option must come from caller
    {
        var primary = FilePaths.Where(x => !x.IsSelected).First();
        return new Multiplet
        {
            PrimaryPath = primary.DisplayedPath,
            ReplicatePaths = FilePaths.Where(x => x != primary).Select(y => y.DisplayedPath).ToList()
        };
    }

    public void ToIgnoreList(List<string> ignoredPaths) // can accomodate multiple or no de-selected options
    {
        foreach (var pathVM in FilePaths.Where(x => x.IsSelected).ToArray())
        {
            ignoredPaths.Add(pathVM.FullPath);
        }
    }
    
    public void RemoveSelected()
    {
        for (int i = 0; i < FilePaths.Count; i++)
        {
            if (FilePaths[i].IsSelected)
            {
                FilePaths.RemoveAt(i);
                i--;
            }
        }
    }
}

public class VM_DrafterArchiveContainer : VM
{
    public delegate VM_DrafterArchiveContainer Factory();
    private readonly VM_ConfigDrafter _configDrafter;
    public VM_DrafterArchiveContainer(VM_ConfigDrafter drafter)
    {
        _configDrafter = drafter;

        SelectArchive = new RelayCommand(
            canExecute: _ => true,
            execute: _ =>
            {
                if (IO_Aux.SelectFile("", "Archive Files (*.7z;*.zip;*.rar)|*.7z;*.zip;*.rar|" + "All files (*.*)|*.*", "Select a zipped archive", out string path))
                {
                    FilePath = path;
                }
            });

        DeleteMe = new RelayCommand(
            canExecute: _ => true,
            execute: _ =>
            {
                _configDrafter.SelectedFileArchives.Remove(this);
            });
    }
    public string FilePath { get; set; }
    public string Prefix { get; set; }
    public RelayCommand SelectArchive { get; }
    public RelayCommand DeleteMe { get; }
}

public class VM_SelectableDirectoryWrapper : VM
{
    public VM_SelectableDirectoryWrapper(ObservableCollection<VM_SelectableDirectoryWrapper> parentCollection, Action categorizePaths)
    {
        _parentCollection = parentCollection;

        SelectPath = new RelayCommand(
            canExecute: _ => true,
            execute: _ =>
            {
                if (IO_Aux.SelectFolder("", out string path))
                {
                    DirPath = path;
                    categorizePaths();
                }
            });

        DeleteMe = new RelayCommand(
            canExecute: _ => true,
            execute: _ =>
            {
                _parentCollection.Remove(this);
                categorizePaths();
            });
    }

    public string DirPath { get; set; }
    public RelayCommand SelectPath { get; }
    public RelayCommand DeleteMe { get; }
    private ObservableCollection<VM_SelectableDirectoryWrapper> _parentCollection { get; }
}

public enum DrafterTextureSource
{
    Archives,
    Directories
}

public enum DrafterBodyType
{
    CBBE_3BA,
    BHUNP
}

public class BoolToSolidColorBrushConverter : IValueConverter
{
    public SolidColorBrush TrueColor { get; set; }
    public SolidColorBrush FalseColor { get; set; }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool boolValue)
        {
            return boolValue ? TrueColor : FalseColor;
        }

        return DependencyProperty.UnsetValue; // Return an unset value if the input is not a bool
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException(); // You don't need this for one-way conversion
    }
}

