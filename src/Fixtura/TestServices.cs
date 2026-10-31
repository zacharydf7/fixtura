using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Fixtura;

/// <summary>Records service swaps. Each one runs against the app's registrations when the host is built.</summary>
public sealed class TestServices
{
    private readonly List<Action<IServiceCollection>> _steps;
    private readonly Recorders _recorders;

    internal TestServices(List<Action<IServiceCollection>> steps, Recorders recorders)
    {
        _steps = steps;
        _recorders = recorders;
    }

    /// <summary>Anything the typed swaps don't cover, as plain service-collection code.</summary>
    public TestServices Configure(Action<IServiceCollection> configure)
    {
        _steps.Add(configure);
        return this;
    }

    /// <inheritdoc cref="ServiceCollectionSwaps.Replace{TService, TImplementation}(IServiceCollection, ServiceLifetime?)"/>
    public TestServices Replace<TService, TImplementation>(ServiceLifetime? lifetime = null)
        where TService : class where TImplementation : class, TService =>
        Configure(services => services.Replace<TService, TImplementation>(lifetime));

    /// <inheritdoc cref="ServiceCollectionSwaps.Replace{TService}(IServiceCollection, TService)"/>
    public TestServices Replace<TService>(TService instance) where TService : class =>
        Configure(services => services.Replace(instance));

    public TestServices ReplaceKeyed<TService, TImplementation>(object key)
        where TService : class where TImplementation : class, TService =>
        Configure(services => services.ReplaceKeyed<TService, TImplementation>(key));

    public TestServices Remove<TService>() => Configure(services => services.Remove<TService>());

    /// <summary>
    /// Replaces every registration of <typeparamref name="TService"/>, an interface, with one that records the app's calls,
    /// such as the emails an <c>IEmailSender</c> was asked to send, for a test to read through the fixture's
    /// <c>Recorder&lt;TService&gt;()</c>. Calls return a completed task or a default value unless a test sets a result.
    /// </summary>
    public TestServices Record<TService>() where TService : class
    {
        CallRecorder<TService> recorder = _recorders.Add<TService>();
        return Configure(services =>
        {
            ServiceCollectionSwaps.RemoveWhere(services, $"Record<{TypeNames.Readable(typeof(TService))}>",
                descriptor => descriptor.ServiceType == typeof(TService) && !descriptor.IsKeyedService);
            services.AddSingleton(recorder.Service);
        });
    }

    public TestServices RemoveHostedService<THostedService>() where THostedService : IHostedService =>
        Configure(services => services.RemoveHostedService<THostedService>());

    /// <summary>
    /// Keeps the hosted service from starting with the host, so a test starts it once what it needs is in place, with the
    /// fixture's <c>StartHostedServiceAsync</c>. The host still stops it. Matches what <c>RemoveHostedService</c> matches.
    /// </summary>
    public TestServices DeferHostedService<THostedService>() where THostedService : class, IHostedService =>
        Configure(services => services.DeferHostedService<THostedService>());
}
