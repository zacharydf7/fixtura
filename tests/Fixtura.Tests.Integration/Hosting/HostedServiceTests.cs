using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Fixtura.Tests.Integration.Hosting;

public sealed class WorkLog
{
    public int Passes { get; set; }

    public bool Stopped { get; set; }

    public TaskCompletionSource Running { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
}

// One pass over its queue, as an outbox worker makes, then done.
public sealed class OnePassWorker(WorkLog log) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        log.Passes++;
        return Task.CompletedTask;
    }
}

// Runs until the host stops it.
public sealed class WaitingWorker(WorkLog log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        log.Running.TrySetResult();
        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            log.Stopped = true;
        }
    }
}

public sealed class CrashingWorker : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Yield();
        throw new InvalidOperationException("The queue is gone.");
    }
}

public sealed class DeferredWorkerFixture : TestHost<Program>
{
    public WorkLog Log { get; } = new();

    protected override void Configure(TestHostBuilder host) => host.Services
        .Configure(services => services.AddSingleton(Log).AddHostedService<OnePassWorker>().AddHostedService<WaitingWorker>())
        .DeferHostedService<OnePassWorker>()
        .DeferHostedService<WaitingWorker>();
}

public sealed class DeferredCrashFixture : TestHost<Program>
{
    protected override void Configure(TestHostBuilder host)
    {
        host.Services.Configure(services => services.AddHostedService<CrashingWorker>()).DeferHostedService<CrashingWorker>();
        host.FailOnBackgroundServiceCrash();
    }
}

public sealed class CrashingWorkerFixture : TestHost<Program>
{
    protected override void Configure(TestHostBuilder host)
    {
        host.Services.Configure(services => services.AddHostedService<CrashingWorker>());
        host.FailOnBackgroundServiceCrash();
    }
}

public class DeferredHostedServiceTests(DeferredWorkerFixture api) : IClassFixture<DeferredWorkerFixture>
{
    [Fact]
    public async Task StartHostedServiceAsync_ShouldRunTheServiceOnce_WhenATestStartsIt()
    {
        int passesBeforeStart = api.Services.GetRequiredService<WorkLog>().Passes;

        OnePassWorker worker = await api.StartHostedServiceAsync<OnePassWorker>(TestContext.Current.CancellationToken);
        await worker.ExecuteTask!;

        Assert.Equal((0, 1), (passesBeforeStart, api.Log.Passes));
    }

    [Fact]
    public async Task StartHostedServiceAsync_ShouldThrow_WhenTheServiceWasAlreadyStarted()
    {
        await api.StartHostedServiceAsync<WaitingWorker>(TestContext.Current.CancellationToken);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            api.StartHostedServiceAsync<WaitingWorker>(TestContext.Current.CancellationToken));

        Assert.Contains("already been started", error.Message);
    }

    [Fact]
    public async Task StartHostedServiceAsync_ShouldNameTheSetup_WhenTheServiceIsNotDeferred()
    {
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            api.StartHostedServiceAsync<CrashingWorker>(TestContext.Current.CancellationToken));

        Assert.Contains("DeferHostedService<CrashingWorker>()", error.Message);
    }
}

public class HostedServiceLifetimeTests
{
    [Fact]
    public async Task DisposeAsync_ShouldStopAStartedDeferredService_WhenTheHostStops()
    {
        var api = new DeferredWorkerFixture();
        await api.InitializeAsync();
        await api.StartHostedServiceAsync<WaitingWorker>(TestContext.Current.CancellationToken);
        await api.Log.Running.Task;

        await api.DisposeAsync();

        Assert.True(api.Log.Stopped);
    }

    [Fact]
    public async Task FailOnBackgroundServiceCrash_ShouldFailTheFixture_WhenADeferredServiceCrashesAfterItStarted()
    {
        var api = new DeferredCrashFixture();
        await api.InitializeAsync();
        CrashingWorker worker = await api.StartHostedServiceAsync<CrashingWorker>(TestContext.Current.CancellationToken);
        await Record.ExceptionAsync(() => worker.ExecuteTask!);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => api.CreateClientAsync());

        Assert.Contains("CrashingWorker crashed", error.Message);
        await Assert.ThrowsAsync<InvalidOperationException>(() => api.DisposeAsync().AsTask());
    }

    [Fact]
    public async Task FailOnBackgroundServiceCrash_ShouldFailTheFixture_WhenABackgroundServiceCrashed()
    {
        var api = new CrashingWorkerFixture();
        await api.InitializeAsync();
        CrashingWorker worker = api.Services.GetServices<IHostedService>().OfType<CrashingWorker>().Single();
        await Record.ExceptionAsync(() => worker.ExecuteTask!);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => api.CreateClientAsync());

        Assert.Contains("CrashingWorker crashed: The queue is gone.", error.Message);
        await Assert.ThrowsAsync<InvalidOperationException>(() => api.DisposeAsync().AsTask());
    }
}
