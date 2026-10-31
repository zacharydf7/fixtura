using Fixtura.Tests.Integration.Composition;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SampleApp.Data;

namespace Fixtura.Tests.Integration.Sql;

public sealed class PooledSqlFixture : TestHost<Program>
{
    protected override void Configure(TestHostBuilder host) =>
        host.Use(new SampleSqlDatabases(reuseDatabases: true)).Use<FakeBus>().Use<QuietBackground>();
}

// The same options in another fixture type, whose app setup may differ, so it never takes the other type's databases.
public sealed class OtherPooledSqlFixture : TestHost<Program>
{
    protected override void Configure(TestHostBuilder host) =>
        host.Use(new SampleSqlDatabases(reuseDatabases: true)).Use<FakeBus>().Use<QuietBackground>();
}

// Each test creates and disposes its own fixtures, one after another, as test classes sharing a fixture type would.
public class SqlPoolTests
{
    [Fact]
    public async Task ReuseDatabases_ShouldHandTheResetDatabasesOn_WhenTheNextFixtureOfTheTypeStarts()
    {
        var first = new PooledSqlFixture();
        await first.InitializeAsync();
        string database = await AddOrderAsync(first);
        await first.DisposeAsync();

        await using var second = new PooledSqlFixture();
        await second.InitializeAsync();

        using IServiceScope scope = second.CreateScope();
        var orders = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();
        Assert.Equal(database, orders.Database.GetDbConnection().Database);
        Assert.False(await orders.Orders.AnyAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ReuseDatabases_ShouldKeepTheDatabasesFromAnotherType_WhenADifferentFixtureStarts()
    {
        var first = new PooledSqlFixture();
        await first.InitializeAsync();
        string database = await AddOrderAsync(first);
        await first.DisposeAsync();

        await using var other = new OtherPooledSqlFixture();
        await other.InitializeAsync();

        using IServiceScope scope = other.CreateScope();
        var orders = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();
        Assert.NotEqual(database, orders.Database.GetDbConnection().Database);
    }

    // A kept name that matches no table fails the reset, so these databases mustn't be handed on half reset.
    [Fact]
    public async Task DisposeAsync_ShouldDropTheDatabases_WhenReuseDatabasesIsSetAndTheResetFails()
    {
        var sql = new SqlServerResource(new SqlServerOptions
        {
            Source = SqlServerSource.EnvironmentVariableOrLocalDb("FIXTURA_SQLSERVER"),
            RequiredWhenVariableIsTrue = "FIXTURA_SQLSERVER_REQUIRED",
            ReadyTimeout = TimeSpan.FromSeconds(15),
            ReuseDatabases = true,
            KeepOnReset = ["NoSuchTable"]
        });
        var context = new TestResourceContext("fixtura_" + Guid.NewGuid().ToString("N")[..8]) { FixtureType = typeof(SqlPoolTests) };
        string? unavailable = (await sql.ProvisionAsync(context)).UnavailableReason;
        Assert.SkipWhen(unavailable is not null, unavailable ?? "");
        var probe = new SqlConnectionStringBuilder(sql.ConnectionString("Probe"));
        await SqlProbe.ExecuteAsync(probe, $"CREATE DATABASE [{probe.InitialCatalog}]", probe.InitialCatalog);

        await sql.DisposeAsync();

        Assert.Equal(0, await SqlProbe.ExecuteAsync(probe, "SELECT COUNT(*) FROM sys.databases WHERE name = @name",
            probe.InitialCatalog));
    }

    [Fact]
    public async Task DropPooledAsync_ShouldDropTheHandedBackDatabases_WhenTheyWaitInThePool()
    {
        var fixture = new PooledSqlFixture();
        await fixture.InitializeAsync();
        string database = await AddOrderAsync(fixture);
        string server = fixture.Resource<SqlServerResource>().ConnectionString("master");
        await fixture.DisposeAsync();

        await SqlServerResource.DropPooledAsync();

        Assert.Equal(0, await SqlProbe.ExecuteAsync(new SqlConnectionStringBuilder(server),
            "SELECT COUNT(*) FROM sys.databases WHERE name = @name", database));
    }

    private static async Task<string> AddOrderAsync(TestHost<Program> fixture)
    {
        using IServiceScope scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();
        db.Orders.Add(new Order { Product = "Lamp", Quantity = 1 });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return db.Database.GetDbConnection().Database;
    }
}
