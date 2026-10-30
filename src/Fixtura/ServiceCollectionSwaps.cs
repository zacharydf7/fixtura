using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Fixtura;

/// <summary>
/// Typed swaps on a plain service collection. Each one throws when nothing matched, so a renamed type fails loudly.
/// </summary>
public static class ServiceCollectionSwaps
{
    /// <summary>
    /// Replaces every registration of TService, keeping the lifetime the app gave it unless <paramref name="lifetime"/> is
    /// set, for example to keep an in-memory fake's state for the whole host.
    /// </summary>
    public static IServiceCollection Replace<TService, TImplementation>(this IServiceCollection services,
        ServiceLifetime? lifetime = null) where TService : class where TImplementation : class, TService
    {
        ServiceLifetime appLifetime = RemoveAll(services, typeof(TService), serviceKey: null, $"Replace<{typeof(TService).Name}>");
        services.Add(new ServiceDescriptor(typeof(TService), typeof(TImplementation), lifetime ?? appLifetime));
        return services;
    }

    /// <summary>Replaces every registration of TService with this one instance, so a test can inspect it.</summary>
    public static IServiceCollection Replace<TService>(this IServiceCollection services, TService instance)
        where TService : class
    {
        RemoveAll(services, typeof(TService), serviceKey: null, $"Replace<{typeof(TService).Name}>");
        services.AddSingleton(instance);
        return services;
    }

    public static IServiceCollection ReplaceKeyed<TService, TImplementation>(this IServiceCollection services, object key)
        where TService : class where TImplementation : class, TService
    {
        ServiceLifetime lifetime = RemoveAll(services, typeof(TService), key, $"ReplaceKeyed<{typeof(TService).Name}>({key})");
        services.Add(new ServiceDescriptor(typeof(TService), key, typeof(TImplementation), lifetime));
        return services;
    }

    public static IServiceCollection Remove<TService>(this IServiceCollection services)
    {
        RemoveAll(services, typeof(TService), serviceKey: null, $"Remove<{typeof(TService).Name}>");
        return services;
    }

    /// <summary>Matches both <c>AddHostedService&lt;T&gt;()</c> and the factory overload, whose delegate returns T.</summary>
    public static IServiceCollection RemoveHostedService<THostedService>(this IServiceCollection services)
        where THostedService : IHostedService
    {
        RemoveWhere(services, $"RemoveHostedService<{typeof(THostedService).Name}>", IsHostedService<THostedService>);
        return services;
    }

    // Only a fixture can start a deferred service, so this is reached through TestServices.
    internal static IServiceCollection DeferHostedService<THostedService>(this IServiceCollection services)
        where THostedService : class, IHostedService
    {
        ServiceDescriptor? registration = services.LastOrDefault(IsHostedService<THostedService>);
        RemoveWhere(services, $"DeferHostedService<{typeof(THostedService).Name}>", IsHostedService<THostedService>);

        // The app's own registration moves under a private key, so the container still creates and disposes the service,
        // and a factory that hands out the app's singleton still hands out that one.
        const string key = "Fixtura.Deferred";
        services.Add(registration!.ImplementationFactory is { } factory
            ? ServiceDescriptor.KeyedSingleton(typeof(THostedService), key, (provider, _) => factory(provider))
            : registration.ImplementationInstance is { } instance
                ? ServiceDescriptor.KeyedSingleton(typeof(THostedService), key, instance)
                : ServiceDescriptor.KeyedSingleton(typeof(THostedService), key, typeof(THostedService)));
        services.AddSingleton(provider =>
            new DeferredHostedService<THostedService>(provider.GetRequiredKeyedService<THostedService>(key)));
        services.AddHostedService(provider => provider.GetRequiredService<DeferredHostedService<THostedService>>());
        return services;
    }

    private static bool IsHostedService<THostedService>(ServiceDescriptor descriptor) =>
        descriptor.ServiceType == typeof(IHostedService) && !descriptor.IsKeyedService
        && (descriptor.ImplementationType == typeof(THostedService)
            || descriptor.ImplementationInstance is THostedService
            || descriptor.ImplementationFactory?.Method.ReturnType == typeof(THostedService));

    private static ServiceLifetime RemoveAll(IServiceCollection services, Type serviceType, object? serviceKey, string call) =>
        RemoveWhere(services, call, descriptor => descriptor.ServiceType == serviceType
            && Equals(descriptor.IsKeyedService ? descriptor.ServiceKey : null, serviceKey));

    // Returns the lifetime of the last match, the registration the container would have resolved.
    internal static ServiceLifetime RemoveWhere(IServiceCollection services, string call, Func<ServiceDescriptor, bool> match)
    {
        List<ServiceDescriptor> matches = services.Where(match).ToList();
        if (matches.Count == 0)
        {
            throw new NothingMatchedException(call);
        }

        foreach (ServiceDescriptor descriptor in matches)
        {
            services.Remove(descriptor);
        }

        return matches[^1].Lifetime;
    }
}
