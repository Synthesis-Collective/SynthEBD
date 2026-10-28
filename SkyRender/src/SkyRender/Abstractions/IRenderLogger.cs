using System;

namespace SkyRender;

/// <summary>
/// Logging surface that SkyRender uses for all message + error
/// output. Each host supplies its own implementation. Verbose/diagnostic messages are gated upstream by
/// <see cref="RenderLogGate"/> — errors are never gated.
/// </summary>
public interface IRenderLogger
{
    void LogMessage(string message);
    void LogError(string message);
    void LogError(string message, Exception ex);

    /// <summary>Installs a thread-local diagnostic sink that
    /// <see cref="LogMessage"/> / <see cref="LogError"/> routes through for the
    /// duration of the returned scope. Used by the offscreen renderer to
    /// redirect its dedicated render thread's diagnostic emissions to a
    /// per-request flow-scoped writer the host snapshotted on its own thread
    /// (an AsyncLocal flow writer can't propagate across the host→queue→render-thread
    /// hand-off because the render thread is a raw <c>Thread</c>, not a Task
    /// continuation). The default implementation is a no-op — hosts that
    /// don't need per-render capture can leave it alone.</summary>
    IDisposable PushDiagnosticSink(Action<string>? sink) => NoOpDisposable.Instance;
}

internal sealed class NoOpDisposable : IDisposable
{
    public static readonly NoOpDisposable Instance = new();
    public void Dispose() { }
}
