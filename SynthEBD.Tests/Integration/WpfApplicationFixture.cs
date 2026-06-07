using System.Windows;
using System.Windows.Threading;

namespace SynthEBD.Tests.Integration;

/// <summary>
/// Owns a single WPF <see cref="Application"/> on a dedicated STA thread for the whole test run, and
/// marshals test work onto that thread.
///
/// <para>The patcher and its (transitively resolved) view models are full WPF citizens: some subscribe to
/// <c>Application.Current.Exit</c> and use <c>DispatcherTimer</c>/dispatcher marshalling, all of which have
/// thread affinity to the thread that created the <see cref="Application"/>. xUnit runs tests on MTA worker
/// threads, so the entire harness lifecycle — container build, settings load, and
/// <c>Patcher.RunPatcher()</c> — must execute on this fixture's STA thread. Tests wrap their body in
/// <see cref="RunOnStaAsync"/>.</para>
/// </summary>
public sealed class WpfApplicationFixture : IDisposable
{
    private readonly Thread _staThread;
    private Dispatcher _dispatcher = null!;

    public WpfApplicationFixture()
    {
        var ready = new ManualResetEventSlim(false);
        _staThread = new Thread(() =>
        {
            if (Application.Current == null)
            {
                _ = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            }
            _dispatcher = Dispatcher.CurrentDispatcher;
            // The patcher's transitively-resolved UI view models post deferred dispatcher callbacks that
            // resolve from the Autofac container. A few can fire after a test disposes its harness/container;
            // swallow those so a stray late UI-init callback can't crash the test host. They are irrelevant
            // to headless patcher execution.
            _dispatcher.UnhandledException += (_, e) =>
            {
                Console.WriteLine("[WpfApplicationFixture] swallowed dispatcher exception: " + e.Exception.Message);
                e.Handled = true;
            };
            ready.Set();
            Dispatcher.Run();
        })
        {
            IsBackground = true,
            Name = "SynthEBD.Tests WPF Application",
        };
        _staThread.SetApartmentState(ApartmentState.STA);
        _staThread.Start();
        ready.Wait(TimeSpan.FromSeconds(30));
    }

    /// <summary>
    /// Runs an asynchronous test body on the WPF STA thread and awaits its completion, surfacing any
    /// exception (including assertion failures) back to the calling test thread.
    /// </summary>
    public async Task RunOnStaAsync(Func<Task> body)
    {
        var op = _dispatcher.InvokeAsync(body);
        // InvokeAsync(Func<Task>) completes when the delegate returns the inner Task; unwrap to await it.
        var inner = await op.Task.ConfigureAwait(false);
        await inner.ConfigureAwait(false);
    }

    public void Dispose()
    {
        _dispatcher?.InvokeShutdown();
        _staThread.Join(TimeSpan.FromSeconds(5));
    }
}
