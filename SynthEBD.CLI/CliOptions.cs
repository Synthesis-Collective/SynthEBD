using Mutagen.Bethesda.Skyrim;

namespace SynthEBD.CLI;

/// <summary>The CLI verbs supported by SynthEBD.CLI.</summary>
public enum CliVerb
{
    Help,
    Validate,
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

    public const string UsageText = @"SynthEBD.CLI - headless tooling for SynthEBD config authoring

USAGE:
  SynthEBD.CLI <verb> [options]

VERBS:
  validate     Validate asset-pack config files (same checks as the GUI Validate button).
  help         Show this help.

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

EXIT CODES:
  0  success / all configs valid
  1  validation errors found
  2  fatal error (bad arguments, environment/settings failed to load, config not found)";

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
                default:
                    throw new CliArgumentException("Unknown option: " + flag);
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
