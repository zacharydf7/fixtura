using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Fixtura;

/// <summary>
/// Swaps the database an app's DbContexts use. Only the options configuration is replaced, so every registration shape
/// (plain, pooled, factory, pooled factory) keeps the app's lifetimes and pooling.
/// </summary>
public static class DbContextSwaps
{
    private static readonly MethodInfo ReplaceOneMethod =
        typeof(DbContextSwaps).GetMethod(nameof(ReplaceOne), BindingFlags.NonPublic | BindingFlags.Static)!;

    /// <summary>Points the context at the database <paramref name="configure"/> sets up.</summary>
    /// <param name="appOptions">Whether the app's own options, such as interceptors, still apply.</param>
    public static IServiceCollection ReplaceDbContext<TContext>(this IServiceCollection services,
        Action<DbContextOptionsBuilder> configure, AppDbOptions appOptions = AppDbOptions.Drop) where TContext : DbContext
    {
        ReplaceOne<TContext>(services, configure, appOptions);
        return services;
    }

    /// <summary>
    /// Points every registered context whose type is declared in <paramref name="assembly"/> at the database
    /// <paramref name="configure"/> sets up, which reaches contexts a library keeps internal.
    /// </summary>
    /// <param name="appOptions">Whether the app's own options, such as interceptors, still apply.</param>
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
            ReplaceOneMethod.MakeGenericMethod(contextType)
                .Invoke(null, BindingFlags.DoNotWrapExceptions, null, [services, configure, appOptions], null);
        }

        return services;
    }

    /// <inheritdoc cref="ReplaceDbContext{TContext}(IServiceCollection, Action{DbContextOptionsBuilder}, AppDbOptions)"/>
    public static TestServices ReplaceDbContext<TContext>(this TestServices services,
        Action<DbContextOptionsBuilder> configure, AppDbOptions appOptions = AppDbOptions.Drop) where TContext : DbContext =>
        services.Configure(collection => collection.ReplaceDbContext<TContext>(configure, appOptions));

    /// <inheritdoc cref="ReplaceDbContexts(IServiceCollection, Assembly, Action{DbContextOptionsBuilder}, AppDbOptions)"/>
    public static TestServices ReplaceDbContexts(this TestServices services, Assembly assembly,
        Action<DbContextOptionsBuilder> configure, AppDbOptions appOptions = AppDbOptions.Drop) =>
        services.Configure(collection => collection.ReplaceDbContexts(assembly, configure, appOptions));

    private static void ReplaceOne<TContext>(IServiceCollection services, Action<DbContextOptionsBuilder> configure,
        AppDbOptions appOptions) where TContext : DbContext
    {
        ServiceDescriptor options = services.LastOrDefault(descriptor => descriptor.ServiceType == typeof(DbContextOptions<TContext>))
            ?? throw new NothingMatchedException($"ReplaceDbContext<{typeof(TContext).Name}>");

        // Options the app registers itself never read the configurations swapped below, so the app's database would stay.
        // EF Core's own factories do, wherever in EF Core they're declared.
        Assembly? factoryAssembly = options.ImplementationFactory?.Method.DeclaringType?.Assembly;
        if (factoryAssembly != typeof(EntityFrameworkServiceCollectionExtensions).Assembly)
        {
            throw new InvalidOperationException(
                $"ReplaceDbContext<{typeof(TContext).Name}> can't reach this context: its options aren't registered through "
                + "AddDbContext, AddDbContextPool, AddDbContextFactory or AddPooledDbContextFactory. Replace the "
                + $"DbContextOptions<{typeof(TContext).Name}> registration with host.Services.Configure instead.");
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
            IDbContextOptionsExtension? appProvider = appOptions == AppDbOptions.Keep
                ? CopyAppOptions<TContext>(originals, provider, builder)
                : null;
            configure(builder);

            if (appProvider is RelationalOptionsExtension app
                && builder.Options.Extensions.SingleOrDefault(extension => extension.Info.IsDatabaseProvider)
                    is RelationalOptionsExtension replacement
                && app.GetType() == replacement.GetType())
            {
                var infrastructure = (IDbContextOptionsBuilderInfrastructure)builder;
                infrastructure.AddOrUpdateExtension(WithAppSettings(replacement, app));
            }
        });
    }

    // The app's migrations, command timeout and retries, where the replacement left them unset. Nothing else crosses over:
    // the rest of a provider's options, such as its connection, data source or engine, belongs to the test's database.
    private static RelationalOptionsExtension WithAppSettings(RelationalOptionsExtension test,
        RelationalOptionsExtension app)
    {
        if (test.MigrationsAssembly is null && test.MigrationsAssemblyObject is null)
        {
            test = app.MigrationsAssemblyObject is null
                ? test.WithMigrationsAssembly(app.MigrationsAssembly)
                : test.WithMigrationsAssembly(app.MigrationsAssemblyObject);
        }

        return test
            .WithMigrationsHistoryTableName(test.MigrationsHistoryTableName ?? app.MigrationsHistoryTableName)
            .WithMigrationsHistoryTableSchema(test.MigrationsHistoryTableSchema ?? app.MigrationsHistoryTableSchema)
            .WithCommandTimeout(test.CommandTimeout ?? app.CommandTimeout)
            .WithExecutionStrategyFactory(test.ExecutionStrategyFactory ?? app.ExecutionStrategyFactory);
    }

    // Runs the app's own configuration on a scratch builder and copies everything except the database provider, which it
    // returns.
    private static IDbContextOptionsExtension? CopyAppOptions<TContext>(List<ServiceDescriptor> originals,
        IServiceProvider provider, DbContextOptionsBuilder target) where TContext : DbContext
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

        return scratch.Options.Extensions.SingleOrDefault(extension => extension.Info.IsDatabaseProvider);
    }
}
