using System;

namespace CharacterViewer.Rendering;

/// <summary>
/// Library-level metadata for CharacterViewer.Rendering. Hosts (e.g. NPC
/// Plugin Chooser 2, SynthEBD) read <see cref="Version"/> at startup to
/// detect stale bundled DLLs and warn the user when their bundled copy
/// predates an API addition the host depends on.
///
/// <para>Versioning policy (semver-ish):</para>
/// <list type="bullet">
///   <item><b>Major</b> — breaking change to public API surface
///     (renamed/removed types, changed signatures, behaviour changes that
///     would silently corrupt callers).</item>
///   <item><b>Minor</b> — additive change (new types, new optional
///     parameters via overloads, new enum values, new <see cref="Offscreen.CameraFraming"/>
///     cases). Existing callers keep working.</item>
///   <item><b>Patch</b> — bug fix or doc-only change with no API impact.</item>
/// </list>
///
/// <para>Keep this constant in sync with the <c>&lt;Version&gt;</c>,
/// <c>&lt;AssemblyVersion&gt;</c>, and <c>&lt;FileVersion&gt;</c> fields
/// in <c>CharacterViewer.Rendering.csproj</c>. Bump them together.</para>
///
/// <para>Suggested host usage:</para>
/// <code>
/// var required = new Version(1, 1, 0);
/// if (CharacterViewerRendering.Version &lt; required)
/// {
///     logger.LogWarning(
///         $"Bundled CharacterViewer.Rendering is v{CharacterViewerRendering.Version}; " +
///         $"this build of NPC2 expects v{required} or newer. Update your bundled DLL.");
/// }
/// </code>
/// </summary>
public static class CharacterViewerRendering
{
    /// <summary>Current library version. See class summary for the bump policy.</summary>
    public static readonly Version Version = new(2, 6, 1);
}
