using DonetickCalDav.Testing.Harness;

namespace DonetickCalDav.IntegrationTests;

/// <summary>
/// Base class for L2 tests. The Donetick stub is shared by the tests of one class and reset before
/// each test; every test starts its own bridge with <see cref="StartBridgeAsync"/>, which is disposed
/// after the test. Tests in one class run sequentially, classes run in parallel.
/// </summary>
[Trait("Level", "Integration")]
public abstract class IntegrationTest(DonetickStub donetick) : IClassFixture<DonetickStub>, IAsyncLifetime
{
    private BridgeHost? _bridge;

    protected DonetickStub Donetick { get; } = donetick;

    /// <summary>The bridge started by <see cref="StartBridgeAsync"/>.</summary>
    protected BridgeHost Bridge => _bridge ?? throw new InvalidOperationException("Call StartBridgeAsync first.");

    /// <summary>Starts the bridge against the stub; by default waits until the startup load is done.</summary>
    protected async Task<BridgeHost> StartBridgeAsync(Action<BridgeOptions>? configure = null)
    {
        if (_bridge != null)
            throw new InvalidOperationException("The bridge has already been started in this test.");

        _bridge = await BridgeHost.StartAsync(Donetick, configure);
        return _bridge;
    }

    public virtual ValueTask InitializeAsync()
    {
        Donetick.Reset();
        return ValueTask.CompletedTask;
    }

    public virtual async ValueTask DisposeAsync()
    {
        if (_bridge != null)
            await _bridge.DisposeAsync();
        GC.SuppressFinalize(this);
    }
}
