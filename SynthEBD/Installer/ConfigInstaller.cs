using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Forms;
using Mutagen.Bethesda.Skyrim;
using SharpCompress;
using SharpCompress.Archives.Rar;
using SharpCompress.Archives.SevenZip;
using SharpCompress.Archives.Zip;
using SharpCompress.Common;
using SharpCompress.Readers;

namespace SynthEBD
{
    public class ConfigInstaller
    {
        public static void InstallConfigFile()
        {
            if (PatcherSettings.ModManagerIntegration.ModManagerType != ModManager.None && string.IsNullOrWhiteSpace(PatcherSettings.ModManagerIntegration.CurrentInstallationFolder))
            {
                System.Windows.MessageBox.Show("You must set the location of your mod manager's Mods folder before installing a config file archive.");
                return;
            }

            if (!IO_Aux.SelectFile(PatcherSettings.Paths.AssetPackDirPath, "Archive Files (*.7z;*.zip;*.rar)|*.7z;*.zip;*.rar|" + "All files (*.*)|*.*", out string path))
            {
                return;
            }

            string tempFolderPath = Path.Combine(PatcherSettings.ModManagerIntegration.TempExtractionFolder, DateTime.Now.ToString("yyyy-MM-dd-HH-mm", System.Globalization.CultureInfo.InvariantCulture));
            Directory.CreateDirectory(tempFolderPath);

            try
            {
                if (!ExtractArchive(path, tempFolderPath))
                {
                    return;
                }
            }
            catch
            {
                System.Windows.MessageBox.Show("Archive extraction failed. This may be because the resulting file paths were too long. Try moving your Temp Folder in Mod Manager Integration to a short path such as your desktop. Installation aborted.");
            }

            string manifestPath = Path.Combine(tempFolderPath, "Manifest.json");
            if (!File.Exists(manifestPath))
            {
                System.Windows.MessageBox.Show("Could not find Manifest.json in " + tempFolderPath + ". Installation aborted.");
            }

            Manifest manifest = null;
            try
            {
                manifest = JSONhandler<Manifest>.loadJSONFile(manifestPath);
                if (manifest == null)
                {
                    System.Windows.MessageBox.Show("Could not parse Manifest.json in " + tempFolderPath + ". Installation aborted.");
                    return;
                }
                else if (!ValidateManifest(manifest))
                {
                    return;
                }
            }
            catch
            {
                System.Windows.MessageBox.Show("Could not parse Manifest.json in " + tempFolderPath + ". Installation aborted.");
                return;
            }

            var installerWindow = new Window_ConfigInstaller();
            var installerVM = new VM_ConfigInstaller(manifest, installerWindow);
            installerWindow.DataContext = installerVM;
            installerWindow.ShowDialog();

            if (installerVM.Cancelled)
            {
                return;
            }

            #region load potential required dependencies for validating asset pack
            //record templates
            HashSet<string> recordTemplatePaths = new HashSet<string>();
            foreach (var rtPath in installerVM.SelectorMenu.SelectedRecordTemplatePaths)
            {
                recordTemplatePaths.Add(Path.Combine(tempFolderPath, rtPath));
            }
            List<SkyrimMod> validationRecordTemplates = SettingsIO_AssetPack.LoadRecordTemplates(recordTemplatePaths);

            // BodyGen config
            HashSet<string> bodyGenConfigPaths = new HashSet<string>();
            foreach (var bgPath in installerVM.SelectorMenu.SelectedBodyGenConfigPaths)
            {
                bodyGenConfigPaths.Add(Path.Combine(tempFolderPath, bgPath));
            }
            BodyGenConfigs validationBG = SettingsIO_BodyGen.loadBodyGenConfigs(bodyGenConfigPaths.ToArray(), PatcherSettings.General.RaceGroupings);

            #endregion

            Dictionary<string, string> assetPathMapping = new Dictionary<string, string>();
            HashSet<string> referencedFilePaths = new HashSet<string>();

            HashSet<string> skippedConfigs = new HashSet<string>();

            #region Load, validate, and resave Asset Packs
            foreach (var assetPath in installerVM.SelectorMenu.SelectedAssetPackPaths)
            {
                string extractedPath = Path.Combine(tempFolderPath, assetPath);
                try
                {
                    var validationAP = SettingsIO_AssetPack.LoadAssetPack(extractedPath, PatcherSettings.General.RaceGroupings, validationRecordTemplates, validationBG);
                    string destinationPath = Path.Combine(PatcherSettings.Paths.AssetPackDirPath, validationAP.GroupName + ".json");

                    if (!HandleLongFilePaths(validationAP, manifest, assetPathMapping))
                    {
                        continue;
                    }
                    else if (!File.Exists(destinationPath))
                    {
                        validationAP.FilePath = destinationPath;
                        SettingsIO_AssetPack.SaveAssetPack(validationAP); // save as Json instead of moving in case the referenced paths were modified by HandleLongFilePaths()
                    }
                    else
                    {
                        skippedConfigs.Add(Path.GetFileName(extractedPath));
                        continue;
                    }

                    referencedFilePaths.UnionWith(GetAssetPackSourcePaths(validationAP.Subgroups, new HashSet<string>()));

                    /*
                    //test
                    var referencedPaths = GetAssetPackSourcePaths(validationAP.Subgroups, new HashSet<string>());
                    SimulatedDirectory testDir = new SimulatedDirectory(manifest.DestinationModFolder);
                    foreach (var p in referencedPaths)
                    {
                        SimulatedDirectory.CreateFile(testDir, p);
                    }
                    string debug = "";
                    //end test
                    */
                }
                catch
                {
                    System.Windows.MessageBox.Show("Could not parse Asset Pack " + assetPath + ". Installation aborted.");
                    continue;
                }
            }
            #endregion

            #region move bodygen configs
            foreach (var bgPath in installerVM.SelectorMenu.SelectedBodyGenConfigPaths)
            {
                string destPath = Path.Combine(PatcherSettings.Paths.BodyGenConfigDirPath, Path.GetFileName(bgPath));
                if (!File.Exists(destPath))
                {
                    File.Move(Path.Combine(tempFolderPath, bgPath), destPath, false);
                }
                else
                {
                    skippedConfigs.Add(Path.GetFileName(bgPath));
                }
            }
            #endregion

            #region Move record templates
            foreach (var templatePath in installerVM.SelectorMenu.SelectedRecordTemplatePaths)
            {
                string destPath = Path.Combine(PatcherSettings.Paths.RecordTemplatesDirPath, Path.GetFileName(templatePath));
                if (!File.Exists(destPath))
                {
                    File.Move(Path.Combine(tempFolderPath, templatePath), destPath, false);
                }
                else
                {
                    skippedConfigs.Add(Path.GetFileName(templatePath));
                }
            }
            #endregion

            if (skippedConfigs.Any())
            {
                System.Windows.MessageBox.Show("The following resources were not installed because they already exist in your settings:" + Environment.NewLine + String.Join(Environment.NewLine, skippedConfigs));
            }

            #region move dependency files
            Logger.ArchiveStatusAsync();
            Logger.UpdateStatusAsync("Extracting mods - please wait.", false);
            foreach(string dependencyArchive in installerVM.DownloadMenu.DownloadInfo.Select(x => x.Path))
            {
                ExtractArchive(dependencyArchive, tempFolderPath);
            }
            Logger.DeArchiveStatusAsync();

            List<string> missingFiles = new List<string>();
            foreach (string assetPath in referencedFilePaths)
            {
                string extractedPath = GetPathWithoutSynthEBDPrefix(assetPath, manifest);
                string fullPath = Path.Combine(tempFolderPath, extractedPath);
                if (!File.Exists(fullPath))
                {
                    missingFiles.Add(assetPath);
                    continue;
                }

                if (assetPathMapping.ContainsKey(assetPath))
                {
                    if (!File.Exists(assetPathMapping[assetPath]))
                    {
                        PatcherIO.CreateDirectoryIfNeeded(assetPathMapping[assetPath], PatcherIO.PathType.File);
                        File.Move(fullPath, assetPathMapping[assetPath]);
                    }
                }
                else
                {
                    string destination = GenerateInstalledPath(extractedPath, manifest);
                    if (!File.Exists(destination))
                    {
                        PatcherIO.CreateDirectoryIfNeeded(destination, PatcherIO.PathType.File);
                        File.Move(fullPath, destination);
                    }
                }
            }

            #endregion

            if (missingFiles.Any())
            {
                System.Windows.MessageBox.Show("The following expected files were not found in the selected mod archives:" + Environment.NewLine + string.Join(Environment.NewLine, missingFiles));
            }

            /*
            #region Import new BodyGen configs as VMs to be saved upon close
            foreach (var mConfig in validationBG.Male)
            {
                if (!mainViewModel.BGVM.MaleConfigs.Where(x => x.Label == mConfig.Label).Any())
                {
                    var newMaleVM = VM_BodyGenConfig.GetViewModelFromModel(mConfig, mainViewModel.SGVM.RaceGroupings);
                    newMaleVM.SourcePath = ""; // will get auto-updated upon save
                    mainViewModel.BGVM.MaleConfigs.Add(newMaleVM);
                }
            }
            foreach (var fConfig in validationBG.Female)
            {
                if (!mainViewModel.BGVM.MaleConfigs.Where(x => x.Label == fConfig.Label).Any())
                {
                    var newFemaleVM = VM_BodyGenConfig.GetViewModelFromModel(fConfig, mainViewModel.SGVM.RaceGroupings);
                    newFemaleVM.SourcePath = ""; // will get auto-updated upon save
                    mainViewModel.BGVM.FemaleConfigs.Add(newFemaleVM);
                }
            }
            #endregion

            #region Import new Asset Packs as VMs to be saved upon close
            foreach (var ap in assetPacks)
            {
                if (!mainViewModel.AssetPacks.Where(x => x.GroupName == ap.GroupName).Any())
                {
                    var newAssetPackVM = VM_AssetPack.GetViewModelFromModel(ap, mainViewModel.SGVM, mainViewModel.TMVM.AssetPacks, mainViewModel.vm)
                }
            }
            */

            Directory.Delete(tempFolderPath, true);

            if (PatcherSettings.ModManagerIntegration.ModManagerType != ModManager.None && referencedFilePaths.Any())
            {
                System.Windows.MessageBox.Show("Installation complete. You will need to restart your mod manager to rebuild the VFS in order for SynthEBD to see the newly installed asset files.");
            }
        }

        public static bool ValidateManifest(Manifest manifest)
        {
            if (PatcherSettings.ModManagerIntegration.ModManagerType != ModManager.None && (manifest.DestinationModFolder == null || string.IsNullOrWhiteSpace(manifest.DestinationModFolder)))
            {
                System.Windows.MessageBox.Show("Manifest did not include a destination folder. A new folder called \"New SynthEBD Config\" will appear in your mod list. Pleast rename this folder to something sensible after completing installation.");
                manifest.DestinationModFolder = "New SynthEBD Config";
            }
            return true;
        }

        private static bool ExtractArchive(string archivePath, string destinationPath)
        {
            Cursor.Current = Cursors.WaitCursor;
            FileInfo archiveInfo = new FileInfo(archivePath);
            if (SevenZipArchive.IsSevenZipFile(archiveInfo))
            {
                var zArchive = SevenZipArchive.Open(archiveInfo, new ReaderOptions());
                using (var reader = zArchive.ExtractAllEntries())
                {
                    var options = new ExtractionOptions() { Overwrite = true };
                    options.ExtractFullPath = true;
                    reader.WriteAllToDirectory(destinationPath, options);
                }
                Cursor.Current = Cursors.Default;
                return true;
            }
            else if (RarArchive.IsRarFile(archivePath))
            {
                var rArchive = RarArchive.Open(archiveInfo, new ReaderOptions());
                using (var reader = rArchive.ExtractAllEntries())
                {
                    var options = new ExtractionOptions() { Overwrite = true };
                    options.ExtractFullPath = true;
                    reader.WriteAllToDirectory(destinationPath, options);
                }
                Cursor.Current = Cursors.Default;
                return true;
            }
            else if (ZipArchive.IsZipFile(archivePath))
            {
                var ziArchive = ZipArchive.Open(archiveInfo, new ReaderOptions());
                using (var reader = ziArchive.ExtractAllEntries())
                {
                    var options = new ExtractionOptions() { Overwrite = true };
                    options.ExtractFullPath = true;
                    reader.WriteAllToDirectory(destinationPath, options);
                }
                Cursor.Current = Cursors.Default;
                return true;
            }
            else
            {
                Cursor.Current = Cursors.Default;
                System.Windows.MessageBox.Show("Could not extract the config archive. Valid formats are .7z, .zip, and .rar.");
                return false;
            }
        }

        public static HashSet<string> GetAssetPackSourcePaths(IEnumerable<AssetPack.Subgroup> subgroups, HashSet<string> collectedPaths)
        { 
            foreach (var subgroup in subgroups)
            {
                collectedPaths.UnionWith(subgroup.paths.Select(x => x.Source));
                collectedPaths = GetAssetPackSourcePaths(subgroup.Subgroups, collectedPaths);
            }
            return collectedPaths;
        }

        public static bool HandleLongFilePaths(AssetPack assetPack, Manifest manifest, Dictionary<string, string> pathMap)
        {
            int pathLengthLimit = 260;
            if (PatcherSettings.ModManagerIntegration.ModManagerType != ModManager.None)
            {
                pathLengthLimit = 220; // seems to be the limit of what MO2 can virtualize before it encounters errors
            }

            PathModifications actionsTaken = PathModifications.None;
            int currentLongestPathLength = GetLongestPathLength(assetPack, manifest, out string longestPath);

            string originalDestFolder = manifest.DestinationModFolder;

            while (currentLongestPathLength >= pathLengthLimit)
            {
                actionsTaken |= PathModifications.TrimmedModFolder;
                if (TryTrimModFolder(manifest))
                {
                    break;
                }
                else if (!actionsTaken.HasFlag(PathModifications.TrimmedSubFolders))
                {
                    pathMap = RemapDirectoryNames(assetPack, manifest);
                    actionsTaken |= PathModifications.TrimmedSubFolders;
                }
                else
                {
                    System.Windows.MessageBox.Show("Cannot extract the required asset files for config file " + assetPack.GroupName + ". The longest path is " + currentLongestPathLength + " characters and a maximum of " + pathLengthLimit + "are allowed, and no automated measures could fix the issue. Please consider moving the destination directory to a shorter path. The longest filepath was " + longestPath);
                    return false;
                }

                currentLongestPathLength = GetLongestPathLength(assetPack, manifest, out longestPath);
            }

            if (actionsTaken.HasFlag(PathModifications.TrimmedSubFolders))
            {
                System.Windows.MessageBox.Show("Config file " + assetPack.GroupName + " was modified to comply with the path length limit. All paths within the config file and the destination data folder were automatically modified; no additional action is required.");
            }
            else if (actionsTaken.HasFlag(PathModifications.TrimmedModFolder))
            {
                System.Windows.MessageBox.Show("The destination data folder was shortened from " + originalDestFolder + " to " + manifest.DestinationModFolder + " to comply with path length limit. No additional action is required.");
            }

            return true;
        }

        public static int GetLongestPathLength(AssetPack assetPack, Manifest manifest, out string longestPath)
        {
            longestPath = "";
            var referencedPaths = GetAssetPackSourcePaths(assetPack.Subgroups, new HashSet<string>());
            foreach (var referencedPath in referencedPaths)
            {
                if (referencedPath.Length > longestPath.Length)
                {
                    longestPath = referencedPath;
                }
            }

            string installedPath = GenerateInstalledPath(GetPathWithoutSynthEBDPrefix(longestPath, manifest), manifest);

            return installedPath.Length;
        }

        public static bool TryTrimModFolder(Manifest manifest)
        {
            if (!manifest.DestinationModFolder.Any())
            {
                return false;
            }

            string candidateFolderName = manifest.DestinationModFolder.Remove(manifest.DestinationModFolder.Length - 1, 1);
            DirectoryInfo destDir = new DirectoryInfo(PatcherSettings.ModManagerIntegration.CurrentInstallationFolder);
            if (destDir.GetDirectories().Select(x => x.Name).Contains(candidateFolderName))
            {
                return false;
            }
            else
            {
                manifest.DestinationModFolder = candidateFolderName;
                return true;
            }
        }

        public static string GenerateInstalledPath(string extractedPath, Manifest manifest)
        {
            string modFolder = manifest.DestinationModFolder;

            string extensionFolder = GetExpectedDataFolderFromExtension(extractedPath, manifest);

            if (PatcherSettings.ModManagerIntegration.ModManagerType == ModManager.None)
            {
                return Path.Combine(PatcherEnvironmentProvider.Environment.DataFolderPath, extensionFolder, modFolder, extractedPath);
            }
            else
            {
                return Path.Combine(PatcherSettings.ModManagerIntegration.CurrentInstallationFolder, modFolder, extensionFolder, modFolder, extractedPath);
            }
        }

        public static string GetPathWithoutSynthEBDPrefix(string path, Manifest manifest) // expects path straigth from Config file, e.g. textures\\foo\\textures\\blah.dds
        {
            string extensionFolder = GetExpectedDataFolderFromExtension(path, manifest);

            string synthEBDPrefix = Path.Combine(extensionFolder, manifest.DestinationModFolder);

            return Path.GetRelativePath(synthEBDPrefix, path);
        }

        public static string GetExpectedDataFolderFromExtension(string path, Manifest manifest)
        {
            string extension = Path.GetExtension(path).TrimStart('.');
            string extensionFolder = "";
            if (manifest.FileExtensionMap.ContainsKey(extension))
            {
                extensionFolder = manifest.FileExtensionMap[extension]; // otherwise the file will be installed directly to the data folder or top-level of the mod folder
            }
            return extensionFolder;
        }

        public static Dictionary<string, string> RemapDirectoryNames(AssetPack extractedPack, Manifest manifest)
        {
            Dictionary<string, string> pathMap = new Dictionary<string, string>();

            int folderName = 0;

            var containedPaths = GetAssetPackSourcePaths(extractedPack.Subgroups, new HashSet<string>());

            foreach (var path in containedPaths)
            {
                if (!pathMap.ContainsKey(path))
                {
                    pathMap.Add(path, GenerateRemappedPath(path, manifest, folderName));
                    folderName++;
                }
            }

            return pathMap;
        }

        public static void RemapAssetPackPaths(IEnumerable<AssetPack.Subgroup> subgroups, Dictionary<string, string> pathMap)
        {
            foreach (var subgroup in subgroups)
            {
                foreach (var path in subgroup.paths)
                {
                    path.Source = pathMap[path.Source];
                }
                RemapAssetPackPaths(subgroup.Subgroups, pathMap);
            }
        }

        public static string GenerateRemappedPath(string path, Manifest manifest, int folderName)
        {
            string parentFolder = manifest.FileExtensionMap[Path.GetExtension(path)];
            return Path.Join(parentFolder, folderName.ToString(), Path.GetFileName(path));
        }

        public enum PathModifications
        {
            None,
            TrimmedModFolder,
            TrimmedSubFolders
        }


        /* Started work on more intelligent path shortening to more human-readable file paths, but doesn't seem worth the effort. Might come back to it later. 
        public class SimulatedDirectory
        {
            public SimulatedDirectory(string name)
            {
                Name = name;
                Directories = new HashSet<SimulatedDirectory>();
                Files = new HashSet<string>();
            }

            public string Name { get; set; }
            public HashSet<SimulatedDirectory> Directories { get; set; }
            public HashSet<string> Files { get; set; }

            public static SimulatedDirectory GetDirectory(string path, SimulatedDirectory root)
            {
                SimulatedDirectory currentDirectory = root;
                string[] split = path.Split('\\');
                foreach (string s in split)
                {
                    currentDirectory = currentDirectory.GetSubDirectory(s);
                    if (currentDirectory == null)
                    {
                        return null;
                    }
                }
                return currentDirectory;
            }

            public SimulatedDirectory GetSubDirectory(string directoryName)
            {
                return this.Directories.Where(x => x.Name == directoryName).FirstOrDefault();
            }
            public SimulatedDirectory CreateSubDirectory(string directoryName)
            {
                SimulatedDirectory newDir = new SimulatedDirectory(directoryName);
                this.Directories.Add(newDir);
                return newDir;
            }
            public bool HasDirectory(string directoryName)
            {
                return this.Directories.Where(x => x.Name == directoryName).Any();
            }

            public static void CreateDirectory(SimulatedDirectory root, string directoryPath)
            {
                SimulatedDirectory currentDirectory = root;
                string[] split = directoryPath.Split('\\');
                foreach (string s in split)
                {
                    if (!currentDirectory.HasDirectory(s))
                    {
                        currentDirectory.CreateSubDirectory(s);
                    }
                    currentDirectory = currentDirectory.GetSubDirectory(s);
                }
            }

            public static void CreateFile(SimulatedDirectory root, string filePath)
            {
                SimulatedDirectory currentDirectory = root;
                string[] split = filePath.Split('\\');
                for (int i = 0; i < split.Length - 1; i++)
                {
                    string s = split[i];
                    if (!currentDirectory.HasDirectory(s))
                    {
                        currentDirectory.CreateSubDirectory(s);
                    }
                    currentDirectory = currentDirectory.GetSubDirectory(s);
                }
                currentDirectory.Files.Add(filePath);
            }
        }*/
    }
}
