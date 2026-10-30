using Fixtura.Tests.Integration.Data.Notes;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Fixtura.Tests.Integration.Sqlite;

public sealed class SqliteNotesFixture : TestHost<Program>
{
    public SqliteResource Sqlite { get; private set; } = null!;

    protected override void Configure(TestHostBuilder host)
    {
        SqliteResource sqlite = Sqlite = host.AddSqliteInMemory();
        host.Services.Configure(services =>
            services.AddDbContext<NotesDb>(db => db.UseSqlite(sqlite.ConnectionString("Notes"))));
    }
}

public class SqliteTests(SqliteNotesFixture api) : IClassFixture<SqliteNotesFixture>, IAsyncLifetime
{
    public async ValueTask InitializeAsync()
    {
        using IServiceScope scope = api.CreateScope();
        await scope.ServiceProvider.GetRequiredService<NotesDb>().Database.EnsureCreatedAsync();
        await api.ResetDataAsync();
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    // Each scope's context opens and closes its own connection, which alone would let the in-memory database vanish.
    [Fact]
    public async Task Database_ShouldKeepTheData_WhenTheAppClosesItsConnections()
    {
        await AddNoteAsync("first");

        List<string> texts = await NoteTextsAsync();

        Assert.Equal(["first"], texts);
    }

    [Fact]
    public async Task ResetDataAsync_ShouldEmptyTheTablesAndKeepTheSchema_WhenTheTestAddedRows()
    {
        await AddNoteAsync("first");

        await api.ResetDataAsync();

        Assert.Empty(await NoteTextsAsync());
    }

    [Fact]
    public async Task ResetDataAsync_ShouldKeepTheMigrationsHistory_WhenItEmptiesTheRest()
    {
        await ExecuteAsync("""
            CREATE TABLE IF NOT EXISTS "__EFMigrationsHistory" ("MigrationId" TEXT PRIMARY KEY, "ProductVersion" TEXT NOT NULL);
            INSERT OR IGNORE INTO "__EFMigrationsHistory" VALUES ('20260101000000_Initial', '10.0.0');
            """);

        await api.ResetDataAsync();

        Assert.Equal(1L, await ScalarAsync("SELECT COUNT(*) FROM \"__EFMigrationsHistory\""));
    }

    [Fact]
    public async Task ResetDataAsync_ShouldRestartAutoincrementIds_WhenRowsWereAdded()
    {
        await ExecuteAsync("CREATE TABLE IF NOT EXISTS Tickets (Id INTEGER PRIMARY KEY AUTOINCREMENT, Note TEXT); INSERT INTO Tickets (Note) VALUES ('a');");

        await api.ResetDataAsync();

        Assert.Equal(1L, await ScalarAsync("INSERT INTO Tickets (Note) VALUES ('b'); SELECT last_insert_rowid();"));
    }

    [Fact]
    public async Task ResetDataAsync_ShouldEmptyBothTables_WhenAChildReferencesAParent()
    {
        await ExecuteAsync("""
            PRAGMA foreign_keys = ON;
            CREATE TABLE IF NOT EXISTS Parents (Id INTEGER PRIMARY KEY);
            CREATE TABLE IF NOT EXISTS Children (Id INTEGER PRIMARY KEY, ParentId INTEGER NOT NULL REFERENCES Parents (Id));
            INSERT INTO Parents VALUES (1);
            INSERT INTO Children VALUES (1, 1);
            """);

        await api.ResetDataAsync();

        Assert.Equal(0L, await ScalarAsync("SELECT (SELECT COUNT(*) FROM Parents) + (SELECT COUNT(*) FROM Children)"));
    }

    // The audit trigger would refill its table while the reset empties it, so either running shows.
    [Fact]
    public async Task ResetDataAsync_ShouldNotRunTheAppsTriggers_WhenItEmptiesTables()
    {
        await ExecuteAsync("""
            CREATE TABLE IF NOT EXISTS Orders (Id INTEGER PRIMARY KEY);
            CREATE TABLE IF NOT EXISTS AuditLog (Entry TEXT);
            CREATE TRIGGER IF NOT EXISTS AuditOrders AFTER DELETE ON Orders BEGIN INSERT INTO AuditLog VALUES ('deleted'); END;
            INSERT INTO Orders VALUES (1);
            """);

        await api.ResetDataAsync();

        long rows = (long)(await ScalarAsync("SELECT COUNT(*) FROM AuditLog"))!;
        long triggers = (long)(await ScalarAsync("SELECT COUNT(*) FROM sqlite_master WHERE type = 'trigger' AND name = 'AuditOrders'"))!;
        Assert.Equal((0L, 1L), (rows, triggers));
    }

    [Fact]
    public async Task ResetDataAsync_ShouldKeepAFullTextIndexWorking_WhenItEmptiesItsTable()
    {
        await ExecuteAsync("CREATE VIRTUAL TABLE IF NOT EXISTS Docs USING fts5(Body); INSERT INTO Docs VALUES ('harbor maps');");

        await api.ResetDataAsync();

        await ExecuteAsync("INSERT INTO Docs VALUES ('quiet harbor');");
        Assert.Equal(1L, await ScalarAsync("SELECT COUNT(*) FROM Docs WHERE Docs MATCH 'harbor'"));
    }

    private async Task AddNoteAsync(string text)
    {
        using IServiceScope scope = api.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NotesDb>();
        db.Notes.Add(new Note { Text = text });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private async Task<List<string>> NoteTextsAsync()
    {
        using IServiceScope scope = api.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<NotesDb>().Notes
            .Select(note => note.Text).ToListAsync(TestContext.Current.CancellationToken);
    }

    private async Task ExecuteAsync(string sql)
    {
        await using var connection = new SqliteConnection(api.Sqlite.ConnectionString("Notes"));
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = new SqliteCommand(sql, connection);
        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }

    private async Task<object?> ScalarAsync(string sql)
    {
        await using var connection = new SqliteConnection(api.Sqlite.ConnectionString("Notes"));
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = new SqliteCommand(sql, connection);
        return await command.ExecuteScalarAsync(TestContext.Current.CancellationToken);
    }
}

public class SqliteDisposalTests
{
    [Fact]
    public async Task DisposeAsync_ShouldRemoveTheDatabase_WhenTheFixtureIsDone()
    {
        var api = new SqliteNotesFixture();
        await api.InitializeAsync();
        string database = api.Sqlite.ConnectionString("Notes");
        using (IServiceScope scope = api.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<NotesDb>().Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
        }

        await api.DisposeAsync();

        await using var connection = new SqliteConnection(database);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = new SqliteCommand("SELECT COUNT(*) FROM sqlite_master WHERE type = 'table'", connection);
        Assert.Equal(0L, await command.ExecuteScalarAsync(TestContext.Current.CancellationToken));
    }
}
