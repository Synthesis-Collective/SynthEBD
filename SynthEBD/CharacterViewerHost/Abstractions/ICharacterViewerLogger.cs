using System;

namespace SynthEBD;

/// <summary>
/// Logging surface that the CharacterViewer subsystem uses for all message + error
/// output. SynthEBD adapts its concrete <see cref="Logger"/> behind this interface
/// (see <see cref="SynthEbdViewerLoggerAdapter"/>); other host applications supply
/// their own implementation. Verbose/diagnostic messages are gated upstream by
/// <see cref="CharacterViewerLogGate"/> — errors are never gated.
/// </summary>
public interface ICharacterViewerLogger
{
    void LogMessage(string message);
    void LogError(string message);
    void LogError(string message, Exception ex);
}
