using System.IO;
using Autofac;
using Newtonsoft.Json;
using Noggog;

namespace SynthEBD.CLI;

/// <summary>
/// CLI verb that verifies a packaged (or staged) config archive installs correctly for <b>every possible
/// user selection</b>, without running the interactive installer. It enumerates all selection chains
/// through the manifest's Options tree (one chain per root-to-leaf path of each sequential step, exactly
/// as <see cref="VM_ConfigSelector"/> walks them), unions each chain's resources the way
/// <c>VM_ConfigSelector.Finalize</c> does, and for each chain checks that: the referenced config /
/// record-template / BodyGen files exist and parse; every required dependency archive is supplied; and
/// every config Source path resolves to a file the simulated install tree would provide (config-archive
/// contents plus dependency-archive contents routed through the installer's own prefix logic
/// (<see cref="ConfigInstaller.GetPathWithoutSynthEBDPrefix"/>) - no live game data involved).
/// </summary>
public static class VerifyInstallVerb
{
    /// <summary>JSON/text report root for a verify-install run.</summary>
    public class VerifyReport
    {
        public string Command { get; set; } = "verify-install";
        public string ConfigName { get; set; } = "";
        public string ConfigPrefix { get; set; } = "";
        public bool AllChainsValid { get; set; }
        public List<ChainReport> Chains { get; set; } = new();
    }

    /// <summary>Verification outcome for one complete user-selection chain.</summary>
    public class ChainReport
    {
        /// <summary>Human-readable selection path, e.g. "Step 1: CBBE &gt; 4K &gt; With BodyGen".</summary>
        public string Selection { get; set; } = "";
        public bool IsValid { get; set; }
        public List<string> InstalledConfigs { get; set; } = new();
        public List<string> InstalledRecordTemplates { get; set; } = new();
        public List<string> InstalledBodyGenConfigs { get; set; } = new();
        public List<string> RequiredDownloads { get; set; } = new();
        public List<string> Errors { get; set; } = new();
        public List<string> MissingSourceFiles { get; set; } = new();
    }

    public static async Task<int> RunAsync(CliOptions options, TextWriter resultWriter)
    {
        if (!CliBootstrapper.TryCreate(options, out var bootstrapper, out var failureReason) || bootstrapper == null)
        {
            Console.Error.WriteLine("Could not initialize the SynthEBD environment: " + failureReason);
            return 2;
        }

        using (bootstrapper)
        {
            var sevenZip = bootstrapper.Container.Resolve<_7ZipInterface>();
            var installer = bootstrapper.Container.Resolve<ConfigInstaller>();

            // Obtain the staged tree: either directly, or by extracting the packaged archive to a temp folder.
            string staging;
            string? tempExtraction = null;
            if (options.StagingDir != null)
            {
                staging = options.StagingDir;
            }
            else
            {
                tempExtraction = Path.Combine(Path.GetTempPath(), "SynthEBD.CLI", "VerifyInstall_" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(tempExtraction);
                Console.Error.WriteLine("Extracting " + options.ArchivePath + " ...");
                if (!await sevenZip.ExtractArchive(options.ArchivePath!, tempExtraction, hideWindow: true,
                        mirrorUIstr: _ => { }, suppressErrorDialog: true))
                {
                    Console.Error.WriteLine("Could not extract the config archive.");
                    return 2;
                }
                staging = tempExtraction;
            }

            try
            {
                return await VerifyAsync(staging, options, sevenZip, installer, resultWriter);
            }
            finally
            {
                if (tempExtraction != null)
                {
                    try { Directory.Delete(tempExtraction, recursive: true); } catch { /* best effort */ }
                }
            }
        }
    }

    private static async Task<int> VerifyAsync(string staging, CliOptions options, _7ZipInterface sevenZip,
        ConfigInstaller installer, TextWriter resultWriter)
    {
        var manifestPath = Path.Combine(staging, "Manifest.json");
        if (!File.Exists(manifestPath))
        {
            Console.Error.WriteLine("No Manifest.json found at the archive/staging root.");
            return 2;
        }
        var manifest = JSONhandler<Manifest>.LoadJSONFile(manifestPath, out bool manifestLoaded, out string manifestException);
        if (!manifestLoaded)
        {
            Console.Error.WriteLine("Could not parse Manifest.json: " + manifestException);
            return 2;
        }

        MigrateVersion0(manifest);

        var report = new VerifyReport
        {
            ConfigName = manifest.ConfigName,
            ConfigPrefix = manifest.ConfigPrefix,
        };

        // Files the config archive itself provides (relative to the staging root).
        var stagedFiles = Directory.GetFiles(staging, "*", SearchOption.AllDirectories)
            .Select(x => Path.GetRelativePath(staging, x))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Cache of dependency-archive listings, keyed by resolved archive path.
        var archiveListings = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        // A manifest with no options at all is a single (empty) chain over the root resources.
        var chains = EnumerateSelectionChains(manifest);
        if (!chains.Any())
        {
            chains.Add(("(no options)", new List<Manifest.Option>()));
        }

        foreach (var (selectionName, chain) in chains)
        {
            var chainReport = new ChainReport { Selection = selectionName };
            report.Chains.Add(chainReport);

            // Union the chain's resources over the manifest's root fields, as VM_ConfigSelector.Finalize does.
            var effective = new Manifest
            {
                ConfigName = manifest.ConfigName,
                ConfigPrefix = manifest.ConfigPrefix,
                FileExtensionMap = new(manifest.FileExtensionMap, StringComparer.InvariantCultureIgnoreCase),
                DownloadInfo = new(manifest.DownloadInfo),
                AssetPackPaths = new(manifest.AssetPackPaths),
                RecordTemplatePaths = new(manifest.RecordTemplatePaths),
                BodyGenConfigPaths = new(manifest.BodyGenConfigPaths),
                IgnoreMissingSourceFiles = new(manifest.IgnoreMissingSourceFiles),
            };
            foreach (var selection in chain)
            {
                effective.AssetPackPaths.UnionWith(selection.AssetPackPaths);
                effective.RecordTemplatePaths.UnionWith(selection.RecordTemplatePaths);
                effective.BodyGenConfigPaths.UnionWith(selection.BodyGenConfigPaths);
                effective.DownloadInfo.UnionWith(selection.DownloadInfo);
                effective.IgnoreMissingSourceFiles.UnionWith(selection.IgnoreMissingSourceFiles);
                foreach (var mapping in selection.FileExtensionMap)
                {
                    effective.FileExtensionMap[mapping.Key] = mapping.Value;
                }
            }

            chainReport.InstalledConfigs = effective.AssetPackPaths.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
            chainReport.InstalledRecordTemplates = effective.RecordTemplatePaths.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
            chainReport.InstalledBodyGenConfigs = effective.BodyGenConfigPaths.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
            chainReport.RequiredDownloads = effective.DownloadInfo
                .Select(x => x.ExpectedFileName.IsNullOrWhitespace() ? x.ModDownloadName : x.ExpectedFileName)
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();

            if (!effective.AssetPackPaths.Any())
            {
                chainReport.Errors.Add("This selection installs no asset pack config files.");
            }

            // 1. Staged resources must exist (per chain, so a branch that forgot its template is caught).
            foreach (var relPath in effective.RecordTemplatePaths.Concat(effective.BodyGenConfigPaths))
            {
                if (!stagedFiles.Contains(relPath))
                {
                    chainReport.Errors.Add("Referenced file is missing from the package: " + relPath);
                }
            }

            // 2. Build the chain's simulated install pool: the config archive's own files (extracted to the
            //    temp root) plus each dependency archive's contents (extracted to <subPath>\<entry>, where
            //    subPath is the DownloadInfo's ExtractionSubPath or the manifest ConfigPrefix).
            var providedFiles = new HashSet<string>(stagedFiles, StringComparer.OrdinalIgnoreCase);
            foreach (var download in effective.DownloadInfo)
            {
                var subPath = download.ExtractionSubPath.IsNullOrWhitespace() ? effective.ConfigPrefix : download.ExtractionSubPath;
                if (!TryResolveDownloadArchive(download, options, out var archiveFile, out var downloadError))
                {
                    chainReport.Errors.Add(downloadError);
                    continue;
                }
                if (!archiveListings.TryGetValue(archiveFile, out var listing))
                {
                    Console.Error.WriteLine("Cataloguing dependency archive " + Path.GetFileName(archiveFile) + " ...");
                    listing = await sevenZip.GetArchiveContents(archiveFile, hideWindow: true, mirrorUIstr: _ => { }, suppressErrorDialog: true);
                    listing = listing.Where(x => !x.Equals(archiveFile, StringComparison.OrdinalIgnoreCase)).ToList();
                    archiveListings[archiveFile] = listing;
                    if (!listing.Any())
                    {
                        chainReport.Errors.Add("Dependency archive has no readable contents: " + archiveFile);
                    }
                }
                foreach (var entry in listing)
                {
                    providedFiles.Add(Path.Combine(subPath, entry));
                }
            }

            // 3. Every config must parse, and every file-type Source path must resolve in the pool via the
            //    installer's own prefix routing.
            foreach (var configRelPath in effective.AssetPackPaths)
            {
                if (!stagedFiles.Contains(configRelPath))
                {
                    chainReport.Errors.Add("Referenced config is missing from the package: " + configRelPath);
                    continue;
                }
                var assetPack = JSONhandler<AssetPack>.LoadJSONFile(Path.Combine(staging, configRelPath), out bool packLoaded, out string packException);
                if (!packLoaded)
                {
                    chainReport.Errors.Add("Referenced config could not be parsed: " + configRelPath + " (" + packException + ")");
                    continue;
                }

                foreach (var sourcePath in installer.GetAssetPackSourcePaths(assetPack).OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
                {
                    if (installer.PathStartsWithModName(sourcePath)) { continue; }
                    if (effective.IgnoreMissingSourceFiles.Contains(sourcePath, StringComparer.OrdinalIgnoreCase)) { continue; }

                    var extractedSubPath = installer.GetPathWithoutSynthEBDPrefix(sourcePath, effective, out string detectedPrefix);
                    var expectedProvidedPath = Path.Combine(detectedPrefix, extractedSubPath);
                    if (!providedFiles.Contains(expectedProvidedPath))
                    {
                        chainReport.MissingSourceFiles.Add(sourcePath + "  (expected in archive set at: " + expectedProvidedPath + ")");
                    }
                }
            }

            chainReport.IsValid = !chainReport.Errors.Any() && !chainReport.MissingSourceFiles.Any();
        }

        report.AllChainsValid = report.Chains.All(x => x.IsValid);

        if (options.Json)
        {
            resultWriter.WriteLine(JsonConvert.SerializeObject(report, Formatting.Indented));
        }
        else
        {
            WriteTextReport(report, resultWriter);
        }

        return report.AllChainsValid ? 0 : 1;
    }

    /// <summary>Folds a Version-0 manifest's top-level legacy fields into a synthesized root option wrapping the
    /// existing options, exactly as <c>VM_ConfigSelector.UpgradeVersion0</c> does.</summary>
    private static void MigrateVersion0(Manifest manifest)
    {
        if (manifest.Version == 0)
        {
            Manifest.Option rootOption = new();
            rootOption.OptionsDescription = manifest.OptionsDescription;
            rootOption.FileExtensionMap = manifest.FileExtensionMap;
            rootOption.DownloadInfo = manifest.DownloadInfo;
            rootOption.DestinationModFolder = manifest.DestinationModFolder;
            rootOption.AssetPackPaths = manifest.AssetPackPaths;
            rootOption.BodyGenConfigPaths = manifest.BodyGenConfigPaths;
            rootOption.RecordTemplatePaths = manifest.RecordTemplatePaths;
            rootOption.Options.AddRange(manifest.Options);
            manifest.Options.Clear();
            manifest.Options.Add(rootOption);
            // The legacy root collections now live (by reference) in the root option; reset the root fields so
            // resources are not double-counted when chains union over them.
            manifest.FileExtensionMap = new(StringComparer.InvariantCultureIgnoreCase);
            manifest.DownloadInfo = new();
            manifest.AssetPackPaths = new();
            manifest.BodyGenConfigPaths = new();
            manifest.RecordTemplatePaths = new();
        }
    }

    /// <summary>
    /// Enumerates every complete user selection: for each top-level option (a sequential wizard step that is
    /// auto-included), every root-to-leaf path through its sub-options is one chain. Steps are independent for
    /// resource purposes, so each step's chains are verified separately rather than as a cartesian product.
    /// </summary>
    private static List<(string Name, List<Manifest.Option> Chain)> EnumerateSelectionChains(Manifest manifest)
    {
        var chains = new List<(string, List<Manifest.Option>)>();
        for (int i = 0; i < manifest.Options.Count; i++)
        {
            var stepLabel = "Step " + (i + 1);
            var topLevel = manifest.Options[i];
            void Walk(Manifest.Option node, List<Manifest.Option> pathSoFar, string nameSoFar)
            {
                var path = new List<Manifest.Option>(pathSoFar) { node };
                var name = nameSoFar.IsNullOrWhitespace()
                    ? (node.Name.IsNullOrWhitespace() ? "(unnamed)" : node.Name)
                    : nameSoFar + " > " + (node.Name.IsNullOrWhitespace() ? "(unnamed)" : node.Name);
                if (!node.Options.Any())
                {
                    chains.Add((stepLabel + ": " + name, path));
                    return;
                }
                foreach (var child in node.Options)
                {
                    Walk(child, path, name);
                }
            }
            Walk(topLevel, new List<Manifest.Option>(), string.Empty);
        }
        return chains;
    }

    /// <summary>Locates a dependency archive in the --downloads folder by its ExpectedFileName.</summary>
    private static bool TryResolveDownloadArchive(Manifest.DownloadInfoContainer download, CliOptions options,
        out string archiveFile, out string error)
    {
        archiveFile = string.Empty;
        error = string.Empty;
        var label = download.ModDownloadName.IsNullOrWhitespace() ? download.ExpectedFileName : download.ModDownloadName;
        if (options.DownloadsDir == null)
        {
            error = "This selection requires dependency archive \"" + label + "\" (" + download.ExpectedFileName +
                ") but no --downloads folder was provided.";
            return false;
        }
        if (download.ExpectedFileName.IsNullOrWhitespace())
        {
            error = "DownloadInfo entry \"" + label + "\" has no ExpectedFileName, so its archive cannot be matched automatically.";
            return false;
        }
        var candidate = Path.Combine(options.DownloadsDir, download.ExpectedFileName);
        if (!File.Exists(candidate))
        {
            error = "Dependency archive not found in --downloads folder: " + download.ExpectedFileName +
                " (needed by \"" + label + "\")";
            return false;
        }
        archiveFile = candidate;
        return true;
    }

    private static void WriteTextReport(VerifyReport report, TextWriter writer)
    {
        writer.WriteLine("Manifest: " + report.ConfigName + " (prefix " + report.ConfigPrefix + ") - "
            + report.Chains.Count + " selection chain(s).");
        foreach (var chain in report.Chains)
        {
            writer.WriteLine();
            writer.WriteLine((chain.IsValid ? "VALID:   " : "INVALID: ") + chain.Selection);
            writer.WriteLine("  Configs: " + (chain.InstalledConfigs.Any() ? string.Join(", ", chain.InstalledConfigs) : "(none)"));
            if (chain.InstalledRecordTemplates.Any())
            {
                writer.WriteLine("  Record templates: " + string.Join(", ", chain.InstalledRecordTemplates));
            }
            if (chain.InstalledBodyGenConfigs.Any())
            {
                writer.WriteLine("  BodyGen configs: " + string.Join(", ", chain.InstalledBodyGenConfigs));
            }
            if (chain.RequiredDownloads.Any())
            {
                writer.WriteLine("  Requires downloads: " + string.Join(", ", chain.RequiredDownloads));
            }
            foreach (var error in chain.Errors)
            {
                writer.WriteLine("  ERROR: " + error);
            }
            if (chain.MissingSourceFiles.Any())
            {
                writer.WriteLine("  Missing source files (" + chain.MissingSourceFiles.Count + "):");
                foreach (var missing in chain.MissingSourceFiles)
                {
                    writer.WriteLine("    " + missing);
                }
            }
        }
        writer.WriteLine();
        writer.WriteLine(report.AllChainsValid
            ? "All selection chains verified."
            : "At least one selection chain failed verification.");
    }
}
