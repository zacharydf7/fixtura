namespace Fixtura.Tests.Integration.Sql;

// The resource's own skip-or-fail decision, against a server address that refuses connections, so no server is needed.
public class SqlServerGateTests
{
    private const string Unreachable = "Server=tcp:127.0.0.1,1;Integrated Security=true;TrustServerCertificate=true";

    [Fact]
    public async Task ProvisionAsync_ShouldReturnAReadableReason_WhenTheServerIsUnreachable()
    {
        await using SqlServerResource sql = UnreachableServer(required: null);

        string? reason = await sql.ProvisionAsync("fixtura_00000000");

        Assert.StartsWith("SQL Server not reachable at tcp:127.0.0.1,1", reason);
    }

    [Theory]
    [InlineData("true")]
    [InlineData("1")]
    public async Task ProvisionAsync_ShouldThrow_WhenTheServerIsUnreachableAndRequired(string required)
    {
        await using SqlServerResource sql = UnreachableServer(required);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => sql.ProvisionAsync("fixtura_00000000"));

        Assert.EndsWith("is set, so SQL Server is required.", error.Message);
    }

    [Fact]
    public async Task ProvisionAsync_ShouldSkip_WhenTheRequiredVariableHasAnotherValue()
    {
        await using SqlServerResource sql = UnreachableServer(required: "false");

        string? reason = await sql.ProvisionAsync("fixtura_00000000");

        Assert.NotNull(reason);
    }

    // SqlClient reads a connect timeout of 0 as no limit; a hang here fails the test's own timeout.
    [Fact(Timeout = 30_000)]
    public async Task ProvisionAsync_ShouldStillTimeOut_WhenTheReadyTimeoutIsUnderASecond()
    {
        await using SqlServerResource sql = UnreachableServer(required: null, TimeSpan.FromMilliseconds(500));

        string? reason = await sql.ProvisionAsync("fixtura_00000000").WaitAsync(TestContext.Current.CancellationToken);

        Assert.NotNull(reason);
    }

    [Fact]
    public async Task ProvisionAsync_ShouldRejectTheReadyTimeout_WhenItIsNotPositive()
    {
        await using SqlServerResource sql = UnreachableServer(required: null, TimeSpan.Zero);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => sql.ProvisionAsync("fixtura_00000000"));
    }

    // Unique variable names per test, because the process's environment is shared by tests running in parallel.
    private static SqlServerResource UnreachableServer(string? required, TimeSpan? readyTimeout = null)
    {
        string id = Guid.NewGuid().ToString("N");
        Environment.SetEnvironmentVariable($"FIXTURA_TEST_SERVER_{id}", Unreachable);
        Environment.SetEnvironmentVariable($"FIXTURA_TEST_REQUIRED_{id}", required);
        return new SqlServerResource(new SqlServerOptions
        {
            Source = SqlServerSource.EnvironmentVariableOrLocalDb($"FIXTURA_TEST_SERVER_{id}"),
            RequiredWhenSet = $"FIXTURA_TEST_REQUIRED_{id}",
            ReadyTimeout = readyTimeout ?? TimeSpan.FromSeconds(1)
        });
    }
}
