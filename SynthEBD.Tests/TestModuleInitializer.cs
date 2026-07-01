using System.Runtime.CompilerServices;
using ReactiveUI.Builder;

namespace SynthEBD.Tests;

/// <summary>
/// Runs once when the test assembly loads, before any test. ReactiveUI 20+ no longer self-initializes
/// on first use (older ReactiveUI did, which is why these tests never needed this before); any
/// <c>WhenAnyValue</c>/<c>WhenActivated</c> throws "ReactiveUI has not been initialized" until the
/// RxAppBuilder has run. Several tests (and the integration harness) construct SynthEBD ViewModels /
/// ReactiveObjects directly outside <c>App.OnStartup</c>, so register the same WPF platform services
/// here once for the whole assembly — mirroring App.OnStartup.
/// </summary>
internal static class TestModuleInitializer
{
    [ModuleInitializer]
    internal static void Init()
    {
        RxAppBuilder.CreateReactiveUIBuilder()
            .WithWpf()
            .BuildApp();
    }
}
