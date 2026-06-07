using System.IO;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Environments;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Plugins.Order;
using Mutagen.Bethesda.Skyrim;
using Noggog;

namespace SynthEBD.Tests.Integration;

/// <summary>
/// Headless <see cref="IOutputEnvironmentStateProvider"/> for integration tests. Builds a real Mutagen
/// Skyrim SE game environment from the default (auto-detected) install using the same builder pipeline as
/// <see cref="StandaloneRunEnvironmentStateProvider"/> — including the existing
/// <see cref="LoadOrderExtensions.RemoveModAndDependents"/> filter and <c>WithOutputMod</c> — but
/// <b>without</b> the WPF "pick a custom environment" dialog fallback, so it never blocks a test.
///
/// <para><see cref="RunMode"/> is <see cref="EnvironmentMode.Standalone"/> so that
/// <c>Patcher.RunPatcher()</c> writes the output plugin to disk at the end of the run (the Synthesis run
/// mode leaves that to the host pipeline). Bundled <c>InternalData</c> resources are read from the test
/// assembly's output directory, where the SynthEBD project copies them.</para>
/// </summary>
public sealed class TestEnvironmentStateProvider : IOutputEnvironmentStateProvider
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
    // Console logging keeps test output visible and avoids marshaling to the (windowless) UI log pane.
    public LogMode LoggerMode => LogMode.Synthesis;
    public string OutputModName { get; set; }
    public string LogFolderPath { get; }
    public string CreationClubListingsFilePath { get; }
    public string LoadOrderFilePath { get; }
    public List<string> StartUpLog { get; set; } = new();

    private TestEnvironmentStateProvider(string outputModName, string logFolderPath)
    {
        SkyrimVersion = SkyrimRelease.SkyrimSE;
        OutputModName = outputModName;
        LogFolderPath = logFolderPath;

        var baseDir = AppContext.BaseDirectory;
        ExtraSettingsDataPath = baseDir;
        InternalDataPath = Path.Combine(baseDir, "InternalData");

        OutputMod = new SkyrimMod(ModKey.FromName(OutputModName, ModType.Plugin), SkyrimVersion);

        // Same build block as StandaloneRunEnvironmentStateProvider.UpdateEnvironment, minus the dialog.
        var builder = GameEnvironment.Typical.Builder<ISkyrimMod, ISkyrimModGetter>(SkyrimVersion.ToGameRelease());
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
    /// Attempts to build a Skyrim SE environment from the default install. Returns <c>false</c> (with a
    /// human-readable reason) when no usable environment is found — e.g. Skyrim SE is not installed on the
    /// test machine, the load-order file is missing, or the resolved load order contains only the output
    /// mod. Tests use this to skip gracefully rather than fail on CI.
    /// </summary>
    public static bool TryBuild(string outputModName, string logFolderPath,
        out TestEnvironmentStateProvider? provider, out string skipReason)
    {
        provider = null;
        skipReason = string.Empty;
        try
        {
            var candidate = new TestEnvironmentStateProvider(outputModName, logFolderPath);
            // A listed order of just the output mod means no real game data was found.
            if (candidate.LinkCache.ListedOrder.Count <= 1)
            {
                skipReason = "No Skyrim SE load order was resolved (only the output mod is present). " +
                             "A Skyrim SE installation is required to run patcher integration tests.";
                return false;
            }
            provider = candidate;
            return true;
        }
        catch (Exception ex)
        {
            skipReason = "Could not build a Skyrim SE game environment: " + ex.Message;
            return false;
        }
    }
}
