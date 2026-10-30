using Microsoft.Extensions.Hosting;

namespace Fixtura;

// Stands in for a deferred hosted service in the host: nothing happens when the host starts, and the service stops with
// the host once a test has started it.
internal sealed class DeferredHostedService<THostedService>(THostedService service) : IHostedService
    where THostedService : IHostedService
{
    private bool _started;

    public THostedService Service => service;

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public async Task StartNowAsync(CancellationToken cancellationToken)
    {
        if (_started)
        {
            throw new InvalidOperationException($"{typeof(THostedService).Name} has already been started.");
        }

        _started = true;
        await service.StartAsync(cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken) =>
        _started ? service.StopAsync(cancellationToken) : Task.CompletedTask;
}
