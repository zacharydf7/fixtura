using System.Collections.Concurrent;
using Microsoft.Data.SqlClient;

namespace Fixtura;

/// <summary>
/// A SQL Server for one fixture. Each database it names gets the fixture's prefix, and dispose drops every database with
/// that prefix, so tests never share data and leave nothing behind.
/// </summary>
public sealed class SqlServerResource(SqlServerOptions options) : ITestResource
{
    // Databases an earlier run left behind (a killed test process) are dropped once they are this old.
    private static readonly TimeSpan LeftoverAge = TimeSpan.FromHours(1);

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
        string server = _server ?? throw new InvalidOperationException("SQL Server is not provisioned yet.");
        return new SqlConnectionStringBuilder(server) { InitialCatalog = $"{_prefix}_{name}" }.ConnectionString;
    }

    public async Task<string?> ProvisionAsync(string namePrefix)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(options.ReadyTimeout, TimeSpan.Zero, nameof(SqlServerOptions.ReadyTimeout));
        _prefix = namePrefix;
        string? server = options.Source.Resolve();
        string? reason = server is null ? $"No SQL Server is configured in {options.Source}." : await CheckOnceAsync(server);
        if (server is not null && reason is null)
        {
            _server = server;
            string pattern = LeftoverPattern(namePrefix);
            await Sweeps.GetOrAdd((server, pattern), _ => new Lazy<Task>(() => SweepLeftoversAsync(server, pattern, LeftoverAge))).Value;
            return null;
        }

        if (IsRequired())
        {
            throw new InvalidOperationException($"{reason} {options.RequiredWhenSet} is set, so SQL Server is required.");
        }

        return reason;
    }

    public async ValueTask DisposeAsync()
    {
        if (_server is null)
        {
            return;
        }

        SqlConnection.ClearAllPools();
        await DropDatabasesAsync(_server, Like(_prefix) + "[_]%", olderThan: TimeSpan.Zero);
    }

    private bool IsRequired()
    {
        string? value = options.RequiredWhenSet is null ? null : Environment.GetEnvironmentVariable(options.RequiredWhenSet);
        return value is not null && (value == "1" || value.Equals("true", StringComparison.OrdinalIgnoreCase));
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
            await DropDatabasesAsync(server, pattern, olderThan);
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

    private static string Like(string text) => text.Replace("[", "[[]").Replace("_", "[_]").Replace("%", "[%]");

    private static async Task DropDatabasesAsync(string server, string namePattern, TimeSpan olderThan)
    {
        const string drop = """
            DECLARE @sql nvarchar(max) = N'';
            SELECT @sql += N'IF DB_ID(' + QUOTENAME(name, '''') + N') IS NOT NULL BEGIN '
                + N'ALTER DATABASE ' + QUOTENAME(name) + N' SET SINGLE_USER WITH ROLLBACK IMMEDIATE; '
                + N'DROP DATABASE ' + QUOTENAME(name) + N'; END; '
            FROM sys.databases
            WHERE name LIKE @pattern AND create_date <= DATEADD(second, -@ageSeconds, GETDATE());
            EXEC (@sql);
            """;
        await using var connection = new SqlConnection(new SqlConnectionStringBuilder(server) { InitialCatalog = "master" }.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(drop, connection);
        command.Parameters.AddWithValue("@pattern", namePattern);
        command.Parameters.AddWithValue("@ageSeconds", (int)olderThan.TotalSeconds);
        await command.ExecuteNonQueryAsync();
    }
}
