using System.Collections.Concurrent;
using Microsoft.Data.SqlClient;

namespace Fixtura.Spike.SqlServer;

// Prefixing every database per fixture lets dispose drop exactly what this fixture created.
public sealed class SqlServerResource(SqlServerOptions options) : ITestResource
{
    // Databases an earlier run left behind (a killed test process) are dropped once they are this old.
    private static readonly TimeSpan LeftoverAge = TimeSpan.FromHours(1);

    // One sweep per server per process, so parallel fixtures never race to drop the same leftover.
    private static readonly ConcurrentDictionary<string, Task> Sweeps = new();

    private string? _server;
    private string _prefix = "";

    // Call inside the swap's lambda: the server is only known once the fixture has provisioned it.
    public string Database(string name)
    {
        string server = _server ?? throw new InvalidOperationException("SQL Server is not provisioned yet.");
        return new SqlConnectionStringBuilder(server) { InitialCatalog = $"{_prefix}_{name}" }.ConnectionString;
    }

    public async Task<string?> ProvisionAsync(string namePrefix)
    {
        _prefix = namePrefix;
        string? server = options.Source.Resolve();
        string? reason = server is null ? $"No SQL Server is configured in {options.Source}." : await CheckAsync(server);
        if (server is not null && reason is null)
        {
            _server = server;
            await Sweeps.GetOrAdd(server, _ => DropDatabasesAsync(server, "fixtura[_]%", olderThan: LeftoverAge));
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
        await DropDatabasesAsync(_server, _prefix.Replace("_", "[_]") + "[_]%", olderThan: TimeSpan.Zero);
    }

    private bool IsRequired()
    {
        string? value = options.RequiredWhenSet is null ? null : Environment.GetEnvironmentVariable(options.RequiredWhenSet);
        return value is not null && (value == "1" || value.Equals("true", StringComparison.OrdinalIgnoreCase));
    }

    private async Task<string?> CheckAsync(string server)
    {
        var builder = new SqlConnectionStringBuilder(server) { ConnectTimeout = (int)options.ReadyTimeout.TotalSeconds };
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
