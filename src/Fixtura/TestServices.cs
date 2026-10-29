using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Fixtura;

/// <summary>Records service swaps. Each one runs against the app's registrations when the host is built.</summary>
public sealed class TestServices
{
    private readonly List<Action<IServiceCollection>> _steps;

    internal TestServices(List<Action<IServiceCollection>> steps)
    {
        _steps = steps;
    }

    /// <summary>Anything the typed swaps don't cover, as plain service-collection code.</summary>
    public TestServices Configure(Action<IServiceCollection> configure)
    {
        _steps.Add(configure);
        return this;
    }

    public TestServices Replace<TService, TImplementation>() where TService : class where TImplementation : class, TService =>
        Configure(services => services.Replace<TService, TImplementation>());

    public TestServices ReplaceKeyed<TService, TImplementation>(object key)
        where TService : class where TImplementation : class, TService =>
        Configure(services => services.ReplaceKeyed<TService, TImplementation>(key));

    public TestServices Remove<TService>() => Configure(services => services.Remove<TService>());

    public TestServices RemoveHostedService<THostedService>() where THostedService : IHostedService =>
        Configure(services => services.RemoveHostedService<THostedService>());
}
