using System.IO;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Plugins.Order;
using Mutagen.Bethesda.Skyrim;
using Newtonsoft.Json;
using Noggog;

namespace SynthEBD.CLI;

/// <summary>
/// CLI verbs for config packaging and archive plumbing, none of which need a game environment:
/// <c>package</c> validates a staged config folder against its Manifest.json (the structure the
/// Config Packager authors) and 7-zips it into a distributable archive; <c>archive-list</c> and
/// <c>archive-extract</c> wrap the bundled 7-Zip so agents can catalogue and unpack 7z/zip/rar
/// texture-mod archives without host tooling.
/// </summary>
public static class PackageVerb
{
    /// <summary>JSON/text report root for a package run.</summary>
    public class PackageReport
    {
        public string Command { get; set; } = "package";
        public string ConfigName { get; set; } = "";
        public string ConfigPrefix { get; set; } = "";
        public bool ManifestValid { get; set; }
        public List<string> Errors { get; set; } = new();
        public List<string> Warnings { get; set; } = new();
        public int AssetPackCount { get; set; }
        public int RecordTemplateCount { get; set; }
        public int BodyGenConfigCount { get; set; }
        public string? ArchivePath { get; set; }
        public long ArchiveSizeBytes { get; set; }
        public int ArchivedFileCount { get; set; }
    }

    public static async Task<int> RunAsync(CliOptions options, TextWriter resultWriter)
    {
        var staging = options.StagingDir!;
        var manifestPath = Path.Combine(staging, "Manifest.json");
        if (!File.Exists(manifestPath))
        {
            Console.Error.WriteLine("No Manifest.json found at the staging folder root: " + staging);
            return 2;
        }

        var manifest = JSONhandler<Manifest>.LoadJSONFile(manifestPath, out bool loadSuccess, out string exceptionStr);
        if (!loadSuccess)
        {
            Console.Error.WriteLine("Could not parse Manifest.json: " + exceptionStr);
            return 2;
        }

        var report = new PackageReport
        {
            ConfigName = manifest.ConfigName,
            ConfigPrefix = manifest.ConfigPrefix,
        };

        ValidateManifestContents(manifest, staging, report);
        report.ManifestValid = !report.Errors.Any();

        if (!report.ManifestValid)
        {
            EmitReport(report, options, resultWriter);
            return 1;
        }

        // Create the archive from the staging folder's contents (Manifest.json at archive root).
        var outPath = options.OutPath ?? Path.Combine(
            Path.GetDirectoryName(staging.TrimEnd(Path.DirectorySeparatorChar)) ?? staging,
            IO_Aux.MakeValidFileName(manifest.ConfigName) + ".7z");

        var sevenZip = new _7ZipInterface(new SevenZipEnvironmentShim());
        Console.Error.WriteLine("Creating archive " + outPath + " ...");
        bool created = await sevenZip.CreateArchive(staging, outPath, hideWindow: true,
            mirrorUIstr: line => Console.Error.WriteLine(line), suppressErrorDialog: true);
        if (!created)
        {
            Console.Error.WriteLine("Archive creation failed.");
            return 2;
        }

        report.ArchivePath = outPath;
        report.ArchiveSizeBytes = new FileInfo(outPath).Length;
        report.ArchivedFileCount = (await ListArchiveFiles(sevenZip, outPath)).Count;

        EmitReport(report, options, resultWriter);
        return 0;
    }

    /// <summary>Lists an archive's file entries (excluding 7-Zip's header entry for the archive itself).</summary>
    private static async Task<List<string>> ListArchiveFiles(_7ZipInterface sevenZip, string archivePath)
    {
        var contents = await sevenZip.GetArchiveContents(archivePath, hideWindow: true,
            mirrorUIstr: _ => { }, suppressErrorDialog: true);
        // 7z l -slt reports the archive's own path as the first "Path = " entry; drop it.
        return contents.Where(x => !x.Equals(archivePath, StringComparison.OrdinalIgnoreCase)).ToList();
    }

    /// <summary>Lists an archive's file entries via the bundled 7-Zip.</summary>
    public static async Task<int> RunListAsync(CliOptions options, TextWriter resultWriter)
    {
        var sevenZip = new _7ZipInterface(new SevenZipEnvironmentShim());
        var contents = await ListArchiveFiles(sevenZip, options.ArchivePath!);
        if (!contents.Any())
        {
            Console.Error.WriteLine("Archive is empty or could not be read: " + options.ArchivePath);
            return 2;
        }
        if (options.Json)
        {
            resultWriter.WriteLine(JsonConvert.SerializeObject(
                new { Command = "archive-list", Archive = options.ArchivePath, Files = contents }, Formatting.Indented));
        }
        else
        {
            foreach (var entry in contents)
            {
                resultWriter.WriteLine(entry);
            }
        }
        return 0;
    }

    /// <summary>Extracts an archive via the bundled 7-Zip.</summary>
    public static async Task<int> RunExtractAsync(CliOptions options, TextWriter resultWriter)
    {
        Directory.CreateDirectory(options.DestDir!);
        var sevenZip = new _7ZipInterface(new SevenZipEnvironmentShim());
        bool success = await sevenZip.ExtractArchive(options.ArchivePath!, options.DestDir!, hideWindow: true,
            mirrorUIstr: line => Console.Error.WriteLine(line), suppressErrorDialog: true);
        if (!success)
        {
            Console.Error.WriteLine("Extraction failed: " + options.ArchivePath);
            return 2;
        }
        resultWriter.WriteLine(options.Json
            ? JsonConvert.SerializeObject(new { Command = "archive-extract", Archive = options.ArchivePath, ExtractedTo = options.DestDir }, Formatting.Indented)
            : "Extracted " + options.ArchivePath + " to " + options.DestDir);
        return 0;
    }

    /// <summary>
    /// Validates the manifest against the staged files: prefix present, every referenced asset-pack /
    /// record-template / BodyGen-config file exists, every referenced asset pack parses, and each pack's
    /// non-record-relative Source paths carry the manifest's prefix (or a DownloadInfo extraction subpath)
    /// as their second segment.
    /// </summary>
    private static void ValidateManifestContents(Manifest manifest, string staging, PackageReport report)
    {
        if (manifest.ConfigPrefix.IsNullOrWhitespace())
        {
            report.Errors.Add("Manifest does not include a ConfigPrefix. This must match the second directory of each asset path in the config files (e.g. textures\\PREFIX\\some\\texture.dds).");
        }

        var assetPackPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var recordTemplatePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var bodyGenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var extractionSubPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Collect(IEnumerable<string> assetPacks, IEnumerable<string> recordTemplates, IEnumerable<string> bodyGens, IEnumerable<Manifest.DownloadInfoContainer> downloads)
        {
            assetPackPaths.UnionWith(assetPacks);
            recordTemplatePaths.UnionWith(recordTemplates);
            bodyGenPaths.UnionWith(bodyGens);
            extractionSubPaths.UnionWith(downloads.Where(x => !x.ExtractionSubPath.IsNullOrWhitespace()).Select(x => x.ExtractionSubPath));
        }

        Collect(manifest.AssetPackPaths, manifest.RecordTemplatePaths, manifest.BodyGenConfigPaths, manifest.DownloadInfo);
        void Walk(Manifest.Option option)
        {
            Collect(option.AssetPackPaths, option.RecordTemplatePaths, option.BodyGenConfigPaths, option.DownloadInfo);
            foreach (var child in option.Options) { Walk(child); }
        }
        foreach (var option in manifest.Options) { Walk(option); }

        report.AssetPackCount = assetPackPaths.Count;
        report.RecordTemplateCount = recordTemplatePaths.Count;
        report.BodyGenConfigCount = bodyGenPaths.Count;

        if (!assetPackPaths.Any())
        {
            report.Warnings.Add("The manifest references no asset pack config files (neither at the root nor in any option).");
        }

        foreach (var relPath in assetPackPaths.Concat(recordTemplatePaths).Concat(bodyGenPaths))
        {
            if (!File.Exists(Path.Combine(staging, relPath)))
            {
                report.Errors.Add("Referenced file is missing from the staging folder: " + relPath);
            }
        }

        var validPrefixes = new HashSet<string>(extractionSubPaths, StringComparer.OrdinalIgnoreCase);
        if (!manifest.ConfigPrefix.IsNullOrWhitespace())
        {
            validPrefixes.Add(manifest.ConfigPrefix);
        }

        foreach (var relPath in assetPackPaths)
        {
            var fullPath = Path.Combine(staging, relPath);
            if (!File.Exists(fullPath)) { continue; }

            var assetPack = JSONhandler<AssetPack>.LoadJSONFile(fullPath, out bool packLoaded, out string packException);
            if (!packLoaded)
            {
                report.Errors.Add("Referenced asset pack could not be parsed: " + relPath + " (" + packException + ")");
                continue;
            }

            var badPrefixes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            void WalkSubgroups(IEnumerable<AssetPack.Subgroup> subgroups)
            {
                foreach (var subgroup in subgroups)
                {
                    foreach (var path in subgroup.Paths)
                    {
                        var segments = path.Source.Split('\\', '/');
                        if (segments.Length < 2) { continue; }
                        // Record-relative sources (e.g. "Skyrim.esm\...") are not file paths; skip them.
                        var firstSegment = segments[0];
                        if (firstSegment.EndsWith(".esp", StringComparison.OrdinalIgnoreCase)
                            || firstSegment.EndsWith(".esm", StringComparison.OrdinalIgnoreCase)
                            || firstSegment.EndsWith(".esl", StringComparison.OrdinalIgnoreCase)) { continue; }
                        // Dependency assets the package legitimately does not contain (e.g. TNG/SOS genital
                        // meshes under meshes\...) are declared in IgnoreMissingSourceFiles and live at vanilla
                        // paths, not under the config prefix; exempt them, exactly as verify-install does.
                        if (manifest.IgnoreMissingSourceFiles.Contains(path.Source, StringComparer.OrdinalIgnoreCase)) { continue; }
                        if (!validPrefixes.Contains(segments[1]))
                        {
                            badPrefixes.Add(segments[1]);
                        }
                    }
                    WalkSubgroups(subgroup.Subgroups);
                }
            }
            WalkSubgroups(assetPack.Subgroups);

            if (badPrefixes.Any())
            {
                report.Errors.Add("Asset pack " + relPath + " contains Source paths whose prefix (second folder) is not the manifest's ConfigPrefix or a DownloadInfo ExtractionSubPath: " + string.Join(", ", badPrefixes));
            }
        }
    }

    private static void EmitReport(PackageReport report, CliOptions options, TextWriter resultWriter)
    {
        if (options.Json)
        {
            resultWriter.WriteLine(JsonConvert.SerializeObject(report, Formatting.Indented));
            return;
        }
        resultWriter.WriteLine("Manifest: " + report.ConfigName + " (prefix " + report.ConfigPrefix + ") - "
            + report.AssetPackCount + " config(s), " + report.RecordTemplateCount + " record template(s), "
            + report.BodyGenConfigCount + " BodyGen config(s).");
        foreach (var warning in report.Warnings)
        {
            resultWriter.WriteLine("WARNING: " + warning);
        }
        foreach (var error in report.Errors)
        {
            resultWriter.WriteLine("ERROR: " + error);
        }
        if (report.ArchivePath != null)
        {
            resultWriter.WriteLine("Created " + report.ArchivePath + " (" + report.ArchivedFileCount + " files, "
                + (report.ArchiveSizeBytes / 1024) + " KB).");
        }
    }

    /// <summary>
    /// Minimal <see cref="IEnvironmentStateProvider"/> for verbs that only need the bundled 7-Zip:
    /// supplies the executable-relative paths and throws on game-environment members, so packaging and
    /// archive operations work without a Skyrim installation.
    /// </summary>
    private sealed class SevenZipEnvironmentShim : IEnvironmentStateProvider
    {
        public ILoadOrderGetter<IModListingGetter<ISkyrimModGetter>> LoadOrder => throw new NotSupportedException("No game environment in archive-only mode");
        public ILinkCache<ISkyrimMod, ISkyrimModGetter> LinkCache => throw new NotSupportedException("No game environment in archive-only mode");
        public DirectoryPath ExtraSettingsDataPath { get; } = Path.Combine(AppContext.BaseDirectory, "Settings");
        public DirectoryPath InternalDataPath { get; } = Path.Combine(AppContext.BaseDirectory, "InternalData");
        public DirectoryPath DataFolderPath { get; set; } = string.Empty;
        public EnvironmentMode RunMode => EnvironmentMode.Standalone;
        public LogMode LoggerMode => LogMode.Synthesis;
        public string LogFolderPath { get; } = Path.Combine(AppContext.BaseDirectory, "Logs");
        public string OutputModName { get; set; } = "SynthEBD";
        public SkyrimRelease SkyrimVersion => SkyrimRelease.SkyrimSE;
        public string CreationClubListingsFilePath => string.Empty;
        public string LoadOrderFilePath => string.Empty;
        public List<string> StartUpLog { get; set; } = new();
    }
}
