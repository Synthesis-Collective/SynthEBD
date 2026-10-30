using System.ComponentModel;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using SkyRender;
using SkyRender.Character;
using FluentAssertions;
using ReactiveUI;
using ReactiveUI.Builder;
using Xunit;

namespace SkyRender.Tests;

/// <summary>
/// <see cref="VM_CharacterViewer"/> switching NPCs: the previous NPC's scene can finish installing
/// while the next NPC's models are still being read. That commit is stale. It must not drain the
/// narrow updates queued for the newer load (the outfit, as mesh overrides) into the scene that is
/// about to be replaced, nor drop the rebuild gate the newer load holds.
///
/// <para>No GL context: the scenes have no shapes (empty paths), so the install never uploads, and
/// the state a real install leaves behind (a texture manager and a mesh per body part) is stood in
/// at <see cref="VM_CharacterViewer.SceneCleared"/>, which fires inside the install before the
/// commit. The marshaller is not the inline one, so the viewer defers mesh overrides to the render
/// tick as it does for an interactive host, and it can hold a load just before its scene is queued,
/// which is where the race window is.</para>
/// </summary>
public class SceneLoadRaceTests
{
    private static readonly NpcIdentity NpcA = new("A", "A");
    private static readonly NpcIdentity NpcB = new("B", "B");

    private static readonly List<MeshOverride> OutfitB = new()
    {
        new MeshOverride { Key = "OutfitB", MeshPath = @"meshes\test\outfitb_1.nif", BipedSlots = 4 },
    };

    [Fact]
    public async Task StaleSceneCommit_LeavesTheNewerLoadsOutfitQueued_AndItLandsOnTheNewerScene()
    {
        var marshaller = new HoldingMarshaller();
        using var viewer = NewViewer(marshaller, out var resolver);
        StandInForUploadedShapes(viewer, resolver);
        var appliedTo = new List<string>();
        viewer.MeshOverridesApplied += () => appliedTo.Add(viewer.CurrentLoadedIdentityKey);

        // NPC A: models read, scene queued for the render tick.
        await viewer.LoadAsync(NpcA, new ResolvedNpcMeshPaths());

        // NPC B picked: its outfit queued, then its load starts and is held while it is still loading.
        viewer.ApplyMeshOverrides(OutfitB);
        marshaller.Hold = true;
        var loadB = viewer.LoadAsync(NpcB, new ResolvedNpcMeshPaths());
        marshaller.Entered.Wait(TimeSpan.FromSeconds(30)).Should().BeTrue("B's load should reach its queue step");

        // The render tick installs A's scene while B is still loading.
        viewer.ProcessPendingScene();
        viewer.CurrentLoadedIdentityKey.Should().Be("A");
        appliedTo.Should().BeEmpty("B's outfit must not go into A's scene");
        viewer.IsLoading.Should().BeTrue("B is still loading");

        // B's scene arrives and installs: the outfit goes in with it.
        marshaller.Release.Set();
        await loadB;
        viewer.ProcessPendingScene();
        viewer.CurrentLoadedIdentityKey.Should().Be("B");
        appliedTo.Should().Equal("B");
        viewer.MeshOverrideWarnings.Should().ContainSingle(w => w.Contains("OutfitB"));
        viewer.IsLoading.Should().BeFalse();
    }

    [Fact]
    public async Task NewerLoadCancelledWithoutAReplacement_TheOlderSceneCommitsAsCurrent()
    {
        var marshaller = new HoldingMarshaller();
        using var viewer = NewViewer(marshaller, out var resolver);
        StandInForUploadedShapes(viewer, resolver);
        var committed = 0;
        viewer.SceneCommitted += () => committed++;

        await viewer.LoadAsync(NpcA, new ResolvedNpcMeshPaths());

        // B is picked and then abandoned, with nothing picked after it.
        using (var cancelled = new CancellationTokenSource())
        {
            cancelled.Cancel();
            await viewer.LoadAsync(NpcB, new ResolvedNpcMeshPaths(), externalCt: cancelled.Token);
        }

        viewer.ProcessPendingScene();
        committed.Should().Be(1);
        viewer.CurrentLoadedIdentityKey.Should().Be("A");
        viewer.IsLoading.Should().BeFalse("no load is left in flight");
    }

    // ---- harness ----

    /// <summary>ReactiveUI must be initialised once per process before the first view model.</summary>
    private static readonly Lazy<bool> ReactiveUiReady = new(() =>
    {
        RxAppBuilder.CreateReactiveUIBuilder().WithWpf().BuildApp();
        return true;
    });

    /// <summary>A viewer over an empty Data folder, with GL marked initialised (no context: nothing
    /// in these scenes reaches a GL call).</summary>
    private static VM_CharacterViewer NewViewer(IRenderThreadMarshaller marshaller, out GameAssetResolver resolver)
    {
        _ = ReactiveUiReady.Value;
        var data = Path.Combine(Path.GetTempPath(), "SkyRender_SceneRace_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(data);
        var log = new NullLogger();
        var gate = new RenderLogGate();
        var settings = new Settings();
        resolver = new GameAssetResolver(new Folder(data), new NoArchives(), gate, log);
        var cache = new CharacterPreviewCache(new NoNpcs(), resolver, log, gate, settings);
        var viewer = new VM_CharacterViewer(resolver, settings, cache, gate, log, marshaller);
        typeof(VM_CharacterViewer).GetProperty(nameof(VM_CharacterViewer.IsGlInitialized))!.SetValue(viewer, true);
        return viewer;
    }

    /// <summary>What an install leaves behind for overrides to attach to: an (unused, never
    /// initialised) texture manager and a mesh for the body. Set at ClearScene, inside each install.</summary>
    private static void StandInForUploadedShapes(VM_CharacterViewer viewer, GameAssetResolver resolver)
    {
        var log = new NullLogger();
        var textures = new GlTextureManager(new AssetCache(resolver, log, new RenderLogGate()), log);
        var meshes = (Dictionary<string, GlMesh>)typeof(VM_CharacterViewer)
            .GetField("_meshesByBodyPart", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(viewer)!;
        var textureManager = typeof(VM_CharacterViewer).GetProperty(nameof(VM_CharacterViewer.TextureManager))!;
        viewer.SceneCleared += () =>
        {
            textureManager.SetValue(viewer, textures);
            meshes["Body"] = (GlMesh)RuntimeHelpers.GetUninitializedObject(typeof(GlMesh));
        };
    }

    /// <summary>Runs queued work inline (as the render thread would), but while <see cref="Hold"/> is
    /// set it parks the next caller first: a load parked here has read its models and not yet queued them.</summary>
    private sealed class HoldingMarshaller : IRenderThreadMarshaller
    {
        public volatile bool Hold;
        public readonly ManualResetEventSlim Entered = new(false);
        public readonly ManualResetEventSlim Release = new(false);

        public void Invoke(Action action)
        {
            if (Hold)
            {
                Hold = false;   // one call only (the viewer's Dispose also comes through here)
                Entered.Set();
                Release.Wait(TimeSpan.FromSeconds(30));
            }
            action();
        }
    }

    private sealed class NullLogger : IRenderLogger
    {
        public void LogMessage(string message) { }
        public void LogError(string message) { }
        public void LogError(string message, Exception ex) { }
    }

    private sealed class Folder(string path) : IDataFolderProvider
    {
        public string DataFolderPath => path;
        public object? CurrentLoadOrderToken => path;
    }

    private sealed class NoArchives : IBsaArchiveProvider
    {
        public void EnsureAllArchivesOpened() { }
        public bool TryLocateInBsa(string subpath, out string? containingBsaPath) { containingBsaPath = null; return false; }
        public bool TryLocateInScopedBsa(string subpath, string folderPath, IReadOnlyList<string> modKeyFileNames, out string? containingBsaPath)
        { containingBsaPath = null; return false; }
        public bool TryExtractToDisk(string containingBsaPath, string subpath, string destPath, out string? error) { error = "none"; return false; }
    }

    private sealed class NoNpcs : INpcMeshDataSource
    {
        public ResolvedNpcMeshPaths? Resolve(NpcIdentity identity) => null;
        public object? CurrentInvalidationToken => null;
    }

    private sealed class Settings : ICharacterViewerSettings
    {
        public event PropertyChangedEventHandler? PropertyChanged { add { } remove { } }
        public string CharacterViewerLightingLayout { get; set; } = "";
        public string CharacterViewerLightingColorScheme { get; set; } = "";
        public IList<LightingLayout> UserLightingLayouts { get; } = new List<LightingLayout>();
        public IList<LightingColorScheme> UserLightingColorSchemes { get; } = new List<LightingColorScheme>();
        public bool CharacterViewerVerboseLog { get; set; }
    }
}
