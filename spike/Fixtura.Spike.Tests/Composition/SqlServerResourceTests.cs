using Fixtura.Spike.SqlServer;
using Microsoft.Data.SqlClient;

namespace Fixtura.Spike.Tests.Composition;

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
        await ExecuteAsync(probe, $"CREATE DATABASE [{database}]");

        await sql.DisposeAsync();

        Assert.Equal(0, await ExecuteAsync(probe, $"SELECT COUNT(*) FROM sys.databases WHERE name = '{database}'"));
    }

    private static async Task<int> ExecuteAsync(SqlConnectionStringBuilder server, string sql)
    {
        await using var connection = new SqlConnection(new SqlConnectionStringBuilder(server.ConnectionString) { InitialCatalog = "master" }.ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = new SqlCommand(sql, connection);
        return Convert.ToInt32(await command.ExecuteScalarAsync(TestContext.Current.CancellationToken));
    }
}
