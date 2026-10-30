using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SampleApp.Data;
using Testcontainers.MsSql;

namespace Fixtura.Tests.Integration.Sql;

// Without Docker these tests skip; with FIXTURA_SQLSERVER_REQUIRED=true, as on CI's Linux job, they fail.
public sealed class ContainerSqlFixture : TestHost<Program>
{
    protected override void Configure(TestHostBuilder host)
    {
        SqlServerResource sql = host.UseSqlServer(new SqlServerOptions
        {
            Source = SqlServerSource.Container(),
            RequiredWhenSet = "FIXTURA_SQLSERVER_REQUIRED"
        });
        host.Services.ReplaceDbContext<OrdersDbContext>(db => db.UseSqlServer(sql.Database("Orders")));
    }
}

public class SqlContainerTests(ContainerSqlFixture api) : IClassFixture<ContainerSqlFixture>
{
    [Fact]
    public async Task Container_ShouldServeTheFixturesDatabase_WhenDockerIsAvailable()
    {
        using (IServiceScope scope = api.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();
            await db.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
            db.Orders.Add(new Order { Product = "Lamp", Quantity = 1 });
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        using IServiceScope check = api.CreateScope();
        var orders = check.ServiceProvider.GetRequiredService<OrdersDbContext>();

        var connection = new SqlConnectionStringBuilder(orders.Database.GetConnectionString());
        Assert.Equal(("sa", 1), (connection.UserID, await orders.Orders.CountAsync(TestContext.Current.CancellationToken)));
    }

    // Testcontainers' default SA password is public, and the container's port is published on every interface.
    [Fact]
    public void Container_ShouldUseItsOwnSaPassword_WhenStarted()
    {
        using IServiceScope scope = api.CreateScope();
        var orders = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();

        string password = new SqlConnectionStringBuilder(orders.Database.GetConnectionString()).Password;

        Assert.NotEmpty(password);
        Assert.NotEqual(MsSqlBuilder.DefaultPassword, password);
    }
}

public class SqlContainerGateTests
{
    [Fact]
    public async Task ProvisionAsync_ShouldReturnTheReason_WhenTheContainerCannotStart()
    {
        await using var sql = new SqlServerResource(new SqlServerOptions { Source = SqlServerSource.Container("fixtura.invalid/no-such-image:1") });

        string? reason = await sql.ProvisionAsync("fixtura_00000000");

        Assert.StartsWith("Couldn't get a SQL Server from a SQL Server container (fixtura.invalid/no-such-image:1): ", reason);
    }

    // The image can't start, so a container started by mistake would fail instead of passing.
    [Fact]
    public async Task EnvironmentVariableOrContainer_ShouldUseTheVariablesServer_WhenTheVariableIsSet()
    {
        string name = $"FIXTURA_TEST_SERVER_{Guid.NewGuid():N}";
        Environment.SetEnvironmentVariable(name, "Server=ci-sql");
        SqlServerSource source = SqlServerSource.EnvironmentVariableOrContainer(name, "fixtura.invalid/no-such-image:1");

        string? server = await source.ResolveAsync();

        Assert.Equal("Server=ci-sql", server);
    }

    // The image can't start, so the source throws only if the blank variable sent it to the container.
    [Fact]
    public async Task EnvironmentVariableOrContainer_ShouldStartAContainer_WhenTheVariableIsBlank()
    {
        string name = $"FIXTURA_TEST_SERVER_{Guid.NewGuid():N}";
        Environment.SetEnvironmentVariable(name, " ");
        SqlServerSource source = SqlServerSource.EnvironmentVariableOrContainer(name, "fixtura.invalid/no-such-image:1");

        await Assert.ThrowsAnyAsync<Exception>(source.ResolveAsync);
    }
}
