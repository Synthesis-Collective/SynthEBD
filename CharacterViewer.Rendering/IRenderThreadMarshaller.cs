using System;

namespace CharacterViewer.Rendering;

/// <summary>
/// Marshals a callback onto the host's "render thread" — the thread that
/// owns the GL context and is responsible for advancing the scene-install
/// queue (<see cref="VM_CharacterViewer.ProcessPendingScene"/>).
///
/// SynthEBD's WPF host marshals to <c>Application.Current.Dispatcher</c>
/// because <c>UC_CharacterViewer</c>'s render callback runs on the WPF UI
/// thread and pending-scene fields must be written from there. The
/// offscreen renderer (Phase D) doesn't have a WPF Application — its
/// "render thread" is whatever thread executes
/// <c>RenderToPngAsync</c> via <see cref="System.Threading.Tasks.Task.Run"/>,
/// and the VM is single-use per render so there's no cross-thread
/// coordination needed. <see cref="InlineRenderThreadMarshaller"/> covers
/// that case by running the action immediately on the calling thread.
/// </summary>
public interface IRenderThreadMarshaller
{
    /// <summary>Runs <paramref name="action"/> on the render thread,
    /// blocking the caller until it completes. Implementations may run
    /// inline (caller is already on the render thread) or marshal across
    /// thread boundaries.</summary>
    void Invoke(Action action);
}

/// <summary>Default marshaller — runs the action immediately on the
/// calling thread. Used by the offscreen renderer (and by tests) where
/// no cross-thread marshalling is needed.</summary>
public sealed class InlineRenderThreadMarshaller : IRenderThreadMarshaller
{
    public void Invoke(Action action) => action();
}
