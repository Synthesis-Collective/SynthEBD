using System;
using System.Windows;

namespace SynthEBD;

/// <summary>
/// SynthEBD <see cref="IRenderThreadMarshaller"/> implementation that
/// marshals to <see cref="Application.Current"/>'s dispatcher — the WPF UI
/// thread that owns the <see cref="UC_CharacterViewer"/>'s GL context and
/// where <see cref="VM_CharacterViewer.ProcessPendingScene"/> runs from
/// the GLWpfControl render callback.
///
/// The offscreen renderer (Phase D) doesn't use this — it runs work on
/// its own thread and constructs <see cref="VM_CharacterViewer"/> with
/// the default <see cref="InlineRenderThreadMarshaller"/>.
/// </summary>
public sealed class WpfDispatcherMarshaller : IRenderThreadMarshaller
{
    public void Invoke(Action action)
    {
        // Application.Current is null during shutdown / before App.OnStartup
        // completes. Run the action inline in those edge cases — the caller
        // is already running on whatever thread WPF would have marshalled
        // to anyway, since there's no live dispatcher to forward through.
        var app = Application.Current;
        if (app?.Dispatcher == null)
        {
            action();
            return;
        }
        app.Dispatcher.Invoke(action);
    }
}
