using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Skyrim;

namespace SynthEBD;

/// <summary>
/// SynthEBD-facing extension methods on <see cref="VM_CharacterViewer"/>. These
/// expose Mutagen-typed and SynthEBD-typed entry points (LoadNpcAsync taking a
/// FormKey, ApplyTextureOverrides taking FilePathReplacement) by translating to
/// the viewer's neutral entry points (LoadByIdentityAsync, ApplyTextureOverrides
/// taking TextureOverride). The viewer itself stays Mutagen-free so it can
/// move into the CharacterViewer.Rendering project (Phase B2d) and be reused
/// by NPC Plugin Chooser 2.
///
/// Existing SynthEBD callers (VM_AssetPresenter, VM_SpecificNPCAssignment,
/// VM_ConsistencyAssignment, VM_BodySlideSetting, VM_BodyTypeProfileEditor,
/// etc.) continue to write <c>characterViewer.LoadNpcAsync(formKey, linkCache)</c>
/// and <c>characterViewer.ApplyTextureOverrides(overrides)</c> unchanged —
/// extension method dispatch makes the call sites identical to instance methods.
/// </summary>
public static class CharacterViewerSynthEbdExtensions
{
    /// <summary>
    /// Loads <paramref name="npcFormKey"/> by building an <see cref="NpcIdentity"/>
    /// from the FormKey and delegating to <see cref="VM_CharacterViewer.LoadByIdentityAsync"/>.
    /// The <paramref name="linkCache"/> parameter is preserved for backward compat
    /// with the pre-modularization signature but is unused here — the viewer's
    /// preview-cache adapter holds its own LinkCache reference (see
    /// <see cref="SynthEbdNpcMeshDataSourceAdapter"/>).
    /// </summary>
    public static Task LoadNpcAsync(this VM_CharacterViewer viewer,
        FormKey npcFormKey, ILinkCache linkCache,
        string? overrideHeadMeshAbsolutePath = null)
    {
        var identity = new NpcIdentity(npcFormKey.ToString(), npcFormKey.ToString());
        return viewer.LoadByIdentityAsync(identity, overrideHeadMeshAbsolutePath);
    }

    /// <summary>
    /// Translates SynthEBD's <see cref="FilePathReplacement"/> overrides into the
    /// viewer's neutral <see cref="TextureOverride"/> shape and applies them.
    /// Each FilePathReplacement encodes its target body part + slot in its
    /// <see cref="FilePathReplacement.Destination"/> path; the parsing is done
    /// here via the viewer's static <see cref="VM_CharacterViewer.ParseBodyPart"/>
    /// and <see cref="VM_CharacterViewer.ParseTextureSlot"/> helpers.
    /// </summary>
    public static void ApplyTextureOverrides(this VM_CharacterViewer viewer,
        IEnumerable<FilePathReplacement> overrides)
    {
        if (overrides == null) return;

        var converted = new List<TextureOverride>();
        foreach (var r in overrides)
        {
            if (r == null) continue;
            string dest = r.Destination;
            if (string.IsNullOrWhiteSpace(dest) || string.IsNullOrWhiteSpace(r.Source)) continue;

            string? bodyPart = VM_CharacterViewer.ParseBodyPart(dest);
            int? slot = VM_CharacterViewer.ParseTextureSlot(dest);
            if (bodyPart == null || slot == null) continue;

            converted.Add(new TextureOverride(bodyPart, slot.Value, r.Source));
        }

        viewer.ApplyTextureOverrides(converted);
    }

    /// <summary>SynthEBD-facing BodySlide application. Loads the OSD context for
    /// the preset's SliderGroup via <see cref="SynthEbdOsdLoader"/>, translates
    /// the preset to a neutral <see cref="MorphSet"/>, and calls
    /// <see cref="VM_CharacterViewer.ApplyMorphSet"/>. Per-viewer queue state
    /// (for the not-yet-ready scene case) lives in
    /// <see cref="SynthEbdViewerHostState"/>, which subscribes to
    /// <see cref="VM_CharacterViewer.SceneCommitted"/> and replays the queued
    /// preset when the new scene is ready.</summary>
    public static void ApplyBodySlide(this VM_CharacterViewer viewer,
        BodySlideSetting preset, int weight)
    {
        SynthEbdViewerHostStateRegistry.GetOrCreate(viewer).ApplyBodySlide(preset, weight);
    }

    /// <summary>
    /// SynthEBD-facing guest overlay ("superimpose"): draws <paramref name="npcFormKey"/>
    /// wearing <paramref name="preset"/> at <paramref name="weight"/> on top of whatever
    /// <paramref name="viewer"/> is already showing. Used by the BodySlide Compare window so
    /// pane B's model can be overlaid on pane A's.
    ///
    /// <para><paramref name="linkCache"/> is accepted for symmetry with
    /// <see cref="LoadNpcAsync"/> and is likewise unused — the viewer's preview-cache adapter
    /// holds its own reference. Pass a null <paramref name="preset"/> to overlay the NPC's
    /// undeformed body.</para>
    /// </summary>
    public static Task LoadGuestNpcAsync(this VM_CharacterViewer viewer,
        FormKey npcFormKey, ILinkCache linkCache, BodySlideSetting? preset, int weight)
    {
        return SynthEbdViewerHostStateRegistry.GetOrCreate(viewer)
            .LoadGuestNpcAsync(npcFormKey, preset, weight);
    }

    /// <summary>SynthEBD-facing BodyGen application. Stacks
    /// <paramref name="templates"/>' Specs additively, builds a virtual
    /// <see cref="BodySlideSetting"/>, and routes through
    /// <see cref="ApplyBodySlide"/>.</summary>
    public static void ApplyBodyGen(this VM_CharacterViewer viewer,
        IEnumerable<BodyGenConfig.BodyGenTemplate> templates, string sliderGroup, int weight)
    {
        SynthEbdViewerHostStateRegistry.GetOrCreate(viewer).ApplyBodyGen(templates, sliderGroup, weight);
    }

    /// <summary>SynthEBD-facing head-part swap. Generates a preview FaceGen NIF
    /// via <see cref="FaceGenPreviewService"/> and reloads
    /// <paramref name="npcFormKey"/> with that NIF as the head-mesh override.
    /// Takes the head-only fast path (avoiding a full body re-parse) when the
    /// same NPC is already loaded and the scene is committed.</summary>
    public static Task ApplyHeadPartsAsync(this VM_CharacterViewer viewer,
        FormKey npcFormKey, ILinkCache linkCache,
        IReadOnlyDictionary<HeadPart.TypeEnum, FormKey> assignments,
        CancellationToken ct = default)
    {
        return SynthEbdViewerHostStateRegistry.GetOrCreate(viewer)
            .ApplyHeadPartsAsync(npcFormKey, linkCache, assignments, ct);
    }
}
