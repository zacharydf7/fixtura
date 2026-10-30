using Fixtura.Tests.Integration.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SampleApp;

namespace Fixtura.Tests.Integration.Hosting;

// A connection string from a resource, which only has it once provisioned, and settings of both kinds in either order.
public sealed class LazySettingsFixture : TestHost<Program>
{
    protected override void Configure(TestHostBuilder host)
    {
        SqliteResource sqlite = host.AddSqliteInMemory();
        host.Settings(() => new { ConnectionStrings = new { Reports = sqlite.ConnectionString("Reports") } });
        host.Settings(() => new { Probe = new { Order = "lazy" } });
        host.Settings(new { Probe = new { Order = "eager" } });
        host.Settings(new { Probe = new { Later = "eager" } });
        host.Settings(() => new { Probe = new { Later = "lazy" } });
    }
}

// A feature that takes a setting of its own.
public sealed class GreetingFeature(string greeting) : ITestHostFeature
{
    public int Runs { get; private set; }

    public void Configure(TestHostBuilder host)
    {
        Runs++;
        host.Settings(new { Probe = new { Greeting = greeting } });
    }
}

public sealed class FeatureInstanceFixture : TestHost<Program>
{
    public GreetingFeature Greeting { get; } = new("hello");

    protected override void Configure(TestHostBuilder host) => host.Use(Greeting).Use(Greeting);
}

public sealed class TwoResourcesFixture : TestHost<Program>
{
    protected override void Configure(TestHostBuilder host)
    {
        host.AddResource(new RecordingResource());
        host.AddResource(new RecordingResource());
    }
}

// Records when its hooks run, against the state of its resource.
public sealed class HookedFixture : TestHost<Program>
{
    public RecordingResource Resource { get; } = new();

    public List<string> Calls { get; } = [];

    protected override void Configure(TestHostBuilder host)
    {
        host.AddResource(Resource);
        Calls.Add("configure");
    }

    protected override ValueTask OnInitializedAsync()
    {
        Calls.Add($"initialized, provisioned: {Resource.Provisioned}");
        return ValueTask.CompletedTask;
    }

    protected override ValueTask OnDisposingAsync()
    {
        Calls.Add($"disposing, disposed: {Resource.Disposed}");
        return ValueTask.CompletedTask;
    }
}

// Its hook would fail on the unavailable resource, as reading a database that isn't there does.
public sealed class SkippedHookFixture : TestHost<Program>
{
    protected override void Configure(TestHostBuilder host) => host.AddResource(new UnavailableResource());

    protected override ValueTask OnInitializedAsync() => throw new InvalidOperationException("The resource isn't there.");
}

public sealed class FailingCleanupFixture : TestHost<Program>
{
    public RecordingResource Resource { get; } = new();

    protected override void Configure(TestHostBuilder host) => host.AddResource(Resource);

    protected override ValueTask OnDisposingAsync() => throw new InvalidOperationException("Cleanup failed.");
}

public class LazySettingsTests(LazySettingsFixture api) : IClassFixture<LazySettingsFixture>
{
    [Fact]
    public void Settings_ShouldReachAResourcesConnectionString_WhenGivenAFactory()
    {
        IConfiguration configuration = api.Services.GetRequiredService<IConfiguration>();

        string? reports = configuration.GetConnectionString("Reports");

        Assert.Equal(api.Resource<SqliteResource>().ConnectionString("Reports"), reports);
    }

    [Fact]
    public void Settings_ShouldKeepTheCallOrder_WhenFactoriesAndObjectsMix()
    {
        IConfiguration configuration = api.Services.GetRequiredService<IConfiguration>();

        (string?, string?) values = (configuration["Probe:Order"], configuration["Probe:Later"]);

        Assert.Equal(("eager", "lazy"), values);
    }

    [Fact]
    public async Task CreateScopeAsync_ShouldResolveScopedServices_WhenAwaited()
    {
        await using AsyncServiceScope scope = await api.CreateScopeAsync();

        IOrderPublisher publisher = scope.ServiceProvider.GetRequiredService<IOrderPublisher>();

        Assert.IsType<BusOrderPublisher>(publisher);
    }

    [Fact]
    public void Resource_ShouldNameTheType_WhenTheFixtureAddedNone()
    {
        var error = Assert.Throws<InvalidOperationException>(api.Resource<SqlServerResource>);

        Assert.Equal("The fixture added no SqlServerResource in Configure.", error.Message);
    }
}

public class FeatureInstanceTests(FeatureInstanceFixture api) : IClassFixture<FeatureInstanceFixture>
{
    [Fact]
    public void Use_ShouldRunAnInstanceOnce_WhenAddedTwice()
    {
        IConfiguration configuration = api.Services.GetRequiredService<IConfiguration>();

        string? greeting = configuration["Probe:Greeting"];

        Assert.Equal(("hello", 1), (greeting, api.Greeting.Runs));
    }
}

public class ResourceLookupTests(TwoResourcesFixture api) : IClassFixture<TwoResourcesFixture>
{
    [Fact]
    public void Resource_ShouldCountTheMatches_WhenTheFixtureAddedSeveral()
    {
        var error = Assert.Throws<InvalidOperationException>(api.Resource<RecordingResource>);

        Assert.StartsWith("The fixture added 2 of RecordingResource;", error.Message);
    }
}

public class FakeLookupTests(PartnersFixture api) : IClassFixture<PartnersFixture>
{
    [Fact]
    public void HttpFake_ShouldReturnTheFixturesFake_WhenAskedByNameOrType()
    {
        HttpFake rates = api.HttpFake("rates");

        HttpFake tracking = api.HttpFake<TrackingClient>();

        Assert.Same(api.Rates, rates);
        Assert.Same(api.Tracking, tracking);
    }

    [Fact]
    public void HttpFake_ShouldNameTheSetup_WhenTheFixtureFakesNoSuchClient()
    {
        var byName = Assert.Throws<InvalidOperationException>(() => api.HttpFake("payments"));

        var byType = Assert.Throws<InvalidOperationException>(api.HttpFake<BusOrderPublisher>);

        Assert.Contains("host.Http.Fake(\"payments\")", byName.Message);
        Assert.Contains("host.Http.Fake<BusOrderPublisher>()", byType.Message);
    }

    [Fact]
    public async Task HttpFake_ShouldReturnTheOverridesFake_WhenTheOverrideFakesTheClientAgain()
    {
        HttpFake? refaked = null;
        await using TestHost<Program> overridden = api.WithOverrides(host => refaked = host.Http.Fake("rates"));

        HttpFake rates = overridden.HttpFake("rates");

        Assert.Same(refaked, rates);
        Assert.NotSame(api.Rates, rates);
    }
}

public class FixtureLifetimeTests
{
    [Fact]
    public async Task OnInitializedAsyncAndOnDisposingAsync_ShouldRunAroundTheResources_WhenOverridden()
    {
        var api = new HookedFixture();
        await api.InitializeAsync();
        List<string> afterInitialize = [.. api.Calls];

        await api.DisposeAsync();

        Assert.Equal(["configure", "initialized, provisioned: True"], afterInitialize);
        Assert.Equal(["configure", "initialized, provisioned: True", "disposing, disposed: False"], api.Calls);
        Assert.True(api.Resource.Disposed);
    }

    [Fact]
    public async Task OnInitializedAsync_ShouldNotRun_WhenAResourceIsUnavailable()
    {
        await using var api = new SkippedHookFixture();

        Exception? error = await Record.ExceptionAsync(() => api.InitializeAsync().AsTask());

        Assert.Null(error);
    }

    [Fact]
    public async Task DisposeAsync_ShouldStillDisposeTheResources_WhenOnDisposingAsyncThrows()
    {
        var api = new FailingCleanupFixture();
        await api.InitializeAsync();
        _ = api.Services;

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => api.DisposeAsync().AsTask());

        Assert.Equal("Cleanup failed.", error.Message);
        Assert.True(api.Resource.Disposed);
    }

    [Fact]
    public async Task DisposeAsync_ShouldStopOverridesATestLeftRunning_WhenTheFixtureIsDisposed()
    {
        var log = new WorkLog();
        var api = new PlainFixture();
        await api.InitializeAsync();
        TestHost<Program> overridden = api.WithOverrides(host =>
            host.Services.Configure(services => services.AddSingleton(log).AddHostedService<WaitingWorker>()));
        _ = overridden.Services;
        await log.Running.Task;

        await api.DisposeAsync();

        Assert.True(log.Stopped);
        Assert.Null(await Record.ExceptionAsync(() => overridden.DisposeAsync().AsTask()));
    }

    [Fact]
    public async Task DisposeAsync_ShouldStopTheOtherOverrides_WhenOneFailsToShutDown()
    {
        var log = new WorkLog();
        var api = new RecordingResourceFixture();
        await api.InitializeAsync();
        TestHost<Program> broken = api.WithOverrides(host =>
            host.Services.Configure(services => services.AddHostedService<BrokenShutdown>()));
        TestHost<Program> waiting = api.WithOverrides(host =>
            host.Services.Configure(services => services.AddSingleton(log).AddHostedService<WaitingWorker>()));
        _ = broken.Services;
        _ = waiting.Services;
        await log.Running.Task;

        await Assert.ThrowsAnyAsync<Exception>(() => api.DisposeAsync().AsTask());

        Assert.True(log.Stopped);
        Assert.True(api.Resource.Disposed);
    }
}
