using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Fixtura;

// Stands in for a deferred hosted service in the host: nothing happens when the host starts, and once a test has started
// the service, it stops with the host, through every lifecycle step the host would have run.
internal sealed class DeferredHostedService<THostedService>(THostedService service, ILogger? logger) : IHostedService
    where THostedService : IHostedService
{
    private readonly Lock _gate = new();

    // Not started, then started.
    private int _state;

    // WebApplicationFactory and the app's own Run both stop the host, at the same time, so the later stop waits for the
    // first instead of returning before the service has stopped.
    private Task? _stop;

    public THostedService Service => service;

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public async Task StartNowAsync(CancellationToken cancellationToken)
    {
        if (Interlocked.CompareExchange(ref _state, 1, 0) != 0)
        {
            throw new InvalidOperationException($"{typeof(THostedService).Name} has already been started.");
        }

        var lifecycle = service as IHostedLifecycleService;
        await (lifecycle?.StartingAsync(cancellationToken) ?? Task.CompletedTask);
        await service.StartAsync(cancellationToken);
        await (lifecycle?.StartedAsync(cancellationToken) ?? Task.CompletedTask);

        // The host only watches the services it starts, so a deferred worker's crash is logged here, as the host would.
        if (service is BackgroundService { ExecuteTask: { } work })
        {
            _ = work.ContinueWith(failed => logger?.LogError(failed.Exception!.InnerException, "BackgroundService failed"),
                CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        if (Volatile.Read(ref _state) == 0)
        {
            return Task.CompletedTask;
        }

        lock (_gate)
        {
            return _stop ??= StopNowAsync(cancellationToken);
        }
    }

    private async Task StopNowAsync(CancellationToken cancellationToken)
    {
        var lifecycle = service as IHostedLifecycleService;
        await (lifecycle?.StoppingAsync(cancellationToken) ?? Task.CompletedTask);
        await service.StopAsync(cancellationToken);
        await (lifecycle?.StoppedAsync(cancellationToken) ?? Task.CompletedTask);
    }
}
