using System.Net;
using System.Net.Sockets;

namespace Fixtura.Tests.Integration.Sql;

// The resource's own skip-or-fail decision, against a server address that refuses connections, so no server is needed.
public class SqlServerGateTests
{
    private const string Unreachable = "Server=tcp:127.0.0.1,1;Integrated Security=true;TrustServerCertificate=true";

    [Fact]
    public async Task ProvisionAsync_ShouldReturnAReadableReason_WhenTheServerIsUnreachable()
    {
        await using SqlServerResource sql = UnreachableServer(required: null);

        string? reason = (await sql.ProvisionAsync(new TestResourceContext("fixtura_00000000"))).UnavailableReason;

        Assert.StartsWith("SQL Server not reachable at tcp:127.0.0.1,1", reason);
        Assert.Matches(@"The server came from the FIXTURA_TEST_SERVER_\w+ variable, or LocalDB when it isn't set\.$", reason);
    }

    [Theory]
    [InlineData("true")]
    [InlineData("1")]
    public async Task ProvisionAsync_ShouldThrow_WhenTheServerIsUnreachableAndRequired(string required)
    {
        await using SqlServerResource sql = UnreachableServer(required);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            sql.ProvisionAsync(new TestResourceContext("fixtura_00000000")));

        Assert.EndsWith("is set, so SQL Server is required.", error.Message);
    }

    [Fact]
    public async Task ProvisionAsync_ShouldSkip_WhenTheRequiredVariableHasAnotherValue()
    {
        await using SqlServerResource sql = UnreachableServer(required: "false");

        string? reason = (await sql.ProvisionAsync(new TestResourceContext("fixtura_00000000"))).UnavailableReason;

        Assert.NotNull(reason);
    }

    // SqlClient reads a connect timeout of 0 as no limit; a hang here fails the test's own timeout.
    [Fact(Timeout = 30_000)]
    public async Task ProvisionAsync_ShouldStillTimeOut_WhenTheReadyTimeoutIsUnderASecond()
    {
        await using SqlServerResource sql = UnreachableServer(required: null, TimeSpan.FromMilliseconds(500));

        var context = new TestResourceContext("fixtura_00000000");
        string? reason = (await sql.ProvisionAsync(context).WaitAsync(TestContext.Current.CancellationToken)).UnavailableReason;

        Assert.NotNull(reason);
    }

    [Fact]
    public async Task ProvisionAsync_ShouldNotCheckTheServerAgain_WhenAnEarlierFixtureFoundItUnreachable()
    {
        using var server = new SilentServer();
        await using SqlServerResource first = Resource(server.ConnectionString, required: null, TimeSpan.FromSeconds(1));
        await using SqlServerResource second = Resource(server.ConnectionString, required: null, TimeSpan.FromSeconds(1));
        await first.ProvisionAsync(new TestResourceContext("fixtura_00000000"));
        int connectionsAfterFirst = server.Connections;

        string? reason = (await second.ProvisionAsync(new TestResourceContext("fixtura_00000001"))).UnavailableReason;

        Assert.NotNull(reason);
        Assert.Equal(connectionsAfterFirst, server.Connections);
        Assert.True(connectionsAfterFirst > 0);
    }

    [Fact]
    public async Task ProvisionAsync_ShouldSayNoServerIsConfigured_WhenACustomSourceFindsNone()
    {
        await using var sql = new SqlServerResource(new SqlServerOptions
        {
            Source = SqlServerSource.From("the test's source", _ => Task.FromResult<string?>(null))
        });

        string? reason = (await sql.ProvisionAsync(new TestResourceContext("fixtura_00000000"))).UnavailableReason;

        Assert.Equal("No SQL Server is configured in the test's source.", reason);
    }

    [Fact]
    public async Task ProvisionAsync_ShouldPassItsTokenToTheSource_WhenGivenOne()
    {
        using var cancel = new CancellationTokenSource();
        CancellationToken seen = default;
        await using var sql = new SqlServerResource(new SqlServerOptions
        {
            Source = SqlServerSource.From("the test's source", token =>
            {
                seen = token;
                return Task.FromResult<string?>(null);
            })
        });

        await sql.ProvisionAsync(new TestResourceContext("fixtura_00000000", cancel.Token));

        Assert.Equal(cancel.Token, seen);
    }

    [Fact]
    public async Task ProvisionAsync_ShouldThrow_WhenCancelledWhileTheSourceResolves()
    {
        using var cancel = new CancellationTokenSource();
        await using var sql = new SqlServerResource(new SqlServerOptions
        {
            Source = SqlServerSource.From("the test's source", async token =>
            {
                await cancel.CancelAsync();
                token.ThrowIfCancellationRequested();
                return null;
            })
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            sql.ProvisionAsync(new TestResourceContext("fixtura_00000000", cancel.Token)));
    }

    [Fact]
    public async Task ProvisionAsync_ShouldReturnTheSourcesError_WhenACustomSourceThrows()
    {
        await using var sql = new SqlServerResource(new SqlServerOptions
        {
            Source = SqlServerSource.From("the test's source", _ => throw new InvalidOperationException("Docker isn't running."))
        });

        string? reason = (await sql.ProvisionAsync(new TestResourceContext("fixtura_00000000"))).UnavailableReason;

        Assert.Equal("Couldn't get a SQL Server from the test's source: Docker isn't running.", reason);
    }

    [Fact]
    public async Task ProvisionAsync_ShouldThrow_WhenACustomSourceThrowsAndTheServerIsRequired()
    {
        string required = $"FIXTURA_TEST_REQUIRED_{Guid.NewGuid():N}";
        Environment.SetEnvironmentVariable(required, "true");
        await using var sql = new SqlServerResource(new SqlServerOptions
        {
            Source = SqlServerSource.From("the test's source", _ => throw new InvalidOperationException("Docker isn't running.")),
            RequiredWhenSet = required
        });

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            sql.ProvisionAsync(new TestResourceContext("fixtura_00000000")));

        Assert.StartsWith("Couldn't get a SQL Server from the test's source: Docker isn't running.", error.Message);
    }

    [Fact]
    public async Task ProvisionAsync_ShouldRejectTheReadyTimeout_WhenItIsNotPositive()
    {
        await using SqlServerResource sql = UnreachableServer(required: null, TimeSpan.Zero);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            sql.ProvisionAsync(new TestResourceContext("fixtura_00000000")));
    }

    // Dispose drops every database that starts with the prefix, so any other prefix could drop unrelated databases.
    [Theory]
    [InlineData("Sales")]
    [InlineData("")]
    [InlineData("fixtura_0000000")]
    public async Task ProvisionAsync_ShouldThrow_WhenGivenAPrefixTheFixtureDidNotMake(string prefix)
    {
        await using SqlServerResource sql = UnreachableServer(required: null);

        var error = await Assert.ThrowsAsync<ArgumentException>(() =>
            sql.ProvisionAsync(new TestResourceContext(prefix)));

        Assert.Equal("context", error.ParamName);
    }

    // CI leaves a secret blank in a fork's pull request; .NET deletes a variable set to "", so this sets a space.
    [Fact]
    public async Task EnvironmentVariableOrLocalDb_ShouldUseLocalDb_WhenTheVariableIsBlank()
    {
        string name = $"FIXTURA_TEST_SERVER_{Guid.NewGuid():N}";
        Environment.SetEnvironmentVariable(name, " ");

        string? server = await SqlServerSource.EnvironmentVariableOrLocalDb(name)
            .ResolveAsync(TestContext.Current.CancellationToken);

        Assert.Equal(await SqlServerSource.LocalDb.ResolveAsync(TestContext.Current.CancellationToken), server);
    }

    // A unique application name per test, because the resource checks each connection string once per process.
    private static SqlServerResource UnreachableServer(string? required, TimeSpan? readyTimeout = null) =>
        Resource($"{Unreachable};Application Name={Guid.NewGuid():N}", required, readyTimeout ?? TimeSpan.FromSeconds(1));

    // Unique variable names per resource, because the process's environment is shared by tests running in parallel.
    private static SqlServerResource Resource(string server, string? required, TimeSpan readyTimeout)
    {
        string id = Guid.NewGuid().ToString("N");
        Environment.SetEnvironmentVariable($"FIXTURA_TEST_SERVER_{id}", server);
        Environment.SetEnvironmentVariable($"FIXTURA_TEST_REQUIRED_{id}", required);
        return new SqlServerResource(new SqlServerOptions
        {
            Source = SqlServerSource.EnvironmentVariableOrLocalDb($"FIXTURA_TEST_SERVER_{id}"),
            RequiredWhenSet = $"FIXTURA_TEST_REQUIRED_{id}",
            ReadyTimeout = readyTimeout
        });
    }

    // Accepts connections and never answers, like a server that is up but stuck, so a check waits out its whole timeout.
    private sealed class SilentServer : IDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly List<TcpClient> _clients = [];

        public SilentServer()
        {
            _listener.Start();
            _ = AcceptAsync();
        }

        // No pooling: SqlClient's pool briefly fails repeat attempts on its own, which would hide a second check.
        public string ConnectionString => $"Server=tcp:127.0.0.1,{((IPEndPoint)_listener.LocalEndpoint).Port};"
            + "Integrated Security=true;TrustServerCertificate=true;Pooling=false";

        public int Connections
        {
            get
            {
                lock (_clients)
                {
                    return _clients.Count;
                }
            }
        }

        public void Dispose()
        {
            _listener.Stop();
            lock (_clients)
            {
                _clients.ForEach(client => client.Dispose());
            }
        }

        private async Task AcceptAsync()
        {
            try
            {
                while (true)
                {
                    TcpClient client = await _listener.AcceptTcpClientAsync();
                    lock (_clients)
                    {
                        _clients.Add(client);
                    }
                }
            }
            catch (Exception exception) when (exception is SocketException or ObjectDisposedException)
            {
            }
        }
    }
}
