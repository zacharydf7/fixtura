using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Fixtura.Spike.EntityFrameworkCore;

// Every registration shape builds its options from IDbContextOptionsConfiguration<T>, so swapping only those keeps pooling.
public static class DbContextSwaps
{
    private static readonly MethodInfo ReplaceOneMethod =
        typeof(DbContextSwaps).GetMethod(nameof(ReplaceOne), BindingFlags.NonPublic | BindingFlags.Static)!;

    public static IServiceCollection ReplaceDbContext<TContext>(this IServiceCollection services,
        Action<DbContextOptionsBuilder> configure, AppDbOptions appOptions = AppDbOptions.Drop) where TContext : DbContext
    {
        ReplaceOne<TContext>(services, configure, appOptions);
        return services;
    }

    // For contexts a library keeps internal: every context registered from that assembly is replaced.
    public static IServiceCollection ReplaceDbContexts(this IServiceCollection services, Assembly assembly,
        Action<DbContextOptionsBuilder> configure, AppDbOptions appOptions = AppDbOptions.Drop)
    {
        List<Type> contextTypes = services
            .Where(descriptor => descriptor.ServiceType.IsGenericType
                && descriptor.ServiceType.GetGenericTypeDefinition() == typeof(DbContextOptions<>))
            .Select(descriptor => descriptor.ServiceType.GetGenericArguments()[0])
            .Where(contextType => contextType.Assembly == assembly)
            .Distinct()
            .ToList();
        if (contextTypes.Count == 0)
        {
            throw new NothingMatchedException($"ReplaceDbContexts({assembly.GetName().Name})");
        }

        foreach (Type contextType in contextTypes)
        {
            ReplaceOneMethod.MakeGenericMethod(contextType).Invoke(null, [services, configure, appOptions]);
        }

        return services;
    }

    public static TestServices ReplaceDbContext<TContext>(this TestServices services,
        Action<DbContextOptionsBuilder> configure, AppDbOptions appOptions = AppDbOptions.Drop) where TContext : DbContext =>
        services.Configure(collection => collection.ReplaceDbContext<TContext>(configure, appOptions));

    public static TestServices ReplaceDbContexts(this TestServices services, Assembly assembly,
        Action<DbContextOptionsBuilder> configure, AppDbOptions appOptions = AppDbOptions.Drop) =>
        services.Configure(collection => collection.ReplaceDbContexts(assembly, configure, appOptions));

    private static void ReplaceOne<TContext>(IServiceCollection services, Action<DbContextOptionsBuilder> configure,
        AppDbOptions appOptions) where TContext : DbContext
    {
        if (!services.Any(descriptor => descriptor.ServiceType == typeof(DbContextOptions<TContext>)))
        {
            throw new NothingMatchedException($"ReplaceDbContext<{typeof(TContext).Name}>");
        }

        List<ServiceDescriptor> originals = services
            .Where(descriptor => descriptor.ServiceType == typeof(IDbContextOptionsConfiguration<TContext>))
            .ToList();
        foreach (ServiceDescriptor original in originals)
        {
            services.Remove(original);
        }

        services.ConfigureDbContext<TContext>((provider, builder) =>
        {
            if (appOptions == AppDbOptions.Keep)
            {
                CopyAppOptions<TContext>(originals, provider, builder);
            }

            configure(builder);
        });
    }

    // Runs the app's own configuration on a scratch builder and copies everything except the database provider.
    private static void CopyAppOptions<TContext>(List<ServiceDescriptor> originals, IServiceProvider provider,
        DbContextOptionsBuilder target) where TContext : DbContext
    {
        // Starts from what EF has built so far, so its own settings such as the application service provider survive.
        var scratch = new DbContextOptionsBuilder<TContext>((DbContextOptions<TContext>)target.Options);
        foreach (ServiceDescriptor original in originals)
        {
            var configuration = (IDbContextOptionsConfiguration<TContext>)(original.ImplementationInstance
                ?? original.ImplementationFactory!(provider));
            try
            {
                configuration.Configure(provider, scratch);
            }
            catch (Exception exception)
            {
                throw new InvalidOperationException(
                    $"The app's own options for {typeof(TContext).Name} threw, so AppDbOptions.Keep can't reuse them. "
                    + "Use AppDbOptions.Drop and add what the test needs, such as interceptors, in the replacement.",
                    exception);
            }
        }

        foreach (IDbContextOptionsExtension extension in scratch.Options.Extensions)
        {
            if (!extension.Info.IsDatabaseProvider)
            {
                ((IDbContextOptionsBuilderInfrastructure)target).AddOrUpdateExtension(extension);
            }
        }
    }
}
