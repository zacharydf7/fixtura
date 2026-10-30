using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using Npgsql;

namespace Fixtura;

/// <summary>
/// A PostgreSQL server for one fixture. Each database it names gets the fixture's prefix, and dispose drops every database
/// with that prefix, so tests never share data and leave nothing behind.
/// </summary>
public sealed class PostgreSqlResource(PostgreSqlOptions options) : ITestResource
{
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
        string server = _server ?? throw new InvalidOperationException("PostgreSQL is not provisioned yet. Call Database(...) "
            + "inside a lambda that runs when the host is built, such as the one given to ReplaceDbContext or "
            + "Settings(() => ...), not in Configure itself.");
        return new NpgsqlConnectionStringBuilder(server) { Database = $"{_prefix}_{name}" }.ConnectionString;
    }

    /// <summary>
    /// Takes the prefix the fixture passes, such as <c>fixtura_1a2b3c4d</c>, and throws for any other: dispose drops every
    /// database whose name starts with it.
    /// </summary>
    public async Task<string?> ProvisionAsync(string namePrefix)
    {
        if (!Regex.IsMatch(namePrefix, "^fixtura_[0-9a-f]{8}$"))
        {
            throw new ArgumentException($"'{namePrefix}' is not a fixture's name prefix, such as fixtura_1a2b3c4d. Dispose drops " +
                "every database that starts with the prefix, so only the one the fixture passes is accepted.", nameof(namePrefix));
        }

        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(options.ReadyTimeout, TimeSpan.Zero, nameof(PostgreSqlOptions.ReadyTimeout));
        _prefix = namePrefix;
        (string? server, string? reason) = await ResolveAsync();
        if (reason is null && await CheckOnceAsync(server!) is { } unreachable)
        {
            reason = $"{unreachable} The server came from {options.Source}.";
        }

        if (server is not null && reason is null)
        {
            _server = server;
            return null;
        }

        if (IsRequired())
        {
            throw new InvalidOperationException($"{reason} {options.RequiredWhenSet} is set, so PostgreSQL is required.");
        }

        return reason;
    }

    /// <summary>
    /// Empties every table in this fixture's databases, except EF Core's migrations history and
    /// <see cref="PostgreSqlOptions.KeepOnReset"/>, and restarts identity columns and the sequences only those tables'
    /// defaults draw from, so the next test starts from the schema the app created. Throws when a kept name matches no table in any of them.
    /// </summary>
    public async Task ResetAsync()
    {
        if (_server is null)
        {
            return;
        }

        List<string> databases = await FixtureDatabasesAsync(_server);
        var found = new HashSet<string>();
        foreach (string database in databases)
        {
            string connection = new NpgsqlConnectionStringBuilder(_server) { Database = database }.ConnectionString;
            found.UnionWith(await PostgreSqlReset.ResetAsync(connection, options.KeepOnReset));
        }

        // Until the app creates its databases there is nothing a kept name could match.
        List<string> missing = options.KeepOnReset.Where(name => !found.Contains(name)).ToList();
        if (databases.Count > 0 && missing.Count > 0)
        {
            throw new InvalidOperationException($"KeepOnReset names {string.Join(", ", missing)}, which match no table " +
                "in the fixture's databases. Write each as Table, schema.Table or \"schema\".\"Table\".");
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_server is null)
        {
            return;
        }

        NpgsqlConnection.ClearAllPools();
        await using var connection = new NpgsqlConnection(Maintenance(_server));
        await connection.OpenAsync();

        // DROP DATABASE can't share a command with other statements, so each database gets its own.
        foreach (string database in await FixtureDatabasesAsync(_server))
        {
            await using var drop = new NpgsqlCommand($"DROP DATABASE IF EXISTS {Quote(database)} WITH (FORCE)", connection);
            await drop.ExecuteNonQueryAsync();
        }
    }

    private bool IsRequired()
    {
        string? value = options.RequiredWhenSet is null ? null : Environment.GetEnvironmentVariable(options.RequiredWhenSet);
        return value is not null && (value == "1" || value.Equals("true", StringComparison.OrdinalIgnoreCase));
    }

    // A source that throws, such as a container that can't start, is unavailable like one that finds no server.
    private async Task<(string? Server, string? Reason)> ResolveAsync()
    {
        try
        {
            string? server = await options.Source.ResolveAsync();
            return server is null ? (null, $"No PostgreSQL server is configured in {options.Source}.") : (server, null);
        }
        catch (Exception exception)
        {
            return (null, $"Couldn't get a PostgreSQL server from {options.Source}: {exception.Message}");
        }
    }

    private Task<string?> CheckOnceAsync(string server)
    {
        // Npgsql counts whole seconds and reads 0 as no limit, so round up.
        var builder = new NpgsqlConnectionStringBuilder(Maintenance(server))
        {
            Timeout = (int)Math.Ceiling(options.ReadyTimeout.TotalSeconds)
        };
        return Checks.GetOrAdd(builder.ConnectionString, _ => new Lazy<Task<string?>>(() => CheckAsync(builder))).Value;
    }

    private static async Task<string?> CheckAsync(NpgsqlConnectionStringBuilder builder)
    {
        try
        {
            await using var connection = new NpgsqlConnection(builder.ConnectionString);
            await connection.OpenAsync();
            return null;
        }
        catch (Exception exception) when (exception is NpgsqlException or TimeoutException)
        {
            return $"PostgreSQL not reachable at {builder.Host}:{builder.Port}: {exception.Message}";
        }
    }

    private async Task<List<string>> FixtureDatabasesAsync(string server)
    {
        await using var connection = new NpgsqlConnection(Maintenance(server));
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT datname FROM pg_database WHERE datname LIKE @pattern AND datallowconn", connection);
        command.Parameters.AddWithValue("pattern", Like(_prefix) + "\\_%");
        var databases = new List<string>();
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            databases.Add(reader.GetString(0));
        }

        return databases;
    }

    // The server's maintenance database, for listing and dropping the fixture's own.
    private static string Maintenance(string server) =>
        new NpgsqlConnectionStringBuilder(server) { Database = "postgres" }.ConnectionString;

    private static string Like(string text) => text.Replace("\\", "\\\\").Replace("_", "\\_").Replace("%", "\\%");

    private static string Quote(string name) => $"\"{name.Replace("\"", "\"\"")}\"";
}
