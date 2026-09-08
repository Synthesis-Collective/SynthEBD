using System;
using System.Windows;
using OpenTK.Wpf;

namespace SynthEBD;

/// <summary>
/// Per-viewer <see cref="IRenderThreadMarshaller"/> that marshals to the WPF UI
/// thread AND makes the owning <see cref="GLWpfControl"/>'s GL context current
/// before running the action.
///
/// <para><b>Why this exists.</b> <see cref="WpfDispatcherMarshaller"/> only gets
/// the action onto the right <i>thread</i>. That is sufficient while exactly one
/// <see cref="VM_CharacterViewer"/> is alive, because the only GL context the UI
/// thread ever has current is that viewer's. It stops being sufficient the moment
/// a second viewer exists: GLWpfControl mints a private context per control, GL
/// object names are per-context, and two fresh contexts hand out the SAME low
/// integers for the same allocation sequence. A buffer upload or a delete
/// dispatched for viewer B, executing while viewer A's context happens to be
/// current, silently reads or destroys viewer A's object of the same name.
/// See the contract on <see cref="VM_CharacterViewer.RenderThreadMarshaller"/>.</para>
///
/// <para><b>Where it matters.</b> Everything on the hot path already defers its GL
/// work to <c>ProcessPendingScene</c> / <c>Renderer.Render</c>, which run from the
/// control's own <c>Render</c> callback where the context is current by
/// construction. The calls this marshaller protects are the ones that run
/// <i>outside</i> that callback: the BodySlide vertex re-upload in
/// <c>ApplyMorphSet</c>, and the shader/VAO/texture teardown in
/// <c>VM_CharacterViewer.Dispose</c>.</para>
///
/// <para><b>Thread safety.</b> <c>IGraphicsContext.MakeCurrent</c> is only safe on
/// the thread the GLWpfControl runs on, so the MakeCurrent is issued <i>inside</i>
/// the dispatched action — never on the caller's thread. The context is left
/// current on return: each control's next <c>Render</c> callback makes its own
/// current regardless, so there is nothing to restore.</para>
/// </summary>
public sealed class GlControlPinningMarshaller : IRenderThreadMarshaller
{
    private readonly GLWpfControl _control;
    private readonly Action<string>? _diagnosticLog;

    /// <summary>Creates a marshaller pinned to <paramref name="control"/>'s GL context.</summary>
    /// <param name="control">The GLWpfControl whose context owns the calling viewer's GL objects.</param>
    /// <param name="diagnosticLog">Optional sink for the one-shot "could not make context current" notice.</param>
    public GlControlPinningMarshaller(GLWpfControl control, Action<string>? diagnosticLog = null)
    {
        _control = control ?? throw new ArgumentNullException(nameof(control));
        _diagnosticLog = diagnosticLog;
    }

    /// <summary>The control whose context this marshaller pins to. Lets a host check
    /// whether an already-installed marshaller belongs to it before replacing one — a
    /// VM can migrate between controls (WPF recreates the control on navigation), and a
    /// marshaller left pinned to the previous control would defeat the whole purpose.</summary>
    public GLWpfControl Control => _control;

    /// <summary>
    /// Set by the owning control for the duration of its own <c>Render</c> callback, where
    /// GLWpfControl has already made the context current. Inside that window the MakeCurrent
    /// here would be redundant, and re-entering the windowing layer's context switch from
    /// inside its own render callback is not a contract any of these bindings document — so
    /// we simply skip it.
    ///
    /// <para>This is reached in practice: <c>ProcessPendingScene</c> drains a queued
    /// <c>_pendingMorphSet</c> by calling <c>ApplyMorphSet</c>, whose vertex uploads now go
    /// through this marshaller.</para>
    /// </summary>
    public bool ContextAlreadyCurrent { get; set; }

    private bool _makeCurrentFailureLogged;

    /// <summary>Runs <paramref name="action"/> on the UI thread with this control's GL context current.</summary>
    public void Invoke(Action action)
    {
        if (action == null) return;

        // Application.Current is null during shutdown / before App.OnStartup completes.
        // Mirrors WpfDispatcherMarshaller: run inline, since there is no live dispatcher
        // to forward through and the caller is already on whatever thread we'd reach.
        var app = Application.Current;
        if (app?.Dispatcher == null)
        {
            PinAndRun(action);
            return;
        }

        app.Dispatcher.Invoke(() => PinAndRun(action));
    }

    private void PinAndRun(Action action)
    {
        TryMakeCurrent();
        action();
    }

    private void TryMakeCurrent()
    {
        if (ContextAlreadyCurrent) return;

        // Context is null before GLWpfControl.Start() succeeds and after its Dispose.
        // Both are legitimate here: a queued teardown can outlive the control, and a
        // load can be queued before the first render tick. Running the action without
        // pinning is exactly the old single-viewer behavior, so degrade rather than throw.
        var context = _control.Context;
        if (context == null) return;

        try
        {
            context.MakeCurrent();
        }
        catch (Exception ex)
        {
            // A dead/disposed context throws rather than no-opping. There is nothing to
            // recover here — the action either targets objects that died with the context
            // or is a teardown that the driver has already reclaimed — so log once and let
            // the action proceed under whatever context is current.
            if (!_makeCurrentFailureLogged)
            {
                _makeCurrentFailureLogged = true;
                _diagnosticLog?.Invoke(
                    "GlControlPinningMarshaller: MakeCurrent failed (" + ex.GetType().Name + ": "
                    + ex.Message + "); GL calls for this viewer are no longer context-pinned.");
            }
        }
    }
}
