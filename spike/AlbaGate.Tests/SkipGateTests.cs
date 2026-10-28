using System.Net;
using Alba;
using Microsoft.Data.SqlClient;

namespace AlbaGate.Tests;

// The best skip gate an IAlbaExtension can offer: a BeforeEach hook that skips from inside Scenario.
public sealed class SqlSkipGate(string connectionString) : IAlbaExtension
{
    public async Task Start(IAlbaHost host)
    {
        if (await CanConnectAsync())
        {
            return;
        }

        host.BeforeEachAsync(_ =>
        {
            Assert.Skip("SQL Server is not reachable.");
            return Task.CompletedTask;
        });
    }

    private async Task<bool> CanConnectAsync()
    {
        try
        {
            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync();
            return true;
        }
        catch (SqlException)
        {
            return false;
        }
    }

    public void Dispose()
    {
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

public sealed class UnreachableSqlFixture : AlbaApiFixture
{
    protected override IAlbaExtension[] MoreExtensions =>
        [new SqlSkipGate("Server=tcp:127.0.0.1,1;Connect Timeout=1;TrustServerCertificate=true")];
}

public class SkipGateTests(UnreachableSqlFixture api) : IClassFixture<UnreachableSqlFixture>
{
    [Fact]
    public async Task Scenario_ShouldSkip_WhenSqlServerIsUnreachable()
    {
        await api.Host.Scenario(scenario => scenario.Get.Url("/carriers"));

        Assert.Fail("The gate should have skipped this test.");
    }

    [Fact]
    public async Task PlainHttpClient_ShouldNotSkip_WhenSqlServerIsUnreachable()
    {
        HttpResponseMessage response = await api.Host.Server.CreateClient().GetAsync("/carriers", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
