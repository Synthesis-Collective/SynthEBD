using System;

namespace SkyRender;

/// <summary>
/// Library-level metadata for SkyRender. Hosts read <see cref="Version"/> at startup to detect a
/// stale bundled DLL and warn when it predates an API addition the host depends on.
///
/// <para>Versioning policy (semver):</para>
/// <list type="bullet">
///   <item><b>Major</b> — breaking change to public API surface
///     (renamed/removed types, changed signatures, behaviour changes that
///     would silently corrupt callers).</item>
///   <item><b>Minor</b> — additive change (new types, new optional
///     parameters via overloads, new enum values). Existing callers keep working.</item>
///   <item><b>Patch</b> — bug fix or doc-only change with no API impact.</item>
/// </list>
///
/// <para>The version comes from the assembly itself (set once, in the repository's
/// <c>Directory.Build.props</c>), so it cannot drift from the package version. SkyRender and
/// SkyRender.Character always ship the same version.</para>
///
/// <para>Suggested host usage:</para>
/// <code>
/// var required = new Version(3, 0, 0);
/// if (SkyRenderInfo.Version &lt; required)
///     logger.LogWarning($"Bundled SkyRender is v{SkyRenderInfo.Version}; expected v{required} or newer.");
/// </code>
/// </summary>
public static class SkyRenderInfo
{
    /// <summary>Current library version (major.minor.patch), read from the assembly version.</summary>
    public static readonly Version Version = ReadVersion();

    private static Version ReadVersion()
    {
        var v = typeof(SkyRenderInfo).Assembly.GetName().Version ?? new Version(0, 0, 0);
        return new Version(v.Major, v.Minor, Math.Max(v.Build, 0));
    }
}
