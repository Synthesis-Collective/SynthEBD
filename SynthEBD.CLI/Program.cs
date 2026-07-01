using System.Windows;
using ReactiveUI;
using ReactiveUI.Builder;

namespace SynthEBD.CLI;

/// <summary>
/// Console entry point for SynthEBD's headless tooling. SynthEBD's object graph contains WPF view models
/// with thread affinity (dispatcher timers, <c>Application.Current</c> subscriptions), so — like the
/// integration-test harness — every verb runs on an STA thread that owns a WPF <see cref="Application"/>
/// with a live dispatcher. Verb results are written to the real stdout; all other console output
/// (the <see cref="Logger"/> in Synthesis log mode, library chatter) is redirected to stderr so stdout
/// stays machine-parseable.
/// </summary>
internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        var resultWriter = Console.Out;
        Console.SetOut(Console.Error);

        CliOptions options;
        try
        {
            options = CliOptions.Parse(args);
        }
        catch (CliArgumentException ex)
        {
            Console.Error.WriteLine("Argument error: " + ex.Message);
            Console.Error.WriteLine();
            Console.Error.WriteLine(CliOptions.UsageText);
            return 2;
        }

        if (options.Verb == CliVerb.Help)
        {
            resultWriter.WriteLine(CliOptions.UsageText);
            return 0;
        }

        int exitCode = 2;
        bool verbCompleted = false;

        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };

        // ReactiveUI 20+ no longer self-initializes on assembly load; SynthEBD's object graph resolves
        // ReactiveObjects/ViewModels that use WhenAnyValue, which throw "ReactiveUI has not been
        // initialized" until the RxAppBuilder has run. Mirror App.OnStartup: register the WPF platform
        // services here (on this STA thread that owns the Application), then force the main-thread
        // scheduler onto the real dispatcher (the builder leaves it on the thread pool).
        RxAppBuilder.CreateReactiveUIBuilder()
            .WithWpf()
            .BuildApp();
        RxSchedulers.MainThreadScheduler = new System.Reactive.Concurrency.DispatcherScheduler(app.Dispatcher);

        app.DispatcherUnhandledException += (_, e) =>
        {
            if (verbCompleted)
            {
                // Transitively-resolved view models can post deferred dispatcher callbacks that fire after
                // the verb finished and its container was disposed; they are irrelevant to CLI output.
                Console.Error.WriteLine("[SynthEBD.CLI] Swallowed post-run dispatcher exception: " + e.Exception.Message);
            }
            else
            {
                Console.Error.WriteLine("[SynthEBD.CLI] Unhandled exception:");
                Console.Error.WriteLine(ExceptionLogger.GetExceptionStack(e.Exception));
                exitCode = 2;
                app.Shutdown();
            }
            e.Handled = true;
        };

        _ = app.Dispatcher.InvokeAsync(async () =>
        {
            try
            {
                exitCode = options.Verb switch
                {
                    CliVerb.Validate => await ValidateVerb.RunAsync(options, resultWriter),
                    CliVerb.Scan => await ScanVerb.RunAsync(options, resultWriter),
                    CliVerb.Draft => await DraftVerb.RunAsync(options, resultWriter),
                    CliVerb.Simulate => await SimulateVerb.RunAsync(options, resultWriter),
                    CliVerb.Package => await PackageVerb.RunAsync(options, resultWriter),
                    CliVerb.VerifyInstall => await VerifyInstallVerb.RunAsync(options, resultWriter),
                    CliVerb.ArchiveList => await PackageVerb.RunListAsync(options, resultWriter),
                    CliVerb.ArchiveExtract => await PackageVerb.RunExtractAsync(options, resultWriter),
                    _ => throw new InvalidOperationException("Unhandled verb: " + options.Verb),
                };
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("[SynthEBD.CLI] Fatal error:");
                Console.Error.WriteLine(ExceptionLogger.GetExceptionStack(ex));
                exitCode = 2;
            }
            finally
            {
                verbCompleted = true;
                app.Shutdown();
            }
        });

        app.Run();
        return exitCode;
    }
}
