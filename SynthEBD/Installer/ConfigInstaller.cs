using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using DynamicData;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;
using Noggog;
using SharpCompress.Archives.Rar;
using SharpCompress.Archives.SevenZip;
using SharpCompress.Archives.Zip;
using SharpCompress.Common;
using SharpCompress.Readers;

namespace SynthEBD;

public class ConfigInstaller
{
    private readonly Logger _logger;
    private readonly SynthEBDPaths _paths;
    private readonly SettingsIO_AssetPack _assetPackIO;
    private readonly SettingsIO_BodyGen _bodyGenIO;
    private readonly IEnvironmentStateProvider _environmentProvider;
    private readonly PatcherState _patcherState;
    private readonly VM_7ZipInterface _7ZipInterfaceVM;
    private readonly VM_ConfigInstaller.Factory _installerVMFactory;
    public readonly string DefaultDestinationFolderName = "New SynthEBD Config";
    public ConfigInstaller(Logger logger, SynthEBDPaths synthEBDPaths, SettingsIO_AssetPack assetPackIO, SettingsIO_BodyGen bodyGenIO, IEnvironmentStateProvider environmentProvider, PatcherState patcherState, VM_7ZipInterface sevenZipInterfaceVM, VM_ConfigInstaller.Factory installerVMFactory)
    {
        _logger = logger;
        _paths = synthEBDPaths;
        _assetPackIO = assetPackIO;
        _bodyGenIO = bodyGenIO;
        _environmentProvider = environmentProvider;
        _patcherState = patcherState;
        _7ZipInterfaceVM = sevenZipInterfaceVM;
        _installerVMFactory = installerVMFactory;
    }
    public async Task<(List<string>, bool)> InstallConfigFile()
    {
        var installedConfigs = new List<string>();
        var assignedTokens = new List<string>();
        bool triggerGeneralVMRefresh = false;
        if (_patcherState.ModManagerSettings.ModManagerType != ModManager.None && string.IsNullOrWhiteSpace(_patcherState.ModManagerSettings.CurrentInstallationFolder))
        {
            MessageWindow.DisplayNotificationOK("Installation failed", "You must set the location of your mod manager's Mods folder before installing a config file archive.");
            return (installedConfigs, triggerGeneralVMRefresh);
        }

        if (!IO_Aux.SelectFile(_paths.AssetPackDirPath, "Archive Files (*.7z;*.zip;*.rar)|*.7z;*.zip;*.rar|" + "All files (*.*)|*.*", "Select config archive", out string path))
        {
            return (installedConfigs, triggerGeneralVMRefresh);
        }

        string tempFolderPath = Path.Combine(_patcherState.ModManagerSettings.TempExtractionFolder, DateTime.Now.ToString("yyyy-MM-dd-HH-mm", System.Globalization.CultureInfo.InvariantCulture));
            
        try
        {
            Directory.CreateDirectory(tempFolderPath);
        }
        catch (Exception ex)
        {
            _logger.LogError("Could not create or access the temp folder at " + tempFolderPath + ". Details: " + ex.Message);
            return (installedConfigs, triggerGeneralVMRefresh);
        }

        try
        {
            if (!await _7ZipInterfaceVM.ExtractArchive(path, tempFolderPath, true, _patcherState.GeneralSettings.Close7ZipWhenFinished, 500))
            {
                return (installedConfigs, triggerGeneralVMRefresh);
            }
        }
        catch (Exception ex)
        {
            MessageWindow.DisplayNotificationOK("Installation failed", "Archive extraction failed. This may be because the resulting file paths were too long. Try moving your Temp Folder in Mod Manager Integration to a short path such as your desktop. Installation aborted. Exception Message: " + Environment.NewLine + ExceptionLogger.GetExceptionStack(ex));
        }

        string manifestPath = Path.Combine(tempFolderPath, "Manifest.json");
        if (!File.Exists(manifestPath))
        {
            MessageWindow.DisplayNotificationOK("Installation failed", "Could not find Manifest.json in " + tempFolderPath + ". Installation aborted.");
            return (installedConfigs, triggerGeneralVMRefresh);
        }

        Manifest manifest = JSONhandler<Manifest>.LoadJSONFile(manifestPath, out bool parsed, out string exceptionStr);
        if (!parsed)
        {
            MessageWindow.DisplayNotificationOK("Installation failed", "Could not parse Manifest.json in " + tempFolderPath + ". Installation aborted.");
            _logger.LogError(exceptionStr);
            return (installedConfigs, triggerGeneralVMRefresh);
        }
        else if (!ValidateManifest(manifest))
        {
            return (installedConfigs, triggerGeneralVMRefresh);
        }

        var installerWindow = new Window_ConfigInstaller();
        var installerVM = _installerVMFactory(manifest, installerWindow, tempFolderPath); // note: installerVM edits the Manifest to use as a convenient DTO.
        installerWindow.DataContext = installerVM;
        installerWindow.ShowDialog();

        if (installerVM.Cancelled || !installerVM.Completed)
        {
            return (installedConfigs, triggerGeneralVMRefresh);
        }

        if (_patcherState.ModManagerSettings.ModManagerType != ModManager.None && (manifest.DestinationModFolder == null || string.IsNullOrWhiteSpace(manifest.DestinationModFolder)))
        {
            MessageWindow.DisplayNotificationOK("Installation warning", "Manifest did not include a destination folder. A new folder called \"" + DefaultDestinationFolderName + "\" will appear in your mod list. Pleast rename this folder to something sensible after completing installation.");
            manifest.DestinationModFolder = DefaultDestinationFolderName;
        }

        #region load potential required dependencies for validating asset pack
        //record templates
        HashSet<string> recordTemplatePaths = new HashSet<string>();
        foreach (var rtPath in manifest.RecordTemplatePaths)
        {
            recordTemplatePaths.Add(Path.Combine(tempFolderPath, rtPath));
        }
        List<SkyrimMod> validationRecordTemplates = _assetPackIO.LoadRecordTemplates(recordTemplatePaths, out bool loadSuccess);
        if (!loadSuccess)
        {
            MessageWindow.DisplayNotificationOK("Installation failed", "Could not parse all Record Template Plugins at " + string.Join(", ", recordTemplatePaths) + ". Installation aborted.");
            return (new List<string>(), triggerGeneralVMRefresh);
        }

        // BodyGen config
        HashSet<string> bodyGenConfigPaths = new HashSet<string>();
        foreach (var bgPath in manifest.BodyGenConfigPaths)
        {
            bodyGenConfigPaths.Add(Path.Combine(tempFolderPath, bgPath));
        }
        BodyGenConfigs validationBG = _bodyGenIO.LoadBodyGenConfigs(bodyGenConfigPaths.ToArray(), _patcherState.GeneralSettings.RaceGroupings, out loadSuccess);
        if (!loadSuccess)
        {
            MessageWindow.DisplayNotificationOK("Installation failed", "Could not parse all BodyGen configs at " + string.Join(", ", bodyGenConfigPaths) + ". Installation aborted.");
            return (new List<string>(), triggerGeneralVMRefresh);
        }

        #endregion

        Dictionary<string, string> assetPathMapping = new Dictionary<string, string>();
        HashSet<string> referencedFilePaths = new(StringComparer.OrdinalIgnoreCase);

        HashSet<string> skippedConfigs = new HashSet<string>();

        #region Load, validate, and resave Asset Packs
        HashSet<AssetPack> loadedPacks = new();
        foreach (var configPath in manifest.AssetPackPaths)
        {
            string extractedPath = Path.Combine(tempFolderPath, configPath);
            var validationAP = _assetPackIO.LoadAssetPack(extractedPath, _patcherState.GeneralSettings.RaceGroupings, validationRecordTemplates, validationBG, out loadSuccess);
            if (!loadSuccess)
            {
                MessageWindow.DisplayNotificationOK("Installation failed", "Could not parse Asset Pack " + configPath + ". Installation aborted.");
                continue;
            }
            loadedPacks.Add(validationAP);

            string destinationPath = Path.Combine(_paths.AssetPackDirPath, validationAP.GroupName + ".json");

            if (!HandleLongFilePaths(validationAP, manifest, out assetPathMapping))
            {
                continue;
            }

            if (!File.Exists(destinationPath))
            {
                validationAP.FilePath = destinationPath;
                assignedTokens.Add(validationAP.GenerateInstallationToken());
                _assetPackIO.SaveAssetPack(validationAP, out bool saveSuccess); // save as Json instead of moving in case the referenced paths were modified by HandleLongFilePaths()
                if (!saveSuccess)
                {
                    MessageWindow.DisplayNotificationOK("Installation failed", "Could not save Asset Pack to " + destinationPath + ". Installation aborted.");
                    continue;
                }
            }
            else
            {
                skippedConfigs.Add(Path.GetFileName(extractedPath));
                continue;
            }

            referencedFilePaths.UnionWith(GetAssetPackSourcePaths(validationAP));

            installedConfigs.Add(validationAP.GroupName);
        }
        #endregion

        #region move bodygen configs
        foreach (var bgPath in manifest.BodyGenConfigPaths)
        {
            string destPath = Path.Combine(_paths.BodyGenConfigDirPath, Path.GetFileName(bgPath));
            if (!File.Exists(destPath))
            {
                string sourcePath = Path.Combine(tempFolderPath, bgPath);
                try
                {
                    File.Move(sourcePath, destPath, false);
                }
                catch (Exception ex)
                {
                    MessageWindow.DisplayNotificationOK("Installation warning", "Could not move " + sourcePath + " to " + destPath + ": " + ex.Message);
                }
            }
            else
            {
                skippedConfigs.Add(Path.GetFileName(bgPath));
            }
        }
        #endregion

        #region Move record templates
        foreach (var templatePath in manifest.RecordTemplatePaths)
        {
            string destPath = Path.Combine(_paths.RecordTemplatesDirPath, Path.GetFileName(templatePath));
            if (!File.Exists(destPath))
            {
                string sourcePath = Path.Combine(tempFolderPath, templatePath);
                try
                {
                    File.Move(sourcePath, destPath, false);
                }
                catch (Exception ex)
                {
                    MessageWindow.DisplayNotificationOK("Installation warning", "Could not move " + sourcePath + " to " + destPath + ": " + ex.Message);
                }
            }
            else
            {
                skippedConfigs.Add(Path.GetFileName(templatePath));
            }
        }
        #endregion

        if (skippedConfigs.Any())
        {
            MessageWindow.DisplayNotificationOK("Installation warning", "The following resources were not installed because they already exist in your settings:" + Environment.NewLine + String.Join(Environment.NewLine, skippedConfigs));
        }

        #region move dependency files

        foreach(var dependencyArchive in installerVM.DownloadMenu.DownloadInfo)
        {
            string subPath = manifest.ConfigPrefix;
            if (!dependencyArchive.ExtractionSubPath.IsNullOrWhitespace())
            {
                subPath = dependencyArchive.ExtractionSubPath;
            }
            await _7ZipInterfaceVM.ExtractArchive(dependencyArchive.Path, Path.Combine(tempFolderPath, subPath), true, _patcherState.GeneralSettings.Close7ZipWhenFinished, 500);
        }

        bool triggerExtractionPathWarning = false;
        int filePathLimit = _patcherState.ModManagerSettings.FilePathLimit;
        if (_patcherState.ModManagerSettings.ModManagerType == ModManager.ModOrganizer2)
        {
            filePathLimit = _patcherState.ModManagerSettings.MO2Settings.FilePathLimit;
        }
        else if (_patcherState.ModManagerSettings.ModManagerType == ModManager.Vortex)
        {
            filePathLimit = _patcherState.ModManagerSettings.VortexSettings.FilePathLimit;
        }

        List<string> missingFiles = new List<string>();
        Dictionary<string, string> reversedAssetPathMapping = new Dictionary<string, string>();
        if (assetPathMapping.Keys.Any())
        {
            reversedAssetPathMapping = assetPathMapping.ToDictionary(x => x.Value, x => x.Key);
        }

        bool assetPathCopyErrors = false;
        string currentPrefix = "";

        foreach (string assetPath in referencedFilePaths)
        {
            if (PathStartsWithModName(assetPath))
            {
                continue;
            }
            else if (reversedAssetPathMapping.ContainsKey(assetPath))
            {
                var pathInConfigFile = reversedAssetPathMapping[assetPath];
                string extractedSubPath = GetPathWithoutSynthEBDPrefix(pathInConfigFile, manifest, out currentPrefix);
                string extractedFullPath = Path.Combine(tempFolderPath, currentPrefix, extractedSubPath);

                if (!File.Exists(extractedFullPath))
                {
                    if (!manifest.IgnoreMissingSourceFiles.Contains(assetPath, StringComparer.OrdinalIgnoreCase))
                    {
                        missingFiles.Add(assetPath);
                    }
                    continue;
                }

                string destinationSubPath = GetPathWithoutSynthEBDPrefix(assetPath, manifest, out currentPrefix);
                string destinationFullPath = GenerateInstalledPath(destinationSubPath, manifest, currentPrefix);
                if (!File.Exists(destinationFullPath))
                {
                    try
                    {
                        PatcherIO.CreateDirectoryIfNeeded(destinationFullPath, PatcherIO.PathType.File);
                    }
                    catch (Exception ex)
                    {
                        assetPathCopyErrors = true;
                        _logger.LogError("Could not create or access directory " + destinationFullPath + ": " + ex.Message);
                    }

                    try
                    {
                        File.Move(extractedFullPath, destinationFullPath);
                    }
                    catch (Exception ex)
                    {
                        assetPathCopyErrors = true;
                        _logger.LogError("Could not move " + extractedFullPath + " to " + destinationFullPath + ": " + ex.Message);
                    }
                }
            }
            else
            {
                string extractedSubPath = GetPathWithoutSynthEBDPrefix(assetPath, manifest, out currentPrefix);
                string extractedFullPath = Path.Combine(tempFolderPath, currentPrefix, extractedSubPath);

                if (!File.Exists(extractedFullPath))
                {
                    if (!manifest.IgnoreMissingSourceFiles.Contains(assetPath, StringComparer.OrdinalIgnoreCase))
                    {
                        missingFiles.Add(assetPath);
                        if (extractedFullPath.Length > filePathLimit)
                        {
                            triggerExtractionPathWarning = true;
                        }
                    }
                    continue;
                }

                string destinationFullPath = GenerateInstalledPath(extractedSubPath, manifest, currentPrefix);
                if (!File.Exists(destinationFullPath))
                {
                    try
                    {
                        PatcherIO.CreateDirectoryIfNeeded(destinationFullPath, PatcherIO.PathType.File);
                    }
                    catch (Exception ex)
                    {
                        assetPathCopyErrors = true;
                        _logger.LogError("Could not create or access directory " + destinationFullPath + ": " + ex.Message);
                    }

                    try
                    {
                        File.Move(extractedFullPath, destinationFullPath);
                    }
                    catch (Exception ex)
                    {
                        assetPathCopyErrors = true;
                        _logger.LogError("Could not move " + extractedFullPath + " to " + destinationFullPath + ": " + ex.Message);
                    }
                }
            }
        }

        #endregion

        if (missingFiles.Any())
        {
            string missingFilesWarnStr = "The following expected files were not found in the selected mod archives:" + Environment.NewLine + string.Join(Environment.NewLine, missingFiles);
            if (triggerExtractionPathWarning)
            {
                missingFilesWarnStr += Environment.NewLine + Environment.NewLine + "Some extracted paths were longer than your Mod Manager Settings file path length limit of " + filePathLimit + ". ";
                missingFilesWarnStr += Environment.NewLine + "You may need to move your Temp Folder in your Mod Manager Settings to a shorter path.";
            }
            missingFilesWarnStr += Environment.NewLine + "You will likely need to reinstall this config file to correctly extract the missing files.";
            MessageWindow.DisplayNotificationOK("Installation warning", missingFilesWarnStr);
        }
        if (assetPathCopyErrors)
        {
            MessageWindow.DisplayNotificationOK("Installation warning", "Some installation errors occurred. Please see the Status Log.");
        }

        if (referencedFilePaths.Any())
        {
            RegisterInstalledAssets(manifest, assignedTokens, loadedPacks);
        }

        #region Add Patchable Races
        HashSet<FormKey> missingRaces = new();
        HashSet<IRaceGetter> addedRaces = new();
        foreach (var raceFK in manifest.AddPatchableRaces)
        {
            if (_patcherState.GeneralSettings.PatchableRaces.Contains(raceFK)) { continue; }
            if (_environmentProvider.LinkCache.TryResolve<IRaceGetter>(raceFK, out var raceGetter))
            {
                if (!addedRaces.Select(x => x.FormKey).ToHashSet().Contains(raceGetter.FormKey))
                {
                    addedRaces.Add(raceGetter);
                }
            }
            else if (!missingRaces.Contains(raceFK))
            {
                missingRaces.Add(raceFK);
            }
            _patcherState.GeneralSettings.PatchableRaces.Add(raceFK);
        }

        foreach (var configFile in loadedPacks)
        {
            foreach (var subgroup in configFile.Subgroups)
            {
                GetPatchableRaces(addedRaces, missingRaces, subgroup, configFile);
            }
        }

        if (missingRaces.Any() && MessageWindow.DisplayNotificationYesNo("Missing Additional Races", "The installer attempted to add the following patchable races, but they were not found in your load order. Add them to Patchable Races list anyway?" + Environment.NewLine + String.Join(Environment.NewLine, missingRaces)))
        {
            _patcherState.GeneralSettings.PatchableRaces.AddRange(missingRaces);
            triggerGeneralVMRefresh = true;
        }

        if (addedRaces.Any() && MessageWindow.DisplayNotificationYesNo("Found Additional Races", "This config file references the following races. Add them to your Patchable Races list?" + Environment.NewLine + String.Join(Environment.NewLine, addedRaces.Select(x => x.EditorID ?? x.FormKey.ToString()))))
        {
            _patcherState.GeneralSettings.PatchableRaces.AddRange(addedRaces.Select(x => x.FormKey));
            triggerGeneralVMRefresh = true;
        }
        #endregion

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

        try
        {
            IO_Aux.DeleteDirectoryAF(tempFolderPath);
        }
        catch
        {
            MessageWindow.DisplayNotificationOK("Installation warning", "Could not delete the temp folder located at " + tempFolderPath + ". This may be because the folder path or some of the contained file paths exceeded 260 characters. You may delete this folder manually.");
        }

        if (_patcherState.ModManagerSettings.ModManagerType != ModManager.None && referencedFilePaths.Any())
        {
            MessageWindow.DisplayNotificationOK("Installation success", "Installation complete. You will need to restart your mod manager to rebuild the VFS in order for SynthEBD to see the newly installed asset files.");
        }

        return (installedConfigs, triggerGeneralVMRefresh);
    }

    public bool ValidateManifest(Manifest manifest)
    {
        if (manifest.ConfigPrefix == null || string.IsNullOrWhiteSpace(manifest.ConfigPrefix))
        {
            MessageWindow.DisplayNotificationOK("Installation error", "Manifest did not include a destination prefix. This must match the second directory of each file path in the config file (e.g. textures\\PREFIX\\some\\texture.dds). Please fix the manifest file.");
            return false;
        }
        return true;
    }

    public void RegisterInstalledAssets(Manifest manifest, List<string> installationTokens, HashSet<AssetPack> installedAssetPacks)
    {
        string tokenPath = "";

        if (_patcherState.ModManagerSettings.ModManagerType == ModManager.None)
        {
            List<string> manifestPrefixes = new() { manifest.ConfigPrefix };
            manifestPrefixes.AddRange(manifest.DownloadInfo.Where(info => !info.ExtractionSubPath.IsNullOrWhitespace()).Select(info => info.ExtractionSubPath)); // note: not all prefixes in the manifest are actually used by the installed config files - need to actually go through config's file paths and see which are used.
            foreach (var prefix in GetUsedPrefixes(manifestPrefixes, installedAssetPacks))
            {        
                // look for directories within data folder
                foreach (var dataSubDir in Directory.GetDirectories(_environmentProvider.DataFolderPath))
                {
                    foreach (var secondSubDir in Directory.GetDirectories(dataSubDir))
                    {
                        DirectoryInfo dir_info = new DirectoryInfo(secondSubDir);
                        if (dir_info.Name == prefix.Item2) // put a token file in every prefix directory
                        {
                            tokenPath = Path.Combine(_environmentProvider.DataFolderPath, prefix.Item1, prefix.Item2, SynthEBDInstallationTokenFileName);
                            JSONhandler<List<string>>.SaveJSONFile(installationTokens, tokenPath, out _, out _);
                        }
                    }
                }
            }
        }
        else if (!_patcherState.ModManagerSettings.CurrentInstallationFolder.IsNullOrWhitespace() && !manifest.DestinationModFolder.IsNullOrWhitespace())
        {
            tokenPath = Path.Combine(_patcherState.ModManagerSettings.CurrentInstallationFolder, manifest.DestinationModFolder, SynthEBDInstallationTokenFileName);
            JSONhandler<List<string>>.SaveJSONFile(installationTokens, tokenPath, out _, out _);
        }
    }

    public const string SynthEBDInstallationTokenFileName = "SynthEBD_Tokens.json";

    public HashSet<(string,string)> GetUsedPrefixes(List<string> availablePrefixes, HashSet<AssetPack> installedAssetPacks) // returns (data subfolder, prefix)
    {
        HashSet<(string, string)> prefixes = new();
        List<string> usedPaths = new();
        foreach (var ap in installedAssetPacks)
        {
            foreach (var sg in ap.Subgroups)
            {
                usedPaths.AddRange(GetSubgroupPaths(sg));
            }
            foreach (var replacer in ap.ReplacerGroups)
            {
                foreach (var sg in replacer.Subgroups)
                {
                    usedPaths.AddRange(GetSubgroupPaths(sg));
                }
            }
        }

        foreach (var path in usedPaths)
        {
            var split = path.Split(Path.DirectorySeparatorChar);
            if (split.Length > 1 && availablePrefixes.Contains(split[1]) && !prefixes.Where(x => x.Item1 == split[0] && x.Item2 == split[1]).Any())
            {
                prefixes.Add((split[0], split[1]));
            }
        }

        return prefixes;
    }

    public static List<string> GetSubgroupPaths(AssetPack.Subgroup subgroup)
    {
        List<string> subgroupPaths = subgroup.Paths.Select(x => x.Source).ToList();
        foreach (var sg in subgroup.Subgroups)
        {
            subgroupPaths.AddRange(GetSubgroupPaths(sg));
        }
        return subgroupPaths;
    }

    private bool ExtractArchive(string archivePath, string destinationPath)
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
            MessageWindow.DisplayNotificationOK("Installation error", "Could not extract the config archive. Valid formats are .7z, .zip, and .rar.");
            return false;
        }
    }

    public HashSet<string> GetAssetPackSourcePaths(AssetPack assetPack)
    {
        // get paths in main subgroups
        var referencedPaths = GetSubgroupListPaths(assetPack.Subgroups, new HashSet<string>());

        // get paths in replacer subgroups
        foreach (var replacer in assetPack.ReplacerGroups)
        {
            referencedPaths = GetSubgroupListPaths(replacer.Subgroups, referencedPaths);
        }
        return referencedPaths;
    }

    public HashSet<string> GetSubgroupListPaths(IEnumerable<AssetPack.Subgroup> subgroups, HashSet<string> collectedPaths)
    { 
        foreach (var subgroup in subgroups)
        {
            collectedPaths.UnionWith(subgroup.Paths.Select(x => x.Source));
            collectedPaths = GetSubgroupListPaths(subgroup.Subgroups, collectedPaths);
        }
        return collectedPaths;
    }

    public bool HandleLongFilePaths(AssetPack assetPack, Manifest manifest, out Dictionary<string, string> pathMap)
    {
        pathMap = new Dictionary<string, string>();
        int pathLengthLimit = 260;

        switch(_patcherState.ModManagerSettings.ModManagerType)
        {
            case ModManager.None: pathLengthLimit = _patcherState.ModManagerSettings.FilePathLimit; break;
            case ModManager.ModOrganizer2: pathLengthLimit = _patcherState.ModManagerSettings.MO2Settings.FilePathLimit; break;
            case ModManager.Vortex: pathLengthLimit = _patcherState.ModManagerSettings.VortexSettings.FilePathLimit; break;
        }

        int originalLongestPathLength = GetLongestPathLength(assetPack, manifest, out string longestPath);

        if (originalLongestPathLength > pathLengthLimit)
        {
            pathMap = RemapDirectoryNames(assetPack, manifest, pathLengthLimit); // from list of paths, generate a map of old -> new paths
            RemapAssetPackPaths(assetPack, pathMap); // from path map, reassign the paths referenced in the asset pack

            int newLongestPathLength = GetLongestPathLength(assetPack, manifest, out _);
            if (newLongestPathLength > pathLengthLimit)
            {
                List<string> longMessage = new()
                {
                    "Cannot extract the required asset files for config file " + assetPack.GroupName + ".",
                    "The longest path:",
                    longestPath,
                    "is " + originalLongestPathLength + " characters and a maximum of " + pathLengthLimit + " are allowed.",
                    "After automatic renaming the longest path was still " + newLongestPathLength + " charactersl long.",
                    "Please consider moving the destination directory to a shorter path"
                };
                MessageWindow.DisplayNotificationOK("Installation error", string.Join(Environment.NewLine, longMessage));
                return false;
            }
            else
            {
                List<string> longMessage = new()
                {
                    "Config file " + assetPack.GroupName + " was modified to comply with the path length limit (" + pathLengthLimit + ").",
                    "The longest path:",
                    longestPath,
                    "would have been " + originalLongestPathLength + " characters long but is now truncated to " + newLongestPathLength + " characters.",
                    "All long paths within the config file and the destination data folder were automatically modified.",
                    "No additional action is required."
                };
                MessageWindow.DisplayNotificationOK("Installation notice", string.Join(Environment.NewLine, longMessage));
            }
        }

        return true;
    }

    public int GetLongestPathLength(AssetPack assetPack, Manifest manifest, out string longestPath)
    {
        longestPath = "";
            
        var referencedPaths = GetAssetPackSourcePaths(assetPack);

        foreach (var referencedPath in referencedPaths)
        {
            if (PathStartsWithModName(referencedPath)) { continue; }

            if (referencedPath.Length > longestPath.Length)
            {
                longestPath = referencedPath;
            }
        }

        longestPath = GenerateInstalledPath(GetPathWithoutSynthEBDPrefix(longestPath, manifest, out string detectedPrefix), manifest, detectedPrefix);

        return longestPath.Length;
    }

    public bool TryTrimModFolder(Manifest manifest) // currently deprectated - I don't think this is an intuitive functionality but leaving for now as a future consideration.
    {
        if (!manifest.DestinationModFolder.Any())
        {
            return false;
        }

        string candidateFolderName = manifest.DestinationModFolder.Remove(manifest.DestinationModFolder.Length - 1, 1);
        DirectoryInfo destDir = new DirectoryInfo(_patcherState.ModManagerSettings.CurrentInstallationFolder);
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

    public string GenerateInstalledPath(string extractedSubPath, Manifest manifest, string selectedPrefix)
    {
        if (GetExpectedDataFolderFromExtension(extractedSubPath, manifest, out string extensionFolder))
        {
            if (_patcherState.ModManagerSettings.ModManagerType == ModManager.None)
            {
                return Path.Combine(_environmentProvider.DataFolderPath, extensionFolder, selectedPrefix, extractedSubPath);
            }
            else
            {
                return Path.Combine(_patcherState.ModManagerSettings.CurrentInstallationFolder, manifest.DestinationModFolder, extensionFolder, selectedPrefix, extractedSubPath);
            }
        }
        else
        {
            if (_patcherState.ModManagerSettings.ModManagerType == ModManager.None)
            {
                return Path.Combine(_environmentProvider.DataFolderPath, extractedSubPath);
            }
            else
            {
                return Path.Combine(_patcherState.ModManagerSettings.CurrentInstallationFolder, manifest.DestinationModFolder, extractedSubPath);
            }
        }
    }

    public string GetPathWithoutSynthEBDPrefix(string path, Manifest manifest, out string detectedPrefix) // expects path straight from Config file, e.g. textures\\foo\\textures\\blah.dds --> textures\\blah.dds
    {
        detectedPrefix = "";
        if (GetExpectedDataFolderFromExtension(path, manifest, out string extensionFolder))
        {
            detectedPrefix = FindPathPrefix(path, manifest);
            if (!detectedPrefix.IsNullOrWhitespace())
            {
                string fullPrefix = Path.Combine(extensionFolder, detectedPrefix);
                return Path.GetRelativePath(fullPrefix, path);
            }
            else
            {
                return path;
            }
        }
        else
        {
            return path;
        }
    }

    public string FindPathPrefix(string path, Manifest manifest)
    {
        foreach (var additionalPrefix in manifest.DownloadInfo.Select(x => x.ExtractionSubPath).Where(x => !x.IsNullOrWhitespace()))
        {
            if (path.Contains(additionalPrefix))
            {
                return additionalPrefix;
            }
        }

        if (path.Contains(manifest.ConfigPrefix))
        {
            return manifest.ConfigPrefix;
        }

        return "";
    }

    public bool GetExpectedDataFolderFromExtension(string path, Manifest manifest, out string extensionFolder)
    {
        string extension = Path.GetExtension(path).TrimStart('.');
        extensionFolder = "";
        if (manifest.FileExtensionMap.ContainsKey(extension))
        {
            extensionFolder = manifest.FileExtensionMap[extension]; // otherwise the file will be installed directly to the data folder or top-level of the mod folder
            return true;
        }
        else
        {
            var trimPath = _patcherState.TexMeshSettings.TrimPaths.Where(x => x.Extension.Equals(extension, StringComparison.OrdinalIgnoreCase)).FirstOrDefault();
            if (trimPath is not null)
            {
                extensionFolder = trimPath.PathToTrim;
                return true;
            }
        }
        return false;
    }

    public Dictionary<string, string> RemapDirectoryNames(AssetPack extractedPack, Manifest manifest, int pathLengthLimit)
    {
        Dictionary<string, string> pathMap = new Dictionary<string, string>();

        int newFileNameIndex;

        var containedPaths = GetAssetPackSourcePaths(extractedPack);

        Dictionary<string, int> pathCountsByFile = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var path in containedPaths)
        {
            string extractedPath = GenerateInstalledPath(GetPathWithoutSynthEBDPrefix(path, manifest, out string detectedPrefix), manifest, detectedPrefix);
            if (extractedPath.Length > pathLengthLimit && !pathMap.ContainsKey(path) && !PathStartsWithModName(path) && GetExpectedDataFolderFromExtension(path, manifest, out _))
            {
                string fileName = Path.GetFileNameWithoutExtension(path);
                if(pathCountsByFile.ContainsKey(fileName))
                {
                    pathCountsByFile[fileName]++;
                    newFileNameIndex = pathCountsByFile[fileName];
                }
                else
                {
                    pathCountsByFile.Add(fileName, 1);
                    newFileNameIndex = 1;
                }

                pathMap.Add(path, GenerateRemappedPath(path, manifest, fileName, newFileNameIndex));
                newFileNameIndex++;
            }
        }

        return pathMap;
    }

    public void RemapAssetPackPaths(AssetPack assetPack, Dictionary<string, string> pathMap)
    {
        // remap paths in main subgroups
        RemapSubgroupListPaths(assetPack.Subgroups, pathMap);

        // remap paths in replacer subgroups
        foreach (var replacer in assetPack.ReplacerGroups)
        {
            RemapSubgroupListPaths(replacer.Subgroups, pathMap);
        }
    }

    public void RemapSubgroupListPaths(IEnumerable<AssetPack.Subgroup> subgroups, Dictionary<string, string> pathMap)
    {
        foreach (var subgroup in subgroups)
        {
            foreach (var path in subgroup.Paths)
            {
                if (pathMap.ContainsKey(path.Source))
                {
                    path.Source = pathMap[path.Source];
                }
            }
            RemapSubgroupListPaths(subgroup.Subgroups, pathMap);
        }
    }

    public string GenerateRemappedPath(string path, Manifest manifest, string folderName, int fileName)
    {
        string currentPrefix = FindPathPrefix(path, manifest);

        if (GetExpectedDataFolderFromExtension(path, manifest, out string parentFolder))
        {
            return Path.Join(parentFolder, currentPrefix, folderName, fileName.ToString() + Path.GetExtension(path)); // Path.GetExtension returns a string starting with '.'
        }
        else
        {
            return path;
        }
    }

    public enum PathModifications
    {
        None,
        TrimmedModFolder,
        TrimmedSubFolders
    }

    public bool PathStartsWithModName(string path)
    {
        string[] split = path.Split(Path.DirectorySeparatorChar);
        if (!split.Any())
        {
            return false;
        }

        string[] split2 = split[0].Split('.');
        if (split2.Length != 2)
        {
            return false;
        }

        string extension = split2[1].ToLower();
        if (extension == "esp" || extension == "esm" || extension == "esl")
        {
            return true;
        }
        return false;
    }

    public void GetPatchableRaces(HashSet<IRaceGetter> races, HashSet<FormKey> missingRaces, AssetPack.Subgroup subgroup, AssetPack parent)
    {
        foreach (var raceFK in subgroup.AllowedRaces)
        {
            if (_patcherState.GeneralSettings.PatchableRaces.Contains(raceFK)) { continue; }

            if (_environmentProvider.LinkCache.TryResolve<IRaceGetter>(raceFK, out var raceGetter))
            {
                if (!races.Select(x => x.FormKey).ToHashSet().Contains(raceGetter.FormKey))
                {
                    races.Add(raceGetter);
                }
            }
            else if (!missingRaces.Contains(raceFK))
            {
                missingRaces.Add(raceFK);    
            }
        }

        var groupings = parent.RaceGroupings;
        if (!groupings.Any())
        {
            groupings = _patcherState.GeneralSettings.RaceGroupings;
        }

        foreach (var groupingStr in subgroup.AllowedRaceGroupings)
        {
            var grouping = groupings.Where(x => x.Label == groupingStr).FirstOrDefault();
            if (grouping != null)
            {
                foreach (var raceFK in grouping.Races)
                {
                    if (_patcherState.GeneralSettings.PatchableRaces.Contains(raceFK)) { continue; }

                    if (_environmentProvider.LinkCache.TryResolve<IRaceGetter>(raceFK, out var raceGetter))
                    {
                        if (!races.Select(x => x.FormKey).ToHashSet().Contains(raceGetter.FormKey))
                        {
                            races.Add(raceGetter);
                        }
                    }
                    else if (!missingRaces.Contains(raceFK))
                    {
                        missingRaces.Add(raceFK);
                    }
                }
            }
        }

        foreach (var sg in subgroup.Subgroups)
        {
            GetPatchableRaces(races, missingRaces, sg, parent);
        }
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