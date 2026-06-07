using Xunit;

namespace SynthEBD.Tests.Integration;

/// <summary>
/// xUnit collection binding every patcher integration test to a single <see cref="WpfApplicationFixture"/>.
///
/// Membership in one collection also guarantees these tests run <b>sequentially</b>, which matters because
/// <c>SynthEBDPaths._rootPath</c> is a static field: two harnesses building containers concurrently would
/// race on it. Sequential execution keeps each harness's settings root deterministic.
/// </summary>
[CollectionDefinition(Name)]
public sealed class PatcherIntegrationCollection : ICollectionFixture<WpfApplicationFixture>
{
    public const string Name = "PatcherIntegration";
}
