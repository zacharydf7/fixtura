using Fixtura.Tests.Integration.Composition;
using Fixtura.Tests.Integration.Data.Notes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SampleApp.Data;

namespace Fixtura.Tests.Integration.Data;

// Two contexts moved to SQLite; the app's other contexts still point at a server that doesn't exist, so the host only
// starts if CreateDatabasesOnStart leaves them alone.
public sealed class CreatedSqliteDatabasesFixture : TestHost<Program>
{
    protected override void Configure(TestHostBuilder host)
    {
        SqliteResource sqlite = host.AddSqliteInMemory();
        host.Services
            .ReplaceDbContext<OrdersDbContext>(db => db.UseSqlite(sqlite.ConnectionString("Orders")))
            .ReplaceDbContext<CatalogDbContext>(db => db.UseSqlite(sqlite.ConnectionString("Catalog")));
        host.CreateDatabasesOnStart();
        host.Use<FakeBus>().Use<QuietBackground>();
    }
}

// Two contexts on one database, which EnsureCreated alone would give only the first context's tables.
public sealed class SharedSqliteDatabaseFixture : TestHost<Program>
{
    protected override void Configure(TestHostBuilder host)
    {
        SqliteResource sqlite = host.AddSqliteInMemory();
        host.Services
            .ReplaceDbContext<OrdersDbContext>(db => db.UseSqlite(sqlite.ConnectionString("Shared")))
            .ReplaceDbContext<CatalogDbContext>(db => db.UseSqlite(sqlite.ConnectionString("Shared")));
        host.CreateDatabasesOnStart();
        host.Use<FakeBus>().Use<QuietBackground>();
    }
}

// A context that has migrations, which CreateDatabasesOnStart applies instead of creating the schema from the model.
public sealed class CreatedNotesFixture : TestHost<Program>
{
    protected override void Configure(TestHostBuilder host)
    {
        SqlServerResource sql = host.AddSqlServer(new SqlServerOptions
        {
            Source = SqlServerSource.EnvironmentVariableOrLocalDb("FIXTURA_SQLSERVER"),
            RequiredWhenVariableIsTrue = "FIXTURA_SQLSERVER_REQUIRED",
            ReadyTimeout = TimeSpan.FromSeconds(15)
        });
        host.Services
            .Configure(services => services.AddDbContext<NotesDb>(db => db.UseSqlServer("Server=unused")))
            .ReplaceDbContext<NotesDb>(db => db.UseSqlServer(sql.ConnectionString("Notes")));
        host.CreateDatabasesOnStart();
    }
}

public class CreateDatabasesTests(CreatedSqliteDatabasesFixture api) : IClassFixture<CreatedSqliteDatabasesFixture>
{
    [Fact]
    public async Task CreateDatabasesOnStart_ShouldCreateEachMovedContextsSchema_WhenTheHostStarts()
    {
        using IServiceScope scope = api.CreateScope();

        int orders = await scope.ServiceProvider.GetRequiredService<OrdersDbContext>().Orders
            .CountAsync(TestContext.Current.CancellationToken);
        int products = await scope.ServiceProvider.GetRequiredService<CatalogDbContext>().Products
            .CountAsync(TestContext.Current.CancellationToken);

        Assert.Equal((0, 0), (orders, products));
    }
}

public class SharedDatabaseTests(SharedSqliteDatabaseFixture api) : IClassFixture<SharedSqliteDatabaseFixture>
{
    [Fact]
    public async Task CreateDatabasesOnStart_ShouldCreateEachContextsTables_WhenTheyShareADatabase()
    {
        using IServiceScope scope = api.CreateScope();

        int orders = await scope.ServiceProvider.GetRequiredService<OrdersDbContext>().Orders
            .CountAsync(TestContext.Current.CancellationToken);
        int products = await scope.ServiceProvider.GetRequiredService<CatalogDbContext>().Products
            .CountAsync(TestContext.Current.CancellationToken);

        Assert.Equal((0, 0), (orders, products));
    }

    // An override host shares the fixture's database, which by then has every table, so its start must leave them be.
    [Fact]
    public async Task CreateDatabasesOnStart_ShouldKeepTheSchema_WhenTheDatabaseAlreadyHasTables()
    {
        _ = api.Services;
        await using TestHost<Program> overridden = api.WithOverrides(_ => { });

        Exception? error = Record.Exception(() => overridden.Services);

        Assert.Null(error);
    }
}

public class CreateMigratedDatabasesTests(CreatedNotesFixture api) : IClassFixture<CreatedNotesFixture>
{
    [Fact]
    public async Task CreateDatabasesOnStart_ShouldApplyTheMigrations_WhenTheContextHasSome()
    {
        using IServiceScope scope = api.CreateScope();

        IEnumerable<string> applied = await scope.ServiceProvider.GetRequiredService<NotesDb>().Database
            .GetAppliedMigrationsAsync(TestContext.Current.CancellationToken);

        Assert.NotEmpty(applied);
    }
}
