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
            RequiredWhenSet = "FIXTURA_SQLSERVER_REQUIRED",
            ReadyTimeout = TimeSpan.FromSeconds(15)
        });
        string? unavailable = await sql.ProvisionAsync("fixtura_" + Guid.NewGuid().ToString("N")[..8]);
        Assert.SkipWhen(unavailable is not null, unavailable ?? "");
        var probe = new SqlConnectionStringBuilder(sql.Database("Probe"));
        string database = probe.InitialCatalog;
        await ExecuteAsync(probe, $"CREATE DATABASE [{database}]", database);

        await sql.DisposeAsync();

        Assert.Equal(0, await ExecuteAsync(probe, "SELECT COUNT(*) FROM sys.databases WHERE name = @name", database));
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
