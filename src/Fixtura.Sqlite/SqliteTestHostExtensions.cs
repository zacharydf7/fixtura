namespace Fixtura;

/// <summary>Adds SQLite in-memory databases to a fixture.</summary>
public static class SqliteTestHostExtensions
{
    /// <summary>
    /// Adds in-memory SQLite databases that live as long as the fixture and are emptied by <c>ResetDataAsync</c>. Point a
    /// context at one with <c>db.UseSqlite(sqlite.Database("Orders"))</c>.
    /// </summary>
    public static SqliteResource UseSqliteInMemory(this TestHostBuilder host) => host.AddResource(new SqliteResource());
}
