using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;

namespace Fixtura;

/// <summary>
/// A SQL Server for one fixture. Each database it names gets the fixture's prefix, and dispose drops every database with
/// that prefix, so tests never share data and leave nothing behind.
/// </summary>
public sealed class SqlServerResource(SqlServerOptions options) : ITestResource
{
    // Databases an earlier run left behind (a killed test process) are dropped once they are this old and nobody is
    // connected to them, so a long run on a shared server, or one paused in a debugger, keeps its databases.
    private static readonly TimeSpan LeftoverAge = TimeSpan.FromDays(1);

    // One sweep per server and name pattern per process, so parallel fixtures never race to drop the same leftover. Lazy,
    // because GetOrAdd may run its factory more than once.
    private static readonly ConcurrentDictionary<(string Server, string Pattern), Lazy<Task>> Sweeps = new();

    // One reachability check per server per process, so an unreachable server costs the ready timeout once, not per fixture.
    private static readonly ConcurrentDictionary<string, Lazy<Task<string?>>> Checks = new();

    private string? _server;
    private string _prefix = "";

    /// <summary>
    /// The connection string for this fixture's database by that name. Call it inside a swap's lambda: the server is only
    /// known once the fixture has provisioned it.
    /// </summary>
    public string Database(string name)
    {
        string server = _server ?? throw new InvalidOperationException("SQL Server is not provisioned yet. Call Database(...) "
            + "inside a lambda that runs when the host is built, such as the one given to ReplaceDbContext or "
            + "Settings(() => ...), not in Configure itself.");
        return new SqlConnectionStringBuilder(server) { InitialCatalog = $"{_prefix}_{name}" }.ConnectionString;
    }

    /// <summary>
    /// Takes the prefix the fixture passes, such as <c>fixtura_1a2b3c4d</c>, and throws for any other: dispose drops every
    /// database whose name starts with it.
    /// </summary>
    public async Task<TestResourceStatus> ProvisionAsync(TestResourceContext context)
    {
        string namePrefix = context.NamePrefix;
        if (!Regex.IsMatch(namePrefix, "^fixtura_[0-9a-f]{8}$"))
        {
            throw new ArgumentException($"'{namePrefix}' is not a fixture's name prefix, such as fixtura_1a2b3c4d. Dispose drops " +
                "every database that starts with the prefix, so only the one the fixture passes is accepted.", nameof(context));
        }

        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(options.ReadyTimeout, TimeSpan.Zero, nameof(SqlServerOptions.ReadyTimeout));
        _prefix = namePrefix;
        (string? server, string? reason) = await ResolveAsync(context.CancellationToken);
        if (reason is null && await CheckOnceAsync(server!).WaitAsync(context.CancellationToken) is { } unreachable)
        {
            reason = $"{unreachable} The server came from {options.Source}.";
        }

        if (server is not null && reason is null)
        {
            _server = server;
            string pattern = LeftoverPattern(namePrefix);
            await Sweeps.GetOrAdd((server, pattern), _ => new Lazy<Task>(() => SweepLeftoversAsync(server, pattern, LeftoverAge))).Value.WaitAsync(context.CancellationToken);
            return TestResourceStatus.Ready;
        }

        if (IsRequired())
        {
            throw new InvalidOperationException($"{reason} {options.RequiredWhenSet} is set, so SQL Server is required.");
        }

        return TestResourceStatus.Unavailable(reason!);
    }

    /// <summary>
    /// Empties every table in this fixture's databases, except EF Core's migrations history and
    /// <see cref="SqlServerOptions.KeepOnReset"/>, and restarts identity columns and the sequences their defaults draw
    /// from, so the next test starts from the schema the app created. Throws when a kept name matches no table in any of
    /// them.
    /// </summary>
    public async Task ResetAsync(CancellationToken cancellationToken)
    {
        if (_server is null)
        {
            return;
        }

        List<string> databases = await FixtureDatabasesAsync(_server, cancellationToken);
        var found = new HashSet<string>();
        foreach (string database in databases)
        {
            string connection = new SqlConnectionStringBuilder(_server) { InitialCatalog = database }.ConnectionString;
            found.UnionWith(await SqlServerReset.ResetAsync(connection, options.KeepOnReset, cancellationToken));
        }

        // Until the app creates its databases there is nothing a kept name could match.
        List<string> missing = options.KeepOnReset.Where(name => !found.Contains(name)).ToList();
        if (databases.Count > 0 && missing.Count > 0)
        {
            throw new InvalidOperationException($"KeepOnReset names {string.Join(", ", missing)}, which match no table " +
                "in the fixture's databases. Write each as \"Table\", \"schema.Table\" or \"[schema].[Table]\".");
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_server is null)
        {
            return;
        }

        SqlConnection.ClearAllPools();
        await DropDatabasesAsync(_server, Like(_prefix) + "[_]%", olderThan: TimeSpan.Zero, keepInUse: false);
    }

    private bool IsRequired()
    {
        string? value = options.RequiredWhenSet is null ? null : Environment.GetEnvironmentVariable(options.RequiredWhenSet);
        return value is not null && (value == "1" || value.Equals("true", StringComparison.OrdinalIgnoreCase));
    }

    // A source that throws, such as a container that can't start, is unavailable like one that finds no server.
    private async Task<(string? Server, string? Reason)> ResolveAsync(CancellationToken cancellationToken)
    {
        try
        {
            string? server = await options.Source.ResolveAsync(cancellationToken);
            return server is null ? (null, $"No SQL Server is configured in {options.Source}.") : (server, null);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            return (null, $"Couldn't get a SQL Server from {options.Source}: {exception.Message}");
        }
    }

    private Task<string?> CheckOnceAsync(string server)
    {
        // SqlClient counts whole seconds and reads 0 as no limit, so round up.
        var builder = new SqlConnectionStringBuilder(server) { ConnectTimeout = (int)Math.Ceiling(options.ReadyTimeout.TotalSeconds) };
        return Checks.GetOrAdd(builder.ConnectionString, _ => new Lazy<Task<string?>>(() => CheckAsync(builder))).Value;
    }

    private static async Task<string?> CheckAsync(SqlConnectionStringBuilder builder)
    {
        try
        {
            await using var connection = new SqlConnection(builder.ConnectionString);
            await connection.OpenAsync();
            return null;
        }
        catch (Exception exception) when (exception is SqlException or PlatformNotSupportedException)
        {
            return $"SQL Server not reachable at {builder.DataSource}: {exception.Message}";
        }
    }

    // Housekeeping only: whatever goes wrong, such as a leftover another run is dropping or one this login can't drop, must
    // not fail anyone's tests.
    internal static async Task SweepLeftoversAsync(string server, string pattern, TimeSpan olderThan)
    {
        try
        {
            await DropDatabasesAsync(server, pattern, olderThan, keepInUse: true);
        }
        catch (Exception)
        {
        }
    }

    // The kit's own name shape: the prefix's stem, then as many hex digits as the prefix has, so other databases are left alone.
    internal static string LeftoverPattern(string namePrefix)
    {
        int stem = namePrefix.LastIndexOf('_') + 1;
        return Like(namePrefix[..stem]) + string.Concat(Enumerable.Repeat("[0-9a-f]", namePrefix.Length - stem)) + "[_]%";
    }

    private async Task<List<string>> FixtureDatabasesAsync(string server, CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(new SqlConnectionStringBuilder(server) { InitialCatalog = "master" }.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand("SELECT name FROM sys.databases WHERE name LIKE @pattern AND state = 0", connection);
        command.Parameters.AddWithValue("@pattern", Like(_prefix) + "[_]%");
        var databases = new List<string>();
        await using SqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            databases.Add(reader.GetString(0));
        }

        return databases;
    }

    private static string Like(string text) => text.Replace("[", "[[]").Replace("_", "[_]").Replace("%", "[%]");

    // Logins without VIEW SERVER STATE see only their current session, so for them the age is the only guard.
    private static async Task DropDatabasesAsync(string server, string namePattern, TimeSpan olderThan, bool keepInUse)
    {
        const string drop = """
            DECLARE @sql nvarchar(max) = N'';
            SELECT @sql += N'IF DB_ID(' + QUOTENAME(d.name, '''') + N') IS NOT NULL BEGIN '
                + N'ALTER DATABASE ' + QUOTENAME(d.name) + N' SET SINGLE_USER WITH ROLLBACK IMMEDIATE; '
                + N'DROP DATABASE ' + QUOTENAME(d.name) + N'; END; '
            FROM sys.databases d
            WHERE d.name LIKE @pattern AND d.create_date <= DATEADD(second, -@ageSeconds, GETDATE())
                AND (@keepInUse = 0 OR NOT EXISTS (SELECT 1 FROM sys.dm_exec_sessions s WHERE s.database_id = d.database_id));
            EXEC (@sql);
            """;
        await using var connection = new SqlConnection(new SqlConnectionStringBuilder(server) { InitialCatalog = "master" }.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(drop, connection);
        command.Parameters.AddWithValue("@pattern", namePattern);
        command.Parameters.AddWithValue("@ageSeconds", (int)olderThan.TotalSeconds);
        command.Parameters.AddWithValue("@keepInUse", keepInUse);
        await command.ExecuteNonQueryAsync();
    }
}
