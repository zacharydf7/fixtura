using System.Net.Http.Json;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SampleApp.Data;
using SampleApp.Inventory;

namespace Fixtura.Tests.Integration.Sql;

// No test here calls a skip helper: without a server they skip, and with FIXTURA_SQLSERVER_REQUIRED=true they fail.
public class SqlGateTests(ApiSqlFixture api) : IClassFixture<ApiSqlFixture>
{
    [Fact]
    public async Task Orders_ShouldRoundTripThroughTheTestDatabase_WhenThePlainContextIsOnSqlServer()
    {
        using (IServiceScope scope = api.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();
            db.Orders.Add(new Order { Product = "Lamp", Quantity = 2 });
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        using IServiceScope check = api.CreateScope();
        var orders = check.ServiceProvider.GetRequiredService<OrdersDbContext>();

        Assert.StartsWith("fixtura_", orders.Database.GetDbConnection().Database);
        Assert.True(await orders.Orders.AnyAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Catalog_ShouldSaveAProduct_WhenThePooledContextIsOnSqlServer()
    {
        using IServiceScope scope = api.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();

        db.Products.Add(new Product { Name = "Lamp" });
        int saved = await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, saved);
    }

    [Fact]
    public async Task Reports_ShouldSaveARun_WhenTheFactoryContextIsOnSqlServer()
    {
        await using ReportsDbContext db = await api.Services.GetRequiredService<IDbContextFactory<ReportsDbContext>>()
            .CreateDbContextAsync(TestContext.Current.CancellationToken);

        db.Runs.Add(new ReportRun { RanAt = DateTimeOffset.UtcNow });
        int saved = await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, saved);
    }

    [Fact]
    public async Task Analytics_ShouldSaveAPageView_WhenThePooledFactoryContextIsOnSqlServer()
    {
        await using AnalyticsDbContext db = await api.Services.GetRequiredService<IDbContextFactory<AnalyticsDbContext>>()
            .CreateDbContextAsync(TestContext.Current.CancellationToken);

        db.PageViews.Add(new PageView { Path = "/" });
        int saved = await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, saved);
    }

    [Fact]
    public async Task Inventory_ShouldBeEmpty_WhenTheInternalContextIsOnSqlServer()
    {
        using IServiceScope scope = api.CreateScope();

        int count = await scope.ServiceProvider.GetRequiredService<IInventory>().CountAsync();

        Assert.Equal(0, count);
    }
}

public class SqlAppResetTests(ApiSqlFixture api) : IClassFixture<ApiSqlFixture>
{
    [Fact]
    public async Task ResetDataAsync_ShouldEmptyTheAppsTables_WhenTheAppCreatedThem()
    {
        using (IServiceScope scope = api.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();
            db.Orders.Add(new Order { Product = "Lamp", Quantity = 2 });
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await api.ResetDataAsync();

        using IServiceScope check = api.CreateScope();
        Assert.False(await check.ServiceProvider.GetRequiredService<OrdersDbContext>().Orders.AnyAsync(TestContext.Current.CancellationToken));
    }
}

[CollectionDefinition(Name)]
public class ApiWorkerSqlCollection : ICollectionFixture<ApiWorkerSqlFixture>
{
    public const string Name = "api-worker-sql";
}

[Collection(ApiWorkerSqlCollection.Name)]
public class SqlCollectionFixtureTests(ApiWorkerSqlFixture api)
{
    [Fact]
    public async Task Inventory_ShouldUseTheTestDatabase_WhenSharedAsACollectionFixture()
    {
        using IServiceScope scope = api.CreateScope();

        int count = await scope.ServiceProvider.GetRequiredService<IInventory>().CountAsync();

        Assert.Equal(0, count);
    }
}

public class SqlPartnerTests(ApiWorkerPartnerSqlFixture api) : IClassFixture<ApiWorkerPartnerSqlFixture>
{
    [Fact]
    public async Task Carriers_ShouldIncludeThePartner_WhenTheShapeAddsAnExternalClient()
    {
        HttpClient client = await api.CreateClientAsync();

        string[]? carriers = await client.GetFromJsonAsync<string[]>("/carriers", TestContext.Current.CancellationToken);

        Assert.Equal(["Ground", "Air", "Freight", "Partner"], carriers!);
    }
}

public class SqlServerResourceTests
{
    [Fact]
    public async Task DisposeAsync_ShouldDropTheFixturesDatabases_WhenItCreatedSome()
    {
        var sql = new SqlServerResource(new SqlServerOptions
        {
            Source = SqlServerSource.EnvironmentVariableOrLocalDb("FIXTURA_SQLSERVER"),
            RequiredWhenVariableIsTrue = "FIXTURA_SQLSERVER_REQUIRED",
            ReadyTimeout = TimeSpan.FromSeconds(15)
        });
        var context = new TestResourceContext("fixtura_" + Guid.NewGuid().ToString("N")[..8]);
        string? unavailable = (await sql.ProvisionAsync(context)).UnavailableReason;
        Assert.SkipWhen(unavailable is not null, unavailable ?? "");
        var probe = new SqlConnectionStringBuilder(sql.ConnectionString("Probe"));
        string database = probe.InitialCatalog;
        await ExecuteAsync(probe, $"CREATE DATABASE [{database}]", database);

        await sql.DisposeAsync();

        Assert.Equal(0, await ExecuteAsync(probe, "SELECT COUNT(*) FROM sys.databases WHERE name = @name", database));
    }

    // Leftovers get their own name stem here, so a sweep with no age limit can't drop a parallel fixture's databases.
    [Fact]
    public async Task SweepLeftoversAsync_ShouldDropOnlyTheKitsNameShape_WhenTheLeftoversAreOldEnough()
    {
        SqlConnectionStringBuilder server = await ServerOrSkipAsync();
        string leftover = $"sweep_{Guid.NewGuid().ToString("N")[..8]}_Orders";
        string other = $"sweep_other_{Guid.NewGuid().ToString("N")[..8]}";
        await ExecuteAsync(server, $"CREATE DATABASE [{leftover}]", leftover);
        await ExecuteAsync(server, $"CREATE DATABASE [{other}]", other);

        await SqlServerResource.SweepLeftoversAsync(server.ConnectionString, SqlServerResource.LeftoverPattern("sweep_00000000"),
            olderThan: TimeSpan.Zero);

        int leftovers = await ExecuteAsync(server, "SELECT COUNT(*) FROM sys.databases WHERE name = @name", leftover);
        int others = await ExecuteAsync(server, "SELECT COUNT(*) FROM sys.databases WHERE name = @name", other);
        await ExecuteAsync(server, $"DROP DATABASE [{other}]", other);
        Assert.Equal((0, 1), (leftovers, others));
    }

    [Fact]
    public async Task SweepLeftoversAsync_ShouldKeepALeftover_WhenItIsYoungerThanTheAge()
    {
        SqlConnectionStringBuilder server = await ServerOrSkipAsync();
        string leftover = $"sweep_{Guid.NewGuid().ToString("N")[..8]}_Orders";
        await ExecuteAsync(server, $"CREATE DATABASE [{leftover}]", leftover);

        await SqlServerResource.SweepLeftoversAsync(server.ConnectionString, SqlServerResource.LeftoverPattern("sweep_00000000"),
            olderThan: TimeSpan.FromHours(1));

        int leftovers = await ExecuteAsync(server, "SELECT COUNT(*) FROM sys.databases WHERE name = @name", leftover);
        await ExecuteAsync(server, $"DROP DATABASE [{leftover}]", leftover);
        Assert.Equal(1, leftovers);
    }

    // A run paused in a debugger, or still going on a shared server, is connected to its databases.
    [Fact]
    public async Task SweepLeftoversAsync_ShouldKeepALeftover_WhenSomeoneIsConnectedToIt()
    {
        SqlConnectionStringBuilder server = await ServerOrSkipAsync();
        string leftover = $"sweep_{Guid.NewGuid().ToString("N")[..8]}_Orders";
        await ExecuteAsync(server, $"CREATE DATABASE [{leftover}]", leftover);
        var inUse = new SqlConnectionStringBuilder(server.ConnectionString) { InitialCatalog = leftover, Pooling = false };
        await using (var connection = new SqlConnection(inUse.ConnectionString))
        {
            await connection.OpenAsync(TestContext.Current.CancellationToken);

            await SqlServerResource.SweepLeftoversAsync(server.ConnectionString, SqlServerResource.LeftoverPattern("sweep_00000000"),
                olderThan: TimeSpan.Zero);
        }

        int leftovers = await ExecuteAsync(server, "SELECT COUNT(*) FROM sys.databases WHERE name = @name", leftover);
        await ExecuteAsync(server, $"DROP DATABASE [{leftover}]", leftover);
        Assert.Equal(1, leftovers);
    }

    private static async Task<SqlConnectionStringBuilder> ServerOrSkipAsync()
    {
        await using var sql = new SqlServerResource(new SqlServerOptions
        {
            Source = SqlServerSource.EnvironmentVariableOrLocalDb("FIXTURA_SQLSERVER"),
            RequiredWhenVariableIsTrue = "FIXTURA_SQLSERVER_REQUIRED",
            ReadyTimeout = TimeSpan.FromSeconds(15)
        });
        var context = new TestResourceContext("fixtura_" + Guid.NewGuid().ToString("N")[..8]);
        string? unavailable = (await sql.ProvisionAsync(context)).UnavailableReason;
        Assert.SkipWhen(unavailable is not null, unavailable ?? "");
        return new SqlConnectionStringBuilder(sql.ConnectionString("Unused")) { InitialCatalog = "" };
    }

    private static async Task<int> ExecuteAsync(SqlConnectionStringBuilder server, string sql, string database)
    {
        var master = new SqlConnectionStringBuilder(server.ConnectionString) { InitialCatalog = "master" };
        await using var connection = new SqlConnection(master.ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@name", database);
        return Convert.ToInt32(await command.ExecuteScalarAsync(TestContext.Current.CancellationToken));
    }
}
