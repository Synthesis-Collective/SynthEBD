using System.Collections.Generic;

namespace CharacterViewer.Rendering;

/// <summary>
/// One entry in the priority-ordered asset-resolution chain set on
/// <see cref="Offscreen.OffscreenRenderRequest.AdditionalScopes"/> /
/// <see cref="VM_CharacterViewer.AdditionalScopes"/>. A scope binds a
/// data folder to a set of plugin filenames (e.g. <c>"MyMod.esp"</c>);
/// the resolver consults the folder for loose files and any BSAs
/// physically located in that folder owned by one of the listed plugins.
///
/// <para><b>Iteration order</b> (per the contract on <c>AdditionalScopes</c>):
/// the resolver walks the list <i>last-to-first</i> in two phases — all
/// loose-file checks first, then all scoped-BSA checks. The last entry
/// in the list therefore wins for both phases. Hosts typically build
/// the list as <c>[vanilla, mod-folder-1, mod-folder-2, …, mod-folder-N]</c>
/// where vanilla sits at index 0 (lowest priority, checked last).</para>
///
/// <para><see cref="ModKeyFileNames"/> are deliberately strings rather
/// than a Mutagen <c>ModKey</c> type so the rendering library doesn't
/// take a Mutagen dependency. The host parses them back as needed
/// (e.g. NPC2's <c>NpcChooserBsaProviderAdapter</c> uses
/// <c>ModKey.TryFromNameAndExtension</c>).</para>
/// </summary>
public sealed record RenderScope(
    string FolderPath,
    IReadOnlyList<string> ModKeyFileNames);
