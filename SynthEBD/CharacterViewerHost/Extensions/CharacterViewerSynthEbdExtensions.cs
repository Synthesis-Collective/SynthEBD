using System.Collections.Generic;
using System.Threading.Tasks;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;

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
}
