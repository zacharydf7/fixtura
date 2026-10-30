using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

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

// Records each lifecycle call the host makes, in order.
public sealed class LifecycleWorker : IHostedLifecycleService
{
    public List<string> Calls { get; } = [];

    public Task StartingAsync(CancellationToken cancellationToken) => Record("Starting");

    public Task StartAsync(CancellationToken cancellationToken) => Record("Start");

    public Task StartedAsync(CancellationToken cancellationToken) => Record("Started");

    public Task StoppingAsync(CancellationToken cancellationToken) => Record("Stopping");

    public Task StopAsync(CancellationToken cancellationToken) => Record("Stop");

    public Task StoppedAsync(CancellationToken cancellationToken) => Record("Stopped");

    private Task Record(string call)
    {
        Calls.Add(call);
        return Task.CompletedTask;
    }
}

public sealed class DeferredLifecycleFixture : TestHost<Program>
{
    public LifecycleWorker Worker { get; } = new();

    protected override void Configure(TestHostBuilder host) => host.Services
        .Configure(services => services.AddSingleton<IHostedService>(Worker))
        .DeferHostedService<LifecycleWorker>();
}

// Keeps what the app logs, so a test can see a log no test output would show.
public sealed class LogCapture : ILoggerProvider, ILogger
{
    private readonly List<string> _lines = [];

    public IReadOnlyList<string> Lines
    {
        get
        {
            lock (_lines)
            {
                return [.. _lines];
            }
        }
    }

    public ILogger CreateLogger(string categoryName) => this;

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        lock (_lines)
        {
            _lines.Add($"{formatter(state, exception)}: {exception?.Message}");
        }
    }

    public void Dispose()
    {
    }
}

// A deferred worker that crashes, in a fixture that doesn't fail on crashes, so only the log can show it.
public sealed class LoggedCrashFixture : TestHost<Program>
{
    public LogCapture Log { get; } = new();

    protected override void Configure(TestHostBuilder host) => host.Services
        .Configure(services => services.AddLogging(logging => logging.AddProvider(Log)).AddHostedService<CrashingWorker>())
        .DeferHostedService<CrashingWorker>();
}

public sealed class DeferredCrashFixture : TestHost<Program>
{
    protected override void Configure(TestHostBuilder host)
    {
        host.Services.Configure(services => services.AddHostedService<CrashingWorker>()).DeferHostedService<CrashingWorker>();
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
    public async Task StartHostedServiceAsync_ShouldRunTheWholeLifecycle_WhenTheServiceHasOne()
    {
        var api = new DeferredLifecycleFixture();
        await api.InitializeAsync();
        await api.StartHostedServiceAsync<LifecycleWorker>(TestContext.Current.CancellationToken);

        await api.DisposeAsync();

        Assert.Equal(["Starting", "Start", "Started", "Stopping", "Stop", "Stopped"], api.Worker.Calls);
    }

    [Fact]
    public async Task StartHostedServiceAsync_ShouldLogTheCrash_WhenADeferredWorkerCrashes()
    {
        await using var api = new LoggedCrashFixture();
        await api.InitializeAsync();
        CrashingWorker worker = await api.StartHostedServiceAsync<CrashingWorker>(TestContext.Current.CancellationToken);
        await Record.ExceptionAsync(() => worker.ExecuteTask!);

        await api.EventuallyAsync(_ => Assert.Contains("BackgroundService failed: The queue is gone.", api.Log.Lines));
    }

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
        // The worker crashes only once the host is built: a crash during start-up stops the host while it's still building.
        var api = new LateCrashFixture();
        await api.InitializeAsync();
        LateCrashingWorker worker = api.Services.GetServices<IHostedService>().OfType<LateCrashingWorker>().Single();
        api.Gate.Open.TrySetResult();
        await Record.ExceptionAsync(() => worker.ExecuteTask!);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => api.CreateClientAsync());

        Assert.Contains("LateCrashingWorker crashed: The queue is gone.", error.Message);
        await Assert.ThrowsAsync<InvalidOperationException>(() => api.DisposeAsync().AsTask());
    }
}
