using Mutagen.Bethesda.Skyrim;
using Noggog;

namespace SynthEBD.CLI;

/// <summary>The CLI verbs supported by SynthEBD.CLI.</summary>
public enum CliVerb
{
    Help,
    Validate,
    Scan,
    Draft,
    Simulate,
    Package,
    ArchiveList,
    ArchiveExtract,
}

/// <summary>How the draft verb disposes of duplicate (byte-identical) textures.</summary>
public enum CliMultipletMode
{
    /// <summary>Point all subgroups at each duplicate group's keeper (the GUI's "Replace With Primary").</summary>
    Replace,
    /// <summary>Skip non-keeper duplicates entirely (the GUI's "Ignore Non-Primary").</summary>
    Ignore,
    /// <summary>Do not check for duplicates; draft every texture as-is.</summary>
    None,
}

/// <summary>Thrown by <see cref="CliOptions.Parse"/> when the command line is malformed.</summary>
public class CliArgumentException : Exception
{
    public CliArgumentException(string message) : base(message) { }
}

/// <summary>
/// Parsed command-line options for SynthEBD.CLI. Hand-rolled parsing: first positional argument is the
/// verb, followed by <c>--flag value</c> pairs (list-valued flags are repeatable).
/// </summary>
public class CliOptions
{
    public CliVerb Verb { get; private set; } = CliVerb.Help;

    /// <summary>SynthEBD installation folder whose settings tree to load (mirrors the GUI's standalone
    /// startup, including any portable-settings redirect its SettingsSource.json declares). Defaults to
    /// the folder containing this executable, which is correct when the CLI ships alongside SynthEBD.exe.</summary>
    public string SynthEbdPath { get; private set; } = AppContext.BaseDirectory;

    /// <summary>When set, operate directly on this portable settings tree (the folder containing
    /// <c>Settings\</c>, <c>Asset Packs\</c>, <c>Record Templates\</c>, ...), overriding
    /// <see cref="SynthEbdPath"/> — the same pattern the integration-test harness uses.</summary>
    public string? SettingsRoot { get; private set; }

    /// <summary>Configs to operate on, matched against GroupName or config file name (no extension).
    /// Empty = all installed configs.</summary>
    public List<string> ConfigNames { get; } = new();

    /// <summary>Extra root folders probed for Source assets (e.g. the working folder a texture mod was
    /// extracted to). Forwarded to <see cref="AssetPackValidator.ExtraAssetRoots"/>.</summary>
    public List<string> AssetRoots { get; } = new();

    /// <summary>Optional override of the game Data folder used to build the environment.</summary>
    public string? DataFolder { get; private set; }

    /// <summary>Optional override of the Skyrim release; defaults to the instance's EnvironmentSource.json.</summary>
    public SkyrimRelease? SkyrimVersion { get; private set; }

    /// <summary>Optional override of the output plugin name used when filtering the load order.</summary>
    public string? OutputModName { get; private set; }

    /// <summary>Emit machine-readable JSON to stdout instead of human-readable text.</summary>
    public bool Json { get; private set; }

    /// <summary>Texture root folder(s) to scan/draft from. With the default layout each root is the
    /// extraction working folder, laid out like a mod folder (containing <c>textures\&lt;prefix&gt;\...</c>).</summary>
    public List<string> Roots { get; } = new();

    /// <summary>When set, each root is itself a <c>...\Textures\&lt;Prefix&gt;</c> folder (the drafter's
    /// non-mod-manager layout) rather than a mod-style folder containing one.</summary>
    public bool RootsHavePrefix { get; private set; }

    /// <summary>GroupName for the drafted config (also the saved file name).</summary>
    public string? ConfigName { get; private set; }

    /// <summary>Prefix (ShortName) for the drafted config.</summary>
    public string? Prefix { get; private set; }

    /// <summary>Duplicate-texture disposition for the draft verb.</summary>
    public CliMultipletMode MultipletMode { get; private set; } = CliMultipletMode.Replace;

    /// <summary>Unmatched (uncategorized) texture paths to keep as Unknown-type subgroups; all other
    /// unmatched textures are ignored. Paths as reported by the scan verb (root-relative).</summary>
    public List<string> KeepUnmatched { get; } = new();

    /// <summary>Keep every unmatched texture.</summary>
    public bool KeepAllUnmatched { get; private set; }

    /// <summary>Ignore every unmatched texture.</summary>
    public bool IgnoreAllUnmatched { get; private set; }

    /// <summary>Keeper overrides for duplicate groups: each path (as reported by scan) becomes its
    /// group's kept/source texture instead of the auto-suggested one.</summary>
    public List<string> Keepers { get; } = new();

    /// <summary>Body family for "etc" body textures (3BA or BHUNP); required when the drafter detects them.</summary>
    public DrafterBodyType? EtcBody { get; private set; }

    /// <summary>Drafter auto-assign toggles (all default on, matching the GUI).</summary>
    public bool AutoNames { get; private set; } = true;
    public bool AutoRules { get; private set; } = true;
    public bool AutoLinkage { get; private set; } = true;

    /// <summary>Optional explicit output file for the drafted config; default is the instance's Asset Packs folder.</summary>
    public string? OutPath { get; private set; }

    /// <summary>NPCs to simulate distribution for: FormKeys ("123456:Skyrim.esm") or EditorIDs ("Hulda").</summary>
    public List<string> Npcs { get; } = new();

    /// <summary>Number of selection rounds per NPC for the simulate verb.</summary>
    public int Repetitions { get; private set; } = 100;

    /// <summary>When set, the simulate verb writes each NPC's full verbose report (XML) into this folder.</summary>
    public string? FullReportDir { get; private set; }

    /// <summary>Staging folder (containing Manifest.json) for the package verb.</summary>
    public string? StagingDir { get; private set; }

    /// <summary>Archive file for archive-list / archive-extract.</summary>
    public string? ArchivePath { get; private set; }

    /// <summary>Destination folder for archive-extract.</summary>
    public string? DestDir { get; private set; }

    public const string UsageText = @"SynthEBD.CLI - headless tooling for SynthEBD config authoring

USAGE:
  SynthEBD.CLI <verb> [options]

VERBS:
  validate     Validate asset-pack config files (same checks as the GUI Validate button).
  scan         Categorize the textures in a working folder and report unmatched textures and
               byte-identical duplicate groups (with suggested keepers). Run before drafting.
  draft        Draft a new asset-pack config from a working folder (the GUI Config Drafter, headless).
  simulate     Simulate primary asset distribution for specific NPCs (the GUI Distribution Simulator,
               headless): per-pack and per-subgroup assignment counts plus log-derived explanations
               for subgroups that never get assigned.
  package      Validate a staged config folder against its Manifest.json and 7-zip it into a
               distributable archive. (No game environment needed.)
  archive-list     List the file entries of a 7z/zip/rar archive via the bundled 7-Zip.
  archive-extract  Extract a 7z/zip/rar archive via the bundled 7-Zip.
  help         Show this help.

WORKING-FOLDER LAYOUT (scan/draft):
  Extract every archive of the texture mod into ONE working folder laid out like a mod:
    <working folder>\textures\<Prefix>\<archive contents...>
  Pass the working folder via --root. Use a separate <Prefix> per archive if archives share
  identical internal paths. (--roots-have-prefix instead treats each root as a
  ...\Textures\<Prefix> folder itself.)

COMMON OPTIONS:
  --synthebd-path <dir>    SynthEBD installation folder whose settings to load (honors any portable
                           settings redirect). Default: the folder containing this executable.
  --settings-root <dir>    Operate directly on a settings tree (the folder containing Settings\,
                           Asset Packs\, Record Templates\, ...). Overrides --synthebd-path.
  --data-folder <dir>      Override the game Data folder used to build the environment.
  --skyrim-version <ver>   Override the Skyrim release (e.g. SkyrimSE, SkyrimSEGog, SkyrimVR).
  --output-mod <name>      Override the output plugin name excluded from the load order.
  --json                   Emit machine-readable JSON to stdout. All logging goes to stderr either way,
                           so stdout stays parseable.

VALIDATE OPTIONS:
  --config <name>          Validate only the named config (GroupName, or file name without extension).
                           Repeatable. Default: validate all installed configs.
  --asset-root <dir>       Extra root folder probed for Source files, e.g. the working folder the
                           texture mod was extracted to. Repeatable.

SCAN / DRAFT OPTIONS:
  --root <dir>             Working folder to scan/draft from. Repeatable.
  --roots-have-prefix      Roots are ...\Textures\<Prefix> folders themselves (see layout note above).

DRAFT OPTIONS:
  --name <text>            GroupName for the drafted config (required; must be a valid file name).
  --prefix <text>          Short prefix (ShortName) for the config (required), e.g. ""BnP4K_C"".
  --multiplet-mode <mode>  replace (default) | ignore | none. How byte-identical duplicate textures
                           are handled. replace points subgroups at each group's keeper; ignore
                           skips non-keepers; none skips the duplicate check entirely.
  --keeper <path>          Override the suggested keeper of the duplicate group containing this
                           path (path as reported by scan). Repeatable.
  --keep-unmatched <path>  Keep this unmatched texture (as reported by scan); all other unmatched
                           textures are ignored. Repeatable.
  --keep-all-unmatched     Keep every unmatched texture.
  --ignore-all-unmatched   Ignore every unmatched texture.
                           (If unmatched textures exist, one of the three options above is required.)
  --etc-body <type>        3BA | BHUNP. Required when the mod contains ""etc"" body textures, which
                           need body-specific record templates.
  --no-auto-names          Disable the drafter's automatic subgroup naming.
  --no-auto-rules          Disable the drafter's automatic distribution rules.
  --no-auto-linkage        Disable automatic Required-Subgroup linkage of same-named subgroups
                           (recommended for very complex mods; linkage errors can block distribution).
  --out <file>             Save the drafted config to this file instead of the Asset Packs folder.

SIMULATE OPTIONS:
  --npc <id>               NPC to simulate, as a FormKey (""013BA3:Skyrim.esm"") or EditorID (""Hulda"").
                           Repeatable.
  --config <name>          Simulate only the named Primary config(s) (the GUI workflow of deselecting
                           all others). Repeatable. Default: the configs currently selected in the
                           Textures and Meshes menu.
  --repetitions <n>        Selection rounds per NPC (default 100; use 2-3 for very large fresh drafts).
  --full-report-dir <dir>  Write each NPC's full verbose report (XML) into this folder for deep
                           debugging of distribution failures.

PACKAGE / ARCHIVE OPTIONS:
  --staging <dir>          Folder containing Manifest.json plus the staged configs/templates to package.
  --out <file>             Output archive path (default: <ConfigName>.7z next to the staging folder).
  --archive <file>         Archive to list/extract.
  --dest <dir>             Destination folder for archive-extract (created if missing).

EXIT CODES:
  0  success / all configs valid / every simulated NPC received assignments
  1  validation errors found / at least one simulated NPC received no assignments
  2  fatal error (bad arguments, environment/settings failed to load, config/NPC not found)";

    /// <summary>Parses the raw command line, throwing <see cref="CliArgumentException"/> on malformed input.</summary>
    public static CliOptions Parse(string[] args)
    {
        var options = new CliOptions();

        if (args.Length == 0)
        {
            return options; // Help
        }

        options.Verb = args[0].ToLowerInvariant() switch
        {
            "validate" => CliVerb.Validate,
            "scan" => CliVerb.Scan,
            "draft" => CliVerb.Draft,
            "simulate" => CliVerb.Simulate,
            "package" => CliVerb.Package,
            "archive-list" => CliVerb.ArchiveList,
            "archive-extract" => CliVerb.ArchiveExtract,
            "help" or "--help" or "-h" or "-?" or "/?" => CliVerb.Help,
            _ => throw new CliArgumentException("Unknown verb: " + args[0]),
        };

        for (int i = 1; i < args.Length; i++)
        {
            var flag = args[i];
            switch (flag.ToLowerInvariant())
            {
                case "--json":
                    options.Json = true;
                    break;
                case "--synthebd-path":
                    options.SynthEbdPath = TakeDirectoryValue(args, ref i, flag);
                    break;
                case "--settings-root":
                    options.SettingsRoot = TakeDirectoryValue(args, ref i, flag);
                    break;
                case "--config":
                    options.ConfigNames.Add(TakeValue(args, ref i, flag));
                    break;
                case "--asset-root":
                    options.AssetRoots.Add(TakeDirectoryValue(args, ref i, flag));
                    break;
                case "--data-folder":
                    options.DataFolder = TakeDirectoryValue(args, ref i, flag);
                    break;
                case "--skyrim-version":
                    var versionStr = TakeValue(args, ref i, flag);
                    if (!Enum.TryParse<SkyrimRelease>(versionStr, ignoreCase: true, out var release))
                    {
                        throw new CliArgumentException("Unrecognized Skyrim release \"" + versionStr + "\". Valid values: " +
                                                       string.Join(", ", Enum.GetNames<SkyrimRelease>()));
                    }
                    options.SkyrimVersion = release;
                    break;
                case "--output-mod":
                    options.OutputModName = TakeValue(args, ref i, flag);
                    break;
                case "--root":
                    options.Roots.Add(TakeDirectoryValue(args, ref i, flag));
                    break;
                case "--roots-have-prefix":
                    options.RootsHavePrefix = true;
                    break;
                case "--name":
                    options.ConfigName = TakeValue(args, ref i, flag);
                    break;
                case "--prefix":
                    options.Prefix = TakeValue(args, ref i, flag);
                    break;
                case "--multiplet-mode":
                    var modeStr = TakeValue(args, ref i, flag);
                    if (!Enum.TryParse<CliMultipletMode>(modeStr, ignoreCase: true, out var mode))
                    {
                        throw new CliArgumentException("Unrecognized multiplet mode \"" + modeStr + "\". Valid values: replace, ignore, none");
                    }
                    options.MultipletMode = mode;
                    break;
                case "--keeper":
                    options.Keepers.Add(TakeValue(args, ref i, flag));
                    break;
                case "--keep-unmatched":
                    options.KeepUnmatched.Add(TakeValue(args, ref i, flag));
                    break;
                case "--keep-all-unmatched":
                    options.KeepAllUnmatched = true;
                    break;
                case "--ignore-all-unmatched":
                    options.IgnoreAllUnmatched = true;
                    break;
                case "--etc-body":
                    var bodyStr = TakeValue(args, ref i, flag);
                    options.EtcBody = bodyStr.ToLowerInvariant() switch
                    {
                        "3ba" or "cbbe_3ba" or "cbbe-3ba" or "cbbe3ba" => DrafterBodyType.CBBE_3BA,
                        "bhunp" => DrafterBodyType.BHUNP,
                        _ => throw new CliArgumentException("Unrecognized --etc-body value \"" + bodyStr + "\". Valid values: 3BA, BHUNP"),
                    };
                    break;
                case "--no-auto-names":
                    options.AutoNames = false;
                    break;
                case "--no-auto-rules":
                    options.AutoRules = false;
                    break;
                case "--no-auto-linkage":
                    options.AutoLinkage = false;
                    break;
                case "--out":
                    options.OutPath = System.IO.Path.GetFullPath(TakeValue(args, ref i, flag));
                    break;
                case "--npc":
                    options.Npcs.Add(TakeValue(args, ref i, flag));
                    break;
                case "--repetitions":
                    var repStr = TakeValue(args, ref i, flag);
                    if (!int.TryParse(repStr, out int repetitions) || repetitions < 1)
                    {
                        throw new CliArgumentException("--repetitions requires a positive integer, got \"" + repStr + "\"");
                    }
                    options.Repetitions = repetitions;
                    break;
                case "--full-report-dir":
                    options.FullReportDir = System.IO.Path.GetFullPath(TakeValue(args, ref i, flag));
                    break;
                case "--staging":
                    options.StagingDir = TakeDirectoryValue(args, ref i, flag);
                    break;
                case "--archive":
                    var archivePath = TakeValue(args, ref i, flag);
                    if (!System.IO.File.Exists(archivePath))
                    {
                        throw new CliArgumentException("--archive file does not exist: " + archivePath);
                    }
                    options.ArchivePath = System.IO.Path.GetFullPath(archivePath);
                    break;
                case "--dest":
                    options.DestDir = System.IO.Path.GetFullPath(TakeValue(args, ref i, flag));
                    break;
                default:
                    throw new CliArgumentException("Unknown option: " + flag);
            }
        }

        if (options.Verb is CliVerb.Scan or CliVerb.Draft && !options.Roots.Any())
        {
            throw new CliArgumentException(args[0].ToLowerInvariant() + " requires at least one --root");
        }
        if (options.Verb == CliVerb.Simulate && !options.Npcs.Any())
        {
            throw new CliArgumentException("simulate requires at least one --npc");
        }
        if (options.Verb == CliVerb.Package && options.StagingDir == null)
        {
            throw new CliArgumentException("package requires --staging");
        }
        if (options.Verb is CliVerb.ArchiveList or CliVerb.ArchiveExtract && options.ArchivePath == null)
        {
            throw new CliArgumentException(args[0].ToLowerInvariant() + " requires --archive");
        }
        if (options.Verb == CliVerb.ArchiveExtract && options.DestDir == null)
        {
            throw new CliArgumentException("archive-extract requires --dest");
        }
        if (options.Verb == CliVerb.Draft)
        {
            if (options.ConfigName.IsNullOrWhitespace())
            {
                throw new CliArgumentException("draft requires --name");
            }
            if (options.Prefix.IsNullOrWhitespace())
            {
                throw new CliArgumentException("draft requires --prefix");
            }
            if (options.KeepAllUnmatched && options.IgnoreAllUnmatched)
            {
                throw new CliArgumentException("--keep-all-unmatched and --ignore-all-unmatched are mutually exclusive");
            }
        }

        return options;
    }

    /// <summary>Consumes the value following <paramref name="flag"/>, advancing the index.</summary>
    private static string TakeValue(string[] args, ref int i, string flag)
    {
        if (i + 1 >= args.Length || args[i + 1].StartsWith("--", StringComparison.Ordinal))
        {
            throw new CliArgumentException(flag + " requires a value");
        }
        i++;
        return args[i];
    }

    /// <summary>Like <see cref="TakeValue"/> but also requires the value to be an existing directory,
    /// returned as a full path.</summary>
    private static string TakeDirectoryValue(string[] args, ref int i, string flag)
    {
        var value = TakeValue(args, ref i, flag);
        if (!System.IO.Directory.Exists(value))
        {
            throw new CliArgumentException(flag + " directory does not exist: " + value);
        }
        return System.IO.Path.GetFullPath(value);
    }
}
