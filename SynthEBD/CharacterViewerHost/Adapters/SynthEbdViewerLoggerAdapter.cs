using System;

namespace SynthEBD;

/// <summary>
/// Routes the CharacterViewer subsystem's <see cref="ICharacterViewerLogger"/>
/// calls to SynthEBD's host <see cref="Logger"/>. Errors that include an
/// <see cref="Exception"/> are flattened through <see cref="ExceptionLogger"/>
/// to match the formatting of the rest of SynthEBD's error output.
/// </summary>
public sealed class SynthEbdViewerLoggerAdapter : ICharacterViewerLogger
{
    private readonly Logger _inner;

    public SynthEbdViewerLoggerAdapter(Logger inner) => _inner = inner;

    public void LogMessage(string message) => _inner.LogMessage(message);

    public void LogError(string message) => _inner.LogError(message);

    public void LogError(string message, Exception ex) =>
        _inner.LogError(message + ": " + ExceptionLogger.GetExceptionStack(ex));
}
