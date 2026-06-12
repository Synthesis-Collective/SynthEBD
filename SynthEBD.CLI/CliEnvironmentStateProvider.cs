using System.IO;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Environments;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Plugins.Order;
using Mutagen.Bethesda.Skyrim;
using Noggog;

namespace SynthEBD.CLI;

/// <summary>
/// Headless <see cref="IOutputEnvironmentStateProvider"/> for CLI runs. Builds a real Mutagen game
/// environment using the same builder pipeline as <see cref="StandaloneRunEnvironmentStateProvider"/> —
/// including <see cref="LoadOrderExtensions.RemoveModAndDependents"/> and <c>WithOutputMod</c> — but
/// <b>without</b> the WPF "pick a custom environment" dialog fallback, so it can never block a console
/// session; failures surface as an error string via <see cref="TryBuild"/> instead.
///
/// <para><see cref="LoggerMode"/> is <see cref="LogMode.Synthesis"/> so the <see cref="Logger"/> writes to
/// the console (redirected to stderr by <c>Program</c>) rather than marshalling to the windowless UI log
/// pane. Bundled <c>InternalData</c> resources are read from this executable's directory, where the
/// SynthEBD project reference copies them.</para>
/// </summary>
public sealed class CliEnvironmentStateProvider : IOutputEnvironmentStateProvider
{
    private readonly IGameEnvironment<ISkyrimMod, ISkyrimModGetter> _environment;

    public ILoadOrderGetter<IModListingGetter<ISkyrimModGetter>> LoadOrder => _environment.LoadOrder;
    public ILinkCache<ISkyrimMod, ISkyrimModGetter> LinkCache => _environment.LinkCache;
    public SkyrimRelease SkyrimVersion { get; }
    public DirectoryPath ExtraSettingsDataPath { get; }
    public DirectoryPath InternalDataPath { get; }
    public DirectoryPath DataFolderPath { get; set; }
    public ISkyrimMod OutputMod { get; }
    public EnvironmentMode RunMode => EnvironmentMode.Standalone;
    public LogMode LoggerMode => LogMode.Synthesis;
    public string OutputModName { get; set; }
    public string LogFolderPath { get; }
    public string CreationClubListingsFilePath { get; }
    public string LoadOrderFilePath { get; }
    public List<string> StartUpLog { get; set; } = new();

    private CliEnvironmentStateProvider(SkyrimRelease skyrimVersion, string? dataFolderOverride,
        string outputModName, string extraSettingsDataPath)
    {
        SkyrimVersion = skyrimVersion;
        OutputModName = outputModName;
        ExtraSettingsDataPath = extraSettingsDataPath;

        var baseDir = AppContext.BaseDirectory;
        InternalDataPath = Path.Combine(baseDir, "InternalData");
        LogFolderPath = Path.Combine(baseDir, "Logs");

        OutputMod = new SkyrimMod(ModKey.FromName(OutputModName, ModType.Plugin), SkyrimVersion);

        // Same build block as StandaloneRunEnvironmentStateProvider.UpdateEnvironment, minus the dialog.
        var builder = GameEnvironment.Typical.Builder<ISkyrimMod, ISkyrimModGetter>(SkyrimVersion.ToGameRelease());
        if (!dataFolderOverride.IsNullOrWhitespace())
        {
            builder = builder.WithTargetDataFolder(dataFolderOverride);
        }
        _environment = builder
            .TransformModListings(x =>
                x.OnlyEnabledAndExisting()
                 .RemoveModAndDependents(OutputModName, verbose: false, out _))
            .WithOutputMod(OutputMod)
            .Build();

        DataFolderPath = _environment.DataFolderPath;
        CreationClubListingsFilePath = _environment.CreationClubListingsFilePath;
        LoadOrderFilePath = _environment.LoadOrderFilePath;
    }

    /// <summary>
    /// Attempts to build a game environment for the given release/data folder. Returns <c>false</c> with a
    /// human-readable reason when no usable environment is found — e.g. the game is not installed, the
    /// load-order file is missing, or the resolved load order contains only the output mod.
    /// </summary>
    public static bool TryBuild(SkyrimRelease skyrimVersion, string? dataFolderOverride, string outputModName,
        string extraSettingsDataPath, out CliEnvironmentStateProvider? provider, out string failureReason)
    {
        provider = null;
        failureReason = string.Empty;
        try
        {
            var candidate = new CliEnvironmentStateProvider(skyrimVersion, dataFolderOverride, outputModName,
                extraSettingsDataPath);
            // A listed order of just the output mod means no real game data was found.
            if (candidate.LinkCache.ListedOrder.Count <= 1)
            {
                failureReason = "No " + skyrimVersion + " load order was resolved (only the output mod is present). " +
                                "Check the game installation, or pass --data-folder / --skyrim-version explicitly.";
                return false;
            }
            provider = candidate;
            return true;
        }
        catch (Exception ex)
        {
            failureReason = "Could not build a " + skyrimVersion + " game environment: " + ex.Message;
            return false;
        }
    }
}
