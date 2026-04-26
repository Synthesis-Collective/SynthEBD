namespace CharacterViewer.Rendering;

/// <summary>
/// Shared runtime flag that gates the character viewer's informational log output across
/// all helpers (VM, mesh builder, texture/asset resolvers, parsers). Registered as a
/// singleton; <see cref="VM_CharacterViewer"/> writes <see cref="Verbose"/> when its
/// toolbar "Verbose Log" checkbox toggles, and every helper consults it via its local
/// <c>LogVerbose</c> wrapper before forwarding to <see cref="Logger"/>.
///
/// Errors (<c>_logger.LogError</c>) are never gated — only the noisy per-frame /
/// per-load diagnostics flow through this.
/// </summary>
public class CharacterViewerLogGate
{
    public bool Verbose { get; set; } = false;
}
