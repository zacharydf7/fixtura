using System.Net;
using System.Security.Claims;
using Fixtura;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Bookshop.Tests;

// Snippets for the guides. They compile with the rest of the samples, so they can't drift from the API, but never run.
internal static class GuideSnippets
{
    public static void FakeClock(TestHostBuilder host, TestHost<Program> shop)
    {
        // <fake-clock>
        host.UseFakeClock(new DateTimeOffset(2026, 1, 15, 9, 0, 0, TimeSpan.Zero));

        // In a test, a month later:
        shop.Clock.Advance(TimeSpan.FromDays(30));
        // </fake-clock>
    }

    public static void PostgreSql(TestHostBuilder host)
    {
        // <postgresql>
        PostgreSqlResource sql = host.AddPostgreSql(new PostgreSqlOptions
        {
            Source = PostgreSqlSource.EnvironmentVariableOrContainer("FIXTURA_POSTGRES"),
            RequiredWhenVariableIsTrue = "FIXTURA_POSTGRES_REQUIRED",
            KeepOnReset = ["Books"]
        });
        host.Services.ReplaceDbContext<ShopDb>(db => db.UseNpgsql(sql.ConnectionString("Shop")));
        // </postgresql>
    }

    public static void Sqlite(TestHostBuilder host)
    {
        // <sqlite>
        SqliteResource sqlite = host.AddSqliteInMemory();
        host.Services.ReplaceDbContext<ShopDb>(db => db.UseSqlite(sqlite.ConnectionString("Shop")));
        // </sqlite>
    }

    public static void Migrations(TestHostBuilder host, TestHost<Program> shop)
    {
        // <migrations>
        host.MigrateBeforeStart<ShopDb>();

        // In a test:
        shop.Services.AssertNoPendingModelChanges<ShopDb>();
        // </migrations>
    }

    public static async Task Eventually(TestHost<Program> shop)
    {
        // <eventually>
        await shop.EventuallyAsync(services =>
            Assert.True(services.GetRequiredService<ShopDb>().Orders.Any(order => order.BookId == 1)));
        // </eventually>
    }

    public static async Task DeferredService(TestHostBuilder host, TestHost<Program> shop)
    {
        // <deferred-service>
        host.Services.DeferHostedService<RestockReminder>();
        host.FailOnBackgroundServiceCrash();

        // In a test, once the stock it reminds about is in place:
        RestockReminder reminder = await shop.StartHostedServiceAsync<RestockReminder>();
        // </deferred-service>
    }

    public static void ServiceSwaps(TestHostBuilder host)
    {
        // <service-swaps>
        host.Services
            .Replace<IReceiptPrinter, NullReceiptPrinter>()
            .Replace<IPriceList, FixedPriceList>(ServiceLifetime.Singleton)
            .Replace<TimeProvider>(new FixedClock(new DateTimeOffset(2026, 1, 15, 9, 0, 0, TimeSpan.Zero)))
            .ReplaceKeyed<IPaymentGateway, ApprovingGateway>("card")
            .Remove<IAuditTrail>()
            .RemoveHostedService<RestockReminder>()
            .Configure(services => services.AddSingleton<IAuditTrail, MemoryAuditTrail>());
        // </service-swaps>
    }

    public static void CollectionSwaps(IServiceCollection services)
    {
        // <collection-swaps>
        services.Replace<IReceiptPrinter, NullReceiptPrinter>().RemoveHostedService<RestockReminder>();
        // </collection-swaps>
    }

    public static void CiSource()
    {
        var options = new SqlServerOptions
        {
            // <ci-source>
            Source = SqlServerSource.EnvironmentVariableOrLocalDb("TEST_SQLSERVER"),
            RequiredWhenVariableIsTrue = "TEST_SQLSERVER_REQUIRED"
            // </ci-source>
        };
        _ = options;
    }

    public static void DbContextSwaps(TestHostBuilder host)
    {
        // <dbcontext-swaps>
        host.Services
            .ReplaceDbContext<ShopDb>(db => db.UseInMemoryDatabase(host.UniqueName("Shop")))
            .ReplaceDbContext<ArchiveDb>(db => db.UseInMemoryDatabase(host.UniqueName("Archive")), AppDbOptions.Keep);
        // </dbcontext-swaps>
    }

    public static void LibraryContexts(TestHostBuilder host)
    {
        // <library-contexts>
        host.Services.ReplaceDbContexts(typeof(ArchiveDb).Assembly, db => db.UseInMemoryDatabase(host.UniqueName("Library")));
        // </library-contexts>
    }

    public static void Settings(TestHostBuilder host)
    {
        // <settings>
        host.Settings(new
        {
            Shop = new { FreeShippingOver = 0 },
            Shipping = new { Url = "https://shipping.test" }
        });
        host.Settings("""{ "Shop": { "FreeShippingOver": 0 } }""");
        host.Options<ShopOptions>(shop => shop.FreeShippingOver = 0);
        // </settings>
    }

    public static async Task TestUsers(ShopFixture shop)
    {
        // <test-users>
        TestUser ann = TestUser.Named("ann")
            .WithRoles("Customer", "Reviewer")
            .WithClaim("tier", "gold")
            .WithId("0b7d1c2e-4a5f-4e8b-9c3d-2f6a1b8e7d40");

        HttpClient signedIn = await shop.CreateClientAsync(ann);
        HttpClient anonymous = await shop.CreateClientAsync();
        // </test-users>
        signedIn.Dispose();
        anonymous.Dispose();
    }

    public static void CookieSignIn(TestHostBuilder host)
    {
        // <cookie-sign-in>
        host.Users.SignIn = UserSignIn.AppCookie;
        host.Users.SeedWith<IdentityUserSeeder<IdentityUser, IdentityRole>>();
        // </cookie-sign-in>
    }

    public static void HttpFakes(TestHostBuilder host)
    {
        // <http-fakes>
        HttpFake rates = host.Http.Fake("rates")
            .Respond(HttpMethod.Get, "/today/EUR", HttpStatusCode.OK, new { Value = 1.1 })
            .Respond(HttpMethod.Get, "/today?currency=USD", HttpStatusCode.OK, new { Value = 1.0 })
            .Respond(request => new HttpResponseMessage(HttpStatusCode.NotFound));
        // </http-fakes>

        // <recorded-requests>
        FakeHttpRequest sent = rates.Requests[^1];
        string? apiKey = sent.Headers.GetValueOrDefault("X-Api-Key");
        QuoteBody? body = sent.Json<QuoteBody>();
        // </recorded-requests>
        _ = (apiKey, body);
    }

    public static void Logs(TestHostBuilder host)
    {
        // <logs>
        host.LogToTestOutput();
        host.Settings("""{ "Logging": { "LogLevel": { "Microsoft.EntityFrameworkCore.Database.Command": "Information" } } }""");
        // </logs>

        // <logs-warnings>
        host.LogToTestOutput(LogLevel.Warning);
        // </logs-warnings>
    }

    public static void CustomSource()
    {
        // <custom-source>
        var options = new SqlServerOptions
        {
            Source = SqlServerSource.From("the shared test server", async cancellationToken =>
                await File.ReadAllTextAsync("test-server.txt", cancellationToken)),
            RequiredWhenVariableIsTrue = "CI"
        };
        // </custom-source>
        _ = options;
    }

    public static void Reset(TestHostBuilder host)
    {
        // <on-reset>
        host.OnReset(services => services.GetRequiredService<MemoryAuditTrail>().Clear());
        host.AddResource(new QueueResource());
        // </on-reset>
    }

    public static async Task KestrelAndOverrides(TestHostBuilder host, ShopFixture shop)
    {
        // <kestrel>
        host.Server = ServerKind.Kestrel;
        // </kestrel>

        // <overrides>
        await using TestHost<Program> pricey = shop.WithOverrides(overrides =>
            overrides.Settings(new { Shop = new { FreeShippingOver = 1000 } }));
        HttpClient client = await pricey.CreateClientAsync(TestUser.Named("ann").WithRoles("Customer"));
        // </overrides>
        client.Dispose();
    }
}

// <custom-seeder>
public sealed class DirectorySeeder : ITestUserSeeder
{
    public Task<IReadOnlyList<Claim>> SeedAsync(TestUserSeedContext context)
    {
        TestUser user = context.User;
        var directory = context.Services.GetRequiredService<StaffDirectory>();
        string id = directory.AddOrUpdate(user.Name, user.Roles);
        IReadOnlyList<Claim> claims =
            [new(ClaimTypes.NameIdentifier, id), new(ClaimTypes.Name, user.Name), .. user.Roles.Select(role => new Claim(ClaimTypes.Role, role))];
        return Task.FromResult(claims);
    }
}
// </custom-seeder>

// <custom-resource>
public sealed class QueueResource : ITestResource
{
    private string? _queue;

    public async Task<TestResourceStatus> ProvisionAsync(TestResourceContext context)
    {
        if (!await MessageBroker.IsReachableAsync(context.CancellationToken))
        {
            return TestResourceStatus.Unavailable("No message broker is running on localhost.");
        }

        _queue = await MessageBroker.CreateQueueAsync($"{context.NamePrefix}_orders", context.CancellationToken);
        return TestResourceStatus.Ready;
    }

    public async Task ResetAsync(CancellationToken cancellationToken)
    {
        if (_queue is not null)
        {
            await MessageBroker.PurgeAsync(_queue, cancellationToken);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_queue is not null)
        {
            await MessageBroker.DeleteQueueAsync(_queue);
        }
    }
}
// </custom-resource>

// Stand-ins the snippets refer to.
public interface IReceiptPrinter;

public sealed class NullReceiptPrinter : IReceiptPrinter;

public interface IPriceList;

public sealed class FixedPriceList : IPriceList;

public interface IPaymentGateway;

public sealed class ApprovingGateway : IPaymentGateway;

public interface IAuditTrail;

public sealed class MemoryAuditTrail : IAuditTrail
{
    public void Clear()
    {
    }
}

public sealed class ArchiveDb(DbContextOptions<ArchiveDb> options) : DbContext(options);

public sealed record QuoteBody(int BookId);

public sealed class StaffDirectory
{
    public string AddOrUpdate(string name, IReadOnlyList<string> roles) => name;
}

public static class MessageBroker
{
    public static Task<bool> IsReachableAsync(CancellationToken cancellationToken) => Task.FromResult(false);

    public static Task<string> CreateQueueAsync(string name, CancellationToken cancellationToken) => Task.FromResult(name);

    public static Task PurgeAsync(string queue, CancellationToken cancellationToken) => Task.CompletedTask;

    public static Task DeleteQueueAsync(string queue) => Task.CompletedTask;
}

public sealed class FixedClock(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
}
