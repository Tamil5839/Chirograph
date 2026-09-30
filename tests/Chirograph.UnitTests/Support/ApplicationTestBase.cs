namespace Chirograph.UnitTests.Support;

/// <summary>Gives each test a fresh <see cref="ApplicationTestHost"/>.</summary>
public abstract class ApplicationTestBase : IAsyncLifetime
{
    private ApplicationTestHost? _host;

    internal ApplicationTestHost Host => _host ?? throw new InvalidOperationException("Host not initialised.");

    protected static CancellationToken Token => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => _host = await ApplicationTestHost.CreateAsync();

    public async ValueTask DisposeAsync()
    {
        if (_host is not null)
            await _host.DisposeAsync();
        GC.SuppressFinalize(this);
    }
}
