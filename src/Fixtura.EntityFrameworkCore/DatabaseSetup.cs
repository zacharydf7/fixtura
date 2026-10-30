using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Fixtura;

/// <summary>Migrates, seeds and checks the app's EF Core databases around the host's start and data resets.</summary>
public static class DatabaseSetup
{
    /// <summary>
    /// Applies the context's migrations as the host starts, before any of the app's hosted services start, so a worker
    /// never runs against a schema that isn't there yet.
    /// </summary>
    public static TestHostBuilder MigrateBeforeStart<TContext>(this TestHostBuilder host) where TContext : DbContext
    {
        host.Services.Configure(services => services.Insert(0, ServiceDescriptor.Singleton<IHostedService>(provider =>
            new StartingStep(provider, async (scope, cancellationToken) =>
            {
                await using TContext db = Create<TContext>(scope);
                await db.Database.MigrateAsync(cancellationToken);
            }))));
        return host;
    }

    /// <summary>
    /// Runs <paramref name="seed"/> as the fixture's host starts, after <c>MigrateBeforeStart</c>'s migrations and before
    /// the app's hosted services start, and after every <c>ResetDataAsync</c>, and saves what it adds. For reference data
    /// the tests need. The schema must exist by then: from <c>MigrateBeforeStart</c>, or created in Program.cs before the
    /// app runs.
    /// </summary>
    public static TestHostBuilder SeedOnReset<TContext>(this TestHostBuilder host, Func<TContext, Task> seed)
        where TContext : DbContext
    {
        // A reset before the host is built has no app to seed, so the first test's data comes from a seed at start: once per
        // fixture, because a host from WithOverrides shares the fixture's database and these steps.
        int seededAtStart = 0;
        host.Services.Configure(services => services.AddSingleton<IHostedService>(provider => new StartingStep(provider,
            (scope, _) => Interlocked.Exchange(ref seededAtStart, 1) == 0 ? SeedAsync(scope, seed) : Task.CompletedTask)));
        return host.OnReset(services => SeedAsync(services, seed));
    }

    /// <inheritdoc cref="SeedOnReset{TContext}(TestHostBuilder, Func{TContext, Task})"/>
    public static TestHostBuilder SeedOnReset<TContext>(this TestHostBuilder host, Action<TContext> seed)
        where TContext : DbContext =>
        host.SeedOnReset<TContext>(db =>
        {
            seed(db);
            return Task.CompletedTask;
        });

    /// <summary>
    /// Throws when the context's model has changes that no migration covers, as EF Core's <c>HasPendingModelChanges</c>
    /// reports them, so a forgotten migration fails a test instead of a deployment. Call it on the fixture's
    /// <c>Services</c>.
    /// </summary>
    public static void AssertNoPendingModelChanges<TContext>(this IServiceProvider services) where TContext : DbContext
    {
        using IServiceScope scope = services.CreateScope();
        using TContext db = Create<TContext>(scope.ServiceProvider);
        if (db.Database.HasPendingModelChanges())
        {
            throw new InvalidOperationException($"{typeof(TContext).Name}'s model has changes that no migration covers. "
                + "Add a migration, for example with dotnet ef migrations add.");
        }
    }

    internal static async Task SeedAsync<TContext>(IServiceProvider services, Func<TContext, Task> seed)
        where TContext : DbContext
    {
        await using TContext db = Create<TContext>(services);
        await seed(db);
        await db.SaveChangesAsync();
    }

    // From the context's options, so it works however the app registered the context.
    internal static TContext Create<TContext>(IServiceProvider services) where TContext : DbContext =>
        ActivatorUtilities.CreateInstance<TContext>(services, services.GetRequiredService<DbContextOptions<TContext>>());
}

// Runs a step as the host starts. The host runs every StartingAsync before any StartAsync, so the step is done before the
// app's first worker starts.
internal sealed class StartingStep(IServiceProvider services, Func<IServiceProvider, CancellationToken, Task> step)
    : IHostedLifecycleService
{
    public async Task StartingAsync(CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = services.CreateAsyncScope();
        await step(scope.ServiceProvider, cancellationToken);
    }

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StartedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
