using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit.Sdk;

namespace Fixtura.Tests.Integration.Hosting;

// Where the app puts what it sends later, as an outbox does.
public sealed class Outbox
{
    private readonly List<string> _sent = [];

    public IReadOnlyList<string> Sent
    {
        get
        {
            lock (_sent)
            {
                return [.. _sent];
            }
        }
    }

    public void Send(string message)
    {
        lock (_sent)
        {
            _sent.Add(message);
        }
    }
}

// Sends a reminder an hour after the app starts, on the app's clock.
public sealed class HourlyReminder(Outbox outbox, TimeProvider clock) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromHours(1), clock);
        await timer.WaitForNextTickAsync(stoppingToken);
        outbox.Send("reminder");
    }
}

public sealed class ClockedOutboxFixture : TestHost<Program>
{
    protected override void Configure(TestHostBuilder host)
    {
        host.UseFakeClock();
        host.Services.Configure(services => services.AddSingleton<Outbox>().AddHostedService<HourlyReminder>());
    }
}

public sealed class CrashGate
{
    public TaskCompletionSource Open { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
}

// Crashes only once a test has opened the gate, so the crash lands while the test is already waiting.
public sealed class LateCrashingWorker(CrashGate gate) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await gate.Open.Task.WaitAsync(stoppingToken);
        throw new InvalidOperationException("The queue is gone.");
    }
}

public sealed class LateCrashFixture : TestHost<Program>
{
    public CrashGate Gate { get; } = new();

    protected override void Configure(TestHostBuilder host)
    {
        host.Services.Configure(services => services.AddSingleton(Gate).AddHostedService<LateCrashingWorker>());
        host.FailOnBackgroundServiceCrash();
    }
}

public sealed class OutboxFixture : TestHost<Program>
{
    protected override void Configure(TestHostBuilder host) =>
        host.Services.Configure(services => services.AddSingleton<Outbox>());
}

public class EventuallyTests(OutboxFixture api) : IClassFixture<OutboxFixture>
{
    [Fact]
    public async Task EventuallyAsync_ShouldPass_WhenTheAppFinishesTheWorkAfterAWhile()
    {
        var outbox = api.Services.GetRequiredService<Outbox>();
        _ = Task.Run(async () =>
        {
            await Task.Delay(200);
            outbox.Send("receipt");
        }, TestContext.Current.CancellationToken);

        await api.EventuallyAsync(services => Assert.Contains("receipt", services.GetRequiredService<Outbox>().Sent));

        Assert.Contains("receipt", outbox.Sent);
    }

    [Fact]
    public async Task EventuallyAsync_ShouldFailWithTheLastAssertion_WhenTheWorkNeverHappens()
    {
        Task waiting = api.EventuallyAsync(async services =>
        {
            await Task.Yield();
            Assert.Contains("refund", services.GetRequiredService<Outbox>().Sent);
        }, TimeSpan.FromMilliseconds(200));

        await Assert.ThrowsAsync<ContainsException>(() => waiting);
    }
}

public class ClockedEventuallyTests(ClockedOutboxFixture api) : IClassFixture<ClockedOutboxFixture>
{
    [Fact]
    public async Task EventuallyAsync_ShouldBringATimerDue_WhenItAdvancesTheFakeClock()
    {
        await api.EventuallyAsync(services => Assert.Contains("reminder", services.GetRequiredService<Outbox>().Sent),
            advanceClockBy: TimeSpan.FromMinutes(15));

        Assert.True(api.Clock.GetUtcNow() - DateTimeOffset.UtcNow >= TimeSpan.FromMinutes(59));
    }
}

public class EventuallyCrashTests
{
    [Fact]
    public async Task EventuallyAsync_ShouldStopWithTheCrash_WhenABackgroundServiceCrashesWhileItWaits()
    {
        var api = new LateCrashFixture();
        await api.InitializeAsync();
        _ = api.Services;
        var waited = Stopwatch.StartNew();

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => api.EventuallyAsync(_ =>
        {
            api.Gate.Open.TrySetResult();
            Assert.Fail("The work never finishes.");
        }, TimeSpan.FromSeconds(30)));

        Assert.Contains("LateCrashingWorker crashed", error.Message);
        Assert.True(waited.Elapsed < TimeSpan.FromSeconds(10));
        await Assert.ThrowsAsync<InvalidOperationException>(() => api.DisposeAsync().AsTask());
    }

    [Fact]
    public async Task EventuallyAsync_ShouldNameTheSetup_WhenAskedToAdvanceAClockTheFixtureDoesNotHave()
    {
        await using var api = new OutboxFixture();
        await api.InitializeAsync();

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            api.EventuallyAsync(_ => { }, advanceClockBy: TimeSpan.FromMinutes(1)));

        Assert.Contains("host.UseFakeClock()", error.Message);
    }
}
