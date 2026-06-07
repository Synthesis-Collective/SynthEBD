using System.IO;
using Autofac;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;
using Noggog;

namespace SynthEBD.Tests.Integration;

/// <summary>
/// Reusable integration harness that stands up the full SynthEBD object graph against a real Mutagen
/// Skyrim SE environment and a committed set of demo settings, then runs the patcher headlessly.
///
/// <para>It mirrors the container wiring in <c>App.RunPatch</c>: register the settings/environment source
/// providers, register a <see cref="TestEnvironmentStateProvider"/> behind its interfaces, add
/// <see cref="MainModule"/>, then load all settings via <c>SaveLoader</c>. The demo settings tree is
/// cloned into a throwaway temp directory and the loader is driven in <b>portable mode</b> so
/// <c>SynthEBDPaths</c> roots itself at that directory (giving the standard <c>Settings/</c>,
/// <c>Asset Packs/</c>, etc. layout). Patcher output is written to a separate temp folder so tests can
/// assert on generated records and files without touching the committed fixtures.</para>
///
/// <para>Construct via <see cref="TryCreate"/>, which returns <c>null</c> (with a skip reason) when no
/// Skyrim SE environment is available so tests can skip gracefully on CI.</para>
/// </summary>
public sealed class PatcherTestHarness : IDisposable
{
    /// <summary>Folder name under the test assembly output that holds the committed demo settings tree.</summary>
    public const string DemoSettingsRelativePath = "TestData/DemoSettings";

    public Autofac.IContainer Container { get; }
    public TestEnvironmentStateProvider EnvironmentProvider { get; }
    public PatcherState PatcherState { get; }
    public SynthEBDPaths Paths { get; }
    public Patcher Patcher { get; }
    /// <summary>The same combination-log instance the patcher populated (overridden to a singleton).</summary>
    public CombinationLog CombinationLog { get; }

    /// <summary>Temp clone of the demo settings tree; also the patcher's settings root (portable mode).</summary>
    public string SettingsRoot { get; }
    /// <summary>Temp folder the patcher writes generated plugins/scripts/inis into.</summary>
    public string OutputDataFolder { get; }

    private readonly string _tempRoot;

    private PatcherTestHarness(TestEnvironmentStateProvider environmentProvider, string tempRoot)
    {
        EnvironmentProvider = environmentProvider;
        _tempRoot = tempRoot;
        SettingsRoot = Path.Combine(tempRoot, "Settings Root");
        OutputDataFolder = Path.Combine(tempRoot, "Output");
        Directory.CreateDirectory(OutputDataFolder);

        var demoSource = Path.Combine(AppContext.BaseDirectory, DemoSettingsRelativePath);
        if (!Directory.Exists(demoSource))
        {
            throw new DirectoryNotFoundException(
                "Demo settings were not copied to the test output. Expected: " + demoSource);
        }
        CopyDirectory(demoSource, SettingsRoot);

        var builder = new ContainerBuilder();
        builder.RegisterType<PatcherSettingsSourceProvider>().AsSelf().SingleInstance();
        builder.RegisterType<PatcherEnvironmentSourceProvider>().AsSelf().SingleInstance();
        builder.RegisterInstance(environmentProvider).AsSelf().AsImplementedInterfaces().SingleInstance();
        builder.RegisterModule<MainModule>();
        // MainModule registers CombinationLog as transient, so the patcher's instance would be
        // unreachable from the test. Override it to a singleton (registered last → wins) so tests can
        // resolve the same instance the patcher populated and inspect per-NPC asset assignments.
        builder.RegisterType<CombinationLog>().AsSelf().SingleInstance();
        Container = builder.Build();

        // Resolve the settings source first and switch it into portable mode pointing at our temp clone,
        // BEFORE anything resolves SynthEBDPaths (whose constructor reads these to compute its root).
        // The source file path is kept at the settings-root level (not under a Settings/ subfolder) so the
        // provider's DefaultSettingsRootPath equals SettingsRoot. SettingsIO_AssetPack.LoadRecordTemplates
        // probes a fallback folder via GetFallBackPath (rooted at DefaultSettingsRootPath); keeping it at
        // SettingsRoot makes that fallback coincide with the real "Record Templates" folder we cloned.
        var settingsSource = Container.Resolve<PatcherSettingsSourceProvider>(
            new Autofac.NamedParameter("sourcePath",
                Path.Combine(SettingsRoot, SynthEBDPaths.SettingsSourceFileName)));
        settingsSource.Initialized = true;
        settingsSource.UsePortableSettings = true;
        settingsSource.PortableSettingsFolder = SettingsRoot;

        Container.Resolve<PatcherEnvironmentSourceProvider>(
            new Autofac.NamedParameter("sourcePath",
                Path.Combine(SettingsRoot, SynthEBDPaths.EnvironmentSourceDirName)));

        Paths = Container.Resolve<SynthEBDPaths>();

        Container.Resolve<SaveLoader>().LoadAllSettings();
        PatcherState = Container.Resolve<PatcherState>();

        CombinationLog = Container.Resolve<CombinationLog>();
        Patcher = Container.Resolve<Patcher>();

        // OutputDataFolder is the temp sink for generated plugins/scripts/inis; it must stay distinct from
        // the real game DataFolderPath (left as resolved by the environment) so the patcher reads real game
        // assets but writes only into the temp folder. Assigned AFTER resolving Patcher because constructing
        // the patcher graph resets OutputDataFolder (mirrors the ordering requirement in App.RunPatch).
        Paths.OutputDataFolder = OutputDataFolder;
        // Safety guard: RunPatcher falls back to GeneralSettings.OutputDataFolder (and then to the real game
        // DataFolderPath) if _paths.OutputDataFolder is ever empty. Pin the settings value to the temp folder
        // too so a stray reset can never cause the patcher to write into the real game Data folder.
        PatcherState.GeneralSettings.OutputDataFolder = OutputDataFolder;
    }

    /// <summary>
    /// Builds a harness, or returns <c>null</c> with a skip reason when no Skyrim SE environment can be
    /// resolved on this machine.
    /// </summary>
    public static PatcherTestHarness? TryCreate(out string skipReason)
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "SynthEBD.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);

        if (!TestEnvironmentStateProvider.TryBuild("SynthEBDTest",
                Path.Combine(tempRoot, "Logs"), out var provider, out skipReason) || provider == null)
        {
            TryDelete(tempRoot);
            return null;
        }

        return new PatcherTestHarness(provider, tempRoot);
    }

    /// <summary>
    /// Marks every loaded BodySlide preset as "currently installed". Required for BodySlide assignments to
    /// survive <c>Patcher.cs</c>'s filter against <c>CurrentlyExistingBodySlides</c>, which would otherwise
    /// be empty because no BodySlide XML exists in the (empty) test data folder.
    /// </summary>
    public void MarkAllBodySlidesAsExisting()
    {
        var o = PatcherState.OBodySettings;
        var existing = o.CurrentlyExistingBodySlides;
        foreach (var bs in o.BodySlidesMale.And(o.BodySlidesFemale))
        {
            existing.Add(bs.ReferencedBodySlide);
        }
    }

    /// <summary>Runs the full patcher pipeline.</summary>
    public Task RunAsync() => Patcher.RunPatcher();

    /// <summary>
    /// Configures an assets-only run over the supplied in-memory scenario packs: enables asset patching,
    /// disables the body-shape/height/head-part axes (for isolation and speed), pins the primary asset
    /// order, selects the scenario packs, and enables the assignment log. Call before <see cref="RunAsync"/>.
    /// </summary>
    /// <param name="packs">Scenario asset packs (see <see cref="AssetScenario"/>).</param>
    /// <param name="faceMode">Face patching mode; Script (default) avoids FaceGen NIF baking, NifEdit writes
    /// the HeadTexture record for record-position assertions.</param>
    public void UseAssetScenario(IReadOnlyList<AssetPack> packs,
        FacePatchingMode faceMode = FacePatchingMode.Script)
    {
        var g = PatcherState.GeneralSettings;
        g.bChangeMeshesOrTextures = true;
        g.BodySelectionMode = BodyShapeSelectionMode.None;
        g.bChangeHeight = false;
        g.bChangeHeadParts = false;

        var t = PatcherState.TexMeshSettings;
        t.FacePatchingMode = faceMode;
        t.AssetOrder = new List<string> { VM_AssetOrderingMenu.PrimaryLabel };
        t.SelectedAssetPacks = packs.Select(p => p.GroupName).ToHashSet();
        t.bGenerateAssignmentLog = true;
        t.bSkyPatcherModeAssets = false;

        PatcherState.AssetPacks = packs.ToList();
    }

    /// <summary>
    /// Returns the winning NPC getters that were assigned a primary combination containing the given leaf
    /// subgroup id, read from the combination log the patcher populated. Subgroup ids are matched exactly
    /// against the '|'-separated signature, so "P.A" does not match "P.A2".
    /// </summary>
    public IReadOnlyList<INpcGetter> NpcsAssignedSubgroup(string subgroupId)
    {
        var result = new List<INpcGetter>();
        foreach (var combos in CombinationLog.AssignedPrimaryCombinations.Values)
        {
            foreach (var combo in combos)
            {
                if (!combo.SubgroupIDs.Split('|').Contains(subgroupId)) { continue; }
                foreach (var logId in combo.NPCsAssignedTo)
                {
                    if (TryResolveNpc(logId, out var npc)) { result.Add(npc); }
                }
            }
        }
        return result;
    }

    /// <summary>Total number of NPCs assigned the given leaf subgroup across all scenario packs.</summary>
    public int AssignmentCount(string subgroupId) => NpcsAssignedSubgroup(subgroupId).Count;

    /// <summary>Resolves the winning NPC getter from a combination-log id ("Name | EditorID | FormKey").</summary>
    public bool TryResolveNpc(string logId, out INpcGetter npc)
    {
        npc = null!;
        var lastSegment = logId.Split('|').LastOrDefault()?.Trim();
        if (string.IsNullOrEmpty(lastSegment) || !FormKey.TryFactory(lastSegment, out var formKey))
        {
            return false;
        }
        return EnvironmentProvider.LinkCache.TryResolve<INpcGetter>(formKey, out npc!);
    }

    /// <summary>The in-memory output plugin the patcher writes records into.</summary>
    public ISkyrimMod OutputMod => EnvironmentProvider.OutputMod;

    /// <summary>Path to the output plugin file written to disk at the end of a standalone run.</summary>
    public string OutputPluginPath =>
        Path.Combine(OutputDataFolder, EnvironmentProvider.OutputMod.ModKey.FileName);

    public void Dispose()
    {
        // The UI view models pulled in by the patcher graph queue deferred dispatcher callbacks that resolve
        // from the container. Drain the queue while the container is still alive so those callbacks don't
        // throw ObjectDisposedException after disposal. Best-effort; the fixture swallows any stragglers.
        try
        {
            if (System.Windows.Threading.Dispatcher.FromThread(Thread.CurrentThread) is { } dispatcher)
            {
                dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ContextIdle);
            }
        }
        catch
        {
            // ignore
        }

        Container.Dispose();
        TryDelete(_tempRoot);
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var dir in Directory.GetDirectories(source, "*", SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(dir.Replace(source, destination));
        }
        foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
        {
            File.Copy(file, file.Replace(source, destination), overwrite: true);
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
            // Best-effort cleanup; leave temp files if something still holds a handle.
        }
    }
}
