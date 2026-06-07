using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ReactiveUI;
using System.Windows.Media;
using Noggog;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;

namespace SynthEBD
{
    /// <summary>
    /// Packager editor node representing one <see cref="Manifest.Option"/> in the authored config tree (root or branch).
    /// Edits the option's resources (asset packs, BodyGen/record-template paths, downloads, patchable races, ignored
    /// source files, file-extension map, destination folder) and nested child options. Validates referenced paths
    /// against the manifest root directory (green/red border) and round-trips to/from <see cref="Manifest.Option"/>.
    /// </summary>
    public class VM_PackagerOption : VM
    {
        public string Name { get; set; } 
        public string Description { get; set; } = "";
        public ObservableCollection<VM_CollectionMemberStringDecorated> AssetPackPaths { get; set; } = new();
        public ObservableCollection<VM_CollectionMemberStringDecorated> RecordTemplatePaths { get; set; } = new();
        public ObservableCollection<VM_CollectionMemberStringDecorated> BodyGenConfigPaths { get; set; } = new();
        public ObservableCollection<FormKey> AddPatchableRaces { get; set; } = new();
        public ObservableCollection<VM_DownloadInfoContainer> DownloadInfo { get; set; } = new();
        public string OptionsDescription { get; set; } = "";
        public ObservableCollection<VM_PackagerOption> Options { get; set; } = new();
        public string DestinationModFolder { get; set; } = "";
        public ObservableCollection<string[]> FileExtensionMap { get; set; } = new();
        public ObservableCollection<VM_CollectionMemberString> IgnoreMissingSourceFiles { get; set; } = new();
        public RelayCommand AddNew { get; set; }
        public RelayCommand DeleteMe { get; set; }
        public RelayCommand AddFileExtensionMapping { get; set; }
        public RelayCommand RemoveFileExtensionMapping { get; set; }
        public RelayCommand AddAssetConfigFile { get; set; }
        public RelayCommand AddBodyGenConfigFile { get; set; }
        public RelayCommand AddRecordTemplateFile { get; set; }
        public RelayCommand AddDownloadInfo { get; set; }
        public RelayCommand AddIgnoredSourceFile { get; set; }
        public RelayCommand FindJSONFile { get; set; }
        public RelayCommand FindPluginFile { get; set; }
        public ObservableCollection<VM_PackagerOption> ParentCollection { get; set; }
        public VM_Manifest ParentManifest { get; set; }
        /// <summary>Type filter constraining the race picker to <see cref="Mutagen.Bethesda.Skyrim.IRaceGetter"/> records.</summary>
        public IEnumerable<Type> RacePickerFormKeys { get; } = typeof(Mutagen.Bethesda.Skyrim.IRaceGetter).AsEnumerable();
        public ILinkCache LinkCache { get; set; }

        /// <summary>Assigns an auto-generated "Root"/"Branch" name, stores parent links/link cache, wires the add/remove
        /// and file-picker commands, and subscribes to manifest root-directory changes to revalidate path borders.</summary>
        public VM_PackagerOption(ObservableCollection<VM_PackagerOption> parentCollection, VM_Manifest parentManifest, bool isRootNode, ILinkCache linkCache)
        {
            ParentCollection = parentCollection;
            ParentManifest = parentManifest;
            LinkCache = linkCache;

            if (isRootNode)
            {
                Name = "Root";
            }
            else
            {
                Name = "Branch";
            }
            Name += (parentCollection.Count + 1).ToString();

            AddNew = new RelayCommand(
                canExecute: _ => true,
                execute: _ => this.Options.Add(new VM_PackagerOption(Options, ParentManifest, false, LinkCache))
                );

            DeleteMe = new RelayCommand(
                canExecute: _ => true,
                execute: _ => this.ParentCollection.Remove(this)
                );

            AddFileExtensionMapping = new RelayCommand(
                canExecute: _ => true,
                execute: _ => this.FileExtensionMap.Add(new string[2])
                );

            RemoveFileExtensionMapping = new RelayCommand(
                canExecute: _ => true,
                execute: x => this.FileExtensionMap.Remove((string[])x)
                );

            AddAssetConfigFile = new RelayCommand(
                canExecute: _ => true,
                execute: _ =>
                {
                    AddEmptyPath(AssetPackPaths);
                });

            AddBodyGenConfigFile = new RelayCommand(
                canExecute: _ => true,
                execute: _ =>
                {
                    AddEmptyPath(BodyGenConfigPaths);
                });

            AddRecordTemplateFile = new RelayCommand(
                canExecute: _ => true,
                execute: _ =>
                {
                    AddEmptyPath(RecordTemplatePaths);
                });

            AddDownloadInfo = new RelayCommand(
                canExecute: _ => true,
                execute: _ =>
                {
                    DownloadInfo.Add(new VM_DownloadInfoContainer(this));
                });

            AddIgnoredSourceFile = new RelayCommand(
                canExecute: _ => true,
                execute: _ =>
                {
                    IgnoreMissingSourceFiles.Add(new("", IgnoreMissingSourceFiles));
                });

            FindJSONFile = new RelayCommand(
                canExecute: _ => true,
                execute: x =>
                {
                    SearchForPath((VM_CollectionMemberStringDecorated)x, "Config files (*.json)|*.json", "Select JSON to import");
                });

            FindPluginFile = new RelayCommand(
                canExecute: _ => true,
                execute: x =>
                {
                    SearchForPath((VM_CollectionMemberStringDecorated)x, "Plugin files (*.esp)|*.esp", "Select plugin to import");
                });

            this.WhenAnyValue(x => x.ParentManifest.RootDirectory).Subscribe(x =>
            {
                UpdatePathCollectionStatus(AssetPackPaths, ParentManifest.RootDirectory);
                UpdatePathCollectionStatus(BodyGenConfigPaths, ParentManifest.RootDirectory);
                UpdatePathCollectionStatus(RecordTemplatePaths, ParentManifest.RootDirectory);
            }).DisposeWith(this);
        }

        /// <summary>Recursively builds an editor node from a persisted <see cref="Manifest.Option"/>, including its
        /// downloads and child options, then refreshes path-validation borders against the root directory.</summary>
        public static VM_PackagerOption GetViewModelFromModel(Manifest.Option model, ObservableCollection<VM_PackagerOption> parentCollection, VM_Manifest parentManifest, ILinkCache linkCache)
        {
            VM_PackagerOption viewModel = new(parentCollection, parentManifest, false, linkCache);
            viewModel.Name = model.Name;
            viewModel.Description = model.Description;
            viewModel.AssetPackPaths = VM_CollectionMemberStringDecorated.InitializeObservableCollectionFromICollection(model.AssetPackPaths);
            viewModel.RecordTemplatePaths = VM_CollectionMemberStringDecorated.InitializeObservableCollectionFromICollection(model.RecordTemplatePaths);
            viewModel.BodyGenConfigPaths = VM_CollectionMemberStringDecorated.InitializeObservableCollectionFromICollection(model.BodyGenConfigPaths);
            foreach (var dlInfo in model.DownloadInfo)
            {
                viewModel.DownloadInfo.Add(VM_DownloadInfoContainer.GetViewModelFromModel(dlInfo, viewModel));
            }            
            viewModel.OptionsDescription = model.OptionsDescription;
            viewModel.DestinationModFolder = model.DestinationModFolder;
            foreach (var subOption in model.Options)
            {
                viewModel.Options.Add(VM_PackagerOption.GetViewModelFromModel(subOption, viewModel.Options, parentManifest, linkCache));
            }
            viewModel.AddPatchableRaces = new ObservableCollection<FormKey>(model.AddPatchableRaces);
            viewModel.IgnoreMissingSourceFiles = VM_CollectionMemberString.InitializeObservableCollectionFromICollection(model.IgnoreMissingSourceFiles);

            UpdatePathCollectionStatus(viewModel.AssetPackPaths, viewModel.ParentManifest.RootDirectory);
            UpdatePathCollectionStatus(viewModel.BodyGenConfigPaths, viewModel.ParentManifest.RootDirectory);
            UpdatePathCollectionStatus(viewModel.RecordTemplatePaths, viewModel.ParentManifest.RootDirectory);

            return viewModel;
        }

        /// <summary>Serializes this node (and recursively its child options) back into a <see cref="Manifest.Option"/> model.</summary>
        public Manifest.Option DumpViewModelToModel()
        {
            Manifest.Option model = new Manifest.Option();
            model.Name = Name;
            model.Description = Description;
            model.AssetPackPaths = AssetPackPaths.Select(x => x.Content).ToHashSet();
            model.RecordTemplatePaths = RecordTemplatePaths.Select(x => x.Content).ToHashSet();
            model.BodyGenConfigPaths = BodyGenConfigPaths.Select(x => x.Content).ToHashSet();
            foreach (var dlInfo in DownloadInfo)
            {
                model.DownloadInfo.Add(dlInfo.DumpViewModelToModel());
            }
            model.OptionsDescription = OptionsDescription;
            model.DestinationModFolder = DestinationModFolder;
            foreach (var entry in FileExtensionMap)
            {
                if (model.FileExtensionMap.ContainsKey(entry[0]))
                {
                    model.FileExtensionMap[entry[0]] = entry[1];
                }
                else
                {
                    model.FileExtensionMap.Add(entry[0], entry[1]);
                }
            }
            model.AddPatchableRaces = AddPatchableRaces.ToHashSet();
            model.IgnoreMissingSourceFiles = IgnoreMissingSourceFiles.Select(x => x.Content).ToHashSet();
            foreach (var subOption in Options)
            {
                model.Options.Add(subOption.DumpViewModelToModel());
            }
            return model;
        }
        /// <summary>Refreshes the validation border of every path in <paramref name="collection"/> against <paramref name="rootPath"/>.</summary>
        public static void UpdatePathCollectionStatus(ObservableCollection<VM_CollectionMemberStringDecorated> collection, string rootPath)
        {
            foreach (var path in collection)
            {
                UpdatePathStatus(path, rootPath);
            }
        }

        /// <summary>Sets a green border if the path (relative to <paramref name="rootPath"/>) resolves to an existing file, red otherwise.</summary>
        public static void UpdatePathStatus(VM_CollectionMemberStringDecorated pathVM, string rootPath)
        {
            var trialPath = System.IO.Path.Combine(rootPath, pathVM.Content);
            if (System.IO.File.Exists(trialPath))
            {
                pathVM.BorderColor = CommonColors.LightGreen;
            }
            else
            {
                pathVM.BorderColor = CommonColors.FireBrick;
            }
        }

        /// <summary>Appends a new empty (red-bordered) path entry to <paramref name="collection"/>.</summary>
        public static void AddEmptyPath(ObservableCollection<VM_CollectionMemberStringDecorated> collection)
        {
            var newPath = new VM_CollectionMemberStringDecorated("", collection, VM_CollectionMemberStringDecorated.Mode.TextBlock);
            newPath.BorderColor = CommonColors.Red;
            collection.Add(newPath);
        }

        /// <summary>Opens a file picker (starting at the root directory) and stores the chosen path made relative to the
        /// root directory, then revalidates its border. <paramref name="fileArg"/> is the dialog filter.</summary>
        public void SearchForPath(VM_CollectionMemberStringDecorated selectedString, string fileArg, string prompt)
        {
            string startDir = "";
            if (System.IO.Directory.Exists(ParentManifest.RootDirectory))
            {
                startDir = ParentManifest.RootDirectory;
            }
            if (IO_Aux.SelectFile(startDir, fileArg, prompt, out string path))
            {
                if (!ParentManifest.RootDirectory.IsNullOrWhitespace())
                {
                    selectedString.Content = path.Replace(ParentManifest.RootDirectory, string.Empty);
                }
                selectedString.Content = selectedString.Content.TrimStart(System.IO.Path.DirectorySeparatorChar);
                UpdatePathStatus(selectedString, ParentManifest.RootDirectory);
            }
        }
    }
}
