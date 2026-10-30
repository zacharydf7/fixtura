using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Fixtura;

/// <summary>Migrates, seeds and checks the app's EF Core databases around the host's start and data resets.</summary>
public static class DatabaseSetup
{
    // One start-up step per fixture, so migrations finish before seeds even when the app starts services concurrently.
    private static readonly ConditionalWeakTable<TestHostBuilder, StartupSteps> Startups = new();

    /// <summary>
    /// Applies the context's migrations as the host starts, before any of the app's hosted services start, so a worker
    /// never runs against a schema that isn't there yet.
    /// </summary>
    public static TestHostBuilder MigrateBeforeStart<TContext>(this TestHostBuilder host) where TContext : DbContext
    {
        StartupFor(host).Migrations.Add(async (scope, cancellationToken) =>
        {
            await using TContext db = Create<TContext>(scope);
            await db.Database.MigrateAsync(cancellationToken);
        });
        return host;
    }

    /// <summary>
    /// Runs <paramref name="seed"/> as the fixture's host starts, after <c>MigrateBeforeStart</c>'s migrations and before
    /// the app's hosted services start, and after every <c>ResetDataAsync</c>, and saves what it adds. For reference data
    /// the tests need. The schema must exist by then: from <c>MigrateBeforeStart</c>, or created in Program.cs before the
    /// app runs. Call it in the fixture's <c>Configure</c>, not in <c>WithOverrides</c>.
    /// </summary>
    public static TestHostBuilder SeedOnReset<TContext>(this TestHostBuilder host, Func<TContext, Task> seed)
        where TContext : DbContext
    {
        // A reset before a host is built has no app to seed, so the seed is owed until a host starts. A host from
        // WithOverrides shares the fixture's database, this flag and these steps, so only one of them pays it.
        SeedOwed owed = host.AddResource(new SeedOwed());
        StartupFor(host).Seeds.Add((scope, _) => owed.Settle() ? SeedAsync(scope, seed) : Task.CompletedTask);
        return host.OnReset(services =>
        {
            owed.Settle();
            return SeedAsync(services, seed);
        });
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

    // The first call registers the step that runs them; a host from WithOverrides copies that registration with the rest.
    private static StartupSteps StartupFor(TestHostBuilder host) =>
        Startups.GetValue(host, builder =>
        {
            var steps = new StartupSteps();
            builder.Services.Configure(services => services.Insert(0,
                ServiceDescriptor.Singleton<IHostedService>(provider => new StartingStep(provider, steps.RunAsync))));
            return steps;
        });
}

internal sealed class StartupSteps
{
    public List<Func<IServiceProvider, CancellationToken, Task>> Migrations { get; } = [];

    public List<Func<IServiceProvider, CancellationToken, Task>> Seeds { get; } = [];

    public async Task RunAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        foreach (Func<IServiceProvider, CancellationToken, Task> step in Migrations.Concat(Seeds))
        {
            await using AsyncServiceScope scope = services.CreateAsyncScope();
            await step(scope.ServiceProvider, cancellationToken);
        }
    }
}

// Every reset of the fixture's data, even one before any host is built, owes a seed until a seed settles it.
internal sealed class SeedOwed : ITestResource
{
    private int _owed = 1;

    public bool Settle() => Interlocked.Exchange(ref _owed, 0) == 1;

    public Task<string?> ProvisionAsync(string namePrefix) => Task.FromResult<string?>(null);

    public Task ResetAsync()
    {
        Volatile.Write(ref _owed, 1);
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

// Runs a step as the host starts. The host runs every StartingAsync before any StartAsync, so the step is done before the
// app's first worker starts.
internal sealed class StartingStep(IServiceProvider services, Func<IServiceProvider, CancellationToken, Task> step)
    : IHostedLifecycleService
{
    public Task StartingAsync(CancellationToken cancellationToken) => step(services, cancellationToken);

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StartedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
