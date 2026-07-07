using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Skyrim;

namespace SynthEBD;

/// <summary>
/// Per-viewer SynthEBD-side state for the host-coupled extension methods
/// (<see cref="CharacterViewerSynthEbdExtensions.ApplyBodySlide"/>,
/// <see cref="CharacterViewerSynthEbdExtensions.ApplyBodyGen"/>,
/// <see cref="CharacterViewerSynthEbdExtensions.ApplyHeadPartsAsync"/>).
///
/// Holds the host queue (BodySlide preset + weight, head-part assignments)
/// that mirrors the original VM_CharacterViewer fields _pendingBodySlide /
/// _pendingHeadParts, plus the OSD loader and FaceGen service the wrappers
/// need. Subscribes to <see cref="VM_CharacterViewer.SceneCommitted"/> so
/// queued operations re-fire automatically once the new scene is ready.
///
/// Looked up per-VM via a <see cref="ConditionalWeakTable{TKey,TValue}"/>
/// (see <see cref="SynthEbdViewerHostStateRegistry"/>) — no manual
/// disposal required, the entry is garbage-collected with its viewer.
/// </summary>
internal sealed class SynthEbdViewerHostState
{
    private readonly VM_CharacterViewer _vm;
    private readonly SynthEbdOsdLoader _osdLoader;
    private readonly FaceGenPreviewService _faceGen;
    private readonly Logger _logger;

    private (BodySlideSetting Preset, int Weight)? _pendingBodySlide;

    /// <summary>Cancels the previous <see cref="ApplyHeadPartsAsync"/> when a
    /// new one starts. Necessary because head-part assignment editors
    /// (VM_SpecificNPCAssignment / VM_ConsistencyAssignment) subscribe to each
    /// head-part type's FormKey individually with per-subscription throttle —
    /// when an NPC with a saved override loads, every type fires
    /// RefreshViewerNpcAsync after the throttle window expires and they all
    /// race on FaceGenPatcher's per-NPC temp extraction path
    /// (<c>S:\Temp\&lt;plugin&gt;_&lt;formId&gt;_facegen.nif</c>). Cancelling
    /// the prior call collapses the pile-up to a single extraction.</summary>
    private CancellationTokenSource? _headPartsCts;

    internal SynthEbdViewerHostState(VM_CharacterViewer vm, SynthEbdOsdLoader osdLoader,
        FaceGenPreviewService faceGen, Logger logger)
    {
        _vm = vm;
        _osdLoader = osdLoader;
        _faceGen = faceGen;
        _logger = logger;
        _vm.SceneCommitted += OnSceneCommitted;
    }

    private void OnSceneCommitted()
    {
        if (_pendingBodySlide is { } queued)
        {
            _pendingBodySlide = null;
            ApplyBodySlide(queued.Preset, queued.Weight);
        }
    }

    // ───────────────────────────────────────────────────────────────────
    //  ApplyBodySlide
    // ───────────────────────────────────────────────────────────────────

    /// <summary>SynthEBD wrapper around <see cref="VM_CharacterViewer.ApplyMorphSet"/>.
    /// Loads the OSD context for the preset's SliderGroup (a SynthEBD concern that
    /// walks PatcherState.OBodySettings.BodyTypeRegistry — see <see cref="SynthEbdOsdLoader"/>),
    /// translates the preset to a neutral <see cref="MorphSet"/>, and applies it.
    /// Queues if the scene isn't yet ready; the queue drains automatically when
    /// <see cref="VM_CharacterViewer.SceneCommitted"/> fires.</summary>
    internal void ApplyBodySlide(BodySlideSetting preset, int weight)
    {
        // Mirrors the original VM check: queue when meshes haven't yet committed — EXCEPT in
        // the software fallback (RenderingUnavailable), where the GL scene never commits and
        // SceneCommitted never fires, so queuing here would strand the morph forever. There we
        // fall through and apply directly: SetMorphContext + ApplyMorphSet below retain the
        // morph on the VM (see VM_CharacterViewer._lastRequestedMorphSet) so the offscreen
        // fallback preview reproduces the deformation via the body's sibling .tri.
        if (!_vm.IsSceneReady && !_vm.RenderingUnavailable)
        {
            _pendingBodySlide = (preset, weight);
            return;
        }

        try
        {
            // Pre-load OSD context if no sibling .tri exists. The viewer's
            // ApplyMorphSet auto-loads the .tri inside its own try block; we
            // can't tell ahead of time whether it'll succeed, so we always
            // load OSDs and let the deformer prefer .tri when available.
            if (preset?.SliderGroup != null)
            {
                var osd = _osdLoader.LoadForSliderGroup(preset.SliderGroup);
                _vm.SetMorphContext(osd);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError("CharacterViewer: SynthEbd OSD pre-load failed for preset '"
                + (preset?.Label ?? "?") + "': " + ExceptionLogger.GetExceptionStack(ex));
        }

        _vm.ApplyMorphSet(ToMorphSet(preset), weight);
    }

    // ───────────────────────────────────────────────────────────────────
    //  ApplyBodyGen
    // ───────────────────────────────────────────────────────────────────

    /// <summary>Applies a stack of BodyGen templates by parsing and summing
    /// their Specs into a virtual <see cref="BodySlideSetting"/>, then routing
    /// through <see cref="ApplyBodySlide"/>. Matches BodyGen runtime behavior
    /// where templates stack additively on the same NPC.</summary>
    internal void ApplyBodyGen(IEnumerable<BodyGenConfig.BodyGenTemplate> templates,
        string sliderGroup, int weight)
    {
        var list = templates?.Where(t => t != null).ToList() ?? new List<BodyGenConfig.BodyGenTemplate>();
        if (list.Count == 0) return;

        var merged = BodyGenSpecsParser.ParseAndMerge(
            list.Select(t => t.Specs ?? string.Empty),
            sliderGroup,
            out _);

        if (merged.SliderValues.Count == 0) return;
        ApplyBodySlide(merged, weight);
    }

    // ───────────────────────────────────────────────────────────────────
    //  ApplyHeadPartsAsync
    // ───────────────────────────────────────────────────────────────────

    /// <summary>Reloads <paramref name="npcFormKey"/> with
    /// <paramref name="assignments"/> applied as head-part overrides. Generates
    /// a preview FaceGen NIF via <see cref="FaceGenPreviewService"/> and hands
    /// its path to the viewer's neutral entry as the head-mesh override.
    /// When the same NPC is already loaded and the scene is ready, takes the
    /// fast path through <see cref="VM_CharacterViewer.RebuildHeadOnlyAsync"/>
    /// which only re-parses the head NIF (vs. all four body parts).</summary>
    internal async Task ApplyHeadPartsAsync(FormKey npcFormKey, ILinkCache linkCache,
        IReadOnlyDictionary<HeadPart.TypeEnum, FormKey> assignments,
        CancellationToken externalCt = default)
    {
        if (npcFormKey.IsNull || linkCache == null) return;

        // Cancel any prior in-flight call on this viewer, then chain to the
        // external token so external cancellations still propagate.
        _headPartsCts?.Cancel();
        _headPartsCts?.Dispose();
        var cts = CancellationTokenSource.CreateLinkedTokenSource(externalCt);
        _headPartsCts = cts;
        var ct = cts.Token;

        var validAssignments = assignments?
            .Where(kv => !kv.Value.IsNull)
            .ToDictionary(kv => kv.Key, kv => kv.Value) ?? new();

        if (validAssignments.Count == 0)
        {
            await _vm.LoadByIdentityAsync(new NpcIdentity(npcFormKey.ToString(), npcFormKey.ToString()));
            return;
        }

        string? nifPath;
        try
        {
            nifPath = await _faceGen.GeneratePreviewFaceGenAsync(npcFormKey, validAssignments, ct);
        }
        catch (OperationCanceledException)
        {
            // Quietly drop cancelled calls; the newer call that cancelled us
            // will finish the work the user actually wants reflected.
            return;
        }
        catch (Exception ex)
        {
            _logger.LogError("CharacterViewer.ApplyHeadPartsAsync: preview FaceGen generation failed: " + ex.Message);
            nifPath = null;
        }

        ct.ThrowIfCancellationRequested();

        // Fast path: if the NPC is already loaded and the scene is committed,
        // rebuild only the Head shape(s). Body/Hands/Feet keep their current
        // textures and any in-progress BodySlide deformation — a full reload
        // would re-parse all four NIFs and re-decode their DDS textures just
        // to swap the head. Fall back to the full-reload branch when any of
        // the preconditions fail (different NPC, scene not committed,
        // GL not ready, or no FaceGen NIF was produced).
        if (nifPath != null
            && _vm.CanRebuildHeadOnly
            && npcFormKey.ToString() == _vm.CurrentLoadedIdentityKey)
        {
            await _vm.RebuildHeadOnlyAsync(nifPath, ct);
            return;
        }

        var identity = new NpcIdentity(npcFormKey.ToString(), npcFormKey.ToString());
        await _vm.LoadByIdentityAsync(identity, overrideHeadMeshAbsolutePath: nifPath);
    }

    // ───────────────────────────────────────────────────────────────────
    //  Helpers
    // ───────────────────────────────────────────────────────────────────

    /// <summary>Translates a SynthEBD <see cref="BodySlideSetting"/> into the
    /// neutral <see cref="MorphSet"/> the viewer's deformer consumes.</summary>
    private static MorphSet ToMorphSet(BodySlideSetting? preset)
    {
        var sliders = new Dictionary<string, MorphSlider>(StringComparer.OrdinalIgnoreCase);
        if (preset?.SliderValues != null)
        {
            foreach (var kvp in preset.SliderValues)
            {
                var s = kvp.Value;
                if (s == null) continue;
                sliders[kvp.Key] = new MorphSlider(s.Big, s.Small);
            }
        }
        return new MorphSet { Label = preset?.Label ?? "", Sliders = sliders };
    }
}

/// <summary>
/// Static lookup for per-viewer <see cref="SynthEbdViewerHostState"/> instances.
/// Backed by a <see cref="ConditionalWeakTable{TKey,TValue}"/> so each entry is
/// garbage-collected with its viewer; no manual disposal needed.
///
/// Configured once at app startup via <see cref="Configure"/> from MainModule's
/// container build callback. Resolution is deferred to first
/// <see cref="GetOrCreate"/> call — Configure stores resolver delegates rather
/// than constructed instances, because in standalone mode some transitive
/// dependencies (PatcherEnvironmentSourceProvider's sourcePath) aren't bound
/// until after the container is built and the standalone bootstrap completes.
/// By the time a viewer is constructed (at first BodySlide-menu open), the
/// environment is fully wired and <see cref="SynthEbdOsdLoader"/> can resolve.
/// </summary>
public static class SynthEbdViewerHostStateRegistry
{
    private static readonly ConditionalWeakTable<VM_CharacterViewer, SynthEbdViewerHostState> _state = new();

    private static Func<SynthEbdOsdLoader>? _osdLoaderFactory;
    private static Func<FaceGenPreviewService>? _faceGenFactory;
    private static Func<Logger>? _loggerFactory;

    /// <summary>Wires lazy resolvers for the SynthEBD-side services the host
    /// extension methods need. Each factory is invoked on first viewer
    /// construction — never during container build — so transitive deps that
    /// require post-build runtime state (e.g. the standalone environment
    /// provider) can resolve safely.</summary>
    public static void Configure(Func<SynthEbdOsdLoader> osdLoaderFactory,
        Func<FaceGenPreviewService> faceGenFactory, Func<Logger> loggerFactory)
    {
        _osdLoaderFactory = osdLoaderFactory;
        _faceGenFactory = faceGenFactory;
        _loggerFactory = loggerFactory;
    }

    internal static SynthEbdViewerHostState GetOrCreate(VM_CharacterViewer vm)
    {
        if (_osdLoaderFactory == null || _faceGenFactory == null || _loggerFactory == null)
        {
            throw new InvalidOperationException(
                "SynthEbdViewerHostStateRegistry.Configure() must be called before viewer host extensions are used.");
        }
        return _state.GetValue(vm, k =>
            new SynthEbdViewerHostState(k, _osdLoaderFactory(), _faceGenFactory(), _loggerFactory()));
    }
}
