namespace SkyRender;

/// <summary>
/// Shared runtime flag that gates SkyRender's informational log output across
/// all helpers (mesh builder, texture/asset resolvers, parsers, and the character tier's
/// view model). Registered as a
/// singleton; the host sets <see cref="Verbose"/> (e.g. from a UI toggle), and every
/// helper consults it via its local <c>LogVerbose</c> wrapper before forwarding to
/// <see cref="IRenderLogger"/>.
///
/// Errors (<c>_logger.LogError</c>) are never gated — only the noisy per-frame /
/// per-load diagnostics flow through this.
/// </summary>
public class RenderLogGate
{
    public bool Verbose { get; set; } = false;
}
