using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Fixtura;

// Stands in for a deferred hosted service in the host: nothing happens when the host starts, and once a test has started
// the service, it stops with the host, through every lifecycle step the host would have run.
internal sealed class DeferredHostedService<THostedService>(THostedService service, ILogger? logger) : IHostedService
    where THostedService : IHostedService
{
    // Not started, started, then stopped: WebApplicationFactory can stop the host more than once.
    private int _state;

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

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (Interlocked.CompareExchange(ref _state, 2, 1) != 1)
        {
            return;
        }

        var lifecycle = service as IHostedLifecycleService;
        await (lifecycle?.StoppingAsync(cancellationToken) ?? Task.CompletedTask);
        await service.StopAsync(cancellationToken);
        await (lifecycle?.StoppedAsync(cancellationToken) ?? Task.CompletedTask);
    }
}
