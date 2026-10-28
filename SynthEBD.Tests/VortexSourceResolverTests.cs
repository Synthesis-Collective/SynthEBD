using System.IO;
using FluentAssertions;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Plugins.Order;
using Mutagen.Bethesda.Skyrim;
using Noggog;
using Xunit;

namespace SynthEBD.Tests;

/// <summary>
/// <see cref="VortexSourceResolver.FindProvidingModFolder"/> against a synthetic Vortex layout: a Data
/// folder holding <c>vortex.deployment.json</c> (Vortex's IDeploymentManifest shape -- files[] of
/// { relPath, source, time }, plus stagingPath) and a staging folder with one folder per mod.
/// </summary>
public class VortexSourceResolverTests : IDisposable
{
    private const string RelNif = @"CalienteTools\BodySlide\ShapeData\BHUNP 3BBB Advanced\BHUNP 3BBB Advanced Ver 4.nif";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "SynthEBD_VortexTest_" + Guid.NewGuid().ToString("N"));
    private readonly string _data;
    private readonly string _staging;
    private readonly StubEnvironmentProvider _env = new();
    private readonly PatcherState _state = new();

    public VortexSourceResolverTests()
    {
        _data = Path.Combine(_root, "Data");
        _staging = Path.Combine(_root, "staging");
        Directory.CreateDirectory(_data);
        Directory.CreateDirectory(_staging);
        _env.DataFolderPath = new DirectoryPath(_data);
        _state.ModManagerSettings = new Settings_ModManager();
        _state.ModManagerSettings.VortexSettings.StagingFolderPath = _staging;
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    private VortexSourceResolver CreateResolver() => new(new Logger(null!, _env, null!), _state, _env);

    private string AddStagedFile(string modFolder, string relPath)
    {
        var full = Path.Combine(_staging, modFolder, relPath);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, "x");
        return Path.Combine(_staging, modFolder);
    }

    private void WriteManifest(string json) => File.WriteAllText(Path.Combine(_data, "vortex.deployment.json"), json);

    private static string Escape(string s) => s.Replace(@"\", @"\\");

    [Fact]
    public void Manifest_NamesTheDeployingMod_EvenWhenAnotherStagedModHasTheFile()
    {
        AddStagedFile("BHUNP Old-111", RelNif);                 // staged but lost the conflict / disabled
        var winner = AddStagedFile("BHUNP Main-222", RelNif);
        WriteManifest($$"""
            { "version": 1, "instance": "abc", "stagingPath": "{{Escape(_staging)}}",
              "files": [ { "relPath": "{{Escape(RelNif)}}", "source": "BHUNP Main-222", "time": 1 } ] }
            """);

        CreateResolver().FindProvidingModFolder(RelNif).Should().Be(winner);
    }

    [Fact]
    public void Manifest_MatchesForwardSlashesAndCase()
    {
        var mod = AddStagedFile("BHUNP Main-222", RelNif);
        WriteManifest($$"""
            { "version": 1, "files": [ { "relPath": "{{Escape(RelNif.ToLowerInvariant())}}", "source": "BHUNP Main-222", "time": 1 } ] }
            """);

        // No stagingPath in the manifest: falls back to the configured staging folder.
        CreateResolver().FindProvidingModFolder(RelNif.Replace('\\', '/')).Should().Be(mod);
    }

    [Fact]
    public void Manifest_WithoutTheFile_ReturnsNull_RatherThanGuessing()
    {
        AddStagedFile("BHUNP Main-222", RelNif);
        WriteManifest("""{ "version": 1, "files": [] }""");

        CreateResolver().FindProvidingModFolder(RelNif).Should().BeNull();
    }

    [Fact]
    public void NoManifest_SearchesTheStagingFolder()
    {
        var mod = AddStagedFile("BHUNP Main-222", RelNif);

        CreateResolver().FindProvidingModFolder(RelNif).Should().Be(mod);
    }

    [Fact]
    public void UnreadableManifest_FallsBackToTheStagingBackupCopy()
    {
        var mod = AddStagedFile("BHUNP Main-222", RelNif);
        WriteManifest("""{"damaged""");
        File.WriteAllText(Path.Combine(_staging, "vortex.deployment.json"), $$"""
            { "version": 1, "files": [ { "relPath": "{{Escape(RelNif)}}", "source": "BHUNP Main-222", "time": 1 } ] }
            """);

        CreateResolver().FindProvidingModFolder(RelNif).Should().Be(mod);
    }

    [Fact]
    public void InactiveModFolders_AreStagedModsWithNothingDeployed()
    {
        AddStagedFile("CBBE 3BA-1", @"meshes\actors\character\character assets\femalebody_1.nif");
        var bhunp = AddStagedFile("BHUNP Main-222", RelNif);
        WriteManifest($$"""
            { "version": 1, "stagingPath": "{{Escape(_staging)}}",
              "files": [ { "relPath": "meshes\\actors\\character\\character assets\\femalebody_1.nif", "source": "CBBE 3BA-1", "time": 1 } ] }
            """);

        CreateResolver().GetInactiveModFolders().Should().Equal(bhunp);
    }

    [Fact]
    public void InactiveModFolders_WithoutManifest_AreAllStagedMods()
    {
        var a = AddStagedFile("A-1", "x.txt");
        var b = AddStagedFile("B-2", "y.txt");

        CreateResolver().GetInactiveModFolders().Should().Equal(a, b);
    }

    [Fact]
    public void AllModFolders_ListDeployedModsFirst()
    {
        var deployed = AddStagedFile("Z Deployed-1", @"meshes\x.nif");
        var idle = AddStagedFile("A Idle-2", "y.txt");
        WriteManifest($$"""
            { "version": 1, "stagingPath": "{{Escape(_staging)}}",
              "files": [ { "relPath": "meshes\\x.nif", "source": "Z Deployed-1", "time": 1 } ] }
            """);

        CreateResolver().GetAllModFolders().Should().Equal(deployed, idle);
    }

    private class StubEnvironmentProvider : IEnvironmentStateProvider
    {
        public ILoadOrderGetter<IModListingGetter<ISkyrimModGetter>> LoadOrder => null!;
        public ILinkCache<ISkyrimMod, ISkyrimModGetter> LinkCache => null!;
        public DirectoryPath ExtraSettingsDataPath => default;
        public DirectoryPath InternalDataPath => default;
        public DirectoryPath DataFolderPath { get; set; } = default;
        public EnvironmentMode RunMode => EnvironmentMode.Standalone;
        public LogMode LoggerMode => LogMode.Synthesis;
        public string LogFolderPath => string.Empty;
        public string OutputModName { get; set; } = "SynthEBD";
        public SkyrimRelease SkyrimVersion => SkyrimRelease.SkyrimSE;
        public string CreationClubListingsFilePath => string.Empty;
        public string LoadOrderFilePath => string.Empty;
        public List<string> StartUpLog { get; set; } = new();
    }
}
