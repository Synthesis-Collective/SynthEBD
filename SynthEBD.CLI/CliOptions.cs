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
    VerifyInstall,
    UiScreenshot,
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

    /// <summary>Folder containing the dependency archives (matched by DownloadInfo.ExpectedFileName) for verify-install.</summary>
    public string? DownloadsDir { get; private set; }

    /// <summary>Menus to capture for ui-screenshot, matched case-insensitively against the displayed
    /// view-model name without its VM_ prefix (e.g. "Settings_General") or the nav command name without
    /// its Click prefix (e.g. "SG"). Empty = capture every menu.</summary>
    public List<string> Menus { get; } = new();

    /// <summary>Themes to sweep for ui-screenshot (each applied via ThemeManager before its pass).
    /// Empty = just the default theme.</summary>
    public List<string> Themes { get; } = new();

    /// <summary>Disclosure modes to sweep for ui-screenshot. Empty = whatever mode the loaded
    /// settings put the UI in.</summary>
    public List<UiDisplayMode> Modes { get; } = new();

    /// <summary>Window size forced onto MainWindow for deterministic ui-screenshot captures.</summary>
    public int WindowWidth { get; private set; } = 1600;
    public int WindowHeight { get; private set; } = 1000;

    /// <summary>Milliseconds ui-screenshot waits after layout settles before capturing each menu,
    /// letting asynchronously-loaded content (preview images, GL surfaces) appear.</summary>
    public int SettleMs { get; private set; } = 250;

    /// <summary>When set, ui-screenshot expands every Expander in the visual tree before capturing.</summary>
    public bool ExpandExpanders { get; private set; }

    /// <summary>ui-screenshot sub-navigation: "MenuName.CommandProperty" pairs executed after a nav
    /// flip whose displayed menu matches MenuName (same matching rule as <see cref="Menus"/>). Lets
    /// captures reach inner tabs the top-level nav can't (e.g. "SettingsOBody.ClickAnnotationMenu").</summary>
    public List<string> Invokes { get; } = new();

    /// <summary>ui-screenshot scroll target: the DocTooltip.Key of an element (e.g.
    /// "General.AttributeGroups") to scroll to the top of its owning ScrollViewer before capturing,
    /// so below-the-fold controls on long settings pages can be captured. Applied after
    /// --expand-expanders so content inside expanders is addressable.</summary>
    public string? ScrollTo { get; private set; }

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
  verify-install   Verify a packaged (or staged) config archive installs correctly for EVERY possible
               installer selection: enumerates all option chains and checks that each chain's files
               exist and that every config Source path resolves in the simulated install tree
               (config archive contents + dependency archive contents, prefix-routed).
  archive-list     List the file entries of a 7z/zip/rar archive via the bundled 7-Zip.
  archive-extract  Extract a 7z/zip/rar archive via the bundled 7-Zip.
  ui-screenshot    Show the real SynthEBD main window against the real settings/environment, flip
               through every navigation menu, and capture a PNG of each (automated visual QA).
               Never writes settings back to disk.
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
  --staging <dir>          Folder containing Manifest.json plus the staged configs/templates to package
                           (package, verify-install).
  --out <file>             Output archive path (default: <ConfigName>.7z next to the staging folder).
  --archive <file>         Archive to list/extract/verify.
  --dest <dir>             Destination folder for archive-extract (created if missing).
  --downloads <dir>        Folder containing the dependency archives the manifest's DownloadInfo
                           references (matched by ExpectedFileName); used by verify-install.

UI-SCREENSHOT OPTIONS:
  --out <dir>              Output folder for the captured PNGs (required; created if missing).
  --menu <name>            Capture only this menu, matched against the displayed view-model name
                           without the VM_ prefix (e.g. Settings_General, SettingsTexMesh) or the
                           nav command suffix (e.g. SG, TM). Repeatable. Default: every menu.
  --theme <name>           Sweep this theme (a Themes\*.xaml name, e.g. Dark). Repeatable.
                           Default: the default theme only. PNGs land in <out>\<theme>\<mode>\.
  --mode <name>            Sweep this disclosure mode (Use, Customize, Troubleshoot). Repeatable.
                           Default: the mode the loaded settings put the UI in.
  --width <px>             Window width for the capture (default 1600).
  --height <px>            Window height for the capture (default 1000).
  --settle-ms <n>          Wait after layout settles before each capture (default 250).
  --expand-expanders       Expand every Expander in the menu before capturing.
  --invoke <menu.command>  After flipping to a menu that matches <menu> (same matching as --menu),
                           execute the named ICommand property on its view model before capturing —
                           reaches inner tabs the nav panel can't (e.g.
                           SettingsOBody.ClickAnnotationMenu). Repeatable, and applied in the order
                           given, so an earlier --invoke can open the tab a later one acts on.
                           The part after the FIRST dot is a property PATH, so commands on nested
                           view models are reachable too (e.g.
                           SettingsOBody.DisplayedUI.PreviewPanel.CharacterViewer.CompareCommand).
                           Any window an --invoke opens is captured as well, to
                           <menu>-window-<title>.png, and then closed.
  --scroll-to <dockey>     Scroll the element carrying this DocTooltip.Key (e.g.
                           General.AttributeGroups) to the top of its ScrollViewer before capturing,
                           so below-the-fold controls on long settings pages land in the shot.
                           Applied after --expand-expanders.

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
            "verify-install" => CliVerb.VerifyInstall,
            "archive-list" => CliVerb.ArchiveList,
            "archive-extract" => CliVerb.ArchiveExtract,
            "ui-screenshot" => CliVerb.UiScreenshot,
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
                case "--downloads":
                    options.DownloadsDir = TakeDirectoryValue(args, ref i, flag);
                    break;
                case "--menu":
                    options.Menus.Add(TakeValue(args, ref i, flag));
                    break;
                case "--theme":
                    options.Themes.Add(TakeValue(args, ref i, flag));
                    break;
                case "--mode":
                    var modeValue = TakeValue(args, ref i, flag);
                    if (!Enum.TryParse<UiDisplayMode>(modeValue, ignoreCase: true, out var uiMode))
                    {
                        throw new CliArgumentException("Unrecognized --mode \"" + modeValue + "\". Valid values: " +
                                                       string.Join(", ", Enum.GetNames<UiDisplayMode>()));
                    }
                    options.Modes.Add(uiMode);
                    break;
                case "--width":
                    options.WindowWidth = TakePositiveIntValue(args, ref i, flag);
                    break;
                case "--height":
                    options.WindowHeight = TakePositiveIntValue(args, ref i, flag);
                    break;
                case "--settle-ms":
                    var settleStr = TakeValue(args, ref i, flag);
                    if (!int.TryParse(settleStr, out int settleMs) || settleMs < 0)
                    {
                        throw new CliArgumentException("--settle-ms requires a non-negative integer, got \"" + settleStr + "\"");
                    }
                    options.SettleMs = settleMs;
                    break;
                case "--expand-expanders":
                    options.ExpandExpanders = true;
                    break;
                case "--invoke":
                    var invokeValue = TakeValue(args, ref i, flag);
                    if (!invokeValue.Contains('.'))
                    {
                        throw new CliArgumentException("--invoke requires \"MenuName.CommandProperty\", got \"" + invokeValue + "\"");
                    }
                    options.Invokes.Add(invokeValue);
                    break;
                case "--scroll-to":
                    options.ScrollTo = TakeValue(args, ref i, flag);
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
        if (options.Verb == CliVerb.VerifyInstall && (options.ArchivePath == null) == (options.StagingDir == null))
        {
            throw new CliArgumentException("verify-install requires exactly one of --archive or --staging");
        }
        if (options.Verb == CliVerb.UiScreenshot && options.OutPath == null)
        {
            throw new CliArgumentException("ui-screenshot requires --out <dir>");
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

    /// <summary>Like <see cref="TakeValue"/> but requires a positive integer.</summary>
    private static int TakePositiveIntValue(string[] args, ref int i, string flag)
    {
        var value = TakeValue(args, ref i, flag);
        if (!int.TryParse(value, out int result) || result < 1)
        {
            throw new CliArgumentException(flag + " requires a positive integer, got \"" + value + "\"");
        }
        return result;
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
