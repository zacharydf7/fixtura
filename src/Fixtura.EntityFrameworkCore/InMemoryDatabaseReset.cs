using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Fixtura;

/// <summary>Resets the app's EF Core in-memory databases between tests.</summary>
public static class InMemoryDatabaseReset
{
    private const string InMemoryProvider = "Microsoft.EntityFrameworkCore.InMemory";

    /// <summary>
    /// Makes <c>ResetDataAsync</c> delete and recreate every in-memory database the app's contexts use, so only data the
    /// model seeds with <c>HasData</c> is left. Contexts on other providers are left alone.
    /// </summary>
    public static TestHostBuilder RecreateInMemoryDatabasesOnReset(this TestHostBuilder host) =>
        host.OnReset(RecreateAsync);

    // Every database is deleted before any is created, because contexts can share one in-memory database by name.
    private static async Task RecreateAsync(IServiceProvider services)
    {
        await using AsyncServiceScope scope = services.CreateAsyncScope();
        var contexts = new List<DbContext>();
        try
        {
            foreach (DbContextOptions options in scope.ServiceProvider.GetServices<DbContextOptions>())
            {
                var db = (DbContext)ActivatorUtilities.CreateInstance(scope.ServiceProvider, options.ContextType, options);
                contexts.Add(db);
            }

            List<DbContext> inMemory = contexts.Where(db => db.Database.ProviderName == InMemoryProvider).ToList();
            foreach (DbContext db in inMemory)
            {
                await db.Database.EnsureDeletedAsync();
            }

            foreach (DbContext db in inMemory)
            {
                await db.Database.EnsureCreatedAsync();
            }
        }
        finally
        {
            foreach (DbContext db in contexts)
            {
                await db.DisposeAsync();
            }
        }
    }
}
