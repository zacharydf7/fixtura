namespace Fixtura.Tests.Integration.Hosting;

// Counts host builds through a service step, which runs once per build.
public sealed class CountingFixture : TestHost<Program>
{
    private int _builds;

    public int Builds => _builds;

    protected override void Configure(TestHostBuilder host) =>
        host.Services.Configure(_ => Interlocked.Increment(ref _builds));
}

public sealed class FailingBuildFixture : TestHost<Program>
{
    private int _builds;

    public int Builds => _builds;

    protected override void Configure(TestHostBuilder host) =>
        host.Services.Configure(_ => Interlocked.Increment(ref _builds)).Remove<IMissingService>();
}

public interface IMissingService;

// Holds the build until the test opens the gate, so a build that runs inside the caller can't finish.
public sealed class GatedBuildFixture : TestHost<Program>
{
    public ManualResetEventSlim Gate { get; } = new();

    protected override void Configure(TestHostBuilder host) =>
        host.Services.Configure(_ =>
        {
            if (!Gate.Wait(TimeSpan.FromSeconds(10)))
            {
                throw new TimeoutException("The test never opened the gate.");
            }
        });
}

public class ConcurrentBuildTests(CountingFixture api) : IClassFixture<CountingFixture>
{
    [Fact]
    public async Task CreateClientAsync_ShouldBuildTheHostOnce_WhenManyCallersStartTogether()
    {
        Task<HttpClient>[] clients = Enumerable.Range(0, 32).Select(_ => Task.Run(() => api.CreateClientAsync())).ToArray();

        await Task.WhenAll(clients);

        Assert.Equal(1, api.Builds);
    }
}

public class GatedBuildTests(GatedBuildFixture api) : IClassFixture<GatedBuildFixture>
{
    [Fact]
    public async Task CreateClientAsync_ShouldReturnBeforeTheHostIsBuilt_WhenTheBuildIsStillRunning()
    {
        Task<HttpClient> pending = api.CreateClientAsync();
        bool returnedEarly = !pending.IsCompleted;

        api.Gate.Set();

        await pending;
        Assert.True(returnedEarly);
    }
}

public class FailedBuildTests(FailingBuildFixture api) : IClassFixture<FailingBuildFixture>
{
    [Fact]
    public async Task Services_ShouldNotBuildAgain_WhenTheFirstBuildFailed()
    {
        Exception first = Assert.ThrowsAny<Exception>(() => api.Services);

        Exception second = await Assert.ThrowsAnyAsync<Exception>(() => api.CreateClientAsync());

        Assert.Equal(1, api.Builds);
        Assert.Same(first, second);
    }
}
