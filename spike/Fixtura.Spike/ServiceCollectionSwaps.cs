using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Fixtura.Spike;

// Typed swaps on a plain service collection. Each one throws when nothing matched, so a renamed type fails loudly.
public static class ServiceCollectionSwaps
{
    // Replaces every registration of TService, keeping the lifetime the app gave it.
    public static IServiceCollection Replace<TService, TImplementation>(this IServiceCollection services)
        where TService : class where TImplementation : class, TService
    {
        ServiceLifetime lifetime = RemoveAll(services, typeof(TService), serviceKey: null, $"Replace<{typeof(TService).Name}>");
        services.Add(new ServiceDescriptor(typeof(TService), typeof(TImplementation), lifetime));
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

    // Matches both AddHostedService<T>() and the factory overload, whose delegate returns T.
    public static IServiceCollection RemoveHostedService<THostedService>(this IServiceCollection services)
        where THostedService : IHostedService
    {
        RemoveWhere(services, $"RemoveHostedService<{typeof(THostedService).Name}>", descriptor =>
            descriptor.ServiceType == typeof(IHostedService) && !descriptor.IsKeyedService
            && (descriptor.ImplementationType == typeof(THostedService)
                || descriptor.ImplementationInstance is THostedService
                || descriptor.ImplementationFactory?.Method.ReturnType == typeof(THostedService)));
        return services;
    }

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
