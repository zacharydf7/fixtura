using Microsoft.Data.Sqlite;

namespace Fixtura;

/// <summary>
/// SQLite in-memory databases for one fixture. Each name gets a database of its own, which a connection the resource
/// holds open keeps alive until the fixture is done, so it doesn't vanish when the app closes its own connections.
/// </summary>
public sealed class SqliteResource : ITestResource
{
    private readonly Dictionary<string, SqliteConnection> _keptOpen = [];
    private string? _prefix;

    /// <summary>
    /// The connection string for this fixture's in-memory database by that name. Call it inside a swap's lambda, such as
    /// <c>db.UseSqlite(sqlite.Database("Orders"))</c>: the fixture's names are only known once it has been provisioned.
    /// </summary>
    public string Database(string name)
    {
        string prefix = _prefix ?? throw new InvalidOperationException("The SQLite resource is not provisioned yet. Call "
            + "Database(...) inside a lambda that runs when the host is built, such as the one given to ReplaceDbContext or "
            + "Settings(() => ...), not in Configure itself.");
        string connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = $"{prefix}_{name}",
            Mode = SqliteOpenMode.Memory,
            Cache = SqliteCacheMode.Shared
        }.ToString();
        lock (_keptOpen)
        {
            if (!_keptOpen.ContainsKey(name))
            {
                var connection = new SqliteConnection(connectionString);
                connection.Open();
                _keptOpen[name] = connection;
            }
        }

        return connectionString;
    }

    public Task<TestResourceStatus> ProvisionAsync(TestResourceContext context)
    {
        _prefix = context.NamePrefix;
        return Task.FromResult(TestResourceStatus.Ready);
    }

    /// <summary>
    /// Empties every table in this fixture's databases except EF Core's migrations history, so the next test starts from
    /// the schema the app created.
    /// </summary>
    public async Task ResetAsync(CancellationToken cancellationToken)
    {
        foreach (SqliteConnection connection in KeptOpen())
        {
            await SqliteReset.ResetAsync(connection, cancellationToken);
        }
    }

    // Closing the last connection to an in-memory database removes it.
    public async ValueTask DisposeAsync()
    {
        foreach (SqliteConnection connection in KeptOpen())
        {
            await connection.DisposeAsync();
        }
    }

    private List<SqliteConnection> KeptOpen()
    {
        lock (_keptOpen)
        {
            return [.. _keptOpen.Values];
        }
    }
}
